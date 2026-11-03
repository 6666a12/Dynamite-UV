using static CoreTests.CoreTestSupport;
using DynamiteUniverse.Shared.Chart.V2;
using DynamiteUniverse.Shared.Judge;

namespace CoreTests;

/// <summary>
/// v2 曲线族回归：新增缓动曲线的公式、编解码、Scroll/Path 共用进度，以及
/// <see cref="V2PathEvaluator.AddReceiverBoundaries"/> 对余弦类单调曲线的跨帧求根。
/// </summary>
internal static class V2CurveTests
{
	private static readonly V2PathCurve[] PathCurves = Enum.GetValues<V2PathCurve>();
	private static readonly V2ScrollCurve[] ScrollCurves = Enum.GetValues<V2ScrollCurve>();

	/// <summary>入口：由已注册的 <c>V2ChartTests.TestV2CurvesMixerAndDigest</c> 调用，
	/// 因此不需要改动共享的 <c>Program.cs</c>。</summary>
	internal static void RunChecks()
	{
		TestCurveFormulasAndEndpoints();
		TestCurveCodecRoundTrip();
		TestCurveInterpolationPaths();
		TestCurveReceiverBoundaries();
		TestCurveDigestCoverage();
	}

	/// <summary>除 hold 跳变外的曲线都必须单调不减、落在 [0,1] 且不越界。</summary>
	internal static void TestCurveFormulasAndEndpoints()
	{
		Console.WriteLine("[14b] v2 easing formula range, monotonicity and endpoints");
		foreach (var curve in PathCurves)
		{
			if (curve == V2PathCurve.Smooth)
			{
				Check(Throws(() => V2Curves.Progress(curve, 0.5)),
					"smooth has no plain progress and must use the PCHIP evaluator");
				continue;
			}
			if (curve == V2PathCurve.Hold)
			{
				Check(V2Curves.Progress(curve, 0.0) == 0.0 &&
					V2Curves.Progress(curve, 0.999) == 0.0 &&
					V2Curves.Progress(curve, 1.0) == 1.0,
					"hold keeps progress at 0 until the exact endpoint");
				continue;
			}

			Check(Math.Abs(V2Curves.Progress(curve, 0.0)) <= 1e-15 &&
				Math.Abs(V2Curves.Progress(curve, 1.0) - 1.0) <= 1e-15,
				$"{curve}: progress anchors at f(0)=0 and f(1)=1");
			var monotone = true;
			var inRange = true;
			var previous = -1.0;
			for (var step = 0; step <= 1000; step++)
			{
				var progress = V2Curves.Progress(curve, step / 1000.0);
				inRange &= progress >= 0.0 && progress <= 1.0;
				monotone &= progress >= previous;
				previous = progress;
			}
			Check(inRange, $"{curve}: progress stays inside [0,1] with no overshoot");
			Check(monotone, $"{curve}: progress is monotonically non-decreasing");
			Check(Math.Abs(V2Curves.Progress(curve, 0.5) - MidpointOf(curve)) <= 1e-15,
				$"{curve}: midpoint matches the frozen definition");
		}

		foreach (var curve in ScrollCurves)
		{
			Check(Math.Abs(V2Curves.Progress(curve, 0.0)) <= 1e-15 &&
				Math.Abs(V2Curves.Progress(curve, 1.0) - 1.0) <= 1e-15,
				$"scroll {curve}: progress anchors at both ends");
			Check(Math.Abs(V2Curves.Progress(curve, 0.25) -
					V2Curves.Progress((V2PathCurve)(int)curve, 0.25)) <= 0.0,
				$"scroll {curve}: shares the same equation as the path curve of the same name");
		}

		// 新增函数的已知值，防止公式被改成近似曲线。
		Check(Math.Abs(V2Curves.Progress(V2PathCurve.EaseInOutQuad, 0.25) - 0.125) <= 1e-15 &&
			Math.Abs(V2Curves.Progress(V2PathCurve.EaseInOutQuad, 0.75) - 0.875) <= 1e-15,
			"easeInOutQuad follows the piecewise 2u^2 / mirrored definition");
		Check(Math.Abs(V2Curves.Progress(V2PathCurve.EaseInCubic, 0.5) - 0.125) <= 1e-15 &&
			Math.Abs(V2Curves.Progress(V2PathCurve.EaseOutCubic, 0.5) - 0.875) <= 1e-15,
			"easeInCubic/easeOutCubic are exact cubes and complements");
		Check(Math.Abs(V2Curves.Progress(V2PathCurve.EaseInSine, 0.5) - (1.0 - Math.Cos(Math.PI / 4.0))) <= 1e-15 &&
			Math.Abs(V2Curves.Progress(V2PathCurve.EaseOutSine, 0.5) - Math.Sin(Math.PI / 4.0)) <= 1e-15 &&
			Math.Abs(V2Curves.Progress(V2PathCurve.EaseInOutSine, 0.25) - 0.14644660940672624) <= 1e-15,
			"sine easings are the standard cosine/sine definitions");
		Check(Math.Abs(V2Curves.Progress(V2PathCurve.EaseInSine, 0.5) +
				V2Curves.Progress(V2PathCurve.EaseOutSine, 0.5) - 1.0) <= 1e-15,
			"easeInSine and easeOutSine are exact complements");
	}

