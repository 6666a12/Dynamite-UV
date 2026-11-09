using Godot;
using DynamiteUniverse.Ui;

namespace DynamiteUniverse.Game;

/// <summary>Opt-in native object regression; no song, score persistence or visible window required.</summary>
public partial class GameplayHudVerification : Node
{
    public override void _Ready() => Callable.From(Run).CallDeferred();
    private void Run()
    {
        try
        {
            var root = new Node2D();
            AddChild(root);
            var clicks = 0;
            var allow = true;
            var frozen = false;
            var motion = UiMotionMode.Full;
            var hud = new GameplayHud(root, () => clicks++, 40, 8, () => motion, () => frozen, () => allow);
            var state = new GameplayHudSnapshot(123, 100, 1, 0, 0, 0, 1, 1, 0, 100, false, false, 100);
            hud.Update(state, 0);
            hud.RefreshTitleTag(GameplayRunContext.InternalFallback, true);
            var top = root.GetNode<Node2D>("TopHud");
            var bar = top.GetChildren().OfType<GameplayBleedBar>().Single();
            var plates = top.GetChildren().OfType<GameplayHudPlate>().ToArray();
            Require(plates.Length == 7 && !bar.Visible, "six plates + pause; disabled BLEED hidden");
            Require(plates.Take(6).All(p => p.MouseFilter == Control.MouseFilterEnum.Ignore), "statistics pass input");
            var pause = plates.Single(p => p.Activated is not null);
            Require(pause.GlobalPosition == new Vector2(900, 68), "pause design coordinates");
            Require(!pause._HasPoint(new Vector2(1, 1)) && pause._HasPoint(new Vector2(60, 32)), "symmetric clipped hit box");
            pause._GuiInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true });
            pause._GuiInput(new InputEventScreenTouch { Pressed = true });
            Require(clicks == 2, "mouse and touch use real pause callback");
            allow = false;
            pause._GuiInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true });
            Require(clicks == 2, "transition/startup guard rejects pause");
            hud.Update(state with { BleedEnabled = true, BleedHealth = 20 }, 0);
            bar._Process(0.05);
            var shader = (ShaderMaterial)bar.Material;
            Require(bar.Visible && Math.Abs(shader.GetShaderParameter("u_health").AsDouble() - 0.2) < 0.0001, "real health reaches shader");
            Require(bar.GlobalPosition + new Vector2(56, 56) == new Vector2(440, 76), "blood body design coordinates");
            var trail = shader.GetShaderParameter("u_trail_health");
            frozen = true;
            bar._Process(1);
            Require(shader.GetShaderParameter("u_trail_health").Equals(trail), "pause freezes trail");
            frozen = false;
            motion = UiMotionMode.Reduced;
            bar._Process(0.1);
            Require(shader.GetShaderParameter("u_impact_alpha").AsDouble() == 0, "Reduced removes impact");
            hud.Update(state with { BleedEnabled = true, Hardcore = true, BleedHealth = 4 }, 0);
            var color = shader.GetShaderParameter("u_color").AsVector3();
            Require(color.X > color.Z && shader.GetShaderParameter("u_hardcore").AsDouble() == 1, "Hardcore remains red at low health");
            hud.Update(state with { BleedEnabled = true, Hardcore = true, BleedHealth = 0 }, 0);
            Require(shader.GetShaderParameter("u_health").AsDouble() == 0 && shader.GetShaderParameter("u_trail_health").AsDouble() == 0, "failure snapshot empties immediately");
            motion = UiMotionMode.Off;
            hud.Update(state with { BleedEnabled = true, BleedHealth = 100 }, 0);
            bar._Process(0);
            Require(shader.GetShaderParameter("u_health").AsDouble() == 1 && shader.GetShaderParameter("u_glow").AsDouble() == 0, "Off static state");
            var bottom = root.GetNode<Node2D>("StageHud");
            Require(bottom.GetChildren().OfType<CutPanel>().Single().BorderWidth == 0, "title card border removed");
            Require(bottom.GetChildren().OfType<ColorRect>().Any(p => p.Size == new Vector2(6,104)), "difficulty stripe retained");
            var stage = new GameplayStageRenderer();
            root.AddChild(stage);
            stage.BuildCommunityStageChrome(40, gameplayHud: true);
            var chrome = stage.GetNode<Node2D>("CommunityStageChrome");
            Require(chrome.GetChildCount() == 4, "game only backdrop + three lines, no old decoration");
            Require(chrome.GetChildren().OfType<ColorRect>().Any(p => p.Position.X == 0 && p.Size.X == 1920), "bottom line reaches edges");
            stage.BuildCommunityStageChrome();
            Require(chrome.GetChildCount() == 8, "editor default chrome unchanged");
            root.Free();
            GD.Print("GAMEPLAY_HUD_NATIVE PASS");
            GetTree().Quit();
        }
        catch (Exception e)
        {
            GD.PushError(e.ToString());
            GetTree().Quit(1);
        }
    }
    private static void Require(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }
}
