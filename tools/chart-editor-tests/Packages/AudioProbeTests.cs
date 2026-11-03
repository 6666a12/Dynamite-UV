using static EditorTestSupport;
using DynamiteUniverse.Shared.Chart;
using DynamiteUniverse.Shared.Chart.V2;

/// <summary>
/// Regression coverage for the built-in v2 package-writer audio probe: real RIFF/WAVE, MP3, FLAC and
/// Ogg Vorbis structure validation, duration reporting and rejection of corrupt or unsupported input.
/// Fixtures are synthesized in code (see <see cref="AudioProbeFixtures"/>).
/// </summary>
internal static class AudioProbeTests
{
    internal static void Run()
    {
        TestValidDurations();
        TestUnsupportedFormats();
        TestCorruptInputs();
        TestPackageWriterIntegration();
    }

    private static void TestValidDurations()
    {
        // 2 s PCM WAVE at 44.1 kHz.
        ExpectDuration("wav", ".wav", AudioProbeFixtures.Wave(44100, 2.0), 2.0, 0.001);

        // 39 MPEG-1 Layer III frames at 44.1 kHz / 1152 samples per frame.
        var mp3 = AudioProbeFixtures.Mp3(39);
        ExpectDuration("mp3", ".mp3", mp3, 39 * 1152 / 44100.0, 0.001);
        ExpectDuration("mp3 mono", ".mp3", AudioProbeFixtures.Mp3(39, mono: true),
            39 * 1152 / 44100.0, 0.001);
        ExpectDuration("mp3 with id3v2", ".mp3", PrependId3v2(mp3), 39 * 1152 / 44100.0, 0.001);
        ExpectDuration("mp3 with id3v1 tail", ".mp3", [.. mp3, .. Id3v1()],
            39 * 1152 / 44100.0, 0.001);

        // 40 FLAC frames x 1152 samples at 44.1 kHz.
        ExpectDuration("flac", ".flac", AudioProbeFixtures.Flac(40), 40 * 1152 / 44100.0, 0.0005);
        ExpectDuration("flac mono 24-bit", ".flac",
            AudioProbeFixtures.Flac(40, channels: 1, bitsPerSample: 24),
            40 * 1152 / 44100.0, 0.0005);

        // Ogg Vorbis granule 110250 at 44.1 kHz = 2.5 s.
        ExpectDuration("ogg vorbis", ".ogg", AudioProbeFixtures.OggVorbis(44100, 110250), 2.5, 0.0);
        ExpectDuration("ogg vorbis 48k", ".ogg", AudioProbeFixtures.OggVorbis(48000, 96000), 2.0,
            0.0);
    }

    private static void TestUnsupportedFormats()
    {
        ExpectReject("opus extension", ".opus", AudioProbeFixtures.OggOpus(), "not supported");
        ExpectReject("opus inside .ogg", ".ogg", AudioProbeFixtures.OggOpus(), "Ogg Opus");
        ExpectReject("m4a extension", ".m4a", AudioProbeFixtures.Noise(), "not supported");
        ExpectReject("aac extension", ".aac", AudioProbeFixtures.Noise(), "not supported");
        ExpectReject("unknown extension", ".wma", AudioProbeFixtures.Noise(), "not supported");
        ExpectReject("wav holding mp3", ".wav", AudioProbeFixtures.Mp3(8), "RIFF/WAVE");
        ExpectReject("flac holding wav", ".flac", AudioProbeFixtures.Wave(), "FLAC signature");
    }

