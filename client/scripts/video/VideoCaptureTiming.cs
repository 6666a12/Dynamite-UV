namespace DynamiteUniverse.Video;

/// <summary>Fixed output-frame rules, independent of the renderer and audio transport.</summary>
internal static class VideoCaptureTiming
{
    internal const int FramesPerSecond = 60;
    internal const int MaximumSegmentFrames = 60 * FramesPerSecond;

    internal static bool HasPlayableStart(double start, double decodedLength) =>
        double.IsFinite(start) && double.IsFinite(decodedLength) && start >= 0 &&
        start < decodedLength && (float)start < decodedLength;

    internal static int AvailablePreviewFrames(double start, double duration, double decodedLength)
    {
        if (!HasPlayableStart(start, decodedLength))
            throw new InvalidDataException("The audition start is outside the decoded audio stream.");
        if (!double.IsFinite(duration) || duration <= 0)
            throw new InvalidDataException("The audition has no positive finite duration.");
        var seconds = Math.Min(Math.Min(duration, decodedLength - start), MaximumSegmentFrames / FramesPerSecond);
        var frames = seconds * FramesPerSecond;
        // Subtraction at EOF can put an exact half-frame tie just below .5 (e.g. 1.025 − 1).
        // Ignore numerical noise on the order of 1e-11 seconds, not a fraction of a mix sample.
        var half = Math.Floor(frames) + .5;
        if (Math.Abs(frames - half) < 1e-9) frames = half;
        return (int)Math.Round(frames, MidpointRounding.AwayFromZero);
    }

    internal static int FramesUntilPreviewEnd(int firstFrame, int count, int currentFrame)
    {
        var remaining = EndFrame(firstFrame, count) - currentFrame;
        if (firstFrame > currentFrame || remaining < 0)
            throw new InvalidDataException("The audition playback deadline was missed.");
        return remaining;
    }

    // The end of audition is exclusive; the reveal draw is included even when extra hold is 0.
    internal static int EndFrame(int firstFrame, int count) => checked(firstFrame + count);
}
