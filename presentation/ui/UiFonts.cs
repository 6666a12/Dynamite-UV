using Godot;

namespace DynamiteUniverse.Ui;

/// <summary>样式稿（docs/ui-mock）定稿的字体与配色，全客户端统一从这里取。</summary>
public static class UiFonts
{
    // Keep the Latin and numeric face first even when a label mixes Chinese and English.
    private const int WeightAxisTag = 0x77676874; // OpenType wght (match UiRegular.tres).
    private static readonly Font Han = GD.Load<Font>("res://assets/fonts/SourceHanSansCN-VF.woff2");
    public static readonly Font Tech = GD.Load<Font>("res://assets/fonts/UiRegular.tres");
    public static readonly Font TechBold = WithCjk(GD.Load<Font>("res://assets/fonts/SpaceGrotesk-Bold.woff2"), 700);
    public static Font Cjk => Tech;

    public static Font WithCjk(Font latin, int weight) => new FontVariation
    {
        BaseFont = latin,
        Fallbacks = [new FontVariation
        {
            BaseFont = Han,
            VariationOpentype = new Godot.Collections.Dictionary { [WeightAxisTag] = weight },
        }],
    };

    public static readonly Color Bg = new("0a0e1a");
    public static readonly Color Panel = new(0.078f, 0.106f, 0.20f, 0.85f);
    public static readonly Color PanelHover = new("1a2442");
    public static readonly Color Line = new("2a3560");
    public static readonly Color Cyan = new("35e0ff");
    public static readonly Color Pink = new("ff4d8f");
    public static readonly Color Text = new("dfe6ff");
    public static readonly Color Dim = new("7c88b0");
    public static readonly Color InkText = new("04121a"); // 实心按钮上的深色字

    public static readonly Color Casual = new("4ade80");
    public static readonly Color Normal = new("38bdf8");
    public static readonly Color Hard = new("fbbf24");
    public static readonly Color Mega = new("c084fc");
    public static readonly Color Giga = new("f87171");

    public static Color DiffColor(string diff) => diff switch
    {
        "casual" => Casual,
        "normal" => Normal,
        "hard" => Hard,
        "mega" => Mega,
        "giga" => Giga,
        "tech" => Giga,
        _ => Dim,
    };

    public static Color GradeColor(string grade) => grade switch
    {
        "Ω" => Pink,
        "Y" => new Color("ED75FF"),
        "X" => new Color("AA88FF"),
        "S" => Cyan,
        "A" => Hard,
        "B" => Normal,
        "D" => new Color("697F91"),
        _ => Dim,
    };

    public static string DiffName(string diff) => diff.ToUpperInvariant();
}
