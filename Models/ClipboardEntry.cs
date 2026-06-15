using System.ComponentModel;
using System.IO;
using System.Text.Json.Serialization;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ClipboardApp.Models;

public enum EntryKind
{
    Text,
    Image
}

/// <summary>
/// One saved clipboard item. Text items keep their text inline; image items
/// reference a PNG file on disk (so the JSON store stays small).
/// </summary>
public class ClipboardEntry : INotifyPropertyChanged
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public EntryKind Kind { get; set; }

    /// <summary>Full text for text entries. Null for images.</summary>
    public string? Text { get; set; }

    /// <summary>Absolute path to the PNG for image entries. Null for text.</summary>
    public string? ImagePath { get; set; }

    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

    private bool _isPinned;
    public bool IsPinned
    {
        get => _isPinned;
        set { _isPinned = value; OnChanged(nameof(IsPinned)); OnChanged(nameof(PinGlyph)); OnChanged(nameof(PinTooltip)); OnChanged(nameof(PinMenuText)); }
    }

    // ---- View helpers (not serialized) ----

    [JsonIgnore]
    public bool IsImage => Kind == EntryKind.Image;

    [JsonIgnore]
    public bool IsText => Kind == EntryKind.Text;

    [JsonIgnore]
    public string PinGlyph => IsPinned ? "📌" : "📍";

    [JsonIgnore]
    public string PinTooltip => IsPinned ? "Unpin" : "Pin (kept forever)";

    [JsonIgnore]
    public string PinMenuText => IsPinned ? "Unpin" : "Pin (keep forever)";

    /// <summary>Single-line preview used in the list.</summary>
    [JsonIgnore]
    public string DisplayText
    {
        get
        {
            if (IsImage) return "🖼  Image";
            var t = (Text ?? string.Empty).Trim();
            t = t.Replace("\r", " ").Replace("\n", " ");
            return t.Length > 200 ? t[..200] + "…" : t;
        }
    }

    /// <summary>Full, untruncated text for the hover tooltip (null for images).</summary>
    [JsonIgnore]
    public string? FullText => IsImage ? null : Text;

    private BitmapImage? _thumbnail;
    [JsonIgnore]
    public ImageSource? Thumbnail
    {
        get
        {
            if (_thumbnail != null) return _thumbnail;
            if (!IsImage || string.IsNullOrEmpty(ImagePath) || !File.Exists(ImagePath)) return null;
            try
            {
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.DecodePixelWidth = 260; // thumbnail size, keeps memory low
                bmp.UriSource = new Uri(ImagePath);
                bmp.EndInit();
                bmp.Freeze();
                _thumbnail = bmp;
            }
            catch { /* corrupt file -> no thumbnail */ }
            return _thumbnail;
        }
    }

    private BitmapImage? _preview;
    /// <summary>Larger image used for the hover preview tooltip (loaded on demand).</summary>
    [JsonIgnore]
    public ImageSource? Preview
    {
        get
        {
            if (_preview != null) return _preview;
            if (!IsImage || string.IsNullOrEmpty(ImagePath) || !File.Exists(ImagePath)) return null;
            try
            {
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.DecodePixelWidth = 600;
                bmp.UriSource = new Uri(ImagePath);
                bmp.EndInit();
                bmp.Freeze();
                _preview = bmp;
            }
            catch { /* corrupt file -> no preview */ }
            return _preview;
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnChanged(string name) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
