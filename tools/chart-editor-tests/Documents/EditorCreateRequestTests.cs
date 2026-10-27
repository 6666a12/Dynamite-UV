using DynamiteUniverse.ChartEditor.Core;
using DynamiteUniverse.Shared.Chart.V2;
using static EditorTestSupport;

internal static class EditorCreateRequestTests
{
    internal static void Run()
    {
        WithExternalResources((_, audio, cover) =>
        {
            var form = new EditorCreateForm("pack", "Title", "Artist", audio, "chart-01", 6,
                "Signal", null, true, "Charter", 172.5, -.025, cover);
            var result = EditorCreateRequest.Build(EditorCreateMode.NewPackage, form, 24);
            var request = result.DraftRequest!;
            Check(result.Chart is null && request.PackId == "pack" && request.ChartId == "chart-01" &&
                  request.Difficulty == V2Difficulty.Custom && request.DifficultyKey == "Signal" &&
                  request.Unrated && request.Level is null && request.InitialBpm == 172.5 &&
                  request.AudioOffsetSec == -.025 && request.GridDivisor == 24 &&
                  request.ExternalAudioSource == audio && request.ExternalCoverSource == cover,
                "new package request retains custom, unrated, timing, grid, and external resources");
            Check(EditorProjectDraftFactory.Create(request).Document.BuildChartSnapshot("chart-01").AudioOffsetSec == -.025,
                "new package request remains accepted by the draft factory");

            var package = new EditorCreatePackageSummary(["hard"],
                [new EditorCreateChartSummary(V2Difficulty.Custom, "Élite")]);
            var addForm = form with { ChartId = "chart-02", DifficultyIndex = 2, CustomKey = "", Level = 10, Unrated = false };
            var add = EditorCreateRequest.Build(EditorCreateMode.AddDifficulty, addForm, 24).Chart!;
            Check(add.File == "charts/chart-02.json" && add.Audio is null && add.Preview is null &&
                  add.AudioOffsetSec == 0 && add.Charters.SequenceEqual(["Charter"]) &&
                  add.Bpms.Count == 1 && add.Bpms[0].Time == ExactBarTime.Zero && add.Bpms[0].Bpm == 150,
                "add difficulty request keeps fixed timing and in-package resource semantics");
            Check(EditorCreatePreflight.Validate(EditorCreateMode.AddDifficulty, addForm, package, false, false) is null,
                "add difficulty preflight does not require external package resources");
            Check(EditorCreatePreflight.Validate(EditorCreateMode.AddDifficulty,
                    addForm with { DifficultyIndex = 6, CustomKey = "e\u0301LITE" }, package, false, false) ==
                  "difficulty key 'e\u0301LITE' 在本包内已存在。",
                "add difficulty preflight folds custom keys using NFC and case folding");

            Check(EditorCreatePreflight.Validate(EditorCreateMode.NewPackage, form with { AudioPath = "" }, null, false, false) ==
                  "新建包需要一个可访问的外部音频文件。" &&
                  EditorCreatePreflight.Validate(EditorCreateMode.NewPackage, form with { AudioPath = "audio.txt" }, null, true, false) ==
                  "音频扩展名不被支持：需要 .wav/.mp3/.flac/.ogg/.opus/.m4a/.aac。" &&
                  EditorCreatePreflight.Validate(EditorCreateMode.NewPackage, form with { CoverPath = "missing.png" }, null, true, false) ==
                  "封面文件不存在或不可访问。",
                "new package preflight retains resource availability and supported audio checks");
        });
    }
}
