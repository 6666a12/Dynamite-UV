using System.Text.Json;

namespace DynamiteUniverse.Shared.Score;

/// <summary>Mode is an outer local BEST scope; frozen content identity and comparison stay unchanged.</summary>
public sealed class ModeScoreStore
{
    private ScoreStoreCore _standard = new(), _hardcore = new(), _history = new();
    public ScoreStoreCore For(string mode) => mode switch
    { "standard" => _standard, "hardcore" => _hardcore, _ => throw new ArgumentException("Unknown mode") };
    public ScoreStoreCore History => _history;
    public bool NeedsMigration { get; private set; }
    public void LoadJson(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var standard = new ScoreStoreCore(); var hardcore = new ScoreStoreCore(); var history = new ScoreStoreCore();
        var migrated = !root.TryGetProperty("format", out var format) || format.GetString() != "dynamite-uv-mode-scores";
        if (migrated) history.LoadJson(json);
        else
        {
            if (root.GetProperty("version").GetInt32() != 1) throw new JsonException("Unsupported mode scores");
            standard.LoadJson(root.GetProperty("standard").GetRawText());
            hardcore.LoadJson(root.GetProperty("hardcore").GetRawText());
            history.LoadJson(root.GetProperty("unclassified").GetRawText());
        }
        _standard = standard; _hardcore = hardcore; _history = history; NeedsMigration = migrated;
    }
    public string SaveJson() => JsonSerializer.Serialize(new
    {
        format = "dynamite-uv-mode-scores", version = 1,
        standard = JsonSerializer.Deserialize<JsonElement>(_standard.SaveJson()),
        hardcore = JsonSerializer.Deserialize<JsonElement>(_hardcore.SaveJson()),
        unclassified = JsonSerializer.Deserialize<JsonElement>(_history.SaveJson()),
    });
}

/// <summary>Backup-before-migration and corrupt-file preservation; never guesses old play modes.</summary>
public sealed class ModeScoreFile(string path, ModeScoreStore store)
{
    private bool _failed;
    private bool _migrationPreserved;
    public void Load()
    {
        if (!File.Exists(path)) return;
        _failed = true; store.LoadJson(File.ReadAllText(path)); _failed = false;
        if (store.NeedsMigration) PreserveMigration();
    }
    private void PreserveMigration()
    {
        if (_migrationPreserved) return;
        File.Copy(path, path + ".before-modes-" + Guid.NewGuid().ToString("N"));
        _migrationPreserved = true;
    }
    public void Save()
    {
        if (_failed)
        {
            File.Copy(path, path + ".recovery-" + Guid.NewGuid().ToString("N")); _failed = false;
        }
        if (store.NeedsMigration) PreserveMigration();
        DynamiteUniverse.Game.SettingsFilePersistence.Write(path, store.SaveJson());
    }
}
