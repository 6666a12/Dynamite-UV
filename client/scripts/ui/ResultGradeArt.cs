using Godot;

namespace DynamiteUniverse.Ui;

/// <summary>Loads only the current grade atlas (6×5 frames, 512px each), never all five animations.</summary>
internal sealed class ResultGradeArt
{
    private static readonly int[] AmbientFrames = [29, 28, 27, 26, 25, 24, 25, 26, 27, 28];
    private readonly string _path;
    private readonly Texture2D _still;
    private AtlasTexture? _frame;
    private bool _pending;
    public bool Ready => !_pending;
    public Texture2D Still => _still;

    public ResultGradeArt(string grade, bool animate)
    {
        var key = grade == "Ω" ? "omega" : grade is "S" or "A" or "B" ? grade : "C";
        _still = GD.Load<Texture2D>($"res://assets/results/grades/{key}.png");
        _path = $"res://assets/results/grades/{key}_entry.png";
        if (animate)
            _pending = ResourceLoader.LoadThreadedRequest(_path, "Texture2D") == Error.Ok;
    }

    public void Poll()
    {
        if (!_pending) return;
        var status = ResourceLoader.LoadThreadedGetStatus(_path);
        if (status == ResourceLoader.ThreadLoadStatus.InProgress) return;
        _pending = false;
        if (status == ResourceLoader.ThreadLoadStatus.Loaded && ResourceLoader.LoadThreadedGet(_path) is Texture2D atlas)
            _frame = new AtlasTexture { Atlas = atlas, Region = new Rect2(0, 0, 512, 512), FilterClip = true };
        else
            GD.PushWarning($"Result grade animation unavailable: {_path}; using static grade.");
    }

    public Texture2D At(int index)
    {
        if (_frame == null) return _still;
        index = Math.Clamp(index, 0, 29);
        _frame.Region = new Rect2(index % 6 * 512, index / 6 * 512, 512, 512);
        return _frame;
    }

    public int AmbientFrameIndex(double elapsed)
    {
        if (_frame == null) return 29;
        var phase = (int)Math.Floor(Math.Max(0, elapsed) * 7) % AmbientFrames.Length;
        return AmbientFrames[phase];
    }
}
