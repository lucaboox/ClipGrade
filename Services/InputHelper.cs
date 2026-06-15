using System.Windows;
using Point = System.Windows.Point;
using static ClipboardApp.Services.NativeMethods;

namespace ClipboardApp.Services;

/// <summary>
/// Locates the text caret of whatever window is focused, and simulates a paste.
/// All coordinates returned are in physical screen pixels.
/// </summary>
public static class InputHelper
{
    /// <summary>
    /// Returns the screen-pixel rectangle of the focused text caret, or null
    /// if no caret can be found (e.g. focus is not in a text field).
    /// </summary>
    public static Rect? GetCaretScreenRect(IntPtr foregroundWindow)
    {
        if (foregroundWindow == IntPtr.Zero) return null;

        uint threadId = GetWindowThreadProcessId(foregroundWindow, out _);
        if (threadId == 0) return null;

        var gti = new GUITHREADINFO();
        gti.cbSize = System.Runtime.InteropServices.Marshal.SizeOf<GUITHREADINFO>();
        if (!GetGUIThreadInfo(threadId, ref gti)) return null;

        // A zero-size caret rect usually means "no real caret".
        if (gti.hwndCaret == IntPtr.Zero) return null;
        if (gti.rcCaret.Bottom - gti.rcCaret.Top <= 0 &&
            gti.rcCaret.Right - gti.rcCaret.Left <= 0)
            return null;

        var topLeft = new POINT { X = gti.rcCaret.Left, Y = gti.rcCaret.Top };
        var bottomRight = new POINT { X = gti.rcCaret.Right, Y = gti.rcCaret.Bottom };
        ClientToScreen(gti.hwndCaret, ref topLeft);
        ClientToScreen(gti.hwndCaret, ref bottomRight);

        return new Rect(
            topLeft.X, topLeft.Y,
            Math.Max(1, bottomRight.X - topLeft.X),
            Math.Max(1, bottomRight.Y - topLeft.Y));
    }

    /// <summary>Current mouse position in screen pixels.</summary>
    public static Point GetCursorScreenPoint()
    {
        GetCursorPos(out var p);
        return new Point(p.X, p.Y);
    }

    /// <summary>
    /// Brings the given window to the foreground and sends Ctrl+V so the freshly
    /// set clipboard content is pasted into it.
    /// </summary>
    public static void RestoreFocusAndPaste(IntPtr targetWindow)
    {
        if (targetWindow != IntPtr.Zero)
        {
            // Attaching input threads makes SetForegroundWindow reliable.
            uint targetThread = GetWindowThreadProcessId(targetWindow, out _);
            uint thisThread = GetCurrentThreadId();
            bool attached = false;
            if (targetThread != thisThread)
                attached = AttachThreadInput(thisThread, targetThread, true);

            SetForegroundWindow(targetWindow);

            if (attached)
                AttachThreadInput(thisThread, targetThread, false);
        }

        SendCtrlV();
    }

    private static void SendCtrlV()
    {
        keybd_event((byte)VK_CONTROL, 0, 0, UIntPtr.Zero);          // Ctrl down
        keybd_event((byte)VK_V, 0, 0, UIntPtr.Zero);               // V down
        keybd_event((byte)VK_V, 0, KEYEVENTF_KEYUP, UIntPtr.Zero); // V up
        keybd_event((byte)VK_CONTROL, 0, KEYEVENTF_KEYUP, UIntPtr.Zero); // Ctrl up
    }
}
