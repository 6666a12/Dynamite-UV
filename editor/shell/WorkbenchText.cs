using System.ComponentModel;
using System.Globalization;
using System.Reflection;
using System.Text.Json;
using Avalonia.Data;
using Avalonia.Data.Converters;
using Avalonia.Markup.Xaml;

namespace DynamiteUniverse.Editor.Shell;

public sealed record WorkbenchLanguage(string Id, string NativeName);

public interface IWorkbenchLocalizer : INotifyPropertyChanged
{
    string CurrentLanguage { get; }
    IReadOnlyList<WorkbenchLanguage> Languages { get; }
    string this[string key] { get; }
    string Format(string key, params object?[] arguments);
    event Action? LanguageChanged;
    void SetLanguage(string language);
}

public sealed class WorkbenchLocalizer : IWorkbenchLocalizer
{
    private readonly Dictionary<string, Dictionary<string, string>> _catalogs;

    public WorkbenchLocalizer()
    {
        _catalogs = Languages.ToDictionary(language => language.Id, LoadCatalog);
    }

    public static WorkbenchLocalizer Shared { get; } = new();
    public IReadOnlyList<WorkbenchLanguage> Languages { get; } = new[]
    {
        new WorkbenchLanguage("en", "ENGLISH"),
        new WorkbenchLanguage("zh-CN", "简体中文"),
    };
    public string CurrentLanguage { get; private set; } = "en";
    public string this[string key] => _catalogs[CurrentLanguage].GetValueOrDefault(key)
        ?? _catalogs["en"].GetValueOrDefault(key) ?? key;
    public event PropertyChangedEventHandler? PropertyChanged;
    public event Action? LanguageChanged;

    public string Format(string key, params object?[] arguments) =>
        string.Format(CultureInfo.GetCultureInfo(CurrentLanguage), this[key], arguments);

    public Binding CreateBinding(string key) => new(nameof(CurrentLanguage))
    {
        Source = this,
        Mode = BindingMode.OneWay,
        Converter = new TranslationConverter(this, key),
    };

    public void SetLanguage(string language)
    {
        if (!_catalogs.ContainsKey(language)) throw new ArgumentException($"Unsupported language: {language}", nameof(language));
        if (CurrentLanguage == language) return;
        CurrentLanguage = language;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CurrentLanguage)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
        LanguageChanged?.Invoke();
    }

    private static Dictionary<string, string> LoadCatalog(WorkbenchLanguage language)
    {
        var assembly = Assembly.GetExecutingAssembly();
        var suffix = $".Localization.{language.Id}.json";
        var resource = assembly.GetManifestResourceNames().SingleOrDefault(name => name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"Missing localization catalog: {language.Id}");
        using var stream = assembly.GetManifestResourceStream(resource)!;
        return JsonSerializer.Deserialize<Dictionary<string, string>>(stream)
            ?? throw new InvalidOperationException($"Empty localization catalog: {language.Id}");
    }

    private sealed class TranslationConverter(WorkbenchLocalizer owner, string key) : IValueConverter
    {
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => owner[key];
        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
    }
}

public sealed class TrExtension : MarkupExtension
{
    public TrExtension() { }
    public TrExtension(string key) => Key = key;
    public string Key { get; set; } = "";

    public override object ProvideValue(IServiceProvider serviceProvider) => WorkbenchLocalizer.Shared.CreateBinding(Key);
}
