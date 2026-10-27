using Godot;
using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using DynamiteUniverse.ChartEditor.Core;
using DynamiteUniverse.Game;
using DynamiteUniverse.Ui;

namespace DynamiteUniverse.Editor;

/// <summary>编辑器本地设置：内存单例 + `user://editor-settings.json`，启动加载、改动写盘、
/// RESET 回默认值，与客户端 `<c>GameSettings</c>` 同一套做法（读参考，不互相依赖）。
///
/// 只收**真实接线**的项：MOTION 驱动编辑器自己的淡入、DEFAULT GRID 作为新草稿与当前文档的
/// 网格分母、SNAP 是画布空间吸附步长、GRID OPACITY 与 BACKGROUND DIM 是画布网格与舞台背景的倍率、
/// SCROLL INERTIA 是滚轮/触摸板滚动的惯性强度、UI SCALE 是正文字号倍率（见 <see cref="EditorFonts"/>）。
/// 设计板上没有实现可接的占位行（EFFECTS、AUTO SAVE、AUDIO 组）不进本模型，
/// 由设置页按设计稿示例值画 55% 占位，避免把死值伪装成已持久化的设置。</summary>
internal sealed class EditorSettings
{
    private const string SavePath = "user://editor-settings.json";

    /// <summary>SCROLL INERTIA 的默认档（中档）。40% × 上限 0.28s = 0.112s，落在 client 侧
    /// 固定常量（<c>SongSelect.ScrollInertiaTauSeconds</c> = 0.11s）上，两边默认手感一致。</summary>
    public const int DefaultScrollInertiaPercent = 40;

    /// <summary>SCROLL INERTIA 100% 时的时间常数上限。再大就只剩"漂"的观感：指数趋近在 3τ
    /// 后还剩约 5% 行程，τ 到 0.28s 意味着最后一段要花掉近一秒。</summary>
    public const double ScrollInertiaMaxTauSeconds = .28d;

    /// <summary>EDITOR / DEFAULT GRID 的三档（设计稿 1/8、1/16、1/32）。</summary>
    public static readonly int[] GridDivisorChoices = [8, 16, 32];

    /// <summary>EDITOR / SNAP 的三档步长（设计稿示例值 0.10）。</summary>
    public static readonly double[] SnapStepChoices = [.05d, .1d, .25d];

    /// <summary>GENERAL / UI SCALE 的四档字号倍率（100 = 引入这一项之前的观感）。
    /// 板面上没有这一行，四档是按"最小档救小字、最大档不撑破固定几何"定的，依据见
    /// docs/editor-ui-design.md §4 第 15 条。</summary>
    public static readonly int[] UiScaleChoices = [90, 100, 110, 125];

    /// <summary>UI SCALE 的默认档（= 引入这一项之前的观感，设置真源里的值因此与加这项时逐值一致）。</summary>
    public const int DefaultUiScalePercent = 100;

    public static EditorSettings Current { get; } = new();

    /// <summary>GENERAL / MOTION：与客户端同语义（FULL / REDUCED / OFF）。</summary>
    public UiMotionMode MotionMode { get; set; } = UiMotionMode.Full;

    /// <summary>EDITOR / DEFAULT GRID：新草稿的 grid 参数与当前文档 `GridDivisor` 的默认值。</summary>
    public int DefaultGridDivisor { get; set; } = EditorDocument.DefaultGridDivisor;

    /// <summary>EDITOR / SNAP：画布空间吸附的步长（按住 X 时的 0.01 细步不受它影响）。</summary>
    public double SnapStep { get; set; } = .1d;

    /// <summary>EDITOR / GRID OPACITY：三档网格绘制的不透明度倍率，100 = 设计稿浓度。</summary>
    public int GridOpacityPercent { get; set; } = 100;

    /// <summary>EDITOR / BACKGROUND DIM：舞台背景的压暗百分比（0 = 不压暗）。</summary>
    public int BackgroundDimPercent { get; set; }

