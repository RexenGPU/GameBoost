using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using GameBoost.Core.Logging;
using Microsoft.Win32;

namespace GameBoost.Core.Analysis;

internal static class AnalysisNative
{
    internal const int EnumCurrentSettings = -1;
    internal const int EnumRegistrySettings = -2;
    internal const string HighPerformanceScheme = "8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c";
    internal const string BalancedScheme = "381b4222-f694-41f0-9685-ff5bb260df2e";
    internal const string PowerSaverScheme = "a1841308-3541-4fab-bc81-f71556f20b4a";
    internal const string GameBarKeyPath = @"Software\Microsoft\GameBar";
    internal const string GameModeValueName = "AutoGameModeEnabled";
    internal const string StartupRunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";

    [StructLayout(LayoutKind.Sequential)]
    internal struct MemoryStatusEx
    {
        public uint Length;
        public uint MemoryLoad;
        public ulong TotalPhys;
        public ulong AvailPhys;
        public ulong TotalPageFile;
        public ulong AvailPageFile;
        public ulong TotalVirtual;
        public ulong AvailVirtual;
        public ulong AvailExtendedVirtual;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct DevMode
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

    internal sealed record MaxDisplayMode(int Width, int Height, int Frequency);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx buffer);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumDisplaySettings(string deviceName, int modeNum, ref DevMode devMode);

    internal static bool TryGetMemory(out ulong totalPhys, out ulong availablePhys)
    {
        totalPhys = 0;
        availablePhys = 0;
        var status = new MemoryStatusEx { Length = (uint)Marshal.SizeOf<MemoryStatusEx>() };
        if (!GlobalMemoryStatusEx(ref status)) return false;
        totalPhys = status.TotalPhys;
        availablePhys = status.AvailPhys;
        return true;
    }

    internal static bool TryGetMode(string deviceName, int mode, out int width, out int height, out int frequency)
    {
        width = 0;
        height = 0;
        frequency = 0;
        if (string.IsNullOrWhiteSpace(deviceName)) return false;
        var modeBuffer = new DevMode
        {
            DeviceName = string.Empty,
            FormName = string.Empty,
            Size = (short)Marshal.SizeOf<DevMode>()
        };
        if (!EnumDisplaySettings(deviceName, mode, ref modeBuffer)) return false;
        width = modeBuffer.PelsWidth;
        height = modeBuffer.PelsHeight;
        frequency = modeBuffer.DisplayFrequency;
        return width > 0 && height > 0;
    }

    internal static MaxDisplayMode? DetectMaxMode(string deviceName, int currentWidth, int currentHeight)
    {
        var bestFrequency = 0;
        var bestWidth = 0;
        var bestHeight = 0;
        long bestArea = -1;

        if (TryGetMode(deviceName, EnumRegistrySettings, out var regWidth, out var regHeight, out var regFrequency))
        {
            if (regWidth == currentWidth && regHeight == currentHeight && regFrequency > bestFrequency)
                bestFrequency = regFrequency;
            if ((long)regWidth * regHeight > bestArea)
            {
                bestArea = (long)regWidth * regHeight;
                bestWidth = regWidth;
                bestHeight = regHeight;
            }
        }

        for (var index = 0; index < 512; index++)
        {
            if (!TryGetMode(deviceName, index, out var width, out var height, out var frequency)) break;
            if (width == currentWidth && height == currentHeight && frequency > bestFrequency)
                bestFrequency = frequency;
            if ((long)width * height > bestArea)
            {
                bestArea = (long)width * height;
                bestWidth = width;
                bestHeight = height;
            }
        }

        if (bestWidth <= 0 || bestHeight <= 0) return null;
        return new MaxDisplayMode(bestWidth, bestHeight, bestFrequency);
    }

