namespace CoreTests;

/// <summary>
/// 测试专用的最小 FLAC 编码器（clean-room，不依赖外部工具或第三方包）。
/// 它让解码测试有独立的地面真值：编码器按 FLAC 规范写出指定子帧类型/声道分配的
/// 位流，解码后必须逐采样还原。<c>ShouldEncode</c> 之外的 <c>ExpectedPcm</c>
/// 也由这里统一缩放，避免测试自己复制一遍解码侧的换算规则。
/// </summary>
internal sealed class FlacTestEncoder
{
    internal enum SubframeKind
    {
        Constant,
        Verbatim,
        Fixed0,
        Fixed1,
        Fixed2,
        Fixed3,
        Fixed4,
        Lpc,
    }

    internal enum ChannelMode
    {
        Independent,
        LeftSide,
        RightSide,
        MidSide,
    }

    internal enum ResidualKind
    {
        /// <summary>正常 Rice 编码，参数由编码器按残差幅度选。</summary>
        Rice,
        /// <summary>Rice 转义码：直接写固定位宽的原始残差。</summary>
        Escape,
    }

    internal required int[][] Channels { get; init; }
    internal required int SampleRate { get; init; }
    internal required int BitsPerSample { get; init; }
    internal int BlockSize { get; init; } = 4096;
    internal SubframeKind Kind { get; init; } = SubframeKind.Verbatim;
    internal ChannelMode Mode { get; init; } = ChannelMode.Independent;
    internal ResidualKind Residual { get; init; } = ResidualKind.Rice;
    internal int LpcOrder { get; init; } = 2;
    internal int WastedBits { get; init; }
    internal int ResidualPartitionOrder { get; init; }
    internal int SampleSizeCodeOverride { get; init; }
    internal int SampleRateCodeOverride { get; init; } = -1;
    internal int BlockSizeCodeOverride { get; init; } = -1;
    internal bool UseVariableBlocking { get; init; }
    /// <summary>STREAMINFO 里声明的声道数/位深/采样率（默认与真实数据一致）。</summary>
    internal int? StreamInfoChannels { get; init; }
    internal int? StreamInfoBitsPerSample { get; init; }
    internal int? StreamInfoSampleRate { get; init; }
    internal long? StreamInfoTotalSamples { get; init; }
    /// <summary>写入保留的子帧类型编码（0x20 起的保留区）。</summary>
    internal int? ReservedSubframeType { get; init; }
    internal int? ReservedChannelAssignment { get; init; }
    internal bool CorruptHeaderCrc { get; init; }
    internal bool CorruptFrameCrc { get; init; }
    internal int FramePrefixBytes { get; init; }
    internal bool OmitStreamInfo { get; init; }
    internal int ExtraTrailingBytes { get; init; }
    internal int? ReservedLpcPrecision { get; init; }

    internal int FrameCount => Channels[0].Length;

    private long _frameIndex;

    /// <summary>把已知采样按解码器约定换算成 16-bit，作为比较基准。</summary>
    internal byte[] ExpectedPcm16()
    {
        var channels = Channels.Length;
        var frames = FrameCount;
        var bytes = new byte[frames * channels * 2];
        var index = 0;
        for (var i = 0; i < frames; i++)
        {
            for (var c = 0; c < channels; c++)
            {
                var value = Channels[c][i];
                short sample = BitsPerSample <= 16
                    ? (short)Math.Clamp(value << (16 - BitsPerSample), short.MinValue, short.MaxValue)
                    : (short)Math.Clamp(value >> (BitsPerSample - 16), short.MinValue, short.MaxValue);
                bytes[index++] = (byte)sample;
                bytes[index++] = (byte)(sample >> 8);
            }
        }
        return bytes;
    }

    internal byte[] Encode()
    {
        var writer = new BitWriter();
        _frameIndex = 0;
        writer.WriteBytes("fLaC"u8);
        if (!OmitStreamInfo)
            WriteStreamInfo(writer);
        else
            WriteUnknownMetadata(writer);

        var frames = FrameCount;
        var blockIndex = 0;
        while (blockIndex < frames)
        {
            var block = Math.Min(BlockSize, frames - blockIndex);
            WriteFrame(writer, blockIndex, block);
            blockIndex += block;
        }
        for (var i = 0; i < ExtraTrailingBytes; i++)
            writer.WriteByteValue(0x00);
        writer.AlignToByte();
        return writer.ToArray();
    }

