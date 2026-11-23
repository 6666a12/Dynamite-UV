using Avalonia;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;

namespace DynamiteUniverse.Editor.Shell;

/// <summary>Vector cover overlay in 1920×1080 design coordinates, shared by preview and export.</summary>
internal static class CoverArtwork
{
    private const double Width = 1920;
    private const double Height = 1080;
    private const double TopHeight = 40;
    private const double FooterTop = 942;
    private const double CopyInset = 36;
    private const double Radius = 105;
    private const double BadgeScale = Radius / 130;

    private static readonly Color Cyan = Color.Parse("#35E0FF");
    private static readonly Color Pink = Color.Parse("#FF4D8F");
    private static readonly Color Ink = Color.Parse("#0A0E1A");
    private static readonly Color RailBlue = Color.Parse("#8CC9E4");
    private static readonly FontFamily CoverFont = new(
        "avares://DynaMakerUv.Editor.Shell/Assets/SpaceGrotesk-Medium.ttf#Space Grotesk, Microsoft YaHei, Noto Sans CJK SC, PingFang SC");
    private static readonly Typeface CoverBold = new(CoverFont, weight: FontWeight.Bold);

    private static readonly IBrush Gloss = Gradient(Colors.White, .34, Colors.Black, .30,
        new GradientStop(Color.FromArgb(0, 255, 255, 255), .5));
    private static readonly IBrush HeaderPlate = Gradient(Color.Parse("#161F38"), .92, Ink, .92);
    private static readonly IBrush FooterPlate = Gradient(Color.Parse("#161F38"), .97, Ink, .97);
    private static readonly Geometry BadgeFace = Diamond(Radius);
    private static readonly Geometry BadgeShade = Diamond(Radius + 8 * BadgeScale);
    private static readonly Geometry BadgeGlow = DiamondRing(Radius, 11 * BadgeScale);
    private static readonly Geometry BadgeRim = DiamondRing(Radius, 5 * BadgeScale);
    private static readonly Geometry BadgeInset = DiamondRing(Radius - 15 * BadgeScale, 1.6 * BadgeScale);
    private static readonly Geometry BrandSlashes = Geometry.Parse("M-26 22L-6 -22H8L-12 22ZM2 22L22 -22H31L11 22Z");
    private static readonly BlurEffect RimBlur = new() { Radius = 7 * BadgeScale };
    private static readonly BlurEffect ShadowBlur = new() { Radius = 12 * BadgeScale };
    private static readonly BlurEffect RailBlur = new() { Radius = 7 };

    private static readonly (double Start, double End)[] HeaderAccent = [(0, 221), (1699, Width)];
    private static readonly (double Start, double End)[] FooterAccent = [(0, 172), (1748, Width)];

    public static void DrawTheme(DrawingContext context, bool topBar, string title, string artist, string noter)
    {
        // Keep the bars and their illumination inside their own plates. The rest of the cover is transparent.
        if (topBar)
        {
            var header = new Rect(0, 0, Width, TopHeight);
            using var clip = context.PushClip(header);
            context.FillRectangle(HeaderPlate, header);
            Rail(context, 39, 2, Color.Parse("#486782"), 1, [(0, 293), (314, 843), (1077, 1606), (1627, Width)]);
            GlowRail(context, HeaderAccent, 39, 6);
            Rail(context, 29, 1.2, RailBlue, .50, [(0, 152), (174, 274), (1646, 1746), (1768, Width)]);
            Rail(context, 29, 2.5, Pink, 1, [(285, 301), (1619, 1635)]);
        }

        var footer = new Rect(0, FooterTop, Width, Height - FooterTop);
        using var footerClip = context.PushClip(footer);
        context.FillRectangle(FooterPlate, footer);
        Rail(context, 943, 2, Color.Parse("#516B86"), 1, [(0, Width)]);
        GlowRail(context, FooterAccent, 943, 7);
        Rail(context, 953, 1.25, RailBlue, .60, [(0, 107), (125, 274), (1646, 1795), (1813, Width)]);
        Rail(context, 953, 2.7, Pink, 1, [(286, 309), (1611, 1634)]);
        var mutedRail = Color.Parse("#6389A7");
        Rail(context, 953, 1.2, mutedRail, .44, [(320, 751), (1169, 1600)]);
        Rail(context, 1065, 1.2, mutedRail, .44, [(0, 451), (1469, Width)]);

        // Independent text columns prevent very long credits and titles from overlapping.
        DrawText(context, "NOTER", CopyInset, 992, 18, Color.Parse("#89E9F9"), 720, 3.1);
        DrawText(context, noter, CopyInset, 1030, 28, Color.Parse("#EDF5FF"), 720);
        DrawText(context, title, Width - CopyInset, 1010, 62, Color.Parse("#F0F6FF"), 1100, -1.2, TextAlignment.Right);
        DrawText(context, artist, Width - CopyInset, 1054, 32, Color.Parse("#CFE6F8"), 1100, 0, TextAlignment.Right);
    }

