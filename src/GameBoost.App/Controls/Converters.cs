using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using GameBoost.Core.Models;

namespace GameBoost.App.Controls;

public sealed class HealthToBrushConverter : IValueConverter
{
    private static readonly SolidColorBrush Good = Frozen(0x34, 0xD3, 0x99);
    private static readonly SolidColorBrush Warn = Frozen(0xFB, 0xBF, 0x24);
    private static readonly SolidColorBrush Critical = Frozen(0xF8, 0x71, 0x71);
    private static readonly SolidColorBrush Unknown = Frozen(0x8B, 0x98, 0xA5);

    private static SolidColorBrush Frozen(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value switch
        {
            HealthLevel.Good => Good,
            HealthLevel.Warning => Warn,
            HealthLevel.Critical => Critical,
            _ => Unknown
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        Binding.DoNothing;
}

public sealed class HealthToTextConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value switch
        {
            HealthLevel.Good => "Optimal",
            HealthLevel.Warning => "À surveiller",
            HealthLevel.Critical => "Problème détecté",
            _ => "Indisponible"
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        Binding.DoNothing;
}

public sealed class BytesToSizeConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        double bytes = value switch
        {
            long l => l,
            ulong u => u,
            int i => i,
            double d => d,
            _ => 0
        };
        return Format(bytes);
    }

    public static string Format(double bytes)
    {
        string[] units = { "o", "Ko", "Mo", "Go", "To" };
        var unit = 0;
        while (bytes >= 1024 && unit < units.Length - 1)
        {
            bytes /= 1024;
            unit++;
        }
        var digits = unit == 0 ? 0 : bytes >= 100 ? 0 : 1;
        return bytes.ToString("F" + digits, CultureInfo.CurrentCulture) + " " + units[unit];
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        Binding.DoNothing;
}

public sealed class BytesRateConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        double bytes = value is long l ? l : value is double d ? d : 0;
        if (bytes <= 0) return "0 o/s";
        if (bytes < 1024) return bytes.ToString("F0") + " o/s";
        if (bytes < 1024 * 1024) return (bytes / 1024).ToString("F0") + " Ko/s";
        if (bytes < 1024d * 1024 * 1024) return (bytes / 1024 / 1024).ToString("F1") + " Mo/s";
        return (bytes / 1024 / 1024 / 1024).ToString("F2") + " Go/s";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        Binding.DoNothing;
}

public sealed class InverseBoolConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is bool b ? !b : value;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is bool b ? !b : value;
}

public sealed class NullToCollapsedConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is null || (value is string s && string.IsNullOrWhiteSpace(s))
            ? Visibility.Collapsed
            : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        Binding.DoNothing;
}

public sealed class DoubleFormatConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not double d) return value?.ToString() ?? string.Empty;
        var fmt = parameter as string ?? "F0";
        return d.ToString(fmt, CultureInfo.CurrentCulture);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        Binding.DoNothing;
}
