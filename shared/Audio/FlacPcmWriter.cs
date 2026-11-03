namespace DynamiteUniverse.Shared.Audio;

/// <summary>
/// 把逐帧采样累积成 16-bit PCM。内部按声道保存 short，最后一次性交错成小端字节，
/// 这样帧边界不需要提前知道总长度，同时仍然受 <c>MaxPcmBytes</c> 预算约束。
/// </summary>
internal sealed class FlacPcmWriter
{
    private const int InitialFrames = 16 * 1024;

    private readonly int _channels;
    private readonly int _maxPcmBytes;
    private short[][] _samples;
    private int _capacity;
    private bool _released;

    internal FlacPcmWriter(int sampleRate, int channels, int maxPcmBytes)
    {
        if (sampleRate <= 0)
            throw new FlacDecodeException("FLAC 采样率必须为正。");
        if (channels <= 0)
            throw new FlacDecodeException("FLAC 声道数必须为正。");
        if (maxPcmBytes <= 0)
            throw new FlacDecodeException("FLAC PCM 预算必须为正。");
        SampleRate = sampleRate;
        _channels = channels;
        _maxPcmBytes = maxPcmBytes;
        var frames = Math.Min(InitialFrames, Math.Max(1, maxPcmBytes / (2 * channels)));
        _capacity = frames;
        _samples = new short[channels][];
        for (var channel = 0; channel < channels; channel++)
            _samples[channel] = new short[frames];
    }

    internal int SampleRate { get; }

    internal long WrittenFrames { get; private set; }

    internal void Append(int[] channel0, int[] channel1, int frameCount, int bitsPerSample,
        long declaredTotalSamples)
    {
        ObjectDisposedException.ThrowIf(_released, this);
        if (frameCount <= 0)
            throw new FlacDecodeException("FLAC 帧的帧长为 0。");
        if (channel0.Length < frameCount || (_channels > 1 && channel1.Length < frameCount))
            throw new FlacDecodeException("FLAC 帧声道缓冲区不足以容纳该帧。");

        var maxFramesByBudget = _maxPcmBytes / (2 * _channels);
        var target = WrittenFrames + frameCount;
        if (target > maxFramesByBudget)
            throw new FlacDecodeException(
                $"FLAC 解码结果 {target} 帧超过 PCM 预算 {maxFramesByBudget} 帧。");
        if (declaredTotalSamples > 0 && target > declaredTotalSamples)
            throw new FlacDecodeException(
                $"FLAC 帧采样总数超过 STREAMINFO 声明的 {declaredTotalSamples}。");

        EnsureCapacity((int)target);
        var start = (int)WrittenFrames;
        for (var i = 0; i < frameCount; i++)
            _samples[0][start + i] = FlacFrameDecoder.ToPcm16(channel0[i], bitsPerSample);
        if (_channels > 1)
        {
            for (var i = 0; i < frameCount; i++)
                _samples[1][start + i] = FlacFrameDecoder.ToPcm16(channel1[i], bitsPerSample);
        }
        WrittenFrames = target;
    }

    private void EnsureCapacity(int frames)
    {
        if (frames <= _capacity)
            return;

        var maxFrames = _maxPcmBytes / (2 * _channels);
        var next = _capacity;
        while (next < frames)
        {
            next = next > maxFrames / 2 ? maxFrames : next * 2;
            if (next <= _capacity)
                throw new FlacDecodeException("FLAC PCM 缓冲区无法在预算内扩容。");
        }

        // 先算总字节，再分配：任何一次扩容都不允许超过 MaxPcmBytes。
        if ((long)next * _channels * 2 > _maxPcmBytes)
            throw new FlacDecodeException(
                $"FLAC PCM 缓冲区 {next} 帧超过预算 {_maxPcmBytes} 字节。");

        for (var channel = 0; channel < _channels; channel++)
        {
            var grown = new short[next];
            Array.Copy(_samples[channel], grown, (int)WrittenFrames);
            _samples[channel] = grown;
        }
        _capacity = next;
    }

    /// <summary>交错并转换为小端字节；成功后释放内部缓冲。</summary>
    internal byte[] ToLittleEndianBytes()
    {
        ObjectDisposedException.ThrowIf(_released, this);
        var frames = (int)WrittenFrames;
        var bytes = new byte[frames * _channels * 2];
        var index = 0;
        if (_channels == 1)
        {
            var mono = _samples[0];
            for (var i = 0; i < frames; i++)
            {
                var sample = mono[i];
                bytes[index++] = (byte)sample;
                bytes[index++] = (byte)(sample >> 8);
            }
        }
        else
        {
            var left = _samples[0];
            var right = _samples[1];
            for (var i = 0; i < frames; i++)
            {
                var l = left[i];
                var r = right[i];
                bytes[index++] = (byte)l;
                bytes[index++] = (byte)(l >> 8);
                bytes[index++] = (byte)r;
                bytes[index++] = (byte)(r >> 8);
            }
        }
        Release();
        return bytes;
    }

    internal void Release()
    {
        if (_released)
            return;
        _released = true;
        _samples = Array.Empty<short[]>();
        _capacity = 0;
    }
}
