using Godot;
using DynamiteUniverse.Ui;
using DynamiteUniverse.Shared.Chart;

namespace DynamiteUniverse.Game;

internal readonly record struct GameplayHudSnapshot(int Score, double LiveAcc,
    int CountPerfect, int CountGreat, int CountGood, int CountMiss, int MaxCombo,
    int Combo, double Time, double EndTime);

/// <summary>Owns HUD controls, text caches and judgement flashes; receives display values only.</summary>
internal sealed class GameplayHud
{
    private const float CenterTrackX = GameplayStageGeometry.CenterTrackX;
    private const float CenterLineY = GameplayStageGeometry.CenterLineY;
    private readonly Node2D _hudRoot;
    private readonly Node2D _topHudRoot;

    internal GameplayHud(Node2D root, Action pauseRequested, float stageOffsetY, float topOffsetY)
    {
        _topHudRoot = new Node2D
        {
            Name = "TopHud",
            Position = new Vector2(0f, topOffsetY),
        };
        _hudRoot = new Node2D
        {
            Name = "StageHud",
            Position = new Vector2(0f, stageOffsetY),
        };
        root.AddChild(_topHudRoot);
        root.AddChild(_hudRoot);
        Build(pauseRequested);
    }
	private ColorRect _progressFill = null!;
	private Label _scoreLabel = null!;
	private Label _pillP = null!;
	private Label _pillGr = null!;
	private Label _pillGo = null!;
	private Label _pillM = null!;
	private Label _pillAcc = null!;
	private Label _pillMc = null!;
	private Label _comboLabel = null!;
	private Label _comboSub = null!;
	private Label _judgeFlash = null!;
	private CutButton _pauseBtn = null!;

