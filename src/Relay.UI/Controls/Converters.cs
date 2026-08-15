using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Relay.Core.Models;

namespace Relay.UI.Controls;

/// <summary>
/// Converts null / non-null object references to Visibility.
/// Supports parameter="Invert".
/// </summary>
public class NullToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        bool isNull = value == null;
        bool invert = parameter is string s && s.Equals("Invert", StringComparison.OrdinalIgnoreCase);

        if (invert)
        {
            return isNull ? Visibility.Visible : Visibility.Collapsed;
        }

        return isNull ? Visibility.Collapsed : Visibility.Visible;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// Converts boolean to Visibility with support for "Invert" parameter.
/// </summary>
public class BooleanToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        bool b = value is bool flag && flag;
        bool invert = parameter is string s && s.Equals("Invert", StringComparison.OrdinalIgnoreCase);

        if (invert)
        {
            return b ? Visibility.Collapsed : Visibility.Visible;
        }

        return b ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is Visibility vis)
        {
            bool invert = parameter is string s && s.Equals("Invert", StringComparison.OrdinalIgnoreCase);
            bool result = vis == Visibility.Visible;
            return invert ? !result : result;
        }
        return false;
    }
}

/// <summary>
/// Converts collection count or integer to Visibility.
/// </summary>
public class CountToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        int count = value switch
        {
            int i => i,
            System.Collections.ICollection col => col.Count,
            _ => 0
        };

        bool hasItems = count > 0;
        bool invert = parameter is string s && s.Equals("Invert", StringComparison.OrdinalIgnoreCase);

        if (invert)
        {
            return hasItems ? Visibility.Collapsed : Visibility.Visible;
        }

        return hasItems ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// Converts base64 image strings to BitmapImage.
/// </summary>
public class Base64ToImageConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string base64 && !string.IsNullOrWhiteSpace(base64))
        {
            try
            {
                byte[] bytes = System.Convert.FromBase64String(base64);
                var bmp = new BitmapImage();
                using var ms = new MemoryStream(bytes);
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.StreamSource = ms;
                bmp.EndInit();
                bmp.Freeze();
                return bmp;
            }
            catch
            {
                return null;
            }
        }
        return null;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// Converts IntentType to badge background Brush.
/// </summary>
public class IntentToBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is IntentType intent)
        {
            return intent switch
            {
                IntentType.Debug => new SolidColorBrush(Color.FromArgb(40, 244, 63, 94)),    // Rose
                IntentType.Shop => new SolidColorBrush(Color.FromArgb(40, 16, 185, 129)),   // Emerald
                IntentType.Translate => new SolidColorBrush(Color.FromArgb(40, 6, 182, 212)), // Cyan
                IntentType.Explain => new SolidColorBrush(Color.FromArgb(40, 168, 85, 247)), // Purple
                IntentType.Extract => new SolidColorBrush(Color.FromArgb(40, 245, 158, 11)), // Amber
                _ => new SolidColorBrush(Color.FromArgb(40, 99, 102, 241))                   // Indigo
            };
        }
        return new SolidColorBrush(Color.FromArgb(40, 99, 102, 241));
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// Converts IntentType to badge foreground text Brush.
/// </summary>
public class IntentToForegroundConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is IntentType intent)
        {
            return intent switch
            {
                IntentType.Debug => new SolidColorBrush(Color.FromRgb(251, 113, 133)),    // Rose
                IntentType.Shop => new SolidColorBrush(Color.FromRgb(52, 211, 153)),     // Emerald
                IntentType.Translate => new SolidColorBrush(Color.FromRgb(34, 211, 238)),// Cyan
                IntentType.Explain => new SolidColorBrush(Color.FromRgb(192, 132, 252)), // Purple
                IntentType.Extract => new SolidColorBrush(Color.FromRgb(251, 191, 36)),  // Amber
                _ => new SolidColorBrush(Color.FromRgb(129, 140, 248))                   // Indigo
            };
        }
        return new SolidColorBrush(Color.FromRgb(129, 140, 248));
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// Converts status message text into appropriate colored Brush (green for success, red for error, amber for warning, cyan for in-progress).
/// </summary>
public class StatusMessageToBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string msg && !string.IsNullOrWhiteSpace(msg))
        {
            if (msg.StartsWith("❌") || msg.Contains("failed", StringComparison.OrdinalIgnoreCase) || msg.Contains("error", StringComparison.OrdinalIgnoreCase))
            {
                return new SolidColorBrush(Color.FromRgb(244, 63, 94)); // AccentRoseBrush (#F43F5E)
            }
            if (msg.StartsWith("✅") || msg.Contains("success", StringComparison.OrdinalIgnoreCase) || msg.Contains("valid", StringComparison.OrdinalIgnoreCase) || msg.Contains("ready", StringComparison.OrdinalIgnoreCase))
            {
                return new SolidColorBrush(Color.FromRgb(16, 185, 129)); // AccentEmeraldBrush (#10B981)
            }
            if (msg.StartsWith("⚠️") || msg.Contains("warn", StringComparison.OrdinalIgnoreCase) || msg.Contains("Please", StringComparison.OrdinalIgnoreCase))
            {
                return new SolidColorBrush(Color.FromRgb(245, 158, 11)); // AccentAmberBrush (#F59E0B)
            }
            return new SolidColorBrush(Color.FromRgb(6, 182, 212)); // AccentCyanBrush (#06B6D4)
        }
        return new SolidColorBrush(Color.FromRgb(148, 163, 184)); // TextSecondaryBrush (#94A3B8)
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
