using System.Text.Json;
using DynamiteUniverse.Game;

namespace DynamiteUniverse.Editor.Shell;

/// <summary>Versioned user data, independent of install/source paths. Tests opt into their own directory.</summary>
internal sealed class WorkbenchUserStore
{
    private readonly string _directory;
    private readonly HashSet<string> _protected = new();
    public string? Warning { get; private set; }
    public static string DefaultDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DynaMakerUV");
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true, MaxDepth = 48 };

    public WorkbenchUserStore(string? directory = null) => _directory = directory ?? DefaultDirectory;

    public T? Load<T>(string name) where T : class
    {
        var path = Path.Combine(_directory, name);
        try
        {
            if (!File.Exists(path)) return null;
            if (new FileInfo(path).Length > 2 * 1024 * 1024) throw new InvalidDataException("User configuration is too large.");
            return JsonSerializer.Deserialize<T>(File.ReadAllText(path), Json)
                ?? throw new InvalidDataException("Empty user configuration.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
        {
            Protect(name, ex.Message);
            return null;
        }
    }

    public void Protect(string name, string reason)
    {
        _protected.Add(name);
        Warning = $"Configuration fallback ({name}): {reason}";
    }

    public bool Save<T>(string name, T value)
    {
        var path = Path.Combine(_directory, name);
        try
        {
            Directory.CreateDirectory(_directory);
            // Never overwrite unreadable, invalid or newer data without preserving it first.
            if (_protected.Contains(name) && File.Exists(path))
                File.Copy(path, path + ".recovery-" + Guid.NewGuid().ToString("N"));
            SettingsFilePersistence.Write(path, JsonSerializer.Serialize(value, Json));
            _protected.Remove(name);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            Warning = $"Configuration not saved ({name}): {ex.Message}";
            return false;
        }
    }
}

internal sealed record WorkbenchPreferences
{
    public const string FileName = "preferences.json";
    public int Version { get; init; } = 1;
    public string Language { get; init; } = "en";
    public int Grid { get; init; } = 16;
    public bool PositionSnap { get; init; } = true;
    public bool WheelReversed { get; init; }
    public double PlacementWidth { get; init; } = 1;
    public double Volume { get; init; } = 100;
    public string Effects { get; init; } = "Full";
    public int Motion { get; init; }

    public bool IsValid => Version == 1 && Language is "en" or "zh-CN" && Grid is >= 1 and <= 64 &&
        double.IsFinite(PlacementWidth) && PlacementWidth >= 0.1 && PlacementWidth <= (double)decimal.MaxValue &&
        double.IsFinite(Volume) && Volume is >= 0 and <= 100 &&
        Effects is "Full" or "Reduced" or "Off" && Motion is >= 0 and <= 2;
}
