using System.Buffers.Binary;
using System.Diagnostics;
using Godot;

namespace DynamiteUniverse.Game;

/// <summary>Internal-only, silent visual slave of the gameplay media clock. The host owns the gate and layer.</summary>
internal sealed partial class StoryboardVideoPilot : Node2D
{
    private const double DesignWidth = 1920, DesignHeight = 1080;
    private const int MaxWidth = 1280, MaxHeight = 720, MaxBytes = 32 * 1024 * 1024, MaxPages = 16_384;
    private const double MaxDuration = 120, MaxRate = 30;
    // Keep the silent visual slave within roughly one-and-a-half 29.97-FPS frames without
    // seeking every tick. A seek is still rate-limited because Theora keyframe scans are synchronous.
    private const double SeekThreshold = .100, SeekCooldown = 1, RateDeadband = .010;
    private const double SlowSeekMilliseconds = 16;
    private readonly VideoStreamPlayer _player;
    private readonly double _start, _duration;
    private Texture2D? _texture;
    private double _lastTarget = double.NaN, _lastSeek = double.NegativeInfinity;
    private bool _active, _ended, _pauseRequested, _resumePending, _stopped, _transportJump, _automaticSeekTooSlow;

    public bool IsPresenting => _active && !_stopped && Visible;
    public bool Covers(double from, double until) => _start <= from && _start + _duration >= until;
    public void TransportJump() => _transportJump = true;
    public void LeavePlaybackRange()
    {
        if (_active) EndPlayback();
        _ended = false;
    }
    private long _updatesAtStart;

    // A texture Changed event is an actual ImageTexture.Update submission, not a presented frame or a PTS.
    public long FrameUpdates { get; private set; }
    public long DriftSamples { get; private set; }
    public double MaxDriftSeconds { get; private set; }
    public int SeekCount { get; private set; }
    public double MaxSeekMilliseconds { get; private set; }

    private StoryboardVideoPilot(ClipInfo clip, double authoredStartSeconds)
    {
        Name = "StoryboardVideoPilot";
        Visible = false;
        _start = authoredStartSeconds;
        _duration = clip.Duration;
        var scale = Math.Min(DesignWidth / clip.Width, DesignHeight / clip.Height);
        var w = (float)(clip.Width * scale);
        var h = (float)(clip.Height * scale);
        _player = new VideoStreamPlayer
        {
            Name = "SilentVideo",
            Expand = true,
            Autoplay = false,
            Loop = false,
            Volume = 0,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            FocusMode = Control.FocusModeEnum.None,
            TextureFilter = TextureFilterEnum.Linear,
            ProcessPriority = -1, // Observe this frame's player clock when the host calls Sync.
            Size = new Vector2(w, h),
            Position = new Vector2(((float)DesignWidth - w) * .5f, ((float)DesignHeight - h) * .5f),
        };
        AddChild(_player);
    }

    public static StoryboardVideoPilot? TryCreate(string absolutePath, double authoredStartSeconds)
    {
        StoryboardVideoPilot? pilot = null;
        try
        {
            if (!double.IsFinite(authoredStartSeconds)) throw new InvalidDataException("Invalid video start time");
            var started = Stopwatch.GetTimestamp();
            var clip = Inspect(absolutePath);
            var probeMs = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            started = Stopwatch.GetTimestamp();
            pilot = new StoryboardVideoPilot(clip, authoredStartSeconds);
            using var stream = new VideoStreamTheora { File = absolutePath };
            pilot._player.Stream = stream;
            var texture = pilot._player.GetVideoTexture();
            var nativeDuration = pilot._player.GetStreamLength();
            if (texture is null || texture.GetWidth() != clip.Width || texture.GetHeight() != clip.Height ||
                !double.IsFinite(nativeDuration) || nativeDuration <= 0 ||
                Math.Abs(nativeDuration - clip.Duration) > 1 / clip.Fps + .001)
                throw new InvalidDataException("Godot did not instantiate the preflighted Theora stream");
            pilot._texture = texture;
            texture.Changed += pilot.OnFrameUpdate;
            pilot._player.Finished += pilot.OnFinished;
            GD.Print(FormattableString.Invariant(
                $"STORYBOARD_VIDEO_LOAD width={clip.Width} height={clip.Height} bytes={clip.Bytes} fps={clip.Fps:F5} duration_s={nativeDuration:F6} start_s={authoredStartSeconds:F3} probe_ms={probeMs:F3} load_ms={Stopwatch.GetElapsedTime(started).TotalMilliseconds:F3}"));
            return pilot;
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException or ArgumentException or OverflowException)
        {
            pilot?.Free();
            GD.PushWarning($"Storyboard video pilot skipped: {ex.Message}");
            return null;
        }
    }

