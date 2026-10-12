using Godot;
using System.Collections.Generic;
using DynamiteUniverse.Ui;

namespace DynamiteUniverse.Editor;

/// <summary>hover 统一过渡的共享轻量 helper（docs/editor-ui-design.md §3 的「hover 统一过渡」决策）：
/// 全编辑器可点元素的 hover 从瞬时换色改为约 0.10s 的颜色 lerp，鼠标快速划过时「跟手但不闪」。
/// 按页面 hotspot 的数量与稳定性二选一持有：
/// - <see cref="HoverFade"/>：单元素一份 0..1 值，hotspot 少且固定的页面（Create/Pack/Start 卡）
///   直接持有字段，绘制处 `颜色A.Lerp(颜色B, Value)`；
/// - <see cref="HoverFadeMap"/>：按 long 键（页面自编码「热点+下标」）的一组值，hotspot 多且
///   动态的页面（Settings、右键菜单、包行列表）共用一份。
/// 两者都不发事件、不持状态之外的东西：hover 目标仍由各页面既有的 hover 状态变更处给出，
/// 页面在自己的 _Process 里推进，全部到终值即 SetProcess(false)，空闲不逐帧空转；
/// MOTION OFF（设置真源）时推进直接跳终值，不播过渡。</summary>
internal static class HoverMotion
{
    /// <summary>统一过渡时长：0.08–0.12s 档位的中值，与焦点 glow 的 0.12s 同一档，
    /// 观感上和既有动效语言一致。</summary>
    public const float Seconds = .10f;
}

/// <summary>单元素 hover 过渡值（0..1）：可变结构体，持有方用非 readonly 字段/数组元素承接
/// （readonly 字段上调结构体方法会触发防御性拷贝，推进值会丢）。</summary>
internal struct HoverFade
{
    private float _value;

    /// <summary>当前过渡值：0 = 未 hover 绘制色，1 = hover 绘制色。</summary>
    public float Value => _value;

    /// <summary>按本帧的 hover 态推进；返回 true 表示还没到终值（调用方据此保持 _Process）。
    /// MOTION OFF 直接落终值，所以关掉 _Process 后再开也总能一帧结算。</summary>
    public bool Update(bool hovered, double delta)
    {
        var target = hovered ? 1f : 0f;
        _value = EditorSettings.Current.MotionMode == UiMotionMode.Off
            ? target
            : Mathf.MoveToward(_value, target, (float)delta / HoverMotion.Seconds);
        return _value != target;
    }
}

/// <summary>一组 hover 过渡值：键由页面自行编码（热点枚举 + 下标），值只在过渡期存在
/// （落到 0 即删除），元素数量动态变化也不会无限增长。hoveredKey 传页面约定的「无 hover」
/// 哨兵（通常是 -1）时全部键向 0 回落。</summary>
internal sealed class HoverFadeMap
{
    private readonly Dictionary<long, float> _values = new();

    /// <summary>取某键的当前值：没有条目 = 0（未 hover 绘制色），绘制处无需判存在。</summary>
    public float this[long key] => _values.TryGetValue(key, out var value) ? value : 0f;

    /// <summary>清空全部过渡值：给「键空间整体失效」的场景用（右键菜单 Open 重建元素列表，
    /// 下标含义全变，残留旧值会把 hover 画到错误的元素上）。</summary>
    public void Clear() => _values.Clear();

    /// <summary>把所有键推进到 hoveredKey ? 1 : 0；返回 true 表示还没全部到终值。
    /// 新出现的 hovered 键从 0 起步（MOTION OFF 直接 1），与单元素版同语义。</summary>
    public bool Update(long hoveredKey, double delta)
    {
        var off = EditorSettings.Current.MotionMode == UiMotionMode.Off;
        var unsettled = false;
        List<long>? expired = null;
        foreach (var (key, value) in _values)
        {
            var target = key == hoveredKey ? 1f : 0f;
            var next = off
                ? target
                : Mathf.MoveToward(value, target, (float)delta / HoverMotion.Seconds);
            if (!off && next != target) unsettled = true;
            if (next <= 0f) (expired ??= new List<long>()).Add(key);
            else _values[key] = next;
        }
        if (expired is not null)
            foreach (var key in expired)
                _values.Remove(key);
        if (hoveredKey >= 0 && !_values.ContainsKey(hoveredKey))
        {
            _values[hoveredKey] = off ? 1f : Mathf.Min((float)delta / HoverMotion.Seconds, 1f);
            unsettled = true;
        }
        return unsettled;
    }
}
