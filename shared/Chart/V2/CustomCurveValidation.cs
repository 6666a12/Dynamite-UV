namespace DynamiteUniverse.Shared.Chart.V2;

/// <summary>One validation pass: contextual errors, reuse of compiled expressions and limits on
/// custom geometry. Existing charts without custom curves consume none of these budgets.</summary>
internal sealed class CustomCurveValidation(int minor, string source, IReadOnlyList<V2BpmEvent> bpms, CustomCurveCache? cache)
{
    internal const int MaxExpressions = 128;
    private const int MaxUses = 4096, MaxBreaks = 131072, MaxPathBreaks = 8192;
    private Dictionary<string, CustomCurve>? _expressions;
    private int _uses, _breaks;

    /// <summary>Used only after successful validation of the same snapshot.</summary>
    public CustomCurve Resolve(string sourceText) => _expressions![sourceText];
    internal void Publish() => cache?.Publish(_expressions);
    public string Canonical(string sourceText) => Resolve(sourceText).Canonical;

    public void Scroll(V2ScrollEvent left, V2ScrollEvent? right, string pointer)
    {
        var curve = Check(left.CurveToNext == V2ScrollCurve.Custom, left.CustomExpr, pointer);
        if (curve is null || right is null) return;
        var (minimum, maximum) = CustomCurveGeometry.Range(left.Value, right.Value, curve);
        if (!(minimum > 0 && maximum <= 64) || !double.IsFinite(minimum) || !double.IsFinite(maximum))
            throw new V2DiagnosticException(source, pointer + "/customExpr",
                FormattableString.Invariant($"actual scroll range [{minimum:R}, {maximum:R}] must be finite and within (0,64]"));
    }

    public void Path(V2PathNote path, string pointer)
    {
        // The same spelling is compiled once per pass. Check in source order before constructing the
        // evaluator, so its compiler never hides a malformed formula behind a generic path error.
        long pathBreaks = path.Nodes.Count;
        for (int i = 0; i <= path.Nodes.Count; i++)
        {
            var node = i == 0 ? null : path.Nodes[i - 1];
            var kind = node is null ? path.CurveToNext : node.CurveToNext;
            var expr = node is null ? path.CustomExpr : node.CustomExpr;
            if (kind != V2PathCurve.Custom && expr is null) continue;
            var point = i == 0 ? pointer : $"{pointer}/nodes/{i - 1}";
            var curve = Check(kind == V2PathCurve.Custom, expr, point);
            if (curve is null || i == path.Nodes.Count) continue;

            // Adapting this interval to audio seconds also needs interior BPM breakpoints. Count
            // conservatively if a knot and a BPM coincide; do not touch non-custom intervals.
            int interiorBpms = Bound(path.Nodes[i].Time, false) - Bound(node?.Time ?? path.Time, true);
            SpendBreaks(interiorBpms, point);
            pathBreaks += curve.Knots.Count - 2L + interiorBpms;
            if (pathBreaks > MaxPathBreaks)
                throw new V2DiagnosticException(source, point + "/customExpr", $"custom path exceeds its {MaxPathBreaks} breakpoint budget");
        }
    }

    private CustomCurve? Check(bool custom, string? expression, string pointer)
    {
        if (!custom && expression is null) return null;
        if (minor != V2Format.CustomCurvesMinor)
            throw new V2DiagnosticException(source, pointer + (custom ? "/curveToNext" : "/customExpr"), "custom curves require chart formatMinor:1");
        if (!custom || string.IsNullOrWhiteSpace(expression))
            throw new V2DiagnosticException(source, pointer + "/customExpr", custom
                ? "custom curve requires a nonempty customExpr" : "customExpr is only valid with curveToNext:custom");
        if (++_uses > MaxUses)
            throw new V2DiagnosticException(source, pointer + "/customExpr", $"chart exceeds {MaxUses} custom curve uses");
        _expressions ??= new(StringComparer.Ordinal);
        if (!_expressions.TryGetValue(expression, out var curve))
        {
            if (_expressions.Count >= MaxExpressions)
                throw new V2DiagnosticException(source, pointer + "/customExpr", $"chart exceeds {MaxExpressions} distinct expression sources");
            try { curve = cache?.Resolve(expression) ?? CustomCurve.Compile(expression); }
            catch (FormatException e) { throw new V2DiagnosticException(source, pointer + "/customExpr", e.Message, e); }
            _expressions.Add(expression, curve);
        }
        SpendBreaks(curve.Knots.Count, pointer);
        return curve;
    }

    private void SpendBreaks(int count, string pointer)
    {
        if (count > MaxBreaks - _breaks)
            throw new V2DiagnosticException(source, pointer + "/customExpr", $"chart exceeds its {MaxBreaks} custom breakpoint budget");
        _breaks += count;
    }

    private int Bound(ExactBarTime time, bool inclusive)
    {
        int lo = 0, hi = bpms.Count;
        while (lo < hi)
        {
            int mid = lo + (hi - lo) / 2;
            if (inclusive ? bpms[mid].Time <= time : bpms[mid].Time < time) lo = mid + 1;
            else hi = mid;
        }
        return lo;
    }
}
