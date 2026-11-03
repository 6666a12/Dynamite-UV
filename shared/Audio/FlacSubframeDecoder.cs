namespace DynamiteUniverse.Shared.Audio;

/// <summary>
/// 单个子帧的解码（constant / verbatim / fixed / LPC）。解码到调用方提供的
/// <c>int[]</c> 目标缓冲区，residual 阶段才使用 Rice 编码。
/// </summary>
internal static class FlacSubframeDecoder
{
    private const int SubframeTypeConstant = 0;
    private const int SubframeTypeVerbatim = 1;
    private const int SubframeTypeFixedBase = 8;
    private const int SubframeTypeLpcBase = 32;

    /// <summary>LPC 阶数与 fixed 阶数的实现上限，同时限制临时栈/堆分配。</summary>
    private const int MaxLpcOrder = 32;
    private const int MaxFixedOrder = 4;
    private const int MaxRicePartitionOrder = 15;

    /// <summary>
    /// 解码一个子帧。<paramref name="blockSize"/> 与 <paramref name="bitsPerSample"/>
    /// 由帧头给出；<paramref name="target"/> 至少要有 blockSize 个元素。
    /// </summary>
    internal static void Decode(ref FlacBitReader reader, int blockSize, int bitsPerSample,
        int[] target, FlacResidualScratch scratch)
    {
        if (blockSize <= 0 || target.Length < blockSize)
            throw new FlacDecodeException("FLAC 子帧目标缓冲区不足。");

        if (reader.ReadBits(1) != 0)
            throw new FlacDecodeException("FLAC 子帧首位的 0 填充位非 0。");
        var type = (int)reader.ReadBits(6);
        var wastedBits = 0;
        if (reader.ReadBits(1) != 0)
        {
            // 浪费位标记 + unary-1 计数
            wastedBits = reader.ReadUnary(31) + 1;
            if (wastedBits >= bitsPerSample)
                throw new FlacDecodeException(
                    $"FLAC 子帧浪费位 {wastedBits} 不小于位深 {bitsPerSample}。");
        }

        var effectiveBits = bitsPerSample - wastedBits;
        switch (type)
        {
            case SubframeTypeConstant:
            {
                var value = reader.ReadSignedBits(effectiveBits);
                Array.Fill(target, value, 0, blockSize);
                break;
            }
            case SubframeTypeVerbatim:
            {
                for (var i = 0; i < blockSize; i++)
                    target[i] = reader.ReadSignedBits(effectiveBits);
                break;
            }
            case >= SubframeTypeFixedBase and <= SubframeTypeFixedBase + MaxFixedOrder:
                DecodeFixed(ref reader, type - SubframeTypeFixedBase, blockSize, effectiveBits,
                    target, scratch);
                break;
            case >= SubframeTypeLpcBase and <= SubframeTypeLpcBase + MaxLpcOrder - 1:
                DecodeLpc(ref reader, type - SubframeTypeLpcBase + 1, blockSize, effectiveBits,
                    target, scratch);
                break;
            default:
                throw new FlacDecodeException(
                    $"FLAC 子帧类型 0x{type:X2} 为保留/不支持值。");
        }

        if (wastedBits > 0)
        {
            for (var i = 0; i < blockSize; i++)
                target[i] = (int)((uint)target[i] << wastedBits);
        }
    }

    private static void DecodeFixed(ref FlacBitReader reader, int order, int blockSize,
        int bitsPerSample, int[] target, FlacResidualScratch scratch)
    {
        if (order > MaxFixedOrder)
            throw new FlacDecodeException($"FLAC fixed 子帧阶数 {order} 非法。");
        if (order > blockSize)
            throw new FlacDecodeException("FLAC fixed 子帧阶数大于块大小。");

        for (var i = 0; i < order; i++)
            target[i] = reader.ReadSignedBits(bitsPerSample);

        if (!DecodeResidual(ref reader, blockSize, order, scratch))
            throw new FlacDecodeException("FLAC fixed 子帧的 residual 编码方式非法。");

        var residual = scratch.Residual;
        switch (order)
        {
            case 0:
                Array.Copy(residual, 0, target, 0, blockSize);
                break;
            case 1:
                for (var i = 1; i < blockSize; i++)
                    target[i] = unchecked(target[i - 1] + residual[i - 1]);
                break;
            case 2:
                for (var i = 2; i < blockSize; i++)
                    target[i] = unchecked(2 * target[i - 1] - target[i - 2] + residual[i - 2]);
                break;
            case 3:
                for (var i = 3; i < blockSize; i++)
                    target[i] = unchecked(3 * target[i - 1] - 3 * target[i - 2] +
                                          target[i - 3] + residual[i - 3]);
                break;
            case 4:
                for (var i = 4; i < blockSize; i++)
                    target[i] = unchecked(4 * target[i - 1] - 6 * target[i - 2] +
                                          4 * target[i - 3] - target[i - 4] + residual[i - 4]);
                break;
        }
    }

