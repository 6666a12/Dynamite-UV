using Godot;
using DynamiteUniverse.Game;
using DynamiteUniverse.Shared.Chart;
using DynamiteUniverse.Shared.Chart.V2;
using DynamiteUniverse.Shared.Judge;
using DynamiteUniverse.Ui;

namespace DynamiteUniverse.Editor;

/// <summary>Editor-owned seekable Auto HUD state; never reads game settings or writes scores.</summary>
internal sealed class EditorPreviewHud
{
    private readonly GameplayHud _hud;
    private readonly EditorTransportButton _transport;
    private JudgePlan.Plan? _plan;
    private JudgeEngine _score = new(JudgePreset.Hard);
    private JudgmentLineTimeline? _lines;
    private int _cursor;
    private double _second = double.NegativeInfinity;
    private double _end;
    private bool _gamePreview;
    private GameplayHudTitle _title;
    public bool Playing { get; set; }
    public UiMotionMode Motion { get; set; } = UiMotionMode.Full;

    public EditorPreviewHud(Node2D root, Action pause, Action toggle)
    {
        _transport = new EditorTransportButton { ToggleRequested = toggle, ZIndex = 5 };
        root.AddChild(_transport);
        _hud = new(root, pause, 40, 8, () => Motion, () => !Playing, () => Playing);
        _hud.SetMinimal(true);
        _hud.RefreshTitleTag(new("", "hard", "", null), false);
    }

    public void SetChart(V2RuntimeAdapterResult chart, string title, string difficulty, int? level, double audioLength)
    {
        _plan = JudgePlan.Build(chart.RuntimeChart, JudgeSettings.ForPreset(JudgePreset.Hard));
        _lines = JudgmentLineTimeline.FromPlan(_plan);
        _end = Math.Max(audioLength, _plan.EndTime + 2);
        _second = double.NegativeInfinity;
        ResetScore();
        _title = new(title, difficulty.ToLowerInvariant(), difficulty, level);
        _hud.RefreshTitleTag(_title, _gamePreview);
    }

    public void SetGamePreview(bool value)
    {
        _gamePreview = value;
        _transport.Visible = !value;
        _hud.SetMinimal(!value);
        if (_plan is not null) _hud.RefreshTitleTag(_title, value);
    }
    private void ResetScore() { _score = new(JudgePreset.Hard, _plan?.HeadlineUnitCount ?? 0); _cursor = 0; }

    public void Update(double second, double delta, GameplayStageRenderer stage)
    {
        _transport.Observe(_plan is not null, Playing);
        if (_plan is null) return;
        var seek = second < _second || Math.Abs(second - _second) > .5;
        if (second < _second) ResetScore();
        var count = _score.CountPerfect;
        while (_cursor < _plan.Units.Count && _plan.Units[_cursor].Time <= second)
        {
            var unit = _plan.Units[_cursor++];
            _score.Apply(unit.Category, JudgeGrade.Perfect, unit.AffectsCombo, unit.AffectsJudgeCounts);
        }
        if (_gamePreview && Playing && !seek && _score.CountPerfect != count)
            _hud.FlashJudge("PERFECT", true);
        _second = second;
        _hud.Update(new(_score.NormalizedScore(_plan.TheoreticalMax), 100,
            _score.CountPerfect, 0, 0, 0, _score.MaxCombo, _score.Combo,
            second, _end, _gamePreview, false, 100), delta);
        for (var track = 0; track < 3; track++)
            stage.SetJudgmentLineBrightness((Track)track, (float)(_lines?.Sample((Track)track, second, Motion) ?? 1));
    }
}
