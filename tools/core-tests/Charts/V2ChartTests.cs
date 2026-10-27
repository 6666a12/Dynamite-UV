using static CoreTests.CoreTestSupport;
using DynamiteUniverse.Shared.Chart;
using DynamiteUniverse.Shared.Chart.V2;
using DynamiteUniverse.Shared.Judge;
using DynamiteUniverse.Shared.Score;

namespace CoreTests;

internal static class V2ChartTests
{

	internal static void TestV2ExactBarTimeAndLegacyConversion()
	{
		Console.WriteLine("[12] v2 exact binary64 legacy conversion");
		var sevenTenths = ExactBarTime.FromDouble(0.7);
		Check(sevenTenths.ImproperNumerator == 3152519739159347 &&
			sevenTenths.Denominator == 4503599627370496,
			"ExactBarTime.FromDouble preserves the exact 0.7 binary64 rational");
		Check(sevenTenths.ToDouble() == 0.7, "exact binary64 rational round trips to 0.7");

		var head = TestNote(1, 2, NoteType.HoldHead, 0.7, -2.0, 7.0, 0.0);
		var tail = TestNote(2, -1, NoteType.HoldNode, 0.875, -1.0, 8.0, 0.0);
		var tap = TestNote(3, -1, NoteType.Tap, 0.75, 4.5, 2.0, 0.0,
			Track.Right);
		var legacy = new Chart
		{
			Name = "legacy",
			Title = "legacy",
			Difficulty = 3,
			TotalMainNote = 0,
			Sections = new[]
			{
				new BarSection { Bpm = 120.0, BarTime = 0.25, Seconds = 1.0 },
				new BarSection { Bpm = 240.0, BarTime = 1.25, Seconds = 3.0 },
			},
			NotesLeft = Array.Empty<Note>(),
			NotesCenter = new[] { head, tail },
			NotesRight = new[] { tap },
			DropSpeeds = new[] { (0.5, 2.0), (0.5, 3.0), (1.0, 4.0) },
		};
		var converted = V2Integration.ConvertLegacyChart(legacy, "legacy-chart");
		Check(converted.Bpms.Count == 3 && converted.Bpms[0].Time == ExactBarTime.Zero &&
			converted.Bpms[0].Bpm == 120.0,
			"legacy first BPM is extrapolated back to bar 0");
		Check(Math.Abs(converted.AudioOffsetSec - 0.5) < 1e-12,
			"legacy audio offset is extrapolated from first section");
		Check(converted.ScrollSpeeds.Count == 3 &&
			converted.ScrollSpeeds[0].Time == ExactBarTime.Zero &&
			converted.ScrollSpeeds[0].Value == 3.0 &&
			converted.ScrollSpeeds[1].Time == ExactBarTime.FromDouble(0.5) &&
			converted.ScrollSpeeds[1].Value == 3.0,
			"duplicate DropSpeed is later-wins and first event is prefixed to bar 0");
		var convertedHold = (V2PathNote)converted.NotesCenter.Single();
		Check(convertedHold.Time == sevenTenths,
			"in-memory converter uses exact binary64 BarTime authority");
		Check(convertedHold.Center == 1.5 && convertedHold.Width == 7.0 &&
			convertedHold.Nodes[0].Center == 3.0 && convertedHold.Nodes[0].Width == 8.0,
			"legacy Position to center retains overscan without clamping");

		// 回归：Hardcore 预设曾命中 "Tutorial cannot be converted to v2" 的 ArgumentException，
		// 导致 Hardcore 模式下全部 legacy 谱面加载失败。Hardcore 按 Hard 归档、Tutorial 按 Casual。
		var hcLoaded = V2Integration.ConvertLegacyPackageChart(legacy,
			"legacy.pack", "legacy-chart", JudgePreset.Hardcore);
		Check(hcLoaded.SemanticPack.Charts[0].Difficulty == V2Difficulty.Hard,
			"Hardcore 预设 legacy->v2 转换按 Hard 难度归档");
		var tutorialLoaded = V2Integration.ConvertLegacyPackageChart(legacy,
			"legacy.pack", "legacy-chart", JudgePreset.Tutorial);
		Check(tutorialLoaded.SemanticPack.Charts[0].Difficulty == V2Difficulty.Casual,
			"Tutorial 预设 legacy->v2 转换按 Casual 难度归档");
	}