    public static void DrawBadge(DrawingContext context, Point center, bool icon, string label, string level, Color tone)
    {
        using var transform = context.PushTransform(Matrix.CreateTranslation(center.X, center.Y));
        if (icon) tone = Cyan;

        // Only the local shadow and luminous rim enter the blur layers. No full-canvas fill or mask.
        using (context.PushEffect(ShadowBlur, BadgeShade.Bounds))
            context.DrawGeometry(Brush(Color.Parse("#050913"), .40), null, BadgeShade);

        context.DrawGeometry(Brush(Ink, icon ? .90 : .64), null, BadgeFace);
        if (!icon) context.DrawGeometry(Brush(tone, .55), null, BadgeFace);
        context.DrawGeometry(Gloss, null, BadgeFace);
        using (context.PushEffect(RimBlur, BadgeGlow.Bounds))
            context.DrawGeometry(Brush(tone, .60), null, BadgeGlow);
        context.DrawGeometry(Brush(tone), null, BadgeRim);
        context.DrawGeometry(Brush(Colors.White, .65), null, BadgeInset);
        DrawInsetHighlights(context);

        if (icon)
            DrawBrand(context);
        else
        {
            DrawText(context, level, 0, 14 * BadgeScale, 92 * BadgeScale, Colors.White, 135,
                -2.5 * BadgeScale, TextAlignment.Center, 3.5 * BadgeScale);
            DrawText(context, label, 0, 56 * BadgeScale, 19 * BadgeScale, Colors.White, 112,
                1.6 * BadgeScale, TextAlignment.Center, 2.5 * BadgeScale);
        }
    }

    private static void DrawBrand(DrawingContext context)
    {
        var frame = new Rect(-46 * BadgeScale, -72 * BadgeScale, 92 * BadgeScale, 92 * BadgeScale);
        context.DrawRectangle(null, new Pen(Brush(Colors.White, .92), 5 * BadgeScale), frame, 22 * BadgeScale, 22 * BadgeScale);
        var slashTransform = Matrix.CreateScale(1.05 * BadgeScale, 1.05 * BadgeScale) *
            Matrix.CreateTranslation(0, -26 * BadgeScale);
        using (context.PushTransform(slashTransform)) context.DrawGeometry(Brush(Cyan), null, BrandSlashes);
        DrawText(context, "DYNAMAKER UV", 0, 56 * BadgeScale, 15 * BadgeScale, Color.Parse("#E9F6FF"),
            112, 2 * BadgeScale, TextAlignment.Center);
    }

    private static void DrawInsetHighlights(DrawingContext context)
    {
        var inset = Radius - 15 * BadgeScale;
        var pen = new Pen(Brush(Colors.White), 4.5 * BadgeScale);
        context.DrawLine(pen, new Point(-.12 * inset, -.88 * inset), new Point(-.34 * inset, -.66 * inset));
        context.DrawLine(pen, new Point(.66 * inset, .34 * inset), new Point(.88 * inset, .12 * inset));
    }

    private static void DrawText(DrawingContext context, string value, double x, double baseline, double size,
        Color color, double maxWidth, double spacing = 0, TextAlignment anchor = TextAlignment.Left, double outline = 0)
    {
        if (string.IsNullOrWhiteSpace(value)) return;
        // Use Avalonia's shaping/fallback for complete runs, including mixed Latin and CJK credits.
        TextLayout Layout(IBrush brush) => new(value, CoverBold, size, brush,
            maxWidth: maxWidth, maxLines: 1, textTrimming: TextTrimming.CharacterEllipsis, letterSpacing: spacing);
        using var text = Layout(Brush(color));
        var origin = new Point(x - (anchor switch
        {
            TextAlignment.Right => text.Width,
            TextAlignment.Center => text.Width / 2,
            _ => 0
        }), baseline - text.Baseline);

        if (outline > 0)
        {
            // A soft dark keyline keeps the numbers readable on lighter glass and artwork.
            using var keyline = Layout(Brush(Ink));
            using var opacity = context.PushOpacity(.45);
            keyline.Draw(context, origin + new Vector(outline, 0));
            keyline.Draw(context, origin - new Vector(outline, 0));
            keyline.Draw(context, origin + new Vector(0, outline));
            keyline.Draw(context, origin - new Vector(0, outline));
        }
        text.Draw(context, origin);
    }

    private static void GlowRail(DrawingContext context, (double Start, double End)[] segments, double y, double width)
    {
        using (context.PushEffect(RailBlur, new Rect(0, y - width / 2, Width, width)))
            Rail(context, y, width, Cyan, .42, segments);
        Rail(context, y, 2.8, Cyan, 1, segments);
    }

    private static void Rail(DrawingContext context, double y, double width, Color color, double opacity,
        ReadOnlySpan<(double Start, double End)> segments)
    {
        var brush = Brush(color, opacity);
        foreach (var (start, end) in segments)
            context.FillRectangle(brush, new Rect(start, y - width / 2, end - start, width));
    }

    private static Geometry Diamond(double radius) => DiamondRing(radius, 0);

    private static Geometry DiamondRing(double radius, double width)
    {
        var geometry = new StreamGeometry();
        using var path = geometry.Open();
        path.SetFillRule(FillRule.EvenOdd);
        // Normal half-width projected onto a diamond's horizontal/vertical axes.
        var delta = width / Math.Sqrt(2);
        AddDiamond(radius + delta);
        if (width > 0) AddDiamond(radius - delta);
        return geometry;

        void AddDiamond(double r)
        {
            path.BeginFigure(new Point(0, -r));
            path.LineTo(new Point(r, 0));
            path.LineTo(new Point(0, r));
            path.LineTo(new Point(-r, 0));
            path.EndFigure(true);
        }
    }

    private static IBrush Gradient(Color first, double firstOpacity, Color last, double lastOpacity, GradientStop? middle = null)
    {
        var gradient = new LinearGradientBrush
        {
            StartPoint = RelativePoint.TopLeft,
            EndPoint = RelativePoint.BottomRight,
            GradientStops = { new GradientStop(Alpha(first, firstOpacity), 0), new GradientStop(Alpha(last, lastOpacity), 1) }
        };
        if (middle is not null) gradient.GradientStops.Insert(1, middle);
        return gradient;
    }

    private static IBrush Brush(Color color, double opacity = 1) => new SolidColorBrush(Alpha(color, opacity));
    private static Color Alpha(Color color, double opacity) => Color.FromArgb((byte)Math.Round(color.A * opacity), color.R, color.G, color.B);
}
