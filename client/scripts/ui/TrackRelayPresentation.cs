using Godot;

namespace DuxCommunity.Ui;

public enum RelaySource
{
    Selection,
    Restart,
    NextSong,
}

/// <summary>Ephemeral presentation-only context for an opaque Track Handoff.</summary>
public sealed record TrackRelayPresentation(
    string Title,
    string Difficulty,
    Texture2D? Cover,
    RelaySource Source = RelaySource.Selection);

/// <summary>
/// One-entry decoded cover cache. It prevents SongSelect, handoff and Result from decoding the
/// current artwork independently without retaining an unbounded library of textures.
/// </summary>
public static class CoverTextureCache
{
    private static string? _path;
    private static Texture2D? _texture;

    public static Texture2D? Load(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            Clear();
            return null;
        }

        if (StringComparer.Ordinal.Equals(_path, path))
            return _texture;

        _path = path;
        _texture = Game.Res.LoadTexture(path);
        return _texture;
    }

    public static void Remember(string? path, Texture2D? texture)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            Clear();
            return;
        }

        _path = path;
        _texture = texture;
    }

    public static Texture2D? Current(string? path) =>
        !string.IsNullOrWhiteSpace(path) && StringComparer.Ordinal.Equals(_path, path)
            ? _texture
            : null;

    public static void Clear()
    {
        _path = null;
        _texture = null;
    }
}
