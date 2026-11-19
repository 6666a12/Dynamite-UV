using System.Text.Json.Nodes;
using DynamiteUniverse.ChartEditor.Core;
using DynamiteUniverse.Shared.Chart.V2;
using static EditorTestSupport;

internal static class EditorDifficultyWorkflowTests
{
    internal static AddDifficultyRequest Request(string id, DifficultyAudioSource source = DifficultyAudioSource.Current,
        string? audio = null) => new(new EditorCreateForm("", "", "", "", id, (int)V2Difficulty.Hard,
        "", 8, false, "Second author", 96, .5, null), source, ExternalAudioPath: audio);
    internal static PreparedDifficulty Prepare(EditorDocumentSession session, AddDifficultyRequest request) =>
        session.PrepareDifficultyAsync(request).GetAwaiter().GetResult();
    internal static EditorPackageSettingsRequest Settings(EditorDocument doc)
    {
        var c = doc.SelectedChart;
        return new(doc.PackId, doc.Title, doc.Artist, c.Id, c.File, c.Difficulty, c.DifficultyKey,
            c.Level, c.Unrated, c.Charters.ToArray(), c.AudioOffsetSec, doc.Preview, c.Preview, null, null, null, false, false);
    }
    internal static void Apply(EditorDocument doc, EditorPackageSettingsRequest request) =>
        EditorPackageSettings.Apply(doc, doc.SelectedChartId, request);
    internal static string DifferentWav(string root, string audio, string name, byte value)
    {
        var destination = Path.Combine(root, name); var bytes = File.ReadAllBytes(audio);
        bytes[^1] ^= value; File.WriteAllBytes(destination, bytes); return destination;
    }

    internal static void Run()
    {
        WithExternalResources(AuthorAndRoundTrip);
        WithExternalResources(ChartOnlyPackage);
        WithExternalResources(RejectInvalidPlans);
        WithExternalResources(CustomNames);
        EditorMediaOwnershipTests.Run();
    }