	/// <summary>六种新曲线在 path 与 scroll 两条通路上都必须能严格编解码往返。</summary>
	internal static void TestCurveCodecRoundTrip()
	{
		Console.WriteLine("[14c] v2 easing codec round trip for path and scroll");
		foreach (var curve in PathCurves)
		{
			var chart = PathCurveChart(curve);
			var encoded = V2JsonEncoder.EncodeChart(chart);
			var decoded = V2JsonDecoder.DecodeChart(encoded, "curve.json");
			var decodedPath = decoded.NotesCenter.OfType<V2PathNote>().Single();
			var decodedNode = decodedPath.Nodes[0];
			Check(decodedPath.CurveToNext == curve && decodedNode.CurveToNext == curve,
				$"{curve}: path head and shape node survive strict encode/decode");
			Check(V2JsonEncoder.EncodeChart(decoded).SequenceEqual(encoded),
				$"{curve}: re-encoding the decoded chart is byte-identical");
		}

		foreach (var curve in ScrollCurves)
		{
			var chart = ScrollCurveChart(curve);
			var decoded = V2JsonDecoder.DecodeChart(V2JsonEncoder.EncodeChart(chart), "scroll.json");
			Check(decoded.ScrollSpeeds[0].CurveToNext == curve,
				$"scroll {curve}: survives strict encode/decode");
		}

		Check(Throws(() => V2JsonDecoder.DecodeChart(ScrollJson("smooth"), "scroll.json"),
				exception: out _),
			"scroll still rejects smooth, which remains path-only");
		Check(Decodes(PathJson("smooth")), "path still accepts smooth");
		Check(Throws(() => V2JsonDecoder.DecodeChart(ScrollJson("easeInOutBack"), "scroll.json"),
				exception: out _),
			"unknown scroll curve names are rejected");
		Check(Throws(() => V2JsonDecoder.DecodeChart(PathJson("easeInOutBack"), "curve.json"),
				exception: out _),
			"unknown path curve names are rejected");

		// 未新增曲线名不得偷偷沿用旧名：每个名字都必须解码成不同的枚举值。
		Check(ScrollCurves.Length == 11 && PathCurves.Length == 12 &&
			PathCurves.Distinct().Count() == 12 && ScrollCurves.Distinct().Count() == 11,
			"v2 exposes eleven scroll curves and twelve path curves including smooth");
	}

