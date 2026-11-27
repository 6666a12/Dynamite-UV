using System.Diagnostics;
using DynamiteUniverse.Shared.Storyboard;
using Godot;

namespace DynamiteUniverse.Game;

/// <summary>Main-thread deadline scheduler; only independent CPU Image decode/crop runs on the worker.</summary>
internal sealed class StoryboardTextureStore : IDisposable
{
    internal const long MiB = 1024 * 1024;
    private const long RealtimeUploadBytes = 16 * MiB;
    private readonly StoryboardTexturePlan _plan;
    private readonly Entry[] _entries;
    private readonly long _budget;
    private readonly double? _verificationLookahead;
    private readonly Action _clearCanvasCommands;
    private readonly List<int> _current = [], _candidates = [];
    private readonly List<Entry> _victims = [], _retireBatch = [];
    private readonly List<double> _decodeMs = [], _uploadMs = [];
    private readonly double[] _recentDecode = new double[16];
    private DecodeJob? _pending;
    private double _replanAt = double.NegativeInfinity, _lastTime = double.NaN, _lookahead = 1;
    private double _burstUntil = double.NegativeInfinity;
    private bool _pressured, _disposed, _wasWarm;
    private int _generation, _recentIndex, _reloads, _missingFrames, _maxSlots, _maxRetiring, _deferredLarge;
    private long _maxBytes;

    internal string? Failure { get; private set; }
    internal bool CurrentReady
    {
        get
        {
            foreach (var index in _current)
                if (_entries[index].Texture is null) return false;
            return true;
        }
    }

    internal StoryboardTextureStore(StoryboardTexturePlan plan, int tierMiB, Action clearCanvasCommands,
        double? verificationLookahead)
    {
        _plan = plan;
        _budget = tierMiB * MiB;
        _entries = Array.ConvertAll(plan.Requests, static request => new Entry(request));
        _clearCanvasCommands = clearCanvasCommands;
        _verificationLookahead = verificationLookahead;
    }

    internal Texture2D? Resolve(StoryboardImage image) =>
        _plan.Lookup.TryGetValue(image, out var index) ? _entries[index].Texture : null;

    internal void InvalidateRequests()
    {
        ++_generation;
        _pending?.Cancel();
        _pending = null;
        _lastTime = double.NaN;
        _replanAt = double.NegativeInfinity;
    }

    internal void MemoryPressure()
    {
        if (_disposed) return;
        _pressured = true;
        _replanAt = double.NegativeInfinity;
        // An already decoded speculative image must not take space from the active set.
        if (_pending is { } pending && !_entries[pending.Index].Pinned)
        {
            pending.Cancel();
            _pending = null;
        }
        RetireWhere(static entry => !entry.Pinned);
    }

