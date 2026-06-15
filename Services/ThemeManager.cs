using System.Windows;
using System.Windows.Media;
using ClipboardApp.Models;
using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;

namespace ClipboardApp.Services;

/// <summary>
/// Pushes the theme colors into Application resources. Both windows reference
/// these keys via DynamicResource, so changing a color updates the UI live.
/// </summary>
public static class ThemeManager
{
    public static void Apply(ThemeColors t)
    {
        var r = System.Windows.Application.Current.Resources;
        r["Bg"]        = Brush(t.Background);
        r["BgRow"]     = Brush(t.RowBackground);
        r["BgRowHot"]  = Brush(t.Highlight);
        r["Accent"]    = Brush(t.Accent);
        r["Fg"]        = Brush(t.Text);
        r["FgDim"]     = Brush(t.GreyText);
        r["IconColor"] = Brush(t.Icon);
        r["BgPanel"]   = Brush(t.TextBox);
    }

    /// <summary>Parses a #RRGGBB string into a frozen brush; falls back to gray on error.</summary>
    public static SolidColorBrush Brush(string hex)
    {
        try
        {
            var color = (Color)ColorConverter.ConvertFromString(hex);
            var b = new SolidColorBrush(color);
            b.Freeze();
            return b;
        }
        catch
        {
            return new SolidColorBrush(Colors.Gray);
        }
    }
}
