namespace DynamiteUniverse.Game;

/// <summary>
/// 正常结束条件：谱末 +2 秒与音频自然结束分别单调锁存，两者都成立才算结束（取晚，不取早）。
///
/// 音频流自然播完后 Godot 的播放位置会归零，SongClock 的单向过滤会把它冻结在最后一次
/// 读到的位置，因此<b>不能</b>用瞬时条件 <c>songTime &gt;= chartEnd &amp;&amp; audioEnded</c>
/// 判断——谱末条件可能永远不再成立。这里跟踪两条时间：
/// <list type="bullet">
/// <item>谱面时间：采样到的最大 songTime；</item>
/// <item>音频结束后的真实时间：从音频结束那一刻的谱面时间起，累加每帧 delta。</item>
/// </list>
/// 有效时间取两者较大值，谱末锁存只看有效时间，所以常规歌曲仍会真正等到 EndTime + 2。
///
/// 固定帧/无音频设备的回退时钟不产生自然结束信号（<c>audioEndObservable=false</c>），
/// 此时只等谱末锁存：这是自动回归可走的路径，不会死锁。
/// </summary>
public sealed class GameplayEndCondition
{
    /// <summary>单帧计入的最大真实时间，避免一次长停顿把等待快进掉。</summary>
    public const double MaxStepSeconds = 0.5;

    private readonly double _chartEndTime;
    private readonly bool _audioEndObservable;
    private double _observedSeconds;
    private double _postAudioAnchor;
    private double _postAudioSeconds;

    public GameplayEndCondition(double chartEndTime, bool audioEndObservable)
    {
        if (!double.IsFinite(chartEndTime))
            throw new ArgumentOutOfRangeException(nameof(chartEndTime));
        _chartEndTime = chartEndTime;
        _audioEndObservable = audioEndObservable;
    }

    /// <summary>谱末 +2 秒已到达（单调，不回退）。</summary>
    public bool ChartEndReached { get; private set; }

    /// <summary>音频已自然结束（单调；回退时钟下永远为 false）。</summary>
    public bool AudioEnded { get; private set; }

    /// <summary>两个条件都满足。回退时钟只要求谱末锁存。</summary>
    public bool ShouldFinish => ChartEndReached && (!_audioEndObservable || AudioEnded);

    /// <summary>当前用于谱末判断的有效时间，只用于日志/诊断。</summary>
    public double EffectiveSeconds => AudioEnded
        ? Math.Max(_observedSeconds, _postAudioAnchor + _postAudioSeconds)
        : _observedSeconds;

    public void Observe(double songTime, bool audioEnded, double delta)
    {
        if (ShouldFinish)
            return;
        if (double.IsFinite(songTime))
            _observedSeconds = Math.Max(_observedSeconds, songTime);
        if (_audioEndObservable && audioEnded && !AudioEnded)
        {
            AudioEnded = true;
            _postAudioAnchor = _observedSeconds;
            _postAudioSeconds = 0;
        }
        if (AudioEnded && double.IsFinite(delta) && delta > 0)
            _postAudioSeconds += Math.Min(delta, MaxStepSeconds);
        if (EffectiveSeconds >= _chartEndTime)
            ChartEndReached = true;
    }
}