    public void Sync(double mediaSeconds)
    {
        if (_stopped || _pauseRequested || !IsInsideTree() || !double.IsFinite(mediaSeconds)) return;
        var target = mediaSeconds - _start;
        var jump = _transportJump;
        if (!jump && _resumePending && target == _lastTarget && _active) return; // Wait through the resume floor.
        _transportJump = false;
        _lastTarget = target;
        if (target < 0 || target >= _duration)
        {
            if (_active) EndPlayback();
            // A later discontinuity into the clip can start it again.
            _ended = false;
            return;
        }
        if (_ended && !jump) return;
        if (!_active)
        {
            _active = true;
            _ended = false;
            _updatesAtStart = FrameUpdates;
            _player.Paused = false;
            _player.SpeedScale = 1;
            _player.Play();
            if (!_player.IsPlaying())
            {
                GD.PushWarning("Storyboard video pilot could not begin Theora playback");
                Stop();
                return;
            }
            if (target >= .001) Seek(target);
            _resumePending = false;
            return;
        }

        var drift = target - _player.StreamPosition;
        DriftSamples++;
        MaxDriftSeconds = Math.Max(MaxDriftSeconds, Math.Abs(drift));
        if (jump || !_automaticSeekTooSlow && Math.Abs(drift) >= SeekThreshold && target - _lastSeek >= SeekCooldown)
        {
            Seek(target);
            drift = 0;
        }
        _player.SpeedScale = Math.Abs(drift) <= RateDeadband ? 1f : (float)(1 + Math.Clamp(drift * .5, -.05, .05));
        if (_resumePending)
        {
            _resumePending = false;
            _player.Paused = false;
        }
    }

    public void SetPaused(bool paused)
    {
        if (_stopped || _pauseRequested == paused) return;
        _pauseRequested = paused;
        _resumePending = !paused;
        if (paused) _player.Paused = true;
    }

    public void Stop()
    {
        if (_stopped) return;
        _stopped = true;
        _active = false;
        Hide();
        if (_texture is not null)
        {
            _texture.Changed -= OnFrameUpdate;
            _texture.Dispose();
            _texture = null;
        }
        _player.Finished -= OnFinished;
        _player.Stream = null;
        ReportTelemetry("stop");
    }

    public override void _ExitTree() => Stop();

    public void ReportTelemetry(string reason = "sample", long windowUpdates = 0, bool windowActive = false) =>
        GD.Print(FormattableString.Invariant(
            $"STORYBOARD_VIDEO_STATS reason={reason} frame_updates={FrameUpdates} window_frame_updates={windowUpdates} window_active={windowActive} drift_samples={DriftSamples} max_drift_ms={MaxDriftSeconds * 1000:F3} seeks={SeekCount} max_seek_ms={MaxSeekMilliseconds:F3}"));

    private void OnFrameUpdate()
    {
        FrameUpdates++;
        if (!_active || _stopped || FrameUpdates != _updatesAtStart + 1) return;
        Show();
        GD.Print(FormattableString.Invariant($"STORYBOARD_VIDEO_PLAY video_s={_player.StreamPosition:F6} target_s={_lastTarget:F6} frame_updates={FrameUpdates}"));
    }

