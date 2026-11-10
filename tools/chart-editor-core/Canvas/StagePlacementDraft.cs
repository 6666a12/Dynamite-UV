using DynamiteUniverse.Shared.Chart.V2;

namespace DynamiteUniverse.ChartEditor.Core;

/// <summary>
/// One new-note placement on the native stage: width drag, optional Hold tail, then one document command.
/// Pure centre-coordinate model for the current stage: unlike <see cref="CanvasPlacementDraft"/>,
/// <see cref="Center"/> is the track centre, never the canvas left edge. The host supplies a snapped
/// head time and pointer centres; this type performs no second/text conversion and never touches the document.
/// </summary>
public sealed class StagePlacementDraft
{
    private const double MinimumWidth = .1d;

    private readonly double _anchorPointerCenter;
    private readonly double _baseWidth;

    public StagePlacementDraft(ExactBarTime time, double center, double width, double pointerCenter)
    {
        Time = time;
        Center = center;
        Width = width;
        TailTime = time;
        _baseWidth = width;
        _anchorPointerCenter = pointerCenter;
    }

    /// <summary>Snapped head time, fixed for the lifetime of the draft.</summary>
    public ExactBarTime Time { get; }

    /// <summary>Track-centre coordinate; overscan is preserved.</summary>
    public double Center { get; private set; }

    /// <summary>Interactive width with a 0.1 minimum; no stage-boundary maximum.</summary>
    public double Width { get; private set; }

    /// <summary>Candidate Hold tail time; meaningful once <see cref="TailStage"/> is true.</summary>
    public ExactBarTime TailTime { get; private set; }

    public bool TailStage { get; private set; }

    /// <summary>
    /// Signed quadratic width response: delta is measured against the initial pointer centre, so
    /// rightward drags widen and leftward drags narrow with an accelerating curve. The result snaps
    /// using the selected width step with a 0.1 minimum;
    /// the centre stays fixed even across recommended boundaries. Frozen once the tail stage begins.
    /// </summary>
    public bool UpdateWidth(double pointerCenter, double widthStep = .05d)
    {
        if (TailStage) return false;
        var delta = pointerCenter - _anchorPointerCenter;
        Width = Math.Max(MinimumWidth,
            EditorStagePlacement.QuantizePosition(_baseWidth + Math.Abs(delta) * delta, widthStep));
        return true;
    }

    /// <summary>Enters the tail stage and seeds the tail one grid step after the head.</summary>
    public void BeginTail(int gridDivisor)
    {
        ValidateGrid(gridDivisor);
        TailStage = true;
        TailTime = MinimumTail(gridDivisor);
    }

    /// <summary>
    /// Re-targets the Hold tail. The candidate is clamped so the tail never lands on or before the
    /// head: the minimum legal tail is Time + 1/gridDivisor, strictly greater than the head.
    /// No-op until <see cref="BeginTail"/> has run.
    /// </summary>
    public void UpdateTail(ExactBarTime candidate, int gridDivisor)
    {
        ValidateGrid(gridDivisor);
        if (!TailStage) return;
        var minimum = MinimumTail(gridDivisor);
        TailTime = candidate > minimum ? candidate : minimum;
    }

    public void AlignWidth(double width)
    {
        if (!TailStage && double.IsFinite(width) && width >= MinimumWidth) Width = width;
    }

    private ExactBarTime MinimumTail(int gridDivisor) =>
        Time + ExactBarTime.FromFraction(1, gridDivisor);

    private static void ValidateGrid(int gridDivisor)
    {
        if (gridDivisor <= 0)
            throw new ArgumentOutOfRangeException(nameof(gridDivisor), gridDivisor,
                "Grid divisor must be a positive integer.");
    }

}
