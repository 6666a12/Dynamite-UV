using System.Buffers.Binary;
using System.Text;

namespace DynamiteUniverse.Shared.Chart.V2;

/// <summary>
/// In-memory, code-generated audio fixtures for the built-in probe regression tests. Everything is
/// synthesized here so no third-party or real-world audio assets enter the repository.
/// </summary>
internal static class AudioProbeFixtures
{
    /// <summary>RIFF/WAVE, 16-bit mono PCM with a silent payload.</summary>
    internal static byte[] Wave(int sampleRate = 44100, double seconds = 2.0)
    {
        var frames = (int)Math.Round(sampleRate * seconds);
        var dataBytes = frames * 2;
        var bytes = new byte[44 + dataBytes];
        var span = bytes.AsSpan();
        Encoding.ASCII.GetBytes("RIFF").CopyTo(span);
        BinaryPrimitives.WriteUInt32LittleEndian(span[4..], (uint)(36 + dataBytes));
        Encoding.ASCII.GetBytes("WAVEfmt ").CopyTo(span[8..]);
        BinaryPrimitives.WriteUInt32LittleEndian(span[16..], 16);
        BinaryPrimitives.WriteUInt16LittleEndian(span[20..], 1);
        BinaryPrimitives.WriteUInt16LittleEndian(span[22..], 1);
        BinaryPrimitives.WriteUInt32LittleEndian(span[24..], (uint)sampleRate);
        BinaryPrimitives.WriteUInt32LittleEndian(span[28..], (uint)(sampleRate * 2));
        BinaryPrimitives.WriteUInt16LittleEndian(span[32..], 2);
        BinaryPrimitives.WriteUInt16LittleEndian(span[34..], 16);
        Encoding.ASCII.GetBytes("data").CopyTo(span[36..]);
        BinaryPrimitives.WriteUInt32LittleEndian(span[40..], (uint)dataBytes);
        return bytes;
    }

    /// <summary>Frame length of the MPEG-1 Layer III frames produced by <see cref="Mp3"/>.</summary>
    internal const int Mp3FrameBytes = 144 * 128 * 1000 / 44100;

    /// <summary>
    /// MPEG-1 Layer III (44.1 kHz, 128 kbit/s). Only frame headers are meaningful; the payload is
    /// zero because the parser validates frame structure rather than decoding samples.
    /// </summary>
    internal static byte[] Mp3(int frameCount, bool mono = false)
    {
        if (frameCount is < 2 or > 5000)
            throw new ArgumentOutOfRangeException(nameof(frameCount));
        var frameBytes = Mp3FrameBytes;
        var bytes = new byte[frameBytes * frameCount];
        for (var frame = 0; frame < frameCount; frame++)
        {
            var offset = frame * frameBytes;
            bytes[offset] = 0xFF;
            bytes[offset + 1] = 0xFB;
            bytes[offset + 2] = 0x90;
            bytes[offset + 3] = mono ? (byte)0xC0 : (byte)0x00;
        }
        return bytes;
    }

    /// <summary>Native FLAC: STREAMINFO plus uniformly sized audio frames with valid header CRCs.</summary>
    internal static byte[] Flac(int frameCount, int blockSize = 1152, int sampleRate = 44100,
        int channels = 2, int bitsPerSample = 16)
    {
        if (frameCount is < 1 or > 120)
            throw new ArgumentOutOfRangeException(nameof(frameCount));
        var streamInfo = new byte[34];
        BinaryPrimitives.WriteUInt16BigEndian(streamInfo, (ushort)blockSize);
        BinaryPrimitives.WriteUInt16BigEndian(streamInfo.AsSpan(2), (ushort)blockSize);
        var packed = ((ulong)sampleRate << 44) | ((ulong)(uint)(channels - 1) << 41) |
            ((ulong)(uint)(bitsPerSample - 1) << 36) | ((ulong)(uint)(frameCount * blockSize) & 0x0F_FFFF_FFFFUL);
        BinaryPrimitives.WriteUInt64BigEndian(streamInfo.AsSpan(10), packed);

        var blocks = new List<byte>();
        blocks.AddRange("fLaC"u8.ToArray());
        blocks.Add(0x80);
        blocks.Add(0);
        blocks.Add(0);
        blocks.Add(34);
        blocks.AddRange(streamInfo);
        for (var frame = 0; frame < frameCount; frame++)
            blocks.AddRange(FlacFrame(frame, blockSize, sampleRate, channels, bitsPerSample));
        return [.. blocks];
    }

