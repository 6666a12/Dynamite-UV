using Godot;
using DuxCommunity.Game;
using DuxCommunity.Ui;

namespace DuxCommunity.Ui;

/// <summary>
/// 选曲界面（样式稿 #select）：左详情（封面/曲名/谱师/时长/Note）+ 右曲库列表
/// + 底部难度循环按钮（最低档起，点击升档，跳过缺失难度）+ 最佳成绩条 + START。
/// </summary>
public partial class SongSelect : Node2D
{
    private int _packIdx;
    private int _diffIdx;
    private readonly List<SongRow> _rows = new();

    // 详情区
    private TextureRect _cover = null!;
    private ColorRect _coverFallback = null!;
    private Label _title = null!;
    private Label _artist = null!;
    private Label _meta = null!;
    // 底部
    private CutButton _diffBtn = null!;
    private Label _bestLabel = null!;
    private Label _bestGrade = null!;
    private CutButton _startBtn = null!;

    public override void _Ready()
    {
        GameSession.EnsureInit();

        AddChild(new NeonBackground { Size = new Vector2(1920, 1080) });

        BuildTopBar();
        BuildDetail();
        BuildList();
        BuildBottomBar();

        // 从结算/游玩返回时恢复上次选择
        if (GameSession.SelectedPack != null)
        {
            var i = GameSession.Packs.IndexOf(GameSession.SelectedPack);
            if (i >= 0) _packIdx = i;
            if (GameSession.SelectedDiff != null)
            {
                var d = GameSession.Packs[_packIdx].Charts.IndexOf(GameSession.SelectedDiff);
                if (d >= 0) _diffIdx = d;
            }
        }
        RefreshAll();
    }

