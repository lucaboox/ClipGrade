using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ClipboardApp.Models;
using ClipboardApp.Services;
using static ClipboardApp.Services.NativeMethods;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using MessageBox = System.Windows.MessageBox;
using TextBox = System.Windows.Controls.TextBox;
using Button = System.Windows.Controls.Button;
using Cursors = System.Windows.Input.Cursors;
using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;
using Brush = System.Windows.Media.Brush;
using Orientation = System.Windows.Controls.Orientation;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using RadioButton = System.Windows.Controls.RadioButton;

namespace ClipboardApp;

public partial class SettingsWindow : Window
{
    private readonly ClipboardStore _store;
    private readonly AppSettings _settings;
    private readonly SettingsStore _settingsStore;
    private readonly MainWindow _main;

    // shortcut capture state
    private bool _capturing;
    private uint _pendingMods;
    private uint _pendingKey;

    private bool _suppressThemeEvents;
    private readonly List<(ColorRow Row, Border Swatch, TextBox Box, Button Pick)> _themeRows = new();
    private ThemeColors _editingColors = new();   // colors shown in the pickers
    private bool _themeEditable;                    // false for built-in themes
    private bool _populatingThemes;

    private System.Windows.Data.ListCollectionView _clipView = null!;
    private string _clipTab = "All";

    private record ColorRow(string Label, Func<ThemeColors, string> Get, Action<ThemeColors, string> Set);

    public SettingsWindow(ClipboardStore store, AppSettings settings, SettingsStore settingsStore, MainWindow main)
    {
        _store = store;
        _settings = settings;
        _settingsStore = settingsStore;
        _main = main;
        InitializeComponent();

        BuildThemeRows();
        PopulateThemeCombo();

        _clipView = new System.Windows.Data.ListCollectionView(_store.Entries) { Filter = ClipFilter };
        ClipList.ItemsSource = _clipView;
        _store.Entries.CollectionChanged += OnClipEntriesChanged;
        UpdateClipEmptyState();

        int cap = Math.Clamp(_settings.MaxHistory, (int)HistorySlider.Minimum, (int)HistorySlider.Maximum);
        HistorySlider.Value = cap;
        HistoryValue.Text = $"{cap} items";

        RefreshShortcutDisplay();
        WinVCheck.IsChecked = _settings.Hotkey.UseWinV;
        PauseGamesCheck.IsChecked = _settings.PauseInFullscreen;
        DoubleClickCheck.IsChecked = _settings.PasteOnDoubleClick;
        UpdateShortcutControls();
    }

    /// <summary>When Win+V is on, the custom-shortcut controls are disabled.</summary>
    private void UpdateShortcutControls()
    {
        bool winv = _settings.Hotkey.UseWinV;
        ShortcutButton.IsEnabled = !winv;
        SaveShortcut.IsEnabled = !winv;
        ClearShortcut.IsEnabled = !winv;
        if (winv) ShortcutButton.Content = "Win+V (active)";
        else RefreshShortcutDisplay();
    }

    public void SelectTab(int index)
    {
        switch (index)
        {
            case 1: TabTheme.IsChecked = true; break;
            case 2: TabClipboard.IsChecked = true; break;
            default: TabShortcut.IsChecked = true; break;
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        _store.Entries.CollectionChanged -= OnClipEntriesChanged;
        base.OnClosed(e);
    }

    private void OnClipEntriesChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        _clipView?.Refresh();
        UpdateClipEmptyState();
    }

    private void OnSettingsTab(object sender, RoutedEventArgs e)
    {
        if (PanelClipboard == null) return; // fires during InitializeComponent
        PanelShortcut.Visibility = TabShortcut.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        PanelTheme.Visibility = TabTheme.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        PanelClipboard.Visibility = TabClipboard.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
    }

    // ------------------------------------------------------------------
    //  Shortcut tab
    // ------------------------------------------------------------------
    private void RefreshShortcutDisplay()
    {
        _pendingMods = _settings.Hotkey.Modifiers;
        _pendingKey = _settings.Hotkey.Key;
        ShortcutButton.Content = _settings.Hotkey.IsSet
            ? MainWindow.FormatHotkey(_pendingMods, _pendingKey)
            : "None";
    }

