using System.Runtime.CompilerServices;

namespace DynamiteUniverse.Shared.Audio;

/// <summary>一帧解出的采样数与声道缓冲区。</summary>
internal sealed class FlacFrameScratch
{
    internal int[] Channel0 { get; private set; } = Array.Empty<int>();
    internal int[] Channel1 { get; private set; } = Array.Empty<int>();
    internal FlacResidualScratch Residual { get; } = new();

    internal void EnsureCapacity(int frames)
    {
        if (Channel0.Length < frames)
        {
            Channel0 = new int[frames];
            Channel1 = new int[frames];
        }
        Residual.EnsureCapacity(frames);
    }
}

/// <summary>解码单个 FLAC 帧（帧头 + 子帧 + 立体声去相关 + 帧尾 CRC-16）。</summary>
internal static class FlacFrameDecoder
{
    private const uint SyncCode = 0b11_1111_1111_1110; // 14 bits
    private const int MaxChannelAssignment = 10;

    internal readonly record struct FrameHeader(
        int BlockSize,
        int SampleRate,
        int Channels,
        int BitsPerSample,
        int ChannelAssignment,
        bool VariableBlocking,
        long FrameOrSampleNumber);

    /// <summary>
    /// 解码一帧到 <paramref name="scratch"/>，返回帧头信息。
    /// 调用方负责在读取前确保还有数据，并处理帧间对齐。
    /// </summary>
    internal static FrameHeader DecodeFrame(ref FlacBitReader reader, FlacStreamInfo streamInfo,
        FlacDecoderOptions options, FlacFrameScratch scratch)
    {
        var frameStartByte = reader.BitPosition >> 3;
        var header = ReadHeader(ref reader, streamInfo, options, frameStartByte);
        scratch.EnsureCapacity(header.BlockSize);

        var sideBits = header.BitsPerSample + (header.ChannelAssignment is 8 or 9 or 10 ? 1 : 0);
        switch (header.ChannelAssignment)
        {
            case 8: // left/side
                DecodeSubframe(ref reader, header, header.BitsPerSample, scratch.Channel0, 0, scratch);
                DecodeSubframe(ref reader, header, sideBits, scratch.Channel1, 1, scratch);
                for (var i = 0; i < header.BlockSize; i++)
                    scratch.Channel1[i] = unchecked(scratch.Channel0[i] - scratch.Channel1[i]);
                break;
            case 9: // side/right
                DecodeSubframe(ref reader, header, sideBits, scratch.Channel0, 0, scratch);
                DecodeSubframe(ref reader, header, header.BitsPerSample, scratch.Channel1, 1, scratch);
                for (var i = 0; i < header.BlockSize; i++)
                    scratch.Channel0[i] = unchecked(scratch.Channel0[i] + scratch.Channel1[i]);
                break;
            case 10: // mid/side
                DecodeSubframe(ref reader, header, header.BitsPerSample, scratch.Channel0, 0, scratch);
                DecodeSubframe(ref reader, header, sideBits, scratch.Channel1, 1, scratch);
                for (var i = 0; i < header.BlockSize; i++)
                {
                    var side = scratch.Channel1[i];
                    var mid = (scratch.Channel0[i] << 1) | (side & 1);
                    scratch.Channel0[i] = (mid + side) >> 1;
                    scratch.Channel1[i] = (mid - side) >> 1;
                }
                break;
            default: // 0–7：各声道独立
                DecodeSubframe(ref reader, header, header.BitsPerSample, scratch.Channel0, 0, scratch);
                if (header.Channels > 1)
                    DecodeSubframe(ref reader, header, header.BitsPerSample, scratch.Channel1, 1, scratch);
                break;
        }

        reader.AlignToByte();
        var crcEndByte = (int)(reader.BitPosition >> 3);
        var expectedCrc = (ushort)reader.ReadBits(16);
        if (options.VerifyFrameCrc)
        {
            var actual = FlacCrc.Compute16(reader.SliceBytes((int)frameStartByte,
                crcEndByte - (int)frameStartByte));
            if (actual != expectedCrc)
                throw new FlacDecodeException(
                    $"FLAC 帧 CRC-16 不匹配（帧起点 {frameStartByte}，期望 0x{expectedCrc:X4}，" +
                    $"实际 0x{actual:X4}）。");
        }

        return header;
    }

    private static void DecodeSubframe(ref FlacBitReader reader, in FrameHeader header,
        int subframeBits, int[] target, int channelIndex, FlacFrameScratch scratch)
    {
        if (channelIndex == 1 && header.Channels < 2)
            throw new FlacDecodeException("FLAC 帧声道数与声道分配不一致。");
        FlacSubframeDecoder.Decode(ref reader, header.BlockSize, subframeBits, target,
            scratch.Residual);
    }

