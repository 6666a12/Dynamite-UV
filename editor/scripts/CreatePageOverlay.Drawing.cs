using Godot;
using System;
using System.Globalization;
using DynamiteUniverse.ChartEditor.Core;
using DynamiteUniverse.Ui;

namespace DynamiteUniverse.Editor;

internal sealed partial class CreatePageOverlay
{

    // ---------------------------------------------------------------- 绘制

    public override void _Draw()
    {
        DrawTopBar();
        DrawAudioPanel();
        DrawMetadataPanel();
        if (!_snapshot.AddDifficulty) DrawTimingPanel();
        DrawChartPanel();
        DrawCoverPanel();
        DrawDraftPanel();
        DrawBottomBar();
    }

    private void DrawTopBar()
    {
        DrawRect(new Rect2(0, 0, 1920f, BarHeight), new Color(UiFonts.Bg, .92f), true);
        DrawRect(new Rect2(0, BarHeight, 1920f, BarLineHeight), UiFonts.Line, true);
        EditorOverlayDraw.LeftText(this, Bold4, new Vector2(BarWordX, BarCenterY), "DYNAMAKER·UV", EditorFonts.Pt(24), UiFonts.Cyan);
        EditorOverlayDraw.LeftText(this, Tech3, new Vector2(BarPageX, BarCenterY), "NEW PROJECT", EditorFonts.Pt(13), UiFonts.Dim);
    }

    /// <summary>AUDIO：真实音频校验态 + 55% 压暗的试听占位（本 shell 没有试听能力，所以不画波形与
    /// 进度条，也不伪造时长/采样率）+ 路径行 + BROWSE。</summary>
    private void DrawAudioPanel()
    {
        Panel(AudioPanel);
        EditorOverlayDraw.LeftText(this, Tech3, new Vector2(HeaderX, AudioPanel.Position.Y + HeaderCenterYOffset),
            "AUDIO", EditorFonts.Pt(12), UiFonts.Dim);

        var status = AudioStatusText(out var statusColor);
        EditorOverlayDraw.RightText(this, Tech1, AudioStatusRight, AudioStatusCenterY,
            FitLabel(Tech1, status, EditorFonts.Pt(12), AudioStatusMaxWidth), EditorFonts.Pt(12), statusColor);

        // 试听占位：设计稿的播放钮在这里，但编辑器没有音频播放能力 —— 只有描边、整体压到 55%。
        var disabled = new Color(UiFonts.Cyan, DisabledAlpha);
        EditorOverlayDraw.CutStroke(this, AudioPlay, 10f, disabled, 2f);
        DrawColoredPolygon(new[]
        {
            AudioPlayGlyph, AudioPlayGlyph + new Vector2(16f, 10f), AudioPlayGlyph + new Vector2(0f, 20f),
        }, disabled);

        EditorOverlayDraw.CutPanel(this, AudioPathFrame, AudioFieldCut, new Color(UiFonts.Panel, .6f),
            FieldEdge(_audioPath), 2f);

        if (_snapshot.AddDifficulty)
        {
            // 音频属于已打开的包：行不可点，按占位惯例压到 55%。
            EditorOverlayDraw.CutStroke(this, AudioBrowse, AudioFieldCut, new Color(UiFonts.Line, DisabledAlpha), 2f);
            EditorOverlayDraw.CenteredText(this, Bold2, AudioBrowse.Position + AudioBrowse.Size * .5f,
                "BROWSE", EditorFonts.Pt(13), new Color(UiFonts.Dim, DisabledAlpha));
            return;
        }
        EditorOverlayDraw.CutStroke(this, AudioBrowse, AudioFieldCut,
            UiFonts.Cyan.Lerp(UiFonts.Cyan.Lightened(HoverLift), HoverT(Hotspot.Browse)), 2f);
        EditorOverlayDraw.CenteredText(this, Bold2, AudioBrowse.Position + AudioBrowse.Size * .5f,
            "BROWSE", EditorFonts.Pt(13), UiFonts.Cyan);
    }

