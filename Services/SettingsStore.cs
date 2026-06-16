using System.IO;
using System.Text.Json;
using ClipboardApp.Models;

namespace ClipboardApp.Services;

/// <summary>Loads and saves the app settings (hotkey + theme) in settings.json.</summary>
public class SettingsStore
{
    private static readonly string SettingsFile = Path.Combine(Storage.RootDir, "settings.json");
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    public AppSettings Load()
    {
        try
        {
            if (!File.Exists(SettingsFile)) return new AppSettings();
            var json = File.ReadAllText(SettingsFile);
            return JsonSerializer.Deserialize<AppSettings>(json, JsonOpts) ?? new AppSettings();
        }
        catch
        {
            return new AppSettings();
        }
    }

    public void Save(AppSettings settings)
    {
        try
        {
            Directory.CreateDirectory(Storage.RootDir);
            var temp = SettingsFile + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(settings, JsonOpts));
            File.Move(temp, SettingsFile, true);
        }
        catch { /* best effort */ }
    }
}
