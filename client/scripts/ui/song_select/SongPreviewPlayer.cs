using Godot;
using DynamiteUniverse.Game;
using DynamiteUniverse.Shared.IO;

namespace DynamiteUniverse.Ui;

internal readonly record struct SongPreviewKey(int Generation, string PackId, string ChartId, string Path);
internal readonly record struct SongPreviewCompletion(bool Success, SongPreviewKey Key, string Error);
internal readonly record struct SongPreviewReadout(double Elapsed, double Duration, float Fraction);

/// <summary>Owns preview decoding, stream lifetime and transport; the page validates selection identity.</summary>
internal sealed class SongPreviewPlayer : IDisposable
{
    private const double FallbackDuration = 30.0;
    private readonly AudioStreamPlayer _player;
    private readonly Func<string, AudioStream?> _loadAudio;
    private readonly LatestRequestLoader<SongPreviewKey, AudioStream?> _loader = new();
    private AudioStream? _stream;
    private double _start;
    private double _duration;
    private bool _ended;

    internal bool Playing { get; private set; }
    internal double Elapsed => _ended ? _duration : _player.GetPlaybackPosition() - _start;

    internal SongPreviewPlayer(Node parent, Func<string, AudioStream?> loadAudio)
    {
        _loadAudio = loadAudio;
        _player = new AudioStreamPlayer { Name = "PreviewPlayer", Bus = "Music" };
        _player.Finished += () => { Playing = false; _ended = true; };
        parent.AddChild(_player);
    }

    // The page stops the old preview before looking up the new chart, including failed lookups.
    internal void Load(SongPreviewKey key, ChartPreview? preview)
    {
        var start = preview?.StartSec ?? 0.0;
        _start = double.IsFinite(start) ? Math.Max(0.0, start) : 0.0;
        var duration = preview?.DurationSec ?? FallbackDuration;
        _duration = double.IsFinite(duration) && duration > 0.0 ? duration : FallbackDuration;
        _ended = false;
        _ = _loader.Start(key, () => _loadAudio(key.Path));
    }

    internal SongPreviewCompletion? CompleteLoad(Func<SongPreviewKey, bool> isCurrent)
    {
        if (!_loader.TryTakeCompleted(out var result) || !isCurrent(result.Key)) return null;
        if (result.Value is not { } stream)
        {
            _stream = null;
            return new(false, result.Key, result.Error?.Message ?? "");
        }
        var length = stream.GetLength();
        if (double.IsFinite(length) && length > 0)
        {
            if (_start >= length) _start = 0;
            _duration = Math.Min(_duration, length - _start);
        }
        _stream = stream;
        _player.Stream = stream;
        _player.StreamPaused = false;
        _player.Play((float)_start);
        Playing = true;
        return new(true, result.Key, "");
    }

    internal void Stop()
    {
        _loader.Cancel();
        _ended = false;
        Playing = false;
        if (_player.Stream != null)
        {
            _player.Stop();
            _player.Stream = null;
        }
        _stream = null;
    }

    internal bool TogglePause()
    {
        if (_stream == null) return false;
        if (_ended)
        {
            _ended = false;
            _player.StreamPaused = false;
            _player.Play((float)_start);
            Playing = true;
            return true;
        }
        Playing = !Playing;
        _player.StreamPaused = !Playing;
        return true;
    }

    internal SongPreviewReadout Readout(double elapsed)
    {
        elapsed = Mathf.Clamp(elapsed, 0.0, _duration);
        return new(elapsed, _duration, _duration <= 0.0 ? 0f : (float)(elapsed / _duration));
    }

    internal bool StopAtEnd(double elapsed)
    {
        if (!(Playing && elapsed >= _duration)) return false;
        Playing = false;
        _ended = true;
        _player.Stop();
        return true;
    }

    public void Dispose() => _loader.Dispose();
}
