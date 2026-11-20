using System.Text;
using DynamiteUniverse.ChartEditor.Core;
using DynamiteUniverse.Shared.Chart.V2;
using static EditorTestSupport;

internal static class CustomCurveCacheTests
{
    private static ExactBarTime At(int i) => ExactBarTime.FromFraction(i, 1);
    private static int _noise;
    private static int _document;

    internal static void Run()
    {
        foreach (var count in new[] { 64, 65, 128 }) VerifyWorkingSet(count);
        ColdPathCommand();
        Console.WriteLine("Custom curve cache: 64/65/128, command/codec/digest, preview switches and rollback passed.");
    }

    private static EditorDocument Document(int n)
    {
        var d = CreateCleanDocument("test.curve-cache", "same-id", "Cache", "Community");
        // Same public pack/chart IDs across documents, deliberately different sources.
        int tag = ++_document;
        for (int i = 0; i < n; i++)
            d.SelectedChart.ScrollSpeeds.Add(new() { Time = At(i), Value = 1, CurveToNext = V2ScrollCurve.Custom,
                CustomExpr = $"t + 0 * {tag} + 0 * {i}" });
        d.SelectedChart.ScrollSpeeds.Add(new() { Time = At(n), Value = 2 });
        AddTap(d, "tap"); // A normal editor command validates the snapshot and seeds the owner's cache.
        return d;
    }

    // Invalid drafts exercise the actual editor compiler. Empty the entire global fallback cache
    // between windows, so successes cannot come from process-global hits (Shell/Stage run apart).
    private static void FlushDrafts()
    {
        for (int i = 0; i < 65; i++)
            try { CustomCurve.Compile($"invalid{++_noise}(t)"); throw new Exception("bad draft accepted"); }
            catch (FormatException) { }
    }

    private static string Encode(EditorDocument d) => Encoding.UTF8.GetString(V2JsonEncoder.EncodeChart(d.BuildChartSnapshot(d.SelectedChartId)));
    private static string Key(EditorDocument d) => d.SelectedChart.Key.ToString();
    private static void Warm(Action action, string label)
    {
        FlushDrafts(); var calls = CustomCurve.CompilationCount;
        action();
        Check(CustomCurve.CompilationCount == calls, label);
    }

    private static void VerifyWorkingSet(int n)
    {
        var a = Document(n); var b = Document(n);
        var stage = new EditorPreviewChartLoader(); var jA = Encode(a); var jB = Encode(b);
        stage.Load(jA, "a", Key(a));
        FlushDrafts(); var cold = CustomCurve.CompilationCount;
        stage.Load(jB, "b", Key(b));
        Check(CustomCurve.CompilationCount == cold + n, "cold stage compiles each raw JSON source once across decode and adapt");
        Check(a.SelectedChart.CurveCache.Count == n && b.SelectedChart.CurveCache.Count == n, "exact per-document working set");
        Warm(() =>
        {
            foreach (var d in new[] { a, b, a })
            {
                d.Execute(new EditChartOffsetCommand(.1));
                var first = d.SelectedChart.ScrollSpeeds[0];
                d.Execute(new EditScrollCommand(At(0), At(0), 1.125, first.CurveToNext, first.CustomExpr));
                d.Undo(); d.Redo();
                var snapshot = d.BuildChartSnapshot(d.SelectedChartId);
                var encoded = V2JsonEncoder.EncodeChart(snapshot);
                var decoded = stage.Load(Encoding.UTF8.GetString(encoded), "preview", Key(d));
                Check(encoded.SequenceEqual(V2JsonEncoder.EncodeChart(decoded.Metadata.SourceChart)), "same strict bytes after preview load");
                Check(decoded.Metadata.ScrollMap.RuntimeSpeedAt(0) == 1.125, "not a cached snapshot verdict");
                _ = EditorGameplayDigest.Compute(d, new string('a',64));
            }
        }, $"{n} warm formulas never recompile across command/preflight/undo/redo/encode/digest and stage A/B/A");

        var changed = $"t + 0*42 + 0*{n}";
        int editedIndex = n / 2;
        FlushDrafts(); var before = CustomCurve.CompilationCount;
        a.Execute(new EditScrollCommand(At(editedIndex), At(editedIndex), 1, V2ScrollCurve.Custom, changed));
        Check(CustomCurve.CompilationCount == before + 1 && a.SelectedChart.CurveCache.Count == n, "exactly the changed source compiled");
        var original = a.SelectedChart.ScrollSpeeds[editedIndex].CustomExpr;
        var version = a.ChangeVersion; var selected = a.Selection;
        Reject(() => a.Execute(new EditScrollCommand(At(editedIndex), At(editedIndex), 1, V2ScrollCurve.Custom, "bad(t)")));
        Check(a.ChangeVersion == version && a.Selection == selected && a.SelectedChart.ScrollSpeeds[editedIndex].CustomExpr == original,
            "invalid candidate leaves source, history and selection unchanged");
        if (n == 128)
        {
            Reject(() => a.Execute(new AddScrollCommand(ExactBarTime.FromFraction(1,2), 1, V2ScrollCurve.Custom, "t+0*12900")));
            Check(a.ChangeVersion == version && a.SelectedChart.CurveCache.Count == n, "cached sources never bypass the 128 limit");
            // Each use is charged, including repeated uses of one cached valid table.
            var snap = a.BuildChartSnapshot(a.SelectedChartId);
            var tooMany = Enumerable.Range(0,4097).Select(i => new V2ScrollEvent { Time=At(i), Value=1,
                CurveToNext=V2ScrollCurve.Custom, CustomExpr=changed }).Append(new() { Time=At(4097), Value=1 }).ToArray();
            var rejected = a.SelectedChart.CurveCache.Attach(snap with { ScrollSpeeds = tooMany });
            Reject(() => V2JsonEncoder.EncodeChart(rejected));
            Check(a.SelectedChart.CurveCache.Count == n, "failed whole-chart budget pass cannot publish a partial table");
        }
        // A failed third chart must not evict A/B from the bounded preview owner cache.
        Reject(() => stage.Load("{}", "bad", "unrelated"));
        Warm(() => { stage.Load(jB,"b",Key(b)); stage.Load(jA,"a",Key(a)); Encode(a); }, "rejected loads/drafts preserve accepted working sets");
    }

