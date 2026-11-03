using Godot;
using DynamiteUniverse.Game;

namespace DynamiteUniverse.Ui;

/// <summary>hover 统一 0.10s 过渡的共享轻量值：单元素一份 0..1 过渡值，
/// 绘制处对现有配色做 lerp，hover 终态视觉不变。
/// 用法约定：hover 态变更处调 <c>SetProcess(true)</c> 并请求一帧重绘，_Process 里
/// <c>if (fade.Update(hovered, delta)) QueueRedraw(); if (fade.Settled(hovered)) SetProcess(false);</c>——
/// 「值变了才重绘」与「到终值才停表」是两个独立条件：合并成一个会要么漏画落终值帧，
/// 要么条件写反退化成空闲无限重绘。</summary>
public static class UiHoverMotion
{
    /// <summary>0.08–0.12s 档位的中值。</summary>
    public const float Seconds = 0.10f;
}

/// <summary>单元素 hover 过渡值（0..1）：可变结构体，持有方用非 readonly 字段承接
/// （readonly 字段上调结构体方法会触发防御性拷贝，推进值会丢）。</summary>
public struct UiHoverFade
{
    private float _value;

    /// <summary>当前过渡值：0 = 未 hover 绘制色，1 = hover 绘制色。</summary>
    public float Value => _value;

    /// <summary>MOTION OFF 时跳过过渡直接落终值：在 hover 态变更的当帧调用，
    /// 保证瞬时模式下改态当帧就画出终态，不等下一帧 _Process。</summary>
    public void Snap(bool hovered) => _value = hovered ? 1f : 0f;

    /// <summary>按本帧的 hover 态推进；返回 true 表示本帧值有变化（含落终值这一帧），
    /// 调用方据此重绘——落终值帧也必须重绘，否则最后一次绘制停在距终值一步之遥。
    /// MOTION OFF 直接落终值。</summary>
    public bool Update(bool hovered, double delta)
    {
        var target = hovered ? 1f : 0f;
        var next = UiMotionProfile.For(GameSession.Settings.MotionMode).IsAnimated
            ? Mathf.MoveToward(_value, target, (float)delta / UiHoverMotion.Seconds)
            : target;
        var changed = next != _value;
        _value = next;
        return changed;
    }

    /// <summary>是否已到终值：调用方据此停 _Process（与「是否重绘」是两个条件，分开判定）。</summary>
    public readonly bool Settled(bool hovered) => _value == (hovered ? 1f : 0f);
}
