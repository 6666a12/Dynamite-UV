namespace DynamiteUniverse.ChartEditor.Audio;

/// <summary>Audio formats that the editor can validate and play without external executables.</summary>
public enum EditorAudioFormat
{
    Wave,
    Mp3,
    Flac,
    OggVorbis,
    OggOpus,
    M4aAac,
    RawAac,
}

/// <summary>How a format is decoded by the editor.</summary>
public enum EditorAudioBackend
{
    Managed,
    WindowsMediaFoundation,
}

public sealed record EditorAudioFormatDescriptor(
    EditorAudioFormat Format,
    string DisplayName,
    string CanonicalExtension,
    IReadOnlyList<string> Extensions,
    EditorAudioBackend Backend);

/// <summary>Single authority for extension admission and decoder selection.</summary>
public static class EditorAudioFormatRegistry
{
    private static readonly EditorAudioFormatDescriptor Wave = new(
        EditorAudioFormat.Wave, "RIFF/WAVE", ".wav", [".wav"], EditorAudioBackend.Managed);
    private static readonly EditorAudioFormatDescriptor Mp3 = new(
        EditorAudioFormat.Mp3, "MP3", ".mp3", [".mp3"], EditorAudioBackend.Managed);
    private static readonly EditorAudioFormatDescriptor Flac = new(
        EditorAudioFormat.Flac, "FLAC", ".flac", [".flac"], EditorAudioBackend.Managed);
    private static readonly EditorAudioFormatDescriptor OggVorbis = new(
        EditorAudioFormat.OggVorbis, "Ogg Vorbis", ".ogg", [".ogg"], EditorAudioBackend.Managed);
    private static readonly EditorAudioFormatDescriptor OggOpus = new(
        EditorAudioFormat.OggOpus, "Ogg Opus", ".opus", [".opus"], EditorAudioBackend.Managed);
    private static readonly EditorAudioFormatDescriptor M4aAac = new(
        EditorAudioFormat.M4aAac, "M4A/AAC", ".m4a", [".m4a"], EditorAudioBackend.WindowsMediaFoundation);
    private static readonly EditorAudioFormatDescriptor RawAac = new(
        EditorAudioFormat.RawAac, "AAC", ".aac", [".aac"], EditorAudioBackend.WindowsMediaFoundation);

    public static IReadOnlyList<EditorAudioFormatDescriptor> Formats { get; } =
        [Wave, Mp3, Flac, OggVorbis, OggOpus, M4aAac, RawAac];

    public static IReadOnlyList<string> FilePickerPatterns { get; } =
        Formats.SelectMany(format => format.Extensions)
            .Select(extension => "*" + extension)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    public static bool TryGet(string path, out EditorAudioFormatDescriptor descriptor)
    {
        var extension = Path.GetExtension(path);
        descriptor = Formats.FirstOrDefault(format => format.Extensions.Contains(
            extension, StringComparer.OrdinalIgnoreCase))!;
        return descriptor is not null;
    }

    public static EditorAudioFormatDescriptor GetRequired(string path)
    {
        if (!TryGet(path, out var descriptor))
            throw new NotSupportedException(
                $"Unsupported chart audio extension '{Path.GetExtension(path)}'. " +
                "Use .wav, .mp3, .flac, .ogg, .opus, .m4a, or .aac.");
        if (descriptor.Backend == EditorAudioBackend.WindowsMediaFoundation &&
            !OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException(
                $"{descriptor.DisplayName} requires Windows Media Foundation; this platform fails closed.");
        return descriptor;
    }

    public static string PackageFileName(string sourcePath) =>
        "audio" + GetRequired(sourcePath).CanonicalExtension;
}
