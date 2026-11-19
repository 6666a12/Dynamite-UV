using DynamiteUniverse.ChartEditor.Core;
using DynamiteUniverse.Shared.Chart.V2;
using static EditorTestSupport;

internal static class ChartOffsetNudgeTests
{
    internal static void Run()
    {
        SeparatePressesCancelExactly();
        DisplayAndParseRoundTrip();
        RepeatedStepsShareOneHistoryCommand();
        ReturningToStartRemovesEmptyCommand();
        OtherEditsAndChartSwitchEndTheGesture();
    }

    private static void SeparatePressesCancelExactly()
    {
        var d = CreateCleanDocument("offset.precision", "hard", "Precision", "Tests");
        void Press(double seconds) { var n = d.BeginChartOffsetNudge(); n.Step(seconds); n.End(); }
        foreach (var start in new[] { 0d, .000012345678901234567, -3.469446951953614E-18, double.Epsilon })
        {
            d.SelectedChart.AudioOffsetSec = start;
            foreach (int sign in new[] { 1, -1 })
            {
                for (var i = 0; i < 3; i++) Press(sign * .010);
                for (var i = 0; i < 3; i++) Press(-sign * .010);
                var actual = d.SelectedChart.AudioOffsetSec;
                Check(BitConverter.DoubleToInt64Bits(actual) == BitConverter.DoubleToInt64Bits(start),
                    $"independent O/P gestures must return to {start:R}; got {actual:R} s ({actual * 1000:R} ms)");
            }
        }
    }

    private static void DisplayAndParseRoundTrip()
    {
        foreach (var seconds in new[] { 0d, .001001, -.000012345678901234567, -3.469446951953614E-18, double.Epsilon, -double.MaxValue })
        {
            var text = ChartOffsetUnits.FormatMilliseconds(seconds, signed: true);
            Check(ChartOffsetUnits.TryParseMilliseconds(text, out var parsed) && parsed == seconds,
                $"read/write milliseconds round-trip {seconds:R} via {text}");
        }
        Check(ChartOffsetUnits.FormatMilliseconds(.001001, true) == "+1.001" &&
            ChartOffsetUnits.TryParseMilliseconds("10.01", out var parsedMs) && parsedMs == .01001,
            "milliseconds move the decimal point before binary conversion");
        Check(!ChartOffsetUnits.TryParseMilliseconds("NaN", out _) &&
            !ChartOffsetUnits.TryParseMilliseconds("9e999", out _) && !ChartOffsetUnits.TryParseMilliseconds("1e-999", out _),
            "reject non-finite, overflow and nonzero underflow inputs");
        var doc = CreateCleanDocument("offset.anchor", "hard", "Anchor", "Tests");
        doc.SelectedChart.AudioOffsetSec = double.Epsilon;
        var n = doc.BeginChartOffsetNudge(); n.Step(.010); n.End();
        doc.Execute(new EditChartOffsetCommand(.123456789)); doc.Undo();
        doc.Undo(); doc.Redo();
        n = doc.BeginChartOffsetNudge(); n.Step(-.010); n.End();
        Check(doc.SelectedChart.AudioOffsetSec == double.Epsilon, "undo/redo preserves authored origin across independent gestures");
    }

    private static void RepeatedStepsShareOneHistoryCommand()
    {
        var document = CreateCleanDocument("offset.nudge", "hard", "Offset", "Tests");
        AddTap(document, "offset-tap");
        document.MarkSaved(Path.Combine(Path.GetTempPath(), "offset-nudge-test"));
        var initialVersion = document.ChangeVersion;
        var originalBpms = document.SelectedChart.Bpms.Select(bpm => (bpm.Time, bpm.Bpm)).ToArray();
        var originalNoteTime = document.SelectedChart.Notes.Single().Time;
        var nudge = document.BeginChartOffsetNudge();

        Check(nudge.Step(.010), "first offset step applies immediately");
        Check(nudge.Step(.010), "held/repeated offset step applies immediately");
        Check(nudge.Step(-.001), "fine opposite step applies during the same gesture");
        Check(document.SelectedChart.AudioOffsetSec == .019 && document.ChangeVersion == initialVersion + 3 &&
              document.HasUnsavedChanges, "each preview updates the offset, revision and dirty state");
        var timeline = new V2BpmTimeline(document.SelectedChart.Bpms.Select(x => new V2BpmEvent(x.Time, x.Bpm)).ToArray(),
            document.SelectedChart.AudioOffsetSec);
        Check(timeline.RuntimeSecondsAt(ExactBarTime.Zero) == .019, "runtime audio timeline follows the live offset");
        Check(document.SelectedChart.Bpms.Select(bpm => (bpm.Time, bpm.Bpm)).SequenceEqual(originalBpms) &&
              document.SelectedChart.Notes.Single().Time == originalNoteTime, "offset adjustment preserves BPM events and note BarTime");

        nudge.End();
        Check(document.CanUndo, "committed nudge creates one undoable history entry");
        document.Undo();
        Check(document.SelectedChart.AudioOffsetSec == 0 && document.SelectedChart.Notes.Count == 1 &&
              !document.HasUnsavedChanges, "single undo restores offset without changing notes or saved baseline");
        document.Redo();
        Check(document.SelectedChart.AudioOffsetSec == .019 && document.HasUnsavedChanges,
            "single redo restores the final repeated-step offset");
    }

