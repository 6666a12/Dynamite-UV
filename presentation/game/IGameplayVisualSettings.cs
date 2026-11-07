using DynamiteUniverse.Ui;

namespace DynamiteUniverse.Game;

/// <summary>Host-supplied appearance and clock projection; no application settings access.</summary>
public interface IGameplayVisualSettings
{
    int TimingOffsetMs { get; }
    double FallSpeedMultiplier { get; }
    bool MirrorEnabled { get; }
    float NoteBrightnessScale { get; }
    float FlashIntensityScale { get; }
    int HitResidualMs { get; }
    float HitResidualSeconds { get; }
    HitEffectLevel HitEffectLevel { get; }
    UiMotionMode MotionMode { get; }
}

/// <summary>Full burst/sparks/embers, reduced burst/sparks, or no hit effects.</summary>
public enum HitEffectLevel { Full, Reduced, Off }
