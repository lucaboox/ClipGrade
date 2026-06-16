using System.Collections.ObjectModel;
using System.IO;
using System.Security.Cryptography;
using System.Windows.Media.Imaging;
using ClipboardApp.Models;

namespace ClipboardApp.Services;

/// <summary>
/// The single source of truth for clipboard entries. Owns capture, de-dup,
/// trimming, persistence, and clipboard read/write. Shared by the popup and the
/// Clipboard manager window so edits stay in sync.
/// </summary>
public class ClipboardStore
{
    public const int MinHistory = 30;
    public const int MaxHistoryLimit = 100;

    /// <summary>How many unpinned entries to keep (pinned are unlimited).</summary>
    public int MaxHistory { get; private set; } = 30;

    public ObservableCollection<ClipboardEntry> Entries { get; } = new();

    private readonly Storage _storage = new();
    private DateTime _suppressUntil;            // ignore clipboard changes we cause

    public void Load()
    {
        foreach (var e in _storage.Load()
                     .OrderByDescending(x => x.IsPinned)
                     .ThenByDescending(x => x.CreatedUtc))
            Entries.Add(e);
    }

    /// <summary>True while we're suppressing capture of a clipboard change we made.</summary>
    public bool ShouldSuppressCapture => DateTime.UtcNow < _suppressUntil;

    public void AddText(string text)
    {
        var existing = Entries.FirstOrDefault(e => e.Kind == EntryKind.Text && e.Text == text);
        if (existing != null)
        {
            Entries.Remove(existing);
            existing.CreatedUtc = DateTime.UtcNow;
            Entries.Insert(0, existing);
        }
        else
        {
            Entries.Insert(0, new ClipboardEntry { Kind = EntryKind.Text, Text = text });
        }
        TrimAndSave();
    }

    public void AddImage(BitmapSource bmp)
    {
        byte[] pngBytes;
        string hash;
        try
        {
            using var ms = new MemoryStream();
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bmp));
            encoder.Save(ms);
            pngBytes = ms.ToArray();
            hash = Convert.ToHexString(SHA256.HashData(pngBytes));
        }
        catch
        {
            return;
        }

        var existing = Entries.FirstOrDefault(e => e.Kind == EntryKind.Image && ImageHash(e) == hash);
        if (existing != null)
        {
            Entries.Remove(existing);
            existing.CreatedUtc = DateTime.UtcNow;
            existing.ContentHash = hash;
            Entries.Insert(0, existing);
            TrimAndSave();
            return;
        }

        var path = _storage.NewImagePath();
        try
        {
            File.WriteAllBytes(path, pngBytes);
        }
        catch
        {
            return;
        }
        Entries.Insert(0, new ClipboardEntry { Kind = EntryKind.Image, ImagePath = path, ContentHash = hash });
        TrimAndSave();
    }

    /// <summary>Sets the unpinned history cap (clamped) and trims to it.</summary>
    public void SetMaxHistory(int value)
    {
        MaxHistory = Math.Clamp(value, MinHistory, MaxHistoryLimit);
        TrimAndSave();
    }

    public void TogglePin(ClipboardEntry entry)
    {
        entry.IsPinned = !entry.IsPinned;
        TrimAndSave();
    }

    public void Delete(ClipboardEntry entry)
    {
        _storage.DeleteImageFile(entry);
        Entries.Remove(entry);
        TrimAndSave();
    }

    /// <summary>
    /// Puts an entry onto the system clipboard (used for both paste and copy).
    /// Suppresses our own capture so it isn't re-recorded as a new entry.
    /// </summary>
    public void CopyToClipboard(ClipboardEntry entry)
    {
        try
        {
            _suppressUntil = DateTime.UtcNow.AddMilliseconds(700);
            if (entry.Kind == EntryKind.Text)
            {
                System.Windows.Clipboard.SetText(entry.Text ?? string.Empty);
            }
            else if (entry.Kind == EntryKind.Image && entry.ImagePath != null && File.Exists(entry.ImagePath))
            {
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.UriSource = new Uri(entry.ImagePath);
                bmp.EndInit();
                bmp.Freeze();
                System.Windows.Clipboard.SetImage(bmp);
            }
        }
        catch { /* clipboard contention — ignore */ }
    }

    /// <summary>Keeps at most MaxHistory unpinned entries; pinned are never trimmed.</summary>
    private void TrimAndSave()
    {
        int unpinned = 0;
        for (int i = 0; i < Entries.Count; i++)
        {
            if (Entries[i].IsPinned) continue;
            unpinned++;
            if (unpinned > MaxHistory)
            {
                _storage.DeleteImageFile(Entries[i]);
                Entries.RemoveAt(i);
                i--;
            }
        }
        _storage.Save(Entries);
    }

    private static string? ImageHash(ClipboardEntry entry)
    {
        if (!string.IsNullOrEmpty(entry.ContentHash)) return entry.ContentHash;
        if (string.IsNullOrEmpty(entry.ImagePath) || !File.Exists(entry.ImagePath)) return null;

        try
        {
            entry.ContentHash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(entry.ImagePath)));
            return entry.ContentHash;
        }
        catch
        {
            return null;
        }
    }
}