    private static void TestCorruptInputs()
    {
        ExpectReject("empty file", ".flac", [], "empty");

        // WAVE: truncated container, non-PCM/float sample format, missing data chunk.
        var wave = AudioProbeFixtures.Wave(44100, 2.0);
        ExpectReject("truncated wav", ".wav", AudioProbeFixtures.Truncate(wave), "truncated");
        ExpectReject("unsupported wav codec", ".wav", SetUInt16(wave, 20, 6), "unsupported WAVE");
        ExpectReject("wav without data chunk", ".wav", SetAscii(wave, 36, "junk"), "missing");

        // MP3: truncated frame chain, corrupted mid-stream frame, noise, implausible VBR count.
        var mp3 = AudioProbeFixtures.Mp3(39);
        ExpectReject("truncated mp3", ".mp3", AudioProbeFixtures.Truncate(mp3, 0.6),
            "truncated MPEG audio frame");
        // Breaking a frame's sync word forces the frame-chain walker to report corruption; damage
        // confined to frame payload bytes would legitimately still be a structurally valid stream.
        ExpectReject("corrupt mp3 frame sync", ".mp3",
            AudioProbeFixtures.FlipByte(mp3, AudioProbeFixtures.Mp3FrameBytes * 20),
            "MPEG audio frame chain");
        ExpectReject("noise as mp3", ".mp3", AudioProbeFixtures.Noise(), "no valid MPEG audio frame");
        ExpectReject("truncated id3v2 mp3", ".mp3", PrependId3v2(mp3)[..20], "ID3v2");

        // FLAC: metadata truncation, sample-count overrun, corrupted frame header CRC, noise.
        var flac = AudioProbeFixtures.Flac(40);
        ExpectReject("truncated flac", ".flac", AudioProbeFixtures.Truncate(flac, 0.6), "FLAC");
        ExpectReject("flac zero sample rate", ".flac", ClearStreamInfoRate(flac), "zero sample rate");
        ExpectReject("flac first frame sync", ".flac",
            AudioProbeFixtures.FlipByte(flac, AudioProbeFixtures.FlacFirstFrameOffset),
            "no valid audio frame header");
        // The frame header CRC-8 byte is the last field before the subframe payload.
        ExpectReject("flac frame header crc", ".flac",
            AudioProbeFixtures.FlipByte(flac, AudioProbeFixtures.FlacFirstFrameCrcOffset),
            "FLAC");
        ExpectReject("noise as flac", ".flac", AudioProbeFixtures.Noise(), "FLAC signature");

        // Ogg: missing EOS page, corrupted CRC, sequence gap, short identification header.
        var ogg = AudioProbeFixtures.OggVorbis(44100, 110250);
        ExpectReject("truncated ogg", ".ogg", AudioProbeFixtures.Truncate(ogg, 0.5), "Ogg");
        ExpectReject("corrupt ogg crc", ".ogg", AudioProbeFixtures.FlipByte(ogg, ogg.Length - 8),
            "CRC mismatch");
        ExpectReject("noise as ogg", ".ogg", AudioProbeFixtures.Noise(), "Ogg");
    }