    /// <summary>音频状态是真实校验态：文件存在 + 扩展名受 draft factory 支持 + 落盘探测只吃 WAV。
    /// 设计稿的 `✓ 02:34 · 44.1KHZ` 需要时长/采样率探测，编辑器没有，所以一律不写。</summary>
    private string AudioStatusText(out Color color)
    {
        var path = _audioPath.Text.Trim();
        if (_snapshot.AddDifficulty)
        {
            color = path.Length == 0 ? UiFonts.Hard : UiFonts.Dim;
            return path.Length == 0 ? "NO AUDIO IN PACKAGE" : "FROM OPEN PACKAGE";
        }
        if (path.Length == 0) { color = UiFonts.Dim; return "NO AUDIO SELECTED"; }
        var name = System.IO.Path.GetFileName(path);
        if (!System.IO.File.Exists(path)) { color = UiFonts.Giga; return $"FILE NOT FOUND · {name}"; }
        var extension = System.IO.Path.GetExtension(path);
        if (!EditorProjectDraftFactory.IsSupportedAudioPath(path))
        {
            color = UiFonts.Giga;
            return $"{extension.TrimStart('.').ToUpperInvariant()} UNSUPPORTED";
        }
        if (!string.Equals(extension, ".wav", StringComparison.OrdinalIgnoreCase))
        {
            color = UiFonts.Hard;
            return $"✓ {name} · SAVE NEEDS WAV";
        }
        color = UiFonts.Casual;
        return $"✓ {name}";
    }

    private void DrawMetadataPanel()
    {
        Panel(MetaPanel);
        EditorOverlayDraw.LeftText(this, Tech3, new Vector2(HeaderX, MetaPanel.Position.Y + HeaderCenterYOffset),
            "METADATA", EditorFonts.Pt(12), UiFonts.Dim);
        DrawField(TitleFrame, "TITLE", _title);
        DrawField(ArtistFrame, "ARTIST", _artist);
        DrawField(PackIdFrame, "PACK ID", _packId);
        DrawField(PackCharterFrame, "CHARTER", _packCharter);
    }

    /// <summary>TIMING：BPM/offset 的大数字本身是 LineEdit，可直接输入；配设计稿的 32 格 tick 条
    /// （每 4 格高亮 cyan）。非法值由 <see cref="NumericField"/> 拒绝（短暂红框 + 回退），不流到 factory。</summary>
    private void DrawTimingPanel()
    {
        Panel(TimingPanel);
        EditorOverlayDraw.LeftText(this, Tech3, new Vector2(HeaderX, TimingPanel.Position.Y + HeaderCenterYOffset),
            "TIMING", EditorFonts.Pt(12), UiFonts.Dim);
        EditorOverlayDraw.LeftText(this, Tech2, new Vector2(BpmLabelX, TimingLabelCenterY), "BPM", EditorFonts.Pt(11), UiFonts.Dim);
        EditorOverlayDraw.LeftText(this, Tech2, new Vector2(OffsetLabelX, TimingLabelCenterY), "OFFSET SEC", EditorFonts.Pt(11), UiFonts.Dim);
        for (var i = 0; i < TickCount; i++)
        {
            var major = i % 4 == 0;
            var height = major ? TicksHeight : TicksHeight * .5f;
            DrawRect(new Rect2(TicksX + Mathf.Floor(i * TicksWidth / TickCount), TicksY + (TicksHeight - height),
                3f, height), major ? new Color(UiFonts.Cyan, .9f) : new Color(UiFonts.Dim, .4f), true);
        }
    }

