using Godot;

namespace DynamiteUniverse.Editor;

public partial class StandaloneEditorMain : Control
{
    public override void _Ready()
    {
        var preview = new EditorPreviewController { Name = "EditorPreview" };
        AddChild(preview);
        preview.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
    }
}
