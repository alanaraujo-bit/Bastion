using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using Bastion.Ui.Services;

namespace Bastion.App.Converters;

/// <summary>Binds an executable path to its extracted icon (null if unavailable).</summary>
public sealed class ExeIcon : IValueConverter
{
    public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is string path ? IconExtractor.FromExecutable(path, 48) : null;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Resolves a theme brush resource key (string) to the actual Brush.</summary>
public sealed class ColorKeyToBrush : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var key = value as string ?? "Brush.TextSecondary";
        return Application.Current?.TryFindResource(key) as Brush
               ?? new SolidColorBrush(Colors.Gray);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>True -> Visible, False -> Collapsed. Invert with parameter "invert".</summary>
public sealed class BoolToVisibility : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        bool b = value is true;
        if (parameter as string == "invert") b = !b;
        return b ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => value is Visibility.Visible;
}
