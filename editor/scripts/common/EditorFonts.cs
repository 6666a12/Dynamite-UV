using Godot;
using System;
using System.Collections.Generic;
using DynamiteUniverse.Ui;

namespace DynamiteUniverse.Editor;

/// <summary>编辑器正文字重与字号的集中调整点。窗口实际开 1600×900（视口 1920×1080 经
/// canvas_items 打 0.833 折），设计稿字号在物理像素上偏小，SpaceGrotesk Regular 又偏细，
/// 所以正文一律升到 Medium 字重，字号在设计稿值上加一档（≤13 +2、14–18 +1、更大不变），
/// 再乘用户可调的 UI SCALE 档（Settings 页 GENERAL / UI SCALE，90/100/110/125%）。
/// 强调字仍用 <see cref="UiFonts.TechBold"/>。档位修正是相对设计稿的固定偏差，已记入
/// docs/editor-ui-design.md；调观感只改这里，不要散回各页面。
///
/// **为什么要有注册表**：字号有两个读取面——自绘页面每次 <c>_Draw</c> 现算 <see cref="Pt"/>
/// （换档后下一次重绘就是新值），而 Control 的 theme override 设一次就固定。控件侧因此一律
/// 走 <see cref="ApplySize"/> 登记，换档时由 <see cref="ReapplySizes"/> 重放，UI SCALE 才能
/// 立即生效，不需要重启或重建页面。</summary>
public static class EditorFonts
{
    /// <summary>编辑器正文：SpaceGrotesk Medium（与 Regular/Bold 同为 Google Fonts v22 全字符版）。</summary>
    public static readonly Font Medium = GD.Load<Font>("res://assets/fonts/SpaceGrotesk-Medium.ttf");

    /// <summary>设计稿字号 → 编辑器实际字号：**先按设计稿档位修正，再乘 UI SCALE 倍率**并四舍五入。
    /// 顺序不能反——档位修正是相对设计稿的固定偏差，缩放是用户倍率；先修正再缩放才能保证
    /// 100% 档与引入 UI SCALE 之前逐值一致。四舍五入取 AwayFromZero（不用银行家舍入，避免
    /// 恰好 .5 时奇偶档一跳一跳），并保底 1px：字号 0 会让 Godot 退回默认字号。
    /// 保持纯函数（只读设置真源、不缓存），自绘页面的 <c>static readonly</c> 常量因此都改成了
    /// 现值属性，绘制时才求值。</summary>
    public static int Pt(int designSize)
    {
        var corrected = designSize <= 13 ? designSize + 2 : designSize <= 18 ? designSize + 1 : designSize;
        var scaled = Math.Round(corrected * EditorSettings.Current.UiScale, MidpointRounding.AwayFromZero);
        return Math.Max(1, (int)scaled);
    }

    // ---------------------------------------------------------------- 控件字号注册表

    /// <summary>一条登记项。控件用弱引用持着：页面销毁后重放时顺手剔除，
    /// 注册表不会把已释放的控件留在内存里。</summary>
    private sealed class SizeEntry
    {
        public SizeEntry(Control control, int designSize)
        {
            Control = new WeakReference<Control>(control);
            DesignSize = designSize;
        }

        public WeakReference<Control> Control { get; }
        public int DesignSize { get; set; }
    }

    private static readonly List<SizeEntry> Registered = new();

    /// <summary>控件字号的唯一入口：立刻写 theme override，并登记 (控件, 设计稿字号)。
    /// 传的是**设计稿字号**而不是 <see cref="Pt"/> 的结果——缩放值只在重放时现算，
    /// 否则注册表会把换档前的旧字号冻结下来。同一个控件重复登记只更新档位，不追加条目。</summary>
    public static void ApplySize(Control control, int designSize)
    {
        control.AddThemeFontSizeOverride("font_size", Pt(designSize));
        for (var index = Registered.Count - 1; index >= 0; index--)
        {
            if (!TryGet(Registered[index], out var registered))
            {
                Registered.RemoveAt(index);
                continue;
            }
            if (ReferenceEquals(registered, control))
            {
                Registered[index].DesignSize = designSize;
                return;
            }
        }
        Registered.Add(new SizeEntry(control, designSize));
    }

    /// <summary>UI SCALE 换档后重放全部登记项。自绘页面（画布、Pack/Create/Settings overlay）
    /// 的字号在 <c>_Draw</c> 里现算，由调用方 <c>QueueRedraw</c> 触发，不在这里管。</summary>
    public static void ReapplySizes()
    {
        for (var index = Registered.Count - 1; index >= 0; index--)
        {
            if (!TryGet(Registered[index], out var control))
            {
                Registered.RemoveAt(index);
                continue;
            }
            control.AddThemeFontSizeOverride("font_size", Pt(Registered[index].DesignSize));
            // 显式重绘：theme override 的变更多数情况下会带出自重绘，但不保证每一帧都排上，
            // 立即生效是这一项设置的硬要求，所以这里不依赖引擎侧的通知。
            control.QueueRedraw();
        }
    }

    /// <summary>登记项是否还指向一个活着的控件：弱引用可能已被回收，也可能节点已 <c>QueueFree</c>
    /// 或已释放。逐项校验而不靠页面主动注销——页面与控件是成批建的，让注册表自己回收才不会
    /// 出现"忘了注销就泄漏"的漏洞。</summary>
    private static bool TryGet(SizeEntry entry, out Control control)
    {
        control = null!;
        if (!entry.Control.TryGetTarget(out var target)) return false;
        if (!GodotObject.IsInstanceValid(target) || target.IsQueuedForDeletion()) return false;
        control = target;
        return true;
    }

    // 字距（FontVariation.SpacingGlyph，各页的 ls1/ls2/ls3… 档）**不随 UI SCALE 变**：
    // 它是设计稿的字形间呼吸，不是可用性参数——跟着字号一起放大会在小字上把词拆散，
    // 反倒降低可读性；而且 FontVariation 只吃整数像素，乘 0.9/1.1 后要么被四舍五入吃掉、
    // 要么在 1 与 2 之间来回抖。所以字距保持设计稿原值，只有字号随档位走。
}
