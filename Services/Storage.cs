using System.IO;
using System.Text.Json;
using ClipboardApp.Models;

namespace ClipboardApp.Services;

/// <summary>
/// Persists entries to %AppData%\ClipGrade. Metadata lives in entries.json;
/// images live as PNG files in the images subfolder.
/// </summary>
public class Storage
{
    public static readonly string RootDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ClipGrade");

    /// <summary>One-time move of data saved under the previous app name.</summary>
    public static void MigrateFromLegacy()
    {
        try
        {
            var legacy = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ClipboardApp");
            if (!Directory.Exists(RootDir) && Directory.Exists(legacy))
                Directory.Move(legacy, RootDir);
        }
        catch { /* best effort */ }
    }

    public static readonly string ImagesDir = Path.Combine(RootDir, "images");

    private static readonly string EntriesFile = Path.Combine(RootDir, "entries.json");

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true
    };

    public Storage()
    {
        Directory.CreateDirectory(RootDir);
        Directory.CreateDirectory(ImagesDir);
    }

    public List<ClipboardEntry> Load()
    {
        try
        {
            if (!File.Exists(EntriesFile)) return new();
            var json = File.ReadAllText(EntriesFile);
            var list = JsonSerializer.Deserialize<List<ClipboardEntry>>(json, JsonOpts) ?? new();
            // Drop image entries whose backing file vanished.
            list.RemoveAll(e => e.Kind == EntryKind.Image &&
                                (string.IsNullOrEmpty(e.ImagePath) || !File.Exists(e.ImagePath)));
            return list;
        }
        catch
        {
            return new();
        }
    }

    public void Save(IEnumerable<ClipboardEntry> entries)
    {
        try
        {
            var json = JsonSerializer.Serialize(entries.ToList(), JsonOpts);
            File.WriteAllText(EntriesFile, json);
        }
        catch { /* best effort */ }
    }

    public string NewImagePath() => Path.Combine(ImagesDir, Guid.NewGuid() + ".png");

    /// <summary>Deletes the backing PNG for an image entry, if present.</summary>
    public void DeleteImageFile(ClipboardEntry entry)
    {
        try
        {
            if (entry.Kind == EntryKind.Image && !string.IsNullOrEmpty(entry.ImagePath) && File.Exists(entry.ImagePath))
                File.Delete(entry.ImagePath);
        }
        catch { /* best effort */ }
    }
}
