using Godot;
using DynamiteUniverse.Game;
using DynamiteUniverse.Shared.Judge;
using DynamiteUniverse.Shared.Score;

namespace DynamiteUniverse.Ui;

internal sealed record ResultScreenData(GameplayRunContext Track, ScoreRecord Score,
    int TotalUnits, int? PreviousBest, bool NewRecord, bool Auto,
    bool Hardcore, bool Bleed, bool Mirror, Texture2D? Cover);

/// <summary>Penpot Result v2. Owns presentation and input gating, never score persistence.</summary>
public partial class ResultScreen : Control
{
    private readonly List<(Control Node, double Start, double End)> _groups = [];
    private readonly List<CutButton> _buttons = [];
    private Control _page = null!;
    private TextureRect _grade = null!;
    private ResultShutter _shutter = null!;
    private ResultGradeArt _art = null!;
    private ResultRevealTimeline _timeline = null!;
    private Action? _covered;
    private bool _coveredCalled;
    private bool _actionInFlight;
    private bool _active;
    private bool _actionsEnabled;
    private bool _canNext;
    private ulong _unlockAt;
    private int _lastFrame = -1;
    private int _lastAmbientFrame = -1;
    private double _ambientElapsed;
    private UiMotionProfile _motion = null!;

    internal void Present(ResultScreenData data, UiMotionProfile motion,
        Action covered, Action back, Action next, Action retry, bool canNext)
    {
        _motion = motion;
        _covered = covered;
        _canNext = canNext;
        _timeline = new ResultRevealTimeline(motion.Mode);
        _art = new ResultGradeArt(data.Score.Grade, motion.Mode == UiMotionMode.Full);
        _lastFrame = -1;
        _lastAmbientFrame = -1;
        _ambientElapsed = 0;
        Size = new Vector2(1920, 1080);
        ClipContents = true;
        MouseFilter = MouseFilterEnum.Stop;
        Build(data, back, next, retry);
        _active = true;
        Visible = true;
        ApplyFrame();
    }

    public override void _Process(double delta)
    {
        if (!_active) return;
        _art.Poll();
        if (_motion.Mode == UiMotionMode.Full && _timeline.IsComplete && double.IsFinite(delta) && delta > 0)
            _ambientElapsed += delta;
        _timeline.Advance(delta, _art.Ready);
        ApplyFrame();
    }

