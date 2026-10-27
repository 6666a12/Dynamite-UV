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

    internal bool Playing { get; private set; }
    internal double Elapsed => _player.GetPlaybackPosition() - _start;

    internal SongPreviewPlayer(Node parent, Func<string, AudioStream?> loadAudio)
    {
        _loadAudio = loadAudio;
        _player = new AudioStreamPlayer { Name = "PreviewPlayer", Bus = "Music" };
        parent.AddChild(_player);
    }

    // The page stops the old preview before looking up the new chart, including failed lookups.
    internal void Load(SongPreviewKey key, ChartPreview? preview)
    {
        _start = Math.Max(0.0, preview?.StartSec ?? 0.0);
        var duration = preview?.DurationSec ?? FallbackDuration;
        _duration = duration > 0.0 ? duration : FallbackDuration;
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
        _stream = stream;
        _player.Stream = stream;
        _player.Play((float)_start);
        Playing = true;
        return new(true, result.Key, "");
    }

    internal void Stop()
    {
        _loader.Cancel();
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
        _player.Stop();
        return true;
    }

    public void Dispose() => _loader.Dispose();
}