	private void Build(Action pauseRequested)
	{
		// 原版风 HUD（2340 直出截图换算到 1440）：顶栏细青线框（中央缺口放暂停钮）、
		// 底线上的发光进度段、左下曲名+难度、右下分数、顶部 ACC、
		// 中央偏下 COMBO + 判定字。
		var frameCol = new Color(UiFonts.Cyan, 0.45f);
		AddHudLine(new Vector2(53, 64), new Vector2(773, 2), frameCol);
		AddHudLine(new Vector2(1093, 64), new Vector2(773, 2), frameCol);

		// 顶部中央：六个紧凑 pill；ACC 是得分/理论满分，不是 timing accuracy。
		_pillP = MakePill(461, UiFonts.Cyan);
		_pillGr = MakePill(629, UiFonts.Hard);
		_pillGo = MakePill(797, UiFonts.Casual);
		_pillM = MakePill(965, new Color(0.53f, 0.53f, 0.53f));
		_pillAcc = MakePill(1133, new Color(0.64f, 0.90f, 0.21f));
		_pillMc = MakePill(1301, UiFonts.Text);

		// 顶部 pill 行下方正中间：暂停按钮（用户拍板：放大，嵌在顶栏缺口）
		_pauseBtn = new CutButton
		{
			Position = new Vector2(900, 60),
			Size = new Vector2(120, 64),
			Text = "II",
			FontSize = 30,
			TechFont = true,
			ZIndex = 2,
		};
		_pauseBtn.Pressed += () => pauseRequested();
		_topHudRoot.AddChild(_pauseBtn);

		// 底线上的发光进度段（从中央向两侧生长）
		_hudRoot.AddChild(new ColorRect
		{
			Color = new Color(1, 1, 1, 0.045f),
			Position = new Vector2(493, CenterLineY - 2),
			Size = new Vector2(933, 3),
			MouseFilter = Control.MouseFilterEnum.Ignore,
			ZIndex = 2,
		});
		_progressFill = new ColorRect
		{
			Color = new Color(UiFonts.Cyan, 0.68f),
			Position = new Vector2(CenterTrackX, CenterLineY - 2),
			Size = new Vector2(0, 3),
			MouseFilter = Control.MouseFilterEnum.Ignore,
			ZIndex = 2,
		};
		_hudRoot.AddChild(_progressFill);

			// Left-bottom track identity: stable two-line plate with independent title and status.
			_titleTagPanel = new CutPanel
			{
				Position = new Vector2(52, 884),
				Size = new Vector2(612, 104),
				Cut = 16,
				Fill = new Color(0.018f, 0.035f, 0.082f, 0.88f),
				Border = new Color(UiFonts.Line, 0.82f),
				BorderWidth = 1.5f,
				MouseFilter = Control.MouseFilterEnum.Ignore,
				ZIndex = 2,
			};
			_hudRoot.AddChild(_titleTagPanel);
			_titleTagAccent = new ColorRect
			{
				Position = new Vector2(52, 884),
				Size = new Vector2(6, 104),
				Color = UiFonts.Cyan,
				MouseFilter = Control.MouseFilterEnum.Ignore,
				ZIndex = 3,
			};
			_hudRoot.AddChild(_titleTagAccent);
			_titleTag = new Label
			{
				Position = new Vector2(78, 896),
				Size = new Vector2(556, 42),
				TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis,
				MouseFilter = Control.MouseFilterEnum.Ignore,
				ZIndex = 3,
			};
			_titleTag.AddThemeFontOverride("font", UiFonts.Cjk);
			_titleTag.AddThemeFontSizeOverride("font_size", 27);
			_titleTag.AddThemeColorOverride("font_color", UiFonts.Text);
			_hudRoot.AddChild(_titleTag);
			_titleTagStatus = new Label
			{
				Position = new Vector2(78, 944),
				Size = new Vector2(556, 28),
				TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis,
				MouseFilter = Control.MouseFilterEnum.Ignore,
				ZIndex = 3,
			};
			_titleTagStatus.AddThemeFontOverride("font", UiFonts.TechBold);
			_titleTagStatus.AddThemeFontSizeOverride("font_size", 17);
			_hudRoot.AddChild(_titleTagStatus);

		// 右下：分数（ACC 收进顶部 pill 行）
		_scoreLabel = new Label
		{
			Position = new Vector2(1290, 879),
			Size = new Vector2(550, 74),
			HorizontalAlignment = HorizontalAlignment.Right,
			Text = "0",
			ZIndex = 2,
		};
		_scoreLabel.AddThemeFontOverride("font", UiFonts.TechBold);
		_scoreLabel.AddThemeFontSizeOverride("font_size", 58);
		_scoreLabel.AddThemeColorOverride("font_color", UiFonts.Text);
		_hudRoot.AddChild(_scoreLabel);

		// 判定反馈在线上方；Combo 放到判定线下方安全区（用户 2026-08-10 拍板）。
		_comboLabel = new Label
		{
			Position = new Vector2(693, 872),
			Size = new Vector2(533, 72),
			HorizontalAlignment = HorizontalAlignment.Center,
			Text = "",
			ZIndex = 2,
		};
		_comboLabel.AddThemeFontOverride("font", UiFonts.TechBold);
		_comboLabel.AddThemeFontSizeOverride("font_size", 62);
		_comboLabel.AddThemeColorOverride("font_color", UiFonts.Text);
		_hudRoot.AddChild(_comboLabel);
		_comboSub = new Label
		{
			Position = new Vector2(826.5f, 944),
			Size = new Vector2(267, 28),
			HorizontalAlignment = HorizontalAlignment.Center,
			Text = "",
			ZIndex = 2,
		};
		_comboSub.AddThemeFontOverride("font", UiFonts.Tech);
		_comboSub.AddThemeFontSizeOverride("font_size", 18);
		_comboSub.AddThemeColorOverride("font_color", UiFonts.Dim);
		_hudRoot.AddChild(_comboSub);

		_judgeFlash = new Label
		{
			Position = new Vector2(CenterTrackX - 150, 696),
			Size = new Vector2(300, 44),
			HorizontalAlignment = HorizontalAlignment.Center,
			Modulate = new Color(1, 1, 1, 0),
			ZIndex = 2,
		};
		_judgeFlash.AddThemeFontOverride("font", UiFonts.TechBold);
		_judgeFlash.AddThemeFontSizeOverride("font_size", 34);
		_judgeFlash.AddThemeColorOverride("font_color", UiFonts.Cyan);
		_hudRoot.AddChild(_judgeFlash);
	}

	private void AddHudLine(Vector2 pos, Vector2 size, Color color)
	{
		_topHudRoot.AddChild(new ColorRect
		{
			Color = color,
			Position = pos,
			Size = size,
			MouseFilter = Control.MouseFilterEnum.Ignore,
			ZIndex = 2,
		});
	}

	// 顶部 pill：切角暗底 + 居中彩色文字，返回值标签由 UpdateHud 刷新
	private Label MakePill(float x, Color color)
	{
		_topHudRoot.AddChild(new CutPanel
		{
			Position = new Vector2(x, 12),
			Size = new Vector2(158, 40),
			Cut = 12,
			Fill = new Color(0.03f, 0.05f, 0.10f, 0.72f),
			ZIndex = 2,
		});
		var label = new Label
		{
			Position = new Vector2(x, 17),
			Size = new Vector2(158, 32),
			HorizontalAlignment = HorizontalAlignment.Center,
			MouseFilter = Control.MouseFilterEnum.Ignore,
			ZIndex = 2,
		};
		label.AddThemeFontOverride("font", UiFonts.Tech);
		label.AddThemeFontSizeOverride("font_size", 16);
		label.AddThemeColorOverride("font_color", color);
		_topHudRoot.AddChild(label);
		return label;
	}

