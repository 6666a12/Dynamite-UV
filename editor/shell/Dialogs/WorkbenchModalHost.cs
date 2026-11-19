using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;

namespace DynamiteUniverse.Editor.Shell;

/// <summary>A single in-workbench sheet. Owns the interaction barrier and its lifetime,
/// not the form data, validation, or document command. Open/close on the UI thread.</summary>
internal sealed class WorkbenchModalHost(Window owner, Control background, Border layer, ContentControl slot,
    Func<IEnumerable<Window>> floatingWindows, Action<bool, long> setStageBarrier)
{
    private readonly List<(Window Window, bool Enabled)> _blocked = [];
    private IInputElement? _previousFocus;
    private CancellationTokenSource? _lifetime;
    public bool IsOpen => _lifetime is not null;
    public long Epoch { get; private set; }

    public void Show(Func<CancellationToken, Action, Control> create)
    {
        if (IsOpen) throw new InvalidOperationException("A workbench form is already open.");
        var lifetime = new CancellationTokenSource();
        _lifetime = lifetime;
        _previousFocus = owner.FocusManager?.GetFocusedElement();
        try
        {
            // Disable the content, not the owner: the sheet must keep keyboard and picker access.
            background.IsEnabled = false;
            foreach (var window in floatingWindows().Where(w => w != owner).Distinct())
            {
                _blocked.Add((window, window.IsEnabled));
                window.IsEnabled = false;
                window.Closing += BlockFloatingClose;
            }
            setStageBarrier(true, ++Epoch);
            var form = create(lifetime.Token, () => { if (ReferenceEquals(_lifetime, lifetime)) Close(); });
            if (!ReferenceEquals(_lifetime, lifetime)) return;
            slot.Content = form;
            layer.IsVisible = true;
            var focus = form.GetVisualDescendants().OfType<Control>()
                .FirstOrDefault(c => c.Focusable && c.IsEffectivelyVisible && c.IsEffectivelyEnabled);
            if (focus?.Focus() != true) slot.Focus();
        }
        catch { Close(); throw; }
    }

    public void Close()
    {
        if (_lifetime is not { } lifetime) return;
        _lifetime = null;
        try { lifetime.Cancel(); }
        finally
        {
            slot.Content = null;
            layer.IsVisible = false;
            foreach (var (window, enabled) in _blocked)
            {
                window.Closing -= BlockFloatingClose;
                window.IsEnabled = enabled;
            }
            _blocked.Clear();
            background.IsEnabled = true;
            setStageBarrier(false, ++Epoch);
            if (_previousFocus is Control { IsEffectivelyVisible: true, IsEffectivelyEnabled: true } previous)
                previous.Focus();
            _previousFocus = null;
            lifetime.Dispose();
        }
    }

    private static void BlockFloatingClose(object? sender, WindowClosingEventArgs e) => e.Cancel = true;

    // Lifecycle/clock/error messages must still arrive while a sheet is visible. Authoring
    // messages posted before the modal boundary must not edit the newly selected chart later.
    public bool Blocks(StageMessage message) => message.Event is
        ("selection" or "place" or "pathAdd" or "pathMove" or "noteMove" or "noteResize" or "key" or "contextAction") &&
        (IsOpen || (message.Data.TryGetProperty("workbenchEpoch", out var epoch)
            ? epoch.ValueKind != System.Text.Json.JsonValueKind.Number || !epoch.TryGetInt64(out var value) || value != Epoch
            : Epoch != 0));
}
