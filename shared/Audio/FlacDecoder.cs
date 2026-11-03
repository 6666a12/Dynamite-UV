namespace DynamiteUniverse.Shared.Audio;

/// <summary>
/// 纯 C# 的 FLAC 解码器（不依赖引擎或第三方包）。支持：
/// 8/16/24-bit、1/2 声道、constant/verbatim/fixed/LPC 子帧、Rice 与 Rice-2 residual、
/// 以及 left/side、side/right、mid/side 三种立体声去相关。
/// 输出统一为 16-bit 有符号小端交错 PCM。
/// </summary>
/// <remarks>
/// 明确拒绝：声道数 &gt; 2、位深不是 8/16/24、未知采样率、保留编码值、CRC 不匹配、
/// residual 分区阶数 &gt; 15、LPC 阶数 &gt; 32。所有长度/数量在分配前先与
/// <see cref="FlacDecoderOptions"/> 的预算比较。
/// </remarks>
public static class FlacDecoder
{
    private static ReadOnlySpan<byte> Magic => "fLaC"u8;
    private static ReadOnlySpan<byte> Id3Magic => "ID3"u8;
    private const int Id3HeaderBytes = 10;

    /// <summary>解码 FLAC 文件字节。失败时抛 <see cref="FlacDecodeException"/>，绝不返回空音频。</summary>
    public static FlacDecodedAudio Decode(ReadOnlySpan<byte> file, FlacDecoderOptions? options = null)
    {
        options ??= FlacDecoderOptions.Default;
        if (file.IsEmpty)
            throw new FlacDecodeException("FLAC 输入为空。");
        if (file.Length > options.MaxInputBytes)
            throw new FlacDecodeException(
                $"FLAC 输入 {file.Length} 字节超过上限 {options.MaxInputBytes}。");

        var offset = SkipLeadingId3(file);
        if (file.Length - offset < Magic.Length || !file.Slice(offset, Magic.Length).SequenceEqual(Magic))
            throw new FlacDecodeException("FLAC 文件头缺少 \"fLaC\" 标识。");

        var reader = new FlacBitReader(file.Slice(offset + Magic.Length));
        var streamInfo = FlacMetadataBlocks.ReadStreamAndSkipMetadata(ref reader, options,
            out var sawStreamInfo);
        if (!sawStreamInfo)
            throw new FlacDecodeException("FLAC 缺少 STREAMINFO。");

        var writer = new FlacPcmWriter(streamInfo.SampleRate, streamInfo.Channels,
            options.MaxPcmBytes);
        try
        {
            DecodeFrames(ref reader, streamInfo, options, writer);
        }
        catch (FlacDecodeException)
        {
            writer.Release();
            throw;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            writer.Release();
            throw new FlacDecodeException($"FLAC 解码失败：{ex.Message}", ex);
        }

        var frames = writer.WrittenFrames;
        if (frames == 0)
        {
            writer.Release();
            throw new FlacDecodeException("FLAC 没有解出任何音频帧。");
        }
        if (streamInfo.TotalSamples > 0 && frames != streamInfo.TotalSamples)
        {
            writer.Release();
            throw new FlacDecodeException(
                $"FLAC 声明 {streamInfo.TotalSamples} 个采样，实际解出 {frames} 个（截断或数据不一致）。");
        }

        var pcm = writer.ToLittleEndianBytes();
        return new FlacDecodedAudio(streamInfo.SampleRate, streamInfo.Channels,
            streamInfo.BitsPerSample, pcm);
    }

    private static void DecodeFrames(ref FlacBitReader reader, FlacStreamInfo streamInfo,
        FlacDecoderOptions options, FlacPcmWriter writer)
    {
        var scratch = new FlacFrameScratch();
        var frameCount = 0;
        while (reader.BitsRemaining > 0)
        {
            if (streamInfo.TotalSamples > 0 && writer.WrittenFrames >= streamInfo.TotalSamples)
                return; // 已满足声明长度；尾随字节（多为标签/填充）不再解释为帧

            if (++frameCount > options.MaxFrameCount)
                throw new FlacDecodeException(
                    $"FLAC 帧数超过上限 {options.MaxFrameCount}。");

            var before = reader.BitPosition;
            reader.AlignToByte();
            var header = FlacFrameDecoder.DecodeFrame(ref reader, streamInfo, options, scratch);
            if (reader.BitPosition <= before)
                throw new FlacDecodeException("FLAC 解码未推进位流，已中止以避免死循环。");
            writer.Append(scratch.Channel0, scratch.Channel1, header.BlockSize,
                header.BitsPerSample, streamInfo.TotalSamples);
        }
    }

    /// <summary>
    /// 跳过文件头部的 ID3v2 标签（部分打标工具会写在 FLAC 前面）。长度在这里硬校验，
    /// 不会按标签自述长度越界。
    /// </summary>
    private static int SkipLeadingId3(ReadOnlySpan<byte> file)
    {
        if (file.Length < Id3HeaderBytes || !file.Slice(0, 3).SequenceEqual(Id3Magic))
            return 0;

        var flags = file[5];
        var size = 0;
        for (var i = 6; i < 10; i++)
        {
            // 同步安全整数：每字节最高位必须是 0
            if ((file[i] & 0x80) != 0)
                throw new FlacDecodeException("FLAC 前置 ID3v2 标签长度字段非法。");
            size = (size << 7) | file[i];
        }

        var total = Id3HeaderBytes + size;
        if ((flags & 0x10) != 0)
            total += Id3HeaderBytes; // footer 存在
        if (total < 0 || total > file.Length)
            throw new FlacDecodeException(
                $"FLAC 前置 ID3v2 标签声明长度 {total} 越界。");
        return total;
    }
}
