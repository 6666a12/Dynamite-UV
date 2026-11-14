namespace DynamiteUniverse.Shared.Score;

/// <summary>Immutable client-reported play, not an anti-cheat proof. Version 2 preserves raw score precision.</summary>
public sealed record PlayReport(Guid PlayId, Guid ReleaseId, string ChartId, string Digest, string RulesetId,
    string Mode, bool Mirror, bool Auto, bool Completed, int Score, int Perfect, int Great, int Good, int Miss, int MaxCombo,
    int ContractVersion, bool Bleed, double RawScore, double TheoreticalMax, double Clear,
    string FinishReason, DateTimeOffset StartedAt, bool VerificationRun, bool UsedSeek, bool DebugRun);
