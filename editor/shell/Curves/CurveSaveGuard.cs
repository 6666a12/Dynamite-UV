using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using DynamiteUniverse.ChartEditor.Core;

namespace DynamiteUniverse.Editor.Shell;

/// <summary>Package-wide UI policy for uncommitted formula drafts and the one-time 2.1 upgrade.
/// Does not apply an editor command, modify a chart, or write files.</summary>
internal sealed class CurveSaveGuard
{
    private readonly CustomCurveEditor[] _panels;
    private readonly IWorkbenchLocalizer _text;
    private readonly Action<string> _report;
    private readonly WorkbenchModalHost _modal;

    internal CurveSaveGuard(CustomCurveEditor path, CustomCurveEditor timing, Button discard,
        WorkbenchModalHost modal, IWorkbenchLocalizer text, Action<string> report)
    {
        _panels = [path, timing]; _modal = modal; _text = text; _report = report;
        foreach (var p in _panels) p.DraftsChanged += () => discard.IsVisible = HasDraft;
        discard.Click += (_, _) => { foreach (var p in _panels) p.DiscardAll(); };
    }

    internal bool HasDraft => _panels.Any(p => p.HasDraft);
    internal long Revision => _panels.Sum(p => p.Revision);
    internal bool BlockPending()
    {
        var location = _panels.Select(p => p.PendingLocation).FirstOrDefault(s => s is not null);
        if (location is null) return false;
        _report(_text.Format("Curve.Blocked", location)); return true;
    }

    internal async Task ReplaceDocument(Func<Task> replace)
    {
        // A successful async Open/New must not overtake a fresh formula typed after its file picker.
        foreach (var p in _panels) p.Locked = true;
        try { await replace(); }
        finally { foreach (var p in _panels) p.Locked = false; }
    }

    internal Task<bool> ConfirmVersion(EditorDocument doc)
    {
        if (doc.SourceFormatMinor >= 1 || doc.FormatMinor < 1) return Task.FromResult(true);
        var result = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _modal.Show((cancel, close) =>
        {
            var ok = new Button { Name = "ConfirmCurveUpgrade", Content = _text["Curve.UpgradeConfirm"], Classes = { "accent" } };
            var back = new Button { Name = "CancelCurveUpgrade", Content = _text["Create.Cancel"] };
            ok.Click += (_, _) => { result.TrySetResult(true); close(); };
            back.Click += (_, _) => close();
            cancel.Register(() => result.TrySetResult(false));
            return new StackPanel { Spacing = 14, Children = {
                new TextBlock { Text = _text["Curve.UpgradeTitle"], Classes = { "section" } },
                new TextBlock { Text = _text["Curve.Upgrade"], TextWrapping = TextWrapping.Wrap },
                new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8, Children = { back, ok } }
            } };
        });
        return result.Task;
    }
}
