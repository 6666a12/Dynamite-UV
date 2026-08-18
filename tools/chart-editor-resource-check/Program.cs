using System.Reflection;
using System.Xml.Linq;
using DynamiteUniverse.ChartEditor.Localization;
using DynamiteUniverse.ChartEditor.Settings;

var repository = FindRepositoryRoot(AppContext.BaseDirectory);
var resources = Path.Combine(repository, "tools", "chart-editor", "Resources");
var neutralDocument = ReadResourceDocument(Path.Combine(resources, "Strings.resx"));
var chineseDocument = ReadResourceDocument(Path.Combine(resources, "Strings.zh-CN.resx"));
var neutral = ToUniqueDictionary(neutralDocument, "neutral", out var neutralDuplicates);
var chinese = ToUniqueDictionary(chineseDocument, "zh-CN", out var chineseDuplicates);

var errors = new List<string>();
errors.AddRange(neutralDuplicates);
errors.AddRange(chineseDuplicates);
CheckValues(neutral, errors);
CheckValues(chinese, errors);
CheckParity(neutral, chinese, errors);
CheckRequiredGroups(neutral.Keys, errors);
CheckPlaceholders(neutral, chinese, errors);
CheckLocalizationService(errors);
CheckPreferences(errors);

if (errors.Count > 0)
{
    foreach (var error in errors)
        Console.Error.WriteLine("RESOURCE CHECK FAIL: " + error);
    return 1;
}

Console.WriteLine($"RESOURCE CHECK PASS: {neutral.Count} matching en-US/zh-CN keys; localization and preferences smoke tests pass");
return 0;

static string FindRepositoryRoot(string start)
{
    var directory = new DirectoryInfo(start);
    while (directory is not null)
    {
        if (File.Exists(Path.Combine(directory.FullName, "AGENTS.md")) &&
            Directory.Exists(Path.Combine(directory.FullName, "tools", "chart-editor")))
            return directory.FullName;
        directory = directory.Parent;
    }
    throw new DirectoryNotFoundException("Could not locate the community repository root.");
}

static (string Key, string Value)[] ReadResourceDocument(string path) =>
    XDocument.Load(path).Root!
        .Elements("data")
        .Select(element => (
            (string?)element.Attribute("name") ?? string.Empty,
            (string?)element.Element("value") ?? string.Empty))
        .ToArray();

static IReadOnlyDictionary<string, string> ToUniqueDictionary(
    IEnumerable<(string Key, string Value)> entries, string label, out string[] errors)
{
    var groups = entries.GroupBy(entry => entry.Key, StringComparer.Ordinal).ToArray();
    errors = groups.Where(group => group.Count() > 1)
        .Select(group => $"{label} contains duplicate key {group.Key}")
        .ToArray();
    return groups.ToDictionary(group => group.Key, group => group.Last().Value, StringComparer.Ordinal);
}

static void CheckValues(IReadOnlyDictionary<string, string> values, ICollection<string> errors)
{
    if (values.ContainsKey(string.Empty))
        errors.Add("an empty key is present");
    foreach (var entry in values.Where(item => string.IsNullOrWhiteSpace(item.Value)))
        errors.Add($"{entry.Key} has an empty value");
}

static void CheckParity(IReadOnlyDictionary<string, string> neutral,
    IReadOnlyDictionary<string, string> localized, ICollection<string> errors)
{
    foreach (var key in neutral.Keys.Except(localized.Keys, StringComparer.Ordinal).Order())
        errors.Add($"zh-CN is missing {key}");
    foreach (var key in localized.Keys.Except(neutral.Keys, StringComparer.Ordinal).Order())
        errors.Add($"neutral resources are missing {key}");
}

static void CheckRequiredGroups(IEnumerable<string> keys, ICollection<string> errors)
{
    string[] requiredPrefixes =
    [
        "Welcome.", "NewProject.", "MainEditor.", "Tool.", "Track.", "Event.",
        "Validation.", "Publish.", "Dynamic.", "Note.", "Curve.", "Settings.",
        "Grid.", "Navigation.", "Icon.",
    ];
    var all = keys.ToArray();
    foreach (var prefix in requiredPrefixes)
    {
        if (!all.Any(key => key.StartsWith(prefix, StringComparison.Ordinal)))
            errors.Add($"required group {prefix} is absent");
    }

    string[] requiredIcons =
    [
        "Select", "Tap", "Drag", "Hold", "ExTap", "Mixer", "Mine", "BarLine",
        "Scroll", "Bpm", "Track", "Undo", "Redo", "Open", "Save", "Language",
        "Info", "Delete", "Apply", "Play", "Speed", "Offset", "Grid", "Snap",
    ];
    foreach (var icon in requiredIcons)
    {
        if (!all.Contains("Icon." + icon, StringComparer.Ordinal))
            errors.Add($"required icon label Icon.{icon} is absent");
    }
}

