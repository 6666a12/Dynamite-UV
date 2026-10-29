using Godot;
using DynamiteUniverse.Game;

namespace DynamiteUniverse.Ui;

/// <summary>
/// 首页动作底板：切角底板 + 中文/英文两行 + 可选右箭头。
/// 文字与箭头是底板的子节点，因此永远画在底板填充之上；Home v2 的两行按稿内左上对齐给定坐标，
/// 与通用 <see cref="CutButton"/> 的居中排布不同，故单独成控件而不是给 CutButton 加分支。
/// 禁用态直接用设计稿的灰板灰字，并且不接收输入。
/// </summary>
public partial class HomeActionTile : Control
{
    public enum TileStyle { Primary, Outline, Disabled }

    [Signal]
    public delegate void PressedEventHandler();

    private Label? _title;
    private Label? _subTitle;
    private TileStyle _style = TileStyle.Outline;
    private bool _hover;
    private UiHoverFade _hoverFade;
    private float _pressAmount;

    public string Title { get; set; } = "";
    public string SubTitle { get; set; } = "";

    public TileStyle Style
    {
        get => _style;
        set
        {
            _style = value;
            ApplyInteraction();
            Refresh();
        }
    }

    public Vector2 TitleOffset { get; set; } = new(28f, 15f);
    public Vector2 SubTitleOffset { get; set; } = new(28f, 53f);
    public int TitleFontSize { get; set; } = 24;
    public int SubTitleFontSize { get; set; } = 12;
    public Vector2 CutSize { get; set; } = new(13.5f, 9.1f);
    public bool ShowArrow { get; set; }
    public Vector2 ArrowOffset { get; set; }

    private bool Interactive => _style != TileStyle.Disabled;

    public override void _Ready()
    {
        _title = BuildLabel(Title, TitleOffset, UiFonts.Cjk, TitleFontSize, TitleColor);
        _subTitle = BuildLabel(SubTitle, SubTitleOffset, HomePalette.Tech(1), SubTitleFontSize, SubTitleColor);
        ApplyInteraction();
        MouseEntered += () => SetHover(true);
        MouseExited += () => SetHover(false);
    }

    public override void _Process(double delta)
    {
        var changed = _hoverFade.Update(_hover && Interactive, delta);
        if (_pressAmount > 0f)
        {
            var duration = Math.Max(0.01, MotionProfile.PressDuration);
            _pressAmount = Mathf.MoveToward(_pressAmount, 0f, (float)delta / (float)duration);
            changed = true;
        }

        if (changed)
            QueueRedraw();
        if (_hoverFade.Settled(_hover && Interactive) && _pressAmount <= 0f)
            SetProcess(false);
    }

    public override void _GuiInput(InputEvent e)
    {
        if (!Interactive)
            return;
        var pressed = e is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true }
            or InputEventScreenTouch { Pressed: true };
        if (!pressed)
            return;

        _pressAmount = MotionProfile.IsAnimated ? 1f : 0f;
        QueueRedraw();
        AcceptEvent();
        EmitSignal(SignalName.Pressed);
    }

    public override void _Draw()
    {
        var hover = UiEase.Standard(_hoverFade.Value);
        var press = _style == TileStyle.Primary ? 0f : _pressAmount;
        var points = BuildPlatePoints();

        var fill = _style switch
        {
            TileStyle.Primary => HomePalette.PrimaryFill.Lerp(HomePalette.PrimaryHoverFill, hover),
            TileStyle.Disabled => HomePalette.DisabledFill,
            _ => HomePalette.TileFill.Lerp(HomePalette.TileHoverFill, hover),
        };
        if (press > 0f)
            fill = fill.Lerp(Colors.White, 0.10f * press);

        DrawColoredPolygon(points, fill);
        if (_style != TileStyle.Primary)
        {
            var border = _style == TileStyle.Disabled
                ? HomePalette.DisabledBorder
                : HomePalette.TileBorder.Lerp(HomePalette.TileHoverBorder, hover);
            DrawPolyline(UiGeometry.Close(points), border, 1f, true);
        }

        if (ShowArrow)
            DrawArrow(MotionProfile.AllowDirectionalMotion ? 3f * hover : 0f);
    }

    private Label BuildLabel(string text, Vector2 offset, Font font, int size, Color color)
    {
        var label = new Label
        {
            Position = offset,
            Size = new Vector2(Mathf.Max(0f, Size.X - offset.X - 12f), size * 1.6f),
            Text = text,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        UiLabels.Apply(label, font, size, color);
        AddChild(label);
        return label;
    }

    private Vector2[] BuildPlatePoints()
    {
        var width = Size.X;
        var height = Size.Y;
        var cutX = Mathf.Min(CutSize.X, width * 0.5f);
        var cutY = Mathf.Min(CutSize.Y, height * 0.5f);
        return
        [
            new(cutX, 0f), new(width, 0f), new(width, height - cutY),
            new(width - cutX, height), new(0f, height), new(0f, cutY),
        ];
    }

    /// <summary>箭头与稿内同形：斜角箭头头部 + 横杆，2.5px，深青描边。</summary>
    private void DrawArrow(float shift)
    {
        var origin = ArrowOffset + new Vector2(shift, 0f);
        DrawPolyline(
            [origin + new Vector2(16f, 0f), origin + new Vector2(34f, 14f), origin + new Vector2(16f, 28f)],
            HomePalette.PrimaryArrow, 2.5f, true);
        DrawLine(origin + new Vector2(0f, 14f), origin + new Vector2(34f, 14f),
            HomePalette.PrimaryArrow, 2.5f, true);
    }

    private void ApplyInteraction()
    {
        MouseFilter = Interactive ? MouseFilterEnum.Stop : MouseFilterEnum.Ignore;
        MouseDefaultCursorShape = Interactive ? CursorShape.PointingHand : CursorShape.Arrow;
        if (Interactive)
            return;
        _hover = false;
        _hoverFade.Snap(false);
        _pressAmount = 0f;
        SetProcess(false);
    }

    private void SetHover(bool hover)
    {
        if (_hover == hover || !Interactive)
            return;
        _hover = hover;
        if (MotionProfile.IsAnimated)
        {
            SetProcess(true);
        }
        else
        {
            _hoverFade.Snap(hover);
            SetProcess(false);
        }
        QueueRedraw();
    }

    private void Refresh()
    {
        if (!IsInsideTree())
            return;
        if (_title is not null)
        {
            _title.Text = Title;
            _title.Position = TitleOffset;
            UiLabels.Apply(_title, UiFonts.Cjk, TitleFontSize, TitleColor);
        }
        if (_subTitle is not null)
        {
            _subTitle.Text = SubTitle;
            _subTitle.Position = SubTitleOffset;
            UiLabels.Apply(_subTitle, HomePalette.Tech(1), SubTitleFontSize, SubTitleColor);
        }
        QueueRedraw();
    }

    private Color TitleColor => _style switch
    {
        TileStyle.Primary => HomePalette.PrimaryText,
        TileStyle.Disabled => HomePalette.DisabledText,
        _ => HomePalette.TileText,
    };

    private Color SubTitleColor => _style switch
    {
        TileStyle.Primary => HomePalette.PrimarySubText,
        TileStyle.Disabled => HomePalette.DisabledSubText,
        _ => HomePalette.TileSubText,
    };

    private static UiMotionProfile MotionProfile =>
        UiMotionProfile.For(GameSession.Settings.MotionMode);
}
