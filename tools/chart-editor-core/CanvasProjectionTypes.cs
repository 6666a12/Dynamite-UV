using DynamiteUniverse.Shared.Chart.V2;

namespace DynamiteUniverse.ChartEditor.Core;

// Canvas projection coordinates named Center retain the existing left-edge convention.
// Commands convert these values back to document centers at the mutation boundary.

public enum AuthoringNoteKind { Normal, Chain, Hold, Mixer, ExTap, Mine, BarLine }
// 投影除几何外还带回曲线与 judge：右键菜单的 Node Menu 直接读这两个值，
// 不再单独去查文档。
public sealed class AuthoringPathNode
{
    public string Id { get; init; } = string.Empty;
    public ExactBarTime TimeExact { get; set; }
    public double Time { get; set; }
    public double Center { get; set; }
    public double Width { get; set; }
    public V2PathCurve? CurveToNext { get; set; }
    public bool Judge { get; set; }
}

public sealed class AuthoringNote
{
    public string Id { get; init; } = string.Empty;
    public AuthoringNoteKind Kind { get; set; }
    public int Side { get; set; }
    public ExactBarTime TimeExact { get; set; }
    public double Time { get; set; }
    public ExactBarTime EndExact { get; set; }
    public double EndTime { get; set; }
    public double Center { get; set; }
    public double Width { get; set; }
    public V2PathCurve? CurveToNext { get; set; }
    public List<AuthoringPathNode> Nodes { get; } = new();
    public override string ToString() => $"{Kind} · {Id} · {Time:0.###}s";
}

public sealed class AuthoringBpmEvent
{
    public string Id { get; init; } = string.Empty;
    public ExactBarTime TimeExact { get; set; }
    public double Time { get; set; }
    public double Bpm { get; set; }
    public bool Locked { get; init; }
}