    /// <summary>EDITOR / SCROLL INERTIA：滚轮与触摸板滚动的惯性强度（0 = 瞬时，与改动前一致）。
    /// 只作用于"视图值"（画布预览时间、包页列表偏移），不碰写入文档的时间逻辑。</summary>
    public int ScrollInertiaPercent { get; set; } = DefaultScrollInertiaPercent;

    /// <summary>GENERAL / UI SCALE：正文字号倍率的主体（90/100/110/125），落到
    /// <see cref="EditorFonts.Pt"/> 上。它**不缩放布局几何**——编辑器坐标是设计稿绝对值，
    /// 倍率只改字的像素高度。90–125 是「最小档救得了小字、最大档不撑破固定框」的窄区间：
    /// 125% 时最大的标题字已经要贴住它那一行的固定框，再往上就是顶框而不是更好读。</summary>
    public int UiScalePercent { get; set; } = DefaultUiScalePercent;

    /// <summary>网格分母当前落在哪一档（手改过的文件按最近档显示）。</summary>
    public int GridChoiceIndex => Math.Max(0, Array.IndexOf(GridDivisorChoices, DefaultGridDivisor));

    /// <summary>吸附步长当前落在哪一档。</summary>
    public int SnapChoiceIndex => Math.Max(0, Array.IndexOf(SnapStepChoices, SnapStep));

    /// <summary>UI SCALE 当前落在哪一档。</summary>
    public int UiScaleChoiceIndex => Math.Max(0, Array.IndexOf(UiScaleChoices, UiScalePercent));

    /// <summary>UI SCALE 的倍率（0.9 / 1.0 / 1.1 / 1.25）。</summary>
    public float UiScale => Math.Clamp(UiScalePercent, 90, 125) / 100f;

    /// <summary>背景压暗的 0..1 系数；上限 80% 是为了不把舞台背景压成纯黑。</summary>
    public float BackgroundDimScale => 1f - Math.Clamp(BackgroundDimPercent, 0, 80) / 100f;

    /// <summary>网格不透明度倍率（0..1），画布三档线芯与软光都乘它。</summary>
    public float GridOpacityScale => Math.Clamp(GridOpacityPercent, 0, 100) / 100f;

    /// <summary>滚动惯性强度（0..1）：0 = 瞬时落目标，1 = 上限 τ。</summary>
    public float ScrollInertiaScale => Math.Clamp(ScrollInertiaPercent, 0, 100) / 100f;

    /// <summary>滚轮/触摸板滚动的指数趋近时间常数（秒）。0 表示瞬时（直接落目标）。
    /// MOTION = OFF 时强制为 0：那是"无动效"档，滚动拖尾属于动效，留着会与设置语义矛盾。
    /// （选择依据见 docs/editor-ui-design.md §4 第 14 条。）</summary>
    public double ScrollInertiaTauSeconds => MotionMode == UiMotionMode.Off
        ? 0d
        : ScrollInertiaScale * ScrollInertiaMaxTauSeconds;

    public void ResetToDefaults()
    {
        MotionMode = UiMotionMode.Full;
        DefaultGridDivisor = EditorDocument.DefaultGridDivisor;
        SnapStep = .1d;
        GridOpacityPercent = 100;
        BackgroundDimPercent = 0;
        ScrollInertiaPercent = DefaultScrollInertiaPercent;
        UiScalePercent = DefaultUiScalePercent;
    }