    internal static (bool Success, bool Present, int Value) ReadGameMode()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(GameBarKeyPath);
            if (key is null) return (true, false, 0);
            var raw = key.GetValue(GameModeValueName);
            if (raw is null) return (true, false, 0);
            return raw switch
            {
                int number => (true, true, number),
                long number => (true, true, (int)number),
                bool flag => (true, true, flag ? 1 : 0),
                string text when int.TryParse(text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) => (true, true, parsed),
                string text when bool.TryParse(text.Trim(), out var flag) => (true, true, flag ? 1 : 0),
                _ => (true, true, 1)
            };
        }
        catch (Exception ex)
        {
            Log.Warn("Analysis", "Lecture du Mode Jeu Windows impossible : " + ex.Message);
            return (false, false, 0);
        }
    }

    internal static void WriteGameMode(int value)
    {
        using var key = Registry.CurrentUser.CreateSubKey(GameBarKeyPath, true);
        if (key is null) throw new InvalidOperationException("La clé de registre GameBoost GameBar est inaccessible.");
        key.SetValue(GameModeValueName, value, RegistryValueKind.DWord);
    }

    internal static void DeleteGameMode()
    {
        using var key = Registry.CurrentUser.OpenSubKey(GameBarKeyPath, true);
        if (key is null) return;
        if (key.GetValue(GameModeValueName) is not null) key.DeleteValue(GameModeValueName, false);
    }

    internal static List<string> ReadStartupEntries()
    {
        var entries = new List<string>();
        using var key = Registry.CurrentUser.OpenSubKey(StartupRunKeyPath);
        if (key is null) return entries;
        foreach (var name in key.GetValueNames())
        {
            if (string.IsNullOrWhiteSpace(name)) continue;
            entries.Add(name);
        }
        entries.Sort(StringComparer.CurrentCultureIgnoreCase);
        return entries;
    }

    internal static (int ExitCode, string Output) RunCommand(string fileName, string arguments, int timeoutMs = 20000)
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            using var process = Process.Start(startInfo);
            if (process is null) return (-1, string.Empty);
            var output = process.StandardOutput.ReadToEnd();
            var error = process.StandardError.ReadToEnd();
            if (!process.WaitForExit(timeoutMs))
            {
                try
                {
                    process.Kill(true);
                }
                catch (Exception ex)
                {
                    Log.Warn("Analysis", "Arrêt de " + fileName + " impossible : " + ex.Message);
                }
                return (-1, output);
            }
            return (process.ExitCode, string.IsNullOrWhiteSpace(error) ? output : output + Environment.NewLine + error);
        }
        catch (Exception ex)
        {
            Log.Warn("Analysis", "Exécution de " + fileName + " impossible : " + ex.Message);
            return (-1, ex.Message);
        }
    }

    internal static string ExtractGuid(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;
        var match = Regex.Match(text, "[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}");
        return match.Success ? match.Value.ToLowerInvariant() : string.Empty;
    }

    internal static string DescribeScheme(string guid)
    {
        if (guid.Equals(HighPerformanceScheme, StringComparison.OrdinalIgnoreCase)) return "Hautes performances";
        if (guid.Equals(BalancedScheme, StringComparison.OrdinalIgnoreCase)) return "Équilibré";
        if (guid.Equals(PowerSaverScheme, StringComparison.OrdinalIgnoreCase)) return "Économie d'énergie";
        return string.Empty;
    }

    internal static string ExtractSchemeLabel(string output)
    {
        if (string.IsNullOrWhiteSpace(output)) return string.Empty;
        var line = output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? string.Empty;
        var open = line.IndexOf('(');
        var close = open >= 0 ? line.IndexOf(')', open + 1) : -1;
        if (close > open)
        {
            var name = line[(open + 1)..close].Trim();
            if (name.Length > 0 && ExtractGuid(name).Length == 0) return name;
        }
        var separator = line.IndexOf(':');
        if (separator < 0) separator = line.IndexOf('>');
        if (separator < 0) return line.Trim();
        var label = line[(separator + 1)..].Trim();
        var parenthesis = label.IndexOf('(');
        if (parenthesis > 0) label = label[..parenthesis].Trim();
        label = label.Trim().Trim(':').Trim();
        return ExtractGuid(label).Equals(label.Trim(), StringComparison.OrdinalIgnoreCase) ? string.Empty : label;
    }

    internal static string FormatBytes(long bytes)
    {
        if (bytes <= 0) return "0 o";
        const double kilo = 1024d;
        const double mega = kilo * 1024d;
        const double giga = mega * 1024d;
        if (bytes >= giga) return (bytes / giga).ToString("0.0", CultureInfo.CurrentCulture) + " Go";
        if (bytes >= mega) return (bytes / mega).ToString("0.0", CultureInfo.CurrentCulture) + " Mo";
        if (bytes >= kilo) return (bytes / kilo).ToString("0", CultureInfo.CurrentCulture) + " Ko";
        return bytes.ToString("0", CultureInfo.CurrentCulture) + " o";
    }

    internal static string FormatPercent(double value) =>
        value.ToString("0.#", CultureInfo.CurrentCulture) + " %";
}
