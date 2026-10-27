using static EditorTestSupport;
using DynamiteUniverse.ChartEditor.Core;
using DynamiteUniverse.Shared.Chart;
using DynamiteUniverse.Shared.Chart.V2;

internal static class EditorPathTests
{

    internal static void TestAllSevenNoteTypes()
    {
        var document = CreateCleanDocument("editor.types", "hard", "Types", "Community");
        var types = Enum.GetValues<V2NoteType>();
        Check(types.Length == 7, "v2 exposes seven note types");
        for (var index = 0; index < types.Length; index++)
        {
            var type = types[index];
            var note = new EditableNote
            {
                Id = $"type-{index}",
                Type = type,
                Track = (EditorTrack)(index % 3),
                Time = ExactBarTime.FromFraction(index, 2),
                Center = 1 + index * .25,
                Width = .75,
            };
            if (type is V2NoteType.Hold or V2NoteType.Mixer)
            {
                note.CurveToNext = V2PathCurve.EaseInQuad;
                note.Nodes.Add(new EditablePathNode
                {
                    Id = $"type-{index}-tail",
                    Time = note.Time + ExactBarTime.FromFraction(1, 2),
                    Center = note.Center + .25,
                    Width = 1,
                    Judge = type == V2NoteType.Hold ? true : null,
                });
            }
            document.Execute(new AddNoteCommand(note));
        }

        var snapshot = document.BuildChartSnapshot("hard");
        V2SemanticValidator.ValidateChart(snapshot, "seven-types.json");
        var roundTrip = V2JsonDecoder.DecodeChart(V2JsonEncoder.EncodeChart(snapshot), "seven-types.json");
        Check(roundTrip.AllNotes.Select(item => item.Note.Type).Order()
                .SequenceEqual(types.Order()),
            "strict snapshot keeps all seven note types");

        document.Execute(new ChangeNoteTypeCommand("type-0", V2NoteType.Hold,
            new EditablePathNode
            {
                Id = "type-0-tail",
                Time = ExactBarTime.FromFraction(1, 4),
                Center = 1.25,
                Width = 1,
            }));
        var changed = document.RequireTestNote("type-0");
        Check(changed.Type == V2NoteType.Hold && changed.Nodes.Count == 1 &&
              changed.Nodes[0].Judge == true,
            "basic note converts to Hold with valid terminal semantics");
        document.Undo();
        Check(document.RequireTestNote("type-0").Type == V2NoteType.Tap &&
              document.RequireTestNote("type-0").Nodes.Count == 0,
            "undo restores basic note type exactly");
        document.Redo();
        document.Execute(new ChangeNoteTypeCommand("type-0", V2NoteType.Mixer));
        Check(document.RequireTestNote("type-0").Type == V2NoteType.Mixer &&
              document.RequireTestNote("type-0").Nodes.All(node => node.Judge is null),
            "Hold converts to Mixer and strips judge state");
        document.Execute(new ChangeNoteTypeCommand("type-0", V2NoteType.ExTap));
        Check(document.RequireTestNote("type-0").Nodes.Count == 0 &&
              document.RequireTestNote("type-0").CurveToNext is null,
            "path note converts to basic and strips path data");
    }

