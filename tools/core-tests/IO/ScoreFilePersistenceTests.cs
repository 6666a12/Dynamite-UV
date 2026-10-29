using System.Text.Json;
using DynamiteUniverse.Shared.Score;

namespace CoreTests;

internal static class ScoreFilePersistenceTests
{
    public static void Run(Action<bool, string> check)
    {
        var directory = Path.Combine(Path.GetTempPath(), "du-score-recovery-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "scores.json");
        try
        {
            var store = new ScoreStoreCore();
            var persistence = new ScoreFilePersistence(path, store);
            persistence.Load();
            store.TryUpdateLegacy("pack", "hard", new ScoreRecord { Score = 100, Acc = 80 });
            persistence.Save();
            persistence.Load();
            check(store.GetLegacy("pack", "hard")?.Score == 100, "score file round-trip preserves records");

            var broken = new byte[] { 123, 34, 255, 0 };
            File.WriteAllBytes(path, broken);
            try { persistence.Load(); check(false, "broken score file must report failure"); }
            catch (JsonException) { }
            check(store.GetLegacy("pack", "hard")?.Score == 100, "failed load preserves in-memory scores");
            persistence.Save();
            var backups = Directory.GetFiles(directory, "scores.json.recovery-*");
            check(backups.Length == 1 && File.ReadAllBytes(backups[0]).SequenceEqual(broken),
                "save after failed load preserves the original bytes in a recovery file");
            persistence.Save();
            check(Directory.GetFiles(directory, "scores.json.recovery-*").Length == 1,
                "subsequent saves do not create redundant recovery files");
            persistence.Load();
            check(store.GetLegacy("pack", "hard")?.Score == 100, "recovered score file can be loaded");

            File.WriteAllText(path, "{");
            try { persistence.Load(); } catch (JsonException) { }
            File.Delete(path);
            try { persistence.Save(); check(false, "failed backup must prevent publication"); }
            catch (FileNotFoundException) { }
            check(!File.Exists(path), "failed preservation does not publish a replacement score file");
        }
        finally { Directory.Delete(directory, recursive: true); }
        Console.WriteLine("  Score file recovery checks complete.");
    }
}
