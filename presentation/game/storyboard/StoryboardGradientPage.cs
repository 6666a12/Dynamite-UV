using DynamiteUniverse.Shared.Storyboard;
using Godot;

namespace DynamiteUniverse.Game;

/// <summary>One non-mipmapped linear-filtered RGBA ramp page, prepared before drawing any shape.</summary>
public sealed class StoryboardGradientPage : IDisposable
{
    private readonly Dictionary<StoryboardGradient, float> _rows = new();
    public Texture2D Texture { get; }
    public bool TryGetRow(StoryboardGradient gradient, out float v) => _rows.TryGetValue(gradient, out v);

    public StoryboardGradientPage(IReadOnlyList<StoryboardGradient> gradients)
    {
        var width = StoryboardLimits.GradientRampWidth;
        var height = gradients.Count;
        var data = new byte[checked(width * height * 4)];
        for (var y = 0; y < height; y++)
        {
            var gradient = gradients[y];
            _rows.Add(gradient, (y + .5f) / height);
            for (var x = 0; x < width; x++)
            {
                var c = gradient.SampleLinear((float)x / (width - 1));
                var offset = (y * width + x) * 4;
                data[offset] = EncodeByte(StoryboardCanvas.Encode(c.X));
                data[offset + 1] = EncodeByte(StoryboardCanvas.Encode(c.Y));
                data[offset + 2] = EncodeByte(StoryboardCanvas.Encode(c.Z));
                data[offset + 3] = EncodeByte(c.W);
            }
        }
        using var image = Image.CreateFromData(width, height, false, Image.Format.Rgba8, data);
        Texture = ImageTexture.CreateFromImage(image);
    }

    private static byte EncodeByte(float value) => (byte)(Math.Clamp(value, 0f, 1f) * 255f);
    public void Dispose() => Texture.Dispose();
}
