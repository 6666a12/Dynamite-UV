using System.Numerics;

namespace DynamiteUniverse.Shared.Audio;

/// <summary>
/// FLAC 位流读取器（MSB-first）。所有越界、非法 UTF-8 编号和超长一元码都抛
/// <see cref="FlacDecodeException"/>，不会静默读到垃圾数据。
/// </summary>
internal ref struct FlacBitReader
{
    private readonly ReadOnlySpan<byte> _data;
    private int _nextByte;
    private ulong _buffer;
    private int _bitsInBuffer;

    internal FlacBitReader(ReadOnlySpan<byte> data)
    {
        _data = data;
        _nextByte = 0;
        _buffer = 0;
        _bitsInBuffer = 0;
    }

    /// <summary>已消耗的绝对位偏移。</summary>
    internal readonly long BitPosition => (long)_nextByte * 8 - _bitsInBuffer;

    internal readonly long BitsRemaining => ((long)_data.Length - _nextByte) * 8 + _bitsInBuffer;

    private readonly FlacDecodeException Exhausted() =>
        new("FLAC 位流在数据结束前被截断。");

    private bool TryEnsure(int bits)
    {
        while (_bitsInBuffer < bits)
        {
            if (_nextByte >= _data.Length)
                return false;
            _buffer = (_buffer << 8) | _data[_nextByte++];
            _bitsInBuffer += 8;
        }
        return true;
    }

    /// <summary>读取 <paramref name="count"/> 位（0–32）无符号整数。</summary>
    internal uint ReadBits(int count)
    {
        if (count is < 0 or > 32)
            throw new FlacDecodeException($"非法的位读取宽度 {count}。");
        if (count == 0)
            return 0;
        if (!TryEnsure(count))
            throw Exhausted();
        _bitsInBuffer -= count;
        return (uint)((_buffer >> _bitsInBuffer) & ((1UL << count) - 1));
    }

    /// <summary>读取 <paramref name="count"/> 位（0–32）二补码有符号整数。</summary>
    internal int ReadSignedBits(int count)
    {
        if (count is < 0 or > 32)
            throw new FlacDecodeException($"非法的位读取宽度 {count}。");
        if (count == 0)
            return 0;
        if (count == 32)
            return (int)ReadBits(32);
        var raw = ReadBits(count);
        var signBit = 1u << (count - 1);
        return (raw & signBit) != 0
            ? (int)(raw | ~((1u << count) - 1))
            : (int)raw;
    }

    /// <summary>
    /// 读取一元码（连续的 0 后接一个 1），返回 0 的个数。
    /// <paramref name="maxValue"/> 是硬上限：恶意位流不能靠超长一元码拖住解码。
    /// </summary>
    internal int ReadUnary(int maxValue)
    {
        var count = 0;
        while (true)
        {
            if (!TryEnsure(1))
                throw Exhausted();
            var available = Math.Min(_bitsInBuffer, 8);
            var window = (uint)((_buffer >> (_bitsInBuffer - available)) &
                                ((1UL << available) - 1));
            if (window == 0)
            {
                count += available;
                _bitsInBuffer -= available;
                if (count > maxValue)
                    throw new FlacDecodeException("FLAC 一元码超出允许长度。");
                continue;
            }

            var zeros = BitOperations.LeadingZeroCount(window) - (32 - available);
            count += zeros;
            if (count > maxValue)
                throw new FlacDecodeException("FLAC 一元码超出允许长度。");
            _bitsInBuffer -= zeros + 1;
            return count;
        }
    }

    /// <summary>跳过任意位数的内容（元数据块），不逐位展开。</summary>
    internal void SkipBits(long bits)
    {
        if (bits < 0)
            throw new FlacDecodeException("FLAC 位跳过长度为负。");
        if (bits > BitsRemaining)
            throw Exhausted();
        if (bits <= _bitsInBuffer)
        {
            _bitsInBuffer -= (int)bits;
            return;
        }

        var rest = bits - _bitsInBuffer;
        _bitsInBuffer = 0;
        _nextByte += (int)(rest / 8);
        var leftover = (int)(rest % 8);
        if (leftover > 0)
        {
            if (!TryEnsure(leftover))
                throw Exhausted();
            _bitsInBuffer -= leftover;
        }
    }

    internal void AlignToByte()
    {
        var remainder = (int)(BitPosition & 7);
        if (remainder != 0)
            SkipBits(8 - remainder);
    }

    /// <summary>把一个字节对齐区域交给 CRC 或其它按字节处理的代码。</summary>
    internal readonly ReadOnlySpan<byte> SliceBytes(int startByte, int byteCount)
    {
        if (startByte < 0 || byteCount < 0 || startByte + byteCount > _data.Length)
            throw new FlacDecodeException("FLAC 字节区间越界。");
        return _data.Slice(startByte, byteCount);
    }

    /// <summary>FLAC 帧号/样本号使用 UTF-8 风格的变长编码（最长 7 字节）。</summary>
    internal long ReadUtf8CodedNumber()
    {
        var first = (int)ReadBits(8);
        int continuationBytes;
        long value;
        if ((first & 0x80) == 0x00)
        {
            continuationBytes = 0;
            value = first;
        }
        else if ((first & 0xE0) == 0xC0)
        {
            continuationBytes = 1;
            value = first & 0x1F;
        }
        else if ((first & 0xF0) == 0xE0)
        {
            continuationBytes = 2;
            value = first & 0x0F;
        }
        else if ((first & 0xF8) == 0xF0)
        {
            continuationBytes = 3;
            value = first & 0x07;
        }
        else if ((first & 0xFC) == 0xF8)
        {
            continuationBytes = 4;
            value = first & 0x03;
        }
        else if ((first & 0xFE) == 0xFC)
        {
            continuationBytes = 5;
            value = first & 0x01;
        }
        else if (first == 0xFE)
        {
            continuationBytes = 6;
            value = 0;
        }
        else
        {
            throw new FlacDecodeException("FLAC 帧号/样本号前缀字节非法。");
        }

        for (var i = 0; i < continuationBytes; i++)
        {
            var next = (int)ReadBits(8);
            if ((next & 0xC0) != 0x80)
                throw new FlacDecodeException("FLAC 帧号/样本号续字节非法。");
            value = (value << 6) | (long)(next & 0x3F);
        }
        return value;
    }
}