    private static void TestPackageWriterIntegration()
    {
        var root = Path.Combine(Path.GetTempPath(),
            "dynamite-universe-editor-audio-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            foreach (var (extension, bytes, expected) in new (string, byte[], double)[]
                     {
                         (".wav", AudioProbeFixtures.Wave(44100, 2.0), 2.0),
                         (".mp3", AudioProbeFixtures.Mp3(39), 39 * 1152 / 44100.0),
                         (".flac", AudioProbeFixtures.Flac(40), 40 * 1152 / 44100.0),
                         (".ogg", AudioProbeFixtures.OggVorbis(44100, 110250), 2.5),
                     })
            {
                var package = CreatePackageWithAudio(root, extension, bytes, out var audioRelative);
                var info = V2BuiltInAudioProbe.ProbeFile(
                    Path.Combine(package, audioRelative), audioRelative);
                Check(Math.Abs(info.DurationSeconds - expected) <= 0.001,
                    $"writer accepts {extension} audio and reports {expected:R}s");
            }

            // A corrupt MP3 must fail staging with a diagnostic naming the package-relative path and
            // must not publish the destination directory.
            var brokenRoot = Path.Combine(root, "broken");
            Directory.CreateDirectory(brokenRoot);
            var destination = Path.Combine(brokenRoot, "target");
            ExpectAudioDiagnostic(() => CreatePackage(brokenRoot, ".mp3", destination,
                    AudioProbeFixtures.Truncate(AudioProbeFixtures.Mp3(39), 0.5)), "audio.mp3",
                "corrupt mp3 is rejected during staging");
            Check(!Directory.Exists(destination) && !FindStagingDirectories(brokenRoot).Any(),
                "rejected audio leaves no destination or staging directory");

            // An unsupported extension is still rejected with the built-in probe.
            var opusDestination = Path.Combine(brokenRoot, "target-opus");
            ExpectAudioDiagnostic(() => CreatePackage(brokenRoot, ".opus", opusDestination,
                    AudioProbeFixtures.OggOpus()), "audio.opus",
                "unsupported opus audio is rejected");
            Check(!Directory.Exists(opusDestination), "unsupported audio publishes nothing");
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    private static string CreatePackageWithAudio(string root, string extension, byte[] audio,
        out string audioRelative)
    {
        var work = Path.Combine(root, "case" + extension.Replace('.', '_'));
        Directory.CreateDirectory(work);
        var destination = Path.Combine(work, "package");
        CreatePackage(work, extension, destination, audio);
        audioRelative = "audio" + extension;
        return destination;
    }

    /// <summary>Creates a minimal single-chart package whose audio resource is a generated file.</summary>
    private static void CreatePackage(string workDirectory, string extension, string destination,
        byte[] audio)
    {
        var source = Path.Combine(workDirectory, "source" + extension);
        File.WriteAllBytes(source, audio);
        var entry = new V2ChartEntry
        {
            Id = "hard",
            Difficulty = V2Difficulty.Hard,
            Unrated = true,
            Charters = ["Audio Probe"],
            File = "charts/hard.json",
        };
        var pack = new V2Pack
        {
            Id = "audio.probe",
            Revision = 1,
            Title = "Audio Probe",
            Artist = "Audio Probe",
            Audio = "audio" + extension,
            Charts = [entry],
        };
        var chart = new V2Chart
        {
            ChartId = "hard",
            AudioOffsetSec = 0,
            Bpms = [new V2BpmEvent(ExactBarTime.Zero, 150)],
            NotesLeft = [ZeroTap("left")],
            NotesCenter = [],
            NotesRight = [],
        };
        V2PackageWriter.Create(new V2PackageCreateRequest
        {
            DestinationDirectory = destination,
            Pack = pack,
            Charts = new Dictionary<string, V2Chart>(StringComparer.Ordinal) { ["hard"] = chart },
            ExternalResources = [new V2ExternalResourceMapping(source, "audio" + extension)],
        });
    }

    private static V2BasicNote ZeroTap(string id) => new()
    {
        Id = id,
        Type = V2NoteType.Tap,
        Time = ExactBarTime.Zero,
        Center = 2.5,
        Width = 1.0,
    };

    private static void ExpectDuration(string name, string extension, byte[] audio,
        double expected, double tolerance)
    {
        using var file = TempAudioFile(extension, audio);
        var info = V2BuiltInAudioProbe.ProbeFile(file.Path, "audio" + extension);
        Check(double.IsFinite(info.DurationSeconds) && info.DurationSeconds >= 0 &&
              Math.Abs(info.DurationSeconds - expected) <= tolerance,
            $"{name} probe duration {info.DurationSeconds:R}s matches {expected:R}s");
    }

    private static void ExpectReject(string name, string extension, byte[] audio,
        string reasonFragment)
    {
        using var file = TempAudioFile(extension, audio);
        try
        {
            var info = V2BuiltInAudioProbe.ProbeFile(file.Path, "audio" + extension);
            throw new InvalidOperationException(
                $"{name} was accepted with duration {info.DurationSeconds:R}s");
        }
        catch (V2DiagnosticException exception)
        {
            Check(exception.SourceName == "audio" + extension &&
                  exception.JsonPointer == "/" &&
                  exception.Reason.Contains(reasonFragment, StringComparison.Ordinal),
                $"{name} rejected: {exception.Reason}");
        }
    }

    private static void ExpectAudioDiagnostic(Action action, string expectedSource, string name)
    {
        try
        {
            action();
        }
        catch (V2DiagnosticException exception)
        {
            Check(string.Equals(exception.SourceName, expectedSource, StringComparison.Ordinal) &&
                  exception.JsonPointer == "/",
                $"{name} ({exception.Reason})");
            return;
        }
        throw new InvalidOperationException(name + " did not throw a v2 audio diagnostic");
    }

    private static TempFile TempAudioFile(string extension, byte[] bytes)
    {
        var path = Path.Combine(Path.GetTempPath(),
            "dynamite-universe-audio-probe-" + Guid.NewGuid().ToString("N") + extension);
        File.WriteAllBytes(path, bytes);
        return new TempFile(path);
    }

    private sealed class TempFile(string path) : IDisposable
    {
        internal string Path { get; } = path;

        public void Dispose()
        {
            try
            {
                File.Delete(Path);
            }
            catch (IOException)
            {
                // A leftover temp file must not mask the assertion that just ran.
            }
        }
    }

    private static byte[] PrependId3v2(byte[] audio)
    {
        // 10-byte ID3v2 header followed by a 60-byte syncsafe-sized payload.
        var tag = new byte[70];
        tag[0] = (byte)'I';
        tag[1] = (byte)'D';
        tag[2] = (byte)'3';
        tag[3] = 3;
        tag[6] = 0x00;
        tag[7] = 0x00;
        tag[8] = 0x00;
        tag[9] = 60;
        return [.. tag, .. audio];
    }

    private static byte[] Id3v1()
    {
        var bytes = new byte[128];
        bytes[0] = (byte)'T';
        bytes[1] = (byte)'A';
        bytes[2] = (byte)'G';
        return bytes;
    }

    private static byte[] SetUInt16(byte[] bytes, int offset, ushort value)
    {
        var copy = (byte[])bytes.Clone();
        System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(copy.AsSpan(offset), value);
        return copy;
    }

    private static byte[] SetAscii(byte[] bytes, int offset, string text)
    {
        var copy = (byte[])bytes.Clone();
        System.Text.Encoding.ASCII.GetBytes(text).CopyTo(copy.AsSpan(offset));
        return copy;
    }

    private static byte[] ClearStreamInfoRate(byte[] bytes)
    {
        // STREAMINFO starts after "fLaC" + a 4-byte metadata header; the sample rate occupies the
        // top 20 bits of the packed 64-bit field at payload offset 10.
        var copy = (byte[])bytes.Clone();
        for (var index = 4 + 4 + 10; index < 4 + 4 + 18; index++)
            copy[index] = 0;
        return copy;
    }
}
