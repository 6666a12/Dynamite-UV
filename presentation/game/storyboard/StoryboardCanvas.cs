using DynamiteUniverse.Shared.Storyboard;
using Godot;
using System.Numerics;
using Vec2 = Godot.Vector2;

namespace DynamiteUniverse.Game;

/// <summary>A visual-only Storyboard slot. Contiguous runs share a CanvasItem without reordering the script.</summary>
public partial class StoryboardCanvas : Node2D
{
    private readonly List<StoryboardDrawItem> _items;
    private readonly List<StoryboardDrawRun> _runs = new();
    internal readonly StoryboardGeometry Geometry = new();
    internal List<StoryboardDrawItem> Items => _items;
    internal IReadOnlyList<StoryboardClipRect> Clips => _sampler.Clips;
    private readonly Func<StoryboardImage, Texture2D?> _resolveTexture;
    internal Texture2D? ResolveTexture(StoryboardImage image) => _resolveTexture(image);
    public StoryboardGradientPage? Gradients { get; set; }
    private readonly CanvasItemMaterial _add = new() { BlendMode = CanvasItemMaterial.BlendModeEnum.Add };
    private readonly StoryboardSampler _sampler;
    private readonly bool _batchSprites;
    private Mesh? _spriteQuad;
    private int _reservedInstances;
    private bool _released;
    private const int MinBatchSprites = 32;
    private double _sampleTime = double.NaN;
    public int ActiveDrawItems => _items.Count;
    public int DrawRuns { get; private set; }
    public int SpriteBatches { get; private set; }
    public StoryboardCanvas(StoryboardSampler sampler, Func<StoryboardImage, Texture2D?> resolveTexture,
        bool batchSprites = true)
    {
        _sampler = sampler;
        _resolveTexture = resolveTexture;
        // The instance color/UV path has been compared against rect drawing on Forward Mobile.
        _batchSprites = batchSprites && RenderingServer.GetCurrentRenderingMethod() == "mobile";
        _items = new(sampler.MaxDrawItems);
        Name = "StoryboardCanvas";
    }

    // Clear RID references synchronously in the command stream before the owner retires a
    // texture. A forced fresh sample republishes the surviving runs in their original order.
    public void InvalidateTextureCommands()
    {
        if (_released) return;
        _sampleTime = double.NaN;
        foreach (var run in _runs) run.ClearCommands();
    }

    public override void _ExitTree() => ReleaseDrawing();

    /// <summary>End the one-shot performance without hiding its separate opaque fallback.</summary>
    public void Finish()
    {
        Hide();
        ReleaseDrawing();
    }

    private void ReleaseDrawing()
    {
        if (_released) return;
        _released = true;
        // Drop retained commands before freeing their native mesh, material and image references.
        foreach (var run in _runs) run.ReleaseDrawing();
        _spriteQuad?.Dispose();
        Gradients = null;
        _add.Dispose();
    }

    public void Sample(double time)
    {
        if (_released || time == _sampleTime) return;
        _sampleTime = time;
        _sampler.Sample(time, _items);
        var run = 0;
        SpriteBatches = 0;
        for (var start = 0; start < _items.Count;)
        {
            var style = _items[start];
            // Filled shapes use linear gradient lookup regardless of a sprite's sampling choice.
            var filter = Filter(style);
            var end = start + 1;
            var batchable = _batchSprites && CanBatch(style);
            while (end < _items.Count && _items[end].Blend == style.Blend && Filter(_items[end]) == filter)
            {
                // Keep an existing state run whole. Fragmenting alternating textures into hundreds
                // of CanvasItems is worse than using the original direct-draw path for that run.
                batchable = batchable && CanBatch(_items[end]) && _items[end].Image == style.Image;
                end++;
            }
            if (run == _runs.Count)
            {
                var child = new StoryboardDrawRun(this);
                AddChild(child);
                _runs.Add(child);
            }
            var node = _runs[run++];
            var material = style.Blend == StoryboardBlend.Add ? _add : null;
            if (node.Material != material) node.Material = material;
            if (node.TextureFilter != filter) node.TextureFilter = filter;
            var useBatch = batchable && end - start >= MinBatchSprites;
            if (node.SetRange(start, end, useBatch)) SpriteBatches++;
            start = end;
        }
        for (var i = run; i < DrawRuns; i++) _runs[i].Hide();
        DrawRuns = run;
    }

