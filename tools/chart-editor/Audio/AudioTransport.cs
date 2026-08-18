using NAudio.Wave;

namespace DynamiteUniverse.ChartEditor.Audio;

public enum AudioTransportState
{
    Stopped,
    Playing,
    Paused,
}

public interface IAudioTransport : IDisposable
{
    TimeSpan Position { get; }
    TimeSpan Duration { get; }
    AudioTransportState State { get; }
    void Play();
    void Pause();
    void Seek(TimeSpan position);
}

public interface IAudioTransportFactory
{
    IAudioTransport Open(string absolutePath);
}

public sealed class EditorAudioTransportFactory : IAudioTransportFactory
{
    public static EditorAudioTransportFactory Instance { get; } = new();

    public IAudioTransport Open(string absolutePath) => new NAudioTransport(absolutePath);
}

/// <summary>
/// Windows playback transport whose clock is the audio device's played-frame position. Decoder
/// stream position is intentionally not exposed as the clock because output buffering reads ahead.
/// </summary>
internal sealed class NAudioTransport : IAudioTransport
{
    private readonly WaveStream _reader;
    private readonly WaveOutEvent _output;
    private long _deviceOriginBytes;
    private double _originSeconds;
    private bool _disposed;

    public NAudioTransport(string absolutePath)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException(
                "Editor audio playback currently requires the Windows NAudio output backend.");
        _reader = EditorWaveStreamFactory.Open(absolutePath);
        try
        {
            _output = new WaveOutEvent();
            _output.Init(_reader);
        }
        catch
        {
            _reader.Dispose();
            throw;
        }
    }

    public TimeSpan Duration
    {
        get
        {
            ThrowIfDisposed();
            return _reader.TotalTime;
        }
    }

    public TimeSpan Position
    {
        get
        {
            ThrowIfDisposed();
            var elapsedBytes = Math.Max(0L, _output.GetPosition() - _deviceOriginBytes);
            var seconds = _originSeconds + elapsedBytes /
                (double)_output.OutputWaveFormat.AverageBytesPerSecond;
            return TimeSpan.FromSeconds(Math.Clamp(seconds, 0.0, Duration.TotalSeconds));
        }
    }

    public AudioTransportState State
    {
        get
        {
            ThrowIfDisposed();
            return _output.PlaybackState switch
            {
                PlaybackState.Playing => AudioTransportState.Playing,
                PlaybackState.Paused => AudioTransportState.Paused,
                _ => AudioTransportState.Stopped,
            };
        }
    }

    public void Play()
    {
        ThrowIfDisposed();
        if (_originSeconds >= Duration.TotalSeconds)
            Seek(TimeSpan.Zero);
        _output.Play();
    }

    public void Pause()
    {
        ThrowIfDisposed();
        if (_output.PlaybackState == PlaybackState.Playing)
            _output.Pause();
    }

    public void Seek(TimeSpan position)
    {
        ThrowIfDisposed();
        var wasPlaying = _output.PlaybackState == PlaybackState.Playing;
        _output.Stop();
        var seconds = Math.Clamp(position.TotalSeconds, 0.0, Duration.TotalSeconds);
        _reader.CurrentTime = TimeSpan.FromSeconds(seconds);
        _originSeconds = _reader.CurrentTime.TotalSeconds;
        _deviceOriginBytes = _output.GetPosition();
        if (wasPlaying && seconds < Duration.TotalSeconds)
            _output.Play();
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _output.Stop();
        _output.Dispose();
        _reader.Dispose();
        _disposed = true;
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);
}
