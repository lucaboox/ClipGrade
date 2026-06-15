namespace ClipboardApp.Models;

/// <summary>The global hotkey, stored as Win32 modifier flags + virtual-key code.</summary>
public class HotkeyConfig
{
    // Defaults to Ctrl+Shift+V (MOD_CONTROL | MOD_SHIFT, VK 'V').
    public uint Modifiers { get; set; } = 0x0002 | 0x0004;
    public uint Key { get; set; } = 0x56;

    /// <summary>When true, a keyboard hook also opens the popup on Win+V
    /// (and swallows it so Windows' own clipboard never appears).</summary>
    public bool UseWinV { get; set; }

    /// <summary>False when the hotkey has been cleared (no key bound).</summary>
    public bool IsSet => Key != 0;
}

/// <summary>All themeable colors, stored as #RRGGBB strings.</summary>
public class ThemeColors
{
    public string Background { get; set; } = "#0A0A0D";
    public string RowBackground { get; set; } = "#141419";
    public string Highlight { get; set; } = "#202028";
    public string Accent { get; set; } = "#4F5BD5";
    public string Text { get; set; } = "#E8E8EE";
    public string GreyText { get; set; } = "#7C7C8A";
    public string Icon { get; set; } = "#E8E8EE";
    public string TextBox { get; set; } = "#121217";

    public ThemeColors Clone() => (ThemeColors)MemberwiseClone();
}

/// <summary>Everything persisted to settings.json.</summary>
/// <summary>A user-saved theme with a name.</summary>
public class NamedTheme
{
    public string Name { get; set; } = "";
    public ThemeColors Colors { get; set; } = new();
}

public class AppSettings
{
    public HotkeyConfig Hotkey { get; set; } = new();

    /// <summary>When true, the global hooks + hotkey are suspended while a
    /// fullscreen app (e.g. a game) is in the foreground.</summary>
    public bool PauseInFullscreen { get; set; }

    /// <summary>When true, an entry is pasted on double-click; otherwise a
    /// single click pastes it (the default).</summary>
    public bool PasteOnDoubleClick { get; set; }

    /// <summary>Name of the active theme: "Dark", "Light", or a custom name.</summary>
    public string SelectedTheme { get; set; } = "Dark";

    /// <summary>User-created/imported themes.</summary>
    public List<NamedTheme> CustomThemes { get; set; } = new();
}