    private static byte[] FlacFrame(int index, int blockSize, int sampleRate, int channels,
        int bitsPerSample)
    {
        var blockCode = blockSize switch
        {
            192 => 1,
            576 => 2,
            1152 => 3,
            2304 => 4,
            4608 => 5,
            256 => 8,
            512 => 9,
            _ => throw new ArgumentOutOfRangeException(nameof(blockSize)),
        };
        var rateCode = sampleRate switch
        {
            44100 => 9,
            48000 => 10,
            8000 => 4,
            16000 => 5,
            22050 => 6,
            24000 => 7,
            32000 => 8,
            96000 => 11,
            _ => throw new ArgumentOutOfRangeException(nameof(sampleRate)),
        };
        var sampleSizeCode = bitsPerSample switch
        {
            8 => 1,
            16 => 4,
            24 => 6,
            _ => throw new ArgumentOutOfRangeException(nameof(bitsPerSample)),
        };
        var channelCode = channels switch
        {
            2 => 1,
            1 => 0,
            _ => throw new ArgumentOutOfRangeException(nameof(channels)),
        };
        var header = new List<byte>
        {
            0xFF,
            0xF8,
            (byte)((blockCode << 4) | rateCode),
            (byte)((channelCode << 4) | (sampleSizeCode << 1)),
            (byte)index,
        };
        if (index >= 0x80)
            throw new ArgumentOutOfRangeException(nameof(index), "frame index must be UTF-8 single byte");
        var frame = new List<byte>(header) { Crc8(header) };
        var partial = 0;
        var partialBits = 0;
        for (var channel = 0; channel < channels; channel++)
        {
            WriteBits(0, 8);
            WriteBits(0, bitsPerSample);
        }
        while (partialBits != 0)
            WriteBits(0, 1);
        var crc = Crc16(frame);
        frame.Add((byte)(crc >> 8));
        frame.Add((byte)crc);
        return [.. frame];

        void WriteBits(int value, int count)
        {
            for (var bit = count - 1; bit >= 0; bit--)
            {
                partial = (partial << 1) | ((value >> bit) & 1);
                if (++partialBits != 8) continue;
                frame.Add((byte)partial);
                partial = 0;
                partialBits = 0;
            }
        }
    }

    /// <summary>One Ogg Vorbis logical stream: BOS page with identification header, then data pages.</summary>
    internal static byte[] OggVorbis(int sampleRate = 44100, int granule = 110250)
    {
        const uint serial = 0x5EED_1234;
        var pages = new List<byte>();
        var identification = new List<byte> { 0x01 };
        identification.AddRange("vorbis"u8.ToArray());
        identification.AddRange(BitConverter.GetBytes(0u));
        identification.Add(2);
        identification.AddRange(BitConverter.GetBytes((uint)sampleRate));
        identification.AddRange(BitConverter.GetBytes(0));
        identification.AddRange(BitConverter.GetBytes(0));
        identification.AddRange(BitConverter.GetBytes(0));
        identification.Add(0xB8);
        identification.Add(0x01);
        if (identification.Count != 30)
            throw new InvalidOperationException("Vorbis identification header must be 30 bytes");
        pages.AddRange(OggPage(0x02, 0, serial, 0, [.. identification]));
        pages.AddRange(OggPage(0x00, 0, serial, 1, Build(0x03, "vorbis")));
        pages.AddRange(OggPage(0x00, granule / 2, serial, 2, Build(0x05, "vorbis")));
        pages.AddRange(OggPage(0x04, granule, serial, 3, Build(0x81, "audio")));
        return [.. pages];

        static byte[] Build(int type, string prefix)
        {
            var payload = new byte[64];
            payload[0] = (byte)type;
            Encoding.ASCII.GetBytes(prefix).CopyTo(payload.AsSpan(1));
            return payload;
        }
    }

