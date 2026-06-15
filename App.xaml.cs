using System.Threading;
using System.Windows;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms; // NotifyIcon
using ClipboardApp.Models;
using ClipboardApp.Services;

namespace ClipboardApp;

public partial class App : System.Windows.Application
{
    private const string SingleInstanceMutexName = @"Local\ClipGrade_SingleInstance";
    private Mutex? _instanceMutex;
    private bool _ownsInstanceMutex;

    private NotifyIcon? _tray;
    private Icon? _trayIcon;
    private MainWindow? _window;
    private SettingsWindow? _settingsWindow;

    private SettingsStore _settingsStore = null!;
    private AppSettings _settings = null!;
    private ClipboardStore _store = null!;

    private void OnStartup(object sender, StartupEventArgs e)
    {
        // Only allow one instance — a second launch would fight over the saved
        // data file and add a duplicate tray icon.
        _instanceMutex = new Mutex(true, SingleInstanceMutexName, out _ownsInstanceMutex);
        if (!_ownsInstanceMutex)
        {
            Shutdown();
            return;
        }

        // Carry over data saved under the old app name.
        Storage.MigrateFromLegacy();

        _settingsStore = new SettingsStore();
        _settings = _settingsStore.Load();
        ThemeManager.Apply(ThemePresets.Resolve(_settings));

        _store = new ClipboardStore();
        _store.Load();
        _store.SetMaxHistory(_settings.MaxHistory);

        // The popup also serves as the hidden clipboard/hotkey message sink.
        _window = new MainWindow(_store, _settings);
        _window.InitializeBackground();

        SetupTrayIcon();
    }

    private void SetupTrayIcon()
    {
        _trayIcon = CreateClipboardIcon();
        _tray = new NotifyIcon
        {
            Icon = _trayIcon,
            Visible = true,
            Text = "ClipGrade"
        };

        var menu = new ContextMenuStrip();
        menu.Items.Add("Show clipboard", null, (_, _) => _window?.ShowAtCaret());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Settings…", null, (_, _) => OpenSettings(0));
        menu.Items.Add("Clipboard history…", null, (_, _) => OpenSettings(2));
        menu.Items.Add(new ToolStripSeparator());

        var startupItem = new ToolStripMenuItem("Start on boot")
        {
            CheckOnClick = true,
            Checked = StartupManager.IsEnabled()
        };
        startupItem.Click += (_, _) => StartupManager.SetEnabled(startupItem.Checked);
        menu.Items.Add(startupItem);

        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => Shutdown());
        _tray.ContextMenuStrip = menu;

        _tray.DoubleClick += (_, _) => _window?.ShowAtCaret();
    }

    /// <summary>Draws a small clipboard glyph for the tray icon (no asset file needed).</summary>
    private static Icon CreateClipboardIcon()
    {
        using var bmp = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);

            // Board
            using (var board = RoundedRect(new RectangleF(6, 5, 20, 24), 3))
            using (var fill = new SolidBrush(Color.FromArgb(0x4F, 0x5B, 0xD5)))
                g.FillPath(fill, board);

            // Clip at the top
            using (var clip = RoundedRect(new RectangleF(11, 2, 10, 6), 2))
            using (var light = new SolidBrush(Color.FromArgb(0xEC, 0xEC, 0xF2)))
                g.FillPath(light, clip);

            // Text lines
            using (var pen = new Pen(Color.FromArgb(0xEC, 0xEC, 0xF2), 2))
            {
                g.DrawLine(pen, 10, 14, 22, 14);
                g.DrawLine(pen, 10, 19, 22, 19);
                g.DrawLine(pen, 10, 24, 18, 24);
            }
        }

        IntPtr hicon = bmp.GetHicon();
        using var temp = Icon.FromHandle(hicon);
        return (Icon)temp.Clone(); // own a managed copy so we can free the HICON
    }

    private static GraphicsPath RoundedRect(RectangleF r, float radius)
    {
        float d = radius * 2;
        var path = new GraphicsPath();
        path.AddArc(r.Left, r.Top, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Top, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.Left, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    private void OpenSettings(int tabIndex)
    {
        if (_settingsWindow == null || !_settingsWindow.IsLoaded)
        {
            _settingsWindow = new SettingsWindow(_store, _settings, _settingsStore, _window!);
            _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        }
        _settingsWindow.SelectTab(tabIndex);
        _settingsWindow.Show();
        _settingsWindow.Activate();
    }

    private void OnExit(object sender, ExitEventArgs e)
    {
        if (_tray != null)
        {
            _tray.Visible = false;
            _tray.Dispose();
        }
        _trayIcon?.Dispose();
        _window?.ShutdownBackground();

        if (_ownsInstanceMutex) _instanceMutex?.ReleaseMutex();
        _instanceMutex?.Dispose();
    }
}
