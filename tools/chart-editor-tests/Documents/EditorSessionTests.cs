using DynamiteUniverse.ChartEditor.Core;
using DynamiteUniverse.Shared.Chart.V2;
using static EditorTestSupport;

internal static class EditorSessionTests
{
    internal static void Run()
    {
        WithExternalResources((root, audio, cover) =>
        {
            var session = new EditorDocumentSession();
            var document = session.CreateDraft(BuildDraftRequest(audio, cover));
            var draft = session.Draft;
            Check(ReferenceEquals(document, session.Document) && draft is not null &&
                  session.ResolveDigestAudio() == Path.GetFullPath(audio),
                "session owns the new document and its external resources");
            var empty = document.BuildChartSnapshot(document.SelectedChartId);
            var previewJson = V2JsonEncoder.EncodeChart(empty, requireMainJudgement: false);
            var preview = V2JsonDecoder.DecodeChart(previewJson, "editor-preview", requireMainJudgement: false);
            var adapted = V2RuntimeAdapter.Adapt(preview, requireMainJudgement: false);
            Check(!adapted.RuntimeChart.AllNotes.Any() && preview.Bpms.Count == 1,
                "empty in-memory draft survives preview encode/decode/runtime adaptation");
            ExpectException<V2DiagnosticException>(() => V2JsonEncoder.EncodeChart(empty),
                "strict chart encoding still rejects an empty draft by default");
            AddTap(document, "first");
            var audioSha = Convert.ToHexStringLower(
                System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(audio)));
            var packEntry = document.BuildPackSnapshot().Charts.Single();
            Check(EditorGameplayDigest.Compute(document, audioSha).Sha256 == V2GameplayDigest.Compute(
                    document.BuildChartSnapshot(document.SelectedChartId), packEntry.JudgePreset, audioSha).Sha256,
                "gameplay digest uses the current selected chart without a Godot host");
            var second = new EditableChart
            {
                Id = "second", File = "charts/second.json", Difficulty = V2Difficulty.Mega,
                Level = 12, Unrated = false, AudioOffsetSec = 0,
            };
            second.Charters.Add("Community Charter");
            second.Bpms.Add(new EditableBpm { Time = ExactBarTime.Zero, Bpm = 150 });
            session.AddChart(second);
            AddTap(document, "second-tap");
            Check(ReferenceEquals(session.Draft, draft),
                "adding a difficulty to an unsaved package preserves first-save audio/cover");
            ExpectException<InvalidOperationException>(() => session.AddChart(second),
                "duplicate difficulty is rejected");
            Check(ReferenceEquals(session.Draft, draft) && document.Charts.Count == 2,
                "rejected difficulty leaves the original draft and charts intact");

            ExpectException<Exception>(() => session.Open(Path.Combine(root, "missing")),
                "session rejects an invalid package open");
            ExpectException<Exception>(() => session.CreateDraft(BuildDraftRequest(Path.Combine(root, "missing.wav"))),
                "session rejects invalid replacement draft resources");
            Check(ReferenceEquals(session.Document, document) && ReferenceEquals(session.Draft, draft),
                "failed open/create cannot replace the active dirty document");

            var blocked = Path.Combine(root, "occupied");
            Directory.CreateDirectory(blocked);
            File.WriteAllText(Path.Combine(blocked, "keep.txt"), "keep");
            ExpectException<Exception>(() => session.Save(blocked), "first save rejects a nonempty destination");
            Check(document.IsUnpersisted && document.HasUnsavedChanges && ReferenceEquals(session.Draft, draft) &&
                  File.ReadAllText(Path.Combine(blocked, "keep.txt")) == "keep",
                "failed first save preserves draft, dirty state and existing destination");

            var created = session.Save(Path.Combine(root, "first-save"));
            var reopened = EditorPackageRepository.Open(created.DestinationDirectory);
            Check(created.Kind == EditorSaveKind.Created && created.ChartCount == 2 &&
                  !document.HasUnsavedChanges && !document.IsUnpersisted && session.Draft is null &&
                  reopened.Charts.Count == 2 && File.Exists(Path.Combine(created.DestinationDirectory, "cover.png")),
                "multi-difficulty draft first save publishes audio/cover and clears draft only after success");
            Check(session.ResolveDigestAudio() == Path.Combine(created.DestinationDirectory, "audio.wav"),
                "persisted session resolves digest audio inside the saved package");

            AddTap(document, "third-tap");
            var beforeFailure = File.ReadAllBytes(Path.Combine(created.DestinationDirectory, "meta.json"));
            ExpectException<Exception>(() => session.Save(blocked), "save as rejects nonempty destination");
            Check(document.HasUnsavedChanges && document.PackageDirectory == created.DestinationDirectory &&
                  File.ReadAllBytes(Path.Combine(created.DestinationDirectory, "meta.json")).SequenceEqual(beforeFailure),
                "failed save as preserves source path, source bytes and dirty state");
            var saved = session.Save(created.DestinationDirectory);
            Check(saved.Kind == EditorSaveKind.Saved && !document.HasUnsavedChanges,
                "same-directory save marks the existing session clean");
            var copied = session.Save(Path.Combine(root, "save-as"));
            Check(document.PackageDirectory == copied.DestinationDirectory &&
                  EditorPackageRepository.Open(copied.DestinationDirectory).Charts.Count == 2,
                "save as adopts the new path without losing charts");
            var opened = session.Open(created.DestinationDirectory);
            Check(ReferenceEquals(session.Document, opened) && !ReferenceEquals(opened, document) && session.Draft is null,
                "successful open replaces the session only with a validated document");
        });
    }
}