		private CutPanel _titleTagPanel = null!;
		private ColorRect _titleTagAccent = null!;
		private Label _titleTag = null!;
		private Label _titleTagStatus = null!;

		internal void RefreshTitleTag(GameplayRunContext run, bool auto)
		{
			var color = UiFonts.DiffColor(run.DifficultyColorKey);
			var display = run.DifficultyDisplay.ToUpperInvariant();
			var difficulty = run.DifficultyLevel is { } level
				? $"{display}   /   LEVEL {level}"
				: $"{display}   /   UNRATED";
			_titleTag.Text = run.SongTitle;
			_titleTagStatus.Text = auto ? $"{difficulty}   /   AUTO · 不计成绩" : difficulty;
			_titleTagStatus.AddThemeColorOverride("font_color", color);
			_titleTagAccent.Color = color;
			_titleTagPanel.Border = new Color(color, 0.56f);
		}

	private double _flashTimer;
	private const double JudgeFlashDuration = 0.45;

	// HUD 刷新缓存：Label.Text 赋值触发全文 reshaping，值不变时跳过写入与插值。
	private int _hudScore = -1;
	private int _hudPerfect = -1;
	private int _hudGreat = -1;
	private int _hudGood = -1;
	private int _hudMiss = -1;
	private int _hudMaxCombo = -1;
	private int _hudCombo = -1;
	private double _hudLiveAcc = -1.0;

	internal void FlashJudge(string text, bool enabled)
	{
		if (!enabled)
			return;
		_judgeFlash.Text = text;
		_judgeFlash.Modulate = Colors.White;
		_judgeFlash.Position = new Vector2(CenterTrackX - 150, 696);
		_flashTimer = JudgeFlashDuration;
	}

	internal void Update(GameplayHudSnapshot state, double delta)
	{
		if (_flashTimer > 0)
		{
			_flashTimer -= delta;
			if (_flashTimer <= 0)
				_judgeFlash.Modulate = new Color(1, 1, 1, 0);
			else
			{
				var progress = 1f - (float)(_flashTimer / JudgeFlashDuration);
				var alpha = Mathf.Clamp((float)(_flashTimer / 0.14), 0f, 1f);
				_judgeFlash.Position = new Vector2(CenterTrackX - 150, 696 - progress * 24f);
				_judgeFlash.Modulate = new Color(1, 1, 1, alpha);
			}
		}

		var score = state.Score;
		if (score != _hudScore)
		{
			_hudScore = score;
			_scoreLabel.Text = $"{score:N0}";
		}
		// 实时 ACC = 当前得分 / 已判定单元的理论满分（未判定不计入），并钳制在 0..100%。
		var liveAcc = state.LiveAcc;
		if (liveAcc != _hudLiveAcc)
		{
			_hudLiveAcc = liveAcc;
			_pillAcc.Text = $"ACC {liveAcc:F2}%";
		}
		if (state.CountPerfect != _hudPerfect)
		{
			_hudPerfect = state.CountPerfect;
			_pillP.Text = $"PERFECT {_hudPerfect}";
		}
		if (state.CountGreat != _hudGreat)
		{
			_hudGreat = state.CountGreat;
			_pillGr.Text = $"GREAT {_hudGreat}";
		}
		if (state.CountGood != _hudGood)
		{
			_hudGood = state.CountGood;
			_pillGo.Text = $"GOOD {_hudGood}";
		}
		if (state.CountMiss != _hudMiss)
		{
			_hudMiss = state.CountMiss;
			_pillM.Text = $"MISS {_hudMiss}";
		}
		if (state.MaxCombo != _hudMaxCombo)
		{
			_hudMaxCombo = state.MaxCombo;
			_pillMc.Text = $"M.COMBO {_hudMaxCombo}";
		}
		if (state.Combo != _hudCombo)
		{
			_hudCombo = state.Combo;
			_comboLabel.Text = _hudCombo > 1 ? $"{_hudCombo}" : "";
			_comboSub.Text = _hudCombo > 1 ? "COMBO" : "";
		}
		// 底线上的进度段：从中央向两侧生长
		var pw = 933f * Mathf.Clamp((float)(state.Time / Math.Max(0.01, state.EndTime)), 0f, 1f);
		_progressFill.Position = new Vector2(CenterTrackX - pw / 2f, CenterLineY - 2);
		_progressFill.Size = new Vector2(pw, 3);
	}
}
