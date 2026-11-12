using System.Text;
using DynamiteUniverse.ChartEditor.Core;
using DynamiteUniverse.Shared.Chart.V2;

/// <summary>Original, deterministic media and authored data; no developer/Internal corpus required.</summary>
internal static class PersistenceFixture
{
    internal static EditorDocumentSession Create(string sources)
    {
        Directory.CreateDirectory(sources);
        var audio = Path.Combine(sources, "合成节拍 source.wav");
        WriteWave(audio, 220);
        var cover = Path.Combine(sources, "原创 cover.png");
        // A complete 1x1 PNG, rather than the header-only resource used by older unit fixtures.
        File.WriteAllBytes(cover, Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR4nGMI2HLnPwAGGALgxtfrgAAAAABJRU5ErkJggg=="));
        var session = new EditorDocumentSession();
        var document = session.CreateDraft(new EditorProjectDraftRequest(
            "editor.persistence", "保存往返测试 / Persistence", "Clean-room synthesized audio",
            "hard", V2Difficulty.Hard, null, 10, false, "自动测试谱师", 150, .125, 12, audio, cover));
        Populate(document);
        document.Execute(new EditPreviewCommand(true, null, 2, 12));
        foreach (var id in new[] { "normal", "mega" })
        {
            var chart = new EditableChart
            {
                Id = id, File = $"charts/{id}.json",
                Difficulty = id == "normal" ? V2Difficulty.Normal : V2Difficulty.Mega,
                Level = id == "normal" ? 6 : 12, Unrated = false, AudioOffsetSec = -.075,
            };
            chart.Charters.AddRange(["测试谱师", "Second Charter"]);
            chart.Bpms.Add(new EditableBpm { Time = ExactBarTime.Zero, Bpm = 150 });
            session.AddChart(chart);
            Populate(document);
            document.Execute(new EditPreviewCommand(false, id, 3, 10));
        }
        var alternate = Path.Combine(sources, "独立差分.wav");
        WriteWave(alternate, 330);
        document.Execute(new ReplaceChartResourceCommand("mega", false, false, "audio/mega.wav", alternate));
        document.SelectChart("hard");
        return session;
    }

    private static void Populate(EditorDocument document)
    {
        var types = Enum.GetValues<V2NoteType>();
        var curves = Enum.GetValues<V2PathCurve>();
        var denominators = new[] { 3, 5, 12 };
        for (var section = 0; section < 12; section++)
        for (var track = 0; track < 3; track++)
        for (var typeIndex = 0; typeIndex < types.Length; typeIndex++)
        {
            var type = types[typeIndex];
            var id = $"n-{section}-{track}-{typeIndex}";
            var time = ExactBarTime.FromFraction(section * 2, 1) +
                ExactBarTime.FromFraction(typeIndex, 12) + ExactBarTime.FromFraction(1, denominators[section % 3]);
            var note = new EditableNote
            {
                Id = id, Type = type, Track = (EditorTrack)track, Time = time,
                Center = section == 11 ? (track == 0 ? -.2 : 5.2) : .6 + typeIndex * .6,
                Width = .6, ScrollSpeedMultiplier = section % 2 == 0 ? 1.35 : .75,
            };
            if (type is V2NoteType.Hold or V2NoteType.Mixer)
            {
                note.CurveToNext = curves[section % curves.Length];
                note.Nodes.Add(new EditablePathNode
                {
                    Id = id + "-mid", Time = time + ExactBarTime.FromFraction(1, 3),
                    Center = 2.4, Width = .8, CurveToNext = V2PathCurve.EaseInOutSine,
                    Judge = type == V2NoteType.Hold ? section % 2 == 0 : null,
                });
                note.Nodes.Add(new EditablePathNode
                {
                    Id = id + "-tail", Time = time + ExactBarTime.FromFraction(4, 5),
                    Center = 3.6, Width = 1.2, Judge = type == V2NoteType.Hold ? true : null,
                });
            }
            document.Execute(new AddNoteCommand(note));
        }
        document.Execute(new AddBpmCommand(ExactBarTime.FromFraction(8, 1), 180));
        document.Execute(new AddBpmCommand(ExactBarTime.FromFraction(16, 1), 127.5));
        var scrollCurves = Enum.GetValues<V2ScrollCurve>();
        for (var i = 0; i <= scrollCurves.Length; i++)
            document.Execute(new AddScrollCommand(ExactBarTime.FromFraction(i * 2, 1), 1 + i % 3 * .25));
        for (var i = 0; i < scrollCurves.Length; i++)
        {
            var time = ExactBarTime.FromFraction(i * 2, 1);
            document.Execute(new EditScrollCommand(time, time, 1 + i % 3 * .25, scrollCurves[i]));
        }
    }

    internal static void WriteWave(string path, double frequency)
    {
        const int sampleRate = 22050;
        const int frames = sampleRate * 120;
        using var writer = new BinaryWriter(File.Create(path));
        writer.Write(Encoding.ASCII.GetBytes("RIFF"));
        writer.Write(36 + frames * 2);
        writer.Write(Encoding.ASCII.GetBytes("WAVEfmt "));
        writer.Write(16); writer.Write((short)1); writer.Write((short)1);
        writer.Write(sampleRate); writer.Write(sampleRate * 2);
        writer.Write((short)2); writer.Write((short)16);
        writer.Write(Encoding.ASCII.GetBytes("data")); writer.Write(frames * 2);
        for (var i = 0; i < frames; i++)
        {
            var phase = (i / (double)sampleRate) % .4;
            var envelope = phase < .04 ? 1 - phase / .04 : 0;
            writer.Write((short)(Math.Sin(2 * Math.PI * frequency * i / sampleRate) * 2400 * envelope));
        }
    }
}
