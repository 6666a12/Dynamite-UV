using Godot;
using DynamiteUniverse.Game;
using DynamiteUniverse.Network;

namespace DynamiteUniverse.Ui;

/// <summary>Expanded store slot on the same catalogue face/height/text/cover motion as a song.
/// Its lower slot owns chart selection instead of an audio transport. No network or install commands.</summary>
internal sealed partial class StoreDetailsView : Control
{
    private const float Height = 392;
    private SongCardMotion _motion = new(StoreDeck.RowHeight, Height);
    private CardMotionProfile Profile { get { var p = UiMotionProfile.For(GameSession.Settings.MotionMode); return new(p.IsAnimated, p.AllowDirectionalMotion); } }
    private readonly TextureRect _cover;
    private readonly Label _title, _artist, _mapper, _bpm;
    private readonly HBoxContainer _charts;
    private readonly ScrollContainer _chartScroll;
    private readonly Button _fold, _prev, _next;
    private const int ChartsPerPage = 4;
    private int _chartPage, _chartIndex;
    private Texture2D? _texture;
    private int _generation;
    private StoreRelease? _release;
    public Guid? ReleaseId => _release?.Id;
    public bool Closing => _motion.IsCollapsing;
    public event Action? FoldRequested, Folded;
    public StoreDetailsView()
    {
        Name = "StoreExpanded"; Visible = false; ClipContents = true; MouseFilter = MouseFilterEnum.Pass;
        SizeFlagsHorizontal = SizeFlags.ExpandFill;
        var frame = new Rect2(32, 52, 288, 288);
        AddChild(new CatalogueScope { Position = frame.Position, Size = frame.Size, MouseFilter = MouseFilterEnum.Ignore });
        _cover = new TextureRect { Name = "StoreCover", Position = frame.Position, Size = frame.Size, ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered, MouseFilter = MouseFilterEnum.Ignore }; AddChild(_cover);
        _title = Text("StoreTitle", new(360, 37, 840, 68), 46, MenuStyle.Text);
        _artist = Text("StoreArtist", new(360, 116, 840, 42), 25, MenuStyle.Text);
        _mapper = Text("StoreCharters", new(360, 172, 680, 38), 21, MenuStyle.Secondary);
        _bpm = Text("StoreBpm", new(360, 216, 782, 34), 21, MenuStyle.Secondary);
        _chartScroll = new ScrollContainer { Name = "StoreCharts", Position = new(360, 273), Size = new(716, 89), HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, VerticalScrollMode = ScrollContainer.ScrollMode.Disabled };
        _charts = new HBoxContainer(); _charts.AddThemeConstantOverride("separation", 12); _chartScroll.AddChild(_charts); AddChild(_chartScroll);
        _prev = OnlineWidgets.Link(this, "‹", new(1092, 280, 67, 64), 27, () => PageCharts(-1)); _prev.Name = "StoreChartsPrevious";
        _next = OnlineWidgets.Link(this, "›", new(1175, 280, 67, 64), 27, () => PageCharts(1)); _next.Name = "StoreChartsNext";
        _fold = OnlineWidgets.Link(this, "收起", new(1177, 17, 82, 50), 18, () => FoldRequested?.Invoke(), MenuStyle.Secondary); _fold.Name = "StoreFold";
        SetProcess(false);
    }
    private Label Text(string name, Rect2 r, int size, Color color)
    {
        var l = OnlineWidgets.Text(this, "", r, size, color); l.Name = name; l.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis; return l;
    }
    public override void _Draw() => CatalogueCardFace.Draw(this, selected: 1f, expanded: true);
    public void Open(StoreRelease r, bool focus)
    {
        bool hidden = !Visible; Visible = true; _fold.Disabled = false; _chartScroll.Visible = true;
        if (_motion.Expand(hidden, Profile)) ApplyHeight();
        _release = r; _generation++;
        ClearCover(); _motion.HideCover(Profile); _cover.Modulate = new Color(1, 1, 1, _motion.CoverAlpha);
        _chartPage = _chartIndex = 0;
        RenderCharts(r);
        _bpm.Text = _mapper.Text = "";
        if (r.Display?.Charts is { Length: > 0 }) ChooseChart(r, 0); else SetText(r, "");
        SetProcess(_motion.Animating); if (focus) _fold.GrabFocus();
    }
    private void PageCharts(int direction)
    {
        if (_release is not { Display: {} display } r || Closing) return;
        _chartPage = Math.Clamp(_chartPage + direction, 0, (display.Charts.Length - 1) / ChartsPerPage); RenderCharts(r);
    }
    private void RenderCharts(StoreRelease r)
    {
        foreach (var c in _charts.GetChildren()) { _charts.RemoveChild(c); c.QueueFree(); }
        var charts = r.Display?.Charts ?? [];
        _prev.Visible = _next.Visible = charts.Length > ChartsPerPage;
        _prev.Disabled = _chartPage == 0; _next.Disabled = (_chartPage + 1) * ChartsPerPage >= charts.Length;
        for (var i = _chartPage * ChartsPerPage; i < Math.Min(charts.Length, (_chartPage + 1) * ChartsPerPage); i++)
        {
            var chart = charts[i]; var diff = chart.Difficulty == "custom" ? chart.DifficultyKey ?? "CUSTOM" : chart.Difficulty.ToUpperInvariant();
            var b = new Button { Name = $"StoreChart_{i}", Text = $"{diff}   {(chart.Unrated ? "?" : chart.Level?.ToString("00") ?? "?")}", CustomMinimumSize = new(160, 53),
                TooltipText = diff, MouseDefaultCursorShape = CursorShape.PointingHand, ClipText = true, SizeFlagsVertical = SizeFlags.ShrinkCenter };
            b.AddThemeFontOverride("font", UiFonts.Cjk); b.AddThemeFontSizeOverride("font_size", 18);
            b.AddThemeStyleboxOverride("normal", new StyleBoxFlat { BgColor = MenuStyle.Surface, BorderWidthBottom = 2, BorderColor = UiFonts.DiffColor(chart.Difficulty) });
            b.AddThemeColorOverride("font_color", i == _chartIndex ? MenuStyle.Accent : MenuStyle.Secondary);
            int ix = i; b.Pressed += () => { if (!Closing && _release?.Id == r.Id && !TransitionDirector.IsBusy) ChooseChart(r, ix); }; _charts.AddChild(b);
        }
    }
    private void ChooseChart(StoreRelease r, int ix)
    {
        _chartIndex = ix; var chart = r.Display!.Charts[ix]; var charters = string.Join(", ", chart.Charters);
        _bpm.Text = (chart.BpmMin == chart.BpmMax ? $"{chart.BpmMin:0.##}" : $"{chart.BpmMin:0.##}–{chart.BpmMax:0.##}") + " BPM";
        for (int i = 0; i < _charts.GetChildCount(); i++)
            _charts.GetChild<Button>(i).AddThemeColorOverride("font_color", i + _chartPage * ChartsPerPage == ix ? MenuStyle.Accent : MenuStyle.Secondary);
        SetText(r, charters.Length > 0 ? "谱师  " + charters : "");
    }
    private void SetText(StoreRelease r, string charters)
    {
        var next = new CardTextContent(r.Title, r.Artist, charters);
        if (_motion.BeginTextRefresh(next, _generation, _title.Modulate.A, Profile) == SongCardMotion.TextOutcome.Applied) ApplyText();
        SetProcess(_motion.Animating);
    }
    private void ApplyText()
    {
        var t = _motion.PendingText; _title.Text = _title.TooltipText = t.Title;
        _artist.Text = _artist.TooltipText = t.Artist; _mapper.Text = _mapper.TooltipText = t.Charter;
    }
    private void ClearCover() { _cover.Texture = null; _texture?.Dispose(); _texture = null; }
    public void SetCover(byte[] data, string mime)
    {
        if (_release is null || Closing) return;
        ClearCover(); _texture = Res.LoadTextureBytes(mime switch { "image/png" => "png", "image/jpeg" => "jpg", "image/webp" => "webp", _ => "" }, data);
        _cover.Texture = _texture; _motion.ShowCover(Profile); _cover.Modulate = new Color(1, 1, 1, _motion.CoverAlpha); SetProcess(_motion.Animating);
    }
    public void ClearNow()
    {
        Visible = false; _release = null; _generation++; ClearCover(); SetProcess(false);
        _motion = new(StoreDeck.RowHeight, Height); _artist.Text = _title.Text = _mapper.Text = _bpm.Text = "";
        _artist.Modulate = _title.Modulate = _mapper.Modulate = Colors.White; _fold.Disabled = true; _prev.Visible = _next.Visible = _chartScroll.Visible = false;
    }
    public void Collapse()
    {
        _fold.Disabled = true; _prev.Visible = _next.Visible = _chartScroll.Visible = false;
        if (_motion.Collapse(Profile) == SongCardMotion.CollapseOutcome.Completed) { ClearNow(); Folded?.Invoke(); }
        else SetProcess(_motion.Animating);
    }
    private void ApplyHeight() { CustomMinimumSize = new(0, _motion.ShownHeight); Size = new(Size.X, _motion.ShownHeight); }
    public override void _Process(double delta)
    {
        var f = _motion.Advance(delta, Profile, _generation);
        if (f.CollapseLanded) { ClearNow(); Folded?.Invoke(); return; }
        if (f.HeightChanged) ApplyHeight(); if (f.TextReplaced) ApplyText();
        if (f.TextAlphaChanged) _artist.Modulate = _title.Modulate = _mapper.Modulate = new Color(1, 1, 1, _motion.TextAlpha);
        if (f.CoverAlphaChanged) _cover.Modulate = new Color(1, 1, 1, _motion.CoverAlpha);
        SetProcess(_motion.Animating);
    }
    public override void _ExitTree() => ClearCover();
}
