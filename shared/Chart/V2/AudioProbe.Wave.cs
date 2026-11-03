using System.Buffers.Binary;

namespace DynamiteUniverse.Shared.Chart.V2;

/// <summary>
/// RIFF/WAVE container parser. Keeps the pre-existing writer semantics: only PCM (tag 1) and
/// IEEE-float (tag 3) payloads are accepted and the duration comes from the summed
/// <c>data</c> chunk bytes divided by the declared average byte rate.
/// </summary>
internal static class WaveAudioFormat
{
    internal static double Probe(Stream stream, string source)
    {
        Span<byte> header = stackalloc byte[12];
        if (stream.Length < header.Length ||
            AudioProbeSupport.ReadAt(stream, 0, header) != header.Length ||
            BinaryPrimitives.ReadUInt32LittleEndian(header) != 0x46464952 ||
            BinaryPrimitives.ReadUInt32LittleEndian(header[8..]) != 0x45564157)
        {
            throw AudioProbeSupport.Reject(source,
                AudioProbeSupport.StartsWithAscii(header, "RF64")
                    ? "RF64 WAVE is not supported; use RIFF/WAVE with PCM or IEEE float"
                    : "invalid RIFF/WAVE audio file");
        }

        uint? byteRate = null;
        long dataBytes = 0;
        Span<byte> chunkHeader = stackalloc byte[8];
        Span<byte> format = stackalloc byte[16];
        var position = 12L;
        while (position + chunkHeader.Length <= stream.Length)
        {
            if (AudioProbeSupport.ReadAt(stream, position, chunkHeader) != chunkHeader.Length)
                break;
            var id = BinaryPrimitives.ReadUInt32LittleEndian(chunkHeader);
            var size = BinaryPrimitives.ReadUInt32LittleEndian(chunkHeader[4..]);
            var payload = position + chunkHeader.Length;
            var end = payload + size;
            if (end > stream.Length)
                throw AudioProbeSupport.Reject(source,
                    $"truncated RIFF/WAVE chunk at byte offset {position}");
            if (id == 0x20746d66)
            {
                if (byteRate is not null)
                    throw AudioProbeSupport.Reject(source, "duplicate WAVE fmt chunk");
                if (size < 16)
                    throw AudioProbeSupport.Reject(source, "invalid WAVE fmt chunk");
                format.Clear();
                AudioProbeSupport.ReadExactlyAt(stream, payload, format, source,
                    "truncated WAVE fmt chunk");
                var tag = BinaryPrimitives.ReadUInt16LittleEndian(format);
                var channels = BinaryPrimitives.ReadUInt16LittleEndian(format[2..]);
                var sampleRate = BinaryPrimitives.ReadUInt32LittleEndian(format[4..]);
                var rate = BinaryPrimitives.ReadUInt32LittleEndian(format[8..]);
                if (tag is not (1 or 3) || channels == 0 || sampleRate == 0 || rate == 0)
                    throw AudioProbeSupport.Reject(source,
                        "unsupported WAVE format (PCM/IEEE float required)");
                byteRate = rate;
            }
            else if (id == 0x61746164)
            {
                if (size == 0)
                    throw AudioProbeSupport.Reject(source, "WAVE data chunk is empty");
                dataBytes = checked(dataBytes + size);
            }
            position = end + (size & 1);
        }

        if (byteRate is null || dataBytes == 0)
            throw AudioProbeSupport.Reject(source, "WAVE fmt or data chunk is missing");
        return dataBytes / (double)byteRate.Value;
    }
}
