using DynamiteUniverse.Shared.Chart;
using DynamiteUniverse.Shared.Judge;

namespace DynamiteUniverse.ChartEditor.Core;

/// <summary>Disposable preview judgement state, never document state or persisted scores.</summary>
public sealed class EditorAutoTimeline
{
    public JudgePlan.Plan Plan { get; }
    public double Second { get; private set; }
    private int _cursor;

    public EditorAutoTimeline(Chart chart)
    {
        Plan = JudgePlan.Build(chart, JudgeSettings.ForPreset(JudgePreset.Hard));
    }

    /// <summary>Seek/edit establishes a baseline, consuming history without emitting hits.</summary>
    public void Reset(double second)
    {
        Second = second;
        _cursor = 0;
        while (_cursor < Plan.Units.Count && Plan.Units[_cursor].Time <= second) _cursor++;
    }

    public void Advance(double second, Action<JudgeUnit> resolve)
    {
        if (second < Second) throw new ArgumentOutOfRangeException(nameof(second), "Use Reset for seek.");
        Second = second;
        while (_cursor < Plan.Units.Count && Plan.Units[_cursor].Time <= second)
            resolve(Plan.Units[_cursor++]);
    }

    public bool IsActive(SustainPath path) => path.StartTime <= Second && Second < path.EndTime;
}
