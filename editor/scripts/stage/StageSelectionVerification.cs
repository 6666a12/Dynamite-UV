using Godot;
using DynamiteUniverse.Shared.Chart;
using DynamiteUniverse.Shared.Chart.V2;
using DynamiteUniverse.ChartEditor.Core;

namespace DynamiteUniverse.Editor;

internal static class StageSelectionVerification
{
    /// <summary>Uses the actual adapter IDs, authoring press routing and selection payload builder.</summary>
    internal static void Run(V2Chart source)
    {
        var tap = new V2BasicNote { Id = "a", Type = V2NoteType.Tap, Time = ExactBarTime.FromFraction(1,8), Center = 1, Width = 1 };
        var hold = new V2PathNote { Id = "b", Type = V2NoteType.Hold, Time = ExactBarTime.FromFraction(1,8), Center = 3, Width = 1,
            Nodes = [new V2PathNode { Id = "b-tail", Time = ExactBarTime.FromFraction(1,4), Center = 3, Width = 1, Judge = true, JudgeWasExplicit = true }] };
        var chart = V2RuntimeAdapter.Adapt(source with { NotesCenter = [tap, hold] });
        var selected = new HashSet<int>();
        var author = new EditorStageAuthoring();
        author.Configure("SELECT", V2NoteType.Tap, EditorTrack.Center, 1, 16);
        var notes = chart.RuntimeChart.AllNotes.ToArray();
        StageSelection? result = null;
        author.NotePressed += (id, toggle) => result = StageSelection.Pick(chart, selected, id, toggle, false);
        author.PathNotePicked += id => result = StageSelection.Pick(chart, selected, id, false, true);
        StageSelection Click(string id, bool shift)
        {
            var n = notes.Single(n => n.Id == chart.RuntimeIdsBySourceId[id]);
            var pos = GameplayStageGeometry.PositionAt(n.Track, n.Position+n.Width/2,
                (float)(n.Second * GameplayStageGeometry.BaseFallSpeedPx));
            var point = new Vector2(pos.X, pos.Y);
            using var down = new InputEventMouseButton { ButtonIndex=MouseButton.Left, Pressed=true, ShiftPressed=shift };
            using var up = new InputEventMouseButton { ButtonIndex=MouseButton.Left, Pressed=false, ShiftPressed=shift };
            result = null;
            author.HandleInput(down, point, notes, chart.Metadata, 0, 1, selected);
            author.HandleInput(up, point, notes, chart.Metadata, 0, 1, selected);
            return result ?? throw new InvalidOperationException("selection click did not emit " + id);
        }
        void Expect(bool ok, string message) { if (!ok) throw new InvalidOperationException(message); }
        var a = Click("a",false);
        var aAndB = Click("b-tail",true);
        Expect(a.Primary=="a" && aAndB.Primary=="b" && aAndB.Parents.ToHashSet().SetEquals(["a","b"]) && aAndB.Node is null,"authored node joins the group as its parent");
        var removed = Click("b-tail",true);
        Expect(removed.Primary=="a" && removed.Parent=="a" && removed.Sources.SequenceEqual(["a"]),"toggled primary must belong to final selection");
        var empty = Click("a",true);
        Expect(empty.Parents.Length==0 && empty.Primary=="" && empty.Parent=="" && selected.Count==0,"Shift deselects the last note");
        var node = Click("b-tail",false);
        Expect(node.Primary=="b-tail" && node.Node=="b-tail" && node.Parent=="b" && node.Sources.SequenceEqual(["b-tail"]),"single node preserves editable handle and parent");
        Expect(Click("b-tail",true).Parents.Length==0,"Shift on the selected handle removes the last parent");
        GD.Print("PASS selection: SHIFT primary removal, empty and real path-node identity");
    }
}
