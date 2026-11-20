using System.Runtime.CompilerServices;

namespace DynamiteUniverse.Shared.Chart.V2;

/// <summary>Reusable immutable compiler results from one chart's last successful semantic pass.
/// Only the set of sources in that pass is retained (at most MaxExpressions); never a validation
/// verdict. Drafts, invalid snapshots, or a copied difficulty cannot displace the owner's set.</summary>
public sealed class CustomCurveCache
{
    // A V2Chart is a public value record. Keep this non-serialized optimization out of its
    // equality, with-expressions and digest. Snapshots without an owner get their own cache.
    private static readonly ConditionalWeakTable<V2Chart, CustomCurveCache> Owners = new();
    private Dictionary<string, CustomCurve>? _accepted;
    private Dictionary<string, CustomCurve>? _candidates;

    public static CustomCurveCache For(V2Chart chart) => Owners.GetOrCreateValue(chart);
    public V2Chart Attach(V2Chart newSnapshot)
    {
        Owners.Add(newSnapshot, this);
        return newSnapshot;
    }

    // A new writable owner (not a clone of every table). A worker saving an old snapshot and
    // the editor can always read the same immutable results without holding a compiler lock.
    public CustomCurveCache Fork() => new() { _accepted = Volatile.Read(ref _accepted) };
    // Short-lived command transaction only. Candidate lookup has a separate capacity: a first edit
    // introducing 128 new formulas may still need all 128 previous ones until final validation.
    public CustomCurveCache ForkForCommand() => new() { _accepted = Volatile.Read(ref _accepted), _candidates = new(StringComparer.Ordinal) };

    public CustomCurve Resolve(string source)
    {
        if (Volatile.Read(ref _accepted)?.TryGetValue(source, out var hit) == true) return hit;
        if (_candidates is not { } candidates) return CustomCurve.Compile(source);
        lock (candidates)
        {
            if (candidates.TryGetValue(source, out hit)) return hit;
            var curve = CustomCurve.Compile(source);
            // This is a memo, not a semantic limit on the intermediate steps of a compound command.
            if (candidates.Count < CustomCurveValidation.MaxExpressions) candidates.Add(source, curve);
            return curve;
        }
    }
    public V2PathEvaluator EvaluatePath(V2PathNote path) => new(path, Resolve);
    internal void Publish(Dictionary<string, CustomCurve>? accepted) => Volatile.Write(ref _accepted, accepted);
    internal int Count => Volatile.Read(ref _accepted)?.Count ?? 0;
}