    private static void ReturningToStartRemovesEmptyCommand()
    {
        var document = CreateCleanDocument("offset.noop", "hard", "Offset", "Tests");
        document.MarkSaved(Path.Combine(Path.GetTempPath(), "offset-nudge-noop"));
        var nudge = document.BeginChartOffsetNudge();
        Check(nudge.Step(.010) && nudge.Step(-.010), "opposite nudges return to the starting value");
        nudge.End();
        Check(document.SelectedChart.AudioOffsetSec == 0 && !document.CanUndo && !document.HasUnsavedChanges,
            "a net-zero nudge removes its empty history record");

        var first = document.BeginChartOffsetNudge();
        first.Step(.010); first.End();
        var second = document.BeginChartOffsetNudge();
        second.Step(.010); second.End();
        document.Undo();
        Check(document.SelectedChart.AudioOffsetSec == .010, "separate key press has a separate undo boundary");
        document.Undo();
        Check(document.SelectedChart.AudioOffsetSec == 0, "first key press remains independently undoable");
    }

    private static void OtherEditsAndChartSwitchEndTheGesture()
    {
        var document = CreateCleanDocument("offset.boundary", "hard", "Offset", "Tests");
        document.MarkSaved(Path.Combine(Path.GetTempPath(), "offset-nudge-boundary"));
        var nudge = document.BeginChartOffsetNudge();
        nudge.Step(.010);
        document.Execute(new EditPackMetadataCommand("offset.boundary", "Changed", "Tests"));
        Check(!nudge.Step(.010), "a normal document command closes the nudge token");
        document.Undo();
        Check(document.SelectedChart.AudioOffsetSec == .010, "undo of the following edit does not absorb the earlier nudge");
        document.Undo();
        Check(document.SelectedChart.AudioOffsetSec == 0, "the prior nudge has its own undo step");

        AddTap(document, "copy-test");
        document.Execute(new CopyChartCommand("hard", "hard-copy", "charts/hard-copy.json"));
        var chartNudge = document.BeginChartOffsetNudge();
        chartNudge.Step(.010);
        document.SelectChart("hard");
        Check(!chartNudge.Step(.010) && document.SelectedChart.AudioOffsetSec == 0,
            "switching difficulty ends the previous difficulty's nudge");
        document.Undo();
        Check(document.SelectedChartId == "hard-copy" && document.SelectedChart.AudioOffsetSec == 0,
            "undo after switching selects and restores the copied chart");
        document.Redo();
        Check(document.SelectedChart.AudioOffsetSec == .010 && document.Charts.Single(x => x.Id == "hard").AudioOffsetSec == 0,
            "redo edits only the copied chart");

        var beforeSave = document.BeginChartOffsetNudge();
        beforeSave.Step(-.001);
        document.MarkSaved(document.PackageDirectory!);
        Check(!document.IsDirty && !beforeSave.Step(-.001), "save closes the current gesture");
        var afterSave = document.BeginChartOffsetNudge();
        afterSave.Step(-.010);
        Check(document.IsDirty && document.CanUndo, "live post-save step is dirty and can be undone while held");
        document.Undo();
        Check(!document.IsDirty && !afterSave.Step(-.010), "undo while held restores saved position and ends the token");
        var zero = document.BeginChartOffsetNudge();
        zero.Step(.001); zero.Step(-.001); zero.End();
        Check(document.CanRedo && !document.IsDirty, "net-zero gesture preserves redo branch and saved state");
    }

    private static void Check(bool value, string message)
    {
        if (!value) throw new InvalidOperationException("Offset nudge: " + message);
    }
}
