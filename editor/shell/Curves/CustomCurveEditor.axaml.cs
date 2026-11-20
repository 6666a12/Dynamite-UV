using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using DynamiteUniverse.ChartEditor.Core;
using DynamiteUniverse.Shared.Chart.V2;

namespace DynamiteUniverse.Editor.Shell;

/// <summary>One editor per panel, with unapplied formulas retained by chart and outgoing point.
/// A selection change, stage update, or document refresh is not consent to apply/discard a draft.</summary>
public sealed partial class CustomCurveEditor : UserControl
{
    private const string Custom = "Custom";
    private readonly record struct Target(EditorEntityKey Chart, EditorEntityKey Point);
    private sealed record Draft(string Choice, string Expr, string ChartId, string PointLabel, string? ApplyError = null);
    private readonly Dictionary<Target, Draft> _drafts = [];
    private readonly IWorkbenchLocalizer _text = WorkbenchLocalizer.Shared;
    private readonly TextBox _input;
    private readonly CurvePlot _plot;
    private readonly TextBlock _notice;
    private readonly Button _discard;
    private ComboBox? _choice;
    private EditorDocument? _document;
    private Target? _target;
    private string _pointLabel = "";
    private string _originalChoice = "", _originalExpr = "", _expr = "", _chosen = "";
    private string? _previewExpr, _error;
    private bool _binding;
    internal bool Locked { get; set; }
    internal event Action? DraftsChanged;
    internal bool HasDraft => _drafts.Count != 0;
    internal string? PendingLocation
    {
        get
        {
            foreach (var (target, draft) in _drafts)
            {
                // Resolve the current chart ID even when the draft is hidden during a rename.
                var chart = _document?.Charts.FirstOrDefault(c => c.Key == target.Chart);
                return $"{chart?.Id ?? draft.ChartId} / {draft.PointLabel}";
            }
            return null;
        }
    }
    internal long Revision { get; private set; }

    public CustomCurveEditor()
    {
        AvaloniaXamlLoader.Load(this);
        _input = this.FindControl<TextBox>("ExpressionInput")!;
        _plot = this.FindControl<CurvePlot>("PreviewPlot")!;
        _notice = this.FindControl<TextBlock>("FormulaNotice")!;
        _discard = this.FindControl<Button>("DiscardFormula")!;
        _input.PropertyChanged += (_, e) => { if (!_binding && e.Property == TextBox.TextProperty && _target.HasValue && _chosen == Custom) Change(_chosen, _input.Text ?? ""); };
        _discard.Click += (_, _) => { if (_target is { } t && _drafts.Remove(t)) { Notify(); Restore(); } };
        AttachedToVisualTree += (_, _) => { _text.LanguageChanged += RenderState; RenderState(); };
        DetachedFromVisualTree += (_, _) => _text.LanguageChanged -= RenderState;
        IsVisible = false;
    }

    internal void AttachChoice(ComboBox choice)
    {
        _choice = choice;
        choice.SelectionChanged += (_, _) =>
        {
            if (_binding || _target is null || choice.SelectedItem is not ComboBoxItem { Tag: string tag }) return;
            Change(tag, _expr);
        };
    }

    internal void ShowTarget(EditorDocument? doc, EditorEntityKey? key, string? point, string originalChoice, string? originalExpr)
    {
        if (!ReferenceEquals(doc, _document)) { _drafts.Clear(); _document = doc; }
        _target = doc is not null && key is { } id ? new(doc.SelectedChart.Key, id) : null;
        _pointLabel = point ?? "";
        _originalChoice = originalChoice; _originalExpr = originalExpr ?? "t";
        Restore();
    }

    private void Restore()
    {
        var draft = _target is { } t ? _drafts.GetValueOrDefault(t) : null;
        _chosen = draft?.Choice ?? _originalChoice;
        _expr = draft?.Expr ?? _originalExpr;
        _binding = true;
        try
        {
            if (_choice is not null) _choice.SelectedItem = _choice.Items.OfType<ComboBoxItem>().FirstOrDefault(i => Equals(i.Tag, _chosen));
            if (_input.Text != _expr) _input.Text = _expr;
        }
        finally { _binding = false; }
        RenderState();
    }

    private void Change(string choice, string expr)
    {
        if (Locked) { Restore(); return; }
        var target = _target!.Value;
        _chosen = choice; _expr = expr;
        if (_chosen == _originalChoice && _expr == _originalExpr) _drafts.Remove(target);
        else if (choice == Custom || _originalChoice == Custom || _drafts.ContainsKey(target)) _drafts[target] = new(choice, expr, _document!.SelectedChartId, _pointLabel);
        Notify(); RenderState();
    }

    private void RenderState()
    {
        var custom = _target.HasValue && _chosen == Custom;
        _discard.IsVisible = _target is { } t && _drafts.ContainsKey(t);
        this.FindControl<StackPanel>("FormulaFields")!.IsVisible = custom;
        IsVisible = custom || _discard.IsVisible;
        if (!custom) return;
        if (_previewExpr != _expr)
        {
            _previewExpr = _expr;
            // Reopening an existing formula uses its chart's verified table; new text is a draft.
            try { _plot.Show(_document!.SelectedChart.CurveCache.Resolve(_expr)); _error = null; }
            catch (FormatException ex) { _plot.Show(null); _error = ex.Message; }
        }
        var curve = _plot.Curve;
        var applyError = _target is { } target ? _drafts.GetValueOrDefault(target)?.ApplyError : null;
        _notice.Text = (_error ?? applyError) is { } message ? _text.Format("Curve.Invalid", message) :
            curve is not null && (curve.Minimum < 0 || curve.Maximum > 1) ? _text.Format("Curve.Overshoot", curve.Minimum, curve.Maximum) : "";
        _notice.IsVisible = !string.IsNullOrEmpty(_notice.Text);
    }

    // Add can copy the current inputs but does not own this target's draft or diagnostics.
    internal bool TryReadInput(out string? expression)
    {
        expression = _target.HasValue && _chosen == Custom ? _expr : null;
        if (expression is null || _error is null) return true;
        _input.Focus(); return false;
    }

    internal void Submit(Func<string?, string?> apply)
    {
        if (!TryReadInput(out var expression)) return;
        var target = _target;
        // The command can refresh/reselect the panel synchronously. Capture the submitting owner first.
        var submitted = expression is null ? null : new Draft(_chosen, expression, _document!.SelectedChartId, _pointLabel);
        if (apply(expression) is { } error)
        {
            if (submitted is not null && target is { } rejected) { _drafts[rejected] = submitted with { ApplyError = error }; Notify(); RenderState(); }
            return;
        }
        if (target is { } t && _drafts.Remove(t)) { Notify(); Restore(); }
    }

    internal void DiscardAll()
    {
        if (_drafts.Count == 0) return;
        _drafts.Clear(); Notify(); Restore();
    }

    private void Notify() { Revision++; DraftsChanged?.Invoke(); }
}
