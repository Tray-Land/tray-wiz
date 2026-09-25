using System.Drawing;
using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Windows.Graphics;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Gdi;
using Windows.Win32.UI.Shell;
using WinUIEx;

namespace TrayWiz.Services;

/// <summary>
/// Places the popup against the tray icon the way native tray flyouts sit:
/// centered on the icon and flush with the taskbar edge. Adapted from Traydio.
/// </summary>
internal static class WindowPlacementService
{
    private const int WindowMargin = 12;
    private const uint ABM_GETTASKBARPOS = 0x00000005;
    private const uint MDT_EFFECTIVE_DPI = 0;
    private const uint ABE_LEFT = 0;
    private const uint ABE_TOP = 1;
    private const uint ABE_RIGHT = 2;
    private const uint ABE_BOTTOM = 3;

    private static nint _trayIconWindowHandle;
    private static uint _trayIconId;

    public static void SetTrayIconSource(TrayIcon trayIcon)
    {
        // WinUIEx doesn't expose the tray icon's owner HWND, which Shell_NotifyIconGetRect needs.
        try
        {
            FieldInfo? field = typeof(TrayIcon).GetField("_windowHandle", BindingFlags.Instance | BindingFlags.NonPublic);
            if (field?.GetValue(trayIcon) is nint handle && handle != 0)
            {
                _trayIconWindowHandle = handle;
                _trayIconId = trayIcon.TrayIconId;
            }
        }
        catch
        {
            // WinUIEx internals may change; placement falls back to the taskbar corner.
        }
    }

    /// <summary>Moves and sizes the window (logical width/height) next to the tray icon.</summary>
    public static void PositionNearTrayIcon(Window window, int width, int height)
    {
        PointInt32 anchor = GetAnchorPoint();
        DisplayArea displayArea = DisplayArea.GetFromPoint(anchor, DisplayAreaFallback.Nearest) ?? DisplayArea.Primary;
        RectInt32 workArea = displayArea.WorkArea;

        // Positioning APIs use physical pixels; the DPI must come from the anchor's monitor,
        // since a hidden window keeps the DPI of wherever it last was.
        uint dpi = GetDpiForAnchor(anchor, window);
        int physWidth = (int)Math.Ceiling(width * dpi / 96.0);
        int physHeight = (int)Math.Ceiling(height * dpi / 96.0);

        bool haveTaskbar = TryGetTaskbarRect(out RECT taskbar, out uint edge);
        int x;
        int y;

        if (TryGetTrayIconRect(out RECT icon))
        {
            x = ((icon.left + icon.right) / 2) - (physWidth / 2);
            y = haveTaskbar && edge == ABE_BOTTOM ? taskbar.top - physHeight
                : haveTaskbar && edge == ABE_TOP ? taskbar.bottom
                : icon.top >= workArea.Y + (workArea.Height / 2) ? icon.top - physHeight - WindowMargin
                : icon.bottom + WindowMargin;

            if (haveTaskbar && edge == ABE_LEFT)
                x = taskbar.right + WindowMargin;
            else if (haveTaskbar && edge == ABE_RIGHT)
                x = taskbar.left - physWidth - WindowMargin;
        }
        else
        {
            bool rtl = CultureInfo.CurrentUICulture.TextInfo.IsRightToLeft;
            x = rtl ? workArea.X + WindowMargin : workArea.X + workArea.Width - physWidth - WindowMargin;
            y = workArea.Y + workArea.Height - physHeight - WindowMargin;
        }

        x = Math.Clamp(x, workArea.X, Math.Max(workArea.X, workArea.X + workArea.Width - physWidth));
        y = Math.Clamp(y, workArea.Y, Math.Max(workArea.Y, workArea.Y + workArea.Height - physHeight));

        window.AppWindow.MoveAndResize(new RectInt32(x, y, physWidth, physHeight));
    }

    private static PointInt32 GetAnchorPoint()
    {
        if (TryGetTrayIconRect(out RECT icon))
            return new PointInt32((icon.left + icon.right) / 2, (icon.top + icon.bottom) / 2);

        if (TryGetTaskbarRect(out RECT taskbar, out _))
            return new PointInt32(taskbar.right - WindowMargin, taskbar.bottom - WindowMargin);

        RectInt32 work = DisplayArea.Primary.WorkArea;
        return new PointInt32(work.X + work.Width - WindowMargin, work.Y + work.Height - WindowMargin);
    }

    private static uint GetDpiForAnchor(PointInt32 anchor, Window window)
    {
        HMONITOR monitor = PInvoke.MonitorFromPoint(new Point(anchor.X, anchor.Y), MONITOR_FROM_FLAGS.MONITOR_DEFAULTTONEAREST);
        if (monitor != 0
            && PInvoke.GetDpiForMonitor(monitor, (Windows.Win32.UI.HiDpi.MONITOR_DPI_TYPE)MDT_EFFECTIVE_DPI, out uint dpiX, out _) == 0
            && dpiX != 0)
        {
            return dpiX;
        }

        uint dpi = PInvoke.GetDpiForWindow((HWND)window.GetWindowHandle());
        return dpi == 0 ? 96u : dpi;
    }

    private static bool TryGetTrayIconRect(out RECT iconRect)
    {
        iconRect = default;
        if (_trayIconWindowHandle == 0)
            return false;

        NOTIFYICONIDENTIFIER identifier = new()
        {
            cbSize = (uint)Marshal.SizeOf<NOTIFYICONIDENTIFIER>(),
            hWnd = (HWND)_trayIconWindowHandle,
            uID = _trayIconId,
        };

        return PInvoke.Shell_NotifyIconGetRect(in identifier, out iconRect) == 0;
    }

    private static bool TryGetTaskbarRect(out RECT rc, out uint edge)
    {
        APPBARDATA data = new() { cbSize = (uint)Marshal.SizeOf<APPBARDATA>() };
        if (PInvoke.SHAppBarMessage(ABM_GETTASKBARPOS, ref data) == 0)
        {
            rc = default;
            edge = 0;
            return false;
        }

        rc = data.rc;
        edge = data.uEdge;
        return true;
    }
}