    internal static float Encode(float value) => value <= 0.0031308f
        ? value * 12.92f : 1.055f * MathF.Pow(value, 1f / 2.4f) - 0.055f;

    internal static Color Modulation(in StoryboardDrawItem item) => new(
        Encode(item.Tint.X), Encode(item.Tint.Y), Encode(item.Tint.Z), item.Opacity);

    private static bool CanBatch(in StoryboardDrawItem item) =>
        item.Kind == StoryboardDrawKind.Sprite && item.Blend == StoryboardBlend.Normal &&
        item.Image is not null && item.ClipCount == 0;

    private static TextureFilterEnum Filter(in StoryboardDrawItem item) => item.Sampling == StoryboardSampling.Nearest &&
        item.Kind == StoryboardDrawKind.Sprite ? TextureFilterEnum.Nearest : TextureFilterEnum.Linear;

    internal bool ReserveInstances(int additional)
    {
        if (additional <= 0) return true;
        if (_reservedInstances + additional > 2 * _sampler.MaxDrawItems) return false;
        _reservedInstances += additional;
        return true;
    }

    internal Mesh SpriteQuad => _spriteQuad ??= StoryboardSpriteBatch.CreateQuad();

    internal static Transform2D ToGodot(Matrix3x2 m) => new(
        new Vec2(m.M11, m.M12), new Vec2(m.M21, m.M22), new Vec2(m.M31, m.M32));
}

internal sealed partial class StoryboardDrawRun : Node2D
{
    private readonly StoryboardCanvas _owner;
    private int _begin, _end;
    private StoryboardSpriteBatch? _batch;
    private Texture2D? _batchTexture;

    public StoryboardDrawRun(StoryboardCanvas owner)
    {
        _owner = owner;
        TextureRepeat = TextureRepeatEnum.Disabled;
    }

    internal bool SetRange(int begin, int end, bool batch)
    {
        _begin = begin;
        _end = end;
        var wasHidden = !Visible;
        var previousTexture = _batchTexture;
        var capacity = batch ? StoryboardSpriteBatch.CapacityFor(end - begin) : 0;
        if (batch && _owner.Items[begin].Image is { } image && _owner.ResolveTexture(image) is { } texture &&
            _owner.ReserveInstances(capacity - (_batch?.Capacity ?? 0)))
        {
            _batch ??= new StoryboardSpriteBatch(_owner.SpriteQuad);
            _batchTexture = texture;
            _batch.Update(_owner.Items, begin, end, capacity);
        }
        else
            _batchTexture = null;
        if (wasHidden) Show();
        // The command retains the RID. Transforms, color and visible count update through the buffer.
        if (wasHidden || _batchTexture is null || previousTexture != _batchTexture) QueueRedraw();
        return _batchTexture is not null;
    }

    internal void ClearCommands()
    {
        RenderingServer.CanvasItemClear(GetCanvasItem());
        _batchTexture = null;
        _begin = _end = 0;
    }

    internal void ReleaseDrawing()
    {
        ClearCommands();
        Material = null;
        _batch?.Dispose();
        _batch = null;
    }

    public override void _Draw()
    {
        if (_begin == _end) return;
        if (_batch is not null && _batchTexture is not null)
        {
            _batch.Draw(this, _batchTexture);
            return;
        }
        for (var i = _begin; i < _end; i++)
        {
            var item = _owner.Items[i];
            if (!(item.Opacity > 0) || !StoryboardGeometry.CanBeVisible(item)) continue;
            if (item.Image is { } binding && item.ClipCount == 0)
            {
                if (_owner.ResolveTexture(binding) is not { } texture) continue;
                DrawSetTransformMatrix(StoryboardCanvas.ToGodot(item.Transform));
                DrawTextureRect(texture, new Rect2(0, 0, item.Width, item.Height), false, StoryboardCanvas.Modulation(item));
                continue;
            }
            DrawSetTransformMatrix(Transform2D.Identity);
            _owner.Geometry.Draw(this, _owner, item);
        }
        DrawSetTransformMatrix(Transform2D.Identity);
    }
}
