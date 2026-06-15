using System.Diagnostics;
using System.Runtime.InteropServices;
using static ClipboardApp.Services.NativeMethods;

namespace ClipboardApp.Services;

/// <summary>
/// Installs system-wide low-level keyboard and mouse hooks while the popup is
/// open. Because the popup is a non-activating window it never receives normal
/// keyboard focus, so these hooks are how we drive it from the keyboard and how
/// we detect a click outside the popup.
/// </summary>
public sealed class HookManager : IDisposable
{
    private readonly HookProc _kbProc;     // kept as fields so the GC can't collect them
    private readonly HookProc _mouseProc;
    private IntPtr _kbHook;
    private IntPtr _mouseHook;

    /// <summary>Called on each key-down with the virtual-key code. Return true to swallow it.</summary>
    public Func<int, bool>? KeyDown;

    /// <summary>Called on any mouse button-down with the screen-pixel point.</summary>
    public Action<int, int>? MouseDown;

    public HookManager()
    {
        _kbProc = KbCallback;
        _mouseProc = MouseCallback;
    }

    public void Install(bool keyboard = true, bool mouse = true)
    {
        using var proc = Process.GetCurrentProcess();
        using var module = proc.MainModule!;
        var h = GetModuleHandle(module.ModuleName);
        if (keyboard && _kbHook == IntPtr.Zero)
            _kbHook = SetWindowsHookEx(WH_KEYBOARD_LL, _kbProc, h, 0);
        if (mouse && _mouseHook == IntPtr.Zero)
            _mouseHook = SetWindowsHookEx(WH_MOUSE_LL, _mouseProc, h, 0);
    }

    public void Uninstall()
    {
        if (_kbHook != IntPtr.Zero) { UnhookWindowsHookEx(_kbHook); _kbHook = IntPtr.Zero; }
        if (_mouseHook != IntPtr.Zero) { UnhookWindowsHookEx(_mouseHook); _mouseHook = IntPtr.Zero; }
    }

    private IntPtr KbCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && (wParam == WM_KEYDOWN || wParam == WM_SYSKEYDOWN))
        {
            var info = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
            if (KeyDown?.Invoke((int)info.vkCode) == true)
                return 1; // swallow — don't let it reach the focused app
        }
        return CallNextHookEx(_kbHook, nCode, wParam, lParam);
    }

    private IntPtr MouseCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && (wParam == WM_LBUTTONDOWN || wParam == WM_RBUTTONDOWN || wParam == WM_MBUTTONDOWN))
        {
            var info = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
            MouseDown?.Invoke(info.pt.X, info.pt.Y);
        }
        return CallNextHookEx(_mouseHook, nCode, wParam, lParam);
    }

    public void Dispose() => Uninstall();
}
