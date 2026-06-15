using ClipboardApp.Models;

namespace ClipboardApp.Services;

/// <summary>Built-in themes and resolution of the active theme from settings.</summary>
public static class ThemePresets
{
    public const string Dark = "Dark";
    public const string Light = "Light";

    public static ThemeColors DarkColors() => new(); // ThemeColors defaults are the dark theme

    public static ThemeColors LightColors() => new()
    {
        Background    = "#F3F3F6",
        RowBackground = "#FFFFFF",
        Highlight     = "#DCE1FB",
        Accent        = "#4F5BD5",
        Text          = "#1B1B22",
        GreyText      = "#6B6B78",
        Icon          = "#3C3C4A",
        TextBox       = "#FFFFFF",
    };

    public static bool IsBuiltIn(string name) => name == Dark || name == Light;

    /// <summary>The colors for the currently selected theme.</summary>
    public static ThemeColors Resolve(AppSettings s)
    {
        if (s.SelectedTheme == Light) return LightColors();
        if (s.SelectedTheme == Dark) return DarkColors();
        var custom = s.CustomThemes.FirstOrDefault(t => t.Name == s.SelectedTheme);
        return custom?.Colors ?? DarkColors();
    }
}
