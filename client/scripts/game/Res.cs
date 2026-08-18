using Godot;

namespace DynamiteUniverse.Game;

/// <summary>运行时资源加载：res:// 与用户目录（user://，无 .import）通吃。</summary>
public static class Res
{
    private const ulong MaxRawTextureBytes = 8UL * 1024 * 1024;
    private const int MaxRawTextureWidth = 4096;
    private const int MaxRawTextureHeight = 4096;
    private const long MaxRawTexturePixels = 16_000_000;

    public static AudioStream? LoadAudio(string path)
    {
        // Exported res:// audio is stored as an imported/remapped Godot resource;
        // the original wav/ogg/mp3 file may no longer exist in the APK.
        if (path.StartsWith("res://") && ResourceLoader.Exists(path))
            return GD.Load<AudioStream>(path);

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
        // Trusted project resources keep the imported Godot resource path and existing fallback.
        if (path.StartsWith("res://", StringComparison.Ordinal))
        {
            if (ResourceLoader.Exists(path))
            {
                var imported = GD.Load<Texture2D>(path);
                if (imported == null)
                    GD.PushWarning($"Res.LoadTexture: 无法解码已导入图片 {path}");
                return imported;
            }

            if (!Godot.FileAccess.FileExists(path))
            {
                GD.PushWarning($"Res.LoadTexture: 文件不存在 {path}");
                return null;
            }
            var trustedImage = Image.LoadFromFile(path);
            if (trustedImage == null || trustedImage.IsEmpty())
            {
                GD.PushWarning($"Res.LoadTexture: 无法解码图片 {path}");
                return null;
            }
            return ImageTexture.CreateFromImage(trustedImage);
        }

        var extension = path.GetExtension().ToLowerInvariant();
        if (extension is not ("png" or "jpg" or "jpeg" or "webp"))
        {
            GD.PushWarning($"Res.LoadTexture: unsupported raw image extension {path}");
            return null;
        }

        using var file = Godot.FileAccess.Open(path, Godot.FileAccess.ModeFlags.Read);
        if (file == null)
        {
            GD.PushWarning($"Res.LoadTexture: unable to open image {path}");
            return null;
        }

        var byteLength = file.GetLength();
        if (byteLength <= 0 || byteLength > MaxRawTextureBytes)
        {
            GD.PushWarning(
                $"Res.LoadTexture: raw image size {byteLength} bytes is outside the allowed range {path}");
            return null;
        }

        var bufferLength = (long)byteLength; // Capped above, so the cast is safe.
        var bytes = file.GetBuffer(bufferLength);
        if (bytes.LongLength != bufferLength)
        {
            GD.PushWarning($"Res.LoadTexture: unable to read complete image {path}");
            return null;
        }

        if (!TryReadRawImageDimensions(extension, bytes, out var headerWidth, out var headerHeight) ||
            !DimensionsWithinLimits(headerWidth, headerHeight))
        {
            GD.PushWarning(
                $"Res.LoadTexture: encoded image dimensions are invalid or exceed limits {path}");
            return null;
        }

        var image = new Image();
        var error = extension switch
        {
            "png" => image.LoadPngFromBuffer(bytes),
            "jpg" or "jpeg" => image.LoadJpgFromBuffer(bytes),
            "webp" => image.LoadWebpFromBuffer(bytes),
            _ => Error.FileUnrecognized,
        };
        if (error != Error.Ok || image.IsEmpty())
        {
            GD.PushWarning($"Res.LoadTexture: unable to decode image {path} ({error})");
            return null;
        }

        var width = image.GetWidth();
        var height = image.GetHeight();
        if (!DimensionsWithinLimits(width, height))
        {
            GD.PushWarning(
                $"Res.LoadTexture: decoded image dimensions {width}x{height} exceed limits {path}");
            return null;
        }

        return ImageTexture.CreateFromImage(image);
    }

    private static bool DimensionsWithinLimits(int width, int height) =>
        width > 0 && height > 0 &&
        width <= MaxRawTextureWidth && height <= MaxRawTextureHeight &&
        (long)width * height <= MaxRawTexturePixels;

