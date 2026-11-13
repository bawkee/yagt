using System.Runtime.InteropServices;

using Avalonia;
using Avalonia.Controls;

internal static class DisplayPlacement
{
    public static PixelRect PositionOnDisplayOne(Window window)
    {
        foreach (var screen in window.Screens.All)
            Console.WriteLine($"SCREEN: {screen.DisplayName}; primary={screen.IsPrimary}; bounds={screen.Bounds}; scaling={screen.Scaling}");

        PixelRect? displayOne = null;
        if (OperatingSystem.IsWindows())
        {
            EnumDisplayMonitors(nint.Zero, nint.Zero, (nint monitor, nint deviceContext, ref NativeRect bounds, nint data) =>
            {
                var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
                if (GetMonitorInfo(monitor, ref info))
                {
                    Console.WriteLine($"PHYSICAL SCREEN: {info.DeviceName}; bounds={info.Monitor.Left},{info.Monitor.Top},{info.Monitor.Right - info.Monitor.Left},{info.Monitor.Bottom - info.Monitor.Top}");
                    if (string.Equals(info.DeviceName, @"\\.\DISPLAY1", StringComparison.OrdinalIgnoreCase))
                    {
                        displayOne = new PixelRect(info.Monitor.Left, info.Monitor.Top,
                            info.Monitor.Right - info.Monitor.Left, info.Monitor.Bottom - info.Monitor.Top);
                    }
                }
                return true;
            }, nint.Zero);
        }

        var selected = window.Screens.All.FirstOrDefault(s => displayOne.HasValue && s.Bounds.Equals(displayOne.Value));
        if (selected == null)
            throw new InvalidOperationException("Physical display 1 could not be identified; leaving other displays undisturbed");
        window.Position = new PixelPoint(selected.WorkingArea.X + 24, selected.WorkingArea.Y + 24);
        return selected.Bounds;
    }

    [DllImport("user32.dll")]
    private static extern bool EnumDisplayMonitors(nint deviceContext, nint clip, MonitorCallback callback, nint data);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetMonitorInfoW")]
    private static extern bool GetMonitorInfo(nint monitor, ref MonitorInfo info);

    private delegate bool MonitorCallback(nint monitor, nint deviceContext, ref NativeRect bounds, nint data);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MonitorInfo
    {
        public int Size;
        public NativeRect Monitor;
        public NativeRect Work;
        public uint Flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string DeviceName;
    }
}