    internal static void TestPathEditingCommands()
    {
        var document = CreateCleanDocument("editor.path", "hard", "Paths", "Community");
        var hold = new EditableNote
        {
            Id = "hold-path",
            Type = V2NoteType.Hold,
            Track = EditorTrack.Center,
            Time = ExactBarTime.Zero,
            Center = 2,
            Width = 1,
            CurveToNext = V2PathCurve.Linear,
        };
        hold.Nodes.Add(new EditablePathNode
        {
            Id = "hold-tail",
            Time = ExactBarTime.FromFraction(2, 1),
            Center = 3,
            Width = 1,
            Judge = true,
        });
        document.Execute(new AddNoteCommand(hold));
        document.Execute(new AddPathNodeCommand(hold.Id, new EditablePathNode
        {
            Id = "hold-mid",
            Time = ExactBarTime.FromFraction(1, 1),
            Center = 2.5,
            Width = .8,
            CurveToNext = V2PathCurve.EaseOutQuad,
            Judge = false,
        }));
        Check(document.RequireTestNote(hold.Id).Nodes.Select(node => node.Id)
                .SequenceEqual(["hold-mid", "hold-tail"]),
            "add path node inserts in strict time order");
        Check(document.Selection.Kind == EditorSelectionKind.PathNode && document.Selection.Id == "hold-mid" &&
              document.Selection.Key == document.RequireTestNote(hold.Id).Nodes.Single(node => node.Id == "hold-mid").Key,
            "path node selection uses stable ID");
        var nodeKey = document.RequireTestNote(hold.Id).Nodes.Single(node => node.Id == "hold-mid").Key;
        document.Execute(new RenamePathNodeCommand(hold.Id, "hold-mid", "hold-control"));
        Check(document.RequireTestNote(hold.Id).Nodes.Single(node => node.Id == "hold-control").Key == nodeKey,
            "path node identity command preserves stable key");
        document.Undo();
        document.Redo();
        document.Undo();

        document.Execute(new EditPathNodeCommand(hold.Id, "hold-mid",
            ExactBarTime.FromFraction(3, 2), 2.75, .9));
        document.Execute(new SetPathCurveCommand(hold.Id, V2PathCurve.Smooth));
        document.Execute(new SetPathCurveCommand(hold.Id, "hold-mid", V2PathCurve.Hold));
        document.Execute(new SetHoldJudgeCommand(hold.Id, "hold-mid", true));
        var edited = document.RequireTestNote(hold.Id);
        Check(edited.Nodes[0].Time == ExactBarTime.FromFraction(3, 2) &&
              edited.CurveToNext == V2PathCurve.Smooth &&
              edited.Nodes[0].CurveToNext == V2PathCurve.Hold &&
              edited.Nodes[0].Judge == true,
            "edit path node curve and Hold judge commands");

        ExpectException<InvalidOperationException>(() => document.Execute(
                new EditPathNodeCommand(hold.Id, "hold-mid",
                    ExactBarTime.FromFraction(5, 2), 2, 1)),
            "path node cannot cross its successor");
        ExpectException<InvalidOperationException>(() => document.Execute(
                new SetPathCurveCommand(hold.Id, "hold-tail", V2PathCurve.Linear)),
            "tail path node cannot gain an outgoing curve");
        ExpectException<InvalidOperationException>(() => document.Execute(
                new SetHoldJudgeCommand(hold.Id, "hold-tail", false)),
            "Hold tail cannot disable judge");

        document.Execute(new DeletePathNodeCommand(hold.Id, "hold-tail"));
        edited = document.RequireTestNote(hold.Id);
        Check(edited.Nodes.Count == 1 && edited.Nodes[0].Id == "hold-mid" &&
              edited.Nodes[0].CurveToNext is null && edited.Nodes[0].Judge == true,
            "deleting path tail promotes a valid Hold tail");
        V2SemanticValidator.ValidateChart(document.BuildChartSnapshot("hard"), "path.json");
        document.Undo();
        edited = document.RequireTestNote(hold.Id);
        Check(edited.Nodes.Count == 2 && edited.Nodes[0].CurveToNext == V2PathCurve.Hold &&
              edited.Nodes[0].Judge == true && edited.Nodes[1].Judge == true,
            "undo tail deletion restores path semantics");
        document.Redo();
        Check(document.RequireTestNote(hold.Id).Nodes.Count == 1,
            "redo tail deletion is stable");
        ExpectException<InvalidOperationException>(() => document.Execute(
                new DeletePathNodeCommand(hold.Id, "hold-mid")),
            "path cannot delete its only terminal");
    }

