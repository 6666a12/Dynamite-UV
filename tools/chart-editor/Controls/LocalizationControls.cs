using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using DynamiteUniverse.ChartEditor.Localization;
using DynamiteUniverse.ChartEditor.Settings;

namespace DynamiteUniverse.ChartEditor.Controls;

/// <summary>Text presenter backed by editor localization resources.</summary>
public sealed class LocalizedText : TextBlock
{
    public static readonly StyledProperty<string> KeyProperty =
        AvaloniaProperty.Register<LocalizedText, string>(nameof(Key));

    static LocalizedText()
    {
        AffectsMeasure<LocalizedText>(KeyProperty);
    }

    public LocalizedText()
    {
        AttachedToVisualTree += (_, _) =>
        {
            EditorLocalization.Current.LanguageChanged -= LocalizationChanged;
            EditorLocalization.Current.LanguageChanged += LocalizationChanged;
            UpdateText();
        };
        DetachedFromVisualTree += (_, _) =>
            EditorLocalization.Current.LanguageChanged -= LocalizationChanged;
    }

    public string Key
    {
        get => GetValue(KeyProperty);
        set => SetValue(KeyProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == KeyProperty)
            UpdateText();
    }

    private void LocalizationChanged(object? sender, EditorLanguageChangedEventArgs e) => UpdateText();

    private void UpdateText()
    {
        if (!string.IsNullOrWhiteSpace(Key))
            Text = EditorLocalization.Current.Get(Key);
    }
}

/// <summary>Text box whose watermark follows the active editor language.</summary>
public sealed class LocalizedTextBox : TextBox
{
    public static readonly StyledProperty<string> WatermarkKeyProperty =
        AvaloniaProperty.Register<LocalizedTextBox, string>(nameof(WatermarkKey));

    public LocalizedTextBox()
    {
        AttachedToVisualTree += (_, _) =>
        {
            EditorLocalization.Current.LanguageChanged -= LocalizationChanged;
            EditorLocalization.Current.LanguageChanged += LocalizationChanged;
            UpdateWatermark();
        };
        DetachedFromVisualTree += (_, _) =>
            EditorLocalization.Current.LanguageChanged -= LocalizationChanged;
    }

    public string WatermarkKey
    {
        get => GetValue(WatermarkKeyProperty);
        set => SetValue(WatermarkKeyProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == WatermarkKeyProperty)
            UpdateWatermark();
    }

    private void LocalizationChanged(object? sender, EditorLanguageChangedEventArgs e) => UpdateWatermark();
    private void UpdateWatermark()
    {
        if (!string.IsNullOrWhiteSpace(WatermarkKey))
            Watermark = EditorLocalization.Current.Get(WatermarkKey);
    }
}

/// <summary>Localized button content for static command labels.</summary>
public sealed class LocalizedButton : Button
{
    public static readonly StyledProperty<string> KeyProperty =
        AvaloniaProperty.Register<LocalizedButton, string>(nameof(Key));

    public LocalizedButton()
    {
        AttachedToVisualTree += (_, _) =>
        {
            EditorLocalization.Current.LanguageChanged -= LocalizationChanged;
            EditorLocalization.Current.LanguageChanged += LocalizationChanged;
            UpdateContent();
        };
        DetachedFromVisualTree += (_, _) =>
            EditorLocalization.Current.LanguageChanged -= LocalizationChanged;
    }

    public string Key
    {
        get => GetValue(KeyProperty);
        set => SetValue(KeyProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == KeyProperty)
            UpdateContent();
    }

    private void LocalizationChanged(object? sender, EditorLanguageChangedEventArgs e) => UpdateContent();
    private void UpdateContent()
    {
        if (!string.IsNullOrWhiteSpace(Key))
            Content = EditorLocalization.Current.Get(Key);
    }
}

/// <summary>Compact control for switching between supported UI languages.</summary>
public sealed class LanguageButton : Button
{
    public LanguageButton()
    {
        Width = 42;
        Height = 34;
        Padding = new Thickness(0);
        Click += (_, _) => ToggleLanguage();
        EditorLocalization.Current.LanguageChanged += (_, _) => Refresh();
        Refresh();
    }

    private void ToggleLanguage()
    {
        var next = EditorLocalization.Current.IsSimplifiedChinese
            ? EditorLocalization.EnglishLanguage
            : EditorLocalization.SimplifiedChineseLanguage;
        EditorLocalization.Current.SetLanguage(next);
        EditorPreferences.Current.SetLanguage(next);
        Refresh();
    }

    private void Refresh()
    {
        Content = EditorLocalization.Current.IsSimplifiedChinese ? "EN" : "中";
        ToolTip.SetTip(this, EditorLocalization.Current.Get("Settings.Language"));
    }
}
