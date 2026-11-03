namespace DynamiteUniverse.Shared.Audio;

/// <summary>
/// 解码预算。所有上限都在分配之前检查，恶意包只能被拒绝，不能逼出失控分配。
/// </summary>
public sealed record FlacDecoderOptions
{
    public static FlacDecoderOptions Default { get; } = new();

    /// <summary>输入 FLAC 字节数上限。</summary>
    public int MaxInputBytes { get; init; } = 128 * 1024 * 1024;

    /// <summary>解码后 16-bit 交错 PCM 的字节数上限。</summary>
    public int MaxPcmBytes { get; init; } = 128 * 1024 * 1024;

    /// <summary>支持的声道数上限（当前实现只做 1/2 声道）。</summary>
    public int MaxChannels { get; init; } = 2;

    public int MinSampleRate { get; init; } = 8_000;

    public int MaxSampleRate { get; init; } = 192_000;

    /// <summary>FLAC 帧数上限，防止海量零长帧把解码变成无界循环。</summary>
    public int MaxFrameCount { get; init; } = 4_000_000;

    /// <summary>是否校验帧头 CRC-8 与帧尾 CRC-16。关闭只用于定位损坏样本。</summary>
    public bool VerifyFrameCrc { get; init; } = true;
}
