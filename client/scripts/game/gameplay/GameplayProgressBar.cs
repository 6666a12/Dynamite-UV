using Godot;
using DynamiteUniverse.Ui;

namespace DynamiteUniverse.Game;

/// <summary>Top-edge progress, a small moving light and redundant achievement text.</summary>
internal partial class GameplayProgressBar : Control
{
    private readonly GameplayProgressState _state = new();
    private Label _status = null!;
    public Func<UiMotionMode> Motion { get; init; } = () => UiMotionMode.Full;
    internal double Fraction => _state.Fraction;
    internal Color FillColor => _state.Status switch
    {
        ProgressRunStatus.AllPerfect => new Color("#f2ce79"),
        ProgressRunStatus.FullCombo => new Color("#83bce8"),
        _ => new Color("#edf1f7"),
    };

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        Size = new Vector2(1920, 4);
        _status = new Label
        {
            Position = new Vector2(1840, 7), Size = new Vector2(68, 22),
            HorizontalAlignment = HorizontalAlignment.Right,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        _status.AddThemeFontOverride("font", UiFonts.Tech);
        _status.AddThemeFontSizeOverride("font_size", 14);
        _status.AddThemeColorOverride("font_color", new Color("#c5cddc"));
        AddChild(_status);
        Refresh();
    }

    internal void Observe(GameplayHudSnapshot snapshot)
    {
        _state.Observe(snapshot.Time, snapshot.EndTime, snapshot.CountGreat, snapshot.CountGood, snapshot.CountMiss);
        Refresh();
    }

    internal void Complete() { _state.Complete(); QueueRedraw(); }

    private void Refresh()
    {
        if (_status is not null)
            _status.Text = _state.Status switch
            {
                ProgressRunStatus.AllPerfect => "AP", ProgressRunStatus.FullCombo => "FC", _ => "—",
            };
        QueueRedraw();
    }

    public override void _Draw()
    {
        DrawRect(new Rect2(0, 0, 1920, 4), new Color(1, 1, 1, 0.045f));
        var width = (float)_state.Fraction * 1920;
        if (width <= 0) return;
        var color = FillColor;
        DrawRect(new Rect2(0, 0, width, 4), color);
        var tip = new Vector2(Math.Clamp(width, 2, 1918), 2);
        // Local point light only: no global bloom, pulsing or light trail.
        if (Motion() != UiMotionMode.Off)
        {
            var strength = Motion() == UiMotionMode.Full ? 1f : 0.4f;
            for (var radius = 9; radius >= 3; radius--)
                DrawCircle(tip, radius, new Color(color, 0.035f * strength));
        }
        DrawCircle(tip, 2, color.Lerp(Colors.White, 0.7f));
    }
}
