namespace DynamiteUniverse.Ui;

/// <summary>
/// 结算入口的防穿透间隔。前奏与屏风共用同一条规则：跳过/交付那一帧被消费，
/// 之后的 <see cref="Msec"/> 毫秒内任何按压都不得落到刚出现的结算按钮上。
/// 纯算术，不读 Godot 时钟，由调用方传入毫秒读数。
/// </summary>
public static class ResultInputGuard
{
    public const int Msec = 120;

    public static ulong Until(ulong nowMsec) => nowMsec + Msec;

    public static bool Blocks(ulong nowMsec, ulong unlockMsec) => nowMsec < unlockMsec;
}
