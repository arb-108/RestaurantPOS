using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using RestaurantPOS.Domain.Enums;

namespace RestaurantPOS.WPF.Converters;

public class PaisaToCurrencyConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is long paisa)
            return $"Rs. {paisa / 100m:N2}";
        if (value is int paisaInt)
            return $"Rs. {paisaInt / 100m:N2}";
        return "Rs. 0.00";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is string str)
        {
            str = str.Replace("Rs.", "").Replace(",", "").Trim();
            if (decimal.TryParse(str, out var amount))
                return (long)(amount * 100);
        }
        return 0L;
    }
}

public class PaisaToDecimalConverter : IValueConverter
{
    /// <summary>
    /// If ConverterParameter is "BlankIfZero", returns "" when value is 0.
    /// Otherwise returns "0.00".
    /// </summary>
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is long paisa)
        {
            if (paisa == 0 && parameter is string p && p == "BlankIfZero")
                return "";
            return $"{paisa / 100m:N2}";
        }
        return parameter is string p2 && p2 == "BlankIfZero" ? "" : "0.00";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is string str)
        {
            str = str.Replace(",", "").Trim();
            if (string.IsNullOrEmpty(str))
                return 0L;
            if (decimal.TryParse(str, out var amount))
                return (long)(amount * 100);
        }
        return 0L;
    }
}

public class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var boolValue = value is bool b && b;
        var invert = parameter is string s && s == "Invert";
        if (invert) boolValue = !boolValue;
        return boolValue ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value is Visibility v && v == Visibility.Visible;
    }
}

/// <summary>
/// Converts OrderType enum to display string.
/// </summary>
public class OrderTypeToStringConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is OrderType ot)
        {
            return ot switch
            {
                OrderType.DineIn => "Din-in",
                OrderType.TakeAway => "Takeaway",
                OrderType.Delivery => "Delivery",
                _ => ""
            };
        }
        return "";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

/// <summary>
/// Converts TableStatus to a background brush color.
/// Yellow=Available, Green=Occupied, Orange=Reserved, Gray=Cleaning
/// </summary>
/// <summary>
/// Single-value fallback (used where MultiBinding is not convenient).
/// Yellow=Available, Green=Reserved(K-Bill printed), Gray=Cleaning.
/// </summary>
public class TableStatusToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is TableStatus status)
        {
            return status switch
            {
                TableStatus.Available => new SolidColorBrush(Color.FromRgb(0xE0, 0xAE, 0x26)),   // Yellow = Free
                TableStatus.Occupied  => new SolidColorBrush(Color.FromRgb(0xE0, 0xAE, 0x26)),   // Yellow (blue only via multi-converter)
                TableStatus.Reserved  => new SolidColorBrush(Color.FromRgb(0x4C, 0xAF, 0x50)),   // Green = K-Bill printed
                TableStatus.Cleaning  => new SolidColorBrush(Color.FromRgb(0x9E, 0x9E, 0x9E)),   // Gray
                _ => new SolidColorBrush(Color.FromRgb(0xE0, 0xAE, 0x26))
            };
        }
        return new SolidColorBrush(Color.FromRgb(0xE0, 0xAE, 0x26));
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

/// <summary>
/// MultiValueConverter for table button background.
/// Values[0] = Table (the button's DataContext)
/// Values[1] = SelectedTable (from ViewModel)
///
/// Logic:
///   - If this table IS the selected table → Light Blue #90CAF9
///   - If table.Status == Reserved (K-Bill printed) → Green #4CAF50
///   - Otherwise → Yellow #E0AE26 (free)
/// </summary>
public class TableBgMultiConverter : IMultiValueConverter
{
    private static readonly SolidColorBrush YellowBrush = new(Color.FromRgb(0xE0, 0xAE, 0x26));
    private static readonly SolidColorBrush BlueBrush = new(Color.FromRgb(0xFF, 0xF8, 0xE1));
    private static readonly SolidColorBrush GreenBrush = new(Color.FromRgb(0x4C, 0xAF, 0x50));
    private static readonly SolidColorBrush GrayBrush = new(Color.FromRgb(0x9E, 0x9E, 0x9E));
    private static readonly SolidColorBrush PurpleBrush = new(Color.FromRgb(0x9C, 0x27, 0xB0));

    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length < 2) return YellowBrush;

        var table = values[0] as RestaurantPOS.Domain.Entities.Table;
        var selectedTable = values[1] as RestaurantPOS.Domain.Entities.Table;

        if (table == null) return YellowBrush;

        // Currently selected table → Light Blue
        if (selectedTable != null && table.Id == selectedTable.Id)
            return BlueBrush;

        // K-Bill printed → Green
        if (table.Status == TableStatus.Reserved)
            return GreenBrush;

        // Cleaning → Gray
        if (table.Status == TableStatus.Cleaning)
            return GrayBrush;

        // Family tables → Purple
        if (table.Name.Contains("Family", StringComparison.OrdinalIgnoreCase))
            return PurpleBrush;

        // Default → Yellow (free)
        return YellowBrush;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

