using Avalonia;
using Avalonia.Media.Imaging;
using DynamiteUniverse.ChartEditor.Core;
using DynamiteUniverse.Shared.Chart.V2;
using SkiaSharp;

namespace DynamiteUniverse.Editor.Shell;

internal static class CoverFiles
{
    public static string? Resolve(EditorDocument document, EditorProjectDraft? draft)
    {
        if (document.Cover is not { } relative) return null;
        if (document.PendingResourceReplacements.FirstOrDefault(item =>
                string.Equals(item.PackageRelativePath, relative, StringComparison.OrdinalIgnoreCase)) is { } pending)
            return pending.ExternalSourcePath;
        if (document.PackageDirectory is { } root) return V2PackageWriter.ResolveRegularFile(root, relative);
        return draft?.ExternalResources.TryGetValue(relative, out var path) == true ? path : null;
    }

    public static Bitmap Load(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length is < 16 or > 32 * 1024 * 1024) throw new IOException("Expected an image up to 32 MiB.");
        // Decode the same inspected bytes; never reopen a potentially changed path.
        var bytes = new byte[(int)stream.Length];
        stream.ReadExactly(bytes);
        using var data = SKData.CreateCopy(bytes);
        using var codec = SKCodec.Create(data);
        if (codec is null || codec.EncodedFormat is not (SKEncodedImageFormat.Png or SKEncodedImageFormat.Jpeg or SKEncodedImageFormat.Webp) ||
            codec.FrameCount > 1 || codec.Info.Width is < 1 or > 8192 || codec.Info.Height is < 1 or > 8192 ||
            (long)codec.Info.Width * codec.Info.Height > 36_000_000)
            throw new IOException("Expected a static PNG, JPEG or WebP (max. 8192 px per side and 36 megapixels).");
        using var content = new MemoryStream(bytes, writable: false);
        return new Bitmap(content);
    }

    public static byte[] Encode(CoverLayout layout, Bitmap? image, CoverCanvasMode mode)
    {
        var size = mode == CoverCanvasMode.CropPreview ? layout.Crop.Size : CoverLayout.WideSize;
        var canvas = new CoverCanvas();
        canvas.Update(layout, image, mode);
        canvas.Measure(size);
        canvas.Arrange(new Rect(size));
        using var render = new RenderTargetBitmap(new PixelSize((int)size.Width, (int)size.Height), new Vector(96, 96));
        render.Render(canvas);
        using var output = new MemoryStream();
        render.Save(output, PngBitmapEncoderOptions.Default);
        return output.ToArray();
    }

    public static async Task SaveNewAsync(string path, byte[] bytes)
    {
        // Do not overwrite source art, an existing package asset, or an unrelated file.
        await using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true);
        await stream.WriteAsync(bytes);
    }
}