    private void DrawChartPanel()
    {
        Panel(ChartPanelRect);
        EditorOverlayDraw.LeftText(this, Tech3, new Vector2(ChartHeaderX, ChartPanel.Position.Y + HeaderCenterYOffset),
            "FIRST CHART", EditorFonts.Pt(12), UiFonts.Dim);
        DrawField(ChartIdFrame, "CHART ID", _chartId);
        DrawField(ChartCharterFrame, "CHARTER", _chartCharter);
        for (var i = 0; i < DifficultyLabels.Length; i++) DrawChip(i);
        // DIFF KEY 框与提示随折叠过渡淡出/淡入（与 LineEdit 的 modulate 同步）；非 CUSTOM 收敛到
        // t=1 后整块不画，CUSTOM 展开态 t=0 时 alpha=1、坐标走 shift=0，与设计稿常量一致。
        var diffKeyAlpha = 1f - _collapseT;
        if (_difficulty != CustomIndex && diffKeyAlpha <= 0f) return;
        var diffKeyShift = Shift;
        DrawField(new Rect2(DiffKeyFrame.Position + new Vector2(0f, diffKeyShift), DiffKeyFrame.Size),
            "DIFF KEY", _diffKey, diffKeyAlpha);
        EditorOverlayDraw.LeftText(this, Tech1, new Vector2(DiffKeyHintX, DiffKeyHintCenterY + diffKeyShift),
            "≤ 24 CHARS · UNIQUE IN PACK", EditorFonts.Pt(11), new Color(UiFonts.Dim, diffKeyAlpha));
    }

    /// <summary>难度 chip：未选中是同色描边 + 同色文字；选中的那一枚转实心（同色填充 + ink 文字），
    /// CUSTOM 实心就是设计稿的 cyan。</summary>
    private void DrawChip(int index)
    {
        var rect = ChipRect(index);
        var color = ChipColor(index);
        var hovered = HoverT(Hotspot.Chip, index);
        if (index == _difficulty)
        {
            EditorOverlayDraw.CutPanel(this, rect, ChipCut, color.Lerp(color.Lightened(HoverLift), hovered),
                new Color(0, 0, 0, 0), 0f);
            EditorOverlayDraw.CenteredText(this, Bold1, rect.Position + rect.Size * .5f,
                DifficultyLabels[index], EditorFonts.Pt(13), UiFonts.InkText);
            return;
        }
        EditorOverlayDraw.CutStroke(this, rect, ChipCut, color.Lerp(color.Lightened(HoverLift), hovered), 2f);
        EditorOverlayDraw.CenteredText(this, Bold1, rect.Position + rect.Size * .5f,
            DifficultyLabels[index], EditorFonts.Pt(13), color);
    }

    /// <summary>COVER：可选封面。未选时是设计稿的虚线拖放区（点击 = 选文件），选中后显示真实校验态
    /// 与清空 ✕；AddDifficulty 下封面属于已打开的包，只读且压暗。</summary>
    private void DrawCoverPanel()
    {
        var origin = CoverPanelPos;
        var shift = Shift;
        Panel(new Rect2(origin, CoverPanel.Size));
        EditorOverlayDraw.LeftText(this, Tech3, new Vector2(ChartHeaderX, origin.Y + HeaderCenterYOffset),
            "COVER · OPTIONAL", EditorFonts.Pt(12), UiFonts.Dim);

        var readOnly = _snapshot.AddDifficulty;
        // 设计稿的拖放区是 line 色虚线；hover 提到 cyan 是 Pack 页虚线行同一套语言。
        var edge = readOnly
            ? new Color(UiFonts.Line, DisabledAlpha)
            : UiFonts.Line.Lerp(UiFonts.Cyan, HoverT(Hotspot.Cover));
        DashedCut(CoverZoneRect, CoverZoneCut, edge, 2f);
        DrawCoverIcon(new Vector2(CoverIconX, CoverIconY + shift),
            readOnly ? new Color(UiFonts.Dim, DisabledAlpha) : UiFonts.Dim);

        var (text, color) = CoverText();
        EditorOverlayDraw.LeftText(this, Tech2, new Vector2(CoverTextX, CoverTextCenterY + shift),
            FitLabel(Tech2, text, EditorFonts.Pt(13), CoverTextMaxWidth), EditorFonts.Pt(13),
            readOnly ? new Color(color, color.A * DisabledAlpha) : color);
        if (readOnly || _coverPath is null) return;

        var clear = HoverT(Hotspot.CoverClear);
        var tint = UiFonts.Dim.Lerp(UiFonts.Pink, clear);
        EditorOverlayDraw.CutStroke(this, CoverClearRect, 4f, tint, 2f);
        var center = CoverClearRect.Position + CoverClearRect.Size * .5f;
        DrawLine(center + new Vector2(-4f, -4f), center + new Vector2(4f, 4f), tint, 2f, true);
        DrawLine(center + new Vector2(4f, -4f), center + new Vector2(-4f, 4f), tint, 2f, true);
    }

