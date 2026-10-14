namespace DynamiteUniverse.Shared.Judge;

public sealed class V2InputProtection
{
    private double? _lockedTime;

    public bool IsArmed => _lockedTime.HasValue;
    public double LockedTime => _lockedTime ?? double.NaN;

    public void Reset() => _lockedTime = null;

    public bool CanUse(double noteTime) =>
        !_lockedTime.HasValue || noteTime == _lockedTime.Value;

    public void CommitResolved(double noteTime) => _lockedTime ??= noteTime;
}
