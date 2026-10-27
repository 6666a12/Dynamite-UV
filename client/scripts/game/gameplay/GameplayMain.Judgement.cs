using Godot;
using DynamiteUniverse.Audio;
using DynamiteUniverse.Ui;
using DynamiteUniverse.Shared.Chart;
using DynamiteUniverse.Shared.Chart.V2;
using DynamiteUniverse.Shared.Judge;
using DynamiteUniverse.Shared.Score;

namespace DynamiteUniverse.Game;

public partial class GameplayMain
{
	private readonly V2InputProtection _v2InputProtection = new();
	private readonly List<JudgeUnit>[] _pressUnitsByTrack =
		[[], [], []];
	private readonly int[] _trackPressWindowStart = new int[3];
	private readonly Dictionary<int, double> _runtimeNoteSeconds = new();
	private int _sweepCursor;

	private void BuildRuntimeJudgeIndex()
	{
		for (var i = 0; i < 3; i++)
		{
			_pressUnitsByTrack[i].Clear();
			foreach (var unit in _plan.Units)
				if ((int)unit.Track == i &&
					unit.Kind is UnitKind.Input or UnitKind.Mine)
					_pressUnitsByTrack[i].Add(unit);
		}
		_runtimeNoteSeconds.Clear();
		if (_loaded.V2Timeline is not null)
			foreach (var pair in _loaded.ExactTimesByRuntimeId)
				_runtimeNoteSeconds[pair.Key] = _loaded.V2Timeline.ToSeconds(pair.Value);
	}

	// 已判定或远超迟到窗口的单元对后续帧永久不可判，按轨道游标前移跳过；
	// 时间侧留 1s 余量，覆盖同帧 press 时刻早于 frameTime（卡顿帧）的情形。
	private void AdvancePressWindows(double frameTime)
	{
		for (var i = 0; i < 3; i++)
		{
			var bucket = _pressUnitsByTrack[i];
			var start = _trackPressWindowStart[i];
			while (start < bucket.Count)
			{
				var u = bucket[start];
				if (!u.Judged &&
					u.Time + _engine.Settings.MissSec * u.WindowScale >= frameTime - 1.0)
					break;
				start++;
			}
			_trackPressWindowStart[i] = start;
		}
	}

	private bool PressTimeCanJudge(JudgeUnit unit, double time)
	{
		if (unit.Kind == UnitKind.Mine)
			return InputJudgeRules.AcceptsMinePhase(unit.Time, time,
				_engine.Settings.PerfectSec * unit.WindowScale, ContactPhase.Began);
		return unit.Kind == UnitKind.Input &&
			Math.Abs(unit.Time - time) <= _engine.Settings.MissSec * unit.WindowScale;
	}

	private void ProcessPress(TouchSample touch, double t)
	{
		var bucket = _pressUnitsByTrack[(int)touch.Track];
		// 扫描上限沿用原全计划窗口的口径：Miss 窗乘最宽窗口系数（EX-Tap 1.5×）。
		var scanAhead = _engine.Settings.MissSec * JudgePlan.ExTapWindowScale;
		for (var i = _trackPressWindowStart[(int)touch.Track]; i < bucket.Count; i++)
		{
			var u = bucket[i];
			// 桶按时间升序：对本 press 已超出未来窗的单元，之后只会更远。
			if (u.Time - t > scanAhead)
				break;
			if (u.Judged || !PressTimeCanJudge(u, t) ||
				!_noteById.TryGetValue(u.NoteId, out var note) ||
				!InputJudgeRules.Overlaps(InputJudgeRules.Bounds(note.Position, note.Width),
					touch.Position, CommunityTouchWidth, expandByTouchWidth: u.Kind != UnitKind.Mine))
				continue;
			if (u.Kind != UnitKind.Mine && !_v2InputProtection.CanUse(u.Time))
				continue;
			if (u.Kind == UnitKind.Mine)
			{
				_engine.ApplyMine(touched: true);
				u.Judged = true;
				ResolveNoteView(u, JudgeGrade.Miss);
				FlashJudge("MINE!");
				continue;
			}
			var r = _engine.JudgePress(u.Time, t, u.WindowScale, u.IsExTap);
			if (r.Resolution == JudgeResolution.Pending)
				continue;
			_v2InputProtection.CommitResolved(u.Time);
			ApplyUnit(u, r.Grade, r.Resolution);
			u.Judged = true;
			ResolveSustainStart(u, r.Grade, touch, t);
			ResolveNoteView(u, r.Grade, timing: r.Timing);
			FlashJudge(GradeText(r));
		}
	}