/// <summary>
/// Returns Visible when Category.Name == "Deals", Collapsed otherwise.
/// </summary>
public class IsDealToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is RestaurantPOS.Domain.Entities.Category cat)
            return cat.Name.Equals("Deals", StringComparison.OrdinalIgnoreCase)
                ? Visibility.Visible : Visibility.Collapsed;
        return Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

/// <summary>
/// Returns a teal gradient for Deals category, gold gradient for regular items.
/// </summary>
public class MenuCardBgConverter : IValueConverter
{
    private static readonly LinearGradientBrush GoldBrush;
    private static readonly LinearGradientBrush DealBrush;

    static MenuCardBgConverter()
    {
        GoldBrush = new LinearGradientBrush(Color.FromRgb(0xFF, 0xD5, 0x4F), Color.FromRgb(0xF9, 0xA8, 0x25), 45);
        GoldBrush.Freeze();
        DealBrush = new LinearGradientBrush(Color.FromRgb(0xFF, 0xD5, 0x4F), Color.FromRgb(0xF9, 0xA8, 0x25), 45);
        DealBrush.Freeze();
    }

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is RestaurantPOS.Domain.Entities.Category cat &&
            cat.Name.Equals("Deals", StringComparison.OrdinalIgnoreCase))
            return DealBrush;
        return GoldBrush;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

/// <summary>
/// Shows element only when count is 0 (for "no items" placeholders).
/// Returns Visible when count==0, Collapsed otherwise.
/// </summary>
public class CountToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is int count)
            return count == 0 ? Visibility.Visible : Visibility.Collapsed;
        return Visibility.Visible;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

/// <summary>
/// Converts a UTC DateTime to local time for display.
/// </summary>
public class UtcToLocalConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is DateTime dt)
            return dt.ToLocalTime();
        return value;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is DateTime dt)
            return dt.ToUniversalTime();
        return value;
    }
}

/// <summary>
/// Compares an enum value against a string parameter and returns bool.
/// Used for radio-button-like toggle behavior.
/// </summary>
public class EnumToBoolConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value == null || parameter == null) return false;
        return value.ToString() == parameter.ToString();
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool b && b && parameter is string s)
        {
            return Enum.Parse(targetType, s);
        }
        return Binding.DoNothing;
    }
}

/// <summary>
/// Converts Ingredient to stock level bar width (0-70 based on CurrentStock / ReorderLevel ratio).
/// </summary>
public class StockLevelToWidthConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is RestaurantPOS.Domain.Entities.Ingredient item && item.ReorderLevel > 0)
        {
            var ratio = (double)(item.CurrentStock / item.ReorderLevel);
            return Math.Min(70.0, Math.Max(4.0, ratio * 35.0)); // scale so 2x reorder = full bar
        }
        return 35.0;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

/// <summary>
/// Converts Ingredient to stock status text: "OK", "Low", "Critical".
/// </summary>
public class StockStatusConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is RestaurantPOS.Domain.Entities.Ingredient item)
        {
            if (item.CurrentStock <= 0) return "Critical";
            if (item.CurrentStock <= item.ReorderLevel) return "Low";
            return "OK";
        }
        return "OK";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

