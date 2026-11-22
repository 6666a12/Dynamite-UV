using System.Diagnostics;
using System.Globalization;
using System.Text.Json;

namespace DynamiteUniverse.Editor.Shell.VideoExport;

/// <summary>Supervises the independent producer and consumer of one capture stream.</summary>
internal sealed class VideoExportStream(IProgress<VideoExportProgress> progress)
{
    internal const int Slots = 10;
    internal const int AudioFramesPerVideoFrame = 800;
    private const string StreamPrefix = "UV_VIDEO_STREAM ";
    private const string ScenePrefix = "UV_VIDEO_EXPORT ";
    private readonly TaskCompletionSource _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private Summary? _finished;
    private long _activity = Stopwatch.GetTimestamp();
    private long _finalizingSince;
    private int _encodedFrames;

    private void MarkActivity() => Interlocked.Exchange(ref _activity, Stopwatch.GetTimestamp());

    private void Monitor(VideoExportStorage storage)
    {
        storage.CheckWriting();
        var finalizing = Interlocked.Read(ref _finalizingSince);
        // +faststart rewrites the MP4 without advancing frame=, even when the drive is busy.
        // Keep cancellation/space monitoring, but give this pass its own generous deadline.
        if (finalizing != 0)
        {
            if (Stopwatch.GetElapsedTime(finalizing) > TimeSpan.FromMinutes(30))
                throw new TimeoutException("FFmpeg did not finish the MP4 within 30 minutes.");
        }
        else if (Stopwatch.GetElapsedTime(Interlocked.Read(ref _activity)) > TimeSpan.FromMinutes(3))
            throw new TimeoutException("The video recorder and encoder stopped making progress.");
    }

    internal sealed record Summary(int FirstSourceFrame, int Frames, long AudioSamplesPerChannel, int MaxSlotsUsed);

    internal async Task<Summary> Run(string worker, string ffmpeg, VideoExportEncoding encoding,
        string marker, string output, Dictionary<string, string> environment, VideoExportStorage storage,
        CancellationToken cancellationToken)
    {
        var pipe = @"\\.\pipe\dynamaker-uv-" + Guid.NewGuid().ToString("N");
        environment["DU_CAPTURE_PIPE"] = pipe;
        using var session = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var producer = VideoExportProcess.Run(worker,
            ["--write-movie", marker, "--fixed-fps", "60", "--audio-driver", "Dummy", "--disable-vsync", "--", "--uv-video-export"],
            environment, OnWorkerLine, session.Token, () => Monitor(storage));
        Task<VideoExportProcess.Result>? consumer = null;
        try
        {
            if (await Task.WhenAny(_ready.Task, producer).WaitAsync(TimeSpan.FromSeconds(90), session.Token).ConfigureAwait(false) == producer)
            {
                var stopped = await producer.ConfigureAwait(false);
                throw new IOException($"The recording process exited before opening the stream ({stopped.ExitCode}). {stopped.Diagnostic}");
            }

            consumer = VideoExportProcess.Run(ffmpeg, encoding.Arguments(pipe, output), null, OnEncoderLine,
                session.Token, () => Monitor(storage));
            var first = await Task.WhenAny(producer, consumer).ConfigureAwait(false);
            var firstResult = await first.ConfigureAwait(false);
            EnsureExit(firstResult, first == producer);
            var last = first == producer ? consumer : producer;
            // After pipe EOF, the other process only has its bounded queue/final MP4 work remaining.
            EnsureExit(await last.ConfigureAwait(false), last == producer);
            return _finished ?? throw new InvalidDataException("The recorder did not confirm that the stream finished.");
        }
        finally
        {
            session.Cancel();
            // Start cancellation of both at once; the reader closing also unblocks the writer.
            await Task.WhenAll(Observe(producer), consumer is null ? Task.CompletedTask : Observe(consumer)).ConfigureAwait(false);
        }
    }

    private static void EnsureExit(VideoExportProcess.Result result, bool recorder)
    {
        if (result.ExitCode != 0)
            throw new IOException($"{(recorder ? "Game recorder" : "FFmpeg")} exited with code {result.ExitCode}. {result.Diagnostic}");
    }

    private static async Task Observe(Task<VideoExportProcess.Result> task)
    {
        // A secondary cancellation/exit must not replace the original failure.
        try { await task.ConfigureAwait(false); }
        catch (Exception error) { Debug.WriteLine(error); }
    }

    private void OnWorkerLine(string line)
    {
        if (line.StartsWith(StreamPrefix, StringComparison.Ordinal))
        {
            MarkActivity();
            using var message = JsonDocument.Parse(line[StreamPrefix.Length..]);
            var payload = message.RootElement;
            if (payload.GetProperty("version").GetInt32() != 1) throw new InvalidDataException("Invalid stream protocol version.");
            switch (payload.GetProperty("event").GetString())
            {
                case "pipe-ready" when _ready.TrySetResult():
                    break;
                case "finished" when _finished is null:
                    _finished = new(payload.GetProperty("firstSourceFrame").GetInt32(), payload.GetProperty("frames").GetInt32(),
                        payload.GetProperty("audioSamplesPerChannel").GetInt64(), payload.GetProperty("maxSlotsUsed").GetInt32());
                    Interlocked.Exchange(ref _finalizingSince, Stopwatch.GetTimestamp());
                    progress.Report(new(VideoExportPhase.Encode));
                    break;
                case "error":
                    throw new IOException(payload.GetProperty("message").GetString() ?? "Game recorder stream failed.");
                default:
                    throw new InvalidDataException("Unexpected or duplicate recording stream event.");
            }
            return;
        }
        if (!line.StartsWith(ScenePrefix, StringComparison.Ordinal)) return;
        MarkActivity();
        using var scene = JsonDocument.Parse(line[ScenePrefix.Length..]);
        var root = scene.RootElement;
        if (root.TryGetProperty("stage", out var stage) && stage.GetString() is { } value)
            progress.Report(new(VideoExportPhase.Render, Detail: value));
        else if (root.TryGetProperty("event", out var kind) && kind.GetString() == "error")
            throw new IOException(root.GetProperty("message").GetString() ?? "Game recording failed.");
    }

    private void OnEncoderLine(string line)
    {
        if (!TryReadFrame(line, out var frames) || frames <= _encodedFrames) return;
        _encodedFrames = frames;
        MarkActivity();
    }

    private static bool TryReadFrame(string line, out int frames)
    {
        frames = 0;
        return line.StartsWith("frame=", StringComparison.Ordinal) &&
            int.TryParse(line.AsSpan(6), CultureInfo.InvariantCulture, out frames);
    }

    internal static bool DecodedFrameCountMatches(string stdout, int expected) => stdout.Split('\n').Reverse()
        .FirstOrDefault(line => TryReadFrame(line, out _)) is { } last && TryReadFrame(last, out var frames) && frames == expected;
}
