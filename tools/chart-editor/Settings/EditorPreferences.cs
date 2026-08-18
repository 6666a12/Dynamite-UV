using System.Text.Json;
using System.Text.Json.Serialization;

namespace DynamiteUniverse.ChartEditor.Settings;

public enum EditorMotionMode
{
    Full,
    Reduced,
    Off,
}

/// <summary>Fault-tolerant per-user editor preferences stored outside the application package.</summary>
public sealed class EditorPreferences
{
    private const string PreferencesFileName = "preferences.json";
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly string _filePath;

    public EditorPreferences(string? settingsDirectory = null, string? legacySettingsDirectory = null)
    {
        var usesDefaultDirectory = settingsDirectory is null;
        SettingsDirectory = Path.GetFullPath(settingsDirectory ?? GetDefaultSettingsDirectory());
        _filePath = Path.Combine(SettingsDirectory, PreferencesFileName);
        var legacyDirectory = legacySettingsDirectory;
        if (legacyDirectory is null && usesDefaultDirectory)
            legacyDirectory = GetLegacySettingsDirectory();
        if (!string.IsNullOrWhiteSpace(legacyDirectory))
            TryImportLegacyPreferences(legacyDirectory);
        Load();
    }

    public static EditorPreferences Current { get; } = new();

    public string SettingsDirectory { get; }
    public string FilePath => _filePath;

    /// <summary>Null means follow the current system language.</summary>
    public string? Language { get; set; }
    public EditorMotionMode Motion { get; set; } = EditorMotionMode.Full;

    public event EventHandler? Changed;

    public bool Reload() => Load();

    /// <summary>Saves atomically when possible; returns false instead of surfacing I/O failures.</summary>
    public bool Save()
    {
        try
        {
            Directory.CreateDirectory(SettingsDirectory);
            var payload = new PreferencePayload
            {
                Language = NormalizeLanguage(Language),
                Motion = Enum.IsDefined(Motion) ? Motion : EditorMotionMode.Full,
            };
            var bytes = JsonSerializer.SerializeToUtf8Bytes(payload, JsonOptions);
            var temporaryPath = _filePath + ".tmp-" + Guid.NewGuid().ToString("N");
            try
            {
                File.WriteAllBytes(temporaryPath, bytes);
                File.Move(temporaryPath, _filePath, overwrite: true);
            }
            finally
            {
                TryDelete(temporaryPath);
            }
            Language = payload.Language;
            Motion = payload.Motion;
            Changed?.Invoke(this, EventArgs.Empty);
            return true;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
        catch (NotSupportedException)
        {
            return false;
        }
    }

    public bool SetLanguage(string? language)
    {
        Language = NormalizeLanguage(language);
        return Save();
    }

    public bool SetMotion(EditorMotionMode motion)
    {
        Motion = Enum.IsDefined(motion) ? motion : EditorMotionMode.Full;
        return Save();
    }

    private bool Load()
    {
        Reset();
        try
        {
            if (!File.Exists(_filePath))
                return true;
            var payload = JsonSerializer.Deserialize<PreferencePayload>(File.ReadAllBytes(_filePath), JsonOptions);
            if (payload is null)
                return false;
            Language = NormalizeLanguage(payload.Language);
            Motion = Enum.IsDefined(payload.Motion) ? payload.Motion : EditorMotionMode.Full;
            return true;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
        catch (JsonException)
        {
            return false;
        }
        catch (NotSupportedException)
        {
            return false;
        }
    }

    private void Reset()
    {
        Language = null;
        Motion = EditorMotionMode.Full;
    }

    private static string? NormalizeLanguage(string? language)
    {
        if (string.IsNullOrWhiteSpace(language))
            return null;
        if (language.Equals(Localization.EditorLocalization.SimplifiedChineseLanguage,
                StringComparison.OrdinalIgnoreCase) ||
            language.StartsWith("zh", StringComparison.OrdinalIgnoreCase))
            return Localization.EditorLocalization.SimplifiedChineseLanguage;
        if (language.Equals(Localization.EditorLocalization.EnglishLanguage,
                StringComparison.OrdinalIgnoreCase) ||
            language.StartsWith("en", StringComparison.OrdinalIgnoreCase))
            return Localization.EditorLocalization.EnglishLanguage;
        return null;
    }

    private static string GetDefaultSettingsDirectory() =>
        Path.Combine(GetSettingsBaseDirectory(), "Dynamite Universe", "Chart Editor");

    private static string GetLegacySettingsDirectory() =>
        Path.Combine(GetSettingsBaseDirectory(), "DUX Community", "Chart Editor");

    private static string GetSettingsBaseDirectory()
    {
        var baseDirectory = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        if (string.IsNullOrWhiteSpace(baseDirectory))
            baseDirectory = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(baseDirectory))
            baseDirectory = Path.GetTempPath();
        return baseDirectory;
    }

    private void TryImportLegacyPreferences(string legacySettingsDirectory)
    {
        try
        {
            if (File.Exists(_filePath) ||
                Directory.Exists(SettingsDirectory) &&
                Directory.EnumerateFileSystemEntries(SettingsDirectory).Any())
                return;
            var legacyFilePath = Path.Combine(Path.GetFullPath(legacySettingsDirectory), PreferencesFileName);
            if (!File.Exists(legacyFilePath))
                return;
            Directory.CreateDirectory(SettingsDirectory);
            File.Copy(legacyFilePath, _filePath, overwrite: false);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
        catch (NotSupportedException)
        {
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private sealed class PreferencePayload
    {
        public string? Language { get; init; }
        public EditorMotionMode Motion { get; init; } = EditorMotionMode.Full;
    }
}