/// <summary>
/// Converts Ingredient to stock status badge colors.
/// Parameter "bg" = background, "fg" = foreground.
/// </summary>
public class StockStatusColorConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var param = parameter as string ?? "bg";
        var status = "ok";
        if (value is RestaurantPOS.Domain.Entities.Ingredient item)
        {
            if (item.CurrentStock <= 0) status = "critical";
            else if (item.CurrentStock <= item.ReorderLevel) status = "low";
        }

        if (param == "fg")
        {
            return status switch
            {
                "critical" => new SolidColorBrush(Color.FromRgb(0x99, 0x1B, 0x1B)),
                "low" => new SolidColorBrush(Color.FromRgb(0x92, 0x40, 0x0E)),
                _ => new SolidColorBrush(Color.FromRgb(0x06, 0x5F, 0x46))
            };
        }
        // bg
        return status switch
        {
            "critical" => new SolidColorBrush(Color.FromArgb(0x1A, 0xDC, 0x26, 0x26)),
            "low" => new SolidColorBrush(Color.FromArgb(0x1A, 0xD9, 0x77, 0x06)),
            _ => new SolidColorBrush(Color.FromArgb(0x1A, 0x05, 0x96, 0x69))
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

/// <summary>
/// Converts Ingredient to stock bar fill color: green=OK, amber=Low, red=Critical.
/// </summary>
public class StockBarColorConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is RestaurantPOS.Domain.Entities.Ingredient item)
        {
            if (item.CurrentStock <= 0) return new SolidColorBrush(Color.FromRgb(0xDC, 0x26, 0x26));
            if (item.CurrentStock <= item.ReorderLevel) return new SolidColorBrush(Color.FromRgb(0xD9, 0x77, 0x06));
            return new SolidColorBrush(Color.FromRgb(0x05, 0x96, 0x69));
        }
        return new SolidColorBrush(Color.FromRgb(0x05, 0x96, 0x69));
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

/// <summary>
/// Shows element when integer value is greater than 0 (Visible), hides otherwise (Collapsed).
/// </summary>
public class IntToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is int count)
            return count > 0 ? Visibility.Visible : Visibility.Collapsed;
        return Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

/// <summary>
/// Inverts a boolean value. Useful for IsEnabled bindings.
/// </summary>
public class BoolInverterConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool b) return !b;
        return true;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool b) return !b;
        return false;
    }
}

/// <summary>
/// Extracts deal component lines from MenuItem.Description for card display.
/// Description format: "...\nIncludes: Zinger ×1, Large Fries ×1, Pepsi ×1" or "Includes: ..."
/// Returns formatted lines like "1 Zinger\n1 Large Fries\n1 Pepsi"
/// </summary>
public class DealComponentsConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not string desc || string.IsNullOrWhiteSpace(desc))
            return string.Empty;

        // Find the "Includes:" part
        var idx = desc.IndexOf("Includes:", StringComparison.OrdinalIgnoreCase);
        if (idx < 0) return string.Empty;

        var includesPart = desc[(idx + 9)..].Trim();
        if (string.IsNullOrWhiteSpace(includesPart)) return string.Empty;

        var components = includesPart.Split(',', StringSplitOptions.RemoveEmptyEntries);
        var lines = new List<string>();
        foreach (var comp in components)
        {
            var c = comp.Trim();
            // Format: "ItemName ×Qty" → "Qty ItemName"
            var xIdx = c.LastIndexOf('×');
            if (xIdx > 0)
            {
                var itemName = c[..xIdx].Trim();
                var qty = c[(xIdx + 1)..].Trim();
                lines.Add($"{qty} {itemName}");
            }
            else
            {
                lines.Add($"1 {c}");
            }
        }

        return string.Join("\n", lines);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

/// <summary>
/// Converts a Category's ImagePath to a BitmapImage.
/// Falls back to null if path is missing (XAML handles fallback icon).
/// ImagePath can be absolute or relative to Assets\Images\.
/// </summary>
public class CategoryImageConverter : IValueConverter
{
    private static readonly Dictionary<string, System.Windows.Media.Imaging.BitmapImage> _cache = new();

    public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        string? imagePath = null;

        if (value is RestaurantPOS.Domain.Entities.Category cat)
            imagePath = cat.ImagePath;
        else if (value is string s)
            imagePath = s;

        if (string.IsNullOrWhiteSpace(imagePath))
            return null;

        if (!System.IO.Path.IsPathRooted(imagePath))
        {
            var baseDir = System.IO.Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory, "Assets", "Images");
            imagePath = System.IO.Path.Combine(baseDir, imagePath);
        }

        if (!System.IO.File.Exists(imagePath))
            return null;

        if (_cache.TryGetValue(imagePath, out var cached))
            return cached;

        try
        {
            var bmp = new System.Windows.Media.Imaging.BitmapImage();
            bmp.BeginInit();
            bmp.UriSource = new Uri(imagePath, UriKind.Absolute);
            bmp.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
            bmp.DecodePixelWidth = 60;
            bmp.EndInit();
            bmp.Freeze();
            _cache[imagePath] = bmp;
            return bmp;
        }
        catch
        {
            return null;
        }
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}
