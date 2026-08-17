using ChartTool.IsolatedV2;
using System.Text.Json;

namespace ChartTool;

internal static class LegacyParser
{
    private static readonly string[] MetaProperties =
    [
        "format", "formatVersion", "id", "title", "artist", "charter", "audio", "cover",
        "charts",
    ];

    private static readonly string[] ChartProperties =
    [
        "format", "formatVersion", "name", "FixtureNotice", "Baked_TotalMainNote",
        "TimeLine", "NoteSystem__DropSpeeds", "NotesLeft", "NotesCenter", "NotesRight",
        "AudioData", "JudgeSettings", "NotesSystem", "TimeEnd", "path",
    ];

    public static LegacyPack? ParsePack(string directory, DiagnosticBag diagnostics)
    {
        string root;
        try
        {
            root = Path.GetFullPath(directory);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            diagnostics.Error(directory, "", $"invalid pack directory: {ex.Message}");
            return null;
        }
        if (!Directory.Exists(root))
        {
            diagnostics.Error(root, "", "pack directory does not exist");
            return null;
        }
        var rootInfo = new DirectoryInfo(root);
        if (rootInfo.LinkTarget is not null ||
            (rootInfo.Attributes & FileAttributes.ReparsePoint) != 0)
        {
            diagnostics.Error(root, "", "linked/reparse-point pack roots are not accepted");
            return null;
        }
        ValidatePackageTree(root, diagnostics);

        var metaPath = Path.Combine(root, "meta.json");
        if (!File.Exists(metaPath))
        {
            diagnostics.Error("meta.json", "", "file does not exist");
            return null;
        }

        using var document = StrictJsonDocument.Load(metaPath, "meta.json", diagnostics);
        if (document is null)
            return null;
        if (document.HadBom)
            diagnostics.Warning("meta.json", "", "UTF-8 BOM accepted; v2 output removes it");
        var meta = new StrictObject(document.Root, "meta.json", "", diagnostics);
        if (!meta.EnsureObject())
            return null;
        meta.RejectUnknown(MetaProperties);
        ValidateLegacyFormat(meta, "meta.json", "dynamite-uv-pack", diagnostics);

        var id = meta.RequiredString("id");
        var title = meta.RequiredString("title");
        var artist = meta.RequiredString("artist");
        var charter = meta.RequiredString("charter");
        var audio = meta.RequiredString("audio");
        var cover = meta.OptionalString("cover");
        var chartsElement = meta.RequiredArray("charts");

        ValidateId(id, "meta.json", "/id", diagnostics);
        ValidateDisplay(title, "meta.json", "/title", diagnostics);
        ValidateDisplay(artist, "meta.json", "/artist", diagnostics);
        ValidateDisplay(charter, "meta.json", "/charter", diagnostics);

        string? audioFull = null;
        if (audio is not null)
        {
            if (!TextRules.TryResolveFile(root, audio, out audioFull))
                diagnostics.Error("meta.json", "/audio", "unsafe package-relative path");
            else if (!File.Exists(audioFull))
                diagnostics.Error("meta.json", "/audio", $"referenced audio file does not exist: {audio}");
            else if (IsLinkOrReparsePoint(audioFull))
                diagnostics.Error("meta.json", "/audio", "linked/reparse-point package files are not accepted");
        }

        string? coverFull = null;
        if (!string.IsNullOrEmpty(cover))
        {
            if (!TextRules.TryResolveFile(root, cover, out coverFull))
                diagnostics.Error("meta.json", "/cover", "unsafe package-relative path");
            else if (!File.Exists(coverFull))
                diagnostics.Error("meta.json", "/cover", $"referenced cover file does not exist: {cover}");
            else if (IsLinkOrReparsePoint(coverFull))
                diagnostics.Error("meta.json", "/cover", "linked/reparse-point package files are not accepted");
        }

        var charts = new List<LegacyChartEntry>();
        var chartEntryCount = 0;
        if (chartsElement is { } chartArray)
        {
            var index = 0;
            foreach (var element in chartArray.EnumerateArray())
            {
                var pointer = $"/charts/{index}";
                var item = new StrictObject(element, "meta.json", pointer, diagnostics);
                if (item.EnsureObject())
                {
                    item.RejectUnknown("diff", "level", "file");
                    var diff = item.RequiredString("diff");
                    var level = item.RequiredInt32("level");
                    var file = item.RequiredString("file");
                    if (diff is not null && !TextRules.IsId(diff))
                        diagnostics.Error("meta.json", pointer + "/diff", "diff must satisfy the v2 ID syntax");
                    if (diff is not null && diff.Length > 24 &&
                        !StandardDiff(diff))
                        diagnostics.Error("meta.json", pointer + "/diff",
                            "custom diff must also fit the v2 difficultyKey limit of 24 code points");
                    if (level is < 0 or > 99)
                        diagnostics.Error("meta.json", pointer + "/level", "legacy level must be in 0..99");
                    if (file is not null)
                    {
                        if (!TextRules.TryResolveFile(root, file, out var full))
                            diagnostics.Error("meta.json", pointer + "/file", "unsafe package-relative path");
                        else
                        {
                            if (!File.Exists(full))
                                diagnostics.Error("meta.json", pointer + "/file", $"chart file does not exist: {file}");
                            else if (IsLinkOrReparsePoint(full))
                                diagnostics.Error("meta.json", pointer + "/file", "linked/reparse-point package files are not accepted");
                            if (diff is not null && level is not null)
                            {
                                charts.Add(new LegacyChartEntry
                                {
                                    Diff = diff,
                                    Level = level.Value,
                                    File = file,
                                    FullPath = full,
                                    Pointer = pointer,
                                });
                            }
                        }
                    }
                }
                index++;
            }
            if (index == 0)
                diagnostics.Error("meta.json", "/charts", "at least one chart entry is required");
            chartEntryCount = index;
        }

        if (chartEntryCount > 0 && charts.Count != chartEntryCount)
            diagnostics.Error("meta.json", "/charts", "one or more chart entries could not be parsed");

        foreach (var group in charts.GroupBy(chart => chart.Diff, StringComparer.Ordinal))
        {
            if (group.Count() > 1)
            {
                diagnostics.Error("meta.json", "/charts",
                    $"diff '{group.Key}' is repeated; stable chartId=diff is ambiguous. " +
                    "An explicit chart-id override option is not implemented yet; rename/override the duplicate before conversion");
            }
        }
        foreach (var group in charts.GroupBy(chart => chart.File, StringComparer.OrdinalIgnoreCase))
        {
            if (group.Count() > 1)
                diagnostics.Error("meta.json", "/charts", $"chart path is repeated (case-insensitive): {group.Key}");
        }

        if (diagnostics.HasErrors || id is null || title is null || artist is null ||
            charter is null || audioFull is null)
            return null;
        return new LegacyPack
        {
            SourceDirectory = root,
            Id = id,
            Title = title,
            Artist = artist,
            Charter = charter,
            AudioPath = audio!,
            CoverPath = string.IsNullOrEmpty(cover) ? null : cover,
            Charts = charts,
        };
    }