    private static void AuthorAndRoundTrip(string root, string audio, string cover)
    {
        var session = new EditorDocumentSession();
        var doc = session.CreateDraft(BuildDraftRequest(audio, cover) with { InitialBpm = 120, AudioOffsetSec = .25 });
        var reference = doc.SelectedChart;
        var third = ExactBarTime.FromFraction(13, 3);
        doc.Execute(new AddBpmCommand(third, 180.25));
        doc.Execute(new AddScrollCommand(ExactBarTime.Zero, 1));
        doc.Execute(new AddScrollCommand(third, 2));
        AddTap(doc, "original");
        var alternate = DifferentWav(root, audio, "alternate.wav", 1);
        var external = DifferentWav(root, audio, "replacement.wav", 2);
        Apply(doc, Settings(doc) with { ChartAudioSource = alternate, ChartPreview = new V2Preview(0, 1) });
        var plan = Prepare(session, Request("current"));
        Check(plan.AudioPath == alternate && plan.EffectivePreview is null && plan.AudioDurationSec > 0,
            "Current resolves pending overrides; null preview does not materialize an excerpt");
        session.AddDifficulty(plan);
        var fresh = doc.SelectedChart;
        Check(fresh.Audio == reference.Audio && fresh.Preview is null && fresh.AudioOffsetSec == .25 && fresh.Notes.Count == 0 &&
              fresh.ScrollSpeeds.Count == 0 && fresh.Key != reference.Key && fresh.Bpms.Count == 2 &&
              fresh.Bpms[1].Time == third && fresh.Bpms[1].Bpm == 180.25 &&
              fresh.Bpms.Zip(reference.Bpms).All(pair => pair.First.Key != pair.Second.Key && pair.First != pair.Second),
            "blank current chart copies the exact full BPM timeline and offset with fresh identities, no notes or Scroll");
        doc.Execute(new EditBpmCommand(third, third, 130));
        Check(reference.Bpms[1].Bpm == 180.25, "changing copied BPM does not modify reference chart");
        doc.Undo();
        ExpectException<V2DiagnosticException>(() => session.Save(Path.Combine(root, "invalid")),
            "strict writer still rejects a blank difficulty");
        AddTap(doc, "current-note");
        doc.SelectChart(reference.Id);
        plan = Prepare(session, Request("default", DifficultyAudioSource.PackageDefault) with { CopyScroll = true });
        Check(plan.AudioPath == audio, "package source resolves original draft audio despite selected override");
        session.AddDifficulty(plan);
        Check(doc.SelectedChart.Audio is null && doc.SelectedChart.ScrollSpeeds.Count == 2 &&
              doc.SelectedChart.ScrollSpeeds[1].Time == third && doc.SelectedChart.ScrollSpeeds[1].Key != reference.ScrollSpeeds[1].Key,
            "package-default chart follows pack; optional Scroll is copied with new identities");
        AddTap(doc, "default-note");
        doc.SelectChart(reference.Id);
        plan = Prepare(session, Request("external", DifficultyAudioSource.External, external) with
            { CopyTiming = false, PreviewOverride = new V2Preview(0, 1) });
        session.AddDifficulty(plan);
        var ownSlot = doc.SelectedChart.Audio!;
        Check(session.ResolveDigestAudio() == external && doc.SelectedChart.AudioOffsetSec == .5 &&
              doc.SelectedChart.Bpms.Single().Bpm == 96 && doc.SelectedChart.Notes.Count == 0,
            "external recording can start with a new BPM and offset");
        doc.Undo();
        Check(doc.SelectedChartId == reference.Id && doc.Charts.Count == 3 &&
              !doc.PendingResourceReplacements.Any(x => x.PackageRelativePath == ownSlot),
            "one undo removes both new chart and external mapping and returns to reference");
        doc.Redo();
        Check(doc.SelectedChartId == "external" && session.ResolveDigestAudio() == external,
            "one redo restores chart and recording");
        AddTap(doc, "external-note");
        var dir = Path.Combine(root, "roundtrip"); session.Save(dir);
        var rebuilt = session.Open(dir);
        foreach (var (id, path) in new[]{("hard", alternate), ("current", alternate), ("default", audio), ("external", external)})
        {
            rebuilt.SelectChart(id);
            Check(File.ReadAllBytes(session.ResolveDigestAudio()!).SequenceEqual(File.ReadAllBytes(path)),
                $"saved chart {id} reopens on its chosen recording");
        }
        Check(rebuilt.SelectedChart.Preview == new V2Preview(0,1) && rebuilt.Charts.Single(c=>c.Id=="current").Bpms[1].Time == third,
            "explicit preview and fractional tempo survive reopening");
    }

    private static void ChartOnlyPackage(string root, string audio, string cover)
    {
        var session = new EditorDocumentSession(); var doc = session.CreateDraft(BuildDraftRequest(audio, cover));
        AddTap(doc,"one"); var dir = Path.Combine(root,"individual"); session.Save(dir);
        var meta = Path.Combine(dir,"meta.json"); var json = JsonNode.Parse(File.ReadAllBytes(meta))!.AsObject();
        json["charts"]![0]!["audio"] = (string?)json["audio"]; json.Remove("audio"); File.WriteAllText(meta,json.ToJsonString());
        doc = session.Open(dir);
        ExpectException<InvalidOperationException>(()=>Prepare(session,Request("bad", DifficultyAudioSource.PackageDefault)),
            "package without default refuses package source before mutation");
        session.AddDifficulty(Prepare(session,Request("shared")));
        Check(doc.SelectedChart.Audio == "audio.wav", "per-entry-only package can add a blank difficulty using current recording");
        AddTap(doc,"two"); session.Save(dir); doc=session.Open(dir);
        Check(doc.Audio is null && doc.Charts.Count == 2 && doc.Charts.All(c=>c.Audio=="audio.wav"),
            "no fabricated package default after strict round trip");
    }

