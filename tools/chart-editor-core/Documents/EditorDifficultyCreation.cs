using DynamiteUniverse.Shared.Chart.V2;

namespace DynamiteUniverse.ChartEditor.Core;

public enum DifficultyAudioSource { Current, PackageDefault, External }

/// <param name="PreviewOverride">An explicit chart interval; null always follows pack.preview (or the client fallback).</param>
public sealed record AddDifficultyRequest(EditorCreateForm Form,
    DifficultyAudioSource AudioSource = DifficultyAudioSource.Current, bool CopyTiming = true,
    bool CopyScroll = false, string? ExternalAudioPath = null, V2Preview? PreviewOverride = null);

/// <summary>A validated, immutable-to-callers creation plan. Only its originating session can apply it.</summary>
public sealed class PreparedDifficulty
{
    private readonly EditorDocument _owner;
    private readonly long _version;
    private readonly string? _root;
    private readonly EditableChart _chart;
    private readonly bool _importsAudio;
    private readonly string _relativeAudio;
    private long _audioBytes;
    private DateTime _audioWriteTime;
    private CancellationToken _cancellation;
    public string ReferenceChartId { get; }
    public string ChartId => _chart.Id;
    public string AudioPath { get; }
    public V2Preview? EffectivePreview { get; }
    public double AudioDurationSec { get; private set; }

    internal PreparedDifficulty(EditorDocument owner, EditableChart chart, string relativeAudio,
        string absoluteAudio, bool importsAudio)
    {
        _owner = owner;
        _version = owner.ChangeVersion;
        _root = owner.PackageDirectory;
        ReferenceChartId = owner.SelectedChartId;
        _chart = chart;
        _importsAudio = importsAudio;
        _relativeAudio = relativeAudio;
        AudioPath = absoluteAudio;
        EffectivePreview = chart.Preview ?? owner.Preview;
    }

    internal PreparedDifficulty Inspect(IV2PackageAudioProbe probe, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var file = new FileInfo(AudioPath);
        if (!file.Exists || file.Attributes.HasFlag(FileAttributes.ReparsePoint) || file.LinkTarget is not null)
            throw new IOException($"{ChartId}: 录音不存在或不是普通文件 / Audio is missing or not a regular file: {AudioPath}");
        _audioWriteTime = file.LastWriteTimeUtc;
        _audioBytes = file.Length;
        _cancellation = token;
        var duration = probe.Probe(AudioPath, _relativeAudio).DurationSeconds;
        if (!double.IsFinite(duration) || duration <= 0)
            throw new InvalidDataException($"{ChartId}: 音频时长无效 / Invalid recording duration");
        if (EffectivePreview is { } preview && preview.StartSec + preview.DurationSec > duration)
            throw new InvalidOperationException($"{ChartId}: 试听区间超出录音 / Preview exceeds recording: " +
                $"{preview.StartSec:R}s + {preview.DurationSec:R}s > {duration:R}s ({_relativeAudio})");
        token.ThrowIfCancellationRequested();
        AudioDurationSec = duration;
        return this;
    }

    internal void Apply(EditorDocument? current)
    {
        if (!ReferenceEquals(_owner, current) || _owner.ChangeVersion != _version ||
            _owner.PackageDirectory != _root || _owner.SelectedChartId != ReferenceChartId)
            throw new InvalidOperationException("参考谱或谱包已更改，请重新确认 / The reference chart or package changed; try again.");
        _cancellation.ThrowIfCancellationRequested();
        var file = new FileInfo(AudioPath);
        if (!file.Exists || file.Attributes.HasFlag(FileAttributes.ReparsePoint) || file.LinkTarget is not null ||
            file.LastWriteTimeUtc != _audioWriteTime || file.Length != _audioBytes)
            throw new IOException($"录音已变化，请重新确认 / Recording changed; check it again: {AudioPath}");
        var occupied = EditorMediaSlots.Occupied(_owner);
        if (!EditorMediaSlots.Available(_owner, occupied, _chart.File) ||
            (_importsAudio && !EditorMediaSlots.Available(_owner, occupied, _relativeAudio)))
            throw new IOException("目标文件已存在，请重新确认 / A destination is now in use.");
        _owner.Execute(_importsAudio
            ? new AddChartCommand(_chart, new V2ExternalResourceMapping(AudioPath, _relativeAudio))
            : new AddChartCommand(_chart));
    }
}

