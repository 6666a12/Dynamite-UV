namespace DynamiteUniverse.Shared.Judge;

/// <summary>
/// Locks the early/exact side of one input update batch to a single note
/// timestamp while still allowing every note at that exact timestamp to be
/// judged as a chord. Late inputs are intentionally not gated.
/// </summary>
public sealed class InputTimeGroupGate
{
    private bool _locked;
    private float _targetSecond;

    public bool IsLocked => _locked;

    public void Reset()
    {
        _locked = false;
        _targetSecond = 0f;
    }

    /// <summary>
    /// Locks the first target, then accepts only timestamps in the same float32
    /// group. The original judge stores and compares the note time as a float.
    /// </summary>
    public bool TryLock(double noteTime)
    {
        var targetSecond = (float)noteTime;
        if (!_locked)
        {
            _targetSecond = targetSecond;
            _locked = true;
            return true;
        }

        return _targetSecond == targetSecond;
    }

    public bool Matches(double noteTime) =>
        _locked && _targetSecond == (float)noteTime;

	/// <summary>
	/// 原版只在尚未到点的 early/exact 分支检查共享目标时刻；已经过点的
	/// late 输入继续逐触点扫描，不受本批锁影响。
	/// </summary>
	public bool Allows(double noteTime, double inputTime) =>
		noteTime < inputTime || Matches(noteTime);
}
