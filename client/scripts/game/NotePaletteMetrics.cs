using Godot;

namespace DynamiteUniverse.Game;

/// <summary>一条配色提醒：落在某一类音符上，文案直接给行内 chip 用。</summary>
public sealed record NotePaletteWarning(NoteColorChannel Channel, string Text);

/// <summary>
/// 配色可读性口径。只提醒、不钳制：任意颜色都能存，页面上用琥珀色 chip 标出来。
/// 对比度按游玩背景 #05070F 计算；色差用 CIE76 ΔE。
/// </summary>
public static class NotePaletteMetrics
{
    /// <summary>游玩背景，取 GameplayBackdrop 的最暗底色。</summary>
    public static readonly Color Background = new("05070f");

    public const float MinContrast = 3.0f;
    public const float MinTapExDeltaE = 12f;
    public const float MinPairDeltaE = 15f;

    private static readonly NoteColorChannel[] Order =
    [
        NoteColorChannel.Tap, NoteColorChannel.ExTap, NoteColorChannel.Drag,
        NoteColorChannel.Hold, NoteColorChannel.Mixer, NoteColorChannel.Mine,
    ];

    public static float Contrast(Color color, Color background)
    {
        var a = RelativeLuminance(color);
        var b = RelativeLuminance(background);
        var lighter = Mathf.Max(a, b);
        var darker = Mathf.Min(a, b);
        return (lighter + 0.05f) / (darker + 0.05f);
    }

    public static float DeltaE(Color a, Color b)
    {
        var (l1, a1, b1) = ToLab(a);
        var (l2, a2, b2) = ToLab(b);
        var dl = l1 - l2;
        var da = a1 - a2;
        var db = b1 - b2;
        return Mathf.Sqrt(dl * dl + da * da + db * db);
    }

    /// <summary>按值作行内 chip：低对比度优先，其次是与更早类型的色差过近。</summary>
    public static List<NotePaletteWarning> Warnings(NotePalette palette)
    {
        var warnings = new List<NotePaletteWarning>();
        foreach (var channel in Order)
        {
            var color = palette.Get(channel);
            var contrast = Contrast(color, Background);
            string? text = null;
            if (contrast < MinContrast)
            {
                text = $"LOW CONTRAST {contrast:0.0}";
            }
            else
            {
                foreach (var earlier in Order)
                {
                    if (earlier == channel)
                        break;
                    var delta = DeltaE(color, palette.Get(earlier));
                    var threshold = earlier == NoteColorChannel.Tap && channel == NoteColorChannel.ExTap
                        ? MinTapExDeltaE
                        : MinPairDeltaE;
                    if (delta >= threshold)
                        continue;
                    text = $"CLOSE TO {ChannelLabel(earlier)} ΔE {delta:0}";
                    break;
                }
            }

            if (text != null)
                warnings.Add(new NotePaletteWarning(channel, text));
        }
        return warnings;
    }

    public static bool HasWarnings(NotePalette palette) => Warnings(palette).Count > 0;

    public static string ChannelLabel(NoteColorChannel channel) => channel switch
    {
        NoteColorChannel.Tap => "TAP",
        NoteColorChannel.ExTap => "EX TAP",
        NoteColorChannel.Drag => "DRAG",
        NoteColorChannel.Hold => "HOLD",
        NoteColorChannel.Mixer => "MIXER",
        _ => "MINE",
    };

    private static float RelativeLuminance(Color color) =>
        0.2126f * Linear(color.R) + 0.7152f * Linear(color.G) + 0.0722f * Linear(color.B);

    private static float Linear(float channel) =>
        channel <= 0.04045f ? channel / 12.92f : Mathf.Pow((channel + 0.055f) / 1.055f, 2.4f);

    private static (float L, float A, float B) ToLab(Color color)
    {
        var r = Linear(color.R);
        var g = Linear(color.G);
        var b = Linear(color.B);

        // sRGB → XYZ（D65）。
        var x = (0.4124f * r + 0.3576f * g + 0.1805f * b) / 0.95047f;
        var y = 0.2126f * r + 0.7152f * g + 0.0722f * b;
        var z = (0.0193f * r + 0.1192f * g + 0.9505f * b) / 1.08883f;

        var fx = F(x);
        var fy = F(y);
        var fz = F(z);
        return (116f * fy - 16f, 500f * (fx - fy), 200f * (fy - fz));
    }

    private static float F(float t) =>
        t > 0.008856f ? Mathf.Pow(t, 1f / 3f) : 7.787f * t + 16f / 116f;
}
