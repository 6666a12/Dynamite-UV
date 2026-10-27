using Godot;
using System;
using System.Globalization;
using DynamiteUniverse.ChartEditor.Core;
using DynamiteUniverse.Shared.Chart.V2;
using DynamiteUniverse.Ui;

namespace DynamiteUniverse.Editor;

/// <summary>选中状态的叠加层与右键菜单命令入口：浮动 chip（设计稿 Editor — Main 的
/// MAIN_SELECTION）、单选快照，以及菜单步进/曲线/judge/删节点的真实命令入口。
/// 画布仍是唯一的鼠标模型，shell 只按这里的快照把菜单画出来。</summary>
public partial class AuthoringCanvas
{
    // ---- 设计稿 MAIN_SELECTION 的 chip 几何 ----
    private const float ChipWidth = 252f, ChipHeight = 58f, ChipCut = 10f;
    private const float ChipTextInset = 16f;
    private const float ChipIconSize = 18f, ChipIconCopyX = 170f, ChipIconTrashX = 206f, ChipIconInsetY = 20f;
    /// <summary>chip 与选中 Note 的间距（设计稿 852 − 836 = 16）。</summary>
    private const float ChipGap = 16f;
    /// <summary>现值属性：UI SCALE 换档后下一次 <c>_Draw</c> 即取新值（见 <see cref="EditorFonts"/>）。</summary>
    private static int ChipTextSize => EditorFonts.Pt(14);
    private static readonly Font ChipFont = GD.Load<Font>("res://assets/fonts/SpaceGrotesk-Bold.woff2");
    private bool _chipDeleteHover;

    /// <summary>
    /// 单选对象的快照：浮动 chip 与右键菜单都只按这一份数据绘制。
    /// <see cref="NoteCenter"/>/<see cref="NoteWidth"/> 是 chart 坐标下的对象中心与宽度；
    /// 选中路径节点时 <see cref="NodeId"/> 非空，步进器按节点值工作（设计稿 Node Menu）。
    /// </summary>
    public sealed record SelectedTarget(
        string NoteId, AuthoringNoteKind Kind, int Side,
        ExactBarTime NoteTime, double NoteCenter, double NoteWidth,
        string? NodeId, int NodeIndex, int NodeCount,
        ExactBarTime NodeTime, double NodeCenter, double NodeWidth,
        bool Judge, V2PathCurve CurveToNext)
    {
        public bool IsNode => NodeId is not null;
        /// <summary>尾节点没有出向曲线段，Hold 尾的 judge 也锁定为 ON。</summary>
        public bool IsTail => NodeId is not null && NodeIndex == NodeCount - 1;
    }

    /// <summary>单选快照；空选、多选与 BPM 事件选中时返回 null。</summary>
    public SelectedTarget? SingleSelection
    {
        get
        {
            if (_selection.NoteIds.Count != 1) return null;
            var index = FirstSelectedNote();
            if (index < 0 || index >= Notes.Count) return null;
            var note = Notes[index];
            var nodeIndex = _selection.SelectedNodeId is null
                ? -1
                : note.Nodes.FindIndex(node => node.Id == _selection.SelectedNodeId);
            var node = nodeIndex >= 0 ? note.Nodes[nodeIndex] : null;
            return new SelectedTarget(
                note.Id, note.Kind, note.Side,
                note.TimeExact, note.Center + note.Width * .5d, note.Width,
                node?.Id, nodeIndex, note.Nodes.Count,
                node?.TimeExact ?? note.TimeExact,
                (node?.Center ?? note.Center) + (node?.Width ?? note.Width) * .5d,
                node?.Width ?? note.Width,
                node?.Judge == true,
                (node?.CurveToNext ?? note.CurveToNext) ?? V2PathCurve.Linear);
        }
    }

    /// <summary>设计稿与游戏侧的 Note 类型名（菜单上下文行与工具格的同一份文案）。</summary>
    public static string KindLabel(AuthoringNoteKind kind) => kind switch
    {
        AuthoringNoteKind.Chain => "DRAG",
        AuthoringNoteKind.Hold => "HOLD",
        AuthoringNoteKind.Mixer => "MIXER",
        AuthoringNoteKind.ExTap => "EX-TAP",
        AuthoringNoteKind.Mine => "MINE",
        AuthoringNoteKind.BarLine => "BARLINE",
        _ => "TAP",
    };

    /// <summary>游标所在位置的精确 BarTime 文本（设计稿 `90+16/32` 那一档读数）。</summary>
    public string CursorBarText => BarTextAt(_cursorTime);

