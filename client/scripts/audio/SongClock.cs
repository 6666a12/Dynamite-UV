using Godot;
using DynamiteUniverse.Shared.Judge;

namespace DynamiteUniverse.Audio;

/// <summary>Uncalibrated audio and calibrated judgement time from one audio observation.</summary>
public readonly record struct SongClockSample(double MediaSeconds, double JudgeSeconds);

/// <summary>
/// 歌曲时钟：AudioStreamPlayer 播放位置 + 混音补偿 - 输出延迟 + 用户校准。
/// 公式参照 osu!：position = player.GetPlaybackPosition()
///   + AudioServer.GetTimeSinceLastMix() - AudioServer.GetOutputLatency() + offset
/// headless（无音频设备）时回退到系统计时器，保证逻辑可运行。
/// </summary>
public partial class SongClock : Node
{
    private AudioStreamPlayer? _player;
    private double _pausedPosition;
    private bool _playing;
    private readonly MonotonicSongPosition _position = new();

    private bool _manualFallback;
    private double _fallbackBaseSec;
    private ulong _fallbackStartMsec;

    private bool _fixedFrameEnabled;
    private int _fixedFramesPerSecond;
    private double _fixedFrameStart;
    private ulong _fixedElapsedFrames;

    private bool _movieClock;
    private double _moviePosition;
    private int _movieFramesPerSecond;
    private ulong _movieElapsedFrames;
    private ulong _movieStartedFrame;
    public bool MovieClockEnabled => _movieClock;

    // The development recorder mixes one audio block per movie frame. Use the same simulation
    // delta, without real-device latency or the wall-clock age of the last audio mix.
    public void EnableMovieClock(int framesPerSecond = 0)
    {
        _movieClock = true;
        _movieFramesPerSecond = framesPerSecond;
    }

    public void AdvanceMovieFrame(double delta)
    {
        if (!_movieClock || !_playing || Engine.GetProcessFrames() == _movieStartedFrame) return;
        if (_movieFramesPerSecond > 0) _movieElapsedFrames++;
        else _moviePosition += delta; // Keep the editor-only legacy prototype clock.
    }

    /// <summary>用户校准值（毫秒，对应规格书 §8.4 touchOffset），正数 = 判定时间后移。</summary>
    public double UserOffsetMs { get; set; }

    public bool IsPlaying => _playing;

    /// <summary>是否处于 headless 回退时钟模式（此时 _player.Playing 不可信）。</summary>
    public bool ManualFallback => _manualFallback;

    /// <summary>Deterministic verification clock, advanced explicitly once per gameplay frame.</summary>
    public bool FixedFrameEnabled => _fixedFrameEnabled;

    public void EnableFixedFrameClock(int framesPerSecond)
    {
        if (framesPerSecond <= 0)
            throw new ArgumentOutOfRangeException(nameof(framesPerSecond));
        _fixedFrameEnabled = true;
        _fixedFramesPerSecond = framesPerSecond;
        _manualFallback = true;
    }

    public void AdvanceFixedFrame()
    {
        if (_fixedFrameEnabled && _playing)
            _fixedElapsedFrames++;
    }

    public void Attach(AudioStreamPlayer player)
    {
        _player = player;
        // headless 下音频驱动是 Dummy，GetPlaybackPosition 不前进，用回退时钟
        if (!_fixedFrameEnabled)
            _manualFallback = DisplayServer.GetName() == "headless";
    }

    public void Play(double fromSec = 0.0)
    {
        // Play/Seek deliberately start a new timeline. Do not clamp the initial
        // compensated position to zero: output latency can make it negative.
        _position.Reset();
        StartPlayback(fromSec);
    }

    private void StartPlayback(double fromSec)
    {
        _playing = true;
        if (_fixedFrameEnabled)
        {
            _fixedFrameStart = fromSec;
            _fixedElapsedFrames = 0;
            return;
        }
        if (_movieClock)
        {
            if (_movieFramesPerSecond > 0)
            {
                _movieElapsedFrames = (ulong)Math.Max(0, Math.Round(fromSec * _movieFramesPerSecond,
                    MidpointRounding.AwayFromZero));
                _moviePosition = _movieElapsedFrames / (double)_movieFramesPerSecond;
            }
            else _moviePosition = fromSec;
            _movieStartedFrame = Engine.GetProcessFrames();
        }
        if (_manualFallback)
        {
            _fallbackBaseSec = fromSec;
            _fallbackStartMsec = Time.GetTicksMsec();
            return;
        }
        _player?.Play((float)Math.Max(0.0, fromSec));
    }

    public void Pause()
    {
        if (!_playing) return;
        // _pausedPosition 一律保存不含用户校准的原始位置，否则 Resume 后偏移会被重复累加。
        _pausedPosition = GetSongTime() - UserOffsetMs / 1000.0;
        _playing = false;
        if (!_manualFallback && !_fixedFrameEnabled)
            _player?.Stop(); // MVP：暂停即停流，恢复时从记录位置重新 Play
    }

    public void Resume()
    {
        // Restarting the stream subtracts output latency again; retain the pause
        // floor so both input timestamps and render reads remain ordered.
        _position.Reset(_pausedPosition);
        StartPlayback(_pausedPosition);
    }

    public void Seek(double sec)
    {
        if (_playing)
            Play(sec);
        else
        {
            _pausedPosition = sec;
            _position.Reset(sec);
        }
    }

    /// <summary>当前歌曲时间（秒，含用户校准）。未播放时返回暂停位置（同样含校准）。</summary>
    public double GetSongTime() => Sample().JudgeSeconds;

    public SongClockSample Sample()
    {
        if (!_playing)
            return new(_pausedPosition, _pausedPosition + UserOffsetMs / 1000.0);

        double pos;
        if (_fixedFrameEnabled)
        {
            pos = _fixedFrameStart + _fixedElapsedFrames / (double)_fixedFramesPerSecond;
        }
        else if (_movieClock)
        {
            pos = _movieFramesPerSecond > 0 ? _movieElapsedFrames / (double)_movieFramesPerSecond : _moviePosition;
        }
        else if (_manualFallback || _player == null)
        {
            pos = _fallbackBaseSec +
                  (Time.GetTicksMsec() - _fallbackStartMsec) / 1000.0;
        }
        else
        {
            pos = _player.GetPlaybackPosition()
                  + AudioServer.GetTimeSinceLastMix()
                  - AudioServer.GetOutputLatency();
        }
        // Playback position, last-mix age and latency are separate audio-thread
        // observations. Their sum can briefly decrease across a mix boundary.
        var media = _position.Observe(pos);
        return new(media, media + UserOffsetMs / 1000.0);
    }
}
