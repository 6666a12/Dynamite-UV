using Godot;

namespace DynamiteUniverse.Ui;

/// <summary>
/// 屏风之前的结果字标前奏：背景压暗 → 中央黑色斜纹区 → 字标拍入并同步提亮 → 短停留。
/// 几何与参数直接来自独立预览 <c>design/result-outcomes/surge/godot/preview.gd</c>：
/// 字标按 1600×400 源图的 80% 显示为 1280×320 @ (320,380)（中心 960,540），
/// 斜纹区同样按预览原坐标 80%（translate(192,108) scale(.80)）。
///
/// 该节点自带 <c>_Process</c>，因为游玩场景在 <c>ShowResults</c> 之后不再推进帧逻辑；
/// 宿主只负责在结算开始时调用一次 <see cref="Present"/>。
/// </summary>
public partial class ResultOutcomePrelude : Control
{
    public const float SheetWidth = 1280f;
    public const float SheetHeight = 320f;
    public const float SheetLeft = 320f;
    public const float SheetTop = 380f;
    public const float BandScale = 0.80f;

    private static readonly Vector2 BandOrigin = new(192f, 108f);
    private static readonly Vector2[] BandShape =
    [
        new(0, 330), new(1880, 330), new(1920, 360),
        new(1920, 720), new(40, 720), new(0, 690),
    ];
    private static readonly Color DimColor = new(0.008f, 0.012f, 0.028f);
    private static readonly Color BandColor = new("080d18");
    private static readonly Color StripeColor = new(0.165f, 0.208f, 0.376f);
    private static readonly Color DefaultHalo = new(0.208f, 0.878f, 1f);

    private readonly TextureRect _wordmark;
    private ShaderMaterial? _material;
    private ResultPreludeTimeline _timeline = null!;
    private Color _halo = DefaultHalo;
    private Action? _finished;
    private bool _active;

    public ResultOutcomePrelude()
    {
        Name = "ResultOutcomePrelude";
        ZIndex = 90;
        Visible = false;
        Size = new Vector2(1920, 1080);
        MouseFilter = MouseFilterEnum.Ignore;
        SetProcess(false);
        _wordmark = new TextureRect
        {
            Name = "ResultOutcomeWordmark",
            Position = new Vector2(SheetLeft, SheetTop),
            Size = new Vector2(SheetWidth, SheetHeight),
            PivotOffset = new Vector2(SheetWidth * .5f, SheetHeight * .5f),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.Scale,
            TextureFilter = TextureFilterEnum.Linear,
            MouseFilter = MouseFilterEnum.Ignore,
            Visible = false,
        };
        AddChild(_wordmark);
    }

    /// <summary>Full/Reduced 才会调用；Motion Off 由宿主直接显示稳定结算页。</summary>
    internal void Present(ResultOutcomeKind outcome, UiMotionProfile motion, Action finished)
    {
        _timeline = new ResultPreludeTimeline(motion.Mode, ResultOutcome.HasWordmark(outcome));
        _finished = finished;
        _active = true;
        _halo = HaloOf(outcome);
        if (_timeline.HasWordmark)
        {
            var slug = ResultOutcome.AssetSlug(outcome);
            _wordmark.Texture = GD.Load<Texture2D>($"res://assets/results/outcomes/{slug}.png");
            _material ??= new ShaderMaterial
            {
                Shader = GD.Load<Shader>("res://shaders/wordmark_diffuse.gdshader"),
            };
            _material.SetShaderParameter("u_halo_color",
                new Vector3(_halo.R, _halo.G, _halo.B));
            _wordmark.Material = _material;
        }
        _wordmark.Visible = _timeline.HasWordmark;
        Visible = true;
        SetProcess(true);
        ApplyFrame();
    }

    /// <summary>屏风完全遮住游玩画面后调用；前奏自身不再绘制任何帧。</summary>
    internal void Dismiss()
    {
        _active = false;
        Visible = false;
        SetProcess(false);
    }

    public override void _Process(double delta)
    {
        if (!_active)
            return;
        _timeline.Advance(delta);
        ApplyFrame();
        if (_timeline.IsComplete)
            Finish();
    }

    public override void _Input(InputEvent e)
    {
        if (!_active)
            return;
        var pressed = e is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true }
            or InputEventScreenTouch { Pressed: true }
            or InputEventKey { Pressed: true, Echo: false, Keycode: Key.Space or Key.Enter or Key.Escape };
        if (!pressed)
            return;
        // 本帧的按压归前奏：跳过后不再传给刚构建的结算页。
        GetViewport().SetInputAsHandled();
        _timeline.Skip();
        ApplyFrame();
        Finish();
    }

    public override void _Draw()
    {
        if (_timeline is null)
            return;
        var dim = _timeline.DimAlpha;
        if (dim > 0)
            DrawRect(new Rect2(0, 0, 1920, 1080), new Color(DimColor, (float)dim));
        var band = _timeline.BandAlpha;
        if (band <= 0)
            return;
        DrawSetTransform(BandOrigin, 0f, new Vector2(BandScale, BandScale));
        DrawColoredPolygon(BandShape,
            new Color(BandColor.Lerp(_halo, _timeline.Impact * .025f), (float)(.98 * band)));
        for (var x = -500f; x < 2100f; x += 72f)
            DrawLine(new Vector2(x, 710), new Vector2(x + 380, 340),
                new Color(StripeColor, (float)(.34 * band)), 1.4f, true);
        DrawSetTransform(Vector2.Zero, 0f, Vector2.One);
    }

    private void ApplyFrame()
    {
        if (_timeline.HasWordmark)
        {
            var scale = _timeline.MarkScale;
            _wordmark.Scale = new Vector2(scale, scale);
            _wordmark.Modulate = new Color(1f, 1f, 1f, (float)_timeline.MarkAlpha);
            _material?.SetShaderParameter("u_energy", _timeline.Energy);
            _material?.SetShaderParameter("u_glow", _timeline.Glow);
        }
        QueueRedraw();
    }

    private void Finish()
    {
        _active = false;
        SetProcess(false);
        var finished = _finished;
        _finished = null;
        finished?.Invoke();
    }

    private static Color HaloOf(ResultOutcomeKind outcome) => outcome switch
    {
        ResultOutcomeKind.TrackComplete => new Color("28d9ff"),
        ResultOutcomeKind.AllPerfect => new Color("28d9ff"),
        ResultOutcomeKind.FullCombo => new Color("43f2b6"),
        ResultOutcomeKind.OneMiss => new Color("ff5d96"),
        ResultOutcomeKind.OneGood => new Color("4ce8bd"),
        ResultOutcomeKind.DoubleOne => new Color("ff5d96"),
        ResultOutcomeKind.StageFailure => new Color("ff554f"),
        _ => DefaultHalo,
    };
}