    private void WriteStreamInfo(BitWriter writer)
    {
        writer.WriteBits(1, 1); // last metadata block
        writer.WriteBits(0, 7); // STREAMINFO
        writer.WriteBits(34, 24);
        writer.WriteBits(BlockSize, 16);
        writer.WriteBits(BlockSize, 16);
        writer.WriteBits(0, 24);
        writer.WriteBits(0, 24);
        var rate = StreamInfoSampleRate ?? SampleRate;
        writer.WriteBits(rate, 20);
        var channels = StreamInfoChannels ?? Channels.Length;
        writer.WriteBits(channels - 1, 3);
        var bits = StreamInfoBitsPerSample ?? BitsPerSample;
        writer.WriteBits(bits - 1, 5);
        var total = StreamInfoTotalSamples ?? FrameCount;
        writer.WriteBits((int)(total >> 32) & 0xF, 4);
        writer.WriteBits((int)(total & 0xFFFFFFFF), 32);
        for (var i = 0; i < 16; i++)
            writer.WriteBits(0, 8);
    }

    private static void WriteUnknownMetadata(BitWriter writer)
    {
        // 只有一个非 STREAMINFO 的块，用于验证“缺少 STREAMINFO”必须被拒绝。
        writer.WriteBits(1, 1);
        writer.WriteBits(4, 7); // VORBIS_COMMENT
        writer.WriteBits(4, 24);
        writer.WriteBits(0, 32);
    }

    private void WriteFrame(BitWriter writer, int startFrame, int block)
    {
        var frameStart = writer.BytePosition;
        writer.WriteBits(0b11_1111_1111_1110, 14);
        writer.WriteBits(0, 1);
        writer.WriteBits(UseVariableBlocking ? 1 : 0, 1);

        var (blockCode, blockExtraBits, blockExtraValue) = ResolveBlockSizeCode(block);
        writer.WriteBits(blockCode, 4);
        var (rateCode, rateBits, rateValue) = ResolveSampleRateCode();
        writer.WriteBits(rateCode, 4);

        var assignment = ReservedChannelAssignment ??
                         Mode switch
                         {
                             ChannelMode.Independent => Channels.Length - 1,
                             ChannelMode.LeftSide => 8,
                             ChannelMode.RightSide => 9,
                             _ => Channels.Length > 1 ? 10 : 0,
                         };
        writer.WriteBits(assignment, 4);
        var sizeCode = SampleSizeCodeOverride != 0
            ? SampleSizeCodeOverride
            : BitsPerSample switch { 8 => 1, 12 => 2, 16 => 4, 20 => 5, 24 => 6, _ => 4 };
        writer.WriteBits(sizeCode, 3);
        writer.WriteBits(0, 1);
        // 固定块策略写帧序号，变量块策略写首个采样号（libFLAC 会校验前者）。
        WriteUtf8Number(writer, UseVariableBlocking ? startFrame : _frameIndex++);
        if (blockExtraBits > 0)
            writer.WriteBits(blockExtraValue, blockExtraBits);
        if (rateBits > 0)
            writer.WriteBits(rateValue, rateBits);

        var headerCrc = Crc8(writer.Slice(frameStart, writer.BytePosition));
        writer.WriteByteValue((byte)(CorruptHeaderCrc ? headerCrc ^ 0xFF : headerCrc));

        WriteSubframes(writer, startFrame, block, assignment);

        writer.AlignToByte();
        var frameCrc = Crc16(writer.Slice(frameStart, writer.BytePosition));
        writer.WriteBits(CorruptFrameCrc ? frameCrc ^ 0xFFFF : frameCrc, 16);
    }

    private (int Code, int ExtraBits, int ExtraValue) ResolveBlockSizeCode(int block)
    {
        if (BlockSizeCodeOverride >= 0)
            return (BlockSizeCodeOverride, 0, 0);
        if (block == 192)
            return (1, 0, 0);
        if (block == 576)
            return (2, 0, 0);
        if (block == 1152)
            return (3, 0, 0);
        if (block == 2304)
            return (4, 0, 0);
        if (block == 256)
            return (8, 0, 0);
        if (block == 512)
            return (9, 0, 0);
        return block is > 0 and <= 256 ? (6, 8, block - 1) : (7, 16, block - 1);
    }