    public void Load()
    {
        ResetToDefaults();
        if (!Godot.FileAccess.FileExists(SavePath)) return;
        try
        {
            using var file = Godot.FileAccess.Open(SavePath, Godot.FileAccess.ModeFlags.Read);
            var data = JsonSerializer.Deserialize<SettingsData>(file.GetAsText());
            if (data is null) return;
            if (data.MotionMode is { } motion) MotionMode = ParseMotionMode(motion);
            if (data.DefaultGridDivisor is { } divisor) DefaultGridDivisor = divisor;
            if (data.SnapStep is { } snap) SnapStep = snap;
            if (data.GridOpacityPercent is { } opacity) GridOpacityPercent = opacity;
            if (data.BackgroundDimPercent is { } dim) BackgroundDimPercent = dim;
            if (data.ScrollInertiaPercent is { } inertia) ScrollInertiaPercent = inertia;
            if (data.UiScalePercent is { } scale) UiScalePercent = scale;
        }
        catch (Exception exception)
        {
            // 读失败只用默认值，不打断 shell 启动。
            GD.PushWarning($"EditorSettings: 读取 {SavePath} 失败，使用默认值。{exception.Message}");
            ResetToDefaults();
        }
        ClampValues();
    }

    public void Save()
    {
        ClampValues();
        try
        {
            var json = JsonSerializer.Serialize(new SettingsData
            {
                MotionMode = SerializeMotionMode(MotionMode),
                DefaultGridDivisor = DefaultGridDivisor,
                SnapStep = SnapStep,
                GridOpacityPercent = GridOpacityPercent,
                BackgroundDimPercent = BackgroundDimPercent,
                ScrollInertiaPercent = ScrollInertiaPercent,
                UiScalePercent = UiScalePercent,
            }, new JsonSerializerOptions { WriteIndented = true });
            SettingsFilePersistence.Write(ProjectSettings.GlobalizePath(SavePath), json);
        }
        catch (Exception exception)
        {
            GD.PushWarning($"EditorSettings: 写入 {SavePath} 失败。{exception.Message}");
        }
    }

    /// <summary>非法值一律钳制回可用的档位：网格分母、吸附步长与 UI SCALE 必须落在设置页给出的档上，
    /// 否则页面的分段器/步进器没有对应选中项。</summary>
    private void ClampValues()
    {
        if (!Enum.IsDefined(MotionMode)) MotionMode = UiMotionMode.Full;
        DefaultGridDivisor = Nearest(GridDivisorChoices, DefaultGridDivisor);
        SnapStep = Nearest(SnapStepChoices, SnapStep);
        GridOpacityPercent = Math.Clamp(GridOpacityPercent, 0, 100);
        BackgroundDimPercent = Math.Clamp(BackgroundDimPercent, 0, 80);
        ScrollInertiaPercent = Math.Clamp(ScrollInertiaPercent, 0, 100);
        UiScalePercent = Nearest(UiScaleChoices, UiScalePercent);
    }

    private static int Nearest(int[] choices, int value)
    {
        var best = choices[0];
        foreach (var choice in choices)
            if (Math.Abs(choice - value) < Math.Abs(best - value)) best = choice;
        return best;
    }

    private static double Nearest(double[] choices, double value)
    {
        var best = choices[0];
        foreach (var choice in choices)
            if (Math.Abs(choice - value) < Math.Abs(best - value)) best = choice;
        return best;
    }

    private static UiMotionMode ParseMotionMode(string value) => value switch
    {
        "reduced" => UiMotionMode.Reduced,
        "off" => UiMotionMode.Off,
        _ => UiMotionMode.Full,
    };

    private static string SerializeMotionMode(UiMotionMode mode) => mode switch
    {
        UiMotionMode.Reduced => "reduced",
        UiMotionMode.Off => "off",
        _ => "full",
    };

    private sealed class SettingsData
    {
        [JsonPropertyName("motionMode")] public string? MotionMode { get; set; }
        [JsonPropertyName("defaultGridDivisor")] public int? DefaultGridDivisor { get; set; }
        [JsonPropertyName("snapStep")] public double? SnapStep { get; set; }
        [JsonPropertyName("gridOpacityPercent")] public int? GridOpacityPercent { get; set; }
        [JsonPropertyName("backgroundDimPercent")] public int? BackgroundDimPercent { get; set; }
        [JsonPropertyName("scrollInertiaPercent")] public int? ScrollInertiaPercent { get; set; }
        [JsonPropertyName("uiScalePercent")] public int? UiScalePercent { get; set; }
    }
}
