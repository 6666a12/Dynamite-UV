using DynamiteUniverse.ChartEditor.Core;
using DynamiteUniverse.Shared.Chart.V2;
using static EditorTestSupport;

internal static class CustomCurveDocumentTests
{
    private const string HeadExpr = "t^1.5", NodeExpr = "(t + t*t)/2", ScrollExpr = "t+4*t*(1-t)";
    private static ExactBarTime At(int eighths) => ExactBarTime.FromFraction(eighths, 8);

    internal static void Run()
    {
        WithExternalResources(RoundTrip);
        RejectAggregateBudget();
        var doc = CreateCleanDocument("test.custom-neighbours", "hard", "Neighbours", "Community");
        doc.Execute(new AddScrollCommand(At(0), 1));
        doc.Execute(new AddScrollCommand(At(4), 2));
        doc.Execute(new AddScrollCommand(At(8), 64));
        doc.Execute(new EditScrollCommand(At(0), At(0), 1, V2ScrollCurve.Custom, ScrollExpr));
        Reject(() => doc.Execute(new DeleteScrollCommand(At(4))));
        Check(doc.SelectedChart.ScrollSpeeds.Count == 3 && doc.SelectedChart.ScrollSpeeds[0].CustomExpr == ScrollExpr,
            "deleting an event cannot splice a custom segment onto an illegal target speed");
    }

    private static void RejectAggregateBudget()
    {
        var d = CreateCleanDocument("test.custom-budget", "hard", "Budget", "Community");
        var path = new EditableNote { Id = "hold", Type = V2NoteType.Hold, Track = EditorTrack.Center,
            Time = At(0), Width = 1, Center = 1, CurveToNext = V2PathCurve.Custom, CustomExpr = "t+0*0" };
        for (int i = 1; i < 128; ++i)
            path.Nodes.Add(new EditablePathNode { Id = $"p-{i}", Time = At(i), Width = 1, Center = 1,
                Judge = false, CurveToNext = V2PathCurve.Custom, CustomExpr = $"t+0*{i}" });
        path.Nodes.Add(new EditablePathNode { Id = "tail", Time = At(128), Center = 1, Width = 1, Judge = true });
        d.Execute(new AddNoteCommand(path));
        d.Execute(new AddScrollCommand(At(0), 1));
        d.Execute(new AddScrollCommand(At(2), 2));
        var version = d.ChangeVersion;
        var selection = d.Selection;
        Reject(() => d.Execute(new EditScrollCommand(At(0), At(0), 1, V2ScrollCurve.Custom, "t*t")));
        Check(d.SelectedChart.ScrollSpeeds[0].CurveToNext is null && d.SelectedChart.ScrollSpeeds[0].CustomExpr is null &&
            d.ChangeVersion == version && d.Selection == selection,
            "a legal Scroll exceeding the combined chart formula budget rolls back the edit and selection");
        d.Undo();
        Check(d.SelectedChart.ScrollSpeeds.Count == 1, "a rejected global-budget edit never enters undo history");
    }

