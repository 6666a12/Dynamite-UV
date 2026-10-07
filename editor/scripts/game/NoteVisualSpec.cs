using Godot;
using DynamiteUniverse.Shared.Chart;
using DynamiteUniverse.Shared.Judge;

namespace DynamiteUniverse.Game;

/// <summary>Immutable note presentation catalog shared by the gameplay renderer and note view.</summary>
public static class NoteVisualSpec
{
    public static readonly Color Tap = new(0.30f, 0.75f, 1.00f);
    public static readonly Color ExTap = new(0.60f, 0.88f, 1.00f);
    public static readonly Color Drag = new(0.35f, 1.00f, 0.55f);
    public static readonly Color Hold = new(1.00f, 0.72f, 0.30f);
    public static readonly Color Mine = new(0.50f, 0.16f, 0.20f);
    public static readonly Color Mixer = new(1.00f, 0.45f, 0.72f);
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
        type == NoteType.Mine || grade == JudgeGrade.Miss
            ? HitMiss
            : type is NoteType.Drag or NoteType.HoldHead or NoteType.HoldNode or
                NoteType.MixerHead or NoteType.MixerNode
                ? HitPerfect
                : grade switch
                {
                    JudgeGrade.Great => HitGreat,
                    JudgeGrade.Good => HitGood,
                    _ => HitPerfect,
                };

    public static Color LinkColor(NoteType type) => type switch
    {
        NoteType.HoldHead or NoteType.HoldNode => new Color(1.0f, 0.70f, 0.45f, 0.28f),
        NoteType.Drag => new Color(Drag, 0.35f),
        NoteType.MixerHead or NoteType.MixerNode => new Color(Mixer, 0.35f),
        _ => new Color(Tap, 0.35f),
    };
}
