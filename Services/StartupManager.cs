using Microsoft.Win32;

namespace ClipboardApp.Services;

/// <summary>
/// Toggles "start with Windows" by writing a per-user entry under
/// HKCU\…\CurrentVersion\Run. This is a normal user-level autostart key — it
/// does not require administrator rights.
/// </summary>
public static class StartupManager
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "ClipGrade";

    public static bool IsEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(ValueName) != null;
        }
        catch { return false; }
    }

    public static void SetEnabled(bool enable)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey);
            if (key == null) return;
            if (enable)
            {
                var path = Environment.ProcessPath;
                if (!string.IsNullOrEmpty(path))
                    key.SetValue(ValueName, $"\"{path}\"");
            }
            else
            {
                key.DeleteValue(ValueName, throwOnMissingValue: false);
            }
        }
        catch { /* best effort */ }
    }
}