    public static LegacyChart? ParseChart(LegacyChartEntry entry, string packDirectory,
        DiagnosticBag diagnostics)
    {
        using var document = StrictJsonDocument.Load(entry.FullPath, entry.File, diagnostics);
        if (document is null)
            return null;
        if (document.HadBom)
            diagnostics.Warning(entry.File, "", "UTF-8 BOM accepted; v2 output removes it");
        var root = new StrictObject(document.Root, entry.File, "", diagnostics);
        if (!root.EnsureObject())
            return null;
        root.RejectUnknown(ChartProperties);
        ValidateLegacyFormat(root, entry.File, "dynamite-uv-chart", diagnostics);
        var name = root.RequiredString("name") ?? string.Empty;
        var bakedTotal = root.OptionalInt32("Baked_TotalMainNote") ?? 0;
        if (bakedTotal < 0)
            diagnostics.Error(entry.File, "/Baked_TotalMainNote", "must be non-negative");

        var sections = ParseSections(root, entry.File, diagnostics);
        var scrolls = ParseScrollEvents(root, entry.File, diagnostics);
        var left = ParseNotes(root, "NotesLeft", LegacyTrack.Left, entry.File, diagnostics);
        var center = ParseNotes(root, "NotesCenter", LegacyTrack.Center, entry.File, diagnostics);
        var right = ParseNotes(root, "NotesRight", LegacyTrack.Right, entry.File, diagnostics);
        if (diagnostics.HasErrors)
            return null;
        return new LegacyChart
        {
            File = entry.File,
            Name = name,
            BakedTotalMainNote = bakedTotal,
            Sections = sections,
            ScrollEvents = scrolls,
            NotesLeft = left,
            NotesCenter = center,
            NotesRight = right,
        };
    }

