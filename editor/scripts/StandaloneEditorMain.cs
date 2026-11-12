using Godot;

namespace DynamiteUniverse.Editor;

public partial class StandaloneEditorMain : Control
{
    public override void _Ready()
    {
        if (OS.GetCmdlineUserArgs().Contains("--verify-hud"))
        {
            try { EditorHudVerification.Run(this); GetTree().Quit(); }
            catch (Exception error) { GD.PushError(error.ToString()); GetTree().Quit(1); }
            return;
        }
        if (OS.GetCmdlineUserArgs().Contains("--verify-context"))
        {
            try { EditorContextVerification.Run(this); GetTree().Quit(); }
            catch (Exception error) { GD.PushError(error.ToString()); GetTree().Quit(1); }
            return;
        }
        if (OS.GetCmdlineUserArgs().Contains("--verify-auto"))
        {
            try { EditorAutoVerification.Run(this); GetTree().Quit(); }
            catch (Exception error) { GD.PushError(error.ToString()); GetTree().Quit(1); }
            return;
        }
        if (OS.GetCmdlineUserArgs().Contains("--verify-gestures"))
        {
            try { EditorGestureVerification.Run(); GetTree().Quit(); }
            catch (Exception error) { GD.PushError(error.ToString()); GetTree().Quit(1); }
            return;
        }
        var preview = new EditorPreviewController { Name = "EditorPreview" };
        AddChild(preview);
        preview.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
    }
}
