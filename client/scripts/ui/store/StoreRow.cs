using Godot;
using DynamiteUniverse.Game;
using DynamiteUniverse.Network;

namespace DynamiteUniverse.Ui;

/// <summary>Real release identity on the shared catalogue surface. No preview transport.</summary>
internal sealed partial class StoreRow : Button
{
    private bool _selected;
    private UiHoverFade _hover;
    private Texture2D? _thumbnail;
    private readonly string _byline, _chartLabel;
    public StoreRelease Release { get; }
    public Texture2D? Thumbnail { set { if (_thumbnail == value) return; _thumbnail = value; QueueRedraw(); } }
    public StoreRow(StoreRelease release, Action<StoreRelease> selected)
    {
        Release = release; Name = "StoreRelease_" + release.Id.ToString("N");
        CustomMinimumSize = new(0, StoreDeck.RowHeight); SizeFlagsHorizontal = SizeFlags.ExpandFill;
        MouseDefaultCursorShape = CursorShape.PointingHand; TooltipText = release.Title;
        ActionMode = ActionModeEnum.Release;
        foreach (var state in new[] { "normal", "hover", "pressed", "focus" }) AddThemeStyleboxOverride(state, new StyleBoxEmpty());
        var charters = string.Join(", ", release.Display?.Charts.SelectMany(c => c.Charters).Distinct() ?? []);
        _byline = string.Join("  ·  ", new[] { release.Artist, charters.Length > 0 ? "谱师 " + charters : "" }.Where(s => !string.IsNullOrWhiteSpace(s)));
        _chartLabel = release.Display is { } d ? $"{d.Charts.Length} 差分" : "";
        Pressed += () => { if (Visible && !TransitionDirector.IsBusy) selected(Release); };
        MouseEntered += UpdateHover; MouseExited += UpdateHover; FocusEntered += UpdateHover; FocusExited += UpdateHover;
        SetProcess(false);
    }
    public void Select(bool selected) { if (_selected != selected) { _selected = selected; QueueRedraw(); } }
    private bool Hot => IsHovered() || HasFocus();
    private void UpdateHover()
    {
        if (UiMotionProfile.For(GameSession.Settings.MotionMode).IsAnimated) SetProcess(true);
        else { _hover.Snap(Hot); QueueRedraw(); }
    }
    public override void _Process(double delta)
    {
        if (_hover.Update(Hot, delta)) QueueRedraw();
        if (_hover.Settled(Hot)) SetProcess(false);
    }
    public override void _Draw()
    {
        CatalogueCardFace.Draw(this, _hover.Value, _selected ? 1f : 0f);
        var r = new Rect2(20, 14, 68, 68);
        DrawRect(r, MenuStyle.Background);
        if (_thumbnail is not null && GodotObject.IsInstanceValid(_thumbnail)) DrawTextureRect(_thumbnail, r, false);
        else DrawLine(r.Position + new Vector2(13, 51), r.Position + new Vector2(52, 17), UiFonts.Line, 2);
        CatalogueCardFace.DrawHeading(this, Release.Title, _byline, 112, Mathf.Max(100, Size.X - 278));
        DrawString(UiFonts.Cjk, new(Size.X - 138, 54), _chartLabel, HorizontalAlignment.Right, 112, 19, MenuStyle.Secondary);
        if (HasFocus()) DrawRect(new Rect2(2, 2, Size.X - 4, Size.Y - 4), MenuStyle.Accent, false, 1.5f);
    }
}
