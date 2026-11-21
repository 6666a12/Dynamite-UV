using Godot;
using DynamiteUniverse.Presentation.Audio;

namespace DynamiteUniverse.Editor;

public static class EditorAudioLoader
{
    public static AudioStream? Load(string? path) =>
        string.IsNullOrWhiteSpace(path) ? null : GodotRawAudioLoader.Load(path);
}
