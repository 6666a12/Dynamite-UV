using DynamiteUniverse.Game;
using Godot;

namespace DynamiteUniverse.Video;

/// <summary>The dedicated worker plays the resolved track once without modifying the game audio cache.</summary>
internal static class VideoCaptureAudio
{
    private static string? _path;
    private static AudioStreamWav? _unlooped;

    internal static AudioStream? Load(string path)
    {
        if (!VideoCaptureInvocation.Active) return Res.LoadAudio(path);
        if (_unlooped is not null && StringComparer.Ordinal.Equals(path, _path)) return _unlooped;
        var stream = Res.LoadAudio(path);
        if (stream is not AudioStreamWav { LoopMode: not AudioStreamWav.LoopModeEnum.Disabled } wav) return stream;

        // A strict WAV may carry a smpl loop. The gameplay natural-end flag must still fire.
        var copy = (AudioStreamWav)wav.Duplicate();
        copy.LoopMode = AudioStreamWav.LoopModeEnum.Disabled;
        _path = path;
        return _unlooped = copy;
    }
}
