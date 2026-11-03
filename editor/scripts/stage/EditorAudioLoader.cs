using Godot;
using DynamiteUniverse.Shared.Audio;

namespace DynamiteUniverse.Editor;

public static class EditorAudioLoader
{
    private const int MaxFlacBytes = 64 * 1024 * 1024;
    private static readonly FlacDecoderOptions FlacOptions = FlacDecoderOptions.Default with
    {
        MaxInputBytes = MaxFlacBytes,
        MaxPcmBytes = MaxFlacBytes,
    };

    public static AudioStream? Load(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        if (!Godot.FileAccess.FileExists(path)) throw new InvalidDataException($"Audio file not found: {path}");
        AudioStream? stream = path.GetExtension().ToLowerInvariant() switch
        {
            "wav" => AudioStreamWav.LoadFromFile(path),
            "mp3" => AudioStreamMP3.LoadFromFile(path),
            "ogg" => AudioStreamOggVorbis.LoadFromFile(path),
            "flac" => LoadFlac(path),
            _ => throw new InvalidDataException($"Unsupported audio format: {path}"),
        };
        return stream ?? throw new InvalidDataException($"Audio could not be decoded: {path}");
    }

    private static AudioStreamWav LoadFlac(string path)
    {
        byte[] bytes;
        using (var file = Godot.FileAccess.Open(path, Godot.FileAccess.ModeFlags.Read))
        {
            if (file is null) throw new InvalidDataException($"Audio could not be opened: {path}");
            var length = file.GetLength();
            if (length == 0 || length > MaxFlacBytes)
                throw new InvalidDataException($"FLAC size exceeds the editor decode budget: {path}");
            bytes = file.GetBuffer((long)length);
        }
        var decoded = FlacDecoder.Decode(bytes, FlacOptions);
        return new AudioStreamWav
        {
            Format = AudioStreamWav.FormatEnum.Format16Bits,
            MixRate = decoded.SampleRate,
            Stereo = decoded.Channels > 1,
            Data = decoded.Pcm16LittleEndian,
        };
    }
}