	internal static void TestV2GeometryJudgementAndSync()
	{
		Console.WriteLine("[13] v2 geometry, Hold judge, and sync derivation");
		var hold = new V2PathNote
		{
			Id = "hold",
			Type = V2NoteType.Hold,
			Time = Bar(1),
			Center = -2.0,
			Width = 7.0,
			CurveToNext = V2PathCurve.Linear,
			Nodes = new[]
			{
				new V2PathNode
				{
					Id = "shape", Time = Bar(2), Center = 7.0, Width = 8.0,
					CurveToNext = V2PathCurve.Linear, Judge = false,
					JudgeWasExplicit = true,
				},
				new V2PathNode
				{
					Id = "tail", Time = Bar(3), Center = 2.0, Width = 1.0,
					Judge = true, JudgeWasExplicit = true,
				},
			},
		};
		var mixer = new V2PathNote
		{
			Id = "mixer",
			Type = V2NoteType.Mixer,
			Time = Bar(0),
			Center = -3.0,
			Width = 9.0,
			CurveToNext = V2PathCurve.Linear,
			Nodes = new[]
			{
				new V2PathNode
				{
					Id = "mixer-tail", Time = ExactBarTime.FromFraction(3, 8),
					Center = 6.0, Width = 7.0,
				},
			},
		};
		var chart = new V2Chart
		{
			ChartId = "geometry",
			AudioOffsetSec = 0.0,
			Bpms = new[] { new V2BpmEvent(Bar(0), 120.0) },
			NotesLeft = new V2Note[]
			{
				new V2BasicNote
				{
					Id = "tap-left", Type = V2NoteType.Tap, Time = Bar(0),
					Center = 1.0, Width = 1.0,
				},
			},
			NotesCenter = new V2Note[] { mixer, hold },
			NotesRight = new V2Note[]
			{
				new V2BasicNote
				{
					Id = "tap-right-near", Type = V2NoteType.Tap,
					Time = ExactBarTime.FromFraction(1, 9),
					Center = 1.0, Width = 1.0,
				},
			},
		};
		V2SemanticValidator.ValidateChart(chart);
		Check(V2SemanticValidator.CountMainJudgements(chart) == 8,
			"Hold judge:false omits one unit while center Mixer retains exact ticks");
		var adapted = V2RuntimeAdapter.Adapt(chart);
		var plan = JudgePlan.Build(adapted.RuntimeChart,
			JudgeSettings.ForPreset(JudgePreset.Hard));
		Check(plan.Units.All(unit => adapted.Metadata.SourceIdsByRuntimeId[unit.NoteId] != "shape"),
			"Hold shape-only node is absent from runtime JudgePlan");
		Check(adapted.RuntimeChart.AllNotes.Count(note =>
			note.V2Metadata?.SourceId.Contains(".__shape.", StringComparison.Ordinal) == true) == 0,
			"linear paths add no hidden render subdivision and stay legacy-equivalent");
		var holdRuntime = adapted.RuntimeChart.NotesCenter.Single(note =>
			note.V2Metadata?.SourceId == "hold");
		Check(holdRuntime.Position == -5.5 && holdRuntime.Width == 7.0,
			"runtime adapter preserves overscan left edge and width");
		Check(adapted.SyncAccentRuntimeIds.SetEquals(new[]
		{
			adapted.RuntimeIdsBySourceId["tap-left"],
		}), "exact cross-track Mixer/Tap time accents only the Tap");
		Check(!adapted.SyncAccentRuntimeIds.Contains(
			adapted.RuntimeIdsBySourceId["tap-right-near"]),
			"near but unequal exact BarTime is not merged for sync accent");
	}

