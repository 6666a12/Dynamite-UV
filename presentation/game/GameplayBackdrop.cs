using Godot;
using DynamiteUniverse.Shared.Chart;

namespace DynamiteUniverse.Game;

public enum GameplayBackdropMode
{
    Procedural,
    StaticCache,
    Flat,
}

/// <summary>A still, diffuse ion field behind the playfield; no timing, note data or overlay capture.</summary>
public partial class GameplayBackdrop : Node2D
{
    private const int MaxCacheEdge = 4096;
    private const int MaxCachePixels = 10_000_000;
    private const int MaxBakeWaitFrames = 8;
    private static readonly Color FlatInk = new(.034f, .046f, .073f, 1f);
    private static Shader? _shader;
    private GameplayBackdropMode _renderMode;
    private ColorRect? _field;
    private ShaderMaterial? _fieldMaterial;
    private Sprite2D? _cacheQuad;
    private Window? _host;
    private SubViewport? _bake;
    private CacheLayout? _layout;
    private bool _attempted;
    private bool _waitingForDraw;
    private bool _drawCompleted;
    private int _waitFrames;

    public float TopPadding { get; set; }

    /// <summary>StaticCache and Flat are opt-in experiments; production uses Procedural.</summary>
    public GameplayBackdropMode RenderMode
    {
        get => _renderMode;
        set
        {
            if (_renderMode == value) return;
            _renderMode = value;
            if (IsNodeReady()) ApplyRenderMode();
        }
    }

    /// <summary>Zero while inactive, baking, or using the procedural fallback.</summary>
    public Vector2I StaticCachePixelSize => _cacheQuad is { } quad && quad.IsVisibleInTree() && _layout is { } layout
        ? layout.PixelSize : Vector2I.Zero;

    public override void _Ready()
    {
        _shader ??= new Shader { Code = EnergyFieldShader };
        const float width = GameplayStageGeometry.DesignWidth;
        float height = GameplayStageGeometry.DesignHeight + TopPadding;
        var material = new ShaderMaterial { Shader = _shader };
        material.SetShaderParameter("lane_guides", new Vector3(
            GameplayStageGeometry.LeftLineX / width,
            GameplayStageGeometry.RightLineX / width,
            (GameplayStageGeometry.CenterLineY + TopPadding) / height));
        _fieldMaterial = material;
        var field = CreateField(new Rect2(0f, -TopPadding, width, height), material);
        _field = field;
        AddChild(field);
        ApplyRenderMode();
    }

    public override void _EnterTree()
    {
        if (_field is not null) ApplyRenderMode(); // Reattached instance; _Ready only runs once.
    }

    public override void _ExitTree()
    {
        WatchHost(null);
        InvalidateCache();
    }

    private static ColorRect CreateField(Rect2 rect, ShaderMaterial material) => new()
    {
        Name = "EnergyField",
        Position = rect.Position,
        Size = rect.Size,
        MouseFilter = Control.MouseFilterEnum.Ignore,
        Material = material,
    };

    private void ApplyRenderMode()
    {
        if (_field is not { } field) return;
        var host = RenderMode == GameplayBackdropMode.StaticCache && DisplayServer.GetName() != "headless" &&
            GetViewport() is Window window && window == GetTree().Root ? window : null;
        WatchHost(host);
        if (host is null) InvalidateCache();
        var flat = RenderMode == GameplayBackdropMode.Flat;
        field.Material = flat ? null : _fieldMaterial;
        field.Color = flat ? FlatInk : Colors.White;
        SetProcess(host is not null);
    }

    private void WatchHost(Window? host)
    {
        if (_host == host) return;
        if (_host is not null) _host.SizeChanged -= InvalidateCache;
        _host = host;
        if (host is not null) host.SizeChanged += InvalidateCache;
    }

    public override void _Process(double delta)
    {
        if (!IsVisibleInTree() || !TryGetCacheLayout(out var layout))
        {
            if (_layout.HasValue) InvalidateCache();
            return;
        }

        if (_layout != layout)
        {
            InvalidateCache();
            _layout = layout;
            return; // Keep the live shader during resize; wait for a stable layout before baking.
        }

        if (!_attempted)
        {
            _attempted = true;
            StartBake(layout);
        }
        else if (_drawCompleted)
        {
            _drawCompleted = false;
            PublishBake(layout);
        }
        else if (_waitingForDraw && ++_waitFrames >= MaxBakeWaitFrames)
        {
            ReleaseBake(); // No render completion; stay procedural instead of retrying every frame.
        }
    }

