using Godot;
using DynamiteUniverse.Game;
using DynamiteUniverse.Shared.Chart;
using DynamiteUniverse.Shared.Chart.V2;

namespace DynamiteUniverse.Editor;

internal static class EditorHudVerification
{
    internal static void Run(Node host)
    {
        var stage = new EditorStagePreview();
        host.AddChild(stage);
        var root = stage.GetNode<Node2D>("EditorHud");
        var top = root.GetNode<Node2D>("TopHud");
        Require(top.GetChildren().OfType<GameplayHudPlate>().All(p => !p.Visible), "edit mode hides all statistic/pause plates");
        Require(!top.GetChildren().OfType<GameplayBleedBar>().Single().Visible, "edit mode hides bleed");
        Require(top.GetChildren().OfType<ColorRect>().Count(p => p.Visible) == 2, "edit mode keeps blue header lines");
        var transport = root.GetNode<EditorTransportButton>("EditorTransport");
        var toggles = 0;
        stage.TransportRequested += () => toggles++;
        Require(transport.Visible && transport.Size == new Vector2(184, 80) &&
                transport.Position + transport.Size / 2 == new Vector2(960, 72), "large editor transport sits in blue-line gap");
        transport._GuiInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true });
        Require(toggles == 0, "empty stage cannot start playback");
        var note = new V2PathNote
        {
            Id = "h", Type = V2NoteType.Hold, Time = ExactBarTime.Zero, Center = 1, Width = 1,
            CurveToNext = V2PathCurve.EaseInOutCubic,
            Nodes = [new V2PathNode { Id = "tail", Time = ExactBarTime.FromFraction(2, 1), Center = 3, Width = 1, Judge = true, JudgeWasExplicit = true }],
        };
        var chart = new V2Chart { ChartId = "hard", AudioOffsetSec = 0,
            Bpms = [new V2BpmEvent(ExactBarTime.Zero, 150)], NotesLeft = [], NotesCenter = [note], NotesRight = [] };
        var adapted = V2RuntimeAdapter.Adapt(chart);
        stage.SetHudChart(adapted, "Preview Test", "Hard", 10, 10);
        stage.SetChart(adapted);
        stage.CurrentBar = 1;
        stage._Process(0);
        var progress = root.GetNode<GameplayProgressBar>("SongProgress");
        Require(progress.Fraction > 0 && progress.Fraction < 1, "edit progress follows seek");
        var outlines = Descendants(stage).OfType<SustainOutline>().ToArray();
        Require(outlines.Length > 2 && outlines.Count(p => p.CapStart) == 1 && outlines.Count(p => p.CapEnd) == 1,
            "only authored curve endpoints have transverse outline caps");
        Require(transport.Available && !transport.Playing, "loaded paused stage shows play state");
        transport._GuiInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true });
        stage.SetPlaying(true);
        stage._Process(0);
        Require(transport.Playing, "playing stage shows pause state");
        transport._GuiInput(new InputEventScreenTouch { Pressed = true });
        Require(toggles == 2, "mouse and touch dispatch transport intent");
        stage.SetPlaying(false);
        stage.SetGamePreview(true);
        Require(!transport.Visible, "internal capture preview hides editor transport");
        stage._Process(0);
        Require(top.GetChildren().OfType<GameplayHudPlate>().All(p => p.Visible), "game preview enables shared HUD plates");
        Require(top.GetChildren().OfType<GameplayBleedBar>().Single().Visible, "game preview shows Auto full-health bleed");
        stage.CurrentBar = 0;
        stage._Process(0);
        Require(progress.Fraction == 0, "game preview supports backwards seek");
        stage.SetGamePreview(false);
        stage._Process(0);
        Require(top.GetChildren().OfType<GameplayHudPlate>().All(p => !p.Visible), "return to edit restores minimal HUD");
        Require(transport.Visible && !transport.Playing, "return to edit restores transport");
        stage.Free();
        GD.Print("EDITOR_HUD_OUTLINE PASS");
    }
    private static IEnumerable<Node> Descendants(Node root)
    {
        foreach (var child in root.GetChildren())
        {
            yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }
    private static void Require(bool value, string name)
    {
        if (!value) throw new InvalidOperationException(name);
        GD.Print("PASS " + name);
    }
}
