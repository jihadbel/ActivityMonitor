using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace ActivityMonitor.Viewer.Services;

public static class WindowPlacementService
{
    public static Rect FitBounds(Rect workArea, Rect requestedBounds, bool center)
    {
        var width = Math.Min(requestedBounds.Width, workArea.Width);
        var height = Math.Min(requestedBounds.Height, workArea.Height);
        var left = center
            ? workArea.Left + (workArea.Width - width) / 2
            : Math.Clamp(requestedBounds.Left, workArea.Left, workArea.Right - width);
        var top = center
            ? workArea.Top + (workArea.Height - height) / 2
            : Math.Clamp(requestedBounds.Top, workArea.Top, workArea.Bottom - height);
        return new Rect(left, top, width, height);
    }

    public static void FitWindow(Window window, bool center)
    {
        var handle = new WindowInteropHelper(window).Handle;
        var monitor = MonitorFromWindow(handle, 2 /* MONITOR_DEFAULTTONEAREST */);
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (handle == IntPtr.Zero || !GetMonitorInfo(monitor, ref info))
        {
            return;
        }

        // Win32 coordinates are physical pixels; WPF sizes and minimums are DIPs.
        var dpi = GetDpiForWindow(handle);
        var scale = (dpi > 0 ? dpi : 96) / 96.0;
        var workArea = info.WorkArea.ToRect();
        window.MinWidth = Math.Min(680, workArea.Width / scale);
        window.MinHeight = Math.Min(520, workArea.Height / scale);
        if (window.WindowState != WindowState.Normal)
        {
            return;
        }

        Rect requested;
        if (center)
        {
            requested = new Rect(0, 0,
                Math.Max(window.MinWidth * scale, Math.Min(1420 * scale, workArea.Width * 0.94)),
                Math.Max(window.MinHeight * scale, Math.Min(860 * scale, workArea.Height * 0.94)));
        }
        else if (GetWindowRect(handle, out var current))
        {
            requested = current.ToRect();
        }
        else
        {
            return;
        }

        var bounds = FitBounds(workArea, requested, center);
        SetWindowPos(handle, IntPtr.Zero, (int)Math.Round(bounds.Left), (int)Math.Round(bounds.Top),
            (int)Math.Round(bounds.Width), (int)Math.Round(bounds.Height),
            0x0004 | 0x0010 /* SWP_NOZORDER | SWP_NOACTIVATE */);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left, Top, Right, Bottom;
        public readonly Rect ToRect() => new(Left, Top, Right - Left, Bottom - Top);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public int Size;
        public NativeRect MonitorArea;
        public NativeRect WorkArea;
        public uint Flags;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr window, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr window, out NativeRect rect);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(IntPtr window, IntPtr insertAfter,
        int x, int y, int width, int height, uint flags);
}