    public string BarTextAt(double seconds) => ExactBarTimeText.Format(
        EditorTime.SecondsToBar(double.IsFinite(seconds) ? Math.Max(0d, seconds) : 0d, CurrentBpms));

    // ---------------------------------------------------------------- menu commands

    /// <summary>步进选中对象（或选中路径节点）的 TIME/CENTER/WIDTH。整条命令走文档命令层，
    /// 因此一步 = 一个可撤销的移动/宽度调整。</summary>
    public void StepSelection(EditorStepProperty property, int direction)
    {
        var target = SingleSelection;
        if (_controller is null || target is null)
        {
            EmitStatus("STEP NEEDS A SINGLE SELECTION");
            return;
        }
        // 细步沿用画布的空间吸附：按住 X 时 CENTER/WIDTH 走 0.01。
        var fine = !SpaceSnapOverride;
        if (!_controller.StepSelection(target.NoteId, target.NodeId, property, direction, GridDivisor, fine))
        {
            EmitStatus($"STEP REJECTED · {property.ToString().ToUpperInvariant()}");
            return;
        }
        SyncFromDocument();
        var updated = SingleSelection ?? target;
        EmitStatus(property switch
        {
            EditorStepProperty.Time => $"TIME {ExactBarTimeText.Format(updated.NodeTime)}",
            EditorStepProperty.Center => $"CENTER {updated.NodeCenter.ToString("0.00", CultureInfo.InvariantCulture)}",
            _ => $"WIDTH {updated.NodeWidth.ToString("0.00", CultureInfo.InvariantCulture)}",
        });
    }

    /// <summary>把选中节点的出向曲线设为六段选择器里的某一段。</summary>
    public void SetSelectedCurve(V2PathCurve curve)
    {
        var target = SingleSelection;
        if (_controller is null || target?.NodeId is null || target.IsTail)
        {
            EmitStatus("CURVE NEEDS A NODE WITH AN OUTGOING SEGMENT");
            return;
        }
        _controller.SetPathNodeCurve(target.NoteId, target.NodeId, curve);
        SyncFromDocument();
        EmitStatus($"CURVE TO NEXT · {CurveLabel(curve).ToUpperInvariant()}");
    }

    /// <summary>Hold 路径节点的 judge 拨杆；尾节点锁定 ON，由命令层与 v2 校验共同保证。</summary>
    public void ToggleSelectedJudge()
    {
        var target = SingleSelection;
        if (_controller is null || target?.NodeId is null || target.Kind != AuthoringNoteKind.Hold)
        {
            EmitStatus("JUDGE NEEDS A HOLD PATH NODE");
            return;
        }
        if (target.IsTail)
        {
            EmitStatus("HOLD TAIL JUDGE IS LOCKED ON");
            return;
        }
        var next = !target.Judge;
        _controller.SetHoldJudge(target.NoteId, target.NodeId, next);
        SyncFromDocument();
        EmitStatus($"NODE JUDGE {(next ? "ON" : "OFF")}");
    }

    /// <summary>删除选中的路径节点（至少保留一个节点，由命令层拒绝）。</summary>
    public void DeleteSelectedPathNode()
    {
        var target = SingleSelection;
        if (_controller is null || target?.NodeId is null)
        {
            EmitStatus("NO PATH NODE SELECTED");
            return;
        }
        if (target.NodeCount <= 1)
        {
            EmitStatus("PATH NEEDS AT LEAST ONE NODE");
            return;
        }
        var removed = target.IsTail ? "TAIL NODE" : "PATH NODE";
        _controller.DeletePathNode(target.NoteId, target.NodeId);
        _selection.SelectNote(target.NoteId);
        SyncFromDocument();
        EmitStatus($"{removed} REMOVED");
    }

    /// <summary>设计稿六段器的文案（linear/hold/ease-in/…）。</summary>
    public static string CurveLabel(V2PathCurve curve) => curve switch
    {
        V2PathCurve.Hold => "hold",
        V2PathCurve.EaseInQuad => "ease-in",
        V2PathCurve.EaseOutQuad => "ease-out",
        V2PathCurve.EaseInOutCubic => "ease-in-out",
        V2PathCurve.Smooth => "smooth",
        _ => "linear",
    };

    // ---------------------------------------------------------------- floating chip