    /// <summary>路径投影（编辑器画布的真实 body）：细分点必须在 shared 求值器给出的曲线上，
    /// 控制点必须原样保留，细分误差受容差约束，线性段不产生多余细分。</summary>
    internal static void TestPathProjection()
    {
        var head = new EditorPathPoint(ExactBarTime.Zero, 2d, 1d, V2PathCurve.Smooth);
        var middle = new EditorPathPoint(ExactBarTime.FromFraction(1, 1), 3.2d, .7d, V2PathCurve.Smooth);
        var tail = new EditorPathPoint(ExactBarTime.FromFraction(2, 1), 3.6d, .5d, null);
        var shape = new EditorPathShape(head, [middle, tail]);
        var flattened = EditorPathProjection.Flatten(shape, 273.2d, 273.2d);

        Check(flattened.Count > 3, $"smooth path flattens into subdivided segments ({flattened.Count} points)");
        Check(flattened[0] == new EditorPathSample(head.Time, head.Center, head.Width, true) &&
              flattened[^1] == new EditorPathSample(tail.Time, tail.Center, tail.Width, true) &&
              flattened.Any(sample => sample.IsControlPoint &&
                  sample.Time == middle.Time && sample.Center == middle.Center && sample.Width == middle.Width),
            "flattened path keeps head, every control point and tail at their exact values");
        Check(flattened.Zip(flattened.Skip(1)).All(pair => pair.First.Time < pair.Second.Time),
            "flattened path times strictly increase");
        Check(!flattened.Any(sample => sample.Width <= 0d) &&
              !flattened.Any(sample => !double.IsFinite(sample.Center) || !double.IsFinite(sample.Width)),
            "flattened path stays finite and positive");

        var evaluator = new V2PathEvaluator(ReferencePathNote(head, middle, tail));
        var offCurve = flattened.Max(sample =>
        {
            var expected = evaluator.Evaluate(sample.Time);
            return Math.Max(Math.Abs(sample.Center - expected.Center), Math.Abs(sample.Width - expected.Width));
        });
        Check(offCurve <= 1e-12, $"every flattened point sits on the shared evaluator (max {offCurve:e2})");
        var sagitta = flattened.Zip(flattened.Skip(1)).Max(pair =>
        {
            var midpoint = pair.First.Time + (pair.Second.Time - pair.First.Time) * ExactBarTime.FromFraction(1, 2);
            var expected = evaluator.Evaluate(midpoint);
            return Math.Abs(expected.Center - (pair.First.Center + pair.Second.Center) / 2d) * 273.2d +
                Math.Abs(expected.Width - (pair.First.Width + pair.Second.Width) / 2d) * 273.2d / 2d;
        });
        Check(sagitta <= EditorPathProjection.SubdivisionTolerancePx,
            $"subdivision keeps the chord error inside the shared tolerance (max {sagitta:0.###}px <= {EditorPathProjection.SubdivisionTolerancePx}px)");

        var linear = EditorPathProjection.Flatten(new EditorPathShape(
            new EditorPathPoint(ExactBarTime.Zero, 1d, .5d, V2PathCurve.Linear),
            [new EditorPathPoint(ExactBarTime.FromFraction(4, 1), 3d, 2d, null)]), 273.2d, 273.2d);
        Check(linear.Count == 2 && linear.All(sample => sample.IsControlPoint),
            "linear paths flatten to head plus tail only");

        var probe = ExactBarTime.FromFraction(3, 2);
        var sampleAtNode = EditorPathProjection.Sample(shape, middle.Time);
        var sampleAtProbe = EditorPathProjection.Sample(shape, probe);
        var expectedProbe = evaluator.Evaluate(probe);
        Check(sampleAtNode == (middle.Center, middle.Width) &&
              sampleAtProbe == (expectedProbe.Center, expectedProbe.Width),
            "path sampling matches the shared evaluator bit for bit");
        Check(EditorPathProjection.Sample(shape, ExactBarTime.FromFraction(9, 1)) == (tail.Center, tail.Width) &&
              EditorPathProjection.Sample(shape, -ExactBarTime.One) == (head.Center, head.Width),
            "path sampling clamps outside the path span");

        var single = EditorPathProjection.Flatten(new EditorPathShape(head, []), 273.2d, 273.2d);
        Check(single.Count == 1 && single[0].Time == head.Time,
            "a path without nodes projects to its head alone");
    }