    private void OnRecordShortcut(object sender, RoutedEventArgs e)
    {
        _capturing = true;
        ShortcutButton.Content = "Press a key combination…";
        ShortcutStatus.Text = string.Empty;
        ShortcutButton.Focus();
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        if (_capturing)
        {
            var key = e.Key == Key.System ? e.SystemKey : e.Key;
            if (IsModifierKey(key)) { e.Handled = true; return; } // wait for a non-modifier

            uint mods = 0;
            var m = Keyboard.Modifiers;
            if (m.HasFlag(ModifierKeys.Control)) mods |= MOD_CONTROL;
            if (m.HasFlag(ModifierKeys.Alt)) mods |= MOD_ALT;
            if (m.HasFlag(ModifierKeys.Shift)) mods |= MOD_SHIFT;
            if (m.HasFlag(ModifierKeys.Windows)) mods |= MOD_WIN;

            _pendingMods = mods;
            _pendingKey = (uint)KeyInterop.VirtualKeyFromKey(key);
            _capturing = false;
            ShortcutButton.Content = MainWindow.FormatHotkey(_pendingMods, _pendingKey);
            ShortcutStatus.Text = "Press Save to apply this shortcut.";
            e.Handled = true;
            return;
        }
        base.OnPreviewKeyDown(e);
    }

    private static bool IsModifierKey(Key k) => k is
        Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or
        Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin or Key.System;

    private void OnSaveShortcut(object sender, RoutedEventArgs e)
    {
        bool ok = _main.TryApplyHotkey(_pendingMods, _pendingKey);
        if (!ok)
        {
            MessageBox.Show(this,
                "That shortcut couldn't be registered — it may already be in use by Windows or another app " +
                "(for example, Win+V is reserved by Windows). It has been reset to no shortcut.",
                "Shortcut not available", MessageBoxButton.OK, MessageBoxImage.Warning);
            ShortcutStatus.Text = "No shortcut set.";
        }
        else
        {
            ShortcutStatus.Text = _settings.Hotkey.IsSet ? "Shortcut saved." : "No shortcut set.";
        }
        _settingsStore.Save(_settings);
        RefreshShortcutDisplay();
    }

    private void OnClearShortcut(object sender, RoutedEventArgs e)
    {
        _main.TryApplyHotkey(0, 0);
        _settingsStore.Save(_settings);
        RefreshShortcutDisplay();
        ShortcutStatus.Text = "No shortcut set. Open the popup from the tray icon instead.";
    }