    /// <summary>chip 的屏幕矩形与两个图标矩形；无单选、放置工具态或手势进行中返回 null。
    /// 位置取设计稿：chip 底边贴选中框上缘、左缘 = 选中框右缘 + 16；近右边缘时整体翻到 Note 左侧。
    /// 设计稿的 chip 锚在**外扩 6px 的选中框**上（sel-chip 852,706 对 sel-outline 836,764），
    /// 所以这里用与选中描边同一个外扩矩形。</summary>
    private (SelectedTarget Target, Rect2 Chip, Rect2 Copy, Rect2 Delete)? ChipLayout()
    {
        if (_tool != 4 || _draft is not null || _gesture is not null || _selection.MarqueeActive) return null;
        var target = SingleSelection;
        if (target is null) return null;
        if (FindNote(target.NoteId) is not { } note) return null;
        var rect = NoteRect(note, note.Time).Grow(SelectionOutlineGrow);
        var x = rect.End.X + ChipGap;
        if (x + ChipWidth > DesignWidth) x = rect.Position.X - ChipGap - ChipWidth;
        var chip = new Rect2(Math.Max(0f, x), Math.Max(0f, rect.Position.Y - ChipHeight), ChipWidth, ChipHeight);
        return (target, chip,
            new Rect2(chip.Position.X + ChipIconCopyX, chip.Position.Y + ChipIconInsetY, ChipIconSize, ChipIconSize),
            new Rect2(chip.Position.X + ChipIconTrashX, chip.Position.Y + ChipIconInsetY, ChipIconSize, ChipIconSize));
    }

    private void DrawSelectionChip()
    {
        if (ChipLayout() is not { } layout) return;
        var (target, chip, copy, delete) = layout;
        EditorOverlayDraw.CutPanel(this, chip, ChipCut, new Color(UiFonts.Panel, .95f),
            new Color(UiFonts.Cyan, .95f), 1.5f);
        // 读数取真实选中对象：精确 BarTime 分数式 + chart 坐标下的对象中心。
        // 两级选中后读数为被选中的那个路径节点（与右键菜单的节点态读数一致）。
        var text = $"{ExactBarTimeText.Format(target.IsNode ? target.NodeTime : target.NoteTime)} · x" +
            $"{(target.IsNode ? target.NodeCenter : target.NoteCenter).ToString("0.00", CultureInfo.InvariantCulture)}";
        EditorOverlayDraw.LeftText(this, ChipFont,
            new Vector2(chip.Position.X + ChipTextInset, chip.Position.Y + ChipHeight * .5f),
            text, ChipTextSize, UiFonts.Cyan);
        // 复制图标：命令层没有 duplicate 命令，按 55% 压暗当占位。
        DrawCopyIcon(copy, new Color(UiFonts.Dim, .55f));
        DrawTrashIcon(delete, _chipDeleteHover ? Colors.White : new Color("f87171"));
    }

    private void DrawCopyIcon(Rect2 rect, Color color)
    {
        var p = rect.Position;
        var stroke = 1.6f;
        DrawRect(new Rect2(p.X + 6f, p.Y + 6f, 10f, 10f), color, false, stroke);
        DrawPolyline(new[]
        {
            p + new Vector2(12f, 6f), p + new Vector2(12f, 3f), p + new Vector2(2f, 3f),
            p + new Vector2(2f, 13f), p + new Vector2(5f, 13f),
        }, color, stroke, true);
    }

    private void DrawTrashIcon(Rect2 rect, Color color)
    {
        var p = rect.Position;
        var stroke = 1.6f;
        DrawLine(p + new Vector2(3f, 5f), p + new Vector2(15f, 5f), color, stroke);
        DrawPolyline(new[]
        {
            p + new Vector2(7f, 5f), p + new Vector2(7f, 3f), p + new Vector2(11f, 3f), p + new Vector2(11f, 5f),
        }, color, stroke, true);
        DrawPolyline(new[]
        {
            p + new Vector2(5f, 5f), p + new Vector2(6f, 16f), p + new Vector2(12f, 16f), p + new Vector2(13f, 5f),
        }, color, stroke, true);
    }

    /// <summary>chip 上的左键：只有删除图标是真实动作；落在 chip 上的点击一律吞掉，
    /// 避免穿透成底层舞台的选择/框选。</summary>
    private bool HandleChipPress(Vector2 point, bool pressed)
    {
        if (ChipLayout() is not { } layout || !layout.Chip.HasPoint(point)) return false;
        if (pressed && layout.Delete.HasPoint(point)) DeleteSelection();
        return true;
    }

    /// <summary>把鼠标位置换算成 chip 的悬停态，返回是否需要重绘。</summary>
    private bool UpdateChipHover(Vector2 point)
    {
        var hover = ChipLayout() is { } layout && layout.Delete.HasPoint(point);
        if (hover == _chipDeleteHover) return false;
        _chipDeleteHover = hover;
        return true;
    }

    private AuthoringNote? FindNote(string id)
    {
        foreach (var candidate in Notes)
            if (candidate.Id == id) return candidate;
        return null;
    }
}