	/// <summary>Scroll 与 Path 都必须用同一进度做插值，且不改变命中秒。</summary>
	internal static void TestCurveInterpolationPaths()
	{
		Console.WriteLine("[14d] v2 easing interpolation for scroll and path");
		foreach (var curve in ScrollCurves)
		{
			if (curve == V2ScrollCurve.Hold) continue;
			var map = new V2ScrollMap(new[]
			{
				new V2ScrollEvent { Time = Bar(0), Value = 1.0, CurveToNext = curve },
				new V2ScrollEvent { Time = Bar(1), Value = 3.0 },
			});
			var expected = 1.0 + 2.0 * V2Curves.Progress(curve, 0.5);
			Check(Math.Abs(map.SpeedAt(ExactBarTime.FromFraction(1, 2)) - expected) <= 1e-15,
				$"scroll {curve}: midpoint interpolates the shared progress");
			Check(map.SpeedAt(ExactBarTime.Zero) == 1.0 && map.SpeedAt(Bar(1)) == 3.0,
				$"scroll {curve}: exact event times keep their own values");
		}

		foreach (var curve in PathCurves)
		{
			if (curve == V2PathCurve.Smooth) continue;
			var evaluator = new V2PathEvaluator(PathNote(curve));
			foreach (var u in new[] { 0.25, 0.5, 0.75 })
			{
				var time = ExactBarTime.FromFraction((long)Math.Round(u * 1000), 1000);
				var sample = evaluator.Evaluate(time);
				var progress = V2Curves.Progress(curve, u);
				Check(Math.Abs(sample.Center - (1.0 + 2.0 * progress)) <= 1e-12 &&
					Math.Abs(sample.Width - (1.0 + 2.0 * progress)) <= 1e-12,
					$"path {curve}: center and width share one progress at u={u}");
			}
			Check(evaluator.Evaluate(Bar(0)) == new V2PathSample(1.0, 1.0) &&
				evaluator.Evaluate(Bar(1)) == new V2PathSample(3.0, 3.0),
				$"path {curve}: control points keep their exact own geometry");
		}
	}