    private static void ColdPathCommand()
    {
        var document = CreateCleanDocument("test.cache-cold", "cold", "Cold", "Community");
        EditableNote Path(int n)
        {
            string Formula(int i) => $"t + 0 * {n} + 0 * {i}";
            var note = new EditableNote { Id = $"path-{n}", Time = At(0), Center = 1, Width = 1,
                Track = EditorTrack.Center, Type = V2NoteType.Mixer, CurveToNext = V2PathCurve.Custom, CustomExpr = Formula(0) };
            for (int i = 1; i <= n; i++) note.Nodes.Add(new() { Id = $"p-{n}-{i}", Time = At(i), Center = 1, Width = 1,
                CurveToNext = i == n ? null : V2PathCurve.Custom, CustomExpr = i == n ? null : Formula(i) });
            return note;
        }
        FlushDrafts(); var before = CustomCurve.CompilationCount;
        document.Execute(new AddNoteCommand(Path(128)));
        Check(CustomCurve.CompilationCount == before + 128, "128 cold path sources compile once across command candidate and full validation");
        var version = document.ChangeVersion; var selected = document.Selection;
        FlushDrafts(); before = CustomCurve.CompilationCount;
        Reject(() => document.Execute(new AddNoteCommand(Path(129))));
        Check(CustomCurve.CompilationCount == before + 128 && document.ChangeVersion == version && selected == document.Selection &&
            document.SelectedChart.Notes.Count == 1, "the 129th cold path source is rejected before compiling or publishing it");
        Warm(() => Encode(document), "rejected cold path has not displaced any accepted source");
        document.Undo(); document.Redo();
        var board = new EditorNoteClipboard(); board.Copy(document, ["path-128"]);
        var target = CreateCleanDocument("test.cache-paste", "cold", "Paste", "Community");
        FlushDrafts(); before = CustomCurve.CompilationCount;
        board.Paste(target, At(0));
        Check(CustomCurve.CompilationCount == before + 128, "cold cross-document paste validates without compiling its 128 formulas twice");
    }

    private static void Reject(Action action)
    {
        try { action(); }
        catch (Exception e) when (e is FormatException or InvalidOperationException or ArgumentException) { return; }
        throw new Exception("invalid change accepted");
    }
}