static void CheckPlaceholders(IReadOnlyDictionary<string, string> neutral,
    IReadOnlyDictionary<string, string> localized, ICollection<string> errors)
{
    foreach (var key in neutral.Keys.Intersect(localized.Keys, StringComparer.Ordinal))
    {
        var neutralPlaceholders = FindPlaceholders(neutral[key]);
        var localizedPlaceholders = FindPlaceholders(localized[key]);
        if (!neutralPlaceholders.SequenceEqual(localizedPlaceholders, StringComparer.Ordinal))
            errors.Add($"placeholder mismatch for {key}");
    }
}

static string[] FindPlaceholders(string text)
{
    var placeholders = new List<string>();
    for (var index = 0; index < text.Length; index++)
    {
        if (text[index] != '{' || index + 2 >= text.Length || !char.IsDigit(text[index + 1]))
            continue;
        var end = text.IndexOf('}', index + 2);
        if (end < 0)
            break;
        placeholders.Add(text[index..(end + 1)]);
        index = end;
    }
    return placeholders.Order(StringComparer.Ordinal).ToArray();
}

static void CheckLocalizationService(ICollection<string> errors)
{
    var localization = EditorLocalization.Current;
    localization.SetLanguage(EditorLocalization.EnglishLanguage);
    if (localization.Get("Common.Save") != "Save")
        errors.Add("English localization lookup failed");
    if (localization.Get("Missing.Resource.Key") != "Missing.Resource.Key")
        errors.Add("missing-key fallback failed");
    if (localization.Format("Validation.ErrorCount", 3) != "3 error(s)")
        errors.Add("English localization formatting failed");

    var eventCount = 0;
    localization.LanguageChanged += (_, _) => eventCount++;
    localization.SetLanguage(EditorLocalization.SimplifiedChineseLanguage);
    if (localization.Get("Common.Save") != "保存")
        errors.Add("zh-CN localization lookup failed");
    if (localization.Format("Validation.ErrorCount", 3) != "3 个错误")
        errors.Add("zh-CN localization formatting failed");
    if (eventCount != 1)
        errors.Add("LanguageChanged did not fire exactly once");
    localization.SetLanguage(EditorLocalization.EnglishLanguage);
}

static void CheckPreferences(ICollection<string> errors)
{
    var root = Path.Combine(Path.GetTempPath(), "dynamite-universe-editor-preferences-check-" + Guid.NewGuid().ToString("N"));
    try
    {
        var preferences = new EditorPreferences(root)
        {
            Language = EditorLocalization.SimplifiedChineseLanguage,
            Motion = EditorMotionMode.Reduced,
        };
        if (!preferences.Save())
        {
            errors.Add("preferences save returned false");
            return;
        }
        var reopened = new EditorPreferences(root);
        if (reopened.Language != EditorLocalization.SimplifiedChineseLanguage ||
            reopened.Motion != EditorMotionMode.Reduced)
            errors.Add("preferences round trip failed");

        var migrationRoot = Path.Combine(root, "migration");
        var legacyDirectory = Path.Combine(migrationRoot, "DUX Community", "Chart Editor");
        var currentDirectory = Path.Combine(migrationRoot, "Dynamite Universe", "Chart Editor");
        var legacy = new EditorPreferences(legacyDirectory)
        {
            Language = EditorLocalization.SimplifiedChineseLanguage,
            Motion = EditorMotionMode.Off,
        };
        if (!legacy.Save())
        {
            errors.Add("legacy preferences setup returned false");
        }
        else
        {
            Directory.CreateDirectory(currentDirectory);
            var migrated = new EditorPreferences(currentDirectory, legacyDirectory);
            if (migrated.Language != EditorLocalization.SimplifiedChineseLanguage ||
                migrated.Motion != EditorMotionMode.Off || !File.Exists(migrated.FilePath))
                errors.Add("legacy preferences were not imported into the empty current directory");

            migrated.SetMotion(EditorMotionMode.Full);
            var notOverwritten = new EditorPreferences(currentDirectory, legacyDirectory);
            if (notOverwritten.Motion != EditorMotionMode.Full)
                errors.Add("legacy preferences overwrote an initialized current directory");
        }

        File.WriteAllText(preferences.FilePath, "not-json");
        var malformed = new EditorPreferences(root);
        if (malformed.Language is not null || malformed.Motion != EditorMotionMode.Full)
            errors.Add("malformed preferences did not fall back to defaults");
    }
    finally
    {
        if (Directory.Exists(root))
            Directory.Delete(root, recursive: true);
    }
}