    private static List<LegacyBpmSection> ParseSections(StrictObject root, string file,
        DiagnosticBag diagnostics)
    {
        var result = new List<LegacyBpmSection>();
        var timeline = root.OptionalObject("TimeLine");
        if (timeline is null)
        {
            diagnostics.Error(file, "/TimeLine", "BPM timeline is required for lossless v2 conversion");
            return result;
        }
        var obj = new StrictObject(timeline.Value, file, "/TimeLine", diagnostics);
        obj.RejectUnknown("BakedBarSections", "get_type");
        var array = obj.RequiredArray("BakedBarSections");
        if (array is null)
            return result;
        var index = 0;
        foreach (var element in array.Value.EnumerateArray())
        {
            var pointer = $"/TimeLine/BakedBarSections/{index}";
            var item = new StrictObject(element, file, pointer, diagnostics);
            if (item.EnsureObject())
            {
                item.RejectUnknown("BPM", "BarTime", "Seconds", "BarTimeRangeStarted", "get_type");
                var bpm = item.RequiredDouble("BPM");
                var bar = item.RequiredDouble("BarTime");
                var seconds = item.RequiredDouble("Seconds");
                var exact = ConvertBarTime(bar, file, pointer + "/BarTime", diagnostics);
                if (bpm is not null && (bpm <= 0 || !double.IsFinite(bpm.Value)))
                    diagnostics.Error(file, pointer + "/BPM", "BPM must be finite and greater than zero");
                if (exact is not null && bpm is not null && seconds is not null)
                {
                    result.Add(new LegacyBpmSection
                    {
                        Bpm = bpm.Value,
                        BarTimeValue = bar!.Value,
                        BarTime = exact,
                        Seconds = seconds.Value,
                        Pointer = pointer,
                    });
                }
            }
            index++;
        }
        if (index == 0)
            diagnostics.Error(file, "/TimeLine/BakedBarSections", "BPM timeline must not be empty");
        return result;
    }

    private static List<LegacyScrollEvent> ParseScrollEvents(StrictObject root, string file,
        DiagnosticBag diagnostics)
    {
        var result = new List<LegacyScrollEvent>();
        var array = root.OptionalArray("NoteSystem__DropSpeeds");
        if (array is null)
            return result;
        var index = 0;
        foreach (var element in array.Value.EnumerateArray())
        {
            var pointer = $"/NoteSystem__DropSpeeds/{index}";
            var item = new StrictObject(element, file, pointer, diagnostics);
            if (item.EnsureObject())
            {
                item.RejectUnknown("BarTime", "Value", "get_type");
                var bar = item.RequiredDouble("BarTime");
                var value = item.RequiredDouble("Value");
                var exact = ConvertBarTime(bar, file, pointer + "/BarTime", diagnostics);
                if (value is not null && (value <= 0 || value > 64))
                    diagnostics.Error(file, pointer + "/Value", "scroll value must be finite and in (0,64]");
                if (exact is not null && value is not null)
                {
                    result.Add(new LegacyScrollEvent
                    {
                        BarTimeValue = bar!.Value,
                        BarTime = exact,
                        Value = value.Value,
                        SourceIndex = index,
                        Pointer = pointer,
                    });
                }
            }
            index++;
        }
        return result;
    }