internal static class EditorDifficultyCreation
{
    // Capture the document on its owner thread, then give only detached data to the audio worker.
    public static Task<PreparedDifficulty> PrepareAsync(EditorDocument document, EditorProjectDraft? draft,
        AddDifficultyRequest request, IV2PackageAudioProbe? probe, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var reference = document.SelectedChart;
        var form = request.Form;
        var pack = document.BuildPackSnapshot();
        var summary = new EditorCreatePackageSummary(pack.Charts.Select(e => e.Id).ToArray(),
            pack.Charts.Select(e => new EditorCreateChartSummary(e.Difficulty, e.DifficultyKey)).ToArray());
        if (EditorCreatePreflight.Validate(EditorCreateMode.AddDifficulty, form, summary, true, true) is { } error)
            throw new InvalidOperationException(error);
        if (!Enum.IsDefined((V2Difficulty)form.DifficultyIndex)) throw new ArgumentException("未知难度 / Invalid difficulty");
        var kind = (V2Difficulty)form.DifficultyIndex;
        var id = form.ChartId;
        var chart = new EditableChart { Id = id, Difficulty = kind,
            DifficultyKey = kind == V2Difficulty.Custom ? form.CustomKey : null,
            Level = form.Unrated ? null : form.Level, Unrated = form.Unrated, File = $"charts/{id}.json",
            AudioOffsetSec = request.CopyTiming ? reference.AudioOffsetSec : form.OffsetSec,
            Preview = request.PreviewOverride };
        if (!EditorMediaSlots.Available(document, EditorMediaSlots.Occupied(document), chart.File))
            throw new InvalidOperationException($"{chart.File}: 谱面文件名已被占用 / Chart filename is in use.");
        chart.Charters.Add(form.Charter);
        if (request.CopyTiming)
            chart.Bpms.AddRange(reference.Bpms.Select(e => new EditableBpm { Time = e.Time, Bpm = e.Bpm }));
        else
        {
            if (!double.IsFinite(form.Bpm) || form.Bpm <= 0 || !double.IsFinite(form.OffsetSec))
                throw new InvalidOperationException("初始 BPM 须为正数，偏移须有限 / Invalid initial BPM or offset.");
            chart.Bpms.Add(new EditableBpm { Time = ExactBarTime.Zero, Bpm = form.Bpm });
        }
        if (request.CopyScroll)
            chart.ScrollSpeeds.AddRange(reference.ScrollSpeeds.Select(e => new EditableScroll
                { Time = e.Time, Value = e.Value, CurveToNext = e.CurveToNext, CustomExpr = e.CustomExpr }));

        var external = request.AudioSource == DifficultyAudioSource.External;
        chart.Audio = request.AudioSource switch
        {
            DifficultyAudioSource.Current => reference.Audio,
            DifficultyAudioSource.PackageDefault => null,
            DifficultyAudioSource.External when !string.IsNullOrWhiteSpace(request.ExternalAudioPath) =>
                EditorMediaSlots.AudioDestination(document, null, request.ExternalAudioPath!, newChartId: id),
            _ => throw new ArgumentException("请选择录音 / Choose a recording.")
        };
        var slot = chart.Audio ?? pack.Audio ?? throw new InvalidOperationException(
            "谱包没有默认录音；请沿用当前录音或另选文件 / No package default; choose the current or an external recording.");
        var entry = new V2ChartEntry { Id = id, File = chart.File, Difficulty = kind,
            DifficultyKey = chart.DifficultyKey, Level = chart.Level, Unrated = chart.Unrated,
            Charters = chart.Charters, Audio = chart.Audio, Preview = chart.Preview };
        V2SemanticValidator.ValidatePack(pack with { Charts = [..pack.Charts, entry] });
        var absolute = external ? Path.GetFullPath(request.ExternalAudioPath!) :
            document.GetPendingResource(slot) ?? (document.PackageDirectory is { } root ?
                V2PackageWriter.ResolveRegularFile(root, slot) : draft?.ExternalResources.GetValueOrDefault(slot))
            ?? throw new FileNotFoundException("找不到录音 / Cannot resolve recording", slot);
        var plan = new PreparedDifficulty(document, chart, slot, absolute, external);
        return Task.Run(() => plan.Inspect(probe ?? V2BuiltInAudioProbe.Instance, token), token);
    }
}