    private bool TryGetCacheLayout(out CacheLayout layout)
    {
        layout = default;
        if (_field is not { } field || _host is not { Visible: true } host || !RenderingServer.RenderLoopEnabled ||
            host.ContentScaleMode == Window.ContentScaleModeEnum.Viewport ||
            host.UseHdr2D || host.HdrOutputRequested || host.UseDebanding ||
            host.Msaa2D != Viewport.Msaa.Disabled || host.Snap2DTransformsToPixel || host.Snap2DVerticesToPixel)
            return false;

        // The root window stretch is the transform *inside* its physical render target.
        // The later integer letterbox placement in the window does not change sample positions.
        var toPixels = host.GetStretchTransform() * GetGlobalTransformWithCanvas();
        if (!toPixels.IsFinite() || toPixels.X.Y != 0f || toPixels.Y.X != 0f ||
            toPixels.X.X <= 0f || toPixels.Y.Y <= 0f)
            return false;

        var scale = new Vector2(toPixels.X.X, toPixels.Y.Y);
        var bounds = new Rect2(toPixels * field.GetTransform().Origin, field.Size * scale);
        if (!bounds.IsFinite() || !bounds.End.IsFinite()) return false;
        var pixelOrigin = bounds.Position.Floor();
        var size = bounds.End.Ceil() - pixelOrigin;
        if (size.X < 2f || size.Y < 2f || size.X > MaxCacheEdge || size.Y > MaxCacheEdge ||
            size.X * size.Y > MaxCachePixels)
            return false;

        // Retain subpixel phase in the bake, not just rounded width/height. The sprite maps the
        // enclosing texel box 1:1; texels outside the original field are transparent.
        layout = new CacheLayout((Vector2I)size, new Rect2(bounds.Position - pixelOrigin, bounds.Size),
            toPixels.AffineInverse() * pixelOrigin, Vector2.One / scale);
        return true;
    }

    private void StartBake(CacheLayout layout)
    {
        var bake = new SubViewport
        {
            Name = "EnergyFieldBake",
            World2D = new World2D(),
            Disable3D = true,
            GuiDisableInput = true,
            GuiSnapControlsToPixels = false,
            Snap2DTransformsToPixel = false,
            Snap2DVerticesToPixel = false,
            Msaa2D = Viewport.Msaa.Disabled,
            UseHdr2D = false,
            UseDebanding = false,
            TransparentBg = true,
            Size = layout.PixelSize,
            RenderTargetClearMode = SubViewport.ClearMode.Once,
            RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled,
        };
        bake.AddChild(CreateField(layout.BakeRect, _fieldMaterial!));
        AddChild(bake);
        _bake = bake;
        _waitFrames = 0;
        _waitingForDraw = true;
        RenderingServer.FramePostDraw += OnBakeFrameDrawn;
        bake.RenderTargetUpdateMode = SubViewport.UpdateMode.Once;
    }

    private void OnBakeFrameDrawn()
    {
        DisconnectBakeSignal();
        _drawCompleted = true;
    }

    private void PublishBake(CacheLayout layout)
    {
        if (_bake is not { } bake || _field is not { } field) return;
        bake.RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled;
        // Confirm the offscreen pass actually submitted a draw and has the expected texture.
        // Avoid a GPU-to-CPU readback/stall; this checks readiness, not pixel equivalence.
        var texture = bake.GetTexture();
        if (texture is null || !texture.GetRid().IsValid || texture.GetSize() != (Vector2)layout.PixelSize)
        {
            ReleaseBake();
            return;
        }

        if (_cacheQuad is null)
        {
            _cacheQuad = new Sprite2D
            {
                Name = "CachedEnergyField",
                Centered = false,
                TextureFilter = TextureFilterEnum.Nearest, // One sample per host pixel, independent of its default filter.
                Material = new CanvasItemMaterial { LightMode = CanvasItemMaterial.LightModeEnum.Unshaded },
                Visible = false,
            };
            AddChild(_cacheQuad);
        }
        _cacheQuad.Position = layout.CompositePosition;
        _cacheQuad.Scale = layout.CompositeScale;
        _cacheQuad.Texture = texture;
        // Composite under the same CanvasItem ancestry, where RGB modulation and fades stay live.
        field.Hide();
        _cacheQuad.Show();
    }

    private void DisconnectBakeSignal()
    {
        if (!_waitingForDraw) return;
        RenderingServer.FramePostDraw -= OnBakeFrameDrawn;
        _waitingForDraw = false;
    }

    private void InvalidateCache()
    {
        ReleaseBake();
        _attempted = false;
        _layout = null;
    }

