using Godot;
using DynamiteUniverse.Audio;

namespace DynamiteUniverse.Ui;

/// <summary>Owns the synthetic loop, zero-offset clock and loop-position unwrapping.</summary>
internal sealed class CalibrationBeatSource
{
    internal const double BarSeconds = 2.0;
    internal void Pause()
    {
        if (GodotObject.IsInstanceValid(_clock)) _clock.Pause();
    }
    /// <summary>小节的 4 个 click 位置（毫秒）。</summary>
    private static readonly double[] ClickOffsetsMs = [0.0, 500.0, 1000.0, 1500.0];

    private const float BeatClickFrequencyHz = 1000f;
    private const float BarClickFrequencyHz = 2000f;
    private const float BeatClickDecaySeconds = 0.040f;
    private const float BeatClickGain = 0.55f;
    private const float BarClickGain = 0.75f;

    private AudioStreamPlayer _beatPlayer = null!;
    private SongClock _clock = null!;
    private bool _audioReady;
    /// <summary>2s 循环流的回卷展开：GetPlaybackPosition 每圈归零，时间轴必须单调，见 <see cref="LoopPositionUnwrapper"/>。</summary>
    private readonly LoopPositionUnwrapper _loopUnwrap = new(BarSeconds);

    // ---- 时钟：与 SongClock 同一套修正链，只是不叠加 UserOffsetMs ----

    /// <summary>
    /// 判定用 song position（秒）。窗口播放时取
    /// <c>player.GetPlaybackPosition() + AudioServer.GetTimeSinceLastMix() − AudioServer.GetOutputLatency()</c>，
    /// 与 <see cref="SongClock.GetSongTime"/> 的窗口分支逐项相同——校准页测的就是「除 userOffset 以外
    /// 的全部修正是否被玩家充分抵消」，因此这里**不能**叠加任何用户偏移。headless（无音频设备）时
    /// 与 SongClock 一样回退到系统计时器，保证逻辑可运行。
    /// </summary>
    internal double SongPositionSeconds()
    {
        // headless（Dummy 音频驱动）时 SongClock 自己也在用系统计时器回退，直接取它，
        // 两边不会各自漂移；UserOffsetMs 恒为 0，所以这个值就是"不含用户偏移的 song position"。
        if (_clock.ManualFallback)
            return _clock.GetSongTime();
        // 有音频设备但音源没建起来（理论上只在资源缺失时发生）：退回系统计时器，保证页面仍可玩。
        if (!_audioReady)
            return (Time.GetTicksMsec() - _fallbackStartMsec) / 1000.0;
        // 音源是 2s 循环流：GetPlaybackPosition 每圈回卷到 0，直接当 song position 会让
        // 小节序号永远不前进（热身永不结束、采样计数恒为 0），必须先展开成单调时间轴。
        return _loopUnwrap.Advance(_beatPlayer.GetPlaybackPosition()
               + AudioServer.GetTimeSinceLastMix()
               - AudioServer.GetOutputLatency());
    }

    // ---- 合成节拍音源 ----

    private ulong _fallbackStartMsec;

    /// <summary>
    /// 程序合成 2 秒单声道循环（44100Hz / 16bit）：一小节 4 个 click（0/500/1000/1500ms），
    /// 小节头的 2000Hz 更重。2000ms × 44100 = 88200 采样点整，循环点无接缝。
    /// </summary>
    internal void Start(Node parent)
    {
        _beatPlayer = new AudioStreamPlayer
        {
            Name = "CalibrationBeat",
            Bus = "Music",
            Stream = BuildBeatStream(),
        };
        parent.AddChild(_beatPlayer);

        _clock = new SongClock { Name = "CalibrationClock" };
        parent.AddChild(_clock);
        _clock.Attach(_beatPlayer);
        // 校准必须测出玩家侧的偏差，时钟本身不能再叠加任何用户偏移。
        _clock.UserOffsetMs = 0.0;

        _clock.Play(0.0);
        _audioReady = _beatPlayer.Stream is not null && !_clock.ManualFallback;
        _fallbackStartMsec = Time.GetTicksMsec();

        if (_clock.ManualFallback)
            GD.PushWarning("CalibrationScreen: 无音频设备，节拍与计时改用系统计时器回退时钟。");
    }

    private static AudioStreamWav BuildBeatStream()
    {
        const int mixRate = 44100;
        var barFrames = (int)Math.Round(BarSeconds * mixRate); // 88200
        // 数据末尾多垫 1 个静音帧：引擎混音在循环边界的分块会读到 loop_end 这一帧
        // （aux = (limit − offset) + 1 含端点），loop_end == 帧数时越界读 2 字节——
        // 桌面靠分配器冗余掩盖，Android 上越过页边界直接 SIGSEGV（真机已复现，
        // 崩溃时机 = 首个循环回卷 = 第一颗 note 过线）。
        var frameCount = barFrames + 1;
        var data = new byte[frameCount * 2];

        foreach (var offsetMs in ClickOffsetsMs)
        {
            var start = (int)Math.Round(offsetMs / 1000.0 * mixRate);
            var isBarHead = offsetMs == 0.0;
            var frequency = isBarHead ? BarClickFrequencyHz : BeatClickFrequencyHz;
            var gain = isBarHead ? BarClickGain : BeatClickGain;
            var decaySamples = ClickDecaySamples(mixRate);
            for (var i = 0; i < decaySamples && start + i < frameCount; i++)
            {
                var t = (double)i / mixRate;
                var envelope = Math.Exp(-t / BeatClickDecaySeconds);
                var value = Math.Sin(2.0 * Math.PI * frequency * t) * envelope * gain;
                var sample = (short)Math.Clamp(Math.Round(value * short.MaxValue), short.MinValue, short.MaxValue);
                var byteIndex = (start + i) * 2;
                data[byteIndex] = (byte)(sample & 0xFF);
                data[byteIndex + 1] = (byte)((sample >> 8) & 0xFF);
            }
        }

        return new AudioStreamWav
        {
            Format = AudioStreamWav.FormatEnum.Format16Bits,
            MixRate = mixRate,
            Stereo = false,
            Data = data,
            LoopMode = AudioStreamWav.LoopModeEnum.Forward,
            LoopBegin = 0,
            // 循环内容 = [0, barFrames)，垫帧不属于循环；周期仍是整 88200 采样点。
            LoopEnd = barFrames,
        };
    }

    /// <summary>指数衰减到 −60dB 所需的采样数（再长就听不见了，不必写满整段）。</summary>
    private static int ClickDecaySamples(int mixRate) =>
        (int)Math.Ceiling(BeatClickDecaySeconds * 6.0 * mixRate);
}
