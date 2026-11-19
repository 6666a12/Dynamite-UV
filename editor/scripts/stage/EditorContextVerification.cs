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
        var calls = new List<(string Kind, string Value)>();
        menu.Requested += (kind, value) => calls.Add((kind, value));
        menu.SetText(new Dictionary<string, string> { ["Context.Center"] = "↓ 中轨", ["Context.Volume"] = "音乐音量" });
        menu.Sync("PATH", V2NoteType.Hold, EditorTrack.Right, "Reduced");
        menu.SetVolume(60);
        var available = new Vector2(1920, 1080);
        var pointer = new Vector2(600, 450);
        menu.OpenAt(pointer, available);
        void Motion(Vector2 p)
        {
            using var motion = new InputEventMouseMotion { Position = p, Relative = p - pointer };
            Check(menu.FilterInput(motion, p), "mouse motion consumed");
            pointer = p;
        }
        void Left(Vector2 p, bool pressed)
        {
            using var button = new InputEventMouseButton { Position = p, ButtonIndex = MouseButton.Left, Pressed = pressed };
            Check(menu.FilterInput(button, p), "left button consumed");
            pointer = p;
        }
        void KeyStroke(Key key, bool shift = false)
        {
            foreach (var state in new[] { true, false })
            {
                using var ev = new InputEventKey { Keycode = key, Pressed = state, ShiftPressed = shift };
                Check(menu.FilterInput(ev, pointer), "keyboard stroke consumed");
            }
        }
        Vector2 Center(string kind, string value)
        {
            var rect = menu.ItemRect(kind, value);
            Check(rect.HasArea() && menu.MenuRect.HasPoint(rect.GetCenter()), $"accessible surface: {kind}/{value}");
            return rect.GetCenter();
        }
        void Click(string kind, string value)
        {
            var p = Center(kind, value);
            Left(p, true); Left(p, false);
        }
        void Expect(params (string Kind, string Value)[] expected)
        {
            Check(calls.SequenceEqual(expected), $"expected [{string.Join(",", expected)}], got [{string.Join(",", calls)}]");
            calls.Clear();
        }

        VerifyPopupPlacement();
        Check(menu.IsOpen && menu.MenuRect.Position.IsEqualApprox(pointer) && menu.MenuRect.Size.IsEqualApprox(new(352, 496)),
            "normal menu opens down-right with the cursor at its top-left corner");
        Check(menu.GetViewport().GuiGetFocusOwner() is null, "opening does not preselect a GUI button");
        Check(menu.IsCurrent("track", "Right") && !menu.IsCurrent("track", "Left") && menu.IsCurrent("tool", "PATH") &&
            !menu.IsCurrent("type", "Hold") && menu.IsCurrent("effects", "Reduced"), "authoritative state independent of focus");
        Motion(pointer); Motion(pointer + Vector2.Right); KeyStroke(Key.Enter); KeyStroke(Key.Space);
        Expect();
        var left = Center("track", "Left");
        Check(menu.FirstButtonPoint.IsEqualApprox(left), "left segment center reported for native pointer smoke");
        Motion(left); Motion(left); Motion(left + Vector2.One);
        Motion(Center("track", "Center")); Motion(Center("track", "Right"));
        Expect(("track", "Left"), ("track", "Center"), ("track", "Right"));

        var vol = menu.ItemRect("volume", "");
        var xMin = new Vector2(vol.Position.X + .01f, vol.GetCenter().Y);
        var xMax = new Vector2(vol.End.X - .01f, vol.GetCenter().Y);
        Motion(xMin); Motion(vol.GetCenter()); Motion(vol.GetCenter() + new Vector2(.01f, 0)); Motion(xMax);
        Expect(("volume", "0"), ("volume", "50"), ("volume", "100"));
        Left(xMin, true); Left(xMin, false); Expect(("volume", "0"));

        var tap = Center("type", "Tap"); var hold = Center("type", "Hold");
        Motion(tap); Motion(hold); Motion(Center("tool", "SELECT")); Motion(Center("effects", "Full")); Expect();
        Left(tap, true); Expect(); Left(hold, false); Expect();
        foreach (var type in Enum.GetNames<V2NoteType>()) { Click("type", type); Expect(("type", type)); }
        foreach (var tool in new[] { "SELECT", "PATH" }) { Click("tool", tool); Expect(("tool", tool)); }
        foreach (var effect in new[] { "Off", "Reduced", "Full" }) { Click("effects", effect); Expect(("effects", effect)); }
        menu.Sync("PLACE", V2NoteType.Mixer, EditorTrack.Right, "Full");
        Check(menu.IsCurrent("type", "Mixer") && !menu.IsCurrent("tool", "PATH"), "PLACE highlights current note type");
        Click("track", "Left"); Expect(("track", "Left"));
        KeyStroke(Key.Tab); KeyStroke(Key.Tab); KeyStroke(Key.Tab, shift: true); KeyStroke(Key.Tab); KeyStroke(Key.Tab);
        KeyStroke(Key.Enter); Expect(("type", "Tap"));
        Check(menu.IsOpen, "hover, click and keyboard selection keep menu open");
        Left(Vector2.Zero, true); Check(!menu.IsOpen, "outside press consumed and dismisses"); Expect();

        // A tightly bounded viewport pins the panel in place: reopen with the pointer already
        // INSIDE a live segment. No-op motion here tests the arming threshold, not empty padding.
        var fit = menu.MenuRect.Size;
        menu.OpenAt(Vector2.Zero, fit);
        pointer = Center("track", "Center");
        menu.Close(); menu.OpenAt(pointer, fit);
        Check(menu.ItemRect("track", "Center").HasPoint(pointer), "opening point lies within the track segment");
        Motion(pointer); Motion(pointer + Vector2.Right); Expect();
        Motion(pointer + Vector2.Right * 4); Expect(("track", "Center"));
        menu.Close();
        pointer = Center("volume", "");
        menu.SetVolume(90); menu.OpenAt(pointer, fit);
        Motion(pointer); Motion(pointer + Vector2.Right); Expect();
        KeyStroke(Key.Tab, shift: true); KeyStroke(Key.Left); Expect(("volume", "89"));
        Motion(new Vector2(menu.ItemRect("volume", "").Position.X + menu.ItemRect("volume", "").Size.X * .75f, pointer.Y));
        Expect(("volume", "75"));
        menu.Close();

        var small = new Vector2(320, 240);
        foreach (var corner in new[] { Vector2.Zero, new Vector2(small.X - 1, 0), small - Vector2.One, new Vector2(0, small.Y - 1) })
        {
            menu.OpenAt(corner, small);
            var rect = menu.MenuRect;
            Check(rect.HasArea() && rect.Position.X >= -.01f && rect.Position.Y >= -.01f &&
                rect.End.X <= small.X + .01f && rect.End.Y <= small.Y + .01f, "all menu edges within constrained viewport");
            Motion(corner); Expect();
            using var escape = new InputEventKey { Pressed = true, Keycode = Key.Escape };
            Check(menu.FilterInput(escape, corner) && !menu.IsOpen, "Escape closes without Shell forwarding");
        }
        menu.Free();

        var stage = new EditorStagePreview { Size = available };
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
        GD.Print("PASS compact context menu: focus-free entry, hover/click/keyboard, four viewport edges, dismiss and draft cancellation");
    }

    internal static void VerifyPopupPlacement()
    {
        var stage = new Vector2(1920, 1080);
        foreach (var (point, tip) in new (Vector2, Vector2)[]
        {
            (new(500, 500), new(0, 0)), (new(1870, 200), new(352, 0)),
            (new(200, 1050), new(0, 496)), (new(1900, 1050), new(352, 496)),
            (new(1568, 584), new(0, 0)), // Exact fit: do not flip early.
        })
        {
            var bounds = EditorContextMenu.PopupRect(point, stage);
            Check((bounds.Position + tip).IsEqualApprox(point), "cursor meets the correct menu corner");
            var outline = EditorContextMenu.PlateAt(tip);
            Check(outline.Length == 7 && outline.Contains(tip), "visible plate reaches the cursor, free corners stay bevelled");
        }
        foreach (var viewport in new[] { new Vector2(510, 640), new Vector2(300, 220) })
        {
            for (var x = 0; x <= 10; ++x)
            for (var y = 0; y <= 10; ++y)
            {
                var point = viewport * new Vector2(x / 10f, y / 10f);
                var b = EditorContextMenu.PopupRect(point, viewport);
                Check(b.HasArea() && b.Position.X >= -.001f && b.Position.Y >= -.001f &&
                    b.End.X <= viewport.X + .001f && b.End.Y <= viewport.Y + .001f, "constrained popup fits entirely");
            }
        }
    }

    private static void Check(bool value, string label)
    {
        if (!value) throw new InvalidOperationException(label);
    }
}
