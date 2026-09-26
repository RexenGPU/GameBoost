using System.Runtime.InteropServices;
using GameBoost.Core.Logging;
using GameBoost.Core.Models;

namespace GameBoost.Core.Hardware;

internal static class DisplayNative
{
    private const int EnumCurrentSettings = -1;
    private const int MaxDevices = 32;
    private const uint AttachedToDesktop = 0x1;
    private const int MonitorInfoPrimary = 0x1;

    private delegate bool MonitorEnumProc(IntPtr monitor, IntPtr hdc, ref Rect rect, IntPtr data);

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MonitorInfoEx
    {
        public int Size;
        public Rect Monitor;
        public Rect Work;
        public uint Flags;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string Device;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DisplayDevice
    {
        public int Size;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string DeviceName;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string DeviceString;

        public uint StateFlags;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string DeviceId;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string DeviceKey;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DevMode
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string DeviceName;

        public short SpecVersion;
        public short DriverVersion;
        public short Size;
        public short DriverExtra;
        public int Fields;
        public int PositionX;
        public int PositionY;
        public int DisplayOrientation;
        public int DisplayFixedOutput;
        public short Color;
        public short Duplex;
        public short YResolution;
        public short TtOption;
        public short Collate;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string FormName;

        public short LogPixels;
        public int BitsPerPel;
        public int PelsWidth;
        public int PelsHeight;
        public int DisplayFlags;
        public int DisplayFrequency;
        public int IcmMethod;
        public int IcmIntent;
        public int MediaType;
        public int DitherType;
        public int Reserved1;
        public int Reserved2;
        public int PanningWidth;
        public int PanningHeight;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool EnumDisplayDevices(string? device, uint deviceNum, ref DisplayDevice displayDevice, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool EnumDisplaySettings(string deviceName, int modeNum, ref DevMode devMode);

    [DllImport("user32.dll")]
    private static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr clipRect, MonitorEnumProc callback, IntPtr data);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfoEx info);

    internal static List<DisplayInfo> Enumerate()
    {
        var displays = new List<DisplayInfo>();
        var adapters = LoadAdapters();
        var raw = new List<(DisplayInfo Info, Rect Monitor)>();
        foreach (var (handle, deviceName) in LoadMonitors())
        {
            var display = new DisplayInfo { DeviceName = deviceName };
            var mode = new DevMode
            {
                DeviceName = string.Empty,
                FormName = string.Empty,
                Size = (short)Marshal.SizeOf<DevMode>()
            };
            if (EnumDisplaySettings(deviceName, EnumCurrentSettings, ref mode))
            {
                display.Width = mode.PelsWidth;
                display.Height = mode.PelsHeight;
                display.RefreshRate = mode.DisplayFrequency;
                display.BitsPerPixel = mode.BitsPerPel;
            }
            else
            {
                Log.Warn("Display", "Mode vidéo courant inaccessible pour " + deviceName + " (code " + Marshal.GetLastWin32Error() + ")");
            }

            var info = new MonitorInfoEx
            {
                Size = Marshal.SizeOf<MonitorInfoEx>(),
                Device = string.Empty
            };
            if (GetMonitorInfo(handle, ref info))
            {
                display.Primary = (info.Flags & MonitorInfoPrimary) != 0;
                if (display.Width <= 0)
                {
                    display.Width = info.Monitor.Right - info.Monitor.Left;
                    display.Height = info.Monitor.Bottom - info.Monitor.Top;
                }
                raw.Add((display, info.Monitor));
            }
            else
            {
                raw.Add((display, new Rect()));
                Log.Warn("Display", "GetMonitorInfo impossible pour " + deviceName + " (code " + Marshal.GetLastWin32Error() + ")");
            }

            if (adapters.TryGetValue(deviceName, out var adapter) && !string.IsNullOrWhiteSpace(adapter.DeviceString))
                display.GpuName = adapter.DeviceString.Trim();
            display.Description = MonitorDescription(deviceName);
            displays.Add(display);
        }

        if (!displays.Any(d => d.Primary))
        {
            var origin = raw.FirstOrDefault(r => r.Monitor.Left <= 0 && r.Monitor.Top <= 0 && r.Monitor.Right > 0 && r.Monitor.Bottom > 0);
            if (origin.Info is not null) origin.Info.Primary = true;
        }
        return displays;
    }

    private static string MonitorDescription(string deviceName)
    {
        try
        {
            var device = NewDisplayDevice();
            if (EnumDisplayDevices(deviceName, 0, ref device, 0) && !string.IsNullOrWhiteSpace(device.DeviceString))
                return device.DeviceString.Trim();
        }
        catch (Exception ex)
        {
            Log.Warn("Display", "Description du moniteur impossible : " + ex.Message);
        }
        return string.Empty;
    }

    private static List<(IntPtr Handle, string Device)> LoadMonitors()
    {
        var monitors = new List<(IntPtr, string)>();
        try
        {
            MonitorEnumProc callback = (IntPtr monitor, IntPtr hdc, ref Rect rect, IntPtr data) =>
            {
                var info = new MonitorInfoEx { Size = Marshal.SizeOf<MonitorInfoEx>(), Device = string.Empty };
                if (GetMonitorInfo(monitor, ref info) && !string.IsNullOrWhiteSpace(info.Device))
                    monitors.Add((monitor, info.Device));
                return true;
            };
            if (!EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, callback, IntPtr.Zero))
                Log.Warn("Display", "EnumDisplayMonitors a échoué (code " + Marshal.GetLastWin32Error() + ")");
            GC.KeepAlive(callback);
        }
        catch (Exception ex)
        {
            Log.Error("Display", "Énumération des moniteurs impossible", ex);
        }
        return monitors;
    }

    private static Dictionary<string, DisplayDevice> LoadAdapters()
    {
        var adapters = new Dictionary<string, DisplayDevice>(StringComparer.OrdinalIgnoreCase);
        try
        {
            for (uint index = 0; index < MaxDevices; index++)
            {
                var device = NewDisplayDevice();
                if (!EnumDisplayDevices(null, index, ref device, 0)) break;
                if ((device.StateFlags & AttachedToDesktop) == 0) continue;
                if (string.IsNullOrWhiteSpace(device.DeviceName) || adapters.ContainsKey(device.DeviceName)) continue;
                adapters[device.DeviceName] = device;
            }
        }
        catch (Exception ex)
        {
            Log.Error("Display", "Énumération des adaptateurs vidéo impossible", ex);
        }
        return adapters;
    }

    private static DisplayDevice NewDisplayDevice() => new()
    {
        Size = Marshal.SizeOf<DisplayDevice>(),
        DeviceName = string.Empty,
        DeviceString = string.Empty,
        DeviceId = string.Empty,
        DeviceKey = string.Empty
    };
}