	/// <summary>
	/// 关键回归：跨帧路径接触边界不能只对多项式曲线生效。余弦类曲线必须用单调求根，
	/// 且每个求出的切分区间内「接收/失去接触」状态恒定。
	/// </summary>
	internal static void TestCurveReceiverBoundaries()
	{
		Console.WriteLine("[14e] v2 easing receiver boundaries and per-cell contact constancy");
		foreach (var curve in PathCurves)
		{
			// center 0 -> 2、width 恒为 1：左/右边缘分别扫过 receiver，两个方向都要找到。
			var note = new V2PathNote
			{
				Id = "boundary",
				Type = V2NoteType.Mixer,
				Time = Bar(0),
				Center = 0.0,
				Width = 1.0,
				CurveToNext = curve,
				Nodes = [new V2PathNode { Id = "boundary-tail", Time = Bar(1), Center = 2.0, Width = 1.0 }],
			};
			var evaluator = new V2PathEvaluator(note);
			foreach (var receiver in new[] { 0.5, 0.9, 1.5 })
			{
				var cuts = new List<double> { 0.0, 1.0 };
				evaluator.AddReceiverBoundaries(0.0, 1.0, receiver, cuts);
				cuts.Sort();
				Check(cuts.Zip(cuts.Skip(1)).All(pair => pair.Second - pair.First >= -1e-12),
					$"{curve}@{receiver}: boundaries come out sorted and inside the window");
				Check(cuts.All(cut => cut >= 0.0 && cut <= 1.0),
					$"{curve}@{receiver}: boundaries stay inside the sampled span");
				Check(ContactIsConstantPerCell(evaluator, cuts, receiver),
					$"{curve}@{receiver}: every emitted cell has constant contact state");

				// smooth 的接触边界由 PCHIP 多项式切分覆盖（见 ContactIsConstantPerCell）；
				// 这里的独立反解只针对普通进度函数。
				if (curve is V2PathCurve.Hold or V2PathCurve.Smooth) continue;
				foreach (var expected in ExpectedCrossings(curve, receiver))
				{
					Check(cuts.Any(cut => Math.Abs(cut - expected) <= 1e-9),
						$"{curve}@{receiver}: crossing at {expected:R} is not lost; " +
						$"cuts=[{string.Join(",", cuts.Select(c => c.ToString("R")))}]");
				}
			}

			// 变宽路径：边缘偏移仍随单调进度单调，但写法和等宽不同，必须一样可靠。
			var variable = new V2PathNote
			{
				Id = "variable",
				Type = V2NoteType.Hold,
				Time = Bar(0),
				Center = 0.6,
				Width = 1.4,
				CurveToNext = curve,
				Nodes =
				[
					new V2PathNode
					{
						Id = "variable-tail", Time = Bar(1), Center = 2.4, Width = 0.4,
						Judge = true, JudgeWasExplicit = true,
					},
				],
			};
			var variableEvaluator = new V2PathEvaluator(variable);
			foreach (var receiver in new[] { 0.4, 1.2, 2.2 })
			{
				var cuts = new List<double> { 0.0, 1.0 };
				variableEvaluator.AddReceiverBoundaries(0.0, 1.0, receiver, cuts);
				cuts.Sort();
				Check(ContactIsConstantPerCell(variableEvaluator, cuts, receiver),
					$"{curve}@{receiver}: variable width keeps every cell's contact constant");
			}
		}

		// 窗口只在跨帧调用时收窄：切分结果必须与帧无关，且不重复上报窗口外的时刻。
		var sine = new V2PathEvaluator(PathNote(V2PathCurve.EaseInOutSine));
		var whole = new List<double>();
		sine.AddReceiverBoundaries(0.0, 1.0, 1.5, whole);
		var narrow = new List<double>();
		sine.AddReceiverBoundaries(0.4, 0.6, 1.5, narrow);
		var expectedNarrow = whole.Where(cut => cut > 0.4 && cut < 0.6).Order().ToArray();
		Check(narrow.Order().SequenceEqual(expectedNarrow),
			"narrow frame reports exactly the crossings of the whole span it covers");

		// 多段路径：交叉落在第二段时起点扫描不能跳过该段。
		var second = new V2PathNote
		{
			Id = "second",
			Type = V2NoteType.Mixer,
			Time = Bar(0),
			Center = 0.0,
			Width = 1.0,
			CurveToNext = V2PathCurve.Linear,
			Nodes =
			[
				new V2PathNode
				{
					Id = "second-mid", Time = Bar(1), Center = 0.0, Width = 1.0,
					CurveToNext = V2PathCurve.EaseInSine,
				},
				new V2PathNode { Id = "second-tail", Time = Bar(2), Center = 4.0, Width = 1.0 },
			],
		};
		var secondCuts = new List<double>();
		new V2PathEvaluator(second).AddReceiverBoundaries(1.0, 2.0, 3.5, secondCuts);
		Check(secondCuts.Count >= 1 && ContactIsConstantPerCell(
				new V2PathEvaluator(second), secondCuts, 3.5),
			"a crossing in the second interval is found by a mid-path frame window");
	}

	/// <summary>曲线名参与 Gameplay Digest，改曲线必须改摘要。</summary>
	internal static void TestCurveDigestCoverage()
	{
		Console.WriteLine("[14f] v2 easing names are part of the gameplay digest");
		var digests = new Dictionary<V2PathCurve, string>();
		foreach (var curve in PathCurves)
		{
			var chart = PathCurveChart(curve);
			var result = V2GameplayDigest.Compute(chart, JudgePreset.Hard, new string('a', 64));
			digests[curve] = result.Sha256;
			var notes = (IReadOnlyDictionary<string, object?>)result.Projection["notes"]!;
			var center = (IReadOnlyList<object?>)notes["center"]!;
			var hold = (IReadOnlyDictionary<string, object?>)center[0]!;
			Check((string)hold["curveToNext"]! == V2GameplayDigest.PathCurveName(curve),
				$"{curve}: projection records the path curve name");
			var node = (IReadOnlyDictionary<string, object?>)
				((IReadOnlyList<object?>)hold["nodes"]!)[0]!;
			Check((string)node["curveToNext"]! == V2GameplayDigest.PathCurveName(curve),
				$"{curve}: projection records the node curve name");
		}
		Check(digests.Values.Distinct().Count() == digests.Count,
			"every distinct path curve produces a distinct gameplay digest");

		var scrollDigests = ScrollCurves
			.Select(curve => V2GameplayDigest.Compute(ScrollCurveChart(curve), JudgePreset.Hard,
				new string('a', 64)).Sha256)
			.ToArray();
		Check(scrollDigests.Distinct().Count() == ScrollCurves.Length,
			"every distinct scroll curve produces a distinct gameplay digest");
	}