    private static List<LegacyNote> ParseNotes(StrictObject root, string property,
        LegacyTrack track, string file, DiagnosticBag diagnostics)
    {
        var result = new List<LegacyNote>();
        var array = root.RequiredArray(property);
        if (array is null)
            return result;
        var index = 0;
        foreach (var element in array.Value.EnumerateArray())
        {
            var pointer = $"/{property}/{index}";
            var item = new StrictObject(element, file, pointer, diagnostics);
            if (item.EnsureObject())
            {
                item.RejectUnknown("Id", "SubNoteId", "Type", "BarTime", "Position", "Width",
                    "Baked_Second", "Baked_SyncNote", "TimeEnd", "get_type");
                var id = item.RequiredInt32("Id");
                var subId = item.RequiredInt32("SubNoteId");
                var type = item.RequiredInt32("Type");
                var bar = item.RequiredDouble("BarTime");
                var position = item.RequiredDouble("Position");
                var width = item.RequiredDouble("Width");
                var bakedSecond = item.RequiredDouble("Baked_Second");
                var bakedSync = item.OptionalInt32("Baked_SyncNote") ?? 0;
                var exact = ConvertBarTime(bar, file, pointer + "/BarTime", diagnostics);
                if (type is < 1 or > 9)
                    diagnostics.Error(file, pointer + "/Type", "unknown legacy note type; expected integer 1..9");
                if (width is not null && width <= 0)
                    diagnostics.Error(file, pointer + "/Width", "width must be finite and greater than zero");
                if (id is not null && subId is not null && type is not null && exact is not null &&
                    position is not null && width is not null && bakedSecond is not null)
                {
                    result.Add(new LegacyNote
                    {
                        Id = id.Value,
                        SubNoteId = subId.Value,
                        Type = type.Value,
                        Track = track,
                        BarTimeValue = bar!.Value,
                        BarTime = exact,
                        Position = position.Value,
                        Width = width.Value,
                        BakedSecond = bakedSecond.Value,
                        BakedSyncNote = bakedSync,
                        Pointer = pointer,
                    });
                }
            }
            index++;
        }
        return result;
    }

    private static RationalBarTime? ConvertBarTime(double? value, string file, string pointer,
        DiagnosticBag diagnostics)
    {
        if (value is null)
            return null;
        if (!RationalBarTime.TryFromBinary64(value.Value, out var result, out var reason))
        {
            diagnostics.Error(file, pointer, reason);
            return null;
        }
        return result;
    }

    private static void ValidateLegacyFormat(StrictObject root, string file,
        string v2Format, DiagnosticBag diagnostics)
    {
        if (!root.TryGet("format", out var format))
        {
            if (root.TryGet("formatVersion", out _))
                diagnostics.Error(file, "/format", "formatVersion requires an explicit legacy format marker");
            return;
        }
        if (format.ValueKind != JsonValueKind.String)
        {
            diagnostics.Error(file, "/format", "explicit format must be a string");
            return;
        }
        var value = format.GetString();
        var accepted = new[] { "legacy", "dynamix-legacy", "dynamite-uv-v1", v2Format };
        if (!accepted.Contains(value, StringComparer.Ordinal))
        {
            diagnostics.Error(file, "/format",
                $"unknown explicit format '{value}'; only absent format or a recognized legacy/v1 marker is accepted");
        }
        if (root.TryGet("formatVersion", out var version) &&
            (version.ValueKind != JsonValueKind.Number || !version.TryGetInt32(out var number) || number != 1))
        {
            diagnostics.Error(file, "/formatVersion", "legacy formatVersion must be integer 1");
        }
    }

    private static void ValidateId(string? value, string file, string pointer,
        DiagnosticBag diagnostics)
    {
        if (value is not null && !TextRules.IsId(value))
            diagnostics.Error(file, pointer, "must be a v2 ID (1..64 ASCII [A-Za-z0-9._-], alphanumeric first)");
    }

    private static void ValidateDisplay(string? value, string file, string pointer,
        DiagnosticBag diagnostics)
    {
        if (value is not null && !TextRules.IsDisplayText(value))
            diagnostics.Error(file, pointer, "must be NFC display text, 1..256 code points, with no controls or edge whitespace");
    }

    private static void ValidatePackageTree(string root, DiagnosticBag diagnostics)
    {
        var caseInsensitive = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            foreach (var entry in new DirectoryInfo(root).EnumerateFileSystemInfos("*",
                         SearchOption.AllDirectories))
            {
                var relative = Path.GetRelativePath(root, entry.FullName)
                    .Replace(Path.DirectorySeparatorChar, '/');
                if (!caseInsensitive.Add(relative))
                    diagnostics.Error(relative, "", "package paths collide under case-insensitive comparison");
                if (entry.LinkTarget is not null ||
                    (entry.Attributes & FileAttributes.ReparsePoint) != 0)
                    diagnostics.Error(relative, "", "linked/reparse-point package entries are not accepted");
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            diagnostics.Error(root, "", $"cannot inspect package tree: {ex.Message}");
        }
    }

    private static bool StandardDiff(string value) => value.ToLowerInvariant() is
        "casual" or "normal" or "hard" or "mega" or "giga" or "tech";

    private static bool IsLinkOrReparsePoint(string path)
    {
        var info = new FileInfo(path);
        return info.LinkTarget is not null || (info.Attributes & FileAttributes.ReparsePoint) != 0;
    }
}
