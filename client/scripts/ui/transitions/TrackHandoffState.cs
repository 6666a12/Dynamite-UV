namespace DynamiteUniverse.Ui;

internal enum TrackHandoffPhase
{
    Charge,
    Lock,
    Discharge,
}

internal sealed class TrackHandoffState
{
    private const double LockRampSeconds = 0.09;

    public TrackHandoffPhase Phase { get; private set; } = TrackHandoffPhase.Charge;
    public float LockAmount { get; private set; }
    public float Discharge { get; private set; }
    public float ChargePhase { get; private set; }

    public void Reset()
    {
        Phase = TrackHandoffPhase.Charge;
        LockAmount = 0f;
        Discharge = 0f;
        ChargePhase = 0f;
    }

    public void MarkReady(bool allowDirectionalMotion)
    {
        Phase = TrackHandoffPhase.Lock;
        LockAmount = allowDirectionalMotion ? 0f : 1f;
    }

    public void BeginReveal(bool discharge)
    {
        if (discharge && Phase != TrackHandoffPhase.Discharge)
        {
            Phase = TrackHandoffPhase.Discharge;
            Discharge = 0f;
        }
    }

    public bool Advance(double delta, bool allowDirectionalMotion, bool allowLoop, double dischargeSeconds,
        double chargeCycleSeconds)
    {
        if (Phase == TrackHandoffPhase.Lock && LockAmount < 1f)
        {
            LockAmount = allowDirectionalMotion
                ? Math.Min(1f, LockAmount + (float)(delta / LockRampSeconds))
                : 1f;
            return true;
        }

        if (Phase == TrackHandoffPhase.Discharge)
        {
            Discharge = Math.Min(1f, Discharge + (float)(delta / dischargeSeconds));
            return true;
        }

        if (Phase == TrackHandoffPhase.Charge && allowLoop)
        {
            var phase = (ChargePhase + delta / chargeCycleSeconds) % 1.0;
            if (phase < 0.0)
                phase += 1.0;
            ChargePhase = (float)phase;
            return true;
        }

        return false;
    }

    public bool IsDischargeRunning => Phase == TrackHandoffPhase.Discharge && Discharge < 1f;
}
