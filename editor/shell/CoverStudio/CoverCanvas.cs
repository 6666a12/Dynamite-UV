using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace DynamiteUniverse.Editor.Shell;

internal enum CoverCanvasMode { Crop, CropPreview, Layout, Framing, WideImage, Overlay }

internal sealed class CoverCanvas : Control
{
    private static readonly IBrush Mat = new SolidColorBrush(Color.Parse("#202634"));
    private static readonly IBrush Shade = new SolidColorBrush(Color.Parse("#aa070c15"));
    private static readonly IPen Accent = new Pen(new SolidColorBrush(Color.Parse("#59e4f5")), 1.5);
    private static readonly IPen Thirds = new Pen(new SolidColorBrush(Color.Parse("#80c2eaf1")), 1);
    private static readonly IPen Selection = new Pen(Brushes.White, 1, DashStyle.Dash);
    private static readonly IPen Guide = new Pen(Brushes.HotPink, 1, DashStyle.Dash);
    private Bitmap? _image;
    private CoverLayout? _layout;
    private CoverCanvasMode _mode;
    private string? _selected;
    private CoverGuide[] _guides = [];
    private Viewport GetViewport() => new(Bounds.Size, LogicalSize);

    internal static Size ImagePixels(Bitmap image) => new(image.PixelSize.Width, image.PixelSize.Height);

    private Size LogicalSize => (_mode, _image, _layout) switch
    {
        (CoverCanvasMode.Crop, not null, _) => ImagePixels(_image),
        (CoverCanvasMode.CropPreview, not null, not null) => _layout.Crop.Size,
        (CoverCanvasMode.Crop or CoverCanvasMode.CropPreview, _, _) => new(1, 1),
        _ => CoverLayout.WideSize,
    };

    public void Update(CoverLayout layout, Bitmap? image, CoverCanvasMode mode, string? selected = null, CoverGuide[]? guides = null)
    {
        _layout = layout;
        _image = image;
        _mode = mode;
        _selected = selected;
        _guides = guides ?? [];
        InvalidateVisual();
    }

    public double Scale => GetViewport().Scale;
    public Point ToLogical(Point position) => GetViewport().ToLogical(position);
    public bool ContainsImagePoint(Point position) => GetViewport().Bounds.Contains(position);

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        if (_layout is not { } state) return;
        var viewport = GetViewport();
        if (viewport.Scale <= 0) return;
        var screen = viewport.Bounds;
        using var clip = context.PushClip(screen);
        if (_mode is CoverCanvasMode.Crop or CoverCanvasMode.CropPreview)
        {
            if (_image is null) { context.DrawRectangle(Mat, null, screen); return; }
            var crop = state.Crop;
            var imageSize = _image.Size;
            var px = ImagePixels(_image);
            var sx = imageSize.Width / px.Width;
            var sy = imageSize.Height / px.Height;
            var source = new Rect(crop.X*sx, crop.Y*sy, crop.Width*sx, crop.Height*sy);
            context.DrawImage(_image, _mode == CoverCanvasMode.CropPreview ? source : new Rect(imageSize), screen);
            if (_mode == CoverCanvasMode.Crop) DrawCrop(context, screen, viewport.ToView(crop));
            return;
        }

        var preview = _mode is CoverCanvasMode.Layout or CoverCanvasMode.Framing;
        if (preview) context.DrawRectangle(Mat, null, screen);
        using (context.PushTransform(Matrix.CreateScale(viewport.Scale, viewport.Scale) * Matrix.CreateTranslation(screen.X, screen.Y)))
        {
            if (_image is not null && _mode != CoverCanvasMode.Overlay)
                context.DrawImage(_image, new Rect(_image.Size), CoverLayout.WideImage(_image.Size, state.Zoom, state.Framing));
            if (_mode != CoverCanvasMode.Framing || _image is not null)
            {
                using var faded = context.PushOpacity(_mode == CoverCanvasMode.Framing ? .23 : 1);
                CoverArtwork.DrawTheme(context, state.TopBar, state.Title, state.Artist, state.Noter);
                foreach (var b in state.Badges.Where(b => b.Visible))
                    CoverArtwork.DrawBadge(context, b.Center, b.Icon, b.Label, b.Level, b.Color);
            }
        }
        if (_mode == CoverCanvasMode.Layout)
        {
            foreach (var guide in _guides)
            {
                var anchor = new Point(guide.Normal.X * guide.Distance, guide.Normal.Y * guide.Distance);
                var tangent = new Vector(-guide.Normal.Y, guide.Normal.X) * 4000;
                context.DrawLine(Guide, viewport.ToView(anchor - tangent), viewport.ToView(anchor + tangent));
            }
            var selected = state.Badges.FirstOrDefault(b => b.Id == _selected && b.Visible);
            if (selected is not null) DrawDiamond(context, viewport.ToView(selected.Center), (CoverLayout.Radius + 8) * viewport.Scale);
        }
    }

    private static void DrawCrop(DrawingContext context, Rect bounds, Rect frame)
    {
        if (frame.Top > bounds.Top) context.DrawRectangle(Shade, null, new Rect(bounds.X, bounds.Y, bounds.Width, frame.Top - bounds.Top));
        if (frame.Bottom < bounds.Bottom) context.DrawRectangle(Shade, null, new Rect(bounds.X, frame.Bottom, bounds.Width, bounds.Bottom - frame.Bottom));
        if (frame.Left > bounds.Left) context.DrawRectangle(Shade, null, new Rect(bounds.X, frame.Y, frame.Left - bounds.Left, frame.Height));
        if (frame.Right < bounds.Right) context.DrawRectangle(Shade, null, new Rect(frame.Right, frame.Y, bounds.Right - frame.Right, frame.Height));
        context.DrawRectangle(null, Accent, frame);
        for (var i = 1; i < 3; i++)
        {
            var x = frame.X + frame.Width * i / 3;
            var y = frame.Y + frame.Height * i / 3;
            context.DrawLine(Thirds, new(x, frame.Top), new(x, frame.Bottom));
            context.DrawLine(Thirds, new(frame.Left, y), new(frame.Right, y));
        }
        context.DrawRectangle(Accent.Brush, null, new Rect(frame.Right - 5, frame.Bottom - 5, 10, 10));
    }

    private static void DrawDiamond(DrawingContext context, Point p, double r)
    {
        Point[] points = [new(p.X, p.Y-r), new(p.X+r, p.Y), new(p.X, p.Y+r), new(p.X-r, p.Y)];
        for (var i = 0; i < 4; i++) context.DrawLine(Selection, points[i], points[(i+1)%4]);
    }

    private readonly struct Viewport(Size screen, Size logical)
    {
        public double Scale { get; } = Math.Min(screen.Width / logical.Width, screen.Height / logical.Height);
        public Rect Bounds => new((screen.Width-logical.Width*Scale)/2, (screen.Height-logical.Height*Scale)/2,
            logical.Width*Scale, logical.Height*Scale);
        public Point ToView(Point p) => Bounds.Position + new Vector(p.X, p.Y) * Scale;
        public Rect ToView(Rect rect) => new(ToView(rect.Position), new Size(rect.Width * Scale, rect.Height * Scale));
        public Point ToLogical(Point p) => new((p.X-Bounds.X)/Scale, (p.Y-Bounds.Y)/Scale);
    }
}
