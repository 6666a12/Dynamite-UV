using Godot;
using DuxShared.Chart;
using DuxShared.Chart.V2;
using DuxShared.Judge;
using SharedV2Integration = DuxShared.Chart.V2.V2Integration;

namespace DuxCommunity.Game;

/// <summary>
/// Typed client boundary around the strict shared v2 decoder/validator/runtime adapter/digest and
/// the shared clean legacy-to-v2 converter.
/// </summary>
internal static class V2Integration
{
    public static LoadedChart Load(ChartPack pack, ChartDiff diff, ChartLoadMode mode)
    {
        if (mode == ChartLoadMode.LegacyDirect)
        {
            if (pack.IsV2)
                throw new InvalidOperationException("legacy-direct cannot decode an explicit v2 package");
            return LoadLegacyDirect(pack, diff);
        }
        if (pack.IsV2)
            return AdaptShared(LoadStrictV2(pack, diff));

        // Normal legacy playback now converts in memory through the shared canonical semantic model.
        // Empty-timeline legacy charts cannot be represented in v2 and retain their established direct
        // path; explicit v2 packages never use this fallback.
        try
        {
            return AdaptShared(LoadLegacyAsV2(pack, diff));
        }
        catch (InvalidDataException) when (mode == ChartLoadMode.Default)
        {
            return LoadLegacyDirect(pack, diff);
        }
    }

    private static V2LoadedPackageChart LoadStrictV2(ChartPack pack, ChartDiff diff)
    {
        var root = ProjectSettings.GlobalizePath(pack.DirPath);
        var loaded = SharedV2Integration.LoadStrict(root, diff.ChartId);

        // Reuse the shared cross-file validator with Godot audio probing so preview and last-main-
        // judgement duration bounds are enforced in addition to the bridge's path/tree checks.
        string Resolve(string relative) => Path.Combine(root,
            relative.Replace('/', Path.DirectorySeparatorChar));
        double? AudioDurationSeconds(string relative)
        {
            var duration = Res.LoadAudio(Resolve(relative))?.GetLength();
            return duration is > 0.0 ? duration : null;
        }
        V2PackageValidator.Validate(loaded.SemanticPack, new V2PackageValidationHooks
        {
            FileExists = relative => File.Exists(Resolve(relative)),
            LoadChart = entry => V2JsonDecoder.DecodeChartFile(Resolve(entry.File)),
            ValidateResolvedAudio = (_, relative) =>
            {
                if (Res.LoadAudio(Resolve(relative)) == null)
                    throw new InvalidDataException(
                        $"Unsupported or undecodable v2 audio '{relative}'.");
            },
            AudioDurationSeconds = AudioDurationSeconds,
        }, Resolve("meta.json"));
        return loaded;
    }

    private static V2LoadedPackageChart LoadLegacyAsV2(ChartPack pack, ChartDiff diff)
    {
        var legacy = DynamixChartLoader.Load(
            Godot.FileAccess.GetFileAsString(pack.ChartPathFor(diff)));
        var audioPath = pack.AudioPathFor(diff);
        return SharedV2Integration.ConvertLegacyPackageChart(legacy,
            Godot.FileAccess.GetFileAsBytes(audioPath), pack.Id, diff.ChartId,
            PresetFor(diff.Difficulty), audioPath);
    }

    private static LoadedChart AdaptShared(V2LoadedPackageChart shared)
    {
        var metadata = shared.RuntimeChart.V2Metadata ??
            throw new InvalidDataException("shared v2 adapter omitted runtime metadata");
        var exactTimes = shared.RuntimeChart.AllNotes
            .Where(note => note.V2Metadata != null)
            .ToDictionary(note => note.Id, note => note.V2Metadata!.ExactTime);
        return new LoadedChart
        {
            RuntimeChart = shared.RuntimeChart,
            V2Pack = shared.SemanticPack,
            PackId = shared.PackId,
            ChartId = shared.ChartId,
            RulesetId = shared.RulesetId,
            GameplayDigest = shared.GameplayDigest,
            ResolvedAudioPath = shared.ResolvedAudioPath,
            NoteCount = shared.NoteCount,
            DurationSec = DerivedMainDuration(shared.SemanticChart, metadata.BpmTimeline) + 2.0,
            Preset = shared.Preset,
            SyncAccentRuntimeIds = shared.SyncAccentRuntimeIds,
            SourceIdsByRuntimeId = shared.SourceIdsByRuntimeId,
            ExactTimesByRuntimeId = exactTimes,
            V2Timeline = metadata.BpmTimeline,
            V2Scroll = metadata.ScrollMap,
        };
    }

    private static LoadedChart LoadLegacyDirect(ChartPack pack, ChartDiff diff)
    {
        var chart = DynamixChartLoader.Load(
            Godot.FileAccess.GetFileAsString(pack.ChartPathFor(diff)));
        var preset = PresetFor(diff.Difficulty);
        var plan = JudgePlan.Build(chart, JudgeSettings.ForPreset(preset));
        return new LoadedChart
        {
            RuntimeChart = chart,
            PackId = pack.Id,
            ChartId = diff.ChartId,
            RulesetId = null,
            GameplayDigest = null,
            ResolvedAudioPath = pack.AudioPathFor(diff),
            NoteCount = plan.HeadlineUnitCount,
            DurationSec = plan.EndTime + 2.0,
            Preset = preset,
            SyncAccentRuntimeIds = DeriveLegacySyncAccents(chart),
        };
    }

    internal static IReadOnlySet<int> DeriveLegacySyncAccents(Chart chart)
    {
        var result = new HashSet<int>();
        // GroupBy(double) uses exact binary64 equality. Never use epsilon and never trust baked sync.
        foreach (var group in chart.AllNotes
            .Where(note => note.Type is NoteType.Tap or NoteType.ExTap or
                NoteType.HoldHead or NoteType.MixerHead)
            .GroupBy(note => note.BarTime))
        {
            if (group.Select(note => note.Track).Distinct().Take(2).Count() < 2)
                continue;
            foreach (var tap in group.Where(note => note.Type == NoteType.Tap))
                result.Add(tap.Id);
        }
        return result;
    }

    private static double DerivedMainDuration(V2Chart chart, V2BpmTimeline timeline)
    {
        var latest = 0.0;
        foreach (var (_, note) in chart.AllNotes)
        {
            IEnumerable<ExactBarTime> times = note.Type switch
            {
                V2NoteType.BarLine => [],
                V2NoteType.Hold => new[] { note.Time }.Concat(
                    ((V2PathNote)note).Nodes.Where(node => node.Judge == true)
                        .Select(node => node.Time)),
                V2NoteType.Mixer => new[] { note.Time }.Concat(
                    V2MixerTicks.Enumerate((V2PathNote)note)),
                _ => [note.Time],
            };
            foreach (var time in times)
                latest = Math.Max(latest, timeline.ToSeconds(time));
        }
        return latest;
    }

    internal static JudgePreset PresetFor(string difficulty) => difficulty.ToLowerInvariant() switch
    {
        "tutorial" => JudgePreset.Tutorial,
        "casual" => JudgePreset.Casual,
        "normal" => JudgePreset.Normal,
        _ => JudgePreset.Hard,
    };
}
