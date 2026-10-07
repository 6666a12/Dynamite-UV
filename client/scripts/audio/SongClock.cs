using Godot;

namespace DynamiteUniverse.Audio;

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

    private bool _manualFallback;
    private double _fallbackBaseSec;
    private ulong _fallbackStartMsec;

    private bool _fixedFrameEnabled;
    private double _fixedFrameStepSec;
    private double _fixedFramePosition;

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
        _fixedFrameStepSec = 1.0 / framesPerSecond;
        _manualFallback = true;
    }

    public void AdvanceFixedFrame()
    {
        if (_fixedFrameEnabled && _playing)
            _fixedFramePosition += _fixedFrameStepSec;
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
        _playing = true;
        if (_fixedFrameEnabled)
        {
            _fixedFramePosition = fromSec;
            return;
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

    public void Resume() => Play(_pausedPosition);

    public void Seek(double sec)
    {
        if (_playing)
            Play(sec);
        else
            _pausedPosition = sec;
    }

    /// <summary>当前歌曲时间（秒，含用户校准）。未播放时返回暂停位置（同样含校准）。</summary>
    public double GetSongTime()
    {
        if (!_playing)
            return _pausedPosition + UserOffsetMs / 1000.0;

        double pos;
        if (_fixedFrameEnabled)
        {
            pos = _fixedFramePosition;
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
        return pos + UserOffsetMs / 1000.0;
    }
}
