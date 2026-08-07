using Godot;

namespace DuxCommunity.Game;

/// <summary>运行时资源加载：res:// 与用户目录（user://，无 .import）通吃。</summary>
public static class Res
{
    public static AudioStream? LoadAudio(string path)
    {
        if (!Godot.FileAccess.FileExists(path))
        {
            GD.PushError($"Res.LoadAudio: 文件不存在 {path}");
            return null;
        }
        var ext = path.GetExtension().ToLowerInvariant();
        AudioStream? stream = ext switch
        {
            "wav" => AudioStreamWav.LoadFromFile(path),
            "ogg" => AudioStreamOggVorbis.LoadFromFile(path),
            "mp3" => AudioStreamMP3.LoadFromFile(path),
            _ => null,
        };
        if (stream == null)
            GD.PushError($"Res.LoadAudio: 不支持的音频格式 {path}");
        return stream;
    }

    public static Texture2D? LoadTexture(string path)
    {
        // res:// 且有导入产物的走资源管线（导出包兼容）；否则按原始图片文件读（user:// 或未导入文件）
        if (path.StartsWith("res://") && ResourceLoader.Exists(path))
            return GD.Load<Texture2D>(path);
        if (!Godot.FileAccess.FileExists(path))
            return null;
        var img = Image.LoadFromFile(path);
        return img == null ? null : ImageTexture.CreateFromImage(img);
    }
}