	internal static void TestV2CurvesMixerAndDigest()
	{
		Console.WriteLine("[14] v2 curves, Mixer ticks, and golden digest");
		var path = new V2PathNote
		{
			Id = "curve",
			Type = V2NoteType.Hold,
			Time = Bar(0),
			Center = 0.0,
			Width = 1.0,
			CurveToNext = V2PathCurve.EaseInQuad,
			Nodes = new[]
			{
				new V2PathNode
				{
					Id = "curve-tail", Time = Bar(1), Center = 4.0, Width = 3.0,
					Judge = true, JudgeWasExplicit = true,
				},
			},
		};
		var sample = new V2PathEvaluator(path).Evaluate(ExactBarTime.FromFraction(1, 2));
		Check(Math.Abs(sample.Center - 1.0) < 1e-12 &&
			Math.Abs(sample.Width - 1.5) < 1e-12,
			"easeInQuad path evaluates center and width at u^2");
		var curveChart = new V2Chart
		{
			ChartId = "curve-chart", AudioOffsetSec = 0.0,
			Bpms = new[] { new V2BpmEvent(Bar(0), 120.0) },
			NotesLeft = Array.Empty<V2Note>(),
			NotesCenter = new V2Note[] { path },
			NotesRight = Array.Empty<V2Note>(),
		};
		var curveRuntime = V2RuntimeAdapter.Adapt(curveChart).RuntimeChart;
		var hidden = curveRuntime.NotesCenter.Where(note =>
			note.V2Metadata?.SourceId.Contains(".__shape.", StringComparison.Ordinal) == true)
			.ToArray();
		Check(hidden.Length > 0 && hidden.All(note => note.V2Metadata?.HoldJudge == false),
			"curved body gets adaptive hidden render points with no Hold judgements");
		var midpoint = hidden.Single(note => note.V2Metadata!.ExactTime ==
			ExactBarTime.FromFraction(1, 2));
		Check(Math.Abs(midpoint.Position - 0.25) < 1e-12 &&
			Math.Abs(midpoint.Width - 1.5) < 1e-12,
			"hidden midpoint follows the authoritative eased center/width sample");
		var scroll = new V2ScrollMap(new[]
		{
			new V2ScrollEvent
			{
				Time = Bar(0), Value = 1.0, CurveToNext = V2ScrollCurve.EaseOutQuad,
			},
			new V2ScrollEvent { Time = Bar(1), Value = 3.0 },
		});
		Check(Math.Abs(scroll.SpeedAt(ExactBarTime.FromFraction(1, 2)) - 2.5) < 1e-12,
			"easeOutQuad scroll midpoint is exact known value");

		var mixer = new V2PathNote
		{
			Id = "ticks", Type = V2NoteType.Mixer, Time = ExactBarTime.FromFraction(1, 10),
			Center = 0.0, Width = 1.0, CurveToNext = V2PathCurve.Linear,
			Nodes = new[]
			{
				new V2PathNode
				{
					Id = "ticks-tail", Time = ExactBarTime.FromFraction(49, 100),
					Center = 0.0, Width = 1.0,
				},
			},
		};
		var ticks = V2MixerTicks.Enumerate(mixer).ToArray();
		Check(ticks.Length == 3 && ticks[0] == ExactBarTime.FromFraction(9, 40) &&
			ticks[2] == ExactBarTime.FromFraction(19, 40),
			"Mixer exact grid is head-relative k/8 and omits non-grid tail");

		var root = FindRepositoryRoot();
		var vectorPath = Path.Combine(root, "schemas", "chart-format-v2", "vectors",
			"gameplay-digest-v1.json");
		using var vector = System.Text.Json.JsonDocument.Parse(File.ReadAllText(vectorPath));
		var vectorRoot = vector.RootElement;
		var expected = vectorRoot.GetProperty("expectedSha256").GetString();
		var projection = vectorRoot.GetProperty("projection");
		var actual = V2GameplayDigest.Sha256Hex(
			System.Text.Encoding.UTF8.GetBytes(JsonCanonicalizer.Canonicalize(projection)));
		Check(actual == expected, "C# RFC8785 canonicalizer matches golden gameplay digest vector");
	}
}