    private static bool TryReadRawImageDimensions(string extension, byte[] bytes,
        out int width, out int height)
    {
        width = 0;
        height = 0;
        return extension switch
        {
            "png" => TryReadPngDimensions(bytes, out width, out height),
            "jpg" or "jpeg" => TryReadJpegDimensions(bytes, out width, out height),
            "webp" => TryReadWebpDimensions(bytes, out width, out height),
            _ => false,
        };
    }

    private static bool TryReadPngDimensions(byte[] bytes, out int width, out int height)
    {
        width = 0;
        height = 0;
        if (bytes.Length < 24 ||
            !bytes.AsSpan(0, 8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }) ||
            !bytes.AsSpan(12, 4).SequenceEqual("IHDR"u8))
            return false;
        width = ReadBigEndianInt32(bytes, 16);
        height = ReadBigEndianInt32(bytes, 20);
        return true;
    }

    private static bool TryReadJpegDimensions(byte[] bytes, out int width, out int height)
    {
        width = 0;
        height = 0;
        if (bytes.Length < 4 || bytes[0] != 0xff || bytes[1] != 0xd8)
            return false;

        var offset = 2;
        while (offset + 3 < bytes.Length)
        {
            if (bytes[offset++] != 0xff)
                continue;
            while (offset < bytes.Length && bytes[offset] == 0xff)
                offset++;
            if (offset >= bytes.Length)
                return false;
            var marker = bytes[offset++];
            if (marker is 0xd8 or 0xd9 || marker is >= 0xd0 and <= 0xd7)
                continue;
            if (offset + 1 >= bytes.Length)
                return false;
            var length = (bytes[offset] << 8) | bytes[offset + 1];
            if (length < 2 || offset + length > bytes.Length)
                return false;
            if (marker is >= 0xc0 and <= 0xc3 or >= 0xc5 and <= 0xc7 or
                >= 0xc9 and <= 0xcb or >= 0xcd and <= 0xcf)
            {
                if (length < 7)
                    return false;
                height = (bytes[offset + 3] << 8) | bytes[offset + 4];
                width = (bytes[offset + 5] << 8) | bytes[offset + 6];
                return true;
            }
            offset += length;
        }
        return false;
    }

    private static bool TryReadWebpDimensions(byte[] bytes, out int width, out int height)
    {
        width = 0;
        height = 0;
        if (bytes.Length < 30 || !bytes.AsSpan(0, 4).SequenceEqual("RIFF"u8) ||
            !bytes.AsSpan(8, 4).SequenceEqual("WEBP"u8))
            return false;

        var chunk = bytes.AsSpan(12, 4);
        if (chunk.SequenceEqual("VP8X"u8))
        {
            width = 1 + ReadLittleEndian24(bytes, 24);
            height = 1 + ReadLittleEndian24(bytes, 27);
            return true;
        }
        if (chunk.SequenceEqual("VP8L"u8) && bytes.Length >= 25 && bytes[20] == 0x2f)
        {
            var packed = bytes[21] | (bytes[22] << 8) | (bytes[23] << 16) | (bytes[24] << 24);
            width = (packed & 0x3fff) + 1;
            height = ((packed >> 14) & 0x3fff) + 1;
            return true;
        }
        if (chunk.SequenceEqual("VP8 "u8) && bytes.Length >= 30 &&
            bytes[23] == 0x9d && bytes[24] == 0x01 && bytes[25] == 0x2a)
        {
            width = (bytes[26] | (bytes[27] << 8)) & 0x3fff;
            height = (bytes[28] | (bytes[29] << 8)) & 0x3fff;
            return true;
        }
        return false;
    }

    private static int ReadBigEndianInt32(byte[] bytes, int offset) =>
        (bytes[offset] << 24) | (bytes[offset + 1] << 16) |
        (bytes[offset + 2] << 8) | bytes[offset + 3];

    private static int ReadLittleEndian24(byte[] bytes, int offset) =>
        bytes[offset] | (bytes[offset + 1] << 8) | (bytes[offset + 2] << 16);
}
