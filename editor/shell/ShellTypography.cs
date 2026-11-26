using Avalonia.Media;

namespace DynamiteUniverse.Editor.Shell;

/// <summary>Embedded shell typography. Latin stays on Space Grotesk; Chinese is resolved next.</summary>
internal static class ShellTypography
{
    private const string AssetRoot = "avares://DynaMakerUv.Editor.Shell/Assets/";
    private const string SpaceGrotesk = AssetRoot + "SpaceGrotesk-Medium.ttf#Space Grotesk";
    // Both static weights must share one collection key. Avalonia 12.1 does not select
    // instances of embedded variable fonts by weight.
    private const string SourceHanSansCn = AssetRoot + "Fonts/Chinese/#Source Han Sans CN";

    public static FontFamily OfficialFont { get; } = new(
        SpaceGrotesk + ", " + SourceHanSansCn + ", Microsoft YaHei, Noto Sans CJK SC, PingFang SC");
    public static Typeface OfficialTypeface { get; } = new(OfficialFont);
}
