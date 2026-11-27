using DynamiteUniverse.Shared.Storyboard;
using Godot;
using System.Numerics;

namespace DynamiteUniverse.Game;

/// <summary>Optional single-page atlas for the ordered, unclipped rectangle path.</summary>
internal sealed class StoryboardTextureAtlas(ImageTexture texture, Dictionary<StoryboardImage, Rect2> regions) : IDisposable
{
    private const int PageLimit = 1024;
    private const int SpriteLimit = 256;
    private const int BytesPerPixel = 4;
    public ImageTexture Texture { get; } = texture;
    public int TextureCount => regions.Count;
    public int PixelCount { get; } = texture.GetWidth() * texture.GetHeight();

    public bool TryGetRegion(StoryboardImage binding, out Rect2 region) => regions.TryGetValue(binding, out region);
    public void Dispose() => Texture.Dispose();

    public static StoryboardTextureAtlas? TryCreate(IReadOnlyDictionary<StoryboardImage, Texture2D> textures)
    {
        var candidates = new List<Candidate>();
        foreach (var (binding, texture) in textures)
        {
            if (binding.Crop is not null || texture is not ImageTexture source) continue;
            var width = source.GetWidth();
            var height = source.GetHeight();
            if (width is > 0 and <= SpriteLimit && height is > 0 and <= SpriteLimit)
                candidates.Add(new(binding, source, width, height));
        }
        if (candidates.Count < 2) return null;
        candidates.Sort(static (a, b) =>
        {
            var height = b.Height.CompareTo(a.Height);
            if (height != 0) return height;
            var width = b.Width.CompareTo(a.Width);
            return width != 0 ? width : StringComparer.Ordinal.Compare(a.Binding.AssetId, b.Binding.AssetId);
        });

        // Place native-size images before reading them back; one small page, no mipmaps or resampling.
        var placements = new List<Placement>(candidates.Count);
        int x = 0, y = 0, shelfHeight = 0, usedWidth = 0;
        foreach (var entry in candidates)
        {
            var width = entry.Width + 2;
            var height = entry.Height + 2;
            if (x + width > PageLimit)
            {
                if (y + shelfHeight + height > PageLimit) continue;
                x = 0;
                y += shelfHeight;
                shelfHeight = 0;
            }

            placements.Add(new(entry, x + 1, y + 1));
            x += width;
            usedWidth = Math.Max(usedWidth, x);
            shelfHeight = Math.Max(shelfHeight, height);
        }
        if (placements.Count < 2) return null;
        var pageWidth = (int)BitOperations.RoundUpToPowerOf2((uint)usedWidth);
        var pageHeight = (int)BitOperations.RoundUpToPowerOf2((uint)(y + shelfHeight));
        var pixels = new byte[pageWidth * pageHeight * BytesPerPixel];
        var regions = new Dictionary<StoryboardImage, Rect2>(placements.Count);
        foreach (var (entry, left, top) in placements)
        {
            // Prototype: read back only placed images, at mount. For deployment, prefer feeding
            // decoded PNGs from the loader if this one-time GPU synchronization is material.
            using var image = entry.Source.GetImage();
            if (image is null || image.IsEmpty() || image.HasMipmaps() || image.GetWidth() != entry.Width ||
                image.GetHeight() != entry.Height || image.GetFormat() is not (Image.Format.Rgb8 or Image.Format.Rgba8))
                continue;
            if (image.GetFormat() == Image.Format.Rgb8) image.Convert(Image.Format.Rgba8);
            var data = image.GetData();
            if (data.Length != entry.Width * entry.Height * BytesPerPixel) continue;
            CopyWithGutter(data, entry.Width, entry.Height, pixels, pageWidth, left, top);
            regions.Add(entry.Binding, new Rect2(left, top, entry.Width, entry.Height));
        }
        if (regions.Count < 2) return null;
        using var page = Image.CreateFromData(pageWidth, pageHeight, false, Image.Format.Rgba8, pixels);
        if (page is null || page.IsEmpty()) return null;
        var atlasTexture = ImageTexture.CreateFromImage(page);
        if (atlasTexture is null) return null;
        if (atlasTexture.GetWidth() == pageWidth && atlasTexture.GetHeight() == pageHeight)
            return new(atlasTexture, regions);
        atlasTexture.Dispose();
        return null;
    }

    private static void CopyWithGutter(ReadOnlySpan<byte> source, int width, int height, Span<byte> page,
        int pageWidth, int left, int top)
    {
        var spriteRow = width * BytesPerPixel;
        var paddedRow = spriteRow + 2 * BytesPerPixel;
        var pageRow = pageWidth * BytesPerPixel;
        var start = (top * pageWidth + left - 1) * BytesPerPixel;
        for (var y = 0; y < height; y++)
        {
            var row = source.Slice(y * spriteRow, spriteRow);
            var target = page.Slice(start + y * pageRow, paddedRow);
            row.CopyTo(target.Slice(BytesPerPixel, spriteRow));
            row[..BytesPerPixel].CopyTo(target);
            row[^BytesPerPixel..].CopyTo(target[^BytesPerPixel..]);
        }
        // Duplicate the RGBA corners as well, to match clamp-to-edge at fractional UVs.
        page.Slice(start, paddedRow).CopyTo(page.Slice(start - pageRow, paddedRow));
        page.Slice(start + (height - 1) * pageRow, paddedRow).CopyTo(page.Slice(start + height * pageRow, paddedRow));
    }

    private readonly record struct Candidate(StoryboardImage Binding, ImageTexture Source, int Width, int Height);
    private readonly record struct Placement(Candidate Entry, int Left, int Top);
}
