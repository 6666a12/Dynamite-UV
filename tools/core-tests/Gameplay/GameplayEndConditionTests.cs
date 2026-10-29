using DynamiteUniverse.Game;

namespace CoreTests;

/// <summary>
/// 正常结束必须是"谱末 +2 秒"和"音频自然结束"两个单调锁存的合取。音频流播完后
/// SongClock 会冻结或归零，所以这里用固定帧 step 模拟两种情况，确认谱末条件仍能真正等到。
/// </summary>
public static class GameplayEndConditionTests
{
    private const double Frame = 1.0 / 60.0;

    public static void Run(Action<bool, string> check)
    {
        Console.WriteLine("[22] Normal end requires chart end +2s AND natural audio end");

        AudioFirstThenFrozenClock(check);
        AudioFirstThenClockCollapses(check);
        ChartFirstThenAudio(check);
        UntrackableAudioUsesChartPath(check);
        FrameDeltasAreBounded(check);
        ConditionIsMonotonic(check);
    }

    /// <summary>音频在谱末前结束、SongTime 冻结：仍要真实等满谱末 +2 秒。</summary>
    private static void AudioFirstThenFrozenClock(Action<bool, string> check)
    {
        var end = new GameplayEndCondition(40.0, audioEndObservable: true);
        end.Observe(30.0, true, Frame);
        check(end.AudioEnded && !end.ChartEndReached, "audio end alone does not finish the run");

        // 再等 9.9 秒真实时间：冻结在 30s，谱末 40s 不应到达。
        for (var i = 0; i < 594; i++)
            end.Observe(30.0, true, Frame);
        check(!end.ChartEndReached && !end.ShouldFinish,
            "frozen song clock still needs the full remaining wall clock");

        for (var i = 0; i < 12; i++)
            end.Observe(30.0, true, Frame);
        check(end.ChartEndReached && end.ShouldFinish,
            "frozen song clock reaches chart end +2s through frame deltas");
    }

    /// <summary>音频结束后 SongTime 归零：不能用归零后的时间判定，也不能因此取早。</summary>
    private static void AudioFirstThenClockCollapses(Action<bool, string> check)
    {
        var end = new GameplayEndCondition(40.0, audioEndObservable: true);
        end.Observe(38.0, false, Frame);
        end.Observe(0.0, true, Frame);
        check(!end.ShouldFinish, "collapsed clock does not skip the wait");
        var frames = 0;
        while (!end.ShouldFinish && frames < 600)
        {
            end.Observe(0.0, true, Frame);
            frames++;
        }
        check(end.ShouldFinish && frames >= 100 && frames <= 130,
            "collapsed clock waits the remaining 2s of real time before finishing");
    }

    /// <summary>谱末先到：必须继续等音频自然结束（长尾音/Storyboard 不被提前遮住）。</summary>
    private static void ChartFirstThenAudio(Action<bool, string> check)
    {
        var end = new GameplayEndCondition(40.0, audioEndObservable: true);
        end.Observe(41.0, false, Frame);
        check(end.ChartEndReached && !end.ShouldFinish, "chart end alone does not finish the run");
        for (var i = 0; i < 300; i++)
            end.Observe(41.0 + i * Frame, false, Frame);
        check(!end.ShouldFinish, "a playing tail keeps the result away");
        end.Observe(46.0, true, Frame);
        check(end.ShouldFinish, "natural audio end completes the run");
    }

    /// <summary>固定帧/回退时钟没有自然结束信号：只等谱末，不产生死锁。</summary>
    private static void UntrackableAudioUsesChartPath(Action<bool, string> check)
    {
        var end = new GameplayEndCondition(40.0, audioEndObservable: false);
        end.Observe(39.9, false, Frame);
        check(!end.ShouldFinish, "chart path still needs chart end +2s");
        end.Observe(40.0, false, Frame);
        check(end.ShouldFinish && !end.AudioEnded,
            "untrackable audio finishes on the deterministic chart path without a fake audio latch");
    }

    /// <summary>一次长停顿不能被当成"音频结束后的全部等待"。</summary>
    private static void FrameDeltasAreBounded(Action<bool, string> check)
    {
        var end = new GameplayEndCondition(20.0, audioEndObservable: true);
        end.Observe(10.0, true, 30.0);
        check(!end.ShouldFinish && Math.Abs(end.EffectiveSeconds - (10.0 + GameplayEndCondition.MaxStepSeconds)) < 1e-9,
            "a giant frame adds at most the bounded step");
        end.Observe(10.0, true, double.NaN);
        end.Observe(10.0, true, -5.0);
        check(!end.ShouldFinish, "invalid frame deltas are ignored");
    }

    /// <summary>锁存只前进：完成后的采样不再改变结论。</summary>
    private static void ConditionIsMonotonic(Action<bool, string> check)
    {
        var end = new GameplayEndCondition(20.0, audioEndObservable: true);
        end.Observe(22.5, false, Frame);
        check(end.ChartEndReached && !end.ShouldFinish, "chart latch is monotonic before audio end");
        end.Observe(3.0, true, Frame);
        check(end.AudioEnded && end.ShouldFinish, "audio end completes without waiting for the clock");
        end.Observe(0.0, false, Frame);
        check(end.ShouldFinish, "no later sample can unlatch the end condition");
    }
}