    public override void _Input(InputEvent e)
    {
        if (!_active || _actionInFlight) return;
        var pressed = e is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true }
            or InputEventScreenTouch { Pressed: true }
            or InputEventKey { Pressed: true, Echo: false, Keycode: Key.Space or Key.Enter or Key.Escape };
        if (!_timeline.IsComplete && pressed)
        {
            // Handle before GUI dispatch so this same press cannot activate a newly exposed button.
            GetViewport().SetInputAsHandled();
            _unlockAt = Time.GetTicksMsec() + 120;
            _timeline.Skip(_art.Ready);
            ApplyFrame();
        }
        else if (Time.GetTicksMsec() < _unlockAt && e is InputEventMouseButton or InputEventScreenTouch)
            GetViewport().SetInputAsHandled();
    }

    private void ApplyFrame()
    {
        _page.Visible = _timeline.BackgroundVisible;
        if (_page.Visible && !_coveredCalled)
        {
            _coveredCalled = true;
            _covered?.Invoke();
        }
        _shutter.Coverage = (float)_timeline.Coverage;
        _shutter.Visible = _shutter.Coverage > 0;
        _grade.Visible = _timeline.GradeVisible;
        if (_motion.Mode == UiMotionMode.Full && !_timeline.IsComplete)
        {
            if (_grade.Visible && _lastFrame != _timeline.GradeFrame)
            {
                _lastFrame = _timeline.GradeFrame;
                _grade.Texture = _art.At(_lastFrame);
            }
        }
        else if (_motion.Mode == UiMotionMode.Full)
        {
            var ambientFrame = _art.AmbientFrameIndex(_ambientElapsed);
            if (_lastAmbientFrame != ambientFrame)
            {
                _lastAmbientFrame = ambientFrame;
                _grade.Texture = _art.At(ambientFrame);
            }
        }
        else _grade.Texture = _art.Still;
        _grade.Modulate = new Color(1, 1, 1, _motion.Mode == UiMotionMode.Reduced
            ? (float)_timeline.Opacity(0, .26) : 1);
        foreach (var (node, start, end) in _groups)
        {
            var alpha = (float)_timeline.Opacity(start, end);
            node.Modulate = new Color(1, 1, 1, alpha);
            node.Position = new Vector2(0, _motion.AllowDirectionalMotion ? 18 * (1 - alpha) : 0);
        }
        var enabled = _timeline.IsComplete && !_actionInFlight && Time.GetTicksMsec() >= _unlockAt;
        if (enabled != _actionsEnabled)
        {
            _actionsEnabled = enabled;
            foreach (var b in _buttons) b.MouseFilter = enabled ? MouseFilterEnum.Stop : MouseFilterEnum.Ignore;
        }
        if (enabled && _motion.Mode != UiMotionMode.Full)
            SetProcess(false); // Full keeps a low-intensity grade current after the reveal.
    }

    private void Build(ResultScreenData d, Action back, Action next, Action retry)
    {
        _page = Group(this, "ResultPage");
        // Opaque base appears only once shutters are closed. Missing covers never expose gameplay.
        Rect(_page, 0, 0, 1920, 1080, UiFonts.Bg);
        var key = d.Score.Grade == "Ω" ? "omega" : d.Score.Grade;
        Texture(_page, 0, 0, 1920, 1080, GD.Load<Texture2D>($"res://assets/results/background_{key}.svg"));
        _grade = Texture(_page, 104, 204, 624, 624, _art.Still);
        _grade.Name = "GradeV5Current";
        var header = RevealGroup("TrackIdentity", 1.78, 2.00);
        BuildHeader(header, d);
        var clear = RevealGroup("Clear", 1.82, 2.04);
        Label(clear, 104, 834, 624, "CLEAR", 17, UiFonts.Dim, align: HorizontalAlignment.Center);
        Label(clear, 104, 882, 624, $"{d.Score.Acc:F2}%", 52, UiFonts.GradeColor(d.Score.Grade), align: HorizontalAlignment.Center);
        var score = RevealGroup("Score", 1.94, 2.18);
        Label(score, 900, 285, 220, "SCORE", 20, UiFonts.Dim);
        Label(score, 890, 380, 956, $"{d.Score.Score:N0}", d.Score.Score >= 1_000_000 ? 120 : 132, UiFonts.Text, bold: true);
        if (d.PreviousBest is int best)
            Label(score, 902, 446, 940, $"BEST {best:N0}", 18, UiFonts.Dim);
        if (d.NewRecord && !d.Auto)
        {
            Panel(score, 1328, 265, 214, 40, UiFonts.Pink, UiFonts.Pink, 10);
            Label(score, 1328, 285, 214, "NEW RECORD", 17, UiFonts.InkText, true, HorizontalAlignment.Center);
            if (d.PreviousBest is int previous)
                Label(score, 1558, 285, 290, $"+ {d.Score.Score - previous:N0}", 24, UiFonts.Pink, align: HorizontalAlignment.Right);
        }
        Rect(score, 900, 468, 948, 1, new Color(UiFonts.Line, .66f));
        BuildAchievement(RevealGroup("Achievement", 2.08, 2.30), d);
        BuildJudgments(RevealGroup("Judgments", 2.20, 2.42), d);
        var actions = RevealGroup("Actions", 2.36, 2.60);
        Rect(actions, 72, 940, 1776, 1, new Color(UiFonts.Line, .8f));
        Button(actions, 72, 380, "返回选曲", back);
        var nextButton = Button(actions, 1104, 324, "下一首", next);
        nextButton.Disabled = !_canNext;
        Button(actions, 1452, 396, "再来一次", retry, true);
        Texture(actions, 111, 992, 26, 24, GD.Load<Texture2D>("res://assets/results/back.svg"));
        Texture(actions, 1502, 990, 28, 28, GD.Load<Texture2D>("res://assets/results/retry.svg"));
        _shutter = new ResultShutter { Name = "ResultShutter", Size = Size, MouseFilter = MouseFilterEnum.Ignore };
        AddChild(_shutter);
    }

    private void BuildHeader(Control header, ResultScreenData d)
    {
        if (d.Cover is { } cover)
        {
            var art = Texture(header, 72, 64, 128, 128, cover);
            art.StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered;
            art.ClipContents = true;
        }
        else
        {
            Texture(header, 72, 64, 128, 128, GD.Load<Texture2D>("res://assets/results/cover_placeholder.svg"));
            Label(header, 72, 157, 128, "NO COVER", 12, UiFonts.Dim, align: HorizontalAlignment.Center);
        }
        Label(header, 234, 98, 1040, d.Track.SongTitle, 46, UiFonts.Text);
        Label(header, 236, 145, 1040, d.Track.Artist, 22, UiFonts.Dim);
        var diff = d.Track.DifficultyText;
        var width = Math.Clamp(UiFonts.TechBold.GetStringSize(diff, fontSize: 18).X + 32, 142, 450);
        Panel(header, 236, 176, width, 36, UiFonts.Pink, UiFonts.Pink, 9);
        Label(header, 236, 194, width, diff, 18, UiFonts.InkText, true, HorizontalAlignment.Center);
        Label(header, 257 + width, 194, 1010 - width, $"谱师 {d.Track.Charter}", 18, UiFonts.Dim);
        Label(header, 1470, 97, 378, "RESULT", 28, UiFonts.Text, align: HorizontalAlignment.Right);
        if (d.Auto)
            Label(header, 1380, 132, 468, "AUTO · 不计成绩", 19, UiFonts.Dim, align: HorizontalAlignment.Right);
        var modes = new List<(string Name, float Width, Color Color)>();
        if (d.Hardcore) modes.Add(("HARDCORE", 172, UiFonts.Pink));
        if (d.Bleed) modes.Add(("BLEED", 134, UiFonts.Pink));
        if (d.Mirror) modes.Add(("MIRROR", 170, UiFonts.Cyan));
        var x = 1848f - modes.Sum(m => m.Width) - Math.Max(0, modes.Count - 1) * 12;
        foreach (var m in modes)
        {
            Panel(header, x, 163, m.Width, 38, new Color("141b33"), UiFonts.Line, 9);
            Rect(header, x + 12, 177, 4, 10, m.Color);
            Label(header, x + 12, 182, m.Width - 12, m.Name, 15, m.Color, align: HorizontalAlignment.Center);
            x += m.Width + 12;
        }
        Rect(header, 72, 236, 1776, 1, new Color(UiFonts.Line, .68f));
        Rect(header, 72, 235, 128, 3, UiFonts.Cyan);
    }

    private void BuildAchievement(Control parent, ResultScreenData d)
    {
        var all = d.TotalUnits > 0 && d.Score.Perfect == d.TotalUnits && d.Score.Great == 0 && d.Score.Good == 0 && d.Score.Miss == 0;
        var fc = d.TotalUnits > 0 && d.Score.Miss == 0 && d.Score.MaxCombo >= d.TotalUnits;
        if (all || fc)
        {
            var accent = all ? UiFonts.Pink : UiFonts.Cyan;
            Panel(parent, 900, 506, 530, 104, new Color("141b33"), new Color(accent, .38f), 16);
            Texture(parent, 924, 526, 64, 64, GD.Load<Texture2D>($"res://assets/results/{(all ? "ap" : "fc")}.svg"));
            Label(parent, 1010, 549, 400, all ? "ALL PREFECT" : "FULL COMBO", 28, accent, true);
            Label(parent, 1012, 584, 394, all ? "全部最高判定" : "全连达成", 17, UiFonts.Dim);
        }
        Label(parent, 1484, 523, 364, "MAX COMBO", 17, UiFonts.Dim);
        var maxComboText = $"{d.Score.MaxCombo:N0}";
        var maxComboWidth = UiFonts.Tech.GetStringSize(maxComboText, fontSize: 50).X;
        const float maxComboX = 1480;
        Label(parent, maxComboX, 575, maxComboWidth + 4, maxComboText, 50, UiFonts.Text);
        var totalX = maxComboX + maxComboWidth + 12;
        Label(parent, totalX, 586, MathF.Max(80f, 1848f - totalX), $"/ {d.TotalUnits:N0}", 20, UiFonts.Dim);
    }

    private void BuildJudgments(Control parent, ResultScreenData d)
    {
        int[] values = [d.Score.Perfect, d.Score.Great, d.Score.Good, d.Score.Miss];
        string[] tags = ["PREFECT", "GREAT", "GOOD", "MISS"];
        Color[] colors = [UiFonts.Cyan, new("ffcc59"), UiFonts.Casual, UiFonts.Dim];
        var total = values.Sum();
        const float distributionX = 900;
        const float distributionWidth = 860;
        Label(parent, 900, 667, 300, "判定分布", 19, UiFonts.Dim);
        Label(parent, distributionX, 667, distributionWidth, $"{total:N0} NOTES", 16, UiFonts.Dim, align: HorizontalAlignment.Right);
        Rect(parent, distributionX, 702, distributionWidth, 10, UiFonts.Line);
        var cursor = distributionX;
        for (var i = 0; i < 4; i++)
        {
            var width = total > 0 ? distributionWidth * values[i] / total : 0;
            if (width > 0) Rect(parent, cursor, 702, width, 10, colors[i]);
            cursor += width;
            var x = 900 + i * 246;
            Rect(parent, x, 748, 5, 13, colors[i]);
            Label(parent, x + 17, 755, 200, tags[i], 17, colors[i]);
            Label(parent, x + 14, 809, 196, $"{values[i]:N0}", 42, values[i] > 0 ? UiFonts.Text : UiFonts.Dim);
            if (i > 0) Rect(parent, x - 28, 748, 1, 84, new Color(UiFonts.Line, .45f));
        }
    }

    private CutButton Button(Control parent, float x, float width, string text, Action action, bool primary = false)
    {
        var b = new CutButton { Position = new(x, 964), Size = new(width, 80), Text = text,
            FontSize = 26, Cut = 18, StyleKind = primary ? CutButton.ButtonStyle.Solid : CutButton.ButtonStyle.Outline,
            MouseFilter = MouseFilterEnum.Ignore };
        b.Pressed += () =>
        {
            if (!_actionsEnabled || _actionInFlight || b.Disabled || TransitionDirector.IsBusy) return;
            _actionInFlight = true;
            ApplyFrame();
            b.CommitPulse(_motion.FocusDuration);
            if (!_motion.IsAnimated) { action(); return; }
            var tween = CreateTween();
            tween.TweenInterval(_motion.FocusDuration);
            tween.TweenCallback(Callable.From(() => { if (IsInsideTree()) action(); }));
        };
        parent.AddChild(b);
        _buttons.Add(b);
        return b;
    }

    private Control RevealGroup(string name, double start, double end)
    {
        var c = Group(_page, name);
        _groups.Add((c, start, end));
        return c;
    }

    private static Control Group(Node parent, string name)
    {
        var c = new Control { Name = name, Size = new(1920, 1080), MouseFilter = MouseFilterEnum.Ignore };
        parent.AddChild(c);
        return c;
    }

    private static void Rect(Node parent, float x, float y, float w, float h, Color color) =>
        parent.AddChild(new ColorRect { Position = new(x, y), Size = new(w, h), Color = color, MouseFilter = MouseFilterEnum.Ignore });

    private static void Panel(Node parent, float x, float y, float w, float h, Color fill, Color border, float cut) =>
        parent.AddChild(new CutPanel { Position = new(x, y), Size = new(w, h), Fill = fill, Border = border,
            Cut = cut, BorderWidth = 1, MouseFilter = MouseFilterEnum.Ignore });

    private static TextureRect Texture(Node parent, float x, float y, float w, float h, Texture2D texture)
    {
        var c = new TextureRect { Position = new(x, y), Size = new(w, h), Texture = texture,
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.Scale,
            TextureFilter = TextureFilterEnum.Linear, MouseFilter = MouseFilterEnum.Ignore };
        parent.AddChild(c);
        return c;
    }

    private static void Label(Node parent, float x, float cy, float width, string text, int size, Color color,
        bool bold = false, HorizontalAlignment align = HorizontalAlignment.Left)
    {
        var label = new Label { Text = text, Position = new(x, cy - size * .75f), Size = new(width, size * 1.5f),
            HorizontalAlignment = align, VerticalAlignment = VerticalAlignment.Center,
            TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis, ClipText = true,
            MouseFilter = MouseFilterEnum.Ignore };
        label.AddThemeFontOverride("font", text.Any(c => c > 0x2E80) ? UiFonts.Cjk : bold ? UiFonts.TechBold : UiFonts.Tech);
        label.AddThemeFontSizeOverride("font_size", size);
        label.AddThemeColorOverride("font_color", color);
        parent.AddChild(label);
    }
}