	// ---- helpers ----

	private static double MidpointOf(V2PathCurve curve) => curve switch
	{
		V2PathCurve.Linear => 0.5,
		V2PathCurve.EaseInQuad => 0.25,
		V2PathCurve.EaseOutQuad => 0.75,
		V2PathCurve.EaseInOutQuad => 0.5,
		V2PathCurve.EaseInCubic => 0.125,
		V2PathCurve.EaseOutCubic => 0.875,
		V2PathCurve.EaseInOutCubic => 0.5,
		V2PathCurve.EaseInSine => 1.0 - Math.Cos(Math.PI / 4.0),
		V2PathCurve.EaseOutSine => Math.Sin(Math.PI / 4.0),
		V2PathCurve.EaseInOutSine => 0.5,
		_ => throw new ArgumentOutOfRangeException(nameof(curve)),
	};

	/// <summary>
	/// 独立反解 <c>center±width/2 = receiver</c> 的穿越时刻，用来核对被测求根器没有漏解。
	/// 只返回真正改变接触状态的内部穿越：端点处的切触会让边缘表达式在浮点意义上饱和为 0，
	/// 那不是「漏掉的分段点」，而是由端点/中点显式切分覆盖的情形。</summary>
	private static IReadOnlyList<double> ExpectedCrossings(V2PathCurve curve, double receiver)
	{
		const int samples = 20000;
		var result = new List<double>();
		foreach (var edgeSign in new[] { -0.5, 0.5 })
		{
			double Edge(double u)
			{
				var progress = V2Curves.Progress(curve, u);
				return (0.0 + 2.0 * progress) + edgeSign * (1.0 + 0.0 * progress) - receiver;
			}
			for (var i = 0; i < samples; i++)
			{
				double left = (double)i / samples;
				double right = (double)(i + 1) / samples;
				var vLeft = Edge(left);
				var vRight = Edge(right);
				if (vLeft == 0.0 || vRight == 0.0) continue;
				if (Math.Sign(vLeft) == Math.Sign(vRight)) continue;
				for (var n = 0; n < 200; n++)
				{
					var mid = (left + right) * 0.5;
					if (Math.Sign(Edge(mid)) == Math.Sign(vLeft)) left = mid;
					else right = mid;
				}
				result.Add((left + right) * 0.5);
			}
		}
		result.Sort();
		return result;
	}

	private static bool ContactIsConstantPerCell(V2PathEvaluator evaluator, List<double> cuts,
		double receiver)
	{
		for (var i = 1; i < cuts.Count; i++)
		{
			var span = cuts[i] - cuts[i - 1];
			if (span <= 1e-9) continue;
			bool At(double fraction)
			{
				var sample = evaluator.EvaluateRuntime(cuts[i - 1] + span * fraction);
				return MixerContactGeometry.CanCatch(new NoteBounds(sample.Left, sample.Right), receiver);
			}
			var expected = At(0.5);
			for (var step = 1; step < 64; step++)
				if (At(step / 64.0) != expected) return false;
		}
		return true;
	}

	private static V2PathNote PathNote(V2PathCurve curve) => new()
	{
		Id = "path",
		Type = V2NoteType.Mixer,
		Time = Bar(0),
		Center = 1.0,
		Width = 1.0,
		CurveToNext = curve,
		Nodes = [new V2PathNode { Id = "path-tail", Time = Bar(1), Center = 3.0, Width = 3.0 }],
	};