    /// <summary>An Ogg page whose capture pattern and CRC are valid but whose codec is Opus.</summary>
    internal static byte[] OggOpus(int sampleRate = 48000)
    {
        const uint serial = 0x0BAD_0905;
        var head = new List<byte>();
        head.AddRange("OpusHead"u8.ToArray());
        head.Add(1);
        head.Add(2);
        head.AddRange(BitConverter.GetBytes((ushort)0));
        head.AddRange(BitConverter.GetBytes((uint)sampleRate));
        head.AddRange(BitConverter.GetBytes((ushort)0));
        head.Add(0);
        var pages = new List<byte>();
        pages.AddRange(OggPage(0x02, 0, serial, 0, [.. head]));
        pages.AddRange(OggPage(0x04, 96000, serial, 1, new byte[32]));
        return [.. pages];
    }

    private static byte[] OggPage(byte flags, long granule, uint serial, uint sequence,
        byte[] payload)
    {
        var segments = (byte)((payload.Length + 254) / 255);
        var page = new byte[27 + segments + payload.Length];
        var span = page.AsSpan();
        Encoding.ASCII.GetBytes("OggS").CopyTo(span);
        span[4] = 0;
        span[5] = flags;
        BinaryPrimitives.WriteInt64LittleEndian(span[6..], granule);
        BinaryPrimitives.WriteUInt32LittleEndian(span[14..], serial);
        BinaryPrimitives.WriteUInt32LittleEndian(span[18..], sequence);
        span[26] = segments;
        var copied = 0;
        for (var i = 0; i < segments; i++)
        {
            var take = Math.Min(255, payload.Length - copied);
            span[27 + i] = (byte)take;
            payload.AsSpan(copied, take).CopyTo(span[(27 + segments + copied)..]);
            copied += take;
        }
        BinaryPrimitives.WriteUInt32LittleEndian(span[22..], OggCrc(page));
        return page;
    }

    /// <summary>Independent bit-wise Ogg CRC-32 so the fixtures do not reuse the probe's table.</summary>
    private static uint OggCrc(byte[] page)
    {
        var crc = 0u;
        for (var index = 0; index < page.Length; index++)
        {
            crc ^= (uint)(index is >= 22 and < 26 ? 0 : page[index]) << 24;
            for (var bit = 0; bit < 8; bit++)
                crc = (crc & 0x8000_0000) != 0 ? (crc << 1) ^ 0x04C1_1DB7 : crc << 1;
        }
        return crc;
    }

    private static byte Crc8(IReadOnlyList<byte> data)
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

    private static ushort Crc16(IReadOnlyList<byte> data)
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

    /// <summary>Returns a copy truncated to <paramref name="fraction"/> of its original length.</summary>
    internal static byte[] Truncate(byte[] bytes, double fraction = 0.5) =>
        bytes[..Math.Max(1, (int)(bytes.Length * fraction))];

    /// <summary>Byte offset of the first FLAC audio frame header after "fLaC" and STREAMINFO.</summary>
    internal const int FlacFirstFrameOffset = 4 + 4 + 34;

    /// <summary>
    /// Byte offset of the CRC-8 field of the first generated FLAC frame header (sync 2 bytes, block
    /// size / rate 1, channel / depth 1, coded number 1).
    /// </summary>
    internal const int FlacFirstFrameCrcOffset = FlacFirstFrameOffset + 5;

    /// <summary>Returns a copy with one byte flipped.</summary>
    internal static byte[] FlipByte(byte[] bytes, int offset)
    {
        var copy = (byte[])bytes.Clone();
        copy[offset] ^= 0xFF;
        return copy;
    }

    /// <summary>Pseudo-random noise that must never be accepted as any supported format.</summary>
    internal static byte[] Noise(int length = 8192)
    {
        var bytes = new byte[length];
        var state = 0x1234_5678u;
        for (var i = 0; i < bytes.Length; i++)
        {
            state = state * 1_664_525u + 1_013_904_223u;
            bytes[i] = (byte)(state >> 24);
        }
        return bytes;
    }
}
