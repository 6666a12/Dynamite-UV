namespace DynamiteUniverse.Shared.Audio;

/// <summary>Validates one complete FLAC frame without retaining decoded PCM.</summary>
internal static class FlacFrameWalker
{
    internal static int ReadBlockSize(ref FlacBitReader reader, FlacStreamInfo streamInfo,
        FlacDecoderOptions options)
    {
        var frameStartByte = reader.BitPosition >> 3;
        var header = FlacFrameDecoder.ReadHeader(ref reader, streamInfo, options, frameStartByte);
        var channels = header.ChannelAssignment <= 7 ? header.Channels : 2;
        for (var channel = 0; channel < channels; channel++)
        {
            var side = header.ChannelAssignment switch
            {
                8 => channel == 1,
                9 => channel == 0,
                10 => channel == 1,
                _ => false,
            };
            SkipSubframe(ref reader, header.BlockSize, header.BitsPerSample + (side ? 1 : 0));
        }

        reader.AlignToByte();
        var crcEndByte = (int)(reader.BitPosition >> 3);
        var expected = (ushort)reader.ReadBits(16);
        var actual = FlacCrc.Compute16(reader.SliceBytes((int)frameStartByte,
            crcEndByte - (int)frameStartByte));
        if (actual != expected)
            throw new FlacDecodeException(
                $"FLAC frame CRC-16 mismatch at byte offset {frameStartByte} " +
                $"(expected 0x{expected:X4}, actual 0x{actual:X4}).");
        return header.BlockSize;
    }

    private static void SkipSubframe(ref FlacBitReader reader, int blockSize, int bitsPerSample)
    {
        if (reader.ReadBits(1) != 0)
            throw new FlacDecodeException("FLAC subframe padding bit is not zero.");
        var type = (int)reader.ReadBits(6);
        var wastedBits = 0;
        if (reader.ReadBits(1) != 0)
            wastedBits = reader.ReadUnary(bitsPerSample - 1) + 1;
        var effectiveBits = bitsPerSample - wastedBits;
        if (effectiveBits <= 0)
            throw new FlacDecodeException("FLAC subframe wasted bits exceed its bit depth.");

        switch (type)
        {
            case 0:
                reader.SkipBits(effectiveBits);
                return;
            case 1:
                reader.SkipBits((long)blockSize * effectiveBits);
                return;
            case >= 8 and <= 12:
                SkipWarmup(ref reader, blockSize, type - 8, effectiveBits);
                SkipResidual(ref reader, blockSize, type - 8);
                return;
            case >= 32 and <= 63:
                var order = type - 31;
                SkipWarmup(ref reader, blockSize, order, effectiveBits);
                var precision = (int)reader.ReadBits(4);
                if (precision == 15)
                    throw new FlacDecodeException("FLAC LPC coefficient precision 15 is reserved.");
                var shift = reader.ReadSignedBits(5);
                if (shift < 0)
                    throw new FlacDecodeException("FLAC LPC negative shift is not supported.");
                reader.SkipBits((long)order * (precision + 1));
                SkipResidual(ref reader, blockSize, order);
                return;
            default:
                throw new FlacDecodeException($"FLAC subframe type 0x{type:X2} is reserved.");
        }
    }

    private static void SkipWarmup(ref FlacBitReader reader, int blockSize, int order,
        int bitsPerSample)
    {
        if (order > blockSize)
            throw new FlacDecodeException("FLAC predictor order exceeds the frame block size.");
        reader.SkipBits((long)order * bitsPerSample);
    }

    private static void SkipResidual(ref FlacBitReader reader, int blockSize, int order)
    {
        var method = (int)reader.ReadBits(2);
        if (method > 1)
            throw new FlacDecodeException("FLAC residual coding method is reserved.");
        var partitionOrder = (int)reader.ReadBits(4);
        var partitions = 1 << partitionOrder;
        if (blockSize % partitions != 0)
            throw new FlacDecodeException("FLAC residual partitions do not divide the block size.");
        var partitionSamples = blockSize >> partitionOrder;
        if (partitionOrder > 0 && partitionSamples < order)
            throw new FlacDecodeException("FLAC residual first partition is shorter than the predictor order.");

        for (var partition = 0; partition < partitions; partition++)
        {
            var count = partition == 0 ? partitionSamples - order : partitionSamples;
            var parameter = (int)reader.ReadBits(method == 0 ? 4 : 5);
            var escape = method == 0 ? 15 : 31;
            if (parameter == escape)
            {
                var rawBits = (int)reader.ReadBits(5);
                reader.SkipBits((long)count * rawBits);
                continue;
            }

            var maxQuotient = (int)(uint.MaxValue >> (parameter + 1));
            for (var sample = 0; sample < count; sample++)
            {
                reader.ReadUnary(maxQuotient);
                reader.SkipBits(parameter);
            }
        }
    }
}