    /// <summary>封面行是真实态：未选 = 设计稿文案；选中后按扩展名与存在性给可读状态。</summary>
    private (string Text, Color Color) CoverText()
    {
        if (_snapshot.AddDifficulty)
            return (_snapshot.CoverName.Length == 0 ? "NO COVER IN PACKAGE" : _snapshot.CoverName, UiFonts.Dim);
        if (_coverPath is null) return ("DROP IMAGE · PNG / JPG", UiFonts.Dim);
        var name = System.IO.Path.GetFileName(_coverPath);
        if (!System.IO.File.Exists(_coverPath)) return ($"✓ {name} · FILE NOT FOUND", UiFonts.Giga);
        var extension = System.IO.Path.GetExtension(_coverPath).ToLowerInvariant();
        if (extension is not (".png" or ".jpg" or ".jpeg")) return ($"✓ {name} · NOT PNG / JPG", UiFonts.Hard);
        return ($"✓ {name}", UiFonts.Casual);
    }

    /// <summary>MEMORY DRAFT：真实状态行（新建 = 尚未落盘的内存草稿；加差分 = 并入已打开包的内存文档）。</summary>
    private void DrawDraftPanel()
    {
        var shift = Shift;
        Panel(new Rect2(DraftPanelPos, DraftPanel.Size));
        var color = _snapshot.DraftWarn ? UiFonts.Hard : UiFonts.Cyan;
        EditorOverlayDraw.CutPanel(this, new Rect2(DraftSquarePos, new Vector2(10f, 10f)), 3f, color,
            new Color(0, 0, 0, 0), 0f);
        EditorOverlayDraw.LeftText(this, Bold2, new Vector2(DraftLabelX, DraftCenterY + shift), _snapshot.DraftLabel, EditorFonts.Pt(13), color);
        EditorOverlayDraw.LeftText(this, Tech1, new Vector2(DraftDetailX, DraftCenterY + shift), _snapshot.DraftDetail, EditorFonts.Pt(12), UiFonts.Dim);
    }

    /// <summary>底部：CANCEL 描边键、中缝状态（校验不过时写原因并转红，同时 CREATE 压到 55% 禁用）、
    /// CREATE PACKAGE 实心键。</summary>
    private void DrawBottomBar()
    {
        EditorOverlayDraw.CutStroke(this, CancelRect, BottomCut,
            UiFonts.Cyan.Lerp(UiFonts.Cyan.Lightened(HoverLift), HoverT(Hotspot.Cancel)), 2f);
        EditorOverlayDraw.CenteredText(this, Bold2, new Vector2(CancelCenterX, BottomCenterY), "CANCEL", EditorFonts.Pt(15), UiFonts.Cyan);

        EditorOverlayDraw.CenteredText(this, Bold2, new Vector2(StatusCenterX, BottomCenterY),
            FitLabel(Bold2, _statusText, EditorFonts.Pt(13), StatusMaxWidth), EditorFonts.Pt(13), _statusError ? UiFonts.Pink : UiFonts.Casual);

        if (!_createEnabled)
        {
            EditorOverlayDraw.CutStroke(this, CreateRect, BottomCut, new Color(UiFonts.Line, DisabledAlpha), 2f);
            EditorOverlayDraw.CenteredText(this, Bold2, new Vector2(CreateCenterX, BottomCenterY),
                "CREATE PACKAGE", EditorFonts.Pt(15), new Color(UiFonts.Dim, DisabledAlpha));
            return;
        }
        EditorOverlayDraw.CutPanel(this, CreateRect, BottomCut,
            UiFonts.Cyan.Lerp(UiFonts.Cyan.Lightened(HoverLift), HoverT(Hotspot.Create)), new Color(0, 0, 0, 0), 0f);
        EditorOverlayDraw.CenteredText(this, Bold2, new Vector2(CreateCenterX, BottomCenterY),
            "CREATE PACKAGE", EditorFonts.Pt(15), UiFonts.InkText);
    }

