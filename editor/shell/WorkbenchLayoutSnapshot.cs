using Dock.Model.Avalonia.Controls;
using Dock.Model.Controls;
using Dock.Model.Core;

namespace DynamiteUniverse.Editor.Shell;

/// <summary>Data-only Dock tree. Never serializes controls, HWNDs, CLR type names or processes.</summary>
internal sealed class WorkbenchLayoutSnapshot
{
    public const string FileName = "layout.json";
    public int Version { get; set; } = 1;
    public LayoutItem Root { get; set; } = new();
    public List<LayoutWindow> Windows { get; set; } = new();
    public static WorkbenchLayoutSnapshot Capture(IRootDock root) => new()
    {
        Root = LayoutItem.Capture(root),
        Windows = root.Windows?.Select(w => new LayoutWindow
        {
            X = double.IsFinite(w.X) ? w.X : 60, Y = double.IsFinite(w.Y) ? w.Y : 80,
            Width = double.IsFinite(w.Width) ? Math.Clamp(w.Width, 320, 10000) : 960,
            Height = double.IsFinite(w.Height) ? Math.Clamp(w.Height, 240, 10000) : 650,
            Root = LayoutItem.Capture(w.Layout ?? throw new InvalidDataException("Missing floating layout.")),
        }).ToList() ?? new(),
    };

    public void Validate()
    {
        if (Version != 1 || Windows.Count > 16 || Root.Kind != "root" || Root.Id != "root") throw new InvalidDataException("Unsupported layout.");
        var leaves = new HashSet<string>();
        var count = 0;
        void Visit(LayoutItem item, int depth)
        {
            if (depth > 24 || ++count > 256 || !double.IsFinite(item.Proportion) || item.Proportion < 0 ||
                !Enum.IsDefined((Alignment)item.Alignment) || item.Orientation is < 0 or > 1)
                throw new InvalidDataException("Invalid layout geometry.");
            var expected = item.Id switch { "root" => "root", "main" => "proportional", "left" or "right" => "tool", "stage-dock" => "document", _ => null };
            if (expected is not null && expected != item.Kind) throw new InvalidDataException("Container identity/type mismatch.");
            if (item.Kind != "leaf" && item.Id is "stage" or "stage-floating" or "properties" or "tools" or "timing" or "audio-analysis")
                throw new InvalidDataException("Panel identity used for container.");
            if (item.Kind == "leaf")
            {
                if (item.Id is not ("stage" or "stage-floating" or "properties" or "tools" or "timing" or "audio-analysis") || !leaves.Add(item.Id))
                    throw new InvalidDataException("Unknown or duplicate panel.");
                if (item.Children.Count + item.Hidden.Count + item.Left.Count + item.Right.Count + item.Top.Count + item.Bottom.Count != 0)
                    throw new InvalidDataException("Panel cannot contain children.");
            }
            else if (item.Kind is not ("root" or "tool" or "document" or "proportional" or "split"))
                throw new InvalidDataException("Unsupported container.");
            foreach (var child in item.AllChildren()) Visit(child, depth + 1);
        }
        Visit(Root, 0);
        foreach (var window in Windows)
        {
            if (!double.IsFinite(window.X) || !double.IsFinite(window.Y) || !double.IsFinite(window.Width) ||
                !double.IsFinite(window.Height) || window.Width < 320 || window.Height < 240 ||
                window.Width > 10000 || window.Height > 10000 || window.Root.Kind != "root")
                throw new InvalidDataException("Invalid floating window.");
            Visit(window.Root, 0);
        }
        if (!leaves.Contains("stage")) throw new InvalidDataException("Missing stage.");
    }
}

internal sealed class LayoutWindow
{
    public double X { get; set; }
    public double Y { get; set; }
    public double Width { get; set; } = 960;
    public double Height { get; set; } = 650;
    public LayoutItem Root { get; set; } = new();
}

internal sealed class LayoutItem
{
    public string Kind { get; set; } = "root";
    public string Id { get; set; } = "";
    public string? Active { get; set; }
    public double Proportion { get; set; } = 1;
    public int Orientation { get; set; }
    public int Alignment { get; set; }
    public List<LayoutItem> Children { get; set; } = new();
    public List<LayoutItem> Hidden { get; set; } = new();
    public List<LayoutItem> Left { get; set; } = new();
    public List<LayoutItem> Right { get; set; } = new();
    public List<LayoutItem> Top { get; set; } = new();
    public List<LayoutItem> Bottom { get; set; } = new();
    public IEnumerable<LayoutItem> AllChildren() => Children.Concat(Hidden).Concat(Left).Concat(Right).Concat(Top).Concat(Bottom);
    private static List<LayoutItem> CaptureList(IEnumerable<IDockable>? items) => items?.Select(Capture).ToList() ?? new();
    public static LayoutItem Capture(IDockable item) => new()
    {
        Kind = item switch { IRootDock => "root", IToolDock => "tool", IDocumentDock => "document",
            IProportionalDock => "proportional", IProportionalDockSplitter => "split", ITool or IDocument => "leaf",
            _ => throw new InvalidDataException("Unsupported dock container.") },
        Id = item.Id ?? "", Proportion = double.IsFinite(item.Proportion) && item.Proportion >= 0 ? item.Proportion : 1,
        Active = (item as IDock)?.ActiveDockable?.Id,
        Orientation = item is IProportionalDock p ? (int)p.Orientation : 0,
        Alignment = item is IToolDock t ? (int)t.Alignment : 0,
        Children = CaptureList((item as IDock)?.VisibleDockables),
        Hidden = CaptureList((item as IRootDock)?.HiddenDockables),
        Left = CaptureList((item as IRootDock)?.LeftPinnedDockables), Right = CaptureList((item as IRootDock)?.RightPinnedDockables),
        Top = CaptureList((item as IRootDock)?.TopPinnedDockables), Bottom = CaptureList((item as IRootDock)?.BottomPinnedDockables),
    };
}
