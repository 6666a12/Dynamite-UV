using static CoreTests.CoreTestSupport;
using DynamiteUniverse.Shared.Chart;
using DynamiteUniverse.Shared.Chart.V2;
using DynamiteUniverse.Shared.Judge;
using DynamiteUniverse.Shared.Score;

namespace CoreTests;

internal static class SustainJudgementTests
{

	// 9. Hold 按真实节点判定；Mixer 从头相位每 1/8 bar 派生判定。
	internal static void TestDerivedSustainJudgements()
	{
		Console.WriteLine("[9] Derived sustain judgements");
		var holdHead = TestNote(1, 2, NoteType.HoldHead, 0.0, 0.0, 1.0, 0.0);
		var holdNode = TestNote(2, 3, NoteType.HoldNode, 0.25, 0.5, 1.0, 0.5);
		var holdTail = TestNote(3, -1, NoteType.HoldNode, 0.75, 1.0, 1.0, 1.5);
		var mixerHead = TestNote(10, 11, NoteType.MixerHead, 2.0, 1.0, 1.0, 4.0,
			Track.Left);
		var mixerTail = TestNote(11, -1, NoteType.MixerNode, 3.0, 2.0, 1.0, 6.0,
			Track.Left);
		var shortMixerHead = TestNote(20, 21, NoteType.MixerHead, 4.0, 1.0, 1.0, 8.0,
			Track.Right);
		var shortMixerTail = TestNote(21, -1, NoteType.MixerNode, 4.3, 2.0, 1.0, 8.6,
			Track.Right);
		var chart = new Chart
		{
			Name = "test",
			Title = "test",
			Difficulty = 3,
			TotalMainNote = 0,
			Sections = new[]
			{
				new BarSection { Bpm = 120, BarTime = 0.0, Seconds = 0.0 },
			},
			NotesLeft = new[] { mixerHead, mixerTail },
			NotesCenter = new[] { holdHead, holdNode, holdTail },
			NotesRight = new[] { shortMixerHead, shortMixerTail },
		};
		var plan = JudgePlan.Build(chart, JudgeSettings.ForPreset(JudgePreset.Hard));

		var holdUnits = plan.Units
			.Where(unit => unit.SustainHeadId == holdHead.Id).ToArray();
		Check(holdUnits.Length == 3, "三节点 Hold 正好生成头、中间节点和尾三个主判定");
		Check(holdUnits.Select(unit => unit.NoteId).SequenceEqual(new[] { 1, 2, 3 }),
			"Hold 判定严格对应实际路径节点");

		var mixerUnits = plan.Units
			.Where(unit => unit.SustainHeadId == mixerHead.Id).ToArray();
		Check(mixerUnits.Length == 9, "1 bar Mixer 从头到尾生成 9 个主判定");
		for (var i = 0; i < mixerUnits.Length; i++)
			Check(Math.Abs(mixerUnits[i].Time - (4.0 + i * 0.25)) < 1e-9,
				$"Mixer tick[{i}] 位于头部相位 + {i}/8 bar");

		var shortMixerUnits = plan.Units
			.Where(unit => unit.SustainHeadId == shortMixerHead.Id).ToArray();
		Check(shortMixerUnits.Length == 3,
			"0.3 bar Mixer 只生成 0、1/8、2/8 三个判定，不补非网格尾");
		Check(shortMixerUnits.All(unit => Math.Abs(unit.Time - 8.6) > 1e-9),
			"非网格尾不产生额外判定");
		Check(Math.Abs(plan.EndTime - 8.6) < 1e-9,
			"非网格尾仍决定 sustain 和谱面结束时间");

		Check(plan.Units.All(unit => unit.AffectsCombo && unit.AffectsJudgeCounts),
			"Hold 节点和 Mixer 八分点全部进入 Combo 与 P/GR/GD/M");
		Check(plan.Units.All(unit =>
			JudgeEngine.ScoreDelta(unit.Category, JudgeGrade.Perfect) == 100),
			"Hold 节点和 Mixer 八分点 Perfect 权重均为 100 分");
		Check(JudgeEngine.ScoreDelta(ScoreCategory.HoldHolding, JudgeGrade.Great) == 70 &&
			JudgeEngine.ScoreDelta(ScoreCategory.HoldHolding, JudgeGrade.Good) == 50 &&
			JudgeEngine.ScoreDelta(ScoreCategory.MixerHolding, JudgeGrade.Great) == 70 &&
			JudgeEngine.ScoreDelta(ScoreCategory.MixerHolding, JudgeGrade.Good) == 50,
			"派生主判定沿用完整 100/70/50/0 权重");
	}

