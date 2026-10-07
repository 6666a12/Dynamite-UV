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
		_hudRoot = new Node2D { Name = "GameplayHud", ZIndex = 20 };
		AddChild(_stageRoot);
		AddChild(_hudRoot);
		var sharedStage = new GameplayStageRenderer { Name = "SharedCommunityStage" };
		_stageRoot.AddChild(sharedStage);
		sharedStage.BuildCommunityStageChrome();
		// Gameplay-owned judgement state still controls spawn/recycle, but its
		// NoteView and sustain children now live in the shared stage layer.
		_noteRoot = sharedStage.NoteLayer;

#if false // Stage chrome is now owned by GameplayStageRenderer and shared with DynaMaker UV.

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
#endif

		BuildHud();
		BuildPauseMenu();
		BuildResultLayer();
	}

	private void BuildHud()
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
			_titleTagStatus.Text = _auto ? $"{difficulty}   /   AUTO · 不计成绩" : difficulty;
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
		_resultScreen = new ResultScreen { Name = "ResultV2", Visible = false, ZIndex = 100 };
		AddChild(_resultScreen);
	}

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
		_v2InputProtection.Reset();
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

	// HUD 刷新缓存：Label.Text 赋值触发全文 reshaping，值不变时跳过写入与插值。
	private int _hudScore = -1;
	private int _hudPerfect = -1;
	private int _hudGreat = -1;
	private int _hudGood = -1;
	private int _hudMiss = -1;
	private int _hudMaxCombo = -1;
	private int _hudCombo = -1;
	private double _hudLiveAcc = -1.0;

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

		var score = _engine.NormalizedScore(_plan.TheoreticalMax);
		if (score != _hudScore)
		{
			_hudScore = score;
			_scoreLabel.Text = $"{score:N0}";
		}
		// 实时 ACC = 当前得分 / 已判定单元的理论满分（未判定不计入），并钳制在 0..100%。
		var liveAcc = _judgedTheoreticalMax > 0
			? Math.Clamp(100.0 * _engine.Score / _judgedTheoreticalMax, 0.0, 100.0) : 100.0;
		if (liveAcc != _hudLiveAcc)
		{
			_hudLiveAcc = liveAcc;
			_pillAcc.Text = $"ACC {liveAcc:F2}%";
		}
		if (_engine.CountPrefect != _hudPerfect)
		{
			_hudPerfect = _engine.CountPrefect;
			_pillP.Text = $"PERFECT {_hudPerfect}";
		}
		if (_engine.CountGreat != _hudGreat)
		{
			_hudGreat = _engine.CountGreat;
			_pillGr.Text = $"GREAT {_hudGreat}";
		}
		if (_engine.CountGood != _hudGood)
		{
			_hudGood = _engine.CountGood;
			_pillGo.Text = $"GOOD {_hudGood}";
		}
		if (_engine.CountMiss != _hudMiss)
		{
			_hudMiss = _engine.CountMiss;
			_pillM.Text = $"MISS {_hudMiss}";
		}
		if (_engine.MaxCombo != _hudMaxCombo)
		{
			_hudMaxCombo = _engine.MaxCombo;
			_pillMc.Text = $"M.COMBO {_hudMaxCombo}";
		}
		if (_engine.Combo != _hudCombo)
		{
			_hudCombo = _engine.Combo;
			_comboLabel.Text = _hudCombo > 1 ? $"{_hudCombo}" : "";
			_comboSub.Text = _hudCombo > 1 ? "COMBO" : "";
		}
		// 底线上的进度段：从中央向两侧生长
		var pw = 933f * Mathf.Clamp((float)(t / Math.Max(0.01, _plan.EndTime)), 0f, 1f);
		_progressFill.Position = new Vector2(960 - pw / 2f, CenterLineY - 2);
		_progressFill.Size = new Vector2(pw, 3);
	}

	private void ShowResults()
	{
		if (_finished) return;
		_finished = true;
		_pendingPointerInputs.Clear();
		// Freeze judging, but let the song tail play beneath the shutter/result.
		// Navigation still owns stopping playback when leaving or replaying.
		var pct = _engine.Percent(_plan.TheoreticalMax);
		var normalizedScore = _engine.NormalizedScore(_plan.TheoreticalMax);
		var grade = ScoreStore.GradeOf(pct);
		var hasIdentity = _loaded.TryGetScoreIdentity(out var identity);
		var previous = hasIdentity ? GameSession.Scores.Get(identity)
			: GameSession.Scores.Get(_run.PackId, _run.LegacyScoreKey);
		var previousBest = previous?.Score;
		var record = new ScoreRecord
		{
			Score = normalizedScore, Acc = pct, MaxCombo = _engine.MaxCombo, Grade = grade,
			Perfect = _engine.CountPrefect, Great = _engine.CountGreat,
			Good = _engine.CountGood, Miss = _engine.CountMiss,
		};
		// Snapshot the previous best before saving; AUTO must never write scores.
		var isNewRecord = !_auto && (hasIdentity
			? GameSession.Scores.TryUpdate(identity, record)
			: GameSession.Scores.TryUpdate(_run.PackId, _run.LegacyScoreKey, record));
		var texture = CoverTextureCache.Current(_run.CoverPath) ?? CoverTextureCache.Load(_run.CoverPath);
		_presentationPauseTween?.Kill();
		_presentationPauseTween = null;
		_presentationPauseMotionInFlight = false;
		_pauseLayer.Visible = false;
		var selection = GameSession.CurrentSelection;
		var packs = GameSession.Packs;
		var canNext = selection != null && packs.Count > 0 &&
			packs[(packs.IndexOf(selection.Pack) + 1) % packs.Count].Charts.Count > 0;
		_resultScreen.Present(new ResultScreenData(_run, record, _plan.HeadlineUnitCount,
			previousBest, isNewRecord, _auto,
			GameSession.Settings.GameplayMode == GameplayMode.Hardcore,
			GameSession.Settings.BleedEffective, GameSession.Settings.MirrorEnabled, texture),
			PresentationMotionProfile(), () =>
			{
				_stageRoot.Visible = false;
				_noteRoot.Visible = false;
				_hudRoot.Visible = false;
			}, ExitToSelect, NextSong, Restart,
			canNext);

		GD.Print($"=== RESULT === {_run.SongTitle} [{_run.LegacyScoreKey} Lv{_run.DifficultyLevel}] " +
			$"score={normalizedScore} rawScore={_engine.Score} clear={pct:F2}% grade={grade} " +
			$"P={_engine.CountPrefect} Gr={_engine.CountGreat} Gd={_engine.CountGood} M={_engine.CountMiss} " +
			$"maxCombo={_engine.MaxCombo} newRecord={isNewRecord}");
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
