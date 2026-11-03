namespace DynamiteUniverse.Shared.Audio;

/// <summary>STREAMINFO 元数据块的字段快照。</summary>
internal readonly record struct FlacStreamInfo(
    int MinBlockSize,
    int MaxBlockSize,
    int SampleRate,
    int Channels,
    int BitsPerSample,
    long TotalSamples,
    bool HasMd5);

/// <summary>
/// 读取元数据块（STREAMINFO 及后续块）。只解出解码需要的字段，其余块按长度跳过；
/// 块类型与长度都在分配前校验。
/// </summary>
internal static class FlacMetadataBlocks
{
    internal const int StreamInfoBlockType = 0;
    private const int InvalidBlockType = 127;
    private const int StreamInfoBlockBytes = 34;

    internal static FlacStreamInfo ReadStreamAndSkipMetadata(
        ref FlacBitReader reader, FlacDecoderOptions options, out bool sawStreamInfo)
    {
        sawStreamInfo = false;
        FlacStreamInfo? streamInfo = null;
        var isLast = false;
        var blockIndex = 0;

        while (!isLast)
        {
            if (++blockIndex > 4096)
                throw new FlacDecodeException("FLAC 元数据块过多。");

            isLast = reader.ReadBits(1) == 1;
            var type = (int)reader.ReadBits(7);
            var length = (int)reader.ReadBits(24);
            if (type == InvalidBlockType)
                throw new FlacDecodeException("FLAC 元数据块类型 127 为非法值。");
            if (length < 0)
                throw new FlacDecodeException("FLAC 元数据块长度为负。");

            if (type == StreamInfoBlockType)
            {
                if (streamInfo != null)
                    throw new FlacDecodeException("FLAC 出现多个 STREAMINFO 块。");
                if (length != StreamInfoBlockBytes)
                    throw new FlacDecodeException(
                        $"FLAC STREAMINFO 长度 {length} 非法（应为 {StreamInfoBlockBytes}）。");
                streamInfo = ParseStreamInfo(ref reader, options);
                sawStreamInfo = true;
                continue;
            }

            if (streamInfo == null)
                throw new FlacDecodeException("FLAC 首个元数据块不是 STREAMINFO。");
            reader.SkipBits((long)length * 8);
        }

        if (streamInfo is not { } info)
            throw new FlacDecodeException("FLAC 缺少 STREAMINFO 块。");
        return info;
    }

    private static FlacStreamInfo ParseStreamInfo(ref FlacBitReader reader,
        FlacDecoderOptions options)
    {
        var minBlockSize = (int)reader.ReadBits(16);
        var maxBlockSize = (int)reader.ReadBits(16);
        _ = reader.ReadBits(24); // 最小帧字节数：只是提示，不作分配依据
        _ = reader.ReadBits(24); // 最大帧字节数
        var sampleRate = (int)reader.ReadBits(20);
        var channels = (int)reader.ReadBits(3) + 1;
        var bitsPerSample = (int)reader.ReadBits(5) + 1;
        var totalSamples = ((long)reader.ReadBits(4) << 32) | reader.ReadBits(32);
        var hasMd5 = false;
        for (var i = 0; i < 16; i++)
            hasMd5 |= reader.ReadBits(8) != 0;

        if (sampleRate <= 0)
            throw new FlacDecodeException("FLAC STREAMINFO 采样率为 0（未知采样率不支持）。");
        if (sampleRate < options.MinSampleRate || sampleRate > options.MaxSampleRate)
            throw new FlacDecodeException(
                $"FLAC 采样率 {sampleRate} Hz 超出支持范围 " +
                $"{options.MinSampleRate}–{options.MaxSampleRate} Hz。");
        if (channels < 1 || channels > options.MaxChannels)
            throw new FlacDecodeException(
                $"FLAC 声道数 {channels} 超出支持范围 1–{options.MaxChannels}。");
        if (bitsPerSample is not (8 or 16 or 24))
            throw new FlacDecodeException($"FLAC 位深 {bitsPerSample} 不受支持（支持 8/16/24）。");
        if (minBlockSize < 16 || maxBlockSize < minBlockSize || maxBlockSize > 65535)
            throw new FlacDecodeException(
                $"FLAC 块大小范围 {minBlockSize}–{maxBlockSize} 非法。");

        return new FlacStreamInfo(minBlockSize, maxBlockSize, sampleRate, channels,
            bitsPerSample, totalSamples, hasMd5);
    }
}
