using System.Text;

namespace DynamiteUniverse.Game;

/// <summary>
/// Writes a complete UTF-8 snapshot beside the destination before publishing it. The current
/// settings file is never opened for truncation. Same-directory rename stays on the same volume.
/// Godot user:// must be globalized by the caller (never use this for virtual res:// resources).
/// </summary>
public static class SettingsFilePersistence
{
    public static void Write(string destination, string content) =>
        Write(destination, content, (staged, target) => File.Move(staged, target, overwrite: true));

    // The publication seam lets non-Godot tests force a failed replacement without changing ACLs.
    internal static void Write(string destination, string content, Action<string, string> publish)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destination);
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(publish);
        var target = Path.GetFullPath(destination);
        var directory = Path.GetDirectoryName(target)!;
        var staged = Path.Combine(directory, $".{Path.GetFileName(target)}.{Guid.NewGuid():N}.tmp");
        var owned = false;
        try
        {
            using (var stream = new FileStream(staged, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                owned = true;
                var bytes = new UTF8Encoding(false, true).GetBytes(content);
                stream.Write(bytes);
                stream.Flush(flushToDisk: true);
            }
            publish(staged, target);
        }
        finally
        {
            // Delete only our uniquely named staging file. Cleanup must not mask publication errors.
            if (owned)
            {
                try { File.Delete(staged); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
    }
}
