using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;

namespace DynamiteUniverse.Editor.Shell;

internal static class TypographyVerification
{
    private const string Sample = "A1制谱B7";
    private static readonly FontFamily EmbeddedLatin = new("avares://DynaMakerUv.Editor.Shell/Assets/SpaceGrotesk-Medium.ttf#Space Grotesk");
    private static readonly FontFamily EmbeddedChinese = new("avares://DynaMakerUv.Editor.Shell/Assets/Fonts/Chinese/#Source Han Sans CN");

    public static void CheckTitle(Window window, string controlName, string surface, Action<string, bool> check)
    {
        var title = window.FindControl<TextBlock>(controlName)!;
        var original = title.Text;
        try
        {
            title.SetCurrentValue(TextBlock.TextProperty, Sample);
            window.UpdateLayout();
            CheckLayout(title.TextLayout, FontWeight.Normal, surface, check);
        }
        finally { title.SetCurrentValue(TextBlock.TextProperty, original); }
    }

    public static void CheckArtwork(Action<string, bool> check)
    {
        using var layout = CoverArtwork.CreateTextLayout(Sample, 24, Brushes.White, 300);
        CheckLayout(layout, FontWeight.Bold, "Cover artwork", check);
    }

    private static void CheckLayout(TextLayout layout, FontWeight weight, string surface, Action<string, bool> check)
    {
        var fonts = FontManager.Current;
        fonts.TryGetGlyphTypeface(new Typeface(EmbeddedLatin, weight: weight), out var latin);
        fonts.TryGetGlyphTypeface(new Typeface(EmbeddedChinese, weight: weight), out var chinese);
        var runs = layout.TextLines.SelectMany(line => line.TextRuns).OfType<ShapedTextRun>().ToArray();

        // Compare the actual glyph runs with faces from the embedded collections. Matching family
        // names alone could let an installed copy hide broken resource URIs or fallback order.
        bool Uses(string chars, GlyphTypeface? expected) => expected is not null && chars.All(character =>
            runs.Any(run => run.Text.Span.Contains(character) &&
                ReferenceEquals(run.GlyphRun.GlyphTypeface, expected) &&
                run.GlyphRun.GlyphInfos.All(glyph => glyph.GlyphIndex != 0)));

        check($"{surface} shapes Latin and numerals with embedded Space Grotesk", Uses("A1B7", latin));
        check($"{surface} shapes Chinese with real embedded Source Han Sans CN {weight}",
            chinese?.Weight == weight && chinese.FontSimulations == FontSimulations.None && Uses("制谱", chinese));
    }
}