    private void OnFinished()
    {
        _active = false;
        _ended = true;
        Hide();
        if (FrameUpdates == _updatesAtStart) GD.PushWarning("Storyboard video ended without submitting a frame");
        ReportTelemetry("eos");
    }

    private void EndPlayback()
    {
        _active = false;
        Hide();
        if (_player.IsInsideTree() && _player.IsPlaying()) _player.Stop();
    }

    private void Seek(double target)
    {
        var started = Stopwatch.GetTimestamp();
        _player.StreamPosition = target;
        var elapsed = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        MaxSeekMilliseconds = Math.Max(MaxSeekMilliseconds, elapsed);
        // Some Theora backends scan from the start even for this all-intra fixture.
        // A costly automatic correction can generate the next drift spike itself.
        _automaticSeekTooSlow |= elapsed > SlowSeekMilliseconds;
        SeekCount++;
        _lastSeek = target;
    }

    private readonly record struct ClipInfo(int Width, int Height, double Fps, double Duration, long Bytes);

    // Bounded structural screening for locally normalized fixtures, not a public untrusted decoder sandbox.
    // Walk Ogg page headers/lacing without loading the file; restrict it to one silent, unchained Theora stream.
    private static ClipInfo Inspect(string path)
    {
        if (!Path.IsPathFullyQualified(path) || path.StartsWith("\\\\", StringComparison.Ordinal) ||
            path.StartsWith("//", StringComparison.Ordinal) || !Path.GetExtension(path).Equals(".ogv", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Expected an absolute local .ogv path");
        StoryboardLocalFiles.CheckDirectoryChain(Path.GetDirectoryName(path)!);
        using var stream = StoryboardLocalFiles.Open(path, MaxBytes, out var length);
        Span<byte> header = stackalloc byte[27];
        Span<byte> laces = stackalloc byte[255];
        Span<byte> identification = stackalloc byte[42];
        Span<byte> packetPrefix = stackalloc byte[7];
        uint serial = 0;
        int width = 0, height = 0, keyShift = 0, packet = 0, packetBytes = 0, prefixBytes = 0, pages = 0;
        double fps = 0;
        long lastGranule = 0, lastFrame = 0;
        bool eos = false;
        while (stream.Position < length)
        {
            if (eos || pages >= MaxPages) throw new InvalidDataException("Chained or oversized Ogg page table");
            stream.ReadExactly(header);
            var flags = header[5];
            var first = pages == 0;
            if (!header[..4].SequenceEqual("OggS"u8) || header[4] != 0 || (flags & ~7) != 0 ||
                ((flags & 2) != 0) != first || ((flags & 1) != 0) != (packetBytes > 0) ||
                BinaryPrimitives.ReadUInt32LittleEndian(header[18..]) != (uint)pages || header[26] == 0)
                throw new InvalidDataException("Invalid Ogg page sequence");
            var pageSerial = BinaryPrimitives.ReadUInt32LittleEndian(header[14..]);
            if (first) serial = pageSerial;
            if (serial != pageSerial) throw new InvalidDataException("Multiple Ogg streams are not supported");
            var segments = laces[..header[26]];
            stream.ReadExactly(segments);
            if (first && (segments.Length != 1 || segments[0] != identification.Length))
                throw new InvalidDataException("Invalid Theora identification page");

            foreach (var bytes in segments)
            {
                if (stream.Position + bytes > length) throw new InvalidDataException("Truncated Ogg page");
                var end = stream.Position + bytes;
                if (first)
                {
                    stream.ReadExactly(identification);
                    (width, height, fps, keyShift) = ReadIdentification(identification);
                }
                else if (packet is 1 or 2 && prefixBytes < packetPrefix.Length)
                {
                    var take = Math.Min(bytes, packetPrefix.Length - prefixBytes);
                    stream.ReadExactly(packetPrefix.Slice(prefixBytes, take));
                    prefixBytes += take;
                }
                else if (packet >= 3 && packetBytes == 0)
                {
                    var firstByte = bytes == 0 ? 0x40 : stream.ReadByte(); // Empty frame packet is a duplicate.
                    if ((firstByte & 0x80) != 0) throw new InvalidDataException("Unexpected Theora header packet");
                    if ((firstByte & 0x40) != 0)
                        throw new InvalidDataException("This video pilot accepts only verified all-intra Theora exports");
                }
                stream.Position = end;
                packetBytes += bytes;
                if (packetBytes > 1024 * 1024) throw new InvalidDataException("Ogg packet exceeds 1 MiB");
                if (bytes != 255)
                {
                    if (packet is 1 or 2 && (prefixBytes != packetPrefix.Length ||
                        packetPrefix[0] != (0x80 | packet) || !packetPrefix[1..].SequenceEqual("theora"u8)))
                        throw new InvalidDataException("Invalid Theora comment or setup packet");
                    ++packet;
                    packetBytes = prefixBytes = 0;
                }
            }

            var granule = BinaryPrimitives.ReadInt64LittleEndian(header[6..]);
            if (granule < -1 || granule >= 0 && granule < lastGranule)
                throw new InvalidDataException("Non-monotonic Ogg granule position");
            if (granule > 0)
            {
                lastGranule = granule;
                var key = granule >> keyShift;
                var frame = key + (granule & ((1 << keyShift) - 1));
                if (key <= 0 || frame > MaxDuration * fps || frame < lastFrame || frame != packet - 3 || frame - key >= 32)
                    throw new InvalidDataException("Theora stream exceeds duration or granule bounds");
                lastFrame = frame;
            }
            eos = (flags & 4) != 0;
            if (eos && (granule <= 0 || packetBytes != 0)) throw new InvalidDataException("Missing final Theora granule");
            ++pages;
        }
        if (!eos || lastFrame <= 0 || packet < 4 || stream.ReadByte() != -1)
            throw new InvalidDataException("Incomplete or changed Theora stream");
        return new(width, height, fps, lastFrame / fps, length);
    }

    private static (int Width, int Height, double Fps, int KeyShift) ReadIdentification(ReadOnlySpan<byte> header)
    {
        static int U24(ReadOnlySpan<byte> bytes) => (bytes[0] << 16) | (bytes[1] << 8) | bytes[2];
        var codedWidth = BinaryPrimitives.ReadUInt16BigEndian(header[10..]) * 16;
        var codedHeight = BinaryPrimitives.ReadUInt16BigEndian(header[12..]) * 16;
        var width = U24(header[14..]);
        var height = U24(header[17..]);
        var numerator = BinaryPrimitives.ReadUInt32BigEndian(header[22..]);
        var denominator = BinaryPrimitives.ReadUInt32BigEndian(header[26..]);
        var parN = U24(header[30..]);
        var parD = U24(header[33..]);
        var fps = (double)numerator / denominator;
        var keyShift = ((header[40] & 3) << 3) | (header[41] >> 5);
        if (header[0] != 0x80 || !header[1..7].SequenceEqual("theora"u8) || header[7] != 3 || header[8] != 2 || header[9] != 1 ||
            width < 2 || height < 2 || (width & 1) != 0 || (height & 1) != 0 ||
            codedWidth > MaxWidth || codedHeight > MaxHeight || width + header[20] > codedWidth || height + header[21] > codedHeight ||
            (header[20] & 1) != 0 || (header[21] & 1) != 0 ||
            denominator == 0 || fps is < 1 or > MaxRate || keyShift > 6 || parN != parD || (header[41] & 0x1f) != 0)
            throw new InvalidDataException("Expected normalized square-pixel Theora 3.2.1, even YUV420 ≤1280×720, 1–30 FPS and short GOP");
        return (width, height, fps, keyShift);
    }
}
