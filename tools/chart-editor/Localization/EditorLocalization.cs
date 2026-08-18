using System.ComponentModel;
using System.Globalization;
using System.Resources;

namespace DynamiteUniverse.ChartEditor.Localization;

/// <summary>Runtime localization facade for code-behind and XAML bindings.</summary>
public sealed class EditorLocalization : INotifyPropertyChanged
{
    public const string EnglishLanguage = "en-US";
    public const string SimplifiedChineseLanguage = "zh-CN";

    private static readonly CultureInfo EnglishCulture = CultureInfo.GetCultureInfo(EnglishLanguage);
    private static readonly ResourceManager Resources = new(
        "DynamiteUniverse.ChartEditor.Resources.Strings", typeof(EditorLocalization).Assembly);
    private static readonly Lazy<EditorLocalization> SharedInstance = new(() => new EditorLocalization());
    private CultureInfo _culture;

    private EditorLocalization()
    {
        _culture = ResolveCulture(null);
        CultureInfo.CurrentUICulture = _culture;
    }

    public static EditorLocalization Current => SharedInstance.Value;

    /// <summary>Gets localized text and can be used from XAML as <c>[Key]</c>.</summary>
    public string this[string key] => Get(key);

    public CultureInfo Culture => _culture;
    public string Language => _culture.Name;
    public bool IsSimplifiedChinese => _culture.Name.Equals(SimplifiedChineseLanguage, StringComparison.Ordinal);

    public event PropertyChangedEventHandler? PropertyChanged;
    public event EventHandler<EditorLanguageChangedEventArgs>? LanguageChanged;

    public static string GetSystemLanguage() => ResolveCulture(null).Name;

    /// <summary>
    /// Applies an explicit supported language, or the current system language when null/empty.
    /// Unsupported values fall back to English.
    /// </summary>
    public void SetLanguage(string? language)
    {
        var next = ResolveCulture(language);
        if (next.Name.Equals(_culture.Name, StringComparison.Ordinal))
            return;

        var previous = _culture;
        _culture = next;
        CultureInfo.CurrentUICulture = next;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Culture)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Language)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSimplifiedChinese)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
        LanguageChanged?.Invoke(this, new EditorLanguageChangedEventArgs(previous, next));
    }

    public string Get(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        return GetResource(key, _culture) ?? GetResource(key, EnglishCulture) ?? key;
    }

    public string Format(string key, params object?[] arguments) =>
        string.Format(_culture, Get(key), arguments);

    private static string? GetResource(string key, CultureInfo culture)
    {
        try
        {
            return Resources.GetString(key, culture);
        }
        catch (MissingManifestResourceException)
        {
            return null;
        }
    }

    private static CultureInfo ResolveCulture(string? language)
    {
        if (string.IsNullOrWhiteSpace(language))
            return CultureInfo.CurrentUICulture.Name.StartsWith("zh", StringComparison.OrdinalIgnoreCase)
                ? CultureInfo.GetCultureInfo(SimplifiedChineseLanguage)
                : EnglishCulture;

        var normalized = language.Trim();
        if (normalized.Equals(SimplifiedChineseLanguage, StringComparison.OrdinalIgnoreCase) ||
            normalized.StartsWith("zh", StringComparison.OrdinalIgnoreCase))
            return CultureInfo.GetCultureInfo(SimplifiedChineseLanguage);
        if (normalized.Equals(EnglishLanguage, StringComparison.OrdinalIgnoreCase) ||
            normalized.StartsWith("en", StringComparison.OrdinalIgnoreCase))
            return EnglishCulture;
        return EnglishCulture;
    }
}

public sealed class EditorLanguageChangedEventArgs(CultureInfo previousCulture, CultureInfo culture) : EventArgs
{
    public CultureInfo PreviousCulture { get; } = previousCulture;
    public CultureInfo Culture { get; } = culture;
    public string Language => Culture.Name;
}