    private (int Code, int ExtraBits, int ExtraValue) ResolveSampleRateCode()
    {
        if (SampleRateCodeOverride >= 0)
            return (SampleRateCodeOverride, 0, 0);
        return SampleRate switch
        {
            88_200 => (1, 0, 0),
            176_400 => (2, 0, 0),
            192_000 => (3, 0, 0),
            8_000 => (4, 0, 0),
            16_000 => (5, 0, 0),
            22_050 => (6, 0, 0),
            24_000 => (7, 0, 0),
            32_000 => (8, 0, 0),
            44_100 => (9, 0, 0),
            48_000 => (10, 0, 0),
            96_000 => (11, 0, 0),
            _ when SampleRate % 1000 == 0 && SampleRate / 1000 <= 255 =>
                (12, 8, SampleRate / 1000),
            _ when SampleRate <= 65535 => (13, 16, SampleRate),
            _ => (14, 16, SampleRate / 10),
        };
    }

    private void WriteSubframes(BitWriter writer, int startFrame, int block, int assignment)
    {
        // 声道对的子帧位宽：side 声道多 1 位。
        var baseBits = BitsPerSample;
        switch (assignment)
        {
            case 8: // left/side
                WriteSubframe(writer, Channels[0], startFrame, block, baseBits);
                WriteSubframe(writer, BuildSide(startFrame, block), 0, block, baseBits + 1);
                break;
            case 9: // side/right
                WriteSubframe(writer, BuildSide(startFrame, block), 0, block, baseBits + 1);
                WriteSubframe(writer, Channels[1], startFrame, block, baseBits);
                break;
            case 10: // mid/side
                WriteSubframe(writer, BuildMid(startFrame, block), 0, block, baseBits);
                WriteSubframe(writer, BuildSide(startFrame, block), 0, block, baseBits + 1);
                break;
            default:
                WriteSubframe(writer, Channels[0], startFrame, block, baseBits);
                if (Channels.Length > 1)
                    WriteSubframe(writer, Channels[1], startFrame, block, baseBits);
                break;
        }
    }

    private int[] BuildMid(int startFrame, int block)
    {
        var mid = new int[block];
        for (var i = 0; i < block; i++)
            mid[i] = (Channels[0][startFrame + i] + Channels[1][startFrame + i]) >> 1;
        return mid;
    }

    private int[] BuildSide(int startFrame, int block)
    {
        var side = new int[block];
        for (var i = 0; i < block; i++)
        {
            var l = Channels[0][startFrame + i];
            var r = Channels[1][startFrame + i];
            side[i] = l - r;
        }
        return side;
    }

    private void WriteSubframe(BitWriter writer, int[] values, int startFrame, int block,
        int bits)
    {
        writer.WriteBits(0, 1); // zero padding bit
        var effectiveBits = bits - WastedBits;
        if (ReservedSubframeType is { } reserved)
        {
            writer.WriteBits(reserved, 6);
            writer.WriteBits(0, 1);
            writer.WriteBits(0, effectiveBits);
            return;
        }

        if (ReservedLpcPrecision is { } reservedPrecision)
        {
            // 保留系数精度也要走真实 LPC 结构，才能验证解码器拒绝的是精度字段本身。
            writer.WriteBits(32 + LpcOrder - 1, 6);
            writer.WriteBits(0, 1);
            for (var i = 0; i < LpcOrder; i++)
                writer.WriteBits(0, effectiveBits);
            writer.WriteBits(reservedPrecision, 4);
            writer.WriteBits(0, 5);
            for (var i = 0; i < LpcOrder; i++)
                writer.WriteBits(2, Math.Min(16, reservedPrecision + 1));
            writer.WriteBits(0, 2);
            writer.WriteBits(0, 4);
            writer.WriteBits(0, 4);
            return;
        }

        // 短于预测阶数的尾部帧无法用 fixed/LPC 表达，真实编码器会退回 verbatim。
        var requiredOrder = Kind switch
        {
            SubframeKind.Fixed1 => 1,
            SubframeKind.Fixed2 => 2,
            SubframeKind.Fixed3 => 3,
            SubframeKind.Fixed4 => 4,
            SubframeKind.Lpc => LpcOrder,
            _ => 0,
        };
        var effectiveKind = block > requiredOrder ? Kind : SubframeKind.Verbatim;
        int type;
        switch (effectiveKind)
        {
            case SubframeKind.Constant:
                type = 0;
                break;
            case SubframeKind.Verbatim:
                type = 1;
                break;
            case SubframeKind.Fixed0:
                type = 8;
                break;
            case SubframeKind.Fixed1:
                type = 9;
                break;
            case SubframeKind.Fixed2:
                type = 10;
                break;
            case SubframeKind.Fixed3:
                type = 11;
                break;
            case SubframeKind.Fixed4:
                type = 12;
                break;
            default:
                type = 32 + LpcOrder - 1;
                break;
        }
        writer.WriteBits(type, 6);

        if (WastedBits > 0)
        {
            writer.WriteBits(1, 1);
            // unary：WastedBits-1 个 0 后接 1
            writer.WriteBits(1, WastedBits);
        }
        else
        {
            writer.WriteBits(0, 1);
        }

        var shifted = new int[block];
        for (var i = 0; i < block; i++)
            shifted[i] = values[startFrame + i] >> WastedBits;

        switch (effectiveKind)
        {
            case SubframeKind.Constant:
                WriteConstant(writer, shifted, block, effectiveBits);
                break;
            case SubframeKind.Verbatim:
                for (var i = 0; i < block; i++)
                    writer.WriteBits(shifted[i], effectiveBits);
                break;
            default:
                WritePredictive(writer, shifted, block, effectiveBits);
                break;
        }
    }

