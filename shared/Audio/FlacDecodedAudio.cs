namespace DynamiteUniverse.Shared.Audio;

/// <summary>
/// 解码结果。PCM 统一为 16-bit 有符号小端交错，可直接作为 Godot AudioStreamWav 的 Data。
/// </summary>
public sealed class FlacDecodedAudio
{
    public FlacDecodedAudio(int sampleRate, int channels, int sourceBitsPerSample, byte[] pcm16LittleEndian)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sampleRate);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(channels);
        ArgumentNullException.ThrowIfNull(pcm16LittleEndian);
        SampleRate = sampleRate;
        Channels = channels;
        SourceBitsPerSample = sourceBitsPerSample;
        Pcm16LittleEndian = pcm16LittleEndian;
    }

    public int SampleRate { get; }

    public int Channels { get; }

    /// <summary>源文件位深（8/16/24 等），PCM 已按此缩放，仅供诊断。</summary>
    public int SourceBitsPerSample { get; }

    public byte[] Pcm16LittleEndian { get; }

    public long FrameCount => Pcm16LittleEndian.LongLength / (2L * Channels);

    public double DurationSeconds => (double)FrameCount / SampleRate;
}