    private static void RoundTrip(string root, string audio, string cover)
    {
        var dir = Path.Combine(root, "custom-package");
        var session = new EditorDocumentSession();
        var d = session.CreateDraft(BuildDraftRequest(audio, cover));
        AddTap(d, "tap");
        session.Save(dir);
        var oldBytes = File.ReadAllBytes(Path.Combine(dir, d.SelectedChart.File));
        var digestAudio = new string('1', 64);
        var oldDigest = EditorGameplayDigest.Compute(d, digestAudio).Sha256;
        Check(d.SourceFormatMinor == 0 && d.FormatMinor == 0, "plain package remains 2.0");

        d.Execute(new CopyChartCommand("hard", "custom", "charts/custom.json"));
        var c = new CanvasAuthoringController(d);
        var hold = c.AddNote(V2NoteType.Hold, EditorTrack.Center, At(0), 2, 1, At(4));
        var mid = c.AddPathNode(hold, At(2), 3, 1, V2PathCurve.Linear, false);
        EditableNote H() => d.RequireTestNote(hold);
        d.Execute(new SetPathCurveCommand(hold, V2PathCurve.Custom, HeadExpr));
        Check(d.SourceFormatMinor == 0 && d.FormatMinor == 1 && d.SelectedChart.FormatMinor == 1,
            "editing a custom curve proposes a format upgrade without marking the file as already upgraded");
        d.Undo();
        Check(d.FormatMinor == 0 && H().CustomExpr is null, "undoing first custom curve restores 2.0");
        d.Redo();
        c.SetPathNodeCurve(hold, mid, V2PathCurve.Custom, NodeExpr);
        c.DeletePathNode(hold, H().Nodes[^1].Id);
        Check(H().Nodes[0].CurveToNext is null && H().Nodes[0].CustomExpr is null && H().Nodes[0].Judge == true,
            "deleting a tail clears both curve fields on the new terminal node");
        d.Undo();
        Check(H().Nodes[0].CustomExpr == NodeExpr && H().Nodes[0].Judge == false,
            "undo tail promotion restores the node expression and judge flag");
        d.Execute(new ChangeNoteTypeCommand(hold, V2NoteType.Tap));
        Check(H().CustomExpr is null && H().Nodes.Count == 0, "path to basic clears all custom payloads");
        d.Undo();
        d.Execute(new SetPathCurveCommand(hold, V2PathCurve.Hold));
        Check(H().CustomExpr is null, "switching to a built-in drops the old source");
        d.Undo();
        var ver = d.ChangeVersion;
        Reject(() => d.Execute(new SetPathCurveCommand(hold, V2PathCurve.Linear, HeadExpr)));
        Reject(() => d.Execute(new SetPathCurveCommand(hold, V2PathCurve.Custom, "t+")));
        Reject(() => c.SetPathNodeCurve(hold, H().Nodes[^1].Id, V2PathCurve.Custom, HeadExpr));
        Check(d.ChangeVersion == ver && H().CustomExpr == HeadExpr && H().Nodes[0].CustomExpr == NodeExpr,
            "invalid curve edits leave the source and history untouched");

        d.Execute(new AddScrollCommand(At(0), 1));
        d.Execute(new AddScrollCommand(At(6), 3));
        d.Execute(new AddScrollCommand(At(3), 2, V2ScrollCurve.Custom, ScrollExpr));
        d.Execute(new DeleteScrollCommand(At(6)));
        Check(d.SelectedChart.ScrollSpeeds[^1].CustomExpr is null, "promoted final Scroll has no customExpr");
        d.Undo();
        d.Execute(new EditScrollCommand(At(3), At(2), 2, V2ScrollCurve.Linear));
        Check(d.SelectedChart.ScrollSpeeds[1].CustomExpr is null, "retimed built-in Scroll does not carry a stale formula");
        d.Undo();
        Check(d.SelectedChart.ScrollSpeeds[1].Time == At(3) && d.SelectedChart.ScrollSpeeds[1].CustomExpr == ScrollExpr,
            "Scroll edit undo restores original time and formula");

        var clipboard = new EditorNoteClipboard();
        clipboard.Copy(d, [hold]);
        d.Execute(new SetPathCurveCommand(hold, V2PathCurve.Linear));
        var pasted = d.RequireTestNote(clipboard.Paste(d, At(4)).Single());
        Check(pasted.CustomExpr == HeadExpr && pasted.Nodes[0].CustomExpr == NodeExpr && pasted.Nodes[0].Id != mid,
            "paste with new ids uses the formulas from the clipboard snapshot");
        d.Undo(); d.Undo();
        d.Execute(new CopyChartCommand("custom", "copy", "charts/copy.json"));
        d.Execute(new SetPathCurveCommand(hold, mid, V2PathCurve.Hold));
        Check(d.Charts.Single(x => x.Id == "custom").Notes.Single(n => n.Id == hold).Nodes[0].CustomExpr == NodeExpr,
            "editing a duplicated difficulty cannot change its source node formula");
        d.SelectChart("custom");
        var prepared = EditorDifficultyWorkflowTests.Prepare(session,
            EditorDifficultyWorkflowTests.Request("scroll-copy") with { CopyScroll = true });
        session.AddDifficulty(prepared);
        Check(d.SelectedChart.ScrollSpeeds[1].CustomExpr == ScrollExpr &&
            d.SelectedChart.ScrollSpeeds[1].Key != d.Charts.Single(x => x.Id == "custom").ScrollSpeeds[1].Key,
            "new difficulty copies scroll formulas independently");
        AddTap(d, "new");
        session.SaveAsync(dir).GetAwaiter().GetResult();
        Check(d.SourceFormatMinor == 1 && !d.IsDirty, "successful async save records the written minor");

        var rebuilt = session.Open(dir);
        Check(rebuilt.SourceFormatMinor == 1 && rebuilt.BuildPackSnapshot().FormatMinor == 1 &&
            rebuilt.SelectedChart.FormatMinor == 0 && oldBytes.SequenceEqual(File.ReadAllBytes(Path.Combine(dir, rebuilt.SelectedChart.File))) &&
            EditorGameplayDigest.Compute(rebuilt, digestAudio).Sha256 == oldDigest,
            "2.1 mixed pack preserves the original 2.0 sibling bytes and digest");
        rebuilt.SelectChart("custom");
        var note = rebuilt.RequireTestNote(hold);
        Check(note.CustomExpr == HeadExpr && note.Nodes[0].CustomExpr == NodeExpr &&
            rebuilt.SelectedChart.ScrollSpeeds[1].CustomExpr == ScrollExpr && rebuilt.BuildChartSnapshot("custom").FormatMinor == 1,
            "strict save and reopen preserve both path expressions, Scroll and chart minor");
        var path = Path.Combine(dir, rebuilt.SelectedChart.File);
        var stable = File.ReadAllBytes(path);
        note.CustomExpr = "bad(t)"; // A malformed in-memory caller must not bypass the package writer.
        Reject(() => session.Save(dir));
        Check(File.ReadAllBytes(path).SequenceEqual(stable), "rejected save does not overwrite an existing package");
        var pack = rebuilt.BuildPackSnapshot();
        var explicitChart = rebuilt.BuildChartSnapshot("hard") with { FormatMinor = 1 };
        pack = pack with { Charts = [pack.Charts.Single(e => e.Id == "hard")] };
        var explicitDoc = EditorDocument.FromPackage(dir, pack, new Dictionary<string,V2Chart> { ["hard"] = explicitChart });
        Check(explicitDoc.BuildChartSnapshot("hard").FormatMinor == 1,
            "opening an explicitly 2.1 chart without custom does not rewrite its ruleset identity");
    }

    private static void Reject(Action action)
    {
        try { action(); }
        catch (Exception ex) when (ex is InvalidOperationException or FormatException or ArgumentException) { return; }
        throw new InvalidOperationException("Expected custom curve validation to reject the operation.");
    }
}
