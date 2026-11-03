namespace DynamiteUniverse.Shared.Audio;

/// <summary>FLAC 帧头 CRC-8（多项式 0x07）与帧尾 CRC-16（多项式 0x8005）。</summary>
internal static class FlacCrc
{
    private static readonly byte[] Table8 = BuildTable8();
    private static readonly ushort[] Table16 = BuildTable16();

    internal static byte Compute8(ReadOnlySpan<byte> data)
    {
        byte crc = 0;
        foreach (var value in data)
            crc = Table8[crc ^ value];
        return crc;
    }

    internal static ushort Compute16(ReadOnlySpan<byte> data)
    {
        ushort crc = 0;
        foreach (var value in data)
            crc = (ushort)((crc << 8) ^ Table16[((crc >> 8) ^ value) & 0xFF]);
        return crc;
    }

    private static byte[] BuildTable8()
    {
        var table = new byte[256];
        for (var i = 0; i < table.Length; i++)
        {
            var value = i;
            for (var bit = 0; bit < 8; bit++)
                value = (value & 0x80) != 0 ? ((value << 1) ^ 0x07) & 0xFF : (value << 1) & 0xFF;
            table[i] = (byte)value;
        }
        return table;
    }

    private static ushort[] BuildTable16()
    {
        var table = new ushort[256];
        for (var i = 0; i < table.Length; i++)
        {
            var value = i << 8;
            for (var bit = 0; bit < 8; bit++)
                value = (value & 0x8000) != 0
                    ? ((value << 1) ^ 0x8005) & 0xFFFF
                    : (value << 1) & 0xFFFF;
            table[i] = (ushort)value;
        }
        return table;
    }
}