    private static void RejectInvalidPlans(string root, string audio, string cover)
    {
        var session = new EditorDocumentSession(); var doc = session.CreateDraft(BuildDraftRequest(audio, cover));
        Apply(doc, Settings(doc) with { PackPreview = new V2Preview(1,4) });
        var shortFile = Path.Combine(root,"short.wav");
        using(var w = new BinaryWriter(File.Create(shortFile)))
        {
            const int size = 8000 * 2; w.Write("RIFF"u8); w.Write(36 + size); w.Write("WAVEfmt "u8); w.Write(16);
            w.Write((short)1); w.Write((short)1); w.Write(8000); w.Write(16000); w.Write((short)2); w.Write((short)16);
            w.Write("data"u8); w.Write(size); w.Write(new byte[size]);
        }
        var req = Request("short",DifficultyAudioSource.External,shortFile);
        var version = doc.ChangeVersion;
        ExpectException<InvalidOperationException>(() => Prepare(session,req), "real one-second WAV rejects inherited [1s,+4s] preview before creation");
        Check(doc.ChangeVersion == version && doc.Charts.Count == 1 && !doc.PendingResourceReplacements.Any(), "failed preview preflight makes no changes");
        req = req with {PreviewOverride = new V2Preview(0,1)};
        var plan = Prepare(session,req); Check(plan.AudioDurationSec == 1 && plan.EffectivePreview == new V2Preview(0,1), "corrected preview fits actual short recording");
        AddTap(doc,"change");
        ExpectException<InvalidOperationException>(() => session.AddDifficulty(plan), "version-stale plan cannot commit");
        var cancelled = new CancellationTokenSource(); plan = session.PrepareDifficultyAsync(req, cancellationToken:cancelled.Token).GetAwaiter().GetResult();
        cancelled.Cancel(); ExpectException<OperationCanceledException>(() => session.AddDifficulty(plan), "cancelled plan cannot commit");
        plan = Prepare(session,req); session.AddDifficulty(Prepare(session,Request("other")));
        ExpectException<InvalidOperationException>(() => session.AddDifficulty(plan), "selection/version invalidates plan");
        plan = Prepare(session,req); doc.SelectChart("hard");
        ExpectException<InvalidOperationException>(() => session.AddDifficulty(plan), "switch of reference alone invalidates plan");
        ExpectException<InvalidOperationException>(() => Prepare(session,Request("hard")), "duplicate chart ID is rejected before submission");
    }

    private static void CustomNames(string root, string audio, string cover)
    {
        var s = new EditorDocumentSession(); var d = s.CreateDraft(BuildDraftRequest(audio,cover) with
            { ChartId = new string('x',64), Difficulty = V2Difficulty.Custom, DifficultyKey = new string('Q',24) });
        var src = d.SelectedChart; var request = Request("new") with { Form = Request("new").Form with
            {DifficultyIndex=(int)V2Difficulty.Custom, CustomKey=src.DifficultyKey!.ToLowerInvariant()} };
        ExpectException<InvalidOperationException>(() => Prepare(s,request),"case-folding custom key collision rejected before creating blank chart");
        var named = EditorDifficultyNames.SuggestCopy(d);
        d.Execute(new CopyChartCommand(src.Id,named.ChartId,named.File,difficultyKey:named.DifficultyKey));
        Check(d.Charts.Count == 2 && src.Id != d.SelectedChartId && named.ChartId.Length <= 64 && d.SelectedChart.DifficultyKey != src.DifficultyKey &&
              d.SelectedChart.DifficultyKey!.Length <= 24, "custom copy gets distinct length-bounded ID and key");
        d.SelectChart(src.Id); var second = EditorDifficultyNames.SuggestCopy(d);
        Check(second.ChartId != named.ChartId && second.DifficultyKey != named.DifficultyKey, "consecutive copies avoid prior suggested identities");
    }
}