    private static FrameHeader ReadHeader(ref FlacBitReader reader, FlacStreamInfo streamInfo,
        FlacDecoderOptions options, long frameStartByte)
    {
        if (reader.ReadBits(14) != SyncCode)
            throw new FlacDecodeException($"FLAC 帧同步码缺失（帧起点 {frameStartByte}）。");
        if (reader.ReadBits(1) != 0)
            throw new FlacDecodeException("FLAC 帧头保留位非 0。");
        var variableBlocking = reader.ReadBits(1) == 1;

        var blockSizeCode = (int)reader.ReadBits(4);
        var sampleRateCode = (int)reader.ReadBits(4);
        var channelAssignment = (int)reader.ReadBits(4);
        var sampleSizeCode = (int)reader.ReadBits(3);
        if (reader.ReadBits(1) != 0)
            throw new FlacDecodeException("FLAC 帧头第二个保留位非 0。");
        if (channelAssignment > MaxChannelAssignment)
            throw new FlacDecodeException(
                $"FLAC 声道分配 0b{channelAssignment:X4} 为保留值，不受支持。");

        var number = reader.ReadUtf8CodedNumber();

        var blockSize = blockSizeCode switch
        {
            0 => throw new FlacDecodeException("FLAC 帧头块大小编码 0 为保留值。"),
            1 => 192,
            >= 2 and <= 5 => 576 << (blockSizeCode - 2),
            6 => (int)reader.ReadBits(8) + 1,
            7 => (int)reader.ReadBits(16) + 1,
            _ => 256 << (blockSizeCode - 8),
        };

        var sampleRate = sampleRateCode switch
        {
            0 => streamInfo.SampleRate,
            1 => 88_200,
            2 => 176_400,
            3 => 192_000,
            4 => 8_000,
            5 => 16_000,
            6 => 22_050,
            7 => 24_000,
            8 => 32_000,
            9 => 44_100,
            10 => 48_000,
            11 => 96_000,
            12 => (int)reader.ReadBits(8) * 1000,
            13 => (int)reader.ReadBits(16),
            14 => (int)reader.ReadBits(16) * 10,
            _ => throw new FlacDecodeException("FLAC 帧头采样率编码 15 为非法值。"),
        };

        var bitsPerSample = sampleSizeCode switch
        {
            0 => streamInfo.BitsPerSample,
            1 => 8,
            2 => 12,
            4 => 16,
            5 => 20,
            6 => 24,
            _ => throw new FlacDecodeException(
                $"FLAC 帧头位深编码 {sampleSizeCode} 为保留值，不受支持。"),
        };

        if (sampleRate != streamInfo.SampleRate)
            throw new FlacDecodeException(
                $"FLAC 帧采样率 {sampleRate} 与 STREAMINFO 的 {streamInfo.SampleRate} 不一致。");
        if (bitsPerSample != streamInfo.BitsPerSample)
            throw new FlacDecodeException(
                $"FLAC 帧位深 {bitsPerSample} 与 STREAMINFO 的 {streamInfo.BitsPerSample} 不一致。");
        if (sampleRate < options.MinSampleRate || sampleRate > options.MaxSampleRate)
            throw new FlacDecodeException($"FLAC 帧采样率 {sampleRate} Hz 超出支持范围。");
        if (blockSize > streamInfo.MaxBlockSize)
            throw new FlacDecodeException(
                $"FLAC 帧块大小 {blockSize} 超过 STREAMINFO 声明的最大值 {streamInfo.MaxBlockSize}。");
        if (blockSize <= 0)
            throw new FlacDecodeException("FLAC 帧块大小必须为正。");

        var channels = channelAssignment <= 7 ? channelAssignment + 1 : 2;
        if (channels != streamInfo.Channels)
            throw new FlacDecodeException(
                $"FLAC 帧声道数 {channels} 与 STREAMINFO 的 {streamInfo.Channels} 不一致。");

        var headerEndByte = (int)(reader.BitPosition >> 3);
        if (options.VerifyFrameCrc)
        {
            var expected = (byte)reader.ReadBits(8);
            var actual = FlacCrc.Compute8(
                reader.SliceBytes((int)frameStartByte, headerEndByte - (int)frameStartByte));
            if (actual != expected)
                throw new FlacDecodeException(
                    $"FLAC 帧头 CRC-8 不匹配（帧起点 {frameStartByte}）。");
        }
        else
        {
            reader.SkipBits(8);
        }

        return new FrameHeader(blockSize, sampleRate, channels, bitsPerSample, channelAssignment,
            variableBlocking, number);
    }

    /// <summary>
    /// 根据位深把解出的整数采样缩放并钳制到 16-bit PCM。高位深按算术右移截断
    /// （与 libFLAC/ffmpeg 的 s24→s16 惯例一致，负值向负无穷取整），保证同一输入
    /// 在任何平台得到完全相同的字节。
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static short ToPcm16(int value, int bitsPerSample) => bitsPerSample switch
    {
        <= 16 => (short)Math.Clamp(value << (16 - bitsPerSample), short.MinValue, short.MaxValue),
        _ => (short)Math.Clamp(value >> (bitsPerSample - 16), short.MinValue, short.MaxValue),
    };
}