    internal void Tick(double seconds, bool warming = false)
    {
        if (_disposed || Failure is not null || !double.IsFinite(seconds)) return;
        if (double.IsFinite(_lastTime) && seconds < _lastTime) InvalidateRequests();
        _lastTime = seconds;
        if (_wasWarm != warming)
        {
            _wasWarm = warming;
            _replanAt = double.NegativeInfinity;
        }
        var lookahead = _pressured ? .25 : _verificationLookahead ?? (_recentDecode.Max() > 40 || seconds < _burstUntil ? 2 : 1);
        if (seconds >= _replanAt || lookahead != _lookahead)
        {
            _lookahead = lookahead;
            Replan(seconds, lookahead, warming);
        }

        if (_pending is { } obsolete && _entries[obsolete.Index].NextUse - seconds > _lookahead)
        {
            obsolete.Cancel();
            _pending = null;
        }
        if (_pending is { } job && job.TryTake(out var image, out var failure))
        {
            _pending = null;
            var entry = _entries[job.Index];
            var key = entry.Request.Key;
            if (failure is not null || image is null || job.Generation != _generation ||
                !_wasWarm && key.TextureBytes > RealtimeUploadBytes)
            {
                image?.Dispose();
                StoryboardResidencyLedger.CancelJob(key.TextureBytes, key.ScratchBytes);
                if (failure is not null) { Failure = failure; return; }
            }
            else
            {
                var start = Stopwatch.GetTimestamp();
                Texture2D? texture = null;
                try
                {
                    using (image) texture = ImageTexture.CreateFromImage(image);
                    if (texture is null || texture.GetWidth() != key.Width || texture.GetHeight() != key.Height)
                        throw new InvalidDataException($"Unable to create Storyboard texture '{key.Source.Path}'");
                    entry.Texture = texture;
                    if (entry.LoadedBefore) ++_reloads;
                    entry.LoadedBefore = true;
                    var uploadMs = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
                    Record(_uploadMs, uploadMs);
                    Record(_decodeMs, job.DecodeMilliseconds);
                    _recentDecode[_recentIndex++ % _recentDecode.Length] = job.DecodeMilliseconds + uploadMs;
                }
                catch (Exception ex) when (ex is InvalidDataException or ArgumentException)
                {
                    texture?.Dispose();
                    Failure = ex.Message;
                }
                // Keep the decoded payload and one copy's worth of engine staging charged until GPU completion.
                var staging = Math.Min(key.ScratchBytes, 8L * key.Width * key.Height);
                StoryboardResidencyLedger.SubmitJob(key.ScratchBytes - staging, staging);
                if (Failure is not null)
                {
                    StoryboardResidencyLedger.Retire(key.TextureBytes, 1);
                    return;
                }
            }
        }

        if (_pending is null)
        {
            foreach (var index in _candidates)
            {
                var entry = _entries[index];
                if (entry.Texture is not null) continue;
                var key = entry.Request.Key;
                if (key.TextureBytes + key.ScratchBytes > _budget) continue;
                if (!warming && key.TextureBytes > RealtimeUploadBytes) continue;
                if (StoryboardResidencyLedger.TryReserve(_budget, key.TextureBytes, key.ScratchBytes))
                {
                    _pending = new(index, _generation, key);
                    _pending.Start();
                    break;
                }
                // Do not evict for a decode lane still occupied by a cancelled previous scene/seek.
                var snapshot = StoryboardResidencyLedger.Snapshot();
                if (snapshot.Worker) break;
                if (EvictFor(entry, snapshot.Bytes - snapshot.RetiringBytes, snapshot.Slots - snapshot.RetiringSlots)) break;
            }
        }

        if (!warming && !CurrentReady) ++_missingFrames;
        var budget = StoryboardResidencyLedger.Snapshot();
        _maxBytes = Math.Max(_maxBytes, budget.Bytes);
        _maxSlots = Math.Max(_maxSlots, budget.Slots);
        _maxRetiring = Math.Max(_maxRetiring, budget.RetiringSlots);
    }

