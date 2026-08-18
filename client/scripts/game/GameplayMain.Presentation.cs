using Godot;
using DynamiteUniverse.Ui;
using DynamiteUniverse.Shared.Score;
using DynamiteUniverse.Shared.Judge;

namespace DynamiteUniverse.Game;

/// <summary>Procedural gameplay stage, HUD, pause and result presentation. GameplayMain owns the callbacks and score timing.</summary>
public partial class GameplayMain
{
	// ---- UI ----

	private void BuildStage()
	{
		_stageRoot = new Node2D { Name = "StageVisuals" };
		_noteRoot = new Node2D { Name = "NoteVisuals" };
		_hudRoot = new Node2D { Name = "GameplayHud", ZIndex = 20 };
		AddChild(_stageRoot);
		AddChild(_noteRoot);
		AddChild(_hudRoot);
		_stageRoot.AddChild(new GameplayBackdrop { ZIndex = -2 });

		// 判定线（实机为隐形线，此处用暗色细线示意）：三线构成 ∪ 形——
		// 底部横线贯通左右，与两侧竖线在两端相接；Mixer 粉条 + 中央光标。
		var lineCol = new Color(0.35f, 0.35f, 0.4f);
		AddLine(new Vector2(60, CenterLineY - 2), new Vector2(1800, 4), lineCol); // 底线延长超出侧线、接近屏边（原版如此）
		AddLine(new Vector2(LeftLineX - 2, 70), new Vector2(4, CenterLineY - 70), lineCol); // 顶栏下到线底（用户拍板延长）
		AddLine(new Vector2(RightLineX - 2, 70), new Vector2(4, CenterLineY - 70), lineCol);
		AddLine(new Vector2(CenterTrackX - 4, MixerBarY - 14), new Vector2(8, 28),
			new Color(0.5f, 0.5f, 1.0f));

		// Mixer 装饰 UI：粉色固定横条 + 中央光标（实机装饰元素，T6/T7 粉缎带
		// 现在是下落音符，由常规视图/连接体渲染）
		_mixerBar = new CutPanel
		{
			Position = new Vector2(MixerBarLeft, MixerBarY - 12),
			Size = new Vector2(MixerBarLen, 24),
			Cut = 4,
			Fill = new Color(NoteVisualSpec.MixerBar, 0.15f),
			Border = new Color(NoteVisualSpec.MixerBar, 0.68f),
			BorderWidth = 2f,
			MouseFilter = Control.MouseFilterEnum.Ignore,
		};
		_stageRoot.AddChild(_mixerBar);

		BuildHud();
		BuildPauseMenu();
		BuildResultLayer();
	}

