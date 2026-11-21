using System.Globalization;
using System.Text;
using DynamiteUniverse.ChartEditor.Core;
using DynamiteUniverse.Shared.Chart.V2;
using static EditorTestSupport;

internal static class TimingFileImportTests
{
    internal static void Run()
    {
        Malody();
        OsuLegacyVersions();
        OsuDigitsAndFlags();
        OsuSnapBounds();
        OsuReprojection();
        InvalidInput();
    }

    private static void Malody()
    {
        const string timing = """{"time":[{"beat":[5,1,2],"bpm":181.125},{"beat":[0,0,4],"bpm":123.5}]}""";
        const string valid = """{"beat":[0,0,4],"type":1,"sound":"节拍.wav","offset":-1250.5}""";
        var result = Read(WithNotes(timing, valid), ".MC");
        Check(result.Points.Select(point => point.Time).SequenceEqual([At(0), At(11, 8)]) &&
            result.Points[1].Bpm == 181.125 && result.Points.All(point => point.SourceMilliseconds is null),
            "Malody sorts BPMs and divides beat fractions by four exactly");
        Check(result.AudioOffsetSec == 1.2505 && !result.NeedsManualOffset && result.AudioFileName == "节拍.wav",
            "Malody anchors the unique beat-zero BGM and reverses its millisecond offset sign");
        var negative = Read(WithNotes(timing, valid.Replace("-1250.5", "25")), "mc");
        Check(negative.AudioOffsetSec == -0.025, "Malody positive BGM offset produces negative UV offset");

        string[] manual = [timing, WithNotes(timing, valid + "," + valid),
            WithNotes(timing, valid.Replace("[0,0,4]", "[1,0,4]")),
            WithNotes(timing, valid.Replace(",\"offset\":-1250.5", "")),
            WithNotes(timing, valid.Replace("-1250.5", "1e-322"))];
        Check(manual.Select(text => Read(text, ".mc")).All(t => t.NeedsManualOffset && t.AudioOffsetSec is null && t.Points.Count == 2),
            "Missing, ambiguous, nonzero-beat or unrepresentable Malody BGM timing requires manual offset without dropping BPMs");
    }

    private static void OsuLegacyVersions()
    {
        const string early = "osu file format v3\n[TimingPoints]\n118,500.004999999999\n";
        var v3 = Read(early, ".osu");
        Check(v3.Points.Count == 1 && Math.Abs(v3.AudioOffsetSec!.Value - 0.142) < 1e-14 &&
            Math.Abs(v3.Points[0].Bpm - 119.99880001199988) < 1e-12,
            "osu! v3 accepts two-column timing rows and shifts its initial offset by 24 ms");
        CheckRuntime(v3, [142], "osu! v3 legacy correction is included in audio alignment");

        var v4 = Read(early.Replace("v3", "v4"), ".osu");
        var v5 = Read(early.Replace("v3", "v5"), ".osu");
        Check(v4.AudioOffsetSec == 0.142 && v5.AudioOffsetSec == 0.118,
            "osu! applies the 24 ms correction through v4 and not from v5 onwards");

        const string columns = "osu file format v13\n[TimingPoints]\n0,500\n2000,100,0\n" +
            "4000,250,0,1,0,100,0,0\n6000,400,0,1,0,100,1,0\n";
        var v13 = Read(columns, ".osu");
        Check(v13.Points.Count == 3 && v13.Points[0].Time == ExactBarTime.Zero &&
            Math.Abs(v13.Points[1].Time.ToDouble() - 1) < 1e-14 &&
            Math.Abs(v13.Points[2].Time.ToDouble() - 11) < 1e-14 && v13.Points[2].Bpm == 150,
            "osu! v13 defaults a missing flag to red, zero meter to 4/4, and ignores explicit green rows");
        CheckRuntime(v13, [0, 2000, 6000], "osu! v13 partial rows reproject to their audio timestamps");

        var shifted = Read("osu file format v4\n[TimingPoints]\n-149,500\n351,400\n", ".osu");
        Check(shifted.AudioOffsetSec == -0.125 && shifted.Points[1].Time == At(1, 4) &&
            shifted.SnappedCount == 0 && shifted.Points[1].SourceMilliseconds == 375,
            "osu! v4 shifts every red line without changing its interval or counting the shift as snapping");
        CheckRuntime(shifted, [-125, 375], "osu! v4 corrected points survive runtime reprojection");
        ExpectException<InvalidDataException>(() => Read(columns.Replace(",100,0,0", ",100,2,0"), ".osu"),
            "osu! rejects a malformed legacy flag rather than silently dropping the point");
        ExpectException<InvalidDataException>(() => Read(early.Replace("v3", "v15"), ".osu"),
            "osu! rejects unknown versions between legacy and v128");
    }