	internal static void TestHoldFailureSettlement()
	{
		Console.WriteLine(nameof(TestHoldFailureSettlement));
		Check(JudgeEngine.ScoreDelta(ScoreCategory.MixerHolding,
			JudgeGrade.Miss) == 0, nameof(JudgeGrade.Miss));
		var head = TestNote(1, 2, NoteType.HoldHead, 0.0, 0.0, 1.0, 0.0);
		var middle = TestNote(2, 3, NoteType.HoldNode, 0.5, 0.0, 1.0, 1.0);
		var tail = TestNote(3, -1, NoteType.HoldNode, 1.0, 0.0, 1.0, 2.0);
		var chart = new Chart
		{
			Name = string.Empty,
			Title = string.Empty,
			Difficulty = 3,
			TotalMainNote = 0,
			Sections = Array.Empty<BarSection>(),
			NotesLeft = Array.Empty<Note>(),
			NotesCenter = new[] { head, middle, tail },
			NotesRight = Array.Empty<Note>(),
		};
		var plan = JudgePlan.Build(chart, JudgeSettings.ForPreset(JudgePreset.Hard));
		var engine = new JudgeEngine(JudgePreset.Hard, plan.HeadlineUnitCount);
		var headUnit = plan.Units.Single(
			unit => unit.Category == ScoreCategory.HoldStart);
		engine.Apply(headUnit.Category, JudgeGrade.Perfect);
		headUnit.Judged = true;
		var failed = SustainJudgementRules.FailRemainingHold(
			plan.Units, head.Id, engine);
		Check(failed.Count == 2, nameof(TestHoldFailureSettlement));
		Check(failed.All(unit => unit.Judged),
			nameof(SustainJudgementRules.FailRemainingHold));
		Check(engine.CountPerfect == 1 && engine.CountMiss == 2 &&
			engine.Combo == 0, nameof(JudgeEngine.Combo));
		var score = engine.Score;
		var misses = engine.CountMiss;
		var repeated = SustainJudgementRules.FailRemainingHold(
			plan.Units, head.Id, engine);
		Check(repeated.Count == 0 && engine.Score == score &&
			engine.CountMiss == misses, nameof(JudgeUnit.Judged));
	}

	internal static void TestReleasedHoldSettlement()
	{
		Console.WriteLine(nameof(TestReleasedHoldSettlement));
		var head = TestNote(1, 2, NoteType.HoldHead, 0.0, 0.0, 1.0, 0.0);
		var middle = TestNote(2, 3, NoteType.HoldNode, 0.5, 0.0, 1.0, 1.0);
		var tail = TestNote(3, -1, NoteType.HoldNode, 1.0, 0.0, 1.0, 2.0);
		var chart = new Chart
		{
			Name = string.Empty,
			Title = string.Empty,
			Difficulty = 3,
			TotalMainNote = 0,
			Sections = Array.Empty<BarSection>(),
			NotesLeft = Array.Empty<Note>(),
			NotesCenter = new[] { head, middle, tail },
			NotesRight = Array.Empty<Note>(),
		};
		var plan = JudgePlan.Build(chart, JudgeSettings.ForPreset(JudgePreset.Hard));
		var engine = new JudgeEngine(JudgePreset.Hard, plan.HeadlineUnitCount);
		var headUnit = plan.Units.Single(
			unit => unit.Category == ScoreCategory.HoldStart);
		engine.Apply(headUnit.Category, JudgeGrade.Perfect);
		headUnit.Judged = true;

		var settled = SustainJudgementRules.SettleReleasedHold(
			plan.Units, head.Id, 1.9, engine);
		Check(settled.Count == 2, nameof(TestReleasedHoldSettlement));
		Check(settled.Single(item => item.Unit.Category == ScoreCategory.HoldHolding)
			.Grade == JudgeGrade.Miss, "提前松手后未结算的 Hold 中间节点为 Miss");
		Check(settled.Single(item => item.Unit.Category == ScoreCategory.HoldEnd)
			.Grade == JudgeGrade.Great, "Hold 尾按实际松手时刻进入 Great 档");
		Check(settled.Single(item => item.Unit.Category == ScoreCategory.HoldEnd)
			.Timing == HitTiming.Early, "提前松手的 Hold 尾保留 Early 时序");
		Check(engine.CountPerfect == 1 && engine.CountGreat == 1 &&
			engine.CountMiss == 1, "提前松手结算统计正确");

		var afterTailPlan = JudgePlan.Build(chart, JudgeSettings.ForPreset(JudgePreset.Hard));
		var afterTail = new JudgeEngine(JudgePreset.Hard, afterTailPlan.HeadlineUnitCount);
		var afterTailHead = afterTailPlan.Units.Single(
			unit => unit.Category == ScoreCategory.HoldStart);
		afterTail.Apply(afterTailHead.Category, JudgeGrade.Perfect);
		afterTailHead.Judged = true;
		var afterTailSettled = SustainJudgementRules.SettleReleasedHold(
			afterTailPlan.Units, head.Id, 2.2, afterTail);
		Check(afterTailSettled.Single(item => item.Unit.Category == ScoreCategory.HoldEnd)
			.Grade == JudgeGrade.Perfect &&
			afterTailSettled.Single(item => item.Unit.Category == ScoreCategory.HoldEnd)
				.Timing == HitTiming.Exact,
			"共享 Hold 结算将尾后 release 钳到尾时刻 Perfect");
	}

