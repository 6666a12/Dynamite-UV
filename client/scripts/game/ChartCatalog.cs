using Godot;

namespace DynamiteUniverse.Game;

/// <summary>Scans chart package roots according to the current build's release policy.</summary>
public static class ChartCatalog
{
    public static bool IsEditorOrInternal => OS.HasFeature("internal_testdata") ||
        OS.HasFeature("editor") || OS.HasFeature("editor_runtime");

    public static List<ChartPack> ScanAll()
    {
        var byId = new Dictionary<string, ChartPack>();
        // Development packages load first; a user package with the same id overrides it.
        var roots = IsEditorOrInternal
            ? new[] { "res://testdata/packs", "user://charts" }
            : new[] { "user://charts" };
        foreach (var root in roots)
        {
            using var dir = DirAccess.Open(root);
            if (dir == null)
                continue;
            foreach (var sub in dir.GetDirectories())
            {
                var pack = ChartPack.Load($"{root}/{sub}");
                if (pack != null)
                    byId[pack.Id] = pack;
            }
        }
        return byId.Values.OrderBy(pack => pack.Title).ToList();
    }
}