    private void OnToggleWinV(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded) return; // fires while setting the initial state in the constructor
        bool on = WinVCheck.IsChecked == true;
        _main.SetUseWinV(on);
        _settingsStore.Save(_settings);
        UpdateShortcutControls();
        ShortcutStatus.Text = on
            ? "Win+V will now open this app (your custom shortcut is disabled while this is on)."
            : "Win+V override turned off.";
    }

    private void OnTogglePauseGames(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded) return;
        bool on = PauseGamesCheck.IsChecked == true;
        _main.SetPauseInFullscreen(on);
        _settingsStore.Save(_settings);
        ShortcutStatus.Text = on
            ? "Shortcuts will pause while a fullscreen app is open."
            : "Fullscreen pause turned off.";
    }

    private void OnTogglePasteMode(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded) return;
        _settings.PasteOnDoubleClick = DoubleClickCheck.IsChecked == true;
        _settingsStore.Save(_settings);
        ShortcutStatus.Text = _settings.PasteOnDoubleClick
            ? "Double-click an entry to paste it."
            : "Single-click an entry to paste it.";
    }

    // ------------------------------------------------------------------
    //  Theme tab
    // ------------------------------------------------------------------
    private void BuildThemeRows()
    {
        var rows = new[]
        {
            new ColorRow("Background",           t => t.Background,    (t, v) => t.Background = v),
            new ColorRow("Row background",       t => t.RowBackground, (t, v) => t.RowBackground = v),
            new ColorRow("Highlight / selected", t => t.Highlight,     (t, v) => t.Highlight = v),
            new ColorRow("Accent (active tab)",  t => t.Accent,        (t, v) => t.Accent = v),
            new ColorRow("Text",                 t => t.Text,          (t, v) => t.Text = v),
            new ColorRow("Grey text",            t => t.GreyText,      (t, v) => t.GreyText = v),
            new ColorRow("Icons",                t => t.Icon,          (t, v) => t.Icon = v),
            new ColorRow("Text box / search",    t => t.TextBox,       (t, v) => t.TextBox = v),
        };

        foreach (var row in rows)
        {
            var grid = new Grid { Margin = new Thickness(0, 4, 0, 4) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(170) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(36) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var label = new TextBlock
            {
                Text = row.Label,
                VerticalAlignment = VerticalAlignment.Center,
                FontSize = 13
            };
            label.SetResourceReference(TextBlock.ForegroundProperty, "Fg");
            Grid.SetColumn(label, 0);

            var swatch = new Border
            {
                Width = 28,
                Height = 24,
                Margin = new Thickness(0, 0, 8, 0),
                BorderThickness = new Thickness(1),
                Background = ThemeManager.Brush(row.Get(_editingColors)),
                Cursor = System.Windows.Input.Cursors.Hand
            };
            swatch.SetResourceReference(Border.BorderBrushProperty, "BorderColor");
            Grid.SetColumn(swatch, 1);

            var box = new TextBox
            {
                Text = row.Get(_editingColors),
                VerticalContentAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(box, 2);

            var pick = new Button { Content = "Pick…", Margin = new Thickness(8, 0, 0, 0) };
            pick.SetResourceReference(StyleProperty, "Btn");
            Grid.SetColumn(pick, 3);

            box.TextChanged += (_, _) => ApplyHex(row, swatch, box.Text);
            swatch.MouseLeftButtonUp += (_, _) => PickColor(box);
            pick.Click += (_, _) => PickColor(box);

            grid.Children.Add(label);
            grid.Children.Add(swatch);
            grid.Children.Add(box);
            grid.Children.Add(pick);
            ThemeHost.Children.Add(grid);

            _themeRows.Add((row, swatch, box, pick));
        }
    }

    // ---- Theme selection ----

    private void PopulateThemeCombo()
    {
        _populatingThemes = true;
        ThemeCombo.Items.Clear();
        ThemeCombo.Items.Add(ThemePresets.Dark);
        ThemeCombo.Items.Add(ThemePresets.Light);
        foreach (var t in _settings.CustomThemes) ThemeCombo.Items.Add(t.Name);
        var sel = ThemeCombo.Items.Contains(_settings.SelectedTheme) ? _settings.SelectedTheme : ThemePresets.Dark;
        _populatingThemes = false;
        ThemeCombo.SelectedItem = sel; // triggers OnThemeSelected -> SelectTheme
    }

    private void OnThemeSelected(object sender, SelectionChangedEventArgs e)
    {
        if (_populatingThemes) return;
        if (ThemeCombo.SelectedItem is string name) SelectTheme(name);
    }

    private void SelectTheme(string name)
    {
        _settings.SelectedTheme = name;
        bool custom = !ThemePresets.IsBuiltIn(name);
        if (custom)
        {
            var t = _settings.CustomThemes.FirstOrDefault(x => x.Name == name);
            _editingColors = t?.Colors ?? ThemePresets.DarkColors(); // edit the stored instance
        }
        else
        {
            _editingColors = name == ThemePresets.Light ? ThemePresets.LightColors() : ThemePresets.DarkColors();
        }

        _themeEditable = custom;
        ThemeManager.Apply(_editingColors);
        _settingsStore.Save(_settings);

        RefreshThemeRows();
        DeleteTheme.IsEnabled = custom;
        ThemeHint.Text = custom
            ? "Editing a custom theme — click a swatch or type a #RRGGBB value. Changes save automatically."
            : "Built-in themes can't be edited. Click \"Add custom theme\" to make an editable copy.";
    }

    private void RefreshThemeRows()
    {
        _suppressThemeEvents = true;
        foreach (var (row, swatch, box, pick) in _themeRows)
        {
            var hex = row.Get(_editingColors);
            box.Text = hex;
            box.IsEnabled = _themeEditable;
            pick.IsEnabled = _themeEditable;
            swatch.Background = ThemeManager.Brush(hex);
        }
        _suppressThemeEvents = false;
    }

    private void ApplyHex(ColorRow row, Border swatch, string hex)
    {
        if (_suppressThemeEvents || !_themeEditable) return;
        hex = hex.Trim();
        try
        {
            var color = (Color)ColorConverter.ConvertFromString(hex);
            swatch.Background = new SolidColorBrush(color);
            row.Set(_editingColors, hex);
            ThemeManager.Apply(_editingColors);
            _settingsStore.Save(_settings);
        }
        catch
        {
            // not a valid color yet (user still typing) — leave it
        }
    }

    private void PickColor(TextBox box)
    {
        if (!_themeEditable) return;
        using var dlg = new System.Windows.Forms.ColorDialog { FullOpen = true };
        try { dlg.Color = ToWinColor(box.Text); } catch { /* keep default */ }
        if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            box.Text = FromWinColor(dlg.Color); // triggers ApplyHex
    }

    // ---- Add / delete / import / export ----

    private void OnAddTheme(object sender, RoutedEventArgs e)
    {
        var name = PromptName("New theme", "Name for the new theme:", "My theme");
        if (string.IsNullOrWhiteSpace(name)) return;
        name = UniqueThemeName(name.Trim());
        _settings.CustomThemes.Add(new NamedTheme { Name = name, Colors = _editingColors.Clone() });
        _settingsStore.Save(_settings);
        PopulateThemeCombo();
        ThemeCombo.SelectedItem = name;
    }

    private void OnDeleteTheme(object sender, RoutedEventArgs e)
    {
        var name = _settings.SelectedTheme;
        if (ThemePresets.IsBuiltIn(name)) return;
        _settings.CustomThemes.RemoveAll(t => t.Name == name);
        _settings.SelectedTheme = ThemePresets.Dark;
        _settingsStore.Save(_settings);
        PopulateThemeCombo();
        ThemeCombo.SelectedItem = ThemePresets.Dark;
    }

    private void OnExportTheme(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            Filter = "Theme file (*.json)|*.json",
            FileName = _settings.SelectedTheme + ".json"
        };
        if (dlg.ShowDialog() != true) return;
        try
        {
            var nt = new NamedTheme { Name = _settings.SelectedTheme, Colors = _editingColors };
            var json = System.Text.Json.JsonSerializer.Serialize(nt, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
            System.IO.File.WriteAllText(dlg.FileName, json);
            ThemeHint.Text = $"Exported to {System.IO.Path.GetFileName(dlg.FileName)}.";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Couldn't export the theme:\n" + ex.Message, "Export failed",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void OnImportTheme(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog { Filter = "Theme file (*.json)|*.json" };
        if (dlg.ShowDialog() != true) return;
        try
        {
            var nt = System.Text.Json.JsonSerializer.Deserialize<NamedTheme>(System.IO.File.ReadAllText(dlg.FileName));
            if (nt?.Colors == null)
            {
                MessageBox.Show(this, "That file isn't a valid theme.", "Import failed",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            var baseName = string.IsNullOrWhiteSpace(nt.Name)
                ? System.IO.Path.GetFileNameWithoutExtension(dlg.FileName)
                : nt.Name;
            var name = UniqueThemeName(baseName);
            _settings.CustomThemes.Add(new NamedTheme { Name = name, Colors = nt.Colors });
            _settingsStore.Save(_settings);
            PopulateThemeCombo();
            ThemeCombo.SelectedItem = name;
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Couldn't import the theme:\n" + ex.Message, "Import failed",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private string UniqueThemeName(string desired)
    {
        bool Taken(string n) => ThemePresets.IsBuiltIn(n) || _settings.CustomThemes.Any(t => t.Name == n);
        if (!Taken(desired)) return desired;
        for (int i = 2; ; i++)
        {
            var candidate = $"{desired} ({i})";
            if (!Taken(candidate)) return candidate;
        }
    }

    /// <summary>Small themed text-input dialog (WPF has no built-in input box).</summary>
    private string? PromptName(string title, string label, string initial)
    {
        var dlg = new Window
        {
            Title = title,
            Width = 340,
            SizeToContent = SizeToContent.Height,
            ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = this,
            Background = (Brush)FindResource("Bg")
        };
        var panel = new StackPanel { Margin = new Thickness(16) };
        var lbl = new TextBlock { Text = label, Margin = new Thickness(0, 0, 0, 8) };
        lbl.SetResourceReference(TextBlock.ForegroundProperty, "Fg");
        var box = new TextBox { Text = initial };
        box.SelectAll();
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
        string? result = null;
        var ok = new Button { Content = "OK", Width = 72, IsDefault = true, Margin = new Thickness(0, 0, 8, 0) };
        ok.SetResourceReference(StyleProperty, "Btn");
        var cancel = new Button { Content = "Cancel", Width = 72, IsCancel = true };
        cancel.SetResourceReference(StyleProperty, "Btn");
        ok.Click += (_, _) => { result = box.Text; dlg.DialogResult = true; };
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);
        panel.Children.Add(lbl);
        panel.Children.Add(box);
        panel.Children.Add(buttons);
        dlg.Content = panel;
        box.Focus();
        return dlg.ShowDialog() == true ? result : null;
    }

    private static System.Drawing.Color ToWinColor(string hex)
    {
        var c = (Color)ColorConverter.ConvertFromString(hex);
        return System.Drawing.Color.FromArgb(c.R, c.G, c.B);
    }

    private static string FromWinColor(System.Drawing.Color c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";

    // ------------------------------------------------------------------
    //  Clipboard tab (copy only — never pastes, never closes)
    // ------------------------------------------------------------------
    private void OnClipDoubleClick(object sender, MouseButtonEventArgs e)
    {
        var item = FindAncestor<ListBoxItem>(e.OriginalSource as DependencyObject);
        if (item?.DataContext is ClipboardEntry entry)
        {
            _store.CopyToClipboard(entry);
            ClipStatus.Text = entry.IsImage ? "Image copied to clipboard." : "Copied to clipboard.";
        }
    }

    private void OnClipMenuCopy(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is ClipboardEntry entry)
        {
            _store.CopyToClipboard(entry);
            ClipStatus.Text = entry.IsImage ? "Image copied to clipboard." : "Copied to clipboard.";
        }
    }

    private void OnClipPin(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is ClipboardEntry entry)
        {
            _store.TogglePin(entry);
            _clipView.Refresh();
            UpdateClipEmptyState();
        }
        e.Handled = true;
    }

    private void OnClipDelete(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is ClipboardEntry entry)
        {
            _store.Delete(entry);
            _clipView.Refresh();
            UpdateClipEmptyState();
        }
        e.Handled = true;
    }

    // ---- Clipboard search + tabs ----
    private bool ClipFilter(object obj)
    {
        if (obj is not ClipboardEntry e) return false;
        switch (_clipTab)
        {
            case "Pinned" when !e.IsPinned: return false;
            case "Images" when !e.IsImage: return false;
        }
        var q = ClipSearchBox.Text?.Trim();
        if (!string.IsNullOrEmpty(q))
        {
            if (e.Kind == EntryKind.Text)
                return (e.Text ?? string.Empty).Contains(q, StringComparison.OrdinalIgnoreCase);
            return "image picture photo".Contains(q, StringComparison.OrdinalIgnoreCase);
        }
        return true;
    }

    private void OnClipSearchChanged(object sender, TextChangedEventArgs e)
    {
        if (_clipView == null) return;
        bool empty = string.IsNullOrEmpty(ClipSearchBox.Text);
        ClipSearchPlaceholder.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
        ClipClearBtn.Visibility = empty ? Visibility.Collapsed : Visibility.Visible;
        _clipView.Refresh();
        UpdateClipEmptyState();
    }

    private void OnClipClearSearch(object sender, RoutedEventArgs e) => ClipSearchBox.Text = string.Empty;

    private void OnHistorySizeChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!IsLoaded) return; // fires while initializing in the constructor
        int n = (int)HistorySlider.Value;
        HistoryValue.Text = $"{n} items";
        _settings.MaxHistory = n;
        _store.SetMaxHistory(n);
        _settingsStore.Save(_settings);
    }

    private void OnClipTabChanged(object sender, RoutedEventArgs e)
    {
        if (_clipView == null) return;
        if (sender is RadioButton rb && rb.IsChecked == true)
        {
            _clipTab = rb == ClipTabPinned ? "Pinned" : rb == ClipTabImages ? "Images" : "All";
            _clipView.Refresh();
            UpdateClipEmptyState();
        }
    }

    private void UpdateClipEmptyState()
    {
        if (ClipEmptyState == null || ClipList == null) return;
        bool hasItems = ClipList.Items.Count > 0;
        ClipEmptyState.Visibility = hasItems ? Visibility.Collapsed : Visibility.Visible;

        var q = ClipSearchBox.Text?.Trim();
        ClipEmptyState.Text = !string.IsNullOrEmpty(q)
            ? "No matching clipboard items."
            : _clipTab switch
            {
                "Pinned" => "Pinned items will stay here.",
                "Images" => "Copied images will appear here.",
                _ => "Your copied text and images will appear here."
            };
    }

    private static T? FindAncestor<T>(DependencyObject? current) where T : DependencyObject
    {
        while (current != null)
        {
            if (current is T match) return match;
            current = VisualTreeHelper.GetParent(current);
        }
        return null;
    }
}
