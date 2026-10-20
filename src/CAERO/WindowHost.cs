using System.Numerics;
using System.Runtime.InteropServices;
using Raylib_cs;

namespace Caero;

// Win32 monitor selection uses desktop coordinates (including negative origins).
internal static class WindowHost
{
    [StructLayout(LayoutKind.Sequential)] private struct Point { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct MonitorInfo
    { public int Size; public Rect Monitor, Work; public uint Flags; }
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out Point point);
    [DllImport("user32.dll")] private static extern bool SetProcessDpiAwarenessContext(nint context);
    [DllImport("user32.dll")] private static extern nint MonitorFromPoint(Point point, uint flags);
    [DllImport("user32.dll")] private static extern nint MonitorFromWindow(nint window, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetMonitorInfoW")]
    private static extern bool GetMonitorInfo(nint monitor, ref MonitorInfo info);
    private static Vector2 savedPosition;
    private static int savedWidth, savedHeight;
    public static bool Borderless { get; private set; }
    // Freeze the window's drawing size while its image is stretched over the monitor.
    public static (int Width, int Height) RenderSize => Borderless
        ? (savedWidth, savedHeight) : (Raylib.GetScreenWidth(), Raylib.GetScreenHeight());

    private static MonitorInfo Info(nint monitor)
    {
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (!GetMonitorInfo(monitor, ref info)) throw new InvalidOperationException("Cannot read monitor bounds.");
        return info;
    }

    public static void Open()
    {
        // Establish physical desktop coordinates before reading cursor/monitor
        // bounds. GLFW would otherwise set DPI awareness only during InitWindow.
        SetProcessDpiAwarenessContext(new nint(-4)); // PER_MONITOR_AWARE_V2
        GetCursorPos(out var cursor);
        var monitor = Info(MonitorFromPoint(cursor, 2));
        int width = Math.Min(1440, monitor.Work.Right - monitor.Work.Left - 64);
        int height = Math.Min(900, monitor.Work.Bottom - monitor.Work.Top - 64);
        Raylib.SetConfigFlags(ConfigFlags.ResizableWindow | ConfigFlags.Msaa4xHint | ConfigFlags.HiddenWindow);
        Raylib.InitWindow(width, height, "CAERO5 | The wandering citadels");
        Raylib.SetWindowPosition(monitor.Monitor.Left + (monitor.Monitor.Right - monitor.Monitor.Left - width) / 2,
            monitor.Monitor.Top + (monitor.Monitor.Bottom - monitor.Monitor.Top - height) / 2);
        Raylib.ClearWindowState(ConfigFlags.HiddenWindow);
        Raylib.SetWindowMinSize(640, 360);
        Raylib.SetExitKey(KeyboardKey.Null);
        Raylib.SetTargetFPS(60);
    }

    public static unsafe void Toggle()
    {
        if (!Borderless)
        {
            var info = Info(MonitorFromWindow((nint)Raylib.GetWindowHandle(), 2));
            savedPosition = Raylib.GetWindowPosition();
            savedWidth = Raylib.GetScreenWidth(); savedHeight = Raylib.GetScreenHeight();
            if (Raylib.IsWindowMaximized()) Raylib.RestoreWindow();
            Raylib.SetWindowState(ConfigFlags.UndecoratedWindow);
            Raylib.SetWindowSize(info.Monitor.Right - info.Monitor.Left, info.Monitor.Bottom - info.Monitor.Top);
            Raylib.SetWindowPosition(info.Monitor.Left, info.Monitor.Top);
        }
        else
        {
            Raylib.ClearWindowState(ConfigFlags.UndecoratedWindow);
            Raylib.SetWindowSize(savedWidth, savedHeight);
            Raylib.SetWindowPosition((int)savedPosition.X, (int)savedPosition.Y);
        }
        Borderless = !Borderless;
    }
}
