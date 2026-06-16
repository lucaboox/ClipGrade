using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ClipboardApp.Models;
using ClipboardApp.Services;
using System.Text;
using static ClipboardApp.Services.NativeMethods;
using Point = System.Windows.Point;
using RadioButton = System.Windows.Controls.RadioButton;
using DpiChangedEventArgs = System.Windows.DpiChangedEventArgs;

namespace ClipboardApp;

public partial class MainWindow : Window
{
    private const int HotkeyId = 0xB001;

    private readonly ClipboardStore _store;
    private readonly AppSettings _settings;
    private ICollectionView _view = null!;

    private IntPtr _hwnd;
    private HwndSource? _source;
    private IntPtr _previousForeground;          // window focused when the hotkey fired
    private string _activeTab = "All";
    private DispatcherTimer? _captureTimer;      // debounces multi-format clipboard bursts
    private readonly HookManager _hooks = new(); // keyboard/mouse hooks while the popup is open
    private readonly HookManager _triggerHook = new(); // always-on hook for the Win+V override
    private DispatcherTimer? _fullscreenTimer;   // polls for fullscreen apps to pause hooks
    private bool _suspended;                      // true while paused for a fullscreen app
    private bool _contextMenuOpen;                // true while a row's right-click menu is open
    private DateTime _ctxClosedAt;                // when the last context menu closed (for toggle)
    private System.Windows.Controls.ContextMenu? _openContextMenu; // the currently open row menu

    public MainWindow(ClipboardStore store, AppSettings settings)
    {
        _store = store;
        _settings = settings;
        InitializeComponent();
    }

    public void InitializeBackground()
    {
        _view = CollectionViewSource.GetDefaultView(_store.Entries);
        _view.Filter = FilterPredicate;
        List.ItemsSource = _view;
        _store.Entries.CollectionChanged += (_, _) => UpdateEmptyState();
        UpdateEmptyState();

        var helper = new WindowInteropHelper(this);
        helper.EnsureHandle();
        _hwnd = helper.Handle;

        _source = HwndSource.FromHwnd(_hwnd);
        _source?.AddHook(WndProc);

        // Non-activating tool window: showing/clicking it never steals focus from
        // the text field you were typing in, so paste lands there.
        var ex = GetWindowLongPtr(_hwnd, GWL_EXSTYLE).ToInt64();
        ex |= WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW;
        SetWindowLongPtr(_hwnd, GWL_EXSTYLE, (IntPtr)ex);

        _hooks.KeyDown = OnHookKeyDown;
        _hooks.MouseDown = OnHookMouseDown;
        _triggerHook.KeyDown = OnGlobalKeyDown;

        _captureTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(180) };
        _captureTimer.Tick += (_, _) => { _captureTimer!.Stop(); TryCapture(0); };

