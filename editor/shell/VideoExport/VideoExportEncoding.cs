using System.Globalization;

namespace DynamiteUniverse.Editor.Shell.VideoExport;

internal sealed record VideoExportEncoding(string Encoder, string Tag, string NativePreset, int Crf)
{
    private static readonly (string Id, string Encoder, string Tag, string[] Presets)[] Profiles =
    [
        ("h264", "libx264", "avc1", ["slow", "fast", "veryfast"]),
        ("hevc", "libx265", "hvc1", ["slow", "medium", "veryfast"]),
        ("av1", "libsvtav1", "av01", ["4", "7", "9"]),
    ];

    public static VideoExportEncoding Resolve(VideoExportOptions options)
    {
        var profile = Profiles.SingleOrDefault(item => item.Id == options.Codec);
        var codec = VideoExportChoices.Codecs.SingleOrDefault(item => item.Id == options.Codec);
        var speed = options.Preset switch { "slow" => 0, "medium" => 1, "high" => 2, _ => -1 };
        if (codec is null || speed < 0 || options.Crf < codec.MinCrf || options.Crf > codec.MaxCrf)
            throw new ArgumentException("Invalid video encoding options.", nameof(options));
        return new(profile.Encoder, profile.Tag, profile.Presets[speed], options.Crf);
    }

    public string[] Arguments(string pipe, string output) =>
    [
        "-nostdin", "-n", "-v", "error", "-xerror", "-f", "matroska", "-seekable", "0", "-i", pipe,
        "-map", "0:v:0", "-map", "0:a:0", "-c:v", Encoder, "-tag:v", Tag,
        "-preset", NativePreset, "-crf", Crf.ToString(CultureInfo.InvariantCulture),
        "-vf", "scale=out_color_matrix=bt709:out_range=tv", "-pix_fmt", "yuv420p",
        "-colorspace", "bt709", "-color_range", "tv", "-fps_mode", "passthrough", "-enc_time_base:v", "1:60",
        "-c:a", "aac", "-b:a", "192k", "-abort_on", "empty_output_stream", "-movflags", "+faststart", "-progress", "pipe:1", output,
    ];

    internal static bool AdvertisesOption(string line, string option)
    {
        var entry = line.AsSpan().TrimStart();
        return entry.StartsWith(option, StringComparison.Ordinal) &&
            (entry.Length == option.Length || char.IsWhiteSpace(entry[option.Length]) || entry[option.Length] is '[' or ':');
    }

    public static string[] Available(string output)
    {
        var encoders = new HashSet<string>(StringComparer.Ordinal);
        foreach (var line in output.Split('\n'))
        {
            var fields = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (fields.Length >= 2 && fields[0].Length == 6 && fields[0][0] is 'V' or 'A')
                encoders.Add(fields[1]);
        }
        return encoders.Contains("aac") ? Profiles.Where(item => encoders.Contains(item.Encoder)).Select(item => item.Id).ToArray() : [];
    }

}
