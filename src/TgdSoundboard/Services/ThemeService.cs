using System.Windows;

namespace TgdSoundboard.Services;

public enum AppTheme
{
    Purple,
    Cyan,
    Neon
}

public class ThemeService
{
    private static AppTheme _currentTheme = AppTheme.Neon;
    public static event EventHandler<AppTheme>? ThemeChanged;

    public static AppTheme CurrentTheme
    {
        get => _currentTheme;
        set
        {
            if (_currentTheme != value)
            {
                _currentTheme = value;
                ApplyTheme(value);
                ThemeChanged?.Invoke(null, value);
            }
        }
    }

    public static void Initialize(AppTheme theme = AppTheme.Neon)
    {
        _currentTheme = theme;
        ApplyTheme(theme);
    }

    public static void ApplyTheme(AppTheme theme)
    {
        var app = Application.Current;
        if (app == null) return;

        // Find and remove only theme dictionaries (not MaterialDesign ones)
        var toRemove = app.Resources.MergedDictionaries
            .Where(d => d.Source?.ToString().Contains("/Themes/") == true &&
                        d.Source?.ToString().Contains("TgdSoundboard") == true)
            .ToList();

        foreach (var dict in toRemove)
        {
            app.Resources.MergedDictionaries.Remove(dict);
        }

        // Add new theme dictionary
        var themePath = theme switch
        {
            AppTheme.Purple => "Themes/PurpleTheme.xaml",
            AppTheme.Cyan => "Themes/CyanTheme.xaml",
            AppTheme.Neon => "Themes/NeonTheme.xaml",
            _ => "Themes/NeonTheme.xaml"
        };

        try
        {
            var themeDict = new ResourceDictionary
            {
                Source = new Uri($"pack://application:,,,/TgdSoundboard;component/{themePath}")
            };

            app.Resources.MergedDictionaries.Add(themeDict);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error loading theme: {ex.Message}");
        }
    }

    public static string GetThemeName(AppTheme theme) => theme switch
    {
        AppTheme.Purple => "Purple",
        AppTheme.Cyan => "Cyan",
        AppTheme.Neon => "Neon",
        _ => "Neon"
    };
}