        AddClipboardFormatListener(_hwnd);
        ApplyHotkeyState();
        ApplyPauseInFullscreen(_settings.PauseInFullscreen);
    }

    // ------------------------------------------------------------------
    //  Auto-pause while a fullscreen app (game) is focused
    // ------------------------------------------------------------------
    public void SetPauseInFullscreen(bool enabled)
    {
        _settings.PauseInFullscreen = enabled;
        ApplyPauseInFullscreen(enabled);
    }

    private void ApplyPauseInFullscreen(bool enabled)
    {
        if (enabled)
        {
            _fullscreenTimer ??= new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.5) };
            _fullscreenTimer.Tick -= OnFullscreenTick;
            _fullscreenTimer.Tick += OnFullscreenTick;
            _fullscreenTimer.Start();
        }
        else
        {
            _fullscreenTimer?.Stop();
            Resume(); // make sure we're not left suspended
        }
    }

    private void OnFullscreenTick(object? sender, EventArgs e)
    {
        bool fs = IsForegroundFullscreen();
        if (fs && !_suspended) Suspend();
        else if (!fs && _suspended) Resume();
    }

    private bool IsForegroundFullscreen()
    {
        IntPtr fg = GetForegroundWindow();
        if (fg == IntPtr.Zero || fg == _hwnd) return false;
        if (fg == GetShellWindow() || fg == GetDesktopWindow()) return false;
        if (!GetWindowRect(fg, out var r)) return false;

        var bounds = System.Windows.Forms.Screen.FromHandle(fg).Bounds;
        bool coversScreen = r.Left <= bounds.Left && r.Top <= bounds.Top &&
                            r.Right >= bounds.Right && r.Bottom >= bounds.Bottom;
        if (!coversScreen) return false;

        // A maximized normal window also covers the screen but keeps its title
        // bar (WS_CAPTION). Only treat *borderless* full-monitor windows — i.e.
        // real fullscreen games/video — as fullscreen.
        long style = GetWindowLongPtr(fg, GWL_STYLE).ToInt64();
        bool hasCaption = (style & WS_CAPTION) == WS_CAPTION;
        return !hasCaption;
    }

    private void Suspend()
    {
        if (_suspended) return;
        _suspended = true;
        UnregisterHotKey(_hwnd, HotkeyId);
        _triggerHook.Uninstall();
        if (IsVisible) HidePopup();
    }

    private void Resume()
    {
        if (!_suspended) return;
        _suspended = false;
        ApplyHotkeyState();
    }

    /// <summary>Enables/disables the Win+V override. When on, it replaces the
    /// regular hotkey (which is unregistered).</summary>
    public void SetUseWinV(bool enabled)
    {
        _settings.Hotkey.UseWinV = enabled;
        ApplyHotkeyState();
    }

    /// <summary>
    /// Always-on hook: catches Win+V, swallows it so Windows' clipboard never
    /// opens, and shows our popup instead.
    /// </summary>
    private bool OnGlobalKeyDown(int vk)
    {
        if (vk != VK_V) return false;
        bool win = (GetAsyncKeyState(VK_LWIN) & 0x8000) != 0 || (GetAsyncKeyState(VK_RWIN) & 0x8000) != 0;
        if (!win) return false;

        // Tap Ctrl to "mask" the Win key so releasing it doesn't open the Start menu.
        keybd_event((byte)VK_CONTROL, 0, 0, UIntPtr.Zero);
        keybd_event((byte)VK_CONTROL, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);

        // Press Win+V again to close it.
        Dispatcher.BeginInvoke(new Action(() => { if (IsVisible) HidePopup(); else ShowAtCaret(); }));
        return true; // swallow V — Windows never sees the Win+V combo
    }

    public void ShutdownBackground()
    {
        if (_hwnd != IntPtr.Zero)
        {
            UnregisterHotKey(_hwnd, HotkeyId);
            RemoveClipboardFormatListener(_hwnd);
        }
        _hooks.Dispose();
        _triggerHook.Dispose();
        _source?.RemoveHook(WndProc);
    }

    // ------------------------------------------------------------------
    //  Hotkey
    // ------------------------------------------------------------------
    /// <summary>
    /// Reconciles the active trigger with settings: Win+V hook when enabled
    /// (regular hotkey unregistered), otherwise the regular RegisterHotKey hotkey.
    /// </summary>
    private void ApplyHotkeyState()
    {
        UnregisterHotKey(_hwnd, HotkeyId);

        if (_settings.Hotkey.UseWinV)
        {
            _triggerHook.Install(keyboard: true, mouse: false);
        }
        else
        {
            _triggerHook.Uninstall();
            var hk = _settings.Hotkey;
            if (hk.IsSet) RegisterHotKey(_hwnd, HotkeyId, hk.Modifiers | MOD_NOREPEAT, hk.Key);
        }
    }

    /// <summary>
    /// Tries to bind a new hotkey. Returns true on success (or on an intentional
    /// clear, key==0); false if a real combo couldn't be registered — in which
    /// case the hotkey is left unbound so the caller can warn the user.
    /// </summary>
    public bool TryApplyHotkey(uint modifiers, uint key)
    {
        UnregisterHotKey(_hwnd, HotkeyId);

        if (key != 0 && RegisterHotKey(_hwnd, HotkeyId, modifiers | MOD_NOREPEAT, key))
        {
            _settings.Hotkey.Modifiers = modifiers;
            _settings.Hotkey.Key = key;
            return true;
        }

        _settings.Hotkey.Modifiers = 0;
        _settings.Hotkey.Key = 0;
        return key == 0;
    }

    /// <summary>Human-readable hotkey string, e.g. "Ctrl+Shift+V".</summary>
    public static string FormatHotkey(uint mods, uint key)
    {
        if (key == 0) return "None";
        var parts = new List<string>();
        if ((mods & MOD_CONTROL) != 0) parts.Add("Ctrl");
        if ((mods & MOD_ALT) != 0) parts.Add("Alt");
        if ((mods & MOD_SHIFT) != 0) parts.Add("Shift");
        if ((mods & MOD_WIN) != 0) parts.Add("Win");
        parts.Add(KeyInterop.KeyFromVirtualKey((int)key).ToString());
        return string.Join("+", parts);
    }

    // ------------------------------------------------------------------
    //  Native message pump
    // ------------------------------------------------------------------
    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        switch (msg)
        {
            case WM_HOTKEY when wParam.ToInt32() == HotkeyId:
                if (IsVisible) HidePopup(); else ShowAtCaret(); // press again to close
                handled = true;
                break;

            case WM_CLIPBOARDUPDATE:
                OnClipboardChanged();
                handled = true;
                break;

            case WM_MOUSEACTIVATE:
                handled = true;
                return (IntPtr)MA_NOACTIVATE;
        }
        return IntPtr.Zero;
    }

    // ------------------------------------------------------------------
    //  Clipboard capture
    // ------------------------------------------------------------------
    private void OnClipboardChanged()
    {
        if (_store.ShouldSuppressCapture) return;
        _captureTimer?.Stop();
        _captureTimer?.Start();
    }

    private void TryCapture(int attempt)
    {
        try
        {
            if (System.Windows.Clipboard.ContainsImage())
            {
                var bmp = System.Windows.Clipboard.GetImage();
                if (bmp != null) _store.AddImage(bmp);
            }
            else if (System.Windows.Clipboard.ContainsFileDropList())
            {
                CaptureFiles(System.Windows.Clipboard.GetFileDropList());
            }
            else if (System.Windows.Clipboard.ContainsText())
            {
                var text = System.Windows.Clipboard.GetText();
                if (!string.IsNullOrWhiteSpace(text)) _store.AddText(text);
            }
            _view?.Refresh();
            UpdateEmptyState();
        }
        catch (Exception) when (attempt < 5)
        {
            Dispatcher.BeginInvoke(new Action(() => TryCapture(attempt + 1)), DispatcherPriority.Background);
        }
        catch { /* give up */ }
    }

    /// <summary>
    /// Captures copied image files as image entries (with preview). Non-image
    /// files are ignored.
    /// </summary>
    private void CaptureFiles(System.Collections.Specialized.StringCollection files)
    {
        foreach (var path in files)
        {
            if (string.IsNullOrEmpty(path)) continue;
            if (TryLoadImageFile(path, out var bmp) && bmp != null)
                _store.AddImage(bmp);
        }
    }

    /// <summary>Decodes a known image file to a bitmap (uses the system imaging codecs).</summary>
    private static bool TryLoadImageFile(string path, out BitmapSource? bmp)
    {
        bmp = null;
        try
        {
            if (!File.Exists(path)) return false;
            var ext = Path.GetExtension(path).ToLowerInvariant();
            if (ext is not (".png" or ".jpg" or ".jpeg" or ".jfif" or ".bmp" or ".gif"
                or ".tif" or ".tiff" or ".webp" or ".ico"))
                return false;

            var decoder = BitmapDecoder.Create(new Uri(path),
                BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
            var frame = decoder.Frames[0];
            frame.Freeze();
            bmp = frame;
            return true;
        }
        catch
        {
            return false; // unsupported (e.g. webp with no system codec) — caller records the path
        }
    }

    // ------------------------------------------------------------------
    //  Showing the popup
    // ------------------------------------------------------------------
    public void ShowAtCaret()
    {
        _previousForeground = GetForegroundWindow();

        _contextMenuOpen = false; // safety: never start a session with the gate stuck
        SearchBox.Text = string.Empty;
        TabAll.IsChecked = true;
        _activeTab = "All";
        _view.Refresh();
        UpdateEmptyState();

        Visibility = Visibility.Visible;
        UpdateLayout();
        PositionWindow();

        Show();
        Topmost = true;

        if (List.Items.Count > 0)
        {
            List.SelectedIndex = 0;
            List.ScrollIntoView(List.SelectedItem);
        }

        _hooks.Install();
    }

    private void HidePopup()
    {
        // Close any open right-click menu too, so it isn't left floating.
        if (_openContextMenu != null) _openContextMenu.IsOpen = false;
        _hooks.Uninstall();
        Hide();
    }

    private void PositionWindow()
    {
        // Anchor in physical screen pixels (caret, or mouse as fallback).
        int anchorX, anchorY, caretH;
        var caret = InputHelper.GetCaretScreenRect(_previousForeground);
        if (caret.HasValue)
        {
            anchorX = (int)caret.Value.Left;
            anchorY = (int)caret.Value.Top;
            caretH = (int)caret.Value.Height;
        }
        else
        {
            var p = InputHelper.GetCursorScreenPoint();
            anchorX = (int)p.X;
            anchorY = (int)p.Y;
            caretH = 0;
        }

        // Read the target monitor's scale directly so placement is correct on the
        // first frame, even if the window was last shown on a different-DPI monitor.
        double scale = 1.0;
        var mon = MonitorFromPoint(new POINT { X = anchorX, Y = anchorY }, MONITOR_DEFAULTTONEAREST);
        if (mon != IntPtr.Zero && GetDpiForMonitor(mon, MDT_EFFECTIVE_DPI, out uint dpiX, out _) == 0 && dpiX > 0)
            scale = dpiX / 96.0;

        // Estimated physical size, used only for the above-caret math and on-screen
        // clamping. WPF still owns the real window size (so it stays correct and
        // tab clicks don't resize/reposition it).
        int estW = (int)Math.Round((double.IsNaN(Width) ? ActualWidth : Width) * scale);
        int estH = (int)Math.Round((double.IsNaN(Height) ? ActualHeight : Height) * scale);
        int gap = (int)Math.Round(8 * scale);

        var wa = System.Windows.Forms.Screen.FromPoint(new System.Drawing.Point(anchorX, anchorY)).WorkingArea;

        int left = anchorX;
        int top = anchorY - estH - gap;                  // above the caret
        if (top < wa.Top) top = anchorY + caretH + gap;  // …or below if it would clip

        left = Math.Min(Math.Max(left, wa.Left), wa.Right - estW);
        top = Math.Min(Math.Max(top, wa.Top), wa.Bottom - estH);

        // Position only (SWP_NOSIZE) — physical pixels, no size change.
        SetWindowPos(_hwnd, IntPtr.Zero, left, top, 0, 0, SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE);
    }

    // ------------------------------------------------------------------
    //  Pasting
    // ------------------------------------------------------------------
    private void PasteSelected()
    {
        if (List.SelectedItem is ClipboardEntry entry) PasteEntry(entry);
    }

    private void PasteEntry(ClipboardEntry entry)
    {
        _store.CopyToClipboard(entry);
        HidePopup();

        var target = _previousForeground;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(90) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            InputHelper.RestoreFocusAndPaste(target);
        };
        timer.Start();
    }

    // ------------------------------------------------------------------
    //  Filtering (tabs + search)
    // ------------------------------------------------------------------
    private bool FilterPredicate(object obj)
    {
        if (obj is not ClipboardEntry e) return false;

        switch (_activeTab)
        {
            case "Pinned" when !e.IsPinned: return false;
            case "Images" when !e.IsImage: return false;
        }

        var q = SearchBox.Text?.Trim();
        if (!string.IsNullOrEmpty(q))
        {
            if (e.Kind == EntryKind.Text)
                return (e.Text ?? string.Empty).Contains(q, StringComparison.OrdinalIgnoreCase);
            return "image picture photo".Contains(q, StringComparison.OrdinalIgnoreCase);
        }
        return true;
    }

    // ------------------------------------------------------------------
    //  UI events
    // ------------------------------------------------------------------
    private void OnSearchChanged(object sender, TextChangedEventArgs e)
    {
        if (_view == null) return;
        bool empty = string.IsNullOrEmpty(SearchBox.Text);
        SearchPlaceholder.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
        ClearSearchBtn.Visibility = empty ? Visibility.Collapsed : Visibility.Visible;
        _view.Refresh();
        UpdateEmptyState();
        if (List.Items.Count > 0) List.SelectedIndex = 0;
    }

    private void OnClearSearch(object sender, RoutedEventArgs e)
    {
        SearchBox.Text = string.Empty;
    }

    private void OnTabChanged(object sender, RoutedEventArgs e)
    {
        if (_view == null) return;
        if (sender is RadioButton rb && rb.IsChecked == true)
        {
            _activeTab = rb == TabPinned ? "Pinned" : rb == TabImages ? "Images" : "All";
            _view.Refresh();
            UpdateEmptyState();
            if (List.Items.Count > 0) List.SelectedIndex = 0;
        }
    }

    private void UpdateEmptyState()
    {
        if (EmptyState == null || List == null) return;
        bool hasItems = List.Items.Count > 0;
        EmptyState.Visibility = hasItems ? Visibility.Collapsed : Visibility.Visible;

        var q = SearchBox.Text?.Trim();
        EmptyState.Text = !string.IsNullOrEmpty(q)
            ? "No matching clipboard items."
            : _activeTab switch
            {
                "Pinned" => "Pinned items will stay here.",
                "Images" => "Copied images will appear here.",
                _ => "Copy text or an image to start building your history."
            };
    }

    private void OnListPreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        // The popup never activates, so select the clicked row explicitly.
        var item = FindAncestor<ListBoxItem>(e.OriginalSource as DependencyObject);
        if (item?.DataContext is not ClipboardEntry entry) return;

        List.SelectedItem = entry;

        // Single-click paste (default). In double-click mode this just selects.
        if (!_settings.PasteOnDoubleClick)
        {
            PasteEntry(entry);
            e.Handled = true;
        }
    }

    private void OnItemDoubleClick(object sender, MouseButtonEventArgs e)
    {
        var item = FindAncestor<ListBoxItem>(e.OriginalSource as DependencyObject);
        if (item?.DataContext is ClipboardEntry entry) PasteEntry(entry);
        else PasteSelected();
    }

    private static T? FindAncestor<T>(DependencyObject? current) where T : DependencyObject
    {
        while (current != null)
        {
            if (current is T match) return match;
            current = System.Windows.Media.VisualTreeHelper.GetParent(current);
        }
        return null;
    }

    private void OnMenuPaste(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is ClipboardEntry entry)
            PasteEntry(entry);
    }

    private void OnMenuCopy(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is ClipboardEntry entry)
        {
            _store.CopyToClipboard(entry);
            HidePopup();
        }
    }

    private void OnPinClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is ClipboardEntry entry)
        {
            _store.TogglePin(entry);
            _view.Refresh();
        }
        e.Handled = true;
    }

    private void OnDeleteClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is ClipboardEntry entry)
        {
            _store.Delete(entry);
            _view.Refresh();
        }
        e.Handled = true;
    }

    // ------------------------------------------------------------------
    //  Global hook handlers (the popup has no keyboard focus of its own)
    // ------------------------------------------------------------------
    // The ContextMenu's own Opened/Closed events are reliable; the element's
    // ContextMenuClosing routed event is not (it sometimes never fires, which
    // would leave the gate stuck and break Esc / click-outside).
    private void OnCtxOpened(object sender, RoutedEventArgs e)
    {
        _contextMenuOpen = true;
        _openContextMenu = sender as System.Windows.Controls.ContextMenu;
    }
    private void OnCtxClosed(object sender, RoutedEventArgs e)
    {
        _contextMenuOpen = false;
        _openContextMenu = null;
        _ctxClosedAt = DateTime.UtcNow;
    }

    private void OnListContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        // A second right-click closes the open menu (via capture loss) and then
        // tries to reopen it — suppress that reopen so right-click toggles.
        if ((DateTime.UtcNow - _ctxClosedAt).TotalMilliseconds < 250)
            e.Handled = true;
    }

    private bool OnHookKeyDown(int vk)
    {
        if (!IsVisible) return false;
        if (_contextMenuOpen) return false; // let the right-click menu handle keys

        switch (vk)
        {
            case VK_ESCAPE: HidePopup(); return true;
            case VK_RETURN: PasteSelected(); return true;
            case VK_UP: MoveSelection(-1); return true;
            case VK_DOWN: MoveSelection(1); return true;
            case VK_BACK:
                if (SearchBox.Text.Length > 0)
                    SearchBox.Text = SearchBox.Text[..^1];
                return true;
        }

        bool ctrl = (GetAsyncKeyState(VK_CONTROL) & 0x8000) != 0;
        bool alt = (GetAsyncKeyState(0x12 /* VK_MENU */) & 0x8000) != 0;
        bool win = (GetAsyncKeyState(VK_LWIN) & 0x8000) != 0 || (GetAsyncKeyState(VK_RWIN) & 0x8000) != 0;
        // Pass modified keys through (incl. Win+V, which the trigger hook toggles)
        // rather than typing them into the search box.
        if (ctrl || alt || win) return false;

        var text = TranslateKey(vk);
        if (!string.IsNullOrEmpty(text) && !char.IsControl(text[0]))
        {
            SearchBox.Text += text;
            return true;
        }
        return false;
    }

    private void OnHookMouseDown(int x, int y)
    {
        if (!IsVisible) return;

        // A click on the open right-click menu must not close the popup; any
        // other click outside the popup should.
        if (_contextMenuOpen && PointInContextMenu(x, y)) return;

        if (GetWindowRect(_hwnd, out var r) &&
            (x < r.Left || x > r.Right || y < r.Top || y > r.Bottom))
        {
            HidePopup();
        }
    }

    private bool PointInContextMenu(int x, int y)
    {
        if (_openContextMenu == null) return false;
        if (PresentationSource.FromVisual(_openContextMenu) is not HwndSource src) return false;
        if (!GetWindowRect(src.Handle, out var mr)) return false;
        return x >= mr.Left && x <= mr.Right && y >= mr.Top && y <= mr.Bottom;
    }

    private static string? TranslateKey(int vk)
    {
        var ks = new byte[256];
        if ((GetAsyncKeyState(VK_SHIFT) & 0x8000) != 0) ks[VK_SHIFT] = 0x80;
        if ((GetKeyState(VK_CAPITAL) & 0x0001) != 0) ks[VK_CAPITAL] = 0x01;
        uint scan = MapVirtualKey((uint)vk, 0);
        var sb = new StringBuilder(8);
        int r = ToUnicode((uint)vk, scan, ks, sb, sb.Capacity, 0);
        return r > 0 ? sb.ToString() : null;
    }

    private void MoveSelection(int delta)
    {
        int count = List.Items.Count;
        if (count == 0) return;
        int next = List.SelectedIndex + delta;
        next = Math.Min(Math.Max(next, 0), count - 1);
        List.SelectedIndex = next;
        List.ScrollIntoView(List.SelectedItem);
    }
}
