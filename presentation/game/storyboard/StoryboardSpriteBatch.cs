using DynamiteUniverse.Shared.Storyboard;
using Godot;

namespace DynamiteUniverse.Game;

/// <summary>One retained 2D instanced draw for consecutive, unclipped sprites with the same state.</summary>
internal sealed class StoryboardSpriteBatch : IDisposable
{
    private const int Stride = 12; // Two padded transform rows, then RGBA.
    private readonly MultiMesh _mesh;
    private readonly Rid _rid;
    private float[] _buffer = [];
    private int _capacity;
    private int _visibleCount = -1;
    public int Capacity => _capacity;

    public StoryboardSpriteBatch(Mesh quad)
    {
        _mesh = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform2D,
            UseColors = true,
            Mesh = quad,
            CustomAabb = new Aabb(new Vector3(0, 0, -1), new Vector3(1920, 1080, 2)),
        };
        _rid = _mesh.GetRid();
    }

    public static int CapacityFor(int count) => (int)System.Numerics.BitOperations.RoundUpToPowerOf2((uint)count);

    public void Update(List<StoryboardDrawItem> items, int begin, int end, int requiredCapacity)
    {
        if (_capacity < requiredCapacity)
        {
            _capacity = requiredCapacity;
            _buffer = new float[_capacity * Stride];
            _mesh.InstanceCount = _capacity;
            _visibleCount = -1;
        }
        var visible = 0;
        for (var i = begin; i < end; i++)
        {
            var item = items[i];
            if (!(item.Opacity > 0f) || !StoryboardGeometry.CanBeVisible(item)) continue;
            var offset = visible++ * Stride;
            var m = item.Transform;
            // Unit quad [0,1]^2 becomes the sprite's unpivoted local rectangle.
            _buffer[offset] = m.M11 * item.Width;
            _buffer[offset + 1] = m.M21 * item.Height;
            _buffer[offset + 3] = m.M31;
            _buffer[offset + 4] = m.M12 * item.Width;
            _buffer[offset + 5] = m.M22 * item.Height;
            _buffer[offset + 7] = m.M32;
            var color = StoryboardCanvas.Modulation(item);
            _buffer[offset + 8] = color.R;
            _buffer[offset + 9] = color.G;
            _buffer[offset + 10] = color.B;
            _buffer[offset + 11] = color.A;
        }
        if (visible != 0) RenderingServer.MultimeshSetBuffer(_rid, _buffer.AsSpan());
        if (_visibleCount != visible)
        {
            _visibleCount = visible;
            _mesh.VisibleInstanceCount = visible;
        }
    }

    public void Draw(CanvasItem target, Texture2D texture) => target.DrawMultimesh(_mesh, texture);
    public void Dispose() => _mesh.Dispose();

    public static Mesh CreateQuad()
    {
        Vector2[] corners = [Vector2.Zero, Vector2.Right, Vector2.One, Vector2.Down];
        using var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = corners;
        arrays[(int)Mesh.ArrayType.TexUV] = corners;
        arrays[(int)Mesh.ArrayType.Index] = new int[] { 0, 1, 2, 0, 2, 3 };
        var mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays, flags: Mesh.ArrayFormat.FlagUse2DVertices);
        return mesh;
    }
}
