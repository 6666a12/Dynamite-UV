namespace DynamiteUniverse.ChartEditor.Core;

public readonly record struct CanvasHitRect(float X, float Y, float Width, float Height)
{
    public float Right => X + Width;
    public float Bottom => Y + Height;
}

public readonly record struct CanvasHit(int NoteIndex, int NodeIndex, bool Resize, bool ResizeLeading, bool ResizeNodeWidth)
{
    public static CanvasHit None => new(-1, -1, false, false, false);
}

public interface ICanvasHitGeometry
{
    int NoteCount { get; }
    int SideAt(int noteIndex);
    int NodeCount(int noteIndex);
    CanvasHitRect NodeRect(int noteIndex, int nodeIndex);
    CanvasHitRect HeadRect(int noteIndex);
    CanvasHitRect TailRect(int noteIndex);
    bool HasTail(int noteIndex);
    bool HasPathBody(int noteIndex);
}

public static class CanvasHitTesting
{
    private const float Grow = 9f;
    private const float Edge = 12f;

    public static CanvasHit Hit<TGeometry>(in TGeometry geometry, int side, float x, float y)
        where TGeometry : ICanvasHitGeometry
    {
        if (side == 3) return CanvasHit.None;

        for (var noteIndex = geometry.NoteCount - 1; noteIndex >= 0; noteIndex--)
        {
            if (geometry.SideAt(noteIndex) != side) continue;

            for (var nodeIndex = 0; nodeIndex < geometry.NodeCount(noteIndex); nodeIndex++)
            {
                var rect = geometry.NodeRect(noteIndex, nodeIndex);
                if (!ContainsGrown(rect, x, y)) continue;
                var nodeEdge = Math.Abs((side == 0 ? x : y) - (side == 0 ? rect.Right : rect.Bottom)) < Edge;
                return new CanvasHit(noteIndex, nodeIndex, false, false, nodeEdge);
            }

            var head = geometry.HeadRect(noteIndex);
            if (ContainsGrown(head, x, y))
            {
                var axis = side == 0 ? x : y;
                var leading = Math.Abs(axis - (side == 0 ? head.X : head.Y)) < Edge;
                var trailing = Math.Abs(axis - (side == 0 ? head.Right : head.Bottom)) < Edge;
                return new CanvasHit(noteIndex, -1, leading || trailing, leading, false);
            }

            if (geometry.HasTail(noteIndex) && ContainsGrown(geometry.TailRect(noteIndex), x, y))
                return new CanvasHit(noteIndex, -1, false, false, false);

            if (geometry.HasPathBody(noteIndex))
                return new CanvasHit(noteIndex, -1, false, false, false);
        }

        return CanvasHit.None;
    }

    private static bool ContainsGrown(CanvasHitRect rect, float x, float y) =>
        x >= rect.X - Grow && x < rect.Right + Grow &&
        y >= rect.Y - Grow && y < rect.Bottom + Grow;
}