	private TouchSample? FindNearestTouch(Track track, double anchor,
		NoteBounds? requiredBounds = null, bool expandByTouchWidth = true) =>
		FindNearestTouchIn(_touchesByTrack[(int)track], track, anchor,
			requiredBounds, expandByTouchWidth);

	private static TouchSample? FindNearestTouchIn(IEnumerable<TouchSample> samples,
		Track track, double anchor, NoteBounds? requiredBounds = null,
		bool expandByTouchWidth = true)
	{
		TouchSample? best = null;
		var bestDistance = double.MaxValue;
		foreach (var touch in samples)
		{
			if (touch.Track != track)
				continue;
			if (requiredBounds.HasValue && !InputJudgeRules.Overlaps(
				requiredBounds.Value, touch.Position, CommunityTouchWidth,
				expandByTouchWidth))
				continue;
			var distance = Math.Abs(touch.Position - anchor);
			if (distance < bestDistance)
			{
				bestDistance = distance;
				best = touch;
			}
		}
		return best;
	}

	// ---- 判定扫尾 ----

	private void SweepJudges(double t)
	{
		var scan = _sweepCursor;
		while (scan < _plan.Units.Count)
		{
			var u = _plan.Units[scan];
			if (u.Judged)
			{
				if (scan == _sweepCursor)
					_sweepCursor++;
				scan++;
				continue;
			}
			if (u.Time - t > _engine.Settings.MissSec * u.WindowScale)
				break;
			JudgeOne(u, t);
			if (u.Judged && scan == _sweepCursor)
				_sweepCursor++;
			if (!u.Judged && u.Time > t)
				break;
			scan++;
		}
	}

	private void JudgeOne(JudgeUnit u, double t)
	{
		var miss = _engine.Settings.MissSec * u.WindowScale;
		switch (u.Kind)
		{
			case UnitKind.Auto: // 保留给无需输入的普通自动单元
				if (t >= u.Time)
				{
					ApplyUnit(u, JudgeGrade.Perfect, JudgeResolution.Perfect);
					u.Judged = true;
					ResolveNoteView(u, JudgeGrade.Perfect);
				}
				break;

			case UnitKind.HoldPoint:
				JudgeHoldingPoint(u, t);
				break;

			case UnitKind.Mine:
				JudgeMine(u, t);
				break;

			case UnitKind.Contact:
				JudgeContact(u, t);
				break;

			case UnitKind.Input:
				if (_auto && t >= u.Time)
				{
					var r = _engine.Judge(u.Time, u.Time, u.WindowScale, u.IsExTap); // Auto：精确时刻判定
					ApplyUnit(u, r.Grade, r.Resolution);
					u.Judged = true;
					ResolveSustainStart(u, r.Grade, null, u.Time);
					ResolveNoteView(u, JudgeGrade.Perfect);
				}
				else if (t > u.Time + miss)
				{
					ApplyUnit(u, JudgeGrade.Miss, JudgeResolution.AutoMiss);
					u.Judged = true;
					ResolveSustainStart(u, JudgeGrade.Miss, null, t);
					ResolveNoteView(u, JudgeGrade.Miss);
					FlashJudge("MISS");
				}
				break;

			case UnitKind.HoldEnd:
				ResolveHoldEnd(u, t);
				break;

		}
	}

	private void JudgeContact(JudgeUnit unit, double t)
	{
		var perfect = _engine.Settings.PerfectSec * unit.WindowScale;
		var miss = _engine.Settings.MissSec * unit.WindowScale;
		var good = _engine.Settings.GoodSec * unit.WindowScale;
		if (t < unit.Time - perfect)
			return;
		if (t > unit.Time + miss)
		{

			ApplyUnit(unit, JudgeGrade.Miss, JudgeResolution.AutoMiss);
			unit.Judged = true;
			ResolveNoteView(unit, JudgeGrade.Miss);
			FlashJudge("MISS");
			return;
		}
		if (t > unit.Time + good)
			return;
		if (!_noteById.TryGetValue(unit.NoteId, out var note))
			return;
		var bounds = InputJudgeRules.Bounds(note.Position, note.Width);
		if (_auto && t >= unit.Time)
		{
			ApplyUnit(unit, JudgeGrade.Perfect, JudgeResolution.Perfect);
			unit.Judged = true;
			ResolveNoteView(unit, JudgeGrade.Perfect);
			return;
		}
		foreach (var touch in _touchesByTrack[(int)unit.Track])
		{
			if (touch.Track != unit.Track ||
				!InputJudgeRules.AcceptsContactPhase(
					unit.Time, t, perfect, touch.Phase) ||
				!InputJudgeRules.Overlaps(bounds, touch.Position, CommunityTouchWidth))
				continue;
			var result = _engine.Judge(unit.Time, t, unit.WindowScale);
			ApplyUnit(unit, result.Grade, result.Resolution);
			unit.Judged = true;
			ResolveNoteView(unit, result.Grade, timing: result.Timing);
			FlashJudge(GradeText(result));
			return;
		}
	}