	private static V2Chart PathCurveChart(V2PathCurve curve) => new()
	{
		ChartId = "curve-path",
		AudioOffsetSec = 0.0,
		Bpms = [new V2BpmEvent(Bar(0), 120.0)],
		NotesLeft = [],
		NotesCenter =
		[
			new V2PathNote
			{
				Id = "hold",
				Type = V2NoteType.Hold,
				Time = Bar(0),
				Center = 1.0,
				Width = 1.0,
				CurveToNext = curve,
				Nodes =
				[
					new V2PathNode
					{
						Id = "hold-shape", Time = Bar(1), Center = 2.0, Width = 2.0,
						CurveToNext = curve, Judge = false, JudgeWasExplicit = true,
					},
					new V2PathNode
					{
						Id = "hold-tail", Time = Bar(2), Center = 3.0, Width = 1.0,
						Judge = true, JudgeWasExplicit = true,
					},
				],
			},
		],
		NotesRight = [],
	};

	private static V2Chart ScrollCurveChart(V2ScrollCurve curve) => new()
	{
		ChartId = "curve-scroll",
		AudioOffsetSec = 0.0,
		Bpms = [new V2BpmEvent(Bar(0), 120.0)],
		ScrollSpeeds =
		[
			new V2ScrollEvent { Time = Bar(0), Value = 1.0, CurveToNext = curve },
			new V2ScrollEvent { Time = Bar(1), Value = 2.0 },
		],
		NotesLeft = [],
		NotesCenter =
		[
			new V2BasicNote
			{
				Id = "tap", Type = V2NoteType.Tap, Time = Bar(0),
				Center = 2.5, Width = 1.0,
			},
		],
		NotesRight = [],
	};

	private static bool Decodes(string json)
	{
		try
		{
			V2JsonDecoder.DecodeChart(json, "curve.json");
			return true;
		}
		catch (V2DiagnosticException)
		{
			return false;
		}
	}

	private static bool Throws(Action action)
	{
		try
		{
			action();
			return false;
		}
		catch (V2DiagnosticException)
		{
			return true;
		}
		catch (ArgumentException)
		{
			return true;
		}
	}

	private static bool Throws(Action action, out object? exception)
	{
		try
		{
			action();
			exception = null;
			return false;
		}
		catch (Exception caught)
		{
			exception = caught;
			return true;
		}
	}

	private static string ScrollJson(string curve) =>
		$$"""
		{
		  "format": "dynamite-uv-chart",
		  "formatVersion": 2,
		  "chartId": "curve-scroll",
		  "audioOffsetSec": 0,
		  "timing": { "bpms": [ { "time": { "bar": 0, "numerator": 0, "denominator": 1 }, "bpm": 120 } ] },
		  "scrollSpeeds": [
		    { "time": { "bar": 0, "numerator": 0, "denominator": 1 }, "value": 1, "curveToNext": "{{curve}}" },
		    { "time": { "bar": 1, "numerator": 0, "denominator": 1 }, "value": 2 }
		  ],
		  "notesLeft": [],
		  "notesCenter": [
		    { "id": "tap", "type": "tap", "time": { "bar": 0, "numerator": 0, "denominator": 1 },
		      "center": 2.5, "width": 1 }
		  ],
		  "notesRight": []
		}
		""";

	private static string PathJson(string curve) =>
		$$"""
		{
		  "format": "dynamite-uv-chart",
		  "formatVersion": 2,
		  "chartId": "curve-path",
		  "audioOffsetSec": 0,
		  "timing": { "bpms": [ { "time": { "bar": 0, "numerator": 0, "denominator": 1 }, "bpm": 120 } ] },
		  "notesLeft": [],
		  "notesCenter": [
		    {
		      "id": "hold", "type": "hold",
		      "time": { "bar": 0, "numerator": 0, "denominator": 1 },
		      "center": 1, "width": 1,
		      "curveToNext": "{{curve}}",
		      "nodes": [
		        { "id": "hold-tail", "time": { "bar": 1, "numerator": 0, "denominator": 1 },
		          "center": 2, "width": 1, "judge": true }
		      ]
		    }
		  ],
		  "notesRight": []
		}
		""";
}
