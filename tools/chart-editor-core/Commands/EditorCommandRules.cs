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

    public static void ValidateNote(EditableNote note)
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
            if (note.Nodes.Count != 0 || note.CurveToNext is not null)
                throw new InvalidOperationException("Only Hold and Mixer may contain path data.");
            return;
        }
        if (note.Nodes.Count == 0)
            throw new InvalidOperationException("Hold and Mixer require at least one path node.");

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
            if (note.Type == V2NoteType.Hold && node.Judge is null)
                throw new InvalidOperationException("Hold path nodes must resolve judge to true or false.");
            if (note.Type == V2NoteType.Mixer && node.Judge is not null)
                throw new InvalidOperationException("Mixer path nodes must not carry judge state.");
        }
        if (note.Type == V2NoteType.Hold && note.Nodes[^1].Judge != true)
            throw new InvalidOperationException("Hold tail must keep judge:true.");

        var path = new V2PathNote
        {
            Id = note.Id,
            Type = note.Type,
            Time = note.Time,
            Center = note.Center,
            Width = note.Width,
            ScrollSpeedMultiplier = note.ScrollSpeedMultiplier,
            CurveToNext = note.CurveToNext,
            Nodes = note.Nodes.Select((node, index) => new V2PathNode
            {
                Id = node.Id,
                Time = node.Time,
                Center = node.Center,
                Width = node.Width,
                CurveToNext = node.CurveToNext,
                Judge = note.Type == V2NoteType.Hold ? node.Judge : null,
                JudgeWasExplicit = note.Type == V2NoteType.Hold &&
                    (node.Judge is not null || index + 1 == note.Nodes.Count),
            }).ToArray(),
        };
        var evaluator = new V2PathEvaluator(path);
        if (!evaluator.IsFiniteAndPositive(out _, out var reason))
            throw new InvalidOperationException(reason);
    }

    public static void ValidateScrollTimeline(IReadOnlyList<EditableScroll> timeline)
    {
        if (timeline.Count > 0 && timeline[0].Time != ExactBarTime.Zero)
            throw new InvalidOperationException("First nonempty Scroll event must be at bar 0.");
        for (var index = 0; index < timeline.Count; index++)
        {
            var scroll = timeline[index];
            ValidateTime(scroll.Time, "Scroll time");
            if (!double.IsFinite(scroll.Value) || scroll.Value <= 0 || scroll.Value > 64)
                throw new InvalidOperationException("Scroll value must be finite and in (0,64].");
            if (index > 0 && scroll.Time <= timeline[index - 1].Time)
                throw new InvalidOperationException("Scroll event times must be strictly increasing.");
            if (scroll.CurveToNext is not null && !Enum.IsDefined(scroll.CurveToNext.Value))
                throw new InvalidOperationException($"Unknown Scroll curve '{scroll.CurveToNext}'.");
            if (index == timeline.Count - 1 && scroll.CurveToNext is not null)
                throw new InvalidOperationException("Tail Scroll event must omit curveToNext.");
        }
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

    public static EditablePathNode CloneNode(EditablePathNode node) => new()
    {
        Key = node.Key,
        Id = node.Id,
        Time = node.Time,
        Center = node.Center,
        Width = node.Width,
        CurveToNext = node.CurveToNext,
        Judge = node.Judge,
    };

    public static void ValidateId(string id, string label)
    {
        if (string.IsNullOrEmpty(id) || id.Length > 64 || !IsAsciiAlphaNumeric(id[0]) ||
            id.Skip(1).Any(character => !IsAsciiAlphaNumeric(character) && character is not ('.' or '_' or '-')))
            throw new InvalidOperationException($"{label} ID must match ^[A-Za-z0-9][A-Za-z0-9._-]{{0,63}}$.");
    }

    private static bool IsAsciiAlphaNumeric(char value) =>
        value is >= 'A' and <= 'Z' or >= 'a' and <= 'z' or >= '0' and <= '9';
}