    private void ReleaseBake()
    {
        DisconnectBakeSignal();
        _drawCompleted = false;
        if (_cacheQuad is { } quad)
        {
            quad.Hide();
            quad.Texture = null;
        }
        _field?.Show();
        if (_bake is not { } bake) return;
        _bake = null;
        bake.RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled;
        bake.QueueFree();
    }

    private readonly record struct CacheLayout(Vector2I PixelSize, Rect2 BakeRect,
        Vector2 CompositePosition, Vector2 CompositeScale);

    // Source-color uniforms preserve the intended dark palette when the canvas uses linear color.
    private const string EnergyFieldShader = """
        shader_type canvas_item;
        render_mode unshaded;

        uniform vec3 lane_guides;
        uniform vec4 dark_ink : source_color = vec4(0.034, 0.046, 0.073, 1.0);
        uniform vec4 depth_ink : source_color = vec4(0.059, 0.078, 0.109, 1.0);
        uniform vec4 ion_blue : source_color = vec4(0.080, 0.345, 0.462, 1.0);

        float mist(vec2 p) {
            return exp2(-dot(p, p));
        }

        float cell_value(vec2 cell) {
            vec3 h = fract(cell.xyx * vec3(0.1031, 0.11369, 0.13787));
            h += dot(h, h.yzx + 33.33);
            return fract((h.x + h.y) * h.z);
        }

        float cloud(vec2 p) {
            vec2 cell = floor(p);
            vec2 a = fract(p);
            a = a * a * (3.0 - 2.0 * a);
            float bottom = mix(cell_value(cell), cell_value(cell + vec2(1.0, 0.0)), a.x);
            float top = mix(cell_value(cell + vec2(0.0, 1.0)), cell_value(cell + vec2(1.0, 1.0)), a.x);
            return mix(bottom, top, a.y);
        }

        void fragment() {
            float x = UV.x * 2.0 - 1.0;
            float y = UV.y;
            float top_clear = smoothstep(0.075, 0.31, y);
            float floor_clear = 1.0 - smoothstep(0.69, lane_guides.z + 0.045, y);
            float side_bias = mix(0.21, 1.0, smoothstep(0.10, 0.82, abs(x)));

            // Two coarse scales bend the plumes and make their density uneven. No fine grain,
            // contours, sparks, or on-beat flicker that could read as incoming chart objects.
            float bend = cloud(UV * vec2(6.9, 4.4) + vec2(8.1, 1.3));
            vec2 swirl = vec2(UV.x + 0.28 * y, y - 0.30 * UV.x);
            float curl = cloud(swirl * vec2(9.8, 7.0) + vec2(2.9, bend * 0.48));
            float w = y + (bend - 0.5) * 0.177 + (curl - 0.5) * 0.053;
            float left = mist(vec2((x + 0.69) * 1.73, (w - 0.48 - 0.17 * x) * 5.6));
            float right = mist(vec2((x - 0.61) * 1.96, (w - 0.52 + 0.16 * x) * 6.5));
            float tuft = mist(vec2((x + 0.49) * 2.35, (w - 0.66 + 0.09 * x) * 7.2));
            float density = smoothstep(0.23, 0.71, mix(bend, curl, 0.51));
            float charge = (left * 0.67 + right * 0.77 + tuft * 0.27) * (0.32 + density * 0.68);
            float air = mist(vec2(x * 0.91, (y - 0.47) * 3.12));
            vec3 ink = mix(dark_ink.rgb, depth_ink.rgb, air * top_clear * floor_clear * 0.73);

            // Union of the three exterior half-planes, blurred across the boundaries. This is
            // color only: notes are composited on top on either side of all three lines.
            float outside_distance = max(max(lane_guides.x - UV.x, UV.x - lane_guides.y),
                                         (y - lane_guides.z) * 0.58);
            float exterior = smoothstep(-0.041, 0.067, outside_distance);
            float strength = charge * top_clear * floor_clear * side_bias * 0.68;
            ink = mix(ink, ion_blue.rgb, strength * (1.0 - 0.18 * exterior));

            // Recolor the same fog rather than blending in a separate purple layer. The violet
            // vector has unit weighted luminance, so crossing a rail adds hue, not a glow step.
            float luma = dot(ink, vec3(0.2126, 0.7152, 0.0722));
            vec3 violet = luma * vec3(1.12, 0.92, 1.44);
            ink = mix(ink, violet, exterior * (0.63 + 0.12 * density));

            // Keep inherited canvas modulation so the existing stage transition still fades it.
            COLOR = vec4(ink * COLOR.rgb, COLOR.a);
        }
        """;
}
