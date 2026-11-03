using static EditorTestSupport;
using DynamiteUniverse.ChartEditor.Core;
using DynamiteUniverse.Shared.Chart.V2;

/// <summary>
/// 制谱器侧的曲线回归：新增六种缓动曲线必须能被命令层接受、能往返保存，
/// 并且在画布投影里按同一条 shared 求值器展开。
/// </summary>
internal static class EditorCurveTests
{
    private static readonly V2PathCurve[] NewPathCurves =
    [
        V2PathCurve.EaseInOutQuad,
        V2PathCurve.EaseInCubic,
        V2PathCurve.EaseOutCubic,
        V2PathCurve.EaseInSine,
        V2PathCurve.EaseOutSine,
        V2PathCurve.EaseInOutSine,
    ];

    private static readonly V2ScrollCurve[] NewScrollCurves =
    [
        V2ScrollCurve.EaseInOutQuad,
        V2ScrollCurve.EaseInCubic,
        V2ScrollCurve.EaseOutCubic,
        V2ScrollCurve.EaseInSine,
        V2ScrollCurve.EaseOutSine,
        V2ScrollCurve.EaseInOutSine,
    ];

    internal static void TestNewCurvesInCommandsAndPersistence()
    {
        var document = CreateCleanDocument("editor.curves", "hard", "Curves", "Community");
        var hold = new EditableNote
        {
            Id = "curve-hold",
            Type = V2NoteType.Hold,
            Track = EditorTrack.Center,
            Time = ExactBarTime.Zero,
            Center = 1,
            Width = 1,
            CurveToNext = V2PathCurve.EaseInSine,
        };
        hold.Nodes.Add(new EditablePathNode
        {
            Id = "curve-hold-mid",
            Time = ExactBarTime.FromFraction(1, 1),
            Center = 2,
            Width = 1,
            CurveToNext = V2PathCurve.EaseInOutSine,
            Judge = false,
        });
        hold.Nodes.Add(new EditablePathNode
        {
            Id = "curve-hold-tail",
            Time = ExactBarTime.FromFraction(2, 1),
            Center = 3,
            Width = 1,
            Judge = true,
        });
        document.Execute(new AddNoteCommand(hold));

        // 每个新增曲线名都必须能作为「下一段」写进文档并通过严格校验。
        foreach (var curve in NewPathCurves)
        {
            var nodeId = hold.Nodes[0].Id;
            document.Execute(new SetPathCurveCommand("curve-hold", nodeId, curve));
            Check(document.RequireTestNote("curve-hold").Nodes[0].CurveToNext == curve,
                $"path segment accepts {curve}");
            var snapshot = document.BuildChartSnapshot("hard");
            V2SemanticValidator.ValidateChart(snapshot, "editor-curves.json");
        }

        // Scroll 事件必须从 bar 0 起严格递增：先建首事件，再依次把新曲线写成「下一段」。
        // 只有非尾事件能携带曲线，所以先建齐全部事件再改中间段。
        document.Execute(new AddScrollCommand(ExactBarTime.Zero, 1.0));
        for (var index = 1; index <= NewScrollCurves.Length + 1; index++)
            document.Execute(new AddScrollCommand(ExactBarTime.FromFraction(index, 4), 1.5));
        // 每个非尾事件改写成一种新曲线，保证六种曲线同时出现在同一条时间线里。
        for (var index = 0; index < NewScrollCurves.Length; index++)
        {
            var curve = NewScrollCurves[index];
            var time = ExactBarTime.FromFraction(index + 1, 4);
            document.Execute(new EditScrollCommand(time, time, 1.5, curve));
            Check(document.SelectedChart.ScrollSpeeds
                    .Single(scroll => scroll.Time == time)
                    .CurveToNext == curve,
                $"scroll segment accepts {curve}");
            V2SemanticValidator.ValidateChart(document.BuildChartSnapshot("hard"),
                "editor-curves.json");
        }

        var stored = document.BuildChartSnapshot("hard");
        V2SemanticValidator.ValidateChart(stored, "editor-curves.json");
        var roundTrip = V2JsonDecoder.DecodeChart(V2JsonEncoder.EncodeChart(stored),
            "editor-curves.json");
        var decoded = roundTrip.NotesCenter.OfType<V2PathNote>().Single();
        Check(NewPathCurves.Contains(decoded.CurveToNext!.Value),
            "head curve survives the strict editor save/load round trip");
        var scrollCurves = roundTrip.ScrollSpeeds
            .Select(scroll => scroll.CurveToNext)
            .Where(curve => curve is not null)
            .Select(curve => curve!.Value)
            .ToArray();
        Check(NewScrollCurves.All(curve => scrollCurves.Contains(curve)),
            "every new scroll curve survives the strict editor save/load round trip");

        // 未新增的枚举值不得被命令层悄悄接受成有效曲线名。
        Check(Enum.IsDefined(V2ScrollCurve.EaseInOutSine) &&
              !Enum.IsDefined((V2ScrollCurve)999),
            "command-layer enum guard still rejects unknown curve values");
    }

    internal static void TestNewCurvesProjectThroughSharedEvaluator()
    {
        var head = new EditorPathPoint(ExactBarTime.Zero, 1d, 1d, V2PathCurve.EaseInOutSine);
        var tail = new EditorPathPoint(ExactBarTime.FromFraction(2, 1), 3d, 3d, null);
        var shape = new EditorPathShape(head, [tail]);
        var flattened = EditorPathProjection.Flatten(shape, 273.2d, 273.2d);
        Check(flattened.Count > 2,
            "non-linear easing flattens into subdivided segments");

        var note = new V2PathNote
        {
            Id = "projection",
            Type = V2NoteType.Hold,
            Time = head.Time,
            Center = head.Center,
            Width = head.Width,
            CurveToNext = head.CurveToNext,
            Nodes =
            [
                new V2PathNode
                {
                    Id = "projection-tail", Time = tail.Time, Center = tail.Center,
                    Width = tail.Width, Judge = true, JudgeWasExplicit = true,
                },
            ],
        };
        var evaluator = new V2PathEvaluator(note);
        var offCurve = flattened.Max(sample =>
        {
            var expected = evaluator.Evaluate(sample.Time);
            return Math.Max(Math.Abs(sample.Center - expected.Center),
                Math.Abs(sample.Width - expected.Width));
        });
        Check(offCurve <= 1e-12,
            $"every flattened point sits on the shared evaluator (max {offCurve:e2})");
        var sagitta = flattened.Zip(flattened.Skip(1)).Max(pair =>
        {
            var midpoint = pair.First.Time +
                (pair.Second.Time - pair.First.Time) * ExactBarTime.FromFraction(1, 2);
            var expected = evaluator.Evaluate(midpoint);
            return Math.Abs(expected.Center - (pair.First.Center + pair.Second.Center) / 2d) * 273.2d +
                Math.Abs(expected.Width - (pair.First.Width + pair.Second.Width) / 2d) * 273.2d / 2d;
        });
        Check(sagitta <= EditorPathProjection.SubdivisionTolerancePx,
            $"eased path stays inside the shared chord tolerance (max {sagitta:0.###}px)");
    }
}
