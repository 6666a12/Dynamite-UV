using System.Text;
using DynamiteUniverse.Game;

namespace CoreTests;

internal static class SettingsFilePersistenceTests
{
    public static void Run(Action<bool, string> check)
    {
        var directory = Path.Combine(Path.GetTempPath(), "du-settings-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var destination = Path.Combine(directory, "settings.json");
        try
        {
            SettingsFilePersistence.Write(destination, "{\"name\":\"配色\"}");
            check(File.ReadAllText(destination) == "{\"name\":\"配色\"}", "settings initial create preserves UTF-8");
            check(!File.ReadAllBytes(destination).AsSpan().StartsWith(new byte[] { 0xef, 0xbb, 0xbf }), "settings writes no BOM");
            SettingsFilePersistence.Write(destination, "{\"new\":true}");
            check(File.ReadAllText(destination) == "{\"new\":true}", "settings replacement publishes complete snapshot");
            var previous = File.ReadAllBytes(destination);
            var sawStaging = false;
            try
            {
                SettingsFilePersistence.Write(destination, "{\"attempt\":1}", (staged, target) =>
                {
                    sawStaging = Path.GetDirectoryName(staged) == directory && target == destination &&
                        File.ReadAllText(staged) == "{\"attempt\":1}" && File.ReadAllBytes(target).SequenceEqual(previous);
                    throw new IOException("Simulated publication failure");
                });
                check(false, "settings publication failure must escape");
            }
            catch (IOException)
            {
                check(sawStaging, "settings fully flushes sibling staging before publishing");
                check(File.ReadAllBytes(destination).SequenceEqual(previous), "settings failed publication preserves prior file");
            }
            try
            {
                SettingsFilePersistence.Write(destination, "\ud800");
                check(false, "settings invalid UTF-16 must reject encoding");
            }
            catch (EncoderFallbackException)
            {
                check(File.ReadAllBytes(destination).SequenceEqual(previous), "settings encoding failure cannot truncate current file");
            }
            var absent = Path.Combine(directory, "first-failed.json");
            try
            {
                SettingsFilePersistence.Write(absent, "{}", (_, _) => throw new IOException("First publication failed"));
            }
            catch (IOException) { }
            check(!File.Exists(absent), "failed first settings save publishes nothing");
            check(Directory.GetFiles(directory).Length == 1, "settings failed writes clean their owned staging files");
        }
        finally
        {
            // This unique directory was created by this test; never use a caller-provided path here.
            Directory.Delete(directory, recursive: true);
        }
        Console.WriteLine("  Settings staged persistence checks complete.");
    }
}
