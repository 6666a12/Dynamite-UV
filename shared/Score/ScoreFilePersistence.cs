namespace DynamiteUniverse.Shared.Score;

/// <summary>Owns score-file recovery; a failed load must be preserved before a later save.</summary>
public sealed class ScoreFilePersistence(string path, ScoreStoreCore store)
{
    private bool _loadFailed;

    public void Load()
    {
        if (!File.Exists(path))
            return;
        _loadFailed = true;
        store.LoadJson(File.ReadAllText(path));
        _loadFailed = false;
    }

    public void Save()
    {
        if (_loadFailed)
        {
            // Copy, not move: if preservation fails, leave the original untouched and reject save.
            File.Copy(path, path + ".recovery-" + Guid.NewGuid().ToString("N"));
            _loadFailed = false;
        }
        DynamiteUniverse.Game.SettingsFilePersistence.Write(path, store.SaveJson());
    }
}
