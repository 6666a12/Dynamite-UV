using Concentus;
using Concentus.Oggfile;
using NAudio.Wave;

namespace DynamiteUniverse.ChartEditor.Audio;

/// <summary>Seekable NAudio provider backed by the managed Concentus Ogg Opus decoder.</summary>
internal sealed class OpusWaveStream : WaveStream
{
    private const int OpusSampleRate = 48_000;
    private readonly Stream _source;
    private readonly IOpusDecoder _decoder;
    private readonly OpusOggReadStream _reader;
    private readonly WaveFormat _waveFormat;
    private readonly long _length;
    private byte[] _packetBytes = [];
    private int _packetOffset;
    private long _position;
    private bool _disposed;

    public OpusWaveStream(string path)
    {
        _source = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        try
        {
            var channels = ReadOpusChannels(_source);
            _source.Position = 0;
            _decoder = OpusCodecFactory.CreateDecoder(OpusSampleRate, channels);
            _reader = new OpusOggReadStream(_decoder, _source);
            if (!_reader.CanSeek || _reader.TotalTime <= TimeSpan.Zero || !_reader.HasNextPacket)
                throw new InvalidDataException(_reader.LastError ??
                    "Ogg Opus decoder could not determine a seekable positive duration.");
            _waveFormat = new WaveFormat(OpusSampleRate, 16, channels);
            _length = Align((long)Math.Ceiling(
                _reader.TotalTime.TotalSeconds * _waveFormat.AverageBytesPerSecond));
        }
        catch
        {
            _source.Dispose();
            throw;
        }
    }

    public override WaveFormat WaveFormat => _waveFormat;
    public override long Length => _length;

    public override long Position
    {
        get => _position;
        set
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var target = Align(Math.Clamp(value, 0L, Length));
            _reader.SeekTo(TimeSpan.FromSeconds(target /
                (double)_waveFormat.AverageBytesPerSecond));
            _packetBytes = [];
            _packetOffset = 0;
            _position = target;
        }
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(buffer);
        if (offset < 0 || count < 0 || offset + count > buffer.Length)
            throw new ArgumentOutOfRangeException(nameof(offset));
        count -= count % WaveFormat.BlockAlign;
        var written = 0;
        while (written < count && _position < Length)
        {
            if (_packetOffset >= _packetBytes.Length && !DecodePacket())
                break;
            var copy = Math.Min(count - written, _packetBytes.Length - _packetOffset);
            copy = (int)Math.Min(copy, Length - _position);
            Buffer.BlockCopy(_packetBytes, _packetOffset, buffer, offset + written, copy);
            written += copy;
            _packetOffset += copy;
            _position += copy;
        }
        return written;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && !_disposed)
        {
            _reader.Close();
            _decoder.Dispose();
            _source.Dispose();
            _disposed = true;
        }
        base.Dispose(disposing);
    }

    public static EditorAudioProbeResult Probe(string path,
        EditorAudioFormatDescriptor format)
    {
        using var stream = new OpusWaveStream(path);
        return new EditorAudioProbeResult(format, stream.TotalTime,
            stream.WaveFormat.SampleRate, stream.WaveFormat.Channels);
    }

    private bool DecodePacket()
    {
        while (_reader.HasNextPacket)
        {
            var samples = _reader.DecodeNextPacket();
            if (samples is null)
            {
                if (!string.IsNullOrWhiteSpace(_reader.LastError))
                    throw new InvalidDataException(_reader.LastError);
                return false;
            }
            if (samples.Length == 0)
                continue;
            _packetBytes = new byte[samples.Length * sizeof(short)];
            Buffer.BlockCopy(samples, 0, _packetBytes, 0, _packetBytes.Length);
            _packetOffset = 0;
            return true;
        }
        return false;
    }

    private long Align(long value) => value - value % WaveFormat.BlockAlign;

    private static int ReadOpusChannels(Stream stream)
    {
        Span<byte> buffer = stackalloc byte[64 * 1024];
        var count = stream.Read(buffer);
        ReadOnlySpan<byte> signature = "OpusHead"u8;
        var offset = buffer[..count].IndexOf(signature);
        if (offset < 0 || offset + 10 > count)
            throw new InvalidDataException("Ogg stream does not contain an OpusHead packet.");
        var version = buffer[offset + 8];
        var channels = buffer[offset + 9];
        if (version > 15 || channels is not (1 or 2))
            throw new NotSupportedException(
                "Editor Ogg Opus decoding currently supports mono or stereo OpusHead streams.");
        return channels;
    }
}
