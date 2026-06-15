using System.Collections.ObjectModel;
using System.IO;
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
        var path = _storage.NewImagePath();
        try
        {
            using var fs = new FileStream(path, FileMode.Create);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bmp));
            encoder.Save(fs);
        }
        catch
        {
            return;
        }
        Entries.Insert(0, new ClipboardEntry { Kind = EntryKind.Image, ImagePath = path });
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
}
