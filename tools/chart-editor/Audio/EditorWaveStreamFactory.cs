using NAudio.Flac;
using NAudio.Vorbis;
using NAudio.Wave;
using NLayer;

namespace DynamiteUniverse.ChartEditor.Audio;

internal static class EditorWaveStreamFactory
{
    public static WaveStream Open(string absolutePath)
    {
        var path = Path.GetFullPath(absolutePath);
        var format = EditorAudioFormatRegistry.GetRequired(path);
        try
        {
            return format.Format switch
            {
                EditorAudioFormat.Wave => OpenWave(path),
                EditorAudioFormat.Mp3 => new MpegWaveStream(path),
                EditorAudioFormat.Flac => new FlacReader(path),
                EditorAudioFormat.OggVorbis => new VorbisWaveReader(path),
                EditorAudioFormat.OggOpus => new OpusWaveStream(path),
                EditorAudioFormat.M4aAac or EditorAudioFormat.RawAac =>
                    OpenMediaFoundation(path, format),
                _ => throw new NotSupportedException($"No decoder is registered for {format.DisplayName}."),
            };
        }
        catch (Exception exception) when (exception is not PlatformNotSupportedException and
            not NotSupportedException)
        {
            throw new InvalidDataException(
                $"{format.DisplayName} audio could not be opened: {exception.Message}", exception);
        }
    }

    private static WaveStream OpenWave(string path)
    {
        var reader = new WaveFileReader(path);
        try
        {
            EditorAudioProbe.ValidateWavePcm(reader.WaveFormat);
            return reader;
        }
        catch
        {
            reader.Dispose();
            throw;
        }
    }

    private static WaveStream OpenMediaFoundation(string path,
        EditorAudioFormatDescriptor format)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException(
                $"{format.DisplayName} requires Windows Media Foundation; this platform fails closed.");
        return new MediaFoundationReader(path);
    }

    private sealed class MpegWaveStream : WaveStream
    {
        private readonly MpegFile _reader;
        private readonly WaveFormat _waveFormat;

        public MpegWaveStream(string path)
        {
            _reader = new MpegFile(path);
            if (!_reader.CanSeek || _reader.Length <= 0)
            {
                _reader.Dispose();
                throw new InvalidDataException("MP3 decoder could not determine a seekable duration.");
            }
            _waveFormat = WaveFormat.CreateIeeeFloatWaveFormat(
                _reader.SampleRate, _reader.Channels);
        }

        public override WaveFormat WaveFormat => _waveFormat;
        public override long Length => _reader.Length;
        public override long Position
        {
            get => _reader.Position;
            set => _reader.Position = Align(value);
        }

        public override int Read(byte[] buffer, int offset, int count) =>
            _reader.ReadSamples(buffer, offset, count - count % WaveFormat.BlockAlign);

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                _reader.Dispose();
            base.Dispose(disposing);
        }

        private long Align(long value)
        {
            value = Math.Clamp(value, 0L, Length);
            return value - value % WaveFormat.BlockAlign;
        }
    }
}