    private static void DecodeLpc(ref FlacBitReader reader, int order, int blockSize,
        int bitsPerSample, int[] target, FlacResidualScratch scratch)
    {
        if (order is < 1 or > MaxLpcOrder)
            throw new FlacDecodeException($"FLAC LPC 阶数 {order} 超出支持范围 1–{MaxLpcOrder}。");
        if (order > blockSize)
            throw new FlacDecodeException("FLAC LPC 阶数大于块大小。");

        for (var i = 0; i < order; i++)
            target[i] = reader.ReadSignedBits(bitsPerSample);

        var coefficientPrecision = (int)reader.ReadBits(4);
        if (coefficientPrecision == 15)
            throw new FlacDecodeException("FLAC LPC 系数精度 15 为非法值。");
        var coefficientBits = coefficientPrecision + 1;
        var shift = reader.ReadSignedBits(5);
        if (shift < 0)
            throw new FlacDecodeException($"FLAC LPC 负移位 {shift} 不受支持。");

        Span<long> coefficients = stackalloc long[MaxLpcOrder];
        for (var i = 0; i < order; i++)
            coefficients[i] = reader.ReadSignedBits(coefficientBits);

        if (!DecodeResidual(ref reader, blockSize, order, scratch))
            throw new FlacDecodeException("FLAC LPC 子帧的 residual 编码方式非法。");

        var residual = scratch.Residual;
        for (var i = order; i < blockSize; i++)
        {
            long sum = 0;
            for (var j = 0; j < order; j++)
                sum += coefficients[j] * target[i - 1 - j];
            target[i] = unchecked(residual[i - order] + (int)(sum >> shift));
        }
    }

    /// <summary>解码 residual，返回 false 表示编码方式为保留值。</summary>
    private static bool DecodeResidual(ref FlacBitReader reader, int blockSize, int order,
        FlacResidualScratch scratch)
    {
        var method = (int)reader.ReadBits(2);
        if (method > 1)
            return false;

        if (scratch.Residual.Length < blockSize)
            throw new FlacDecodeException("FLAC residual 缓冲区不足。");

        var partitionOrder = (int)reader.ReadBits(4);
        if (partitionOrder > MaxRicePartitionOrder)
            throw new FlacDecodeException(
                $"FLAC residual 分区阶数 {partitionOrder} 超出支持范围 0–{MaxRicePartitionOrder}。");
        var partitions = 1 << partitionOrder;
        if (blockSize % partitions != 0)
            throw new FlacDecodeException(
                $"FLAC residual 分区数 {partitions} 不能整除块大小 {blockSize}。");
        var partitionSamples = blockSize >> partitionOrder;
        if (partitionSamples < order && partitionOrder > 0)
            throw new FlacDecodeException(
                "FLAC residual 首分区样本数小于预测阶数。");

        var residual = scratch.Residual;
        var writeIndex = 0;
        for (var partition = 0; partition < partitions; partition++)
        {
            var count = partition == 0 ? partitionSamples - order : partitionSamples;
            if (count < 0)
                throw new FlacDecodeException("FLAC residual 首个分区样本数为负。");

            var parameterBits = method == 0 ? 4 : 5;
            var riceParameter = (int)reader.ReadBits(parameterBits);
            var escape = method == 0 ? 15 : 31;
            if (riceParameter == escape)
            {
                var rawBits = (int)reader.ReadBits(5);
                for (var i = 0; i < count; i++)
                    residual[writeIndex++] = rawBits == 0 ? 0 : reader.ReadSignedBits(rawBits);
                continue;
            }

            for (var i = 0; i < count; i++)
                residual[writeIndex++] = ReadRiceSigned(ref reader, riceParameter);
        }

        return true;
    }

    /// <summary>Rice 编码：unary 商 + <paramref name="parameter"/> 位余数，再折回有符号值。</summary>
    private static int ReadRiceSigned(ref FlacBitReader reader, int parameter)
    {
        // 一元商的上限按“折叠值仍能落在 uint 内”推导；同时 unary 每级至少消耗 1 个输入位，
        // 因此总工作量天然被输入长度限制，不会被短输入拖成无限循环。
        var maxQuotient = (int)(uint.MaxValue >> (parameter + 1));
        var quotient = reader.ReadUnary(maxQuotient);
        var remainder = parameter == 0 ? 0u : reader.ReadBits(parameter);
        var folded = ((long)quotient << parameter) | remainder;
        // FLAC 使用“折叠”映射：偶数 → 非负，奇数 → 负数
        var value = (folded >> 1) ^ -(folded & 1);
        if (value is < int.MinValue or > int.MaxValue)
            throw new FlacDecodeException("FLAC Rice 残差超出 32 位范围。");
        return (int)value;
    }
}

/// <summary>可复用的 residual 缓冲区，避免每帧分配。</summary>
internal sealed class FlacResidualScratch
{
    internal int[] Residual { get; private set; } = Array.Empty<int>();

    internal void EnsureCapacity(int frames)
    {
        if (frames <= 0)
            throw new FlacDecodeException("FLAC 块大小为 0。");
        if (Residual.Length < frames)
            Residual = new int[frames];
    }
}
