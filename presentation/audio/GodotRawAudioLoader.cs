using Godot;
using DynamiteUniverse.Shared.Audio;

namespace DynamiteUniverse.Presentation.Audio;

/// <summary>Loads raw audio files into Godot streams. The caller owns resource remapping, caching and error handling.</summary>
public static class GodotRawAudioLoader
{
    public static AudioStream Load(string path)
    {
        if (!Godot.FileAccess.FileExists(path))
            throw new InvalidDataException($"Audio file not found: {path}");

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
        var options = FlacDecoderOptions.GamePlayback;
        byte[] bytes;
        using (var file = Godot.FileAccess.Open(path, Godot.FileAccess.ModeFlags.Read))
        {
            if (file is null)
                throw new InvalidDataException($"Audio could not be opened: {path} ({Godot.FileAccess.GetOpenError()})");

            var length = file.GetLength();
            if (length == 0 || length > (ulong)options.MaxInputBytes)
                throw new InvalidDataException($"FLAC size ({length} bytes) is outside the playback budget: {path}");

            bytes = file.GetBuffer((long)length);
            if ((ulong)bytes.LongLength != length)
                throw new InvalidDataException($"FLAC could not be read in full: {path}");
        }

        // Godot has no FLAC stream type: hand the decoder's interleaved 16-bit PCM to a WAV stream.
        var decoded = FlacDecoder.Decode(bytes, options);
        return new AudioStreamWav
        {
            Format = AudioStreamWav.FormatEnum.Format16Bits,
            MixRate = decoded.SampleRate,
            Stereo = decoded.Channels > 1,
            Data = decoded.Pcm16LittleEndian,
        };
    }
}