    /// <summary>双击插入节点的命令层判据：采样值写回、插入即选中、形状零跳变（插入时刻逐位
    /// 相同），线性区间被切开后完全不变，平滑区间只允许亚像素级形变。</summary>
    internal static void TestPathNodeInsertion()
    {
        var document = CreateCleanDocument("editor.path-insert", "hard", "Path Insert", "Community");
        var hold = new EditableNote
        {
            Id = "hold-insert",
            Type = V2NoteType.Hold,
            Track = EditorTrack.Center,
            Time = ExactBarTime.Zero,
            Center = 2,
            Width = 1,
            CurveToNext = V2PathCurve.Smooth,
            // 起点贴边：v2 允许 overscan，插入节点必须写回未钳制的采样值。
        };
        hold.Nodes.Add(new EditablePathNode
        {
            Id = "hold-insert-mid",
            Time = ExactBarTime.FromFraction(1, 1),
            Center = 3.4,
            Width = .6,
            CurveToNext = V2PathCurve.Smooth,
            Judge = true,
        });
        hold.Nodes.Add(new EditablePathNode
        {
            Id = "hold-insert-tail",
            Time = ExactBarTime.FromFraction(2, 1),
            Center = 4.4,
            Width = .4,
            Judge = true,
        });
        document.Execute(new AddNoteCommand(hold));
        var controller = new CanvasAuthoringController(document);
        var insertion = ExactBarTime.FromFraction(3, 2);
        var before = EditorPathProjection.Sample(EditorPathProjection.ShapeOf(document.RequireTestNote("hold-insert")), insertion);

        var nodeId = controller.AddPathNode("hold-insert", insertion, before.Center, before.Width,
            V2PathCurve.Smooth);
        var stored = document.RequireTestNote("hold-insert");
        Check(document.Selection.Kind == EditorSelectionKind.PathNode &&
              document.Selection.Id == nodeId &&
              document.Selection.Key == stored.Nodes.Single(node => node.Id == nodeId).Key,
            "inserted path node becomes the selected document entity");
        Check(stored.Nodes.Select(node => node.Time)
                .SequenceEqual([ExactBarTime.FromFraction(1, 1), insertion, ExactBarTime.FromFraction(2, 1)]),
            "insertion keeps strictly increasing node order at the sampled time");
        var inserted = stored.Nodes.Single(node => node.Id == nodeId);
        Check(inserted.Center == before.Center && inserted.Width == before.Width && inserted.Judge == true,
            $"inserted node keeps the sampled geometry unclamped (x{inserted.Center:0.###} · w{inserted.Width:0.###})");

        var after = EditorPathProjection.Sample(EditorPathProjection.ShapeOf(stored), insertion);
        Check(after == before, "the path passes through the inserted node unchanged (zero jump)");
        var smoothDrift = PathDeviationPx(EditorPathProjection.ShapeOf(document.RequireTestNote("hold-insert")),
            ExpectedSmoothSource());
        Check(smoothDrift <= 4d,
            $"splitting one smooth interval keeps the shape continuous (max drift {smoothDrift:0.###}px)");
        V2SemanticValidator.ValidateChart(document.BuildChartSnapshot("hard"), "path-insert.json");

        // 线性区间：切开后两半仍是同一条直线，形状逐位不变。
        var linearDocument = CreateCleanDocument("editor.linear-insert", "hard", "Linear Insert", "Community");
        var linearHold = new EditableNote
        {
            Id = "hold-linear",
            Type = V2NoteType.Hold,
            Track = EditorTrack.Left,
            Time = ExactBarTime.Zero,
            Center = 1,
            Width = .5,
        };
        linearHold.Nodes.Add(new EditablePathNode
        {
            Id = "hold-linear-tail",
            Time = ExactBarTime.FromFraction(2, 1),
            Center = 3,
            Width = 1.5,
            Judge = true,
        });
        linearDocument.Execute(new AddNoteCommand(linearHold));
        var linearController = new CanvasAuthoringController(linearDocument);
        var linearInsertion = ExactBarTime.FromFraction(5, 4);
        var linearBefore = EditorPathProjection.Sample(
            EditorPathProjection.ShapeOf(linearDocument.RequireTestNote("hold-linear")), linearInsertion);
        linearController.AddPathNode("hold-linear", linearInsertion, linearBefore.Center, linearBefore.Width);
        var linearAfter = EditorPathProjection.ShapeOf(linearDocument.RequireTestNote("hold-linear"));
        var linearDrift = 0d;
        for (var step = 0; step <= 16; step++)
        {
            var time = ExactBarTime.FromFraction(step, 8);
            var reference = EditorPathProjection.Sample(EditorPathProjection.ShapeOf(linearHold), time);
            var candidate = EditorPathProjection.Sample(linearAfter, time);
            linearDrift = Math.Max(linearDrift, Math.Abs(reference.Center - candidate.Center) * 115d +
                Math.Abs(reference.Width - candidate.Width) * 102d / 2d);
        }
        Check(linearDrift <= 1e-9,
            $"splitting a linear interval leaves the path bit-identical (max drift {linearDrift:e2}px)");

        ExpectException<InvalidOperationException>(() => linearController.AddPathNode("hold-linear",
                ExactBarTime.FromFraction(2, 1), 3, 1.5),
            "a second node cannot be inserted at an existing node time");
    }

