using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;

namespace DynamiteUniverse.Editor.Shell;

/// <summary>Owns the workbench interaction barrier for either a sheet or an owned dialog,
/// not the form data, validation, or document command. Open/close on the UI thread.</summary>
internal sealed class WorkbenchModalHost(Window owner, Control background, Border layer, ContentControl slot,
    Func<IEnumerable<Window>> floatingWindows, Action<bool, long, bool> setStageBarrier)
{
    private readonly List<(Window Window, bool Enabled)> _blocked = [];
    private IInputElement? _previousFocus;
    private CancellationTokenSource? _lifetime;
    private Window? _dialog;
    private bool _backgroundEnabled;
    public bool IsOpen => _lifetime is not null;
    public bool PausePlayback { get; private set; }
    public long Epoch { get; private set; }

    public void Show(Func<CancellationToken, Action, Control> create)
    {
        var lifetime = Begin(pausePlayback: true);
        try
        {
            var form = create(lifetime.Token, () => End(lifetime));
            if (!ReferenceEquals(_lifetime, lifetime)) return;
            slot.Content = form;
            layer.IsVisible = true;
            var focus = form.GetVisualDescendants().OfType<Control>()
                .FirstOrDefault(c => c.Focusable && c.IsEffectivelyVisible && c.IsEffectivelyEnabled);
            if (focus?.Focus() != true) slot.Focus();
        }
        catch { End(lifetime); throw; }
    }

    public async Task ShowDialog(Window dialog)
    {
        var lifetime = Begin(pausePlayback: false);
        _dialog = dialog;
        try { await dialog.ShowDialog(owner); }
        finally { End(lifetime); }
    }

    private CancellationTokenSource Begin(bool pausePlayback)
    {
        if (IsOpen) throw new InvalidOperationException("A workbench form is already open.");
        var lifetime = new CancellationTokenSource();
        _lifetime = lifetime;
        PausePlayback = pausePlayback;
        _previousFocus = owner.FocusManager?.GetFocusedElement();
        _backgroundEnabled = background.IsEnabled;
        try
        {
            setStageBarrier(true, ++Epoch, pausePlayback);
            // Disable the content, not the owner: the sheet must keep keyboard and picker access.
            background.SetCurrentValue(InputElement.IsEnabledProperty, false);
            foreach (var window in floatingWindows().Where(w => w != owner).Distinct())
            {
                _blocked.Add((window, window.IsEnabled));
                window.Closing += BlockFloatingClose;
                window.SetCurrentValue(InputElement.IsEnabledProperty, false);
            }
            return lifetime;
        }
        catch { End(lifetime); throw; }
    }

    public void Close()
    {
        if (_dialog is { IsVisible: true } dialog)
        {
            dialog.Close(); // Do not release while the dialog is visible or cancels closure.
            return;
        }
        if (_lifetime is { } lifetime) End(lifetime);
    }

    private void End(CancellationTokenSource lifetime)
    {
        if (!ReferenceEquals(_lifetime, lifetime)) return;
        _lifetime = null;
        _dialog = null;
        PausePlayback = false;
        try { lifetime.Cancel(); }
        finally
        {
            slot.Content = null;
            layer.IsVisible = false;
            foreach (var (window, enabled) in _blocked)
            {
                window.Closing -= BlockFloatingClose;
                window.SetCurrentValue(InputElement.IsEnabledProperty, enabled);
            }
            _blocked.Clear();
            background.SetCurrentValue(InputElement.IsEnabledProperty, _backgroundEnabled);
            setStageBarrier(false, ++Epoch, false);
            if (owner.IsVisible && _previousFocus is Control { IsEffectivelyVisible: true, IsEffectivelyEnabled: true } previous)
                previous.Focus();
            _previousFocus = null;
            lifetime.Dispose();
        }
    }

    private static void BlockFloatingClose(object? sender, WindowClosingEventArgs e) => e.Cancel = true;

    // Lifecycle/clock/error messages must still arrive while a modal is visible. Authoring
    // messages posted before the modal boundary must not edit the newly selected chart later.
    public bool Blocks(StageMessage message) => message.Event is
        ("selection" or "place" or "pathAdd" or "pathMove" or "noteMove" or "noteResize" or "key" or "contextAction") &&
        (IsOpen || (message.Data.TryGetProperty("workbenchEpoch", out var epoch)
            ? epoch.ValueKind != System.Text.Json.JsonValueKind.Number || !epoch.TryGetInt64(out var value) || value != Epoch
            : Epoch != 0));
}
