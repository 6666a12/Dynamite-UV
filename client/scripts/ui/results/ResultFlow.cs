using Godot;

namespace DynamiteUniverse.Ui;

/// <summary>
/// 一次结算的展示顺序：结果字标前奏 → 现有屏风/评级揭晓 → 结算页。
/// 成绩计算、纪录读取与写盘仍由 <c>GameplayMain.ShowResults</c> 在进入这里之前完成一次，
/// 本对象只消费同一份 <see cref="ResultScreenData"/>，不重复判定也不改成绩身份。
/// Motion Off 不创建前奏帧，直接交付稳定结算页。
/// </summary>
internal sealed class ResultFlow
{
    private readonly Func<UiMotionProfile> _motion;
    private readonly ResultOutcomePrelude _prelude;
    private readonly ResultScreen _screen;
    private readonly ResultRunSequence _sequence = new();

    internal ResultFlow(Node host, Func<UiMotionProfile> motion)
    {
        _motion = motion;
        _screen = new ResultScreen { Name = "ResultV2", Visible = false, ZIndex = 100 };
        host.AddChild(_screen);
        // 前奏必须是后加入的兄弟节点：_Input 由树中靠后的节点先收到，跳过才不会被结算页抢先处理。
        _prelude = new ResultOutcomePrelude();
        host.AddChild(_prelude);
    }

    internal void Begin(ResultScreenData data, Action covered, Action back, Action next,
        Action retry, bool canNext)
    {
        if (!_sequence.TryBegin())
            return;
        var motion = _motion();

        void PresentScreen()
        {
            if (!_sequence.TryCompletePrelude())
                return;
            _screen.Present(data, motion, () =>
            {
                // 屏风已经完全遮住游玩画面，前奏可以退场，舞台仍在结算背景之下隐藏。
                _prelude.Dismiss();
                covered();
            }, back, next, retry, canNext);
        }

        if (!ResultPreludeTimeline.RunsFor(motion.Mode))
        {
            PresentScreen();
            return;
        }
        _prelude.Present(data.Outcome, motion, PresentScreen);
    }
}