	private void BuildHud()
	{
		// 原版风 HUD（2340 直出截图换算到 1440）：顶栏细青线框（中央缺口放暂停钮）、
		// 底线上的发光进度段、左下曲名+难度、右下分数、顶部 CLEAR、
		// 中央偏下 COMBO + 判定字。
		var frameCol = new Color(UiFonts.Cyan, 0.45f);
		AddHudLine(new Vector2(53, 64), new Vector2(773, 2), frameCol);
		AddHudLine(new Vector2(1093, 64), new Vector2(773, 2), frameCol);

		// 顶部中央：六个紧凑 pill；CLEAR 是得分/理论满分，不是 timing accuracy。
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
		_pauseBtn.Pressed += TogglePause;
		_hudRoot.AddChild(_pauseBtn);

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
			Position = new Vector2(960, CenterLineY - 2),
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

		// 右下：分数（CLEAR 收进顶部 pill 行）
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
		_hudRoot.AddChild(new ColorRect
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
		_hudRoot.AddChild(new CutPanel
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
		_hudRoot.AddChild(label);
		return label;
	}

		private CutPanel _titleTagPanel = null!;
		private ColorRect _titleTagAccent = null!;
		private Label _titleTag = null!;
		private Label _titleTagStatus = null!;

		private void RefreshTitleTag()
		{
			var color = UiFonts.DiffColor(_run.DifficultyColorKey);
			var display = _run.DifficultyDisplay.ToUpperInvariant();
			var difficulty = _run.DifficultyLevel is { } level
				? $"{display}   /   LEVEL {level}"
				: $"{display}   /   UNRATED";
			_titleTag.Text = _run.SongTitle;
			_titleTagStatus.Text = _auto ? $"{difficulty}   /   AUTO (F1)" : difficulty;
			_titleTagStatus.AddThemeColorOverride("font_color", color);
			_titleTagAccent.Color = color;
			_titleTagPanel.Border = new Color(color, 0.56f);
		}

	private void BuildPauseMenu()
	{
		_pauseLayer = new Control
		{
			Visible = false,
			ZIndex = 60,
			MouseFilter = Control.MouseFilterEnum.Stop,
		};
		AddChild(_pauseLayer);

		// Blocker/dim intentionally stays outside the animated content root so input is
		// intercepted over the full screen throughout pause entry and exit.
		_presentationPauseBlocker = new ColorRect
		{
			Color = new Color(0.02f, 0.03f, 0.07f, 0.72f),
			Size = new Vector2(1920, 1080),
			MouseFilter = Control.MouseFilterEnum.Stop,
		};
		_pauseLayer.AddChild(_presentationPauseBlocker);

		_presentationPauseContent = new Control
		{
			Name = "PauseContent",
			Size = new Vector2(1920, 1080),
			MouseFilter = Control.MouseFilterEnum.Ignore,
		};
		_pauseLayer.AddChild(_presentationPauseContent);

		_presentationPauseContent.AddChild(new CutPanel
		{
			Position = new Vector2(610, 300),
			Size = new Vector2(700, 480),
		});

		var title = new Label
		{
			Position = new Vector2(610, 340),
			Size = new Vector2(700, 70),
			Text = "PAUSED",
			HorizontalAlignment = HorizontalAlignment.Center,
		};
		title.AddThemeFontOverride("font", UiFonts.Tech);
		title.AddThemeFontSizeOverride("font_size", 52);
		title.AddThemeColorOverride("font_color", UiFonts.Text);
		_presentationPauseContent.AddChild(title);

		_presentationPauseResume = new CutButton
		{
			Position = new Vector2(770, 450),
			Size = new Vector2(380, 76),
			Text = "继续",
			StyleKind = CutButton.ButtonStyle.Solid,
			FontSize = 28,
		};
		_presentationPauseResume.Pressed += TogglePause;
		_presentationPauseContent.AddChild(_presentationPauseResume);

		_presentationPauseRetry = new CutButton
		{
			Position = new Vector2(770, 546),
			Size = new Vector2(380, 76),
			Text = "重开",
			FontSize = 28,
		};
		_presentationPauseRetry.Pressed += RestartFromPause;
		_presentationPauseContent.AddChild(_presentationPauseRetry);

		_presentationPauseQuit = new CutButton
		{
			Position = new Vector2(770, 642),
			Size = new Vector2(380, 76),
			Text = "返回选曲",
			FontSize = 28,
		};
		_presentationPauseQuit.Pressed += ExitToSelectFromPause;
		_presentationPauseContent.AddChild(_presentationPauseQuit);
	}

	private void BuildResultLayer()
	{
		_resultLayer = new Control
		{
			Visible = false,
			ZIndex = 100,
			MouseFilter = Control.MouseFilterEnum.Stop,
		};
		AddChild(_resultLayer);

		// Keep the fallback first and fully opaque. Presentation motion only targets the
		// content groups below; the result layer and background are never faded.
		_resultLayer.AddChild(new ColorRect
		{
			Color = new Color(0.018f, 0.026f, 0.060f),
			Size = new Vector2(1920, 1080),
			MouseFilter = Control.MouseFilterEnum.Ignore,
		});

		_resultLayer.AddChild(new CoverPlaceholder
		{
			Size = new Vector2(1920, 1080),
			Modulate = new Color(0.45f, 0.45f, 0.50f),
			MouseFilter = Control.MouseFilterEnum.Ignore,
		});

		// 封面拉伸铺满 + 压暗（样式稿为模糊，Godot 侧先用压暗近似）。
		_resultCover = new TextureRect
		{
			Position = Vector2.Zero,
			Size = new Vector2(1920, 1080),
			ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
			StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered,
			Modulate = new Color(0.34f, 0.34f, 0.38f),
			Visible = false,
			MouseFilter = Control.MouseFilterEnum.Ignore,
		};
		_resultLayer.AddChild(_resultCover);
		_resultLayer.AddChild(new ColorRect
		{
			Color = new Color(0.04f, 0.05f, 0.10f, 0.58f),
			Size = new Vector2(1920, 1080),
			MouseFilter = Control.MouseFilterEnum.Ignore,
		});

		_presentationResultContent = MakePresentationGroup("ResultContent");
		_resultLayer.AddChild(_presentationResultContent);
		_presentationResultPrelock = new ColorRect
		{
			Position = new Vector2(380, 240),
			Size = new Vector2(1160, 600),
			Color = new Color(0.02f, 0.035f, 0.08f, 0.96f),
			MouseFilter = Control.MouseFilterEnum.Ignore,
			Visible = false,
			ZIndex = 10,
		};
		_presentationResultContent.AddChild(_presentationResultPrelock);
		_presentationResultContent.AddChild(new CutPanel
		{
			Position = new Vector2(380, 240),
			Size = new Vector2(1160, 600),
			Fill = new Color(0.078f, 0.106f, 0.20f, 0.78f),
		});
		_presentationResultSignalScan = new ColorRect
		{
			Position = new Vector2(380, 240),
			Size = new Vector2(12, 600),
			Color = new Color(UiFonts.Cyan, 0.72f),
			MouseFilter = Control.MouseFilterEnum.Ignore,
			Visible = false,
		};
		_presentationResultContent.AddChild(_presentationResultSignalScan);

			_songLine = new Label
			{
				Position = new Vector2(450, 276),
				Size = new Vector2(1020, 34),
				TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis,
			};
			_songLine.AddThemeFontOverride("font", UiFonts.Cjk);
			_songLine.AddThemeFontSizeOverride("font_size", 24);
		_songLine.AddThemeColorOverride("font_color", UiFonts.Dim);
		_presentationResultContent.AddChild(_songLine);

		_presentationResultGradeGroup = MakePresentationGroup("ResultGrade");
		_presentationResultScoreGroup = MakePresentationGroup("ResultScore");
		_presentationResultStatsGroup = MakePresentationGroup("ResultStats");
		_presentationResultButtonsGroup = MakePresentationGroup("ResultButtons");
		_presentationResultContent.AddChild(_presentationResultGradeGroup);
		_presentationResultContent.AddChild(_presentationResultScoreGroup);
		_presentationResultContent.AddChild(_presentationResultStatsGroup);
		_presentationResultContent.AddChild(_presentationResultButtonsGroup);

		// Two offset echo layers sit behind the grade and are enabled only by Full motion.
		_presentationResultGradeEchoPink = new Label
		{
			Position = new Vector2(480, 330),
			Size = new Vector2(220, 180),
			PivotOffset = new Vector2(110, 90),
			MouseFilter = Control.MouseFilterEnum.Ignore,
		};
		_presentationResultGradeEchoPink.AddThemeFontOverride("font", UiFonts.TechBold);
		_presentationResultGradeEchoPink.AddThemeFontSizeOverride("font_size", 140);
		_presentationResultGradeGroup.AddChild(_presentationResultGradeEchoPink);

		_presentationResultGradeEcho = new Label
		{
			Position = new Vector2(480, 330),
			Size = new Vector2(220, 180),
			PivotOffset = new Vector2(110, 90),
			MouseFilter = Control.MouseFilterEnum.Ignore,
		};
		_presentationResultGradeEcho.AddThemeFontOverride("font", UiFonts.TechBold);
		_presentationResultGradeEcho.AddThemeFontSizeOverride("font_size", 140);
		_presentationResultGradeGroup.AddChild(_presentationResultGradeEcho);

		_gradeLabel = new Label
		{
			Position = new Vector2(480, 330),
			Size = new Vector2(220, 180),
			PivotOffset = new Vector2(110, 90),
		};
		_gradeLabel.AddThemeFontOverride("font", UiFonts.TechBold);
		_gradeLabel.AddThemeFontSizeOverride("font_size", 140);
		_gradeLabel.AddThemeColorOverride("font_color", UiFonts.Cyan);
		_presentationResultGradeGroup.AddChild(_gradeLabel);

		_newRecChip = new Control { Position = new Vector2(490, 530), Size = new Vector2(200, 40) };
		_newRecChip.AddChild(new CutPanel
		{
			Size = new Vector2(200, 40),
			Cut = 8,
			Fill = UiFonts.Pink,
			Border = UiFonts.Pink,
		});
		var chipText = new Label
		{
			Size = new Vector2(200, 40),
			Text = "NEW RECORD",
			HorizontalAlignment = HorizontalAlignment.Center,
			VerticalAlignment = VerticalAlignment.Center,
		};
		chipText.AddThemeFontOverride("font", UiFonts.Tech);
		chipText.AddThemeFontSizeOverride("font_size", 18);
		chipText.AddThemeColorOverride("font_color", new Color("1a0510"));
		_newRecChip.AddChild(chipText);
		_presentationResultGradeGroup.AddChild(_newRecChip);

		_resultScore = new Label { Position = new Vector2(750, 320), Size = new Vector2(720, 90) };
		_resultScore.AddThemeFontOverride("font", UiFonts.TechBold);
		_resultScore.AddThemeFontSizeOverride("font_size", 76);
		_resultScore.AddThemeColorOverride("font_color", UiFonts.Text);
		_presentationResultScoreGroup.AddChild(_resultScore);

		_resultAcc = new Label { Position = new Vector2(750, 424), Size = new Vector2(720, 36) };
		_resultAcc.AddThemeFontOverride("font", UiFonts.Tech);
		_resultAcc.AddThemeFontSizeOverride("font_size", 26);
		_resultAcc.AddThemeColorOverride("font_color", UiFonts.Dim);
		_presentationResultScoreGroup.AddChild(_resultAcc);

		// 判定分布条（4 段彩色，宽度按占比）
		const float barX = 750, barY = 486, barH = 14;
		_distP = DistBar(barX, barY, UiFonts.Cyan);
		_distGr = DistBar(barX, barY, UiFonts.Hard);
		_distGo = DistBar(barX, barY, UiFonts.Casual);
		_distM = DistBar(barX, barY, new Color(0.33f, 0.33f, 0.33f));

		_resultCountP = MakeResultStatCell(750, "P", UiFonts.Cyan);
		_resultCountGr = MakeResultStatCell(930, "GR", UiFonts.Hard);
		_resultCountGo = MakeResultStatCell(1110, "GD", UiFonts.Casual);
		_resultCountM = MakeResultStatCell(1290, "M", new Color(0.52f, 0.52f, 0.56f));

		_presentationResultBack = new CutButton
		{
			Position = new Vector2(450, 720),
			Size = new Vector2(280, 72),
			Text = "返回选曲",
			FontSize = 26,
		};
		_presentationResultBack.Pressed += ExitToSelectFromResult;
		_presentationResultButtonsGroup.AddChild(_presentationResultBack);

		_presentationResultNext = new CutButton
		{
			Position = new Vector2(750, 720),
			Size = new Vector2(280, 72),
			Text = "下一首",
			FontSize = 26,
		};
		_presentationResultNext.Pressed += NextSongFromResult;
		_presentationResultButtonsGroup.AddChild(_presentationResultNext);

		_presentationResultRetry = new CutButton
		{
			Position = new Vector2(1050, 720),
			Size = new Vector2(280, 72),
			Text = "再来一次",
			StyleKind = CutButton.ButtonStyle.Solid,
			FontSize = 26,
		};
		_presentationResultRetry.Pressed += RestartFromResult;
		_presentationResultButtonsGroup.AddChild(_presentationResultRetry);

		ColorRect DistBar(float x, float y, Color color)
		{
			var rect = new ColorRect
			{
				Color = color,
				Position = new Vector2(x, y),
				Size = new Vector2(0, barH),
				MouseFilter = Control.MouseFilterEnum.Ignore,
			};
			_presentationResultStatsGroup.AddChild(rect);
			return rect;
		}
	}

	private static Control MakePresentationGroup(string name) => new()
	{
		Name = name,
		Size = new Vector2(1920, 1080),
		MouseFilter = Control.MouseFilterEnum.Ignore,
	};

	private Label MakeResultStatCell(float x, string title, Color color)
	{
		_presentationResultStatsGroup.AddChild(new CutPanel
		{
			Position = new Vector2(x, 520),
			Size = new Vector2(160, 72),
			Cut = 10,
			Fill = new Color(0.025f, 0.04f, 0.09f, 0.84f),
			Border = new Color(color, 0.48f),
			BorderWidth = 1.5f,
		});
		var tag = new Label
		{
			Position = new Vector2(x + 14, 530),
			Size = new Vector2(42, 48),
			Text = title,
			VerticalAlignment = VerticalAlignment.Center,
		};
		tag.AddThemeFontOverride("font", UiFonts.TechBold);
		tag.AddThemeFontSizeOverride("font_size", 19);
		tag.AddThemeColorOverride("font_color", color);
		_presentationResultStatsGroup.AddChild(tag);

		var value = new Label
		{
			Position = new Vector2(x + 50, 530),
			Size = new Vector2(94, 48),
			HorizontalAlignment = HorizontalAlignment.Right,
			VerticalAlignment = VerticalAlignment.Center,
		};
		value.AddThemeFontOverride("font", UiFonts.TechBold);
		value.AddThemeFontSizeOverride("font_size", 24);
		value.AddThemeColorOverride("font_color", UiFonts.Text);
		_presentationResultStatsGroup.AddChild(value);
		return value;
	}

	private Label _songLine = null!;

	private static UiMotionProfile PresentationMotionProfile() =>
		UiMotionProfile.For(GameSession.Settings.MotionMode);

	private void PrepareGameplayEntryPresentation()
	{
		var motion = PresentationMotionProfile();
		if (!motion.IsAnimated || !TransitionDirector.IsAwaitingSceneReady)
		{
			ApplyGameplayEntryPresentation(1f);
			return;
		}

		_stageRoot.Modulate = new Color(1f, 1f, 1f, 0f);
		_noteRoot.Modulate = new Color(1f, 1f, 1f, 0f);
		_hudRoot.Modulate = new Color(1f, 1f, 1f, 0f);
	}

	private void ApplyGameplayEntryPresentation(float progress)
	{
		var stage = UiEase.Enter(Mathf.Clamp(progress / 0.62f, 0f, 1f));
		var notes = UiEase.Enter(Mathf.Clamp((progress - 0.14f) / 0.68f, 0f, 1f));
		var hud = UiEase.Enter(Mathf.Clamp((progress - 0.30f) / 0.70f, 0f, 1f));
		_stageRoot.Modulate = new Color(1f, 1f, 1f, stage);
		_noteRoot.Modulate = new Color(1f, 1f, 1f, notes);
		_hudRoot.Modulate = new Color(1f, 1f, 1f, hud);
	}

	private void TogglePause()
	{
		if (_finished || _presentationPauseMotionInFlight)
			return;

		if (!_paused)
			BeginPauseEntry(PresentationMotionProfile());
		else
			BeginPauseExit(PresentationMotionProfile());
	}

	private void BeginPauseEntry(UiMotionProfile motion)
	{
		// Gameplay must stop before either visual begins; clearing contacts first keeps
		// pause from leaving held pointers alive across the menu.
		_paused = true;
		ClearPointerState();
		_playback.Pause();
		_presentationPauseMotionInFlight = motion.IsAnimated;
		SetPauseActionsEnabled(!motion.IsAnimated);
		_pauseLayer.Visible = true;
		_presentationPauseBlocker.Modulate = new Color(1f, 1f, 1f, motion.IsAnimated ? 0f : 1f);
		_presentationPauseContent.Modulate = new Color(1f, 1f, 1f, motion.IsAnimated ? 0f : 1f);
		_presentationPauseContent.Position = motion.IsAnimated && motion.AllowDirectionalMotion
			? new Vector2(0f, motion.ContentShift)
			: Vector2.Zero;

		if (!motion.IsAnimated)
			return;

		_presentationPauseTween?.Kill();
		_presentationPauseTween = CreateTween()
			.SetTrans(Tween.TransitionType.Expo)
			.SetEase(Tween.EaseType.Out);
		_presentationPauseTween.TweenProperty(
			_presentationPauseBlocker, "modulate:a", 1f, motion.PanelDuration);
		_presentationPauseTween.Parallel().TweenProperty(
			_presentationPauseContent, "modulate:a", 1f, motion.PanelDuration);
		_presentationPauseTween.Parallel().TweenProperty(
			_presentationPauseContent, "position", Vector2.Zero, motion.PanelDuration);
		_presentationPauseTween.TweenCallback(Callable.From(FinishPauseEntry));
	}

	private void FinishPauseEntry()
	{
		_presentationPauseTween = null;
		_presentationPauseMotionInFlight = false;
		SetPauseActionsEnabled(true);
	}

	private void BeginPauseExit(UiMotionProfile motion)
	{
		// _paused and playback stay stopped until FinishPauseExit, including Reduced.
		if (!motion.IsAnimated)
		{
			FinishPauseExit();
			return;
		}

		_presentationPauseMotionInFlight = true;
		SetPauseActionsEnabled(false);
		var targetPosition = motion.AllowDirectionalMotion
			? new Vector2(0f, motion.ContentShift)
			: Vector2.Zero;
		_presentationPauseTween?.Kill();
		_presentationPauseTween = CreateTween()
			.SetTrans(Tween.TransitionType.Cubic)
			.SetEase(Tween.EaseType.In);
		_presentationPauseTween.TweenProperty(
			_presentationPauseBlocker, "modulate:a", 0f, motion.PanelDuration);
		_presentationPauseTween.Parallel().TweenProperty(
			_presentationPauseContent, "modulate:a", 0f, motion.PanelDuration);
		_presentationPauseTween.Parallel().TweenProperty(
			_presentationPauseContent, "position", targetPosition, motion.PanelDuration);
		_presentationPauseTween.TweenCallback(Callable.From(FinishPauseExit));
	}

	private void FinishPauseExit()
	{
		_presentationPauseTween = null;
		_pauseLayer.Visible = false;
		_presentationPauseBlocker.Modulate = Colors.White;
		_presentationPauseContent.Modulate = Colors.White;
		_presentationPauseContent.Position = Vector2.Zero;
		_presentationPauseMotionInFlight = false;
		_paused = false;
		SetPauseActionsEnabled(true);
		_playback.Resume();
	}

	private void SetPauseActionsEnabled(bool enabled)
	{
		_presentationPauseResume.Disabled = !enabled;
		_presentationPauseRetry.Disabled = !enabled;
		_presentationPauseQuit.Disabled = !enabled;
	}

	private void RestartFromPause()
	{
		if (!_presentationPauseMotionInFlight)
			Restart();
	}

	private void ExitToSelectFromPause()
	{
		if (!_presentationPauseMotionInFlight)
			ExitToSelect();
	}

	private void ClearPointerState()
	{
		_pendingPointerInputs.Clear();
		_activePointers.Clear();
		_frameTouchSamples.Clear();
		_ignoredTouchIds.Clear();
		_inputTimeGroupGate.Reset();
	}

		private void Restart()
		{
			_playback.Pause();
			_paused = false;
			var texture = CoverTextureCache.Current(_run.CoverPath) ??
				CoverTextureCache.Load(_run.CoverPath);
			TransitionDirector.Navigate(
				UiRoutes.Gameplay,
				TransitionKind.Restart,
				"RE-SYNC",
				_run.DifficultyText,
				relay: new TrackRelayPresentation(
					_run.SongTitle,
					_run.DifficultyText,
					texture,
					RelaySource.Restart));
		}

		private void ExitToSelect()
		{
			_playback.Pause();
			_paused = false;
			TransitionDirector.Navigate(UiRoutes.SongSelect, TransitionKind.Back);
		}

		private void NextSong()
		{
			if (!GameSession.SelectNextSong())
				return;
			_playback.Pause();
			var selection = GameSession.CurrentSelection;
			var texture = CoverTextureCache.Load(selection?.Pack.CoverPath);
			var level = selection?.Diff.Level is { } value ? $"Lv {value}" : "UNRATED";
			var difficulty = selection is null
				? "NEXT TRACK"
				: $"{selection.Diff.Display.ToUpperInvariant()} · {level}";
			TransitionDirector.Navigate(
				UiRoutes.Gameplay,
				TransitionKind.Gameplay,
				"PREPARING NEXT SESSION",
				selection is null ? null : $"{selection.Pack.Title} · {difficulty}",
				relay: new TrackRelayPresentation(
					selection?.Pack.Title ?? "NEXT TRACK",
					difficulty,
					texture,
					RelaySource.NextSong));
		}

	private double _flashTimer;
	private const double JudgeFlashDuration = 0.45;

	private void FlashJudge(string text)
	{
		_judgeFlash.Text = text;
		_judgeFlash.Modulate = Colors.White;
		_judgeFlash.Position = new Vector2(CenterTrackX - 150, 696);
		_flashTimer = JudgeFlashDuration;
	}

	private void UpdateHud(double t)
	{
		if (_flashTimer > 0)
		{
			_flashTimer -= GetProcessDeltaTime();
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

		_scoreLabel.Text = $"{_engine.NormalizedScore(_plan.TheoreticalMax):N0}";
		// 实时 CLEAR = 当前得分 / 已判定单元的理论满分（未判定不计入）。
		var maxSoFar = 0;
		foreach (var u in _plan.Units)
			if (u.Judged)
				maxSoFar += JudgeEngine.ScoreDelta(u.Category, JudgeGrade.Prefect);
		var liveClear = maxSoFar > 0 ? 100.0 * _engine.Score / maxSoFar : 100.0;
		_pillP.Text = $"PERFECT {_engine.CountPrefect}";
		_pillGr.Text = $"GREAT {_engine.CountGreat}";
		_pillGo.Text = $"GOOD {_engine.CountGood}";
		_pillM.Text = $"MISS {_engine.CountMiss}";
		_pillAcc.Text = $"CLEAR {liveClear:F2}%";
		_pillMc.Text = $"M.COMBO {_engine.MaxCombo}";
		_comboLabel.Text = _engine.Combo > 1 ? $"{_engine.Combo}" : "";
		_comboSub.Text = _engine.Combo > 1 ? "COMBO" : "";
		// 底线上的进度段：从中央向两侧生长
		var pw = 933f * Mathf.Clamp((float)(t / Math.Max(0.01, _plan.EndTime)), 0f, 1f);
		_progressFill.Position = new Vector2(960 - pw / 2f, CenterLineY - 2);
		_progressFill.Size = new Vector2(pw, 3);
	}

	private void ShowResults()
	{
		_finished = true;
		_playback.Pause();
		var pct = _engine.Percent(_plan.TheoreticalMax);
		var normalizedScore = _engine.NormalizedScore(_plan.TheoreticalMax);

		// 成绩写回（AUTO 演示不计入成绩库）
		var isNewRecord = false;
		var grade = ScoreStore.GradeOf(pct);
		if (!_auto)
		{
			var record = new ScoreRecord
			{
				Score = normalizedScore,
				Acc = pct,
				MaxCombo = _engine.MaxCombo,
				Grade = grade,
				Perfect = _engine.CountPrefect,
				Great = _engine.CountGreat,
				Good = _engine.CountGood,
				Miss = _engine.CountMiss,
			};
			isNewRecord = _loaded.TryGetScoreIdentity(out var identity)
				? GameSession.Scores.TryUpdate(identity, record)
				: GameSession.Scores.TryUpdate(_run.PackId, _run.LegacyScoreKey, record);
		}

		// 结算面板（样式稿 #result）
		var resultTexture = CoverTextureCache.Current(_run.CoverPath) ??
			CoverTextureCache.Load(_run.CoverPath);
		if (resultTexture is { } tex)
		{
			_resultCover.Texture = tex;
			_resultCover.Visible = true;
		}
		else
		{
			_resultCover.Texture = null;
			_resultCover.Visible = false;
		}
		var resultDiff = _run.DifficultyLevel is { } level
				? $"{_run.DifficultyDisplay.ToUpperInvariant()} {level}"
				: $"{_run.DifficultyDisplay.ToUpperInvariant()} UNRATED";

		_songLine.Text = $"RESULT · {_run.SongTitle} · {resultDiff}" +
						 (_auto ? " · AUTO（不计成绩）" : "");
		_gradeLabel.Text = grade;
		_gradeLabel.AddThemeColorOverride("font_color", UiFonts.GradeColor(grade));
		_newRecChip.Visible = isNewRecord;
		_resultScore.Text = $"{normalizedScore:N0}";
		_resultAcc.Text = $"CLEAR {pct:F2}% · MAX COMBO {_engine.MaxCombo:N0} / {_plan.HeadlineUnitCount:N0}";

		// 判定分布条
		var total = Math.Max(1, _engine.CountPrefect + _engine.CountGreat +
			_engine.CountGood + _engine.CountMiss);
		const float barX = 750, barW = 720;
		var wP = barW * _engine.CountPrefect / total;
		var wGr = barW * _engine.CountGreat / total;
		var wGo = barW * _engine.CountGood / total;
		var wM = barW - wP - wGr - wGo;
		_distP.Position = new Vector2(barX, _distP.Position.Y);
		_distP.Size = new Vector2(wP, _distP.Size.Y);
		_distGr.Position = new Vector2(barX + wP, _distGr.Position.Y);
		_distGr.Size = new Vector2(wGr, _distGr.Size.Y);
		_distGo.Position = new Vector2(barX + wP + wGr, _distGo.Position.Y);
		_distGo.Size = new Vector2(wGo, _distGo.Size.Y);
		_distM.Position = new Vector2(barX + wP + wGr + wGo, _distM.Position.Y);
		_distM.Size = new Vector2(wM, _distM.Size.Y);

		_resultCountP.Text = $"{_engine.CountPrefect:N0}";
		_resultCountGr.Text = $"{_engine.CountGreat:N0}";
		_resultCountGo.Text = $"{_engine.CountGood:N0}";
		_resultCountM.Text = $"{_engine.CountMiss:N0}";

		_stageRoot.Visible = false;
		_noteRoot.Visible = false;
		_hudRoot.Visible = false;
		_presentationPauseTween?.Kill();
		_presentationPauseTween = null;
		_presentationPauseMotionInFlight = false;
		_pauseLayer.Visible = false;
		_resultLayer.Visible = true;
		PlayResultPresentation(PresentationMotionProfile());

		GD.Print($"=== RESULT === {_run.SongTitle} [{_run.LegacyScoreKey} Lv{_run.DifficultyLevel}] " +
				 $"score={normalizedScore} rawScore={_engine.Score} clear={pct:F2}% grade={grade} " +
				 $"P={_engine.CountPrefect} Gr={_engine.CountGreat} " +
				 $"Gd={_engine.CountGood} M={_engine.CountMiss} " +
				 $"maxCombo={_engine.MaxCombo} newRecord={isNewRecord}");
	}

	private void PlayResultPresentation(UiMotionProfile motion)
	{
		_presentationResultTween?.Kill();
		_presentationResultTween = null;
		_presentationResultMotionInFlight = motion.IsAnimated;
		SetResultActionsEnabled(!motion.IsAnimated);
		ResetResultPresentation(motion);

		if (!motion.IsAnimated)
		{
			FinishResultPresentation();
			return;
		}

		var duration = motion.ResultDuration;
		var stagger = motion.AllowStagger ? motion.Stagger : 0.0;
		var scoreDelay = stagger;
		var statsDelay = stagger * 2.0;
		var buttonsDelay = stagger * 3.0;
		var itemDuration = Math.Max(0.01, duration - buttonsDelay);
		var prelockDuration = motion.Mode == UiMotionMode.Reduced
			? Math.Min(0.08, duration * 0.30)
			: Math.Min(0.16, duration * 0.18);
		_presentationResultPrelock.Visible = true;
		_presentationResultPrelock.Modulate = Colors.White;
		_presentationResultTween = CreateTween()
			.SetParallel()
			.SetTrans(Tween.TransitionType.Expo)
			.SetEase(Tween.EaseType.Out);
		TweenResultGroup(_presentationResultGradeGroup, 0.0, itemDuration);
		TweenResultGroup(_presentationResultScoreGroup, scoreDelay, itemDuration);
		TweenResultGroup(_presentationResultStatsGroup, statsDelay, itemDuration);
		TweenResultGroup(_presentationResultButtonsGroup, buttonsDelay, itemDuration);
		_presentationResultTween.TweenProperty(
			_presentationResultPrelock, "modulate:a", 0f, prelockDuration);

		if (motion.AllowDirectionalMotion)
		{
			_presentationResultSignalScan.Visible = true;
			_presentationResultSignalScan.Position = new Vector2(380, 240);
			_presentationResultSignalScan.Modulate = Colors.White;
			_presentationResultTween.TweenProperty(
				_presentationResultSignalScan, "position:x", 1528f, duration * 0.46);
			_presentationResultTween.TweenProperty(
				_presentationResultSignalScan, "modulate:a", 0f, duration * 0.46);
		}

		if (motion.AllowEcho)
		{
			_presentationResultGradeEcho.AddThemeColorOverride(
				"font_color", new Color(UiFonts.Cyan, 0.44f));
			_presentationResultGradeEchoPink.AddThemeColorOverride(
				"font_color", new Color(UiFonts.Pink, 0.36f));
			var echoDelay = Math.Min(0.10, duration * 0.12);
			_presentationResultTween.TweenProperty(
				_presentationResultGradeEcho, "modulate:a", 0f, itemDuration)
				.SetDelay(echoDelay);
			_presentationResultTween.TweenProperty(
				_presentationResultGradeEcho, "scale", new Vector2(1.24f, 1.24f), itemDuration)
				.SetDelay(echoDelay);
			_presentationResultTween.TweenProperty(
				_presentationResultGradeEchoPink, "modulate:a", 0f, itemDuration)
				.SetDelay(echoDelay + 0.04);
			_presentationResultTween.TweenProperty(
				_presentationResultGradeEchoPink, "scale", new Vector2(1.34f, 1.34f), itemDuration)
				.SetDelay(echoDelay + 0.04);
			_presentationResultTween.TweenProperty(
				_gradeLabel, "scale", new Vector2(1.08f, 1.08f), itemDuration * 0.34);
			_presentationResultTween.TweenProperty(
				_gradeLabel, "scale", Vector2.One, itemDuration * 0.66)
				.SetDelay(itemDuration * 0.34);
		}

		// With parallel mode the chain begins after the longest delayed branch.
		_presentationResultTween.Chain().TweenCallback(
			Callable.From(FinishResultPresentation));

		void TweenResultGroup(Control group, double delay, double tweenDuration)
		{
			_presentationResultTween.TweenProperty(group, "modulate:a", 1f, tweenDuration)
				.SetDelay(delay);
			_presentationResultTween.TweenProperty(group, "position", Vector2.Zero, tweenDuration)
				.SetDelay(delay);
		}
	}

	private void ResetResultPresentation(UiMotionProfile motion)
	{
		_presentationResultContent.Modulate = Colors.White;
		_presentationResultContent.Position = Vector2.Zero;
		var shift = motion.AllowDirectionalMotion ? motion.RelayShift : 0f;
		ResetResultGroup(_presentationResultGradeGroup, shift > 0f ? new Vector2(-shift, 0f) : Vector2.Zero);
		ResetResultGroup(_presentationResultScoreGroup, shift > 0f ? new Vector2(shift, 0f) : Vector2.Zero);
		ResetResultGroup(_presentationResultStatsGroup, shift > 0f ? new Vector2(0f, shift) : Vector2.Zero);
		ResetResultGroup(_presentationResultButtonsGroup, shift > 0f ? new Vector2(0f, shift) : Vector2.Zero);

		_presentationResultGradeEcho.Text = _gradeLabel.Text;
		_presentationResultGradeEcho.Visible = motion.AllowEcho;
		_presentationResultGradeEcho.Modulate = motion.AllowEcho
			? Colors.White
			: new Color(1f, 1f, 1f, 0f);
		_presentationResultGradeEcho.Scale = motion.AllowEcho
			? new Vector2(0.88f, 0.88f)
			: Vector2.One;
		_presentationResultGradeEchoPink.Text = _gradeLabel.Text;
		_presentationResultGradeEchoPink.Visible = motion.AllowEcho;
		_presentationResultGradeEchoPink.Modulate = motion.AllowEcho
			? Colors.White
			: new Color(1f, 1f, 1f, 0f);
		_presentationResultGradeEchoPink.Scale = motion.AllowEcho
			? new Vector2(0.82f, 0.82f)
			: Vector2.One;
		_presentationResultPrelock.Visible = motion.IsAnimated;
		_presentationResultPrelock.Modulate = Colors.White;
		_presentationResultSignalScan.Visible = false;
		_presentationResultSignalScan.Position = new Vector2(380, 240);
		_presentationResultSignalScan.Modulate = Colors.White;
		_gradeLabel.Scale = motion.AllowEcho ? new Vector2(0.72f, 0.72f) : Vector2.One;

		void ResetResultGroup(Control group, Vector2 position)
		{
			group.Position = position;
			group.Modulate = motion.IsAnimated
				? new Color(1f, 1f, 1f, 0f)
				: Colors.White;
		}
	}

	private void FinishResultPresentation()
	{
		_presentationResultTween = null;
		_presentationResultGradeGroup.Position = Vector2.Zero;
		_presentationResultScoreGroup.Position = Vector2.Zero;
		_presentationResultStatsGroup.Position = Vector2.Zero;
		_presentationResultButtonsGroup.Position = Vector2.Zero;
		_presentationResultGradeGroup.Modulate = Colors.White;
		_presentationResultScoreGroup.Modulate = Colors.White;
		_presentationResultStatsGroup.Modulate = Colors.White;
		_presentationResultButtonsGroup.Modulate = Colors.White;
		_presentationResultGradeEcho.Modulate = new Color(1f, 1f, 1f, 0f);
		_presentationResultGradeEchoPink.Modulate = new Color(1f, 1f, 1f, 0f);
		_presentationResultPrelock.Visible = false;
		_presentationResultSignalScan.Visible = false;
		_gradeLabel.Scale = Vector2.One;
		_presentationResultMotionInFlight = false;
		SetResultActionsEnabled(true);
	}

	private void SetResultActionsEnabled(bool enabled)
	{
		_presentationResultBack.Disabled = !enabled;
		_presentationResultNext.Disabled = !enabled || GameSession.CurrentSelection is null;
		_presentationResultRetry.Disabled = !enabled;
	}

	// Result confirmation is cosmetic; the action still owns all navigation and business state.
	private void ExitToSelectFromResult()
	{
		if (!_presentationResultMotionInFlight)
			ConfirmResultAction(_presentationResultBack, ExitToSelect);
	}

	private void NextSongFromResult()
	{
		if (!_presentationResultMotionInFlight)
			ConfirmResultAction(_presentationResultNext, NextSong);
	}

	private void RestartFromResult()
	{
		if (!_presentationResultMotionInFlight)
			ConfirmResultAction(_presentationResultRetry, Restart);
	}

	private void ConfirmResultAction(CutButton button, Action action)
	{
		var motion = PresentationMotionProfile();
		if (!motion.IsAnimated)
		{
			action();
			return;
		}

		_presentationResultMotionInFlight = true;
		SetResultActionsEnabled(false);
		button.CommitPulse(motion.FocusDuration);
		GetTree().CreateTimer(motion.FocusDuration).Timeout += () =>
		{
			if (!IsInsideTree())
				return;
			_presentationResultMotionInFlight = false;
			action();
		};
	}

	private void AddLine(Vector2 pos, Vector2 size, Color? color = null)
	{
		var line = new ColorRect
		{
			Color = color ?? JudgeLineColor,
			Position = pos,
			Size = size,
			MouseFilter = Control.MouseFilterEnum.Ignore,
			ZIndex = -1, // 背景之上、连接体/音符之下
		};
		_stageRoot.AddChild(line);
	}

}