    private static void OsuDigitsAndFlags()
    {
        var before = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
            const string text = "\uFEFFosu file format v128\r\n[General]\r\nAudioFilename: song.ogg\r\n" +
                "[TimingPoints]\r\n// only red\r\n1874.95,4e2,3,0,0,100,1,0\r\n" +
                "-125.25,500.05,4,0,0,100,1,0\r\n-125.25,500,4,0,0,100,0,0\r\n0,NaN,4,0,0,100,0,0\r\n" +
                "[HitObjects]\r\n123,0,10,1,0,0\r\n";
            using var input = new MemoryStream(Encoding.UTF8.GetBytes(text));
            var result = TimingFileImport.Read(input, ".osu", snapOsu: false);
            Check(input.CanRead && result.AudioOffsetSec == -0.12525 && result.AudioFileName == "song.ogg" &&
                result.Points.Count == 2 && result.Points[1].Bpm == 150 && result.HasNonFourMeter && !result.NeedsManualOffset,
                "osu! accepts v128 BOM/CRLF, decimal timestamps, explicit red flag, meter and filename independently of locale");
            Check(Math.Abs(result.Points[1].Time.ToDouble() - 1) < 1e-14,
                "osu! retains fractional beat lengths while projecting later reds");
            CheckRuntime(result, [-125.25, 1874.95], "osu! fractional red positions survive UV runtime mapping");
        }
        finally { CultureInfo.CurrentCulture = before; }
    }

    private static void OsuSnapBounds()
    {
        var quart = Read(Osu("0,500", "499,400"), ".osu");
        Check(quart.Points[1].Time == At(1, 4) && quart.SnappedCount == 1 &&
            quart.MaximumSnapMilliseconds == 1 && quart.Points[1].SnapMilliseconds == 1,
            "osu! snaps up to and including one millisecond at a quarter bar");
        var down = Read(Osu("0,500", "500.5,400"), ".osu");
        Check(down.Points[1].Time == At(1, 4) && down.Points[1].SnapMilliseconds == -0.5 && down.MaximumSnapMilliseconds == 0.5,
            "osu! reports signed displacement and absolute maximum");
        var limit = Read(Osu("0,200", "199.2,400"), ".osu");
        Check(limit.SnappedCount == 0 && limit.Points[1].Time == At(249, 1000),
            "osu! does not snap at exactly 0.001 bar even under one millisecond");
        var msLimit = Read(Osu("0,500", "498.99,400"), ".osu");
        Check(msLimit.SnappedCount == 0 && msLimit.Points[1].Time == At(49899, 200000),
            "osu! does not snap above one millisecond even inside the bar threshold");
        var collision = Read(Osu("0,500", "0.5,400"), ".osu");
        Check(collision.SnappedCount == 0 && collision.Points[1].Time == At(1, 4000),
            "osu! does not snap onto the previous output point");
        var future = Read(Osu("0,500", "499.8,500", "499.9,500", "500,500"), ".osu");
        Check(future.SnappedCount == 0 && future.Points.Count == 4,
            "osu! does not snap so far ahead that the next red point would be lost");
        var raw = Read(Osu("0,500", "499,400"), ".osu", false);
        Check(raw.SnappedCount == 0 && raw.Points[1].Time == At(499, 2000), "osu! raw import disables grid snap");
    }

    private static void OsuReprojection()
    {
        var result = Read(Osu("1000,500", "1499.6,500", "1999.2,500", "2498.8,500", "3000.1,400"), ".osu");
        Check(result.AudioOffsetSec == 1 && result.SnappedCount == 3 && result.MaximumSnapMilliseconds == 0.8 &&
            result.Points[1].Time == At(1, 4) && result.Points[2].Time == At(1, 2) &&
            result.Points[3].Time == At(3747, 5000) && result.Points[4].Time == ExactBarTime.One,
            "osu! reprojects each absolute timestamp, ruling out a later snap that would accumulate over 1 ms");
        CheckRuntime(result, [1000, 1500, 2000, 2498.8, 3000], "osu! build meets per-red absolute error after multiple snaps");
        var change = Read(Osu("0,500", "499.6,400", "898.8,500"), ".osu");
        Check(change.SnappedCount == 1 && change.Points[2].Time == At(1997, 4000),
            "osu! reprojects across a changed tempo from the snapped output origin");
        CheckRuntime(change, [0, 500, 898.8], "osu! raw red after a snapped tempo change keeps its absolute timestamp");

        var varied = Read(Osu("1.23456,333.3333333333333", "1334.56789,421.0526315789474",
            "2678.91234,555.5555555555556", "4321.12345,487.8048780487805", "5011.39992,500"), ".osu", false);
        Check(varied.Points.All(p => p.Time.IsJsonSafeCanonical), "osu! varied beat lengths remain serializable as UV BarTime");
        CheckRuntime(varied, [1.23456, 1334.56789, 2678.91234, 4321.12345, 5011.39992],
            "osu! raw output keeps fractional-ms red lines within a 10 ns error budget");
    }

    private static void InvalidInput()
    {
        ExpectException<InvalidDataException>(() => Read(Osu("0,500", "0.000,400"), ".osu"), "osu! rejects duplicate red timestamps after exact numeric normalization");
        ExpectException<InvalidDataException>(() => Read(Osu("0,-50"), ".osu"), "osu! rejects negative beat length on red lines");
        ExpectException<InvalidDataException>(() => Read(Osu("0,NaN"), ".osu"), "osu! rejects non-finite red beat length");
        ExpectException<InvalidDataException>(() => Read(Osu("0,500", "0.00000000000000000001,400"), ".osu"),
            "osu! rejects distinct red timestamps that collapse at safe output precision");
        ExpectException<InvalidDataException>(() => Read(Osu("0,500").Replace("v14", "v2"), ".osu"), "osu! rejects unsupported v2 headers");
        ExpectException<InvalidDataException>(() => Read("osu file format v14\n[TimingPoints]\n0,500,4", ".osu"), "osu! requires explicit inheritance flag");
        ExpectException<InvalidDataException>(() => Read("osu file format v14\n[TimingPoints]\n0,-50,4,0,0,100,0,0", ".osu"), "osu! requires a red timing point");
        ExpectException<InvalidDataException>(() => Read("""{"time":[{"beat":[1,0,4],"bpm":120}]}""", ".mc"), "Malody requires a BPM point at beat zero");
        ExpectException<InvalidDataException>(() => Read("""{"time":[{"beat":[0,0,0],"bpm":120}]}""", ".mc"), "Malody rejects zero beat denominator");
        ExpectException<InvalidDataException>(() => Read("""{"time":[{"beat":[0,0,4],"bpm":120},{"beat":[1,-4,4],"bpm":150}]}""", ".mc"), "Malody rejects duplicate BPM positions after fraction normalization");
        using var large = new MemoryStream(new byte[TimingFileImport.MaxFileBytes + 1]);
        ExpectException<InvalidDataException>(() => TimingFileImport.Read(large, ".osu"), "timing import enforces the byte limit before parsing");
    }

    private static void CheckRuntime(ImportedTiming timing, double[] expectedMilliseconds, string name)
    {
        var timeline = new V2BpmTimeline(timing.Points.Select(p => new V2BpmEvent(p.Time, p.Bpm)).ToArray(), timing.AudioOffsetSec!.Value);
        Check(timing.Points.Select((p, i) => Math.Abs(timeline.RuntimeSecondsAt(p.Time) * 1000 - expectedMilliseconds[i]))
            .All(error => error <= 0.00001), name);
    }

    private static string WithNotes(string malody, string notes) => malody[..^1] + ",\"note\":[" + notes + "]}";
    private static string Osu(params string[] timeAndBeat) => "osu file format v14\n[TimingPoints]\n" +
        string.Join('\n', timeAndBeat.Select(line => line + ",4,0,0,100,1,0"));
    private static ExactBarTime At(int numerator, int denominator = 1) => ExactBarTime.FromFraction(numerator, denominator);

    private static ImportedTiming Read(string text, string extension, bool snap = true)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(text));
        return TimingFileImport.Read(stream, extension, snap);
    }
}
