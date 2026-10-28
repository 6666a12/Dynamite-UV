using Godot;
using DynamiteUniverse.Ui;
using DynamiteUniverse.Shared.Chart;
using DynamiteUniverse.Shared.Judge;
using static DynamiteUniverse.Game.GameplayRenderGeometry;

namespace DynamiteUniverse.Game;

public partial class GameplayMain
{

	private sealed class SustainRuntime
	{
		public required SustainPath Path;
		public bool StartResolved;
		public bool Holding;
		public bool Broken;
		public bool EndResolved;
		public double LastContactUpdateTime;
		public double LostContactSecond;
		public HoldContactState Contact;
		public MixerRuntime? Mixer;
		public readonly List<JudgeUnit> MixerTicks = new();
		public int MixerTickCursor;
		public readonly Dictionary<JudgeUnit, JudgeGrade> MixerGrades = new();
	}
	private readonly Dictionary<int, SustainRuntime> _sustainStates = new();

	private void ResolveSustainStart(JudgeUnit unit, JudgeGrade grade,
		TouchSample? touch, double resolvedTime)
	{
		if (unit.SustainHeadId is not { } headId ||
			!_sustainStates.TryGetValue(headId, out var state))
			return;
		if (unit.Category is not (ScoreCategory.HoldStart or ScoreCategory.MixerStart))
			return;

		state.StartResolved = true;
		state.LastContactUpdateTime = state.Path.Kind == SustainKind.Hold
			? resolvedTime : Math.Max(state.Path.StartTime, resolvedTime);
		state.LostContactSecond = 0.0;
		state.Contact = HoldContactState.Contact;
		if (state.Path.Kind == SustainKind.Hold)
		{
			state.Holding = grade != JudgeGrade.Miss;
			if (grade == JudgeGrade.Miss)
				FailRemainingHold(state);
			return;
		}

		// Head scoring authorizes early placement only. The actual operation is replayed
		// at its timestamp with all other inputs; a late head cannot rewind the slider.
		if (grade != JudgeGrade.Miss && touch is { } mixerTouch)
			_mixerEarlyPlacements.Add((state.Path.HeadId, mixerTouch.Id, resolvedTime));
	}

	private void UpdateSustainStates(double t)
	{
		foreach (var state in _sustainStates.Values)
		{
			if (state.EndResolved)
				continue;

			if (state.Path.Kind == SustainKind.Mixer)
			{
				if (t >= state.Path.StartTime || state.StartResolved)
					UpdateMixerState(state, t);
				continue;
			}

			if (!state.StartResolved || !state.Holding || state.Broken)
				continue;
			UpdateHoldState(state, t);
		}
	}

	private void UpdateHoldState(SustainRuntime state, double t)
	{
		var activeUntil = Math.Min(t, state.Path.EndTime);
		var from = state.LastContactUpdateTime;
		var bounds = state.Path.BoundsAtRuntime(activeUntil);
		var touch = FindNearestTouch(state.Path.Track, bounds.Center,
			bounds, expandByTouchWidth: true);
		var touching = _auto || touch.HasValue;
		_noteRenderer.UpdateHoldHeadContactView(state.Path, touching, bounds);
		_noteRenderer.UpdateSustainEffect(state.Path, touching, bounds, bounds.Center);
		if (activeUntil < from)
			return;

		state.Contact = _auto ? HoldContactState.Contact : _pointerBuffer.HoldContacts.Observe(
			state.Path, from, activeUntil, state.Contact, _engine.Settings,
			_chart.BpmAtSeconds, CommunityTouchWidth);
		state.LostContactSecond = state.Contact.ContactLostAt is { } lostAt
			? Math.Max(0.0, activeUntil - lostAt) : 0.0;

		state.LastContactUpdateTime = activeUntil;
		if (state.Contact is { ContactLostAt: { } lost, GraceDeadline: { } deadline })
		{
			var loss = new SustainJudgementRules.HoldContactLoss(
				lost, deadline - lost, deadline);
			var settlementTime = SustainJudgementRules.HoldSettlementTime(
				state.Path.EndTime, loss);
			if (SustainJudgementRules.ShouldBreakHold(
				activeUntil, state.Path.EndTime, loss))
			{
				BreakHold(state, lost);
			}
			else if (settlementTime == state.Path.EndTime &&
				activeUntil >= settlementTime)
			{
				SettleReleasedHold(state, lost);
			}
		}
	}

	private void FinalizeEndedMixers(double t)
	{
		foreach (var state in _sustainStates.Values)
		{
			if (state.Path.Kind != SustainKind.Mixer || state.EndResolved ||
				t < state.Path.EndTime)
				continue;
			state.Holding = false;
			state.EndResolved = true;
			_noteRenderer.ReleaseMixerHeadView(state.Path.HeadId);
		}
	}

	private void BreakHold(SustainRuntime state, double releaseTime)
	{
		if (state.Broken || state.EndResolved)
			return;
		state.Broken = true;
		SettleReleasedHold(state, releaseTime);
	}

	private void SettleReleasedHold(SustainRuntime state, double releaseTime)
	{
		if (state.EndResolved)
			return;
		SustainJudgementRules.Settlement? tail = null;
		foreach (var settlement in SustainJudgementRules.SettleReleasedHold(
			_plan.Units, state.Path.HeadId, releaseTime, _engine))
		{
			// 批量结算在 shared 内部直接落 engine，这里补齐 BLEED 血量。
			_bleed?.Apply(settlement.Grade);
			state.Broken |= settlement.Grade == JudgeGrade.Miss;
			if (settlement.Unit.Category == ScoreCategory.HoldEnd)
				tail = settlement;
		}
		FinishHoldViews(state, tail?.Grade ?? JudgeGrade.Miss);
		if (tail is { } settledTail)
		{
			ResolveNoteView(settledTail.Unit, settledTail.Grade);
			FlashJudge(GradeText(settledTail.Grade, settledTail.Timing));
		}
	}

	private void FailRemainingHold(SustainRuntime state)
	{
		if (state.EndResolved)
			return;
		state.Broken = true;
		foreach (var unit in SustainJudgementRules.FailRemainingHold(
			_plan.Units, state.Path.HeadId, _engine))
			_bleed?.Apply(JudgeGrade.Miss); // 同上：批量 Miss 也计 BLEED 扣血
		FinishHoldViews(state, JudgeGrade.Miss);
	}

    private void FinishHoldViews(SustainRuntime state, JudgeGrade grade)
    {
        state.Holding = false;
        state.EndResolved = true;
        if (state.Broken || grade == JudgeGrade.Miss) state.Broken = true;
        _noteRenderer.FinishHoldViews(state.Path, state.Broken);
    }

    private SustainVisualSnapshot? ReadSustainVisual(int headId) =>
        _sustainStates.TryGetValue(headId, out var state)
            ? new(state.Path, state.StartResolved, state.Holding, state.Broken,
                state.EndResolved, state.LastContactUpdateTime)
            : null;
}