    private void Replan(double seconds, double lookahead, bool warming)
    {
        _replanAt = double.PositiveInfinity;
        _current.Clear();
        _candidates.Clear();
        _victims.Clear();
        long nearBytes = 0;
        var nearCount = 0;
        for (var i = 0; i < _entries.Length; ++i)
        {
            var entry = _entries[i];
            var intervals = entry.Request.Intervals;
            var lo = 0;
            var hi = intervals.Length;
            while (lo < hi)
            {
                var mid = lo + (hi - lo) / 2;
                if (intervals[mid].Until <= seconds) lo = mid + 1;
                else hi = mid;
            }
            entry.NextUse = lo < intervals.Length ? intervals[lo].From : double.PositiveInfinity;
            entry.Pinned = entry.NextUse <= seconds;
            if (entry.Pinned)
            {
                _current.Add(i);
                _replanAt = Math.Min(_replanAt, intervals[lo].Until);
            }
            else if (double.IsFinite(entry.NextUse))
            {
                var enters = entry.NextUse - lookahead;
                // Avoid repeatedly waking on a rounded-equal time near the boundary.
                _replanAt = Math.Min(_replanAt, enters > seconds ? enters : entry.NextUse);
                var denseHorizon = entry.NextUse - 2;
                if (!_pressured && _verificationLookahead is null && denseHorizon > seconds)
                    _replanAt = Math.Min(_replanAt, denseHorizon);
            }
            if (!entry.Pinned) _victims.Add(entry);
            if (entry.NextUse - seconds <= 2 && entry.Texture is null && !entry.Pinned)
            {
                nearBytes += entry.Request.Key.TextureBytes;
                ++nearCount;
            }
            if (entry.NextUse - lookahead > seconds) continue;
            if (!warming && entry.Request.Key.TextureBytes > RealtimeUploadBytes && entry.Texture is null && entry.Pinned)
                ++_deferredLarge;
            _candidates.Add(i);
        }
        _candidates.Sort((left, right) => ComparePriority(_entries[left], _entries[right]));
        _victims.Sort(static (left, right) =>
        {
            var next = right.NextUse.CompareTo(left.NextUse);
            return next != 0 ? next : right.Request.Key.TextureBytes.CompareTo(left.Request.Key.TextureBytes);
        });
        if (!_pressured && _verificationLookahead is null && (nearBytes > 32 * MiB || nearCount >= 48))
        {
            _burstUntil = seconds + 2;
            if (lookahead == 1)
            {
                _lookahead = 2;
                Replan(seconds, 2, warming);
            }
        }
    }

    private static int ComparePriority(Entry a, Entry b)
    {
        if (a.Pinned != b.Pinned) return a.Pinned ? -1 : 1;
        var deadline = a.NextUse.CompareTo(b.NextUse);
        if (deadline != 0) return deadline;
        // Small current assets become available first on a cold, over-capacity boundary.
        return a.Request.Key.TextureBytes.CompareTo(b.Request.Key.TextureBytes);
    }

    private bool EvictFor(Entry wanted, long bytes, int slots)
    {
        var key = wanted.Request.Key;
        if (slots < StoryboardResidencyLedger.MaxSlots && bytes + key.TextureBytes + key.ScratchBytes <= _budget)
            return true; // Capacity exists as soon as the previously retired GPU allocations are acknowledged.
        _retireBatch.Clear();
        var released = 0L;
        foreach (var entry in _victims)
        {
            if (!wanted.Pinned && entry.NextUse <= wanted.NextUse) break;
            if (entry.Texture is null) continue;
            released += entry.Request.Key.TextureBytes;
            _retireBatch.Add(entry);
            if (slots - _retireBatch.Count < StoryboardResidencyLedger.MaxSlots && bytes - released + key.TextureBytes + key.ScratchBytes <= _budget)
            {
                Retire(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(_retireBatch));
                return true;
            }
        }
        return false;
    }

    private void RetireWhere(Func<Entry, bool> canRetire) =>
        Retire(_entries.Where(entry => entry.Texture is not null && canRetire(entry)).ToArray());

    private void Retire(ReadOnlySpan<Entry> entries)
    {
        if (entries.IsEmpty) return;
        _clearCanvasCommands();
        long bytes = 0;
        foreach (var entry in entries)
        {
            // Every retained command was cleared before dropping the owning texture reference.
            entry.Texture!.Dispose();
            entry.Texture = null;
            bytes += entry.Request.Key.TextureBytes;
        }
        StoryboardResidencyLedger.Retire(bytes, entries.Length);
    }

    internal void Report(string reason)
    {
        _decodeMs.Sort();
        _uploadMs.Sort();
        var total = StoryboardResidencyLedger.Snapshot();
        GD.Print(FormattableString.Invariant($"STORYBOARD_RESIDENCY reason={reason} tier_mib={_budget / MiB} declarations_used={_entries.Length} lookahead_s={_lookahead:F2} peak_bytes={_maxBytes} peak_slots={_maxSlots} peak_retiring_slots={_maxRetiring} now_bytes={total.Bytes} retiring_bytes={total.RetiringBytes} uploads={_uploadMs.Count} reloads={_reloads} missing_frames={_missingFrames} deferred_large={_deferredLarge} decode_p95_ms={Percentile(_decodeMs, .95):F3} decode_p99_ms={Percentile(_decodeMs, .99):F3} upload_p95_ms={Percentile(_uploadMs, .95):F3} upload_p99_ms={Percentile(_uploadMs, .99):F3}"));
    }