    private void BuildTopBar()
    {
        var bar = new ColorRect
        {
            Color = new Color(0.04f, 0.06f, 0.12f, 0.9f),
            Size = new Vector2(1920, 76),
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        AddChild(bar);
        AddLine(new Vector2(0, 76), new Vector2(1920, 2));

        var logo = new Label { Position = new Vector2(66, 16), Text = "DUX·Community" };
        logo.AddThemeFontOverride("font", UiFonts.Tech);
        logo.AddThemeFontSizeOverride("font_size", 34);
        logo.AddThemeColorOverride("font_color", UiFonts.Text);
        AddChild(logo);

        var back = new CutButton
        {
            Position = new Vector2(1736, 12),
            Size = new Vector2(140, 52),
            Text = "返回",
            FontSize = 22,
        };
        back.Pressed += () => GetTree().ChangeSceneToFile("res://scenes/main.tscn");
        AddChild(back);
    }

    private void BuildDetail()
    {
        AddChild(new CutPanel { Position = new Vector2(40, 112), Size = new Vector2(480, 828) });

        _coverFallback = new ColorRect
        {
            Position = new Vector2(80, 152),
            Size = new Vector2(400, 311),
            Color = new Color(0.12f, 0.16f, 0.30f),
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        AddChild(_coverFallback);

        _cover = new TextureRect
        {
            Position = new Vector2(80, 152),
            Size = new Vector2(400, 311),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        AddChild(_cover);

        _title = new Label { Position = new Vector2(64, 500), Size = new Vector2(420, 56) };
        _title.AddThemeFontOverride("font", UiFonts.Cjk);
        _title.AddThemeFontSizeOverride("font_size", 46);
        _title.AddThemeColorOverride("font_color", UiFonts.Text);
        AddChild(_title);

        _artist = new Label { Position = new Vector2(64, 566), Size = new Vector2(420, 32) };
        _artist.AddThemeFontSizeOverride("font_size", 22);
        _artist.AddThemeColorOverride("font_color", UiFonts.Dim);
        AddChild(_artist);

        _meta = new Label { Position = new Vector2(64, 616), Size = new Vector2(420, 34) };
        _meta.AddThemeFontOverride("font", UiFonts.Tech);
        _meta.AddThemeFontSizeOverride("font_size", 24);
        _meta.AddThemeColorOverride("font_color", UiFonts.Text);
        AddChild(_meta);
    }

    private void BuildList()
    {
        AddChild(new CutPanel { Position = new Vector2(560, 112), Size = new Vector2(1320, 828) });

        var hdr = new Label
        {
            Position = new Vector2(590, 132),
            Size = new Vector2(1260, 30),
            Text = $"SONG LIBRARY · {GameSession.Packs.Count} CHARTS",
        };
        hdr.AddThemeFontOverride("font", UiFonts.Tech);
        hdr.AddThemeFontSizeOverride("font_size", 20);
        hdr.AddThemeColorOverride("font_color", UiFonts.Dim);
        AddChild(hdr);

        for (var i = 0; i < GameSession.Packs.Count; i++)
        {
            var idx = i;
            var p = GameSession.Packs[i];
            var row = new SongRow
            {
                Position = new Vector2(590, 180 + i * 118),
                Size = new Vector2(1280, 96),
                Pack = p,
            };
            row.Pressed += () => { _packIdx = idx; _diffIdx = 0; RefreshAll(); };
            _rows.Add(row);
            AddChild(row);
        }

        if (GameSession.Packs.Count == 0)
        {
            var empty = new Label
            {
                Position = new Vector2(590, 200),
                Size = new Vector2(1260, 40),
                Text = "曲库为空：把谱面包放进 user://charts/ 或 res://testdata/packs/",
            };
            empty.AddThemeFontSizeOverride("font_size", 24);
            empty.AddThemeColorOverride("font_color", UiFonts.Dim);
            AddChild(empty);
        }
    }

    private void BuildBottomBar()
    {
        _diffBtn = new CutButton
        {
            Position = new Vector2(40, 966),
            Size = new Vector2(300, 88),
            StyleKind = CutButton.ButtonStyle.Solid,
            SubText = "点击切换难度 ▲",
            FontSize = 30,
            TechFont = true,
        };
        _diffBtn.Pressed += CycleDiff;
        AddChild(_diffBtn);

        AddChild(new CutPanel { Position = new Vector2(360, 966), Size = new Vector2(1120, 88), Cut = 10 });

        _bestLabel = new Label { Position = new Vector2(390, 992), Size = new Vector2(850, 40) };
        _bestLabel.AddThemeFontOverride("font", UiFonts.Tech);
        _bestLabel.AddThemeFontSizeOverride("font_size", 28);
        _bestLabel.AddThemeColorOverride("font_color", UiFonts.Dim);
        AddChild(_bestLabel);

        _bestGrade = new Label
        {
            Position = new Vector2(1310, 976),
            Size = new Vector2(140, 68),
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        _bestGrade.AddThemeFontOverride("font", UiFonts.Tech);
        _bestGrade.AddThemeFontSizeOverride("font_size", 48);
        AddChild(_bestGrade);

        _startBtn = new CutButton
        {
            Position = new Vector2(1500, 966),
            Size = new Vector2(380, 88),
            Text = "START ▶",
            StyleKind = CutButton.ButtonStyle.Solid,
            FontSize = 34,
            TechFont = true,
        };
        _startBtn.Pressed += StartPlay;
        AddChild(_startBtn);
    }

    private void CycleDiff()
    {
        var pack = CurrentPack;
        if (pack == null || pack.Charts.Count == 0)
            return;
        _diffIdx = (_diffIdx + 1) % pack.Charts.Count;
        RefreshAll();
    }

    private void StartPlay()
    {
        var pack = CurrentPack;
        if (pack == null || pack.Charts.Count == 0)
            return;
        GameSession.SelectedPack = pack;
        GameSession.SelectedDiff = pack.Charts[_diffIdx];
        GetTree().ChangeSceneToFile("res://scenes/gameplay.tscn");
    }

    private ChartPack? CurrentPack =>
        GameSession.Packs.Count > 0 ? GameSession.Packs[Mathf.Clamp(_packIdx, 0, GameSession.Packs.Count - 1)] : null;

    private void RefreshAll()
    {
        var pack = CurrentPack;
        for (var i = 0; i < _rows.Count; i++)
            _rows[i].Selected = i == _packIdx;

        if (pack == null || pack.Charts.Count == 0)
        {
            _title.Text = "—";
            _artist.Text = "";
            _meta.Text = "";
            _cover.Texture = null;
            _diffBtn.Text = "无可用谱面";
            _diffBtn.Disabled = true;
            _startBtn.Disabled = true;
            _bestLabel.Text = "";
            _bestGrade.Text = "";
            return;
        }

        _diffBtn.Disabled = false;
        _startBtn.Disabled = false;
        _diffIdx = Mathf.Clamp(_diffIdx, 0, pack.Charts.Count - 1);
        var diff = pack.Charts[_diffIdx];

        _title.Text = pack.Title;
        _artist.Text = $"{pack.Artist} · 谱师: {pack.Charter}";
        var dur = pack.LoadDurationSec(diff);
        _meta.Text = $"时长 {(int)dur / 60}:{(int)dur % 60:D2}    Note {pack.LoadNoteCount(diff):N0}";

        _cover.Texture = pack.CoverPath is { } cp ? Res.LoadTexture(cp) : null;

        _diffBtn.Text = diff.Level > 0
            ? $"{UiFonts.DiffName(diff.Diff)} · Lv {diff.Level}"
            : UiFonts.DiffName(diff.Diff);

        var rec = GameSession.Scores.Get(pack.Id, diff.Diff);
        if (rec != null)
        {
            _bestLabel.Text = $"BEST {rec.Score:N0}   CLEAR {rec.Acc:F2}%";
            _bestGrade.Text = rec.Grade;
            _bestGrade.AddThemeColorOverride("font_color", GradeColor(rec.Grade));
        }
        else
        {
            _bestLabel.Text = "BEST —   尚无成绩";
            _bestGrade.Text = "";
        }
    }

    private static Color GradeColor(string grade) => grade switch
    {
        "Ω" => UiFonts.Pink,
        "S" => UiFonts.Cyan,
        "A" => UiFonts.Hard,
        "B" => UiFonts.Normal,
        _ => UiFonts.Dim,
    };

    private void AddLine(Vector2 pos, Vector2 size)
    {
        AddChild(new ColorRect
        {
            Color = UiFonts.Line,
            Position = pos,
            Size = size,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        });
    }
}
