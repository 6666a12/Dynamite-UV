using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using DynamiteUniverse.ChartEditor.Core;
using DynamiteUniverse.Shared.Chart.V2;
using SkiaSharp;

namespace DynamiteUniverse.Editor.Shell;

internal sealed class CoverDestinationException(string reason) : IOException(reason);

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

    public static SKBitmap Capture(CoverLayout layout, Bitmap? image, CoverCanvasMode mode)
    {
        var size = mode == CoverCanvasMode.CropPreview ? layout.Crop.Size : CoverLayout.WideSize;
        var canvas = new CoverCanvas();
        canvas.Update(layout, image, mode);
        canvas.Measure(size);
        canvas.Arrange(new Rect(size));
        using var render = new RenderTargetBitmap(new PixelSize((int)size.Width, (int)size.Height), new Vector(96, 96));
        render.Render(canvas);
        // Copy on the UI thread; the resulting Skia bitmap owns its pixels and does not reference any visual or source image.
        var pixels = new SKBitmap(new SKImageInfo(render.PixelSize.Width, render.PixelSize.Height,
            SKColorType.Bgra8888, SKAlphaType.Premul));
        try
        {
            if (pixels.GetPixels() == IntPtr.Zero) throw new IOException("Cannot allocate cover pixels.");
            using var target = new PixelBuffer(pixels, render.PixelSize);
            render.CopyPixels(target);
            return pixels;
        }
        catch { pixels.Dispose(); throw; }
    }

    private sealed class PixelBuffer(SKBitmap pixels, PixelSize size) : ILockedFramebuffer
    {
        public IntPtr Address => pixels.GetPixels();
        public int RowBytes => pixels.RowBytes;
        public PixelSize Size => size;
        public Vector Dpi => new(96, 96);
        public PixelFormat Format => PixelFormat.Bgra8888;
        public AlphaFormat AlphaFormat => AlphaFormat.Premul;
        public void Dispose() { } // The caller owns the pixel buffer independently.
    }

    public static string ValidateDestination(string path, string? packageDirectory)
    {
        path = Path.GetFullPath(path);
        if (!string.Equals(Path.GetExtension(path), ".png", StringComparison.OrdinalIgnoreCase))
            throw new CoverDestinationException("Cover.PngRequired");
        var parent = Path.GetDirectoryName(path) ?? throw new IOException("Missing destination directory.");
        RejectLinkedAncestors(parent);
        if (packageDirectory is not null)
        {
            RejectLinkedAncestors(packageDirectory);
            var relative = Path.GetRelativePath(packageDirectory, parent);
            if (!Path.IsPathRooted(relative) && relative != ".." && !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal))
                throw new CoverDestinationException("Cover.OutsidePackage");
        }
        if (File.Exists(path) || Directory.Exists(path)) throw new CoverDestinationException("Cover.NewFile");
        return path;
    }

    private static void RejectLinkedAncestors(string path)
    {
        for (DirectoryInfo? dir = new(path); dir is not null; dir = dir.Parent)
        {
            if (!dir.Exists) throw new DirectoryNotFoundException(dir.FullName);
            if ((dir.Attributes & FileAttributes.ReparsePoint) != 0 || dir.LinkTarget is not null)
                throw new CoverDestinationException("Cover.LinkedDirectory");
        }
    }

    public static Task ExportAsync(string path, string? packageDirectory, SKBitmap pixels, CancellationToken token) =>
        Task.Run(() => Publish(path, packageDirectory, stream =>
        {
            if (!pixels.Encode(stream, SKEncodedImageFormat.Png, 100)) throw new IOException("Could not encode PNG.");
        }, token), token);

    internal static void Publish(string path, string? packageDirectory, Action<Stream> write, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        path = ValidateDestination(path, packageDirectory);
        var staging = Path.Combine(Path.GetDirectoryName(path)!, $".uv-cover-{Guid.NewGuid():N}.tmp");
        var ownsStaging = false;
        try
        {
            using (var stream = new FileStream(staging, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                ownsStaging = true;
                write(stream);
            }
            token.ThrowIfCancellationRequested();
            ValidateDestination(path, packageDirectory);
            File.Move(staging, path, overwrite: false);
            ownsStaging = false;
        }
        finally
        {
            if (ownsStaging)
                try { File.Delete(staging); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
        }
    }
}
