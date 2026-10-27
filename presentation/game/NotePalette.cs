using System.Text;
using Godot;

namespace DynamiteUniverse.Game;

/// <summary>
/// 可编辑的六类音符颜色。范围只到 Note 本体的 tint：
/// 不含判定浮字（HitPerfect / HitGreat / HitGood / HitMiss）、BarLine、Mixer 装饰条，
/// 也不含 EX 的金色描边（着色器里固定 #FFAB21）。设计出处见
/// docs/editor-ui-design.md 的 Note Colors 板组。
/// </summary>
public enum NoteColorChannel
{
    Tap = 0,
    ExTap = 1,
    Drag = 2,
    Hold = 3,
    Mixer = 4,
    Mine = 5,
}

/// <summary>
/// 一个预设槽的配色。空槽（<see cref="IsEmpty"/>）没有语义上的颜色，
/// 但仍保留工厂色以便“落槽前先预览”时不出现黑块。
/// </summary>
public sealed class NotePalette
{
    public const int SlotCount = 10;
    public const int NameMaxLength = 12;

    // 工厂色（design/note-light-v1 REV 03）。NoteVisualSpec 的属性从这里取默认值。
    public static readonly Color FactoryTap = new(0.251f, 0.788f, 1.000f);   // #40C9FF
    public static readonly Color FactoryExTap = new(0.710f, 0.922f, 1.000f); // #B5EBFF
    public static readonly Color FactoryDrag = new(1.000f, 0.776f, 0.416f);  // #FFC66A
    public static readonly Color FactoryHold = new(0.337f, 0.933f, 0.647f);  // #56EEA5
    public static readonly Color FactoryMixer = new(1.000f, 0.459f, 0.733f); // #FF75BB
    public static readonly Color FactoryMine = new(1.000f, 0.412f, 0.467f);  // #FF6977

    public static readonly Color[] FactoryColors =
    [
        FactoryTap, FactoryExTap, FactoryDrag, FactoryHold, FactoryMixer, FactoryMine,
    ];

    public string Name { get; set; } = "DEFAULT";

    public bool IsEmpty { get; set; }

    public Color Tap { get; set; } = FactoryTap;
    public Color ExTap { get; set; } = FactoryExTap;
    public Color Drag { get; set; } = FactoryDrag;
    public Color Hold { get; set; } = FactoryHold;
    public Color Mixer { get; set; } = FactoryMixer;
    public Color Mine { get; set; } = FactoryMine;

    public Color Get(NoteColorChannel channel) => channel switch
    {
        NoteColorChannel.Tap => Tap,
        NoteColorChannel.ExTap => ExTap,
        NoteColorChannel.Drag => Drag,
        NoteColorChannel.Hold => Hold,
        NoteColorChannel.Mixer => Mixer,
        _ => Mine,
    };

    public void Set(NoteColorChannel channel, Color color)
    {
        switch (channel)
        {
            case NoteColorChannel.Tap: Tap = color; break;
            case NoteColorChannel.ExTap: ExTap = color; break;
            case NoteColorChannel.Drag: Drag = color; break;
            case NoteColorChannel.Hold: Hold = color; break;
            case NoteColorChannel.Mixer: Mixer = color; break;
            default: Mine = color; break;
        }
    }

    public NotePalette Clone() => new()
    {
        Name = Name,
        IsEmpty = IsEmpty,
        Tap = Tap,
        ExTap = ExTap,
        Drag = Drag,
        Hold = Hold,
        Mixer = Mixer,
        Mine = Mine,
    };

    public void CopyFrom(NotePalette other)
    {
        Name = other.Name;
        IsEmpty = other.IsEmpty;
        Tap = other.Tap;
        ExTap = other.ExTap;
        Drag = other.Drag;
        Hold = other.Hold;
        Mixer = other.Mixer;
        Mine = other.Mine;
    }

    public void ResetColorsToFactory()
    {
        Tap = FactoryTap;
        ExTap = FactoryExTap;
        Drag = FactoryDrag;
        Hold = FactoryHold;
        Mixer = FactoryMixer;
        Mine = FactoryMine;
    }

    public bool MatchesFactory() =>
        Tap.IsEqualApprox(FactoryTap) && ExTap.IsEqualApprox(FactoryExTap) &&
        Drag.IsEqualApprox(FactoryDrag) && Hold.IsEqualApprox(FactoryHold) &&
        Mixer.IsEqualApprox(FactoryMixer) && Mine.IsEqualApprox(FactoryMine);

    public static NotePalette CreateFactory(string name = "DEFAULT")
    {
        var palette = new NotePalette { Name = name, IsEmpty = false };
        palette.ResetColorsToFactory();
        return palette;
    }

    /// <summary>十个槽：槽 1 为工厂预设，其余为空槽。</summary>
    public static List<NotePalette> CreateLibrary()
    {
        var slots = new List<NotePalette>(SlotCount) { CreateFactory() };
        for (var i = 1; i < SlotCount; i++)
        {
            var empty = CreateFactory(DefaultName(i));
            empty.IsEmpty = true;
            slots.Add(empty);
        }
        return slots;
    }

    public static string DefaultName(int index) => $"PRESET {index + 1:00}";

    /// <summary>
    /// 槽名规则：12 个 ASCII 字符，只允许大写字母、数字、空格与连字符。
    /// 其余字符直接丢弃；全空时回落到 PRESET 0N。
    /// </summary>
    public static string NormalizeName(string? raw, int index)
    {
        var text = (raw ?? "").ToUpperInvariant();
        var builder = new StringBuilder(NameMaxLength);
        foreach (var ch in text)
        {
            if (builder.Length >= NameMaxLength)
                break;
            if (ch is >= 'A' and <= 'Z' or >= '0' and <= '9' or ' ' or '-')
                builder.Append(ch);
        }

        var trimmed = builder.ToString().Trim();
        return trimmed.Length == 0 ? DefaultName(index) : trimmed;
    }

    public static bool IsValidName(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return false;
        var text = raw.Trim();
        if (text.Length > NameMaxLength)
            return false;
        foreach (var ch in text)
        {
            if (ch is not (>= 'A' and <= 'Z' or >= '0' and <= '9' or ' ' or '-'))
                return false;
        }
        return true;
    }

    public static string ToHex(Color color) => color.ToHtml(false).ToUpperInvariant();

    public static bool TryParseHex(string? text, out Color color)
    {
        color = default;
        if (string.IsNullOrWhiteSpace(text))
            return false;
        var value = text.Trim();
        if (!value.StartsWith('#'))
            value = "#" + value;
        if (value.Length != 7 || !Color.HtmlIsValid(value))
            return false;
        color = Color.FromHtml(value);
        return true;
    }
}
