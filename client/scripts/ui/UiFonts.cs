using Godot;

namespace DuxCommunity.Ui;

/// <summary>样式稿（docs/ui-mock）定稿的字体与配色，全客户端统一从这里取。</summary>
public static class UiFonts
{
    /// <summary>科技字体（拉丁/数字，OFL 开源）。</summary>
    public static readonly Font Tech = GD.Load<Font>("res://assets/fonts/Orbitron.woff2");

    /// <summary>科技字体粗体变体（可变字重 wght=800）：标题/分数/大数字用。</summary>
    public static readonly Font TechBold = MakeTechBold();

    private static Font MakeTechBold()
    {
        var fv = new FontVariation { BaseFont = Tech };
        fv.VariationOpentype = new Godot.Collections.Dictionary { { "wght", 800 } };
        return fv;
    }

    /// <summary>中文回退：系统黑体（正式发布前应打包 Noto Sans SC 等开源字体）。</summary>
    public static readonly Font Cjk = new SystemFont
    {
        FontNames = new[] { "Microsoft YaHei", "PingFang SC", "Noto Sans CJK SC", "sans-serif" },
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
        _ => Giga,
    };

    public static string DiffName(string diff) => diff.ToUpperInvariant();
}
