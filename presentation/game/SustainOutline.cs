using Godot;
using DynamiteUniverse.Shared.Chart;

namespace DynamiteUniverse.Game;

/// <summary>Draws ribbon sides; transverse caps belong only to authored boundaries, never tessellation.</summary>
public partial class SustainOutline : Node2D
{
    private Vector2[] _points = [];
    private Color _color;
    public Vector2[] Points { get => _points; set { _points = value; QueueRedraw(); } }
    public Color DefaultColor { get => _color; set { _color = value; QueueRedraw(); } }
    public float Width { get; set; } = 3;
    public Track Track { get; set; }
    public bool CapStart { get; set; } = true;
    public bool CapEnd { get; set; } = true;

    public void Configure(Note from, Note to)
    {
        Track = from.Track;
        CapStart = from.V2Metadata?.IsShapeSample != true;
        CapEnd = to.V2Metadata?.IsShapeSample != true;
    }

    public override void _Draw()
    {
        if (_points.Length < 4) return;
        var p = _points;
        if (Track == Track.Center)
        {
            DrawLine(p[1], p[2], _color, Width, true);
            DrawLine(p[3], p[0], _color, Width, true);
            if (CapStart) DrawLine(p[0], p[1], _color, Width, true);
            if (CapEnd) DrawLine(p[2], p[3], _color, Width, true);
        }
        else
        {
            DrawLine(p[0], p[1], _color, Width, true);
            DrawLine(p[2], p[3], _color, Width, true);
            if (CapStart) DrawLine(p[3], p[0], _color, Width, true);
            if (CapEnd) DrawLine(p[1], p[2], _color, Width, true);
        }
    }
}
