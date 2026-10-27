using Godot;
using DynamiteUniverse.Shared.Chart;
using DynamiteUniverse.Shared.Judge;

namespace DynamiteUniverse.Game;

/// <summary>Immutable note presentation catalog shared by the gameplay renderer and note view.</summary>
public static class NoteVisualSpec
{
    /// <summary>
    /// 当前生效的六类配色。调用方在场景启动时从当前设置抄一份，
    /// 其余场景保持默认；编辑器预览因此始终是设计原色。
    /// </summary>
    public static NotePalette Active { get; set; } = NotePalette.CreateFactory();

    /// <summary>打击爆发是否跟随音符色（配色页 BURST · NOTE）。Miss / Mine 恒红。</summary>
    public static bool BurstFollowsNote { get; set; }

    // 改版配色（design/note-light-v1 REV 03），现从当前调色板取值。尺寸仍由 GameplayVisualMapper 决定。
    public static Color Tap => Active.Tap;
    public static Color ExTap => Active.ExTap;
    public static Color Drag => Active.Drag;
    public static Color Hold => Active.Hold;
    public static Color Mine => Active.Mine;
    public static Color Mixer => Active.Mixer;
    public static readonly Color BarLine = new(0.55f, 0.55f, 0.62f, 0.45f);
    public static readonly Color MixerBar = new(0.9f, 0.3f, 0.55f);

    public static Color BaseColor(NoteType type) => type switch
    {
        NoteType.Tap => Tap,
        NoteType.ExTap => ExTap,
        NoteType.Drag => Drag,
        NoteType.HoldHead or NoteType.HoldNode => Hold,
        NoteType.Mine => Mine,
        NoteType.BarLine => BarLine,
        _ => Mixer,
    };

    public static Color BaseColor(DynamiteUniverse.Shared.Chart.V2.V2NoteType type) => type switch
    {
        DynamiteUniverse.Shared.Chart.V2.V2NoteType.Tap => Tap,
        DynamiteUniverse.Shared.Chart.V2.V2NoteType.ExTap => ExTap,
        DynamiteUniverse.Shared.Chart.V2.V2NoteType.Drag => Drag,
        DynamiteUniverse.Shared.Chart.V2.V2NoteType.Hold => Hold,
        DynamiteUniverse.Shared.Chart.V2.V2NoteType.Mixer => Mixer,
        DynamiteUniverse.Shared.Chart.V2.V2NoteType.Mine => Mine,
        DynamiteUniverse.Shared.Chart.V2.V2NoteType.BarLine => BarLine,
        _ => Tap,
    };

    public static Color HitAccent(NoteType type) => type switch
    {
        NoteType.Tap => Tap,
        NoteType.ExTap => ExTap,
        NoteType.Drag => Drag,
        NoteType.HoldHead or NoteType.HoldNode => Hold,
        NoteType.MixerHead or NoteType.MixerNode => Mixer,
        NoteType.Mine => new Color(0.93f, 0.24f, 0.33f),
        _ => BarLine,
    };

    public static HitEffectKind HitEffect(NoteType type) => type switch
    {
        NoteType.ExTap => HitEffectKind.ExTap,
        NoteType.Drag => HitEffectKind.Drag,
        NoteType.HoldHead or NoteType.HoldNode => HitEffectKind.Hold,
        NoteType.MixerHead or NoteType.MixerNode => HitEffectKind.Mixer,
        NoteType.Mine => HitEffectKind.Mine,
        _ => HitEffectKind.Tap,
    };

    public static readonly Color HitPerfect = new(0.96f, 0.98f, 1.00f);
    public static readonly Color HitGreat = new(0.41f, 0.86f, 1.00f);
    public static readonly Color HitGood = new(1.00f, 0.82f, 0.40f);
    public static readonly Color HitMiss = new(1.00f, 0.33f, 0.38f);

    public static Color EffectAccent(NoteType type, JudgeGrade grade) =>
        EffectAccent(type, grade, Active, BurstFollowsNote);

    /// <summary>Preview uses an isolated draft instead of changing the active gameplay palette.</summary>
    public static Color EffectAccent(NoteType type, JudgeGrade grade, NotePalette palette, bool burstFollowsNote)
    {
        if (type == NoteType.Mine || grade == JudgeGrade.Miss)
            return HitMiss;
        if (burstFollowsNote)
            return type switch
            {
                NoteType.Tap => palette.Tap,
                NoteType.ExTap => palette.ExTap,
                NoteType.Drag => palette.Drag,
                NoteType.HoldHead or NoteType.HoldNode => palette.Hold,
                NoteType.MixerHead or NoteType.MixerNode => palette.Mixer,
                _ => HitPerfect,
            };
        return type is NoteType.Drag or NoteType.HoldHead or NoteType.HoldNode or
            NoteType.MixerHead or NoteType.MixerNode
            ? HitPerfect
            : grade switch
            {
                JudgeGrade.Great => HitGreat,
                JudgeGrade.Good => HitGood,
                _ => HitPerfect,
            };
    }

    public static Color LinkColor(NoteType type) => type switch
    {
        NoteType.HoldHead or NoteType.HoldNode => new Color(Hold, 0.28f),
        NoteType.Drag => new Color(Drag, 0.35f),
        NoteType.MixerHead or NoteType.MixerNode => new Color(Mixer, 0.35f),
        _ => new Color(Tap, 0.35f),
    };
}