    private static void Record(List<double> list, double value)
    {
        if (list.Count < 8192) list.Add(value);
    }

    private static double Percentile(List<double> sorted, double fraction) =>
        sorted.Count == 0 ? 0 : sorted[(int)Math.Ceiling(sorted.Count * fraction) - 1];

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        InvalidateRequests();
        RetireWhere(static _ => true);
        Report("stop");
    }

    private sealed class Entry(StoryboardTextureRequest request)
    {
        internal readonly StoryboardTextureRequest Request = request;
        internal Texture2D? Texture;
        internal double NextUse;
        internal bool Pinned, LoadedBefore;
    }

    private sealed class DecodeJob(int index, int generation, StoryboardTextureKey key)
    {
        private readonly object _gate = new();
        private bool _ready, _taken, _cancelled;
        private Image? _image;
        private string? _failure;
        internal int Index { get; } = index;
        internal int Generation { get; } = generation;
        internal double DecodeMilliseconds { get; private set; }

        internal void Start() => _ = Task.Run(Run);

        private void Run()
        {
            Image? result = null;
            var start = Stopwatch.GetTimestamp();
            string? failure = null;
            try
            {
                StoryboardLocalFiles.CheckDirectoryChain(Path.GetDirectoryName(key.Source.Path)!);
                var bytes = StoryboardLocalFiles.Read(key.Source.Path, key.Source.Length);
                if (bytes.Length != key.Source.Length || GameplayStoryboardLoader.ReadPngSize(bytes, key.Source.Path) != key.Source.Size)
                    throw new InvalidDataException($"Storyboard asset changed after preflight: '{key.Source.Path}'");
                result = new Image();
                if (result.LoadPngFromBuffer(bytes.AsSpan()) != Error.Ok || result.IsEmpty() ||
                    result.GetWidth() != key.Source.Size.Width || result.GetHeight() != key.Source.Size.Height)
                    throw new InvalidDataException($"Storyboard asset could not be decoded: '{key.Source.Path}'");
                if (result.GetFormat() != Image.Format.Rgba8) result.Convert(Image.Format.Rgba8);
                if (key.Crop is { } crop)
                {
                    var region = result.GetRegion(new Rect2I(crop.X, crop.Y, crop.Width, crop.Height));
                    result.Dispose();
                    result = region;
                }
            }
            catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException or ArgumentException or OverflowException)
            {
                result?.Dispose();
                result = null;
                failure = ex.Message;
            }
            lock (_gate)
            {
                DecodeMilliseconds = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
                if (_cancelled)
                {
                    result?.Dispose();
                    StoryboardResidencyLedger.CancelJob(key.TextureBytes, key.ScratchBytes);
                }
                else
                {
                    _image = result;
                    _failure = failure;
                    _ready = true;
                }
            }
        }

        internal bool TryTake(out Image? image, out string? failure)
        {
            lock (_gate)
            {
                image = null;
                failure = null;
                if (!_ready || _cancelled || _taken) return false;
                image = _image;
                failure = _failure;
                _image = null;
                _taken = true;
                return true;
            }
        }

        internal void Cancel()
        {
            lock (_gate)
            {
                if (_taken || _cancelled) return;
                _cancelled = true;
                if (!_ready) return; // The worker owns its allocation until it sees cancellation.
                _image?.Dispose();
                _image = null;
                StoryboardResidencyLedger.CancelJob(key.TextureBytes, key.ScratchBytes);
            }
        }
    }
}