    /// <summary>constant 子帧只能有一个采样值；测试里取块首样本作为常量。</summary>
    private void WriteConstant(BitWriter writer, int[] values, int block, int bits)
    {
        writer.WriteBits(values[0], bits);
    }

    private void WritePredictive(BitWriter writer, int[] values, int block, int bits)
    {
        var order = Kind switch
        {
            SubframeKind.Fixed0 => 0,
            SubframeKind.Fixed1 => 1,
            SubframeKind.Fixed2 => 2,
            SubframeKind.Fixed3 => 3,
            SubframeKind.Fixed4 => 4,
            _ => LpcOrder,
        };

        for (var i = 0; i < order; i++)
            writer.WriteBits(values[i], bits);

        if (Kind == SubframeKind.Lpc)
        {
            // 系数固定为 (2,-1,0,...) + shift 0：二阶差分预测器，残差必然很小。
            var precision = 3; // 4-bit 字段，实际位宽 = precision+1
            writer.WriteBits(precision, 4);
            writer.WriteBits(0, 5); // shift
            writer.WriteBits(2, precision + 1);
            writer.WriteBits(-1 & ((1 << (precision + 1)) - 1), precision + 1);
            for (var i = 2; i < order; i++)
                writer.WriteBits(0, precision + 1);
        }

        var residual = new int[block - order];
        for (var i = order; i < block; i++)
        {
            long prediction = Kind == SubframeKind.Lpc
                ? values[i - 1] * 2L - values[i - 2]
                : order switch
                {
                    0 => 0,
                    1 => values[i - 1],
                    2 => 2L * values[i - 1] - values[i - 2],
                    3 => 3L * values[i - 1] - 3L * values[i - 2] + values[i - 3],
                    _ => 4L * values[i - 1] - 6L * values[i - 2] + 4L * values[i - 3] -
                         values[i - 4],
                };
            residual[i - order] = (int)(values[i] - prediction);
        }

        WriteResidual(writer, residual, order, block);
    }