	private void JudgeMine(JudgeUnit unit, double t)
	{
		if (!_noteById.TryGetValue(unit.NoteId, out var note))
			return;
		var perfect = _engine.Settings.PerfectSec * unit.WindowScale;
		var bounds = InputJudgeRules.Bounds(note.Position, note.Width);
		foreach (var touch in _touchesByTrack[(int)unit.Track])
		{
			if (touch.Track != unit.Track ||
				!InputJudgeRules.AcceptsMinePhase(unit.Time, t, perfect, touch.Phase) ||
				!InputJudgeRules.Overlaps(bounds, touch.Position, CommunityTouchWidth,
					expandByTouchWidth: false))
				continue;
			_engine.ApplyMine(touched: true);
			unit.Judged = true;
			ResolveNoteView(unit, JudgeGrade.Miss);
			FlashJudge("MINE!");
			return;
		}
		if (t >= unit.Time)
		{
			_engine.ApplyMine(touched: false);
			unit.Judged = true;
			ResolveNoteView(unit, JudgeGrade.Perfect, emitEffect: false);
		}
	}

	private void JudgeHoldingPoint(JudgeUnit unit, double t)
	{
		if (t < unit.Time)
			return;
		if (unit.SustainHeadId is not { } headId ||
			!_sustainStates.TryGetValue(headId, out var state))
		{
			ApplyUnit(unit, JudgeGrade.Miss, JudgeResolution.AutoMiss);
			unit.Judged = true;
			return;
		}

		if (unit.Category == ScoreCategory.HoldHolding)
		{
			// 头仍在 Late 窗内时保留节点；头一旦接起或 Miss，节点会在同帧结算。
			if (!state.StartResolved)
				return;
			if (_auto || (state.StartResolved && !state.Broken &&
				(state.Holding || state.Contact.HasLoss)))
				ApplyUnit(unit, JudgeGrade.Perfect, JudgeResolution.Perfect);
			else
				ApplyUnit(unit, JudgeGrade.Miss, JudgeResolution.AutoMiss);
			unit.Judged = true;
			return;
		}

		// Replay computes grades; this globally ordered sweep still owns score/Combo application.
		if (state.MixerGrades.Remove(unit, out var mixerGrade))
			ApplyUnit(unit, mixerGrade, mixerGrade == JudgeGrade.Perfect
				? JudgeResolution.Perfect : JudgeResolution.AutoMiss);
	}

	private void ResolveHoldEnd(JudgeUnit unit, double t)
	{
		if (t < unit.Time || unit.Judged)
			return;
		SustainRuntime? state = null;
		if (unit.SustainHeadId is { } headId)
			_sustainStates.TryGetValue(headId, out state);
		var result = SustainJudgementRules.JudgeHoldTail(unit.Time, t,
			state == null || state.StartResolved, _auto || state?.Holding == true,
			state?.Broken ?? true, state?.Contact ?? default, _engine, unit.WindowScale);
		if (result is not { } settled)
			return;
		ApplyUnit(unit, settled.Grade, settled.Resolution);
		unit.Judged = true;
		if (state != null)
			FinishHoldViews(state, settled.Grade);
		ResolveNoteView(unit, settled.Grade);
		FlashJudge(GradeText(settled.Grade, settled.Timing));
	}

	private void ApplyUnit(JudgeUnit unit, JudgeGrade grade,
		JudgeResolution? resolution = null)
	{
		if (unit.Judged)
			return;
		_engine.Apply(unit.Category, grade, unit.AffectsCombo,
			unit.AffectsJudgeCounts, resolution);
		unit.Judged = true;
	}
}
