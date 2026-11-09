using Godot;
using DynamiteUniverse.Ui;
using DynamiteUniverse.Shared.Score;
using DynamiteUniverse.Shared.Judge;

namespace DynamiteUniverse.Game;

/// <summary>
/// Procedural gameplay stage, HUD and result presentation plus pause wiring; the pause layer
/// and its motion live in <see cref="GameplayPauseOverlay"/>. GameplayMain owns the callbacks
/// (pointer cleanup, playback pause/resume, navigation) and score timing.
/// </summary>
public partial class GameplayMain
{

	// ---- HUD / 结算节点 ----
	private Node2D _stageRoot = null!;
	private Node2D _noteRoot = null!;
	private Node2D _hudRoot = null!;
	private GameplayPauseOverlay _pause = null!;
	private ResultFlow _results = null!;


	private GameplayHud _hud = null!;
	private void BuildHud() => _hud = new GameplayHud(_hudRoot, TogglePause, StageOffsetY, TopHudOffsetY,
		() => GameSession.Settings.MotionMode, () => !_playbackStarted || _finished || _pause.IsPaused,
		() => _playbackStarted && !_finished && !_pause.IsPaused && !TransitionDirector.IsBusy);
	private void RefreshTitleTag() => _hud.RefreshTitleTag(_run, _auto);
	private void FlashJudge(string text) => _hud.FlashJudge(text, GameSession.Settings.JudgeTextEnabled);
	private void UpdateHud(double time)
	{
		var liveAcc = _engine.JudgedTheoreticalMax > 0
			? Math.Clamp(_engine.Percent(_engine.JudgedTheoreticalMax), 0.0, 100.0) : 100.0;
		_hud.Update(new GameplayHudSnapshot(_engine.NormalizedScore(_plan.TheoreticalMax), liveAcc,
			_engine.CountPerfect, _engine.CountGreat, _engine.CountGood, _engine.CountMiss,
			_engine.MaxCombo, _engine.Combo, time, _plan.EndTime, _bleed is not null,
			_bleed?.Rule == BleedRule.Hardcore, _bleed?.Health ?? 100), GetProcessDeltaTime());
	}

	// ---- UI ----

	private void BuildStage()
	{
		_stageRoot = new Node2D { Name = "StageVisuals" };
		_hudRoot = new Node2D { Name = "GameplayHud", ZIndex = 20 };
		AddChild(_stageRoot);
		AddChild(_hudRoot);
		var sharedStage = new GameplayStageRenderer
		{
			Name = "SharedCommunityStage",
			MirrorEnabled = GameplayMirror.Enabled,
			Position = new Vector2(0f, StageOffsetY),
		};
		_stageRoot.AddChild(sharedStage);
		sharedStage.BuildCommunityStageChrome(StageOffsetY, gameplayHud: true);
		// Gameplay-owned judgement state still controls spawn/recycle, but its
		// NoteView and sustain children now live in the shared stage layer.
		_noteRoot = sharedStage.NoteLayer;


		BuildHud();
		BuildPauseMenu();
		BuildResultLayer();
	}

	private void BuildPauseMenu() =>
		_pause = new GameplayPauseOverlay(this, PresentationMotionProfile, () => _finished,
			SuspendForPause, ResumeFromPause, Restart, ExitToSelect);

	// Pause order is a contract: contacts are cleared before the clock stops, and the overlay
	// only flips its paused flag before calling back into these two host hooks.
	private void SuspendForPause()
	{
		ClearPointerState();
		_playback.Pause();
		_hud.PulsePause();
	}

	private void ResumeFromPause() => _playback.Resume();


	private void BuildResultLayer() =>
		_results = new ResultFlow(this, PresentationMotionProfile);

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

	// HUD、Esc 与暂停菜单按钮共用这一个入口；暂停状态和动画由 overlay 持有。
	private void TogglePause() => _pause.Toggle();

	private void ClearPointerState()
	{
		_pointerBuffer.Clear();
		foreach (var state in _sustainStates.Values)
			state.Mixer?.ClearControls();
		_ignoredTouchIds.Clear();
		_v2InputProtection.Reset();
	}

		private void Restart()
		{
			_playback.Pause();
			_pause.ResetPausedForNavigation();
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
			_pause.ResetPausedForNavigation();
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

	/// <summary>结算唯一入口：成绩只算/存一次，再把同一份快照交给前奏与结算页。</summary>
	private void ShowResults(bool stageFailure = false)
	{
		if (_finished) return;
		_finished = true;
		_pointerBuffer.DiscardPending();
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
			Perfect = _engine.CountPerfect, Great = _engine.CountGreat,
			Good = _engine.CountGood, Miss = _engine.CountMiss,
		};
		// Snapshot the previous best before saving; AUTO must never write scores.
		var isNewRecord = !_auto && (hasIdentity
			? GameSession.Scores.TryUpdate(identity, record)
			: GameSession.Scores.TryUpdate(_run.PackId, _run.LegacyScoreKey, record));
		if (isNewRecord)
			GameSession.Scores.SaveDeferred();
		var outcome = ResultOutcome.Classify(record, _plan.HeadlineUnitCount, stageFailure);
		var texture = CoverTextureCache.Current(_run.CoverPath) ?? CoverTextureCache.Load(_run.CoverPath);
		// Force-close only hides the pause layer and keeps its paused flag untouched. Natural
		// completion retains the song tail; BLEED depletion pauses playback before entering here.
		_pause.ForceCloseForResult();
		var selection = GameSession.CurrentSelection;
		var packs = GameSession.Packs;
		var canNext = selection != null && packs.Count > 0 &&
			packs[(packs.IndexOf(selection.Pack) + 1) % packs.Count].Charts.Count > 0;
		_results.Begin(new ResultScreenData(_run, record, _plan.HeadlineUnitCount,
			previousBest, isNewRecord, _auto,
			GameSession.Settings.GameplayMode == GameplayMode.Hardcore,
			GameSession.Settings.BleedEffective, GameSession.Settings.MirrorEnabled, texture, outcome),
			() =>
			{
				_stageRoot.Visible = false;
				_noteRoot.Visible = false;
				_hudRoot.Visible = false;
			}, ExitToSelect, NextSong, Restart,
			canNext);

		GD.Print($"=== RESULT === {_run.SongTitle} [{_run.LegacyScoreKey} Lv{_run.DifficultyLevel}] " +
			$"score={normalizedScore} rawScore={_engine.Score} clear={pct:F2}% grade={grade} " +
			$"P={_engine.CountPerfect} Gr={_engine.CountGreat} Gd={_engine.CountGood} M={_engine.CountMiss} " +
			$"maxCombo={_engine.MaxCombo} newRecord={isNewRecord} outcome={outcome}");
	}

}
