using DynamiteUniverse.Shared.Chart.V2;

namespace DynamiteUniverse.ChartEditor.Core;

internal static class EditorChartCommandRules
{
    public static void Validate(EditorDocument document, string chartId)
    {
        V2SemanticValidator.ValidatePack(document.BuildPackSnapshot());
        _ = V2JsonDecoder.DecodePack(V2JsonEncoder.EncodePack(document.BuildPackSnapshot()),
            "editor chart metadata");
    }
}

internal static class EditorCommandRules
{
    public static EditableNote RequirePathNote(EditorDocument document, string noteId)
    {
        var note = document.RequireNote(noteId);
        if (note.Type is not (V2NoteType.Hold or V2NoteType.Mixer) || note.Nodes.Count == 0)
            throw new InvalidOperationException($"Note '{noteId}' has no editable path.");
        return note;
    }

    public static void ValidateNote(EditableNote note, CustomCurveCache cache)
    {
        if (!Enum.IsDefined(note.Type))
            throw new InvalidOperationException($"Unknown v2 note type '{note.Type}'.");
        ValidateId(note.Id, "Note");
        ValidateTime(note.Time, "Note time");
        ValidatePoint(note.Center, note.Width, "Note");
        if (!double.IsFinite(note.ScrollSpeedMultiplier) || note.ScrollSpeedMultiplier <= 0.0)
            throw new InvalidOperationException("Note scroll speed multiplier must be finite and greater than zero.");
        if (note.CurveToNext is not null && !Enum.IsDefined(note.CurveToNext.Value))
            throw new InvalidOperationException($"Unknown path curve '{note.CurveToNext}'.");
        if (note.Type is not (V2NoteType.Hold or V2NoteType.Mixer))
        {
            if (note.Nodes.Count != 0 || note.CurveToNext is not null || note.CustomExpr is not null)
                throw new InvalidOperationException("Only Hold and Mixer may contain path data.");
            return;
        }
        if (note.Nodes.Count == 0)
            throw new InvalidOperationException("Hold and Mixer require at least one path node.");

        ValidateExpressionPair(note.CurveToNext == V2PathCurve.Custom, note.CustomExpr);
        var previous = note.Time;
        for (var index = 0; index < note.Nodes.Count; index++)
        {
            var node = note.Nodes[index];
            ValidateId(node.Id, "Path node");
            ValidateTime(node.Time, "Path node time");
            ValidatePoint(node.Center, node.Width, "Path node");
            if (node.Time <= previous)
                throw new InvalidOperationException("Path node times must be strictly increasing.");
            previous = node.Time;
            if (index == note.Nodes.Count - 1 && node.CurveToNext is not null)
                throw new InvalidOperationException("Tail path node must omit curveToNext.");
            if (node.CurveToNext is not null && !Enum.IsDefined(node.CurveToNext.Value))
                throw new InvalidOperationException($"Unknown path curve '{node.CurveToNext}'.");
            ValidateExpressionPair(node.CurveToNext == V2PathCurve.Custom, node.CustomExpr);
            if (note.Type == V2NoteType.Hold && node.Judge is null)
                throw new InvalidOperationException("Hold path nodes must resolve judge to true or false.");
            if (note.Type == V2NoteType.Mixer && node.Judge is not null)
                throw new InvalidOperationException("Mixer path nodes must not carry judge state.");
        }
        if (note.Type == V2NoteType.Hold && note.Nodes[^1].Judge != true)
            throw new InvalidOperationException("Hold tail must keep judge:true.");

        var path = (V2PathNote)EditorDocument.ToSnapshot(note);
        var evaluator = V2SemanticValidator.ValidatePathCurves(path, cache);
        if (!evaluator.IsFiniteAndPositive(out _, out var reason))
            throw new InvalidOperationException(reason);
    }

    public static void ValidateScrollTimeline(IReadOnlyList<EditableScroll> timeline, CustomCurveCache cache)
    {
        // The actual interpolated speed must remain in range, even when only a neighbouring
        // event changes. Use the strict reader's rule before mutating the live timeline.
        var snapshot = timeline.Select(scroll => scroll.ToSnapshot()).ToArray();
        try
        {
            V2SemanticValidator.ValidateScrollTimeline(snapshot,
                snapshot.Any(s => s.CurveToNext == V2ScrollCurve.Custom) ? 1 : 0, curveCache: cache);
        }
        catch (V2DiagnosticException e) { throw new InvalidOperationException(e.Message, e); }
    }

    private static void ValidateExpressionPair(bool custom, string? expr)
    {
        if (custom ? string.IsNullOrWhiteSpace(expr) : expr is not null)
            throw new InvalidOperationException(custom ? "Custom curve requires customExpr." : "Only a custom curve may specify customExpr.");
    }

    public static void ValidateTime(ExactBarTime time, string label)
    {
        if (!time.IsJsonSafeCanonical)
            throw new InvalidOperationException($"{label} must be a non-negative JSON-safe exact BarTime.");
    }

    public static void ValidatePoint(double center, double width, string label)
    {
        if (!double.IsFinite(center) || !double.IsFinite(width) || width <= 0)
            throw new InvalidOperationException($"{label} center/width is invalid.");
    }

    public static EditablePathNode CloneNode(EditablePathNode node) => node.Clone();

    public static void ValidateId(string id, string label)
    {
        if (string.IsNullOrEmpty(id) || id.Length > 64 || !IsAsciiAlphaNumeric(id[0]) ||
            id.Skip(1).Any(character => !IsAsciiAlphaNumeric(character) && character is not ('.' or '_' or '-')))
            throw new InvalidOperationException($"{label} ID must match ^[A-Za-z0-9][A-Za-z0-9._-]{{0,63}}$.");
    }

    private static bool IsAsciiAlphaNumeric(char value) =>
        value is >= 'A' and <= 'Z' or >= 'a' and <= 'z' or >= '0' and <= '9';
}
