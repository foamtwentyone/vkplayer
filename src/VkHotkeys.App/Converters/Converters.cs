using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using VkHotkeys.App.ViewModels;

namespace VkHotkeys.App.Converters;

public sealed class StatusLevelToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        Application.Current.FindResource(value switch
        {
            StatusLevel.Ok => "SuccessBrush",
            StatusLevel.Warning => "WarningBrush",
            StatusLevel.Error => "ErrorBrush",
            _ => "IdleBrush",
        });

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class IssueLevelToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is IssueLevel.Error
            ? Application.Current.FindResource("ErrorBrush")
            : Application.Current.FindResource("WarningBrush");

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Цвет рамки поля комбинации: красная при ошибке, жёлтая при предупреждении.</summary>
public sealed class IssueLevelToBorderConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value switch
    {
        IssueLevel.Error => Application.Current.FindResource("ErrorBrush"),
        IssueLevel.Warning => Application.Current.FindResource("WarningBrush"),
        _ => Application.Current.FindResource("InputBorderBrush"),
    };

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class InverseBoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is true ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Доля 0..1 -> ширина в процентах от родителя (для полосы прогресса трека).</summary>
public sealed class FractionToWidthConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length == 2 && values[0] is double f && values[1] is double w && !double.IsNaN(w))
            return Math.Max(0, Math.Min(1, f)) * w;
        return 0.0;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
