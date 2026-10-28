namespace DynamiteUniverse.Shared.Judge;

/// <summary>BLEED 血量规则档（docs/gameplay-spec.md §4.3）：HARDCORE 强制使用 Hardcore 数值。</summary>
public enum BleedRule
{
    Standard,
    Hardcore,
}

/// <summary>
/// BLEED 血量（纯逻辑，不依赖 Godot）：初始/上限 <see cref="MaxHealth"/>，每次真实判定按
/// <see cref="BleedRule"/> 增减；耗尽后钳制 0 且不再变化。
/// </summary>
public sealed class BleedHealth
{
    public const int MaxHealth = 100;

    public BleedRule Rule { get; }

    /// <summary>当前血量（含小数：Perfect +0.2、Great/Good +0.05）。</summary>
    public double Health { get; private set; } = MaxHealth;

    public bool IsDepleted => Health <= 0.0;

    public BleedHealth(BleedRule rule) => Rule = rule;

    /// <summary>普通：Miss -2、Perfect +0.2、Great/Good +0.05。HARDCORE：Miss -4、Perfect +0.1、Great/Good 不回血。</summary>
    public static double DeltaOf(BleedRule rule, JudgeGrade grade) => rule switch
    {
        BleedRule.Hardcore => grade switch
        {
            JudgeGrade.Miss => -4.0,
            JudgeGrade.Perfect => 0.1,
            _ => 0.0,
        },
        _ => grade switch
        {
            JudgeGrade.Miss => -2.0,
            JudgeGrade.Perfect => 0.2,
            _ => 0.05,
        },
    };

    /// <summary>应用一次真实判定；耗尽后忽略后续判定，保持幂等。</summary>
    public void Apply(JudgeGrade grade)
    {
        if (IsDepleted)
            return;
        Health = Math.Clamp(Health + DeltaOf(Rule, grade), 0.0, MaxHealth);
    }
}