    private void WriteResidual(BitWriter writer, int[] residual, int order, int block)
    {
        writer.WriteBits(0, 2); // 4-bit Rice
        var partitionOrder = ResidualPartitionOrder;
        writer.WriteBits(partitionOrder, 4);

        var partitions = 1 << partitionOrder;
        var partitionSamples = block >> partitionOrder;
        var read = 0;
        for (var partition = 0; partition < partitions; partition++)
        {
            var count = partition == 0 ? partitionSamples - order : partitionSamples;
            if (Residual == ResidualKind.Escape)
            {
                var peak = 0;
                for (var i = 0; i < count; i++)
                    peak = Math.Max(peak, Math.Abs(residual[read + i]));
                // 有符号原始值需要额外一位符号位，否则负峰值会被截断。
                var rawBits = peak == 0 ? 0 : BitsNeeded(peak) + 1;
                if (rawBits == 0)
                {
                    writer.WriteBits(15, 4); // escape
                    writer.WriteBits(0, 5); // 0 位：全部为 0
                }
                else
                {
                    writer.WriteBits(15, 4);
                    writer.WriteBits(rawBits, 5);
                    for (var i = 0; i < count; i++)
                        writer.WriteBits(residual[read + i], rawBits);
                }
                read += count;
                continue;
            }

            var maxFolded = 0;
            for (var i = 0; i < count; i++)
                maxFolded = Math.Max(maxFolded, Fold(residual[read + i]));
            var parameter = Math.Clamp(BitsNeeded(maxFolded) - 1, 0, 14);
            writer.WriteBits(parameter, 4);
            for (var i = 0; i < count; i++)
            {
                var folded = Fold(residual[read + i]);
                var quotient = folded >> parameter;
                for (var u = 0; u < quotient; u++)
                    writer.WriteBits(0, 1);
                writer.WriteBits(1, 1); // 一元码终止位
                if (parameter > 0)
                    writer.WriteBits(folded & ((1 << parameter) - 1), parameter);
            }
            read += count;
        }
    }

    private static int Fold(int value) => (value << 1) ^ (value >> 31);

    private static int BitsNeeded(int value)
    {
        var bits = 0;
        while (value > 0)
        {
            bits++;
            value >>= 1;
        }
        return bits;
    }

    private void WriteUtf8Number(BitWriter writer, long value)
    {
        if (value < 0x80)
        {
            writer.WriteBits((int)value, 8);
            return;
        }

        // FLAC 的 UTF-8 风格编号：n 个续字节时，前导字节携带 6-n 位有效载荷，
        // 总 payload 是 (6-n) + 6n = 6 + 5n 位，阈值必须按这个位数算。
        var continuationBytes = 1;
        while (continuationBytes < 6 && value >= (1L << (6 + 5 * continuationBytes)))
            continuationBytes++;

        var leadPayloadBits = 6 - continuationBytes;
        // 前导字节只有 6-n 位能装 payload，这里按位宽掩码，不依赖阈值算术刚好不自溢出。
        var lead = ((0xFF << (leadPayloadBits + 1)) & 0xFF) |
                   ((int)(value >> (6 * continuationBytes)) & ((1 << leadPayloadBits) - 1));
        writer.WriteBits(lead, 8);
        for (var i = continuationBytes - 1; i >= 0; i--)
            writer.WriteBits(0x80 | (int)((value >> (6 * i)) & 0x3F), 8);
    }

    private static byte Crc8(ReadOnlySpan<byte> data)
    {
        byte crc = 0;
        foreach (var value in data)
        {
            crc ^= value;
            for (var bit = 0; bit < 8; bit++)
                crc = (crc & 0x80) != 0 ? (byte)((crc << 1) ^ 0x07) : (byte)(crc << 1);
        }
        return crc;
    }

    private static ushort Crc16(ReadOnlySpan<byte> data)
    {
        ushort crc = 0;
        foreach (var value in data)
        {
            crc ^= (ushort)(value << 8);
            for (var bit = 0; bit < 8; bit++)
                crc = (crc & 0x8000) != 0
                    ? (ushort)((crc << 1) ^ 0x8005)
                    : (ushort)(crc << 1);
        }
        return crc;
    }

    internal sealed class BitWriter
    {
        private readonly List<byte> _bytes = new();
        private int _partial;
        private int _partialBits;

        internal int BytePosition => _bytes.Count;

        internal void WriteBits(int value, int count)
        {
            for (var bit = count - 1; bit >= 0; bit--)
            {
                _partial = (_partial << 1) | ((value >> bit) & 1);
                if (++_partialBits == 8)
                {
                    _bytes.Add((byte)_partial);
                    _partial = 0;
                    _partialBits = 0;
                }
            }
        }

        internal void WriteByteValue(byte value) => WriteBits(value, 8);

        internal void WriteBytes(ReadOnlySpan<byte> data)
        {
            foreach (var value in data)
                WriteByteValue(value);
        }

        internal void AlignToByte()
        {
            while (_partialBits != 0)
                WriteBits(0, 1);
        }

        internal byte[] Slice(int startByte, int endByte) => _bytes.GetRange(startByte, endByte - startByte).ToArray();

        internal byte[] ToArray()
        {
            var copy = new byte[_bytes.Count];
            _bytes.CopyTo(copy);
            return copy;
        }
    }
}
