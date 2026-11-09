using Godot;
using DynamiteUniverse.ChartEditor.Core;
using DynamiteUniverse.Shared.Chart.V2;

namespace DynamiteUniverse.Editor;

internal static class EditorContextVerification
{
    public static void Run(Node host)
    {
        var menu = new EditorContextMenu();
        host.AddChild(menu);
        menu.SetText(new Dictionary<string, string> { ["Context.Title"] = "舞台工具" });
        menu.OpenAt(new Vector2(790, 590), new Vector2(800, 600));
        Check(menu.IsOpen && menu.MenuRect.End.X <= 800 && menu.MenuRect.End.Y <= 600, "menu clamps to viewport");
        var calls = new List<(string, string)>();
        menu.Requested += (kind, value) => calls.Add((kind, value));
        var buttons = Descendants(menu).OfType<Button>().ToArray();
        Check(buttons.Length == 15, "three sides, seven types, SELECT/PATH and three effect levels; no PAN");
        foreach (var button in buttons) button.EmitSignal(BaseButton.SignalName.Pressed);
        Check(calls.Count == 15 && menu.IsOpen, "buttons request actions without closing menu");
        menu.Sync("PATH", V2NoteType.Hold, EditorTrack.Right, "Reduced");
        Check(buttons.Count(button => button.ButtonPressed) == 3, "track, mode and effect selection reflect authoritative state");
        var slider = Descendants(menu).OfType<HSlider>().Single();
        slider.Value = 35;
        Check(calls[^1] == ("volume", "35"), "volume slider emits numeric preview volume");
        using var outside = new InputEventMouseButton { Pressed = true, ButtonIndex = MouseButton.Left };
        Check(menu.FilterInput(outside, Vector2.Zero) && !menu.IsOpen, "outside click consumed and closes");
        menu.OpenAt(Vector2.Zero, new Vector2(800, 600));
        using var escape = new InputEventKey { Pressed = true, Keycode = Key.Escape };
        Check(menu.FilterInput(escape, Vector2.Zero) && !menu.IsOpen, "Escape closes without Shell forwarding");
        menu.Free();

        var stage = new EditorStagePreview { Size = new Vector2(1920, 1080) };
        host.AddChild(stage);
        stage.SetChart(V2RuntimeAdapter.Adapt(new V2Chart
        {
            ChartId = "context", AudioOffsetSec = 0, Bpms = [new(ExactBarTime.Zero, 120)],
            NotesLeft = [], NotesCenter = [], NotesRight = [],
        }, requireMainJudgement: false));
        var opens = 0; stage.ContextRequested += _ => opens++;
        using var right = new InputEventMouseButton { Pressed = true, ButtonIndex = MouseButton.Right, Position = new Vector2(960, 500) };
        stage.HandlePointerInput(right);
        Check(opens == 1, "blank stage right-click opens");
        stage.SetAuthoring("PLACE", V2NoteType.Hold, EditorTrack.Center, 1, 16);
        using var press = new InputEventMouseButton { Pressed = true, ButtonIndex = MouseButton.Left, Position = right.Position };
        stage.HandlePointerInput(press);
        Check(stage.HasActiveDrag, "Hold draft started");
        stage.HandlePointerInput(right);
        Check(!stage.HasActiveDrag && opens == 1, "right-click cancels draft without opening");
        stage.HandlePointerInput(right);
        Check(opens == 2, "next idle right-click opens");
        stage.Free();
        GD.Print("PASS context menu controls, viewport bounds, dismiss and draft cancellation");
    }

    private static IEnumerable<Node> Descendants(Node root)
    {
        foreach (var child in root.GetChildren())
        {
            yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }
    private static void Check(bool value, string label)
    {
        if (!value) throw new InvalidOperationException(label);
    }
}
