using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;

namespace FootballSimulation.Wpf.Services;

public enum AppTheme
{
    Light,
    Dark,
    Work
}

public static class ThemeManager
{
    private const string ThemeFolderSegment = "Themes/";
    private static readonly string PreferenceFilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "FootballSimulation",
        "theme.txt");

    public static event EventHandler? ThemeChanged;

    public static AppTheme CurrentTheme { get; private set; } = AppTheme.Dark;

    public static void ApplySavedTheme()
    {
        ApplyTheme(LoadSavedTheme());
    }

    public static void ToggleTheme()
    {
        var nextTheme = CurrentTheme switch
        {
            AppTheme.Dark => AppTheme.Light,
            AppTheme.Light => AppTheme.Work,
            _ => AppTheme.Dark
        };

        ApplyTheme(nextTheme);
    }

    public static void ApplyTheme(AppTheme theme)
    {
        var application = Application.Current;
        if (application is null)
        {
            return;
        }

        var dictionaries = application.Resources.MergedDictionaries;
        for (var index = dictionaries.Count - 1; index >= 0; index--)
        {
            var source = dictionaries[index].Source?.OriginalString;
            if (source?.Contains(ThemeFolderSegment, StringComparison.OrdinalIgnoreCase) == true)
            {
                dictionaries.RemoveAt(index);
            }
        }

        dictionaries.Add(new ResourceDictionary
        {
            Source = new Uri($"/Themes/{theme}Theme.xaml", UriKind.Relative)
        });

        CurrentTheme = theme;
        SaveTheme(theme);
        ThemeChanged?.Invoke(null, EventArgs.Empty);
    }

    public static string GetBrushHex(string resourceKey, string fallback)
    {
        if (Application.Current?.TryFindResource(resourceKey) is SolidColorBrush brush)
        {
            return ToHex(brush.Color);
        }

        return fallback;
    }

    public static Color GetColor(string resourceKey, Color fallback)
    {
        var resource = Application.Current?.TryFindResource(resourceKey);
        return resource switch
        {
            Color color => color,
            SolidColorBrush brush => brush.Color,
            _ => fallback
        };
    }

    public static string ToneDownColor(string hexColor, double saturationRetention = 0.22)
    {
        if (CurrentTheme != AppTheme.Work ||
            !TryParseRgb(hexColor, out var red, out var green, out var blue))
        {
            return hexColor;
        }

        var gray = 0.2126 * red + 0.7152 * green + 0.0722 * blue;
        var retained = Math.Clamp(saturationRetention, 0, 1);
        return $"#{ToByte(gray + (red - gray) * retained):X2}{ToByte(gray + (green - gray) * retained):X2}{ToByte(gray + (blue - gray) * retained):X2}";
    }

    private static AppTheme LoadSavedTheme()
    {
        if (!File.Exists(PreferenceFilePath))
        {
            return AppTheme.Dark;
        }

        var savedTheme = File.ReadAllText(PreferenceFilePath).Trim();
        return Enum.TryParse<AppTheme>(savedTheme, ignoreCase: true, out var theme)
            ? theme
            : AppTheme.Dark;
    }

    private static void SaveTheme(AppTheme theme)
    {
        var directory = Path.GetDirectoryName(PreferenceFilePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(PreferenceFilePath, theme.ToString());
    }

    private static bool TryParseRgb(string hexColor, out int red, out int green, out int blue)
    {
        var value = hexColor.Trim().TrimStart('#');
        if (value.Length == 6 &&
            int.TryParse(value[..2], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out red) &&
            int.TryParse(value.Substring(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out green) &&
            int.TryParse(value.Substring(4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out blue))
        {
            return true;
        }

        red = green = blue = 0;
        return false;
    }

    private static int ToByte(double value)
    {
        return (int)Math.Clamp(Math.Round(value), 0, 255);
    }

    private static string ToHex(Color color)
    {
        return string.Create(CultureInfo.InvariantCulture, $"#{color.R:X2}{color.G:X2}{color.B:X2}");
    }
}
