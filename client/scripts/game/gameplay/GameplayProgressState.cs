namespace DynamiteUniverse.Game;

internal enum ProgressRunStatus { AllPerfect, FullCombo, Broken }

/// <summary>Display-only progress and irreversible achievement loss; never decides completion.</summary>
internal sealed class GameplayProgressState
{
    public double Fraction { get; private set; }
    public ProgressRunStatus Status { get; private set; }

    public static double EndSeconds(double chartEnd, double audioLength, double offset, bool tracksAudio) =>
        Math.Max(chartEnd + 2, tracksAudio && double.IsFinite(audioLength) && audioLength > 0
            ? audioLength + offset : 0);

    public void Observe(double time, double end, int great, int good, int miss)
    {
        if (double.IsFinite(time) && double.IsFinite(end) && end > 0)
            Fraction = Math.Clamp(time / end, 0, 1);
        var next = miss > 0 ? ProgressRunStatus.Broken
            : great > 0 || good > 0 ? ProgressRunStatus.FullCombo : ProgressRunStatus.AllPerfect;
        if (next > Status) Status = next;
    }

    public void Complete() => Fraction = 1;
}
