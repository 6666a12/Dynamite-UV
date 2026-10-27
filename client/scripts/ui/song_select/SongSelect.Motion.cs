using Godot;
using DynamiteUniverse.Game;
using DynamiteUniverse.Shared.IO;
using DynamiteUniverse.Shared.Judge;
using DynamiteUniverse.Shared.Score;

namespace DynamiteUniverse.Ui;

public partial class SongSelect
{

    // ---- 展开卡动效 ----
    // 高度 / 文字 / 封面三段的动画状态、逐帧插值与控件应用都在 SongDetailCard
    //（动效状态本身在 Godot-free 的 SongCardMotion，单测见 tools/core-tests/SongCardMotionTests.cs）；
    // 页面只保留 _Process 的门闩与"收拢落点恢复普通行"的联动。
    private bool _cardAnimating => _detailCard.Animating;

    /// <summary>展开卡的逐帧推进：由 <see cref="_cardAnimating"/> 作门闩，没有高度/文字/封面动画段时
    /// 整段早退，_Process 里只剩滚动与预览读数。收拢落点回调在组件应用其余差量前恢复普通行。</summary>
    private void StepCardAnimations(double delta) =>
        _detailCard.Advance(delta, _detailRequest.CurrentGeneration, ApplySearchFilter);

    // ---- 进场动画 ----

    private void PrepareEnterAnimation()
    {
        var profile = UiMotionProfile.For(GameSession.Settings.MotionMode);
        if (!profile.IsAnimated)
            return;
        _topRoot.Modulate = new Color(1f, 1f, 1f, 0f);
        _settingsRoot.Modulate = new Color(1f, 1f, 1f, 0f);
        _listRoot.Modulate = new Color(1f, 1f, 1f, 0f);
        _bottomRoot.Modulate = new Color(1f, 1f, 1f, 0f);
        if (!profile.AllowDirectionalMotion)
            return;
        _settingsRoot.Position = new Vector2(-profile.ContentShift, 0f);
        _listRoot.Position = new Vector2(profile.ContentShift, 0f);
        _bottomRoot.Position = new Vector2(0f, profile.ListShift);
    }

    private void PlayEnterAnimation()
    {
        var profile = UiMotionProfile.For(GameSession.Settings.MotionMode);
        if (!profile.IsAnimated)
        {
            ResetEnterState();
            return;
        }

        _localTransitionBusy = true;
        var tween = CreateTween().SetParallel(true);
        TweenEnter(tween, _topRoot, 0.0);
        TweenEnter(tween, _settingsRoot, profile.AllowStagger ? profile.Stagger : 0.0);
        TweenEnter(tween, _listRoot, profile.AllowStagger ? profile.Stagger * 2.0 : 0.0);
        TweenEnter(tween, _bottomRoot, profile.AllowStagger ? profile.Stagger * 3.0 : 0.0);
        tween.Finished += () =>
        {
            ResetEnterState();
            _localTransitionBusy = false;
        };

        void TweenEnter(Tween sequence, CanvasItem item, double delay)
        {
            sequence.TweenProperty(item, "modulate", Colors.White, profile.PanelDuration)
                .SetDelay(delay).SetTrans(Tween.TransitionType.Expo)
                .SetEase(Tween.EaseType.Out);
            if (item is Node2D node)
                sequence.TweenProperty(node, "position", Vector2.Zero, profile.PanelDuration)
                    .SetDelay(delay).SetTrans(Tween.TransitionType.Expo)
                    .SetEase(Tween.EaseType.Out);
        }
    }

    private void ResetEnterState()
    {
        foreach (var root in new[] { _topRoot, _settingsRoot, _listRoot, _bottomRoot })
        {
            root.Position = Vector2.Zero;
            root.Modulate = Colors.White;
        }
    }
}