    /// <summary>测试用的 shared 路径模型：与 <see cref="EditorPathProjection"/> 合成的中间表示
    /// 同形，用来独立复算求值结果。</summary>
    internal static V2PathNote ReferencePathNote(params EditorPathPoint[] points)
    {
        var nodes = new V2PathNode[points.Length - 1];
        for (var index = 1; index < points.Length; index++)
            nodes[index - 1] = new V2PathNode
            {
                Id = $"node-{index}",
                Time = points[index].Time,
                Center = points[index].Center,
                Width = points[index].Width,
                CurveToNext = points[index].CurveToNext,
            };
        return new V2PathNote
        {
            Id = "path",
            Type = V2NoteType.Hold,
            Time = points[0].Time,
            Center = points[0].Center,
            Width = points[0].Width,
            CurveToNext = points[0].CurveToNext,
            Nodes = nodes,
        };
    }

    /// <summary>插入节点前后在同一组探针时刻上的最大偏差（像素）。</summary>
    internal static double PathDeviationPx(EditorPathShape actual, EditorPathShape reference)
    {
        var maximum = 0d;
        for (var step = 0; step <= 32; step++)
        {
            var time = ExactBarTime.FromFraction(step, 16);
            var expected = EditorPathProjection.Sample(reference, time);
            var candidate = EditorPathProjection.Sample(actual, time);
            maximum = Math.Max(maximum, Math.Abs(expected.Center - candidate.Center) * 273.2d +
                Math.Abs(expected.Width - candidate.Width) * 273.2d / 2d);
        }
        return maximum;
    }

    /// <summary>插入前的路径快照（头 + 两个 node），偏差对比的基准。</summary>
    internal static EditorPathShape ExpectedSmoothSource() => new(
        new EditorPathPoint(ExactBarTime.Zero, 2d, 1d, V2PathCurve.Smooth),
        [
            new EditorPathPoint(ExactBarTime.FromFraction(1, 1), 3.4d, .6d, V2PathCurve.Smooth),
            new EditorPathPoint(ExactBarTime.FromFraction(2, 1), 4.4d, .4d, null),
        ]);
}
