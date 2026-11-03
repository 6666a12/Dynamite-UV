using DynamiteUniverse.Shared.Chart.V2;

namespace CoreTests;

internal static class FlacProbeCompatibilityTests
{
    internal static void Run(Action<bool, string> check)
    {
        RunCase(check, FlacTestEncoder.SubframeKind.Fixed4,
            FlacTestEncoder.ChannelMode.MidSide, 2, 16);
        RunCase(check, FlacTestEncoder.SubframeKind.Lpc,
            FlacTestEncoder.ChannelMode.LeftSide, 2, 24);
        RunCase(check, FlacTestEncoder.SubframeKind.Verbatim,
            FlacTestEncoder.ChannelMode.Independent, 1, 24);
    }

    private static void RunCase(Action<bool, string> check, FlacTestEncoder.SubframeKind kind,
        FlacTestEncoder.ChannelMode mode, int channels, int bits)
    {
        const int frames = 1_024;
        const int rate = 44_100;
        var samples = Enumerable.Range(0, channels)
            .Select(channel => Enumerable.Range(0, frames)
                .Select(index => ((index * 37 + channel * 101) % 4096) - 2048)
                .ToArray())
            .ToArray();
        var encoder = new FlacTestEncoder
        {
            Channels = samples,
            SampleRate = rate,
            BitsPerSample = bits,
            BlockSize = 256,
            Kind = kind,
            Mode = mode,
            ResidualPartitionOrder = 1,
        };
        var path = Path.Combine(Path.GetTempPath(),
            "dynamite-universe-flac-walker-" + Guid.NewGuid().ToString("N") + ".flac");
        try
        {
            File.WriteAllBytes(path, encoder.Encode());
            var info = V2BuiltInAudioProbe.ProbeFile(path, "audio.flac");
            check(Math.Abs(info.DurationSeconds - frames / (double)rate) < 0.000_001,
                $"full-frame probe accepts {bits}-bit {channels}ch {kind}/{mode}");
        }
        finally
        {
            File.Delete(path);
        }
    }
}
