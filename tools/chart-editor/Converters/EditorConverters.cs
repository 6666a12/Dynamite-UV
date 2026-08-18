using System.Globalization;
using Avalonia.Data.Converters;
using DynamiteUniverse.ChartEditor.Controls;
using DynamiteUniverse.ChartEditor.Localization;
using Settings = DynamiteUniverse.ChartEditor.Settings;

namespace DynamiteUniverse.ChartEditor.Converters;

/// <summary>Resolves a resource key through <see cref="EditorLocalization"/>.</summary>
public sealed class LocalizationKeyConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        EditorLocalization.Current.Get(parameter as string ?? value as string ?? string.Empty);

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class StringToSignalIconKindConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        Enum.TryParse<SignalIconKind>(value?.ToString(), ignoreCase: true, out var kind)
            ? kind
            : SignalIconKind.Info;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value?.ToString() ?? string.Empty;
}

public sealed class MotionModeDurationConverter : IValueConverter
{
    /// <summary>Parameter accepts Full, Reduced and Off milliseconds separated by commas.</summary>
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var mode = value is Settings.EditorMotionMode motion ? motion : Settings.EditorMotionMode.Full;
        var values = (parameter?.ToString() ?? "160,100,0").Split(',');
        var index = mode switch
        {
            Settings.EditorMotionMode.Full => 0,
            Settings.EditorMotionMode.Reduced => 1,
            Settings.EditorMotionMode.Off => 2,
            _ => 0,
        };
        if (values.Length <= index || !double.TryParse(values[index], NumberStyles.Float,
                CultureInfo.InvariantCulture, out var milliseconds))
            milliseconds = mode == Settings.EditorMotionMode.Off
                ? 0
                : mode == Settings.EditorMotionMode.Reduced ? 100 : 160;
        return TimeSpan.FromMilliseconds(Math.Max(0, milliseconds));
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