	// 10. Hold/Mixer 身体左右边缘分别线性插值。
	internal static void TestSustainInterpolation()
	{
		Console.WriteLine("[10] Sustain bounds interpolation");
		var a = TestNote(1, 2, NoteType.HoldHead, 0, 1, 1, 0);
		var b = TestNote(2, -1, NoteType.HoldNode, 1, 3, 2, 10);
		var path = new SustainPath
		{
			Kind = SustainKind.Hold,
			HeadId = 1,
			Track = Track.Center,
			Nodes = new[] { a, b },
		};
		var mid = path.BoundsAt(5.0);
		Check(Math.Abs(mid.Left - 2.0) < 1e-12, "中点左缘线性插值");
		Check(Math.Abs(mid.Right - 3.5) < 1e-12, "中点右缘线性插值");
	}

	internal static void TestD4CFrozenGrace()
	{
		Console.WriteLine("[15] D4-C frozen Hold grace");
		var settings = JudgeSettings.ForPreset(JudgePreset.Hard);
		var slow = SustainJudgementRules.BeginHoldContactLoss(10.0, 90.0, settings);
		Check(Math.Abs(slow.GraceDuration - 0.25) < 1e-12 &&
			Math.Abs(slow.Deadline - 10.25) < 1e-12,
			"loss BPM clamps to 120 and freezes a 250ms deadline");
		Check(!SustainJudgementRules.ShouldBreakHold(10.24, 12.0, slow) &&
			SustainJudgementRules.ShouldBreakHold(10.25, 12.0, slow),
			"Hold breaks exactly at the frozen deadline, independent of later BPM");
		Check(SustainJudgementRules.HoldSettlementTime(10.2, slow) == 10.2 &&
			SustainJudgementRules.HoldSettlementTime(12.0, slow) == 10.25,
			"D4-C settlement time is min(tail, deadline)");
		var fast = SustainJudgementRules.BeginHoldContactLoss(20.0, 240.0, settings);
		Check(Math.Abs(fast.GraceDuration - 0.15) < 1e-12,
			"loss BPM clamps to 200 and freezes a 150ms deadline");
	}

	internal static void TestHoldReleaseFreezesGrace()
	{
		Console.WriteLine("[15b] Hold release freezes grace at first contact loss");
		var settings = JudgeSettings.ForPreset(JudgePreset.Hard);

		// A judged Hold head starts in steady contact.
		var contact = HoldContactState.Contact;
		Check(!contact.HasLoss, "successful Hold head starts with steady contact");

		// Releasing while overlapping before the tail must freeze the deadline.
		var released = contact.BeginLoss(10.0, 90.0, settings);
		Check(released.HasLoss && released.ContactLostAt == 10.0 &&
			released.GraceDeadline is { } deadline && Math.Abs(deadline - 10.25) < 1e-12,
			"hold release records the frozen grace deadline");

		// Later losses or BPM changes must not move the original deadline.
		var repeated = released.BeginLoss(11.0, 240.0, settings);
		Check(repeated == released, "later losses or BPM changes keep the original deadline");

		// Advancing beyond the frozen grace must break the Hold before its tail.
		var loss = new SustainJudgementRules.HoldContactLoss(released.ContactLostAt!.Value,
			released.GraceDeadline!.Value - released.ContactLostAt!.Value,
			released.GraceDeadline!.Value);
		Check(!SustainJudgementRules.ShouldBreakHold(10.24, 12.0, loss) &&
			SustainJudgementRules.ShouldBreakHold(10.25, 12.0, loss),
			"released Hold breaks at the frozen grace deadline");
	}
}