    // ---------------------------------------------------------------- 形状助手

    private void Panel(Rect2 rect) =>
        EditorOverlayDraw.CutPanel(this, rect, PanelCut, UiFonts.Panel, UiFonts.Line, 2f);

    /// <summary>字段框按对应 LineEdit 的焦点 glow 插值描边色：Line → Cyan（glow 0..1）。</summary>
    private Color FieldEdge(Control field) =>
        _fieldGlow.TryGetValue(field, out var glow) ? UiFonts.Line.Lerp(UiFonts.Cyan, glow) : UiFonts.Line;

    private void DrawField(Rect2 frame, string label, Control field, float alpha = 1f)
    {
        // alpha 供折叠过渡用（DIFF KEY 与它的 LineEdit 同步淡出/淡入）；1 时颜色原样 = 设计稿值。
        EditorOverlayDraw.CutPanel(this, frame, FieldCut, new Color(UiFonts.Panel, .6f * alpha),
            new Color(FieldEdge(field), FieldEdge(field).A * alpha), 2f);
        EditorOverlayDraw.LeftText(this, Tech2, new Vector2(frame.Position.X, frame.Position.Y + FieldLabelOffsetY),
            label, EditorFonts.Pt(FieldLabelSize), new Color(UiFonts.Dim, alpha));
    }

    /// <summary>切角矩形只切左上 + 右下；虚线版本按边逐段画（StyleBox 表达不了虚线切角框）。</summary>
    private void DashedCut(Rect2 rect, float cut, Color color, float width)
    {
        var points = UiGeometry.CutCorners(rect.Position.X, rect.Position.Y, rect.Size.X, rect.Size.Y, cut);
        for (var i = 0; i < points.Length; i++)
            DrawDashedLine(points[i], points[(i + 1) % points.Length], color, width, DashedDash, false);
    }

    /// <summary>设计稿 ICON_IMG：34 框里的相框 + 圆点 + 山线，1.6px 描边。</summary>
    private void DrawCoverIcon(Vector2 origin, Color color)
    {
        DrawRect(new Rect2(origin + new Vector2(3f, 5f), new Vector2(28f, 24f)), color, false, 1.6f);
        DrawArc(origin + new Vector2(12f, 13f), 3f, 0f, Mathf.Tau, 24, color, 1.6f, true);
        DrawPolyline(new[]
        {
            origin + new Vector2(5f, 26f), origin + new Vector2(13f, 18f), origin + new Vector2(19f, 24f),
            origin + new Vector2(24f, 19f), origin + new Vector2(31f, 26f),
        }, color, 1.6f, true);
    }

    /// <summary>状态行可能很长（中文错误原因），按真实字宽截断，避免压到两个动作键。</summary>
    private static string FitLabel(Font font, string text, int size, float maxWidth)
    {
        if (text.Length == 0 || font.GetStringSize(text, HorizontalAlignment.Left, -1, size).X <= maxWidth) return text;
        var trimmed = text;
        while (trimmed.Length > 1 &&
               font.GetStringSize(trimmed + "…", HorizontalAlignment.Left, -1, size).X > maxWidth)
            trimmed = trimmed[..^1];
        return trimmed + "…";
    }
}
