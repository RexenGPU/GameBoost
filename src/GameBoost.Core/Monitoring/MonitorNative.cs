using System.Runtime.InteropServices;
using GameBoost.Core.Logging;

namespace GameBoost.Core.Monitoring;

internal static class MonitorNative
{
    private const uint HkeyLocalMachine = 0x80000002;
    private const uint RrfRtRegDword = 0x00000010;
    private const uint RrfRtRegQword = 0x00000040;
    private const uint ErrorSuccess = 0;
    private const uint ErrorMoreData = 234;
    private const uint RegDword = 4;
    private const uint RegQword = 11;

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatusEx
    {
        public uint DwLength;
        public uint DwMemoryLoad;
        public ulong UllTotalPhys;
        public ulong UllAvailPhys;
        public ulong UllTotalPageFile;
        public ulong UllAvailPageFile;
        public ulong UllTotalVirtual;
        public ulong UllAvailVirtual;
        public ulong UllAvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx lpBuffer);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int RegGetValueW(uint hkey, string subKey, string value, uint flags, out uint type, IntPtr data, ref uint dataSize);

    public static bool TryReadMemory(out long usedBytes, out long totalBytes)
    {
        usedBytes = 0;
        totalBytes = 0;
        try
        {
            var status = new MemoryStatusEx { DwLength = (uint)Marshal.SizeOf<MemoryStatusEx>() };
            if (!GlobalMemoryStatusEx(ref status)) return false;
            totalBytes = (long)status.UllTotalPhys;
            usedBytes = totalBytes - (long)status.UllAvailPhys;
            if (usedBytes < 0) usedBytes = 0;
            return totalBytes > 0;
        }
        catch (Exception ex)
        {
            Log.Warn("Monitor", "Lecture de la memoire impossible : " + ex.Message);
            return false;
        }
    }

    public static long ReadRegistryQword(string subKey, string valueName)
    {
        var flags = RrfRtRegDword | RrfRtRegQword;
        var size = 8u;
        IntPtr buffer = IntPtr.Zero;
        try
        {
            buffer = Marshal.AllocHGlobal(8);
            var status = (uint)RegGetValueW(HkeyLocalMachine, subKey, valueName, flags, out var type, buffer, ref size);
            if (status == ErrorMoreData)
            {
                Marshal.FreeHGlobal(buffer);
                buffer = IntPtr.Zero;
                buffer = Marshal.AllocHGlobal((int)size);
                status = (uint)RegGetValueW(HkeyLocalMachine, subKey, valueName, flags, out type, buffer, ref size);
            }
            if (status != ErrorSuccess) return 0;
            if (type == RegQword && size >= 8) return Marshal.ReadInt64(buffer);
            if (type == RegDword && size >= 4) return Marshal.ReadInt32(buffer);
            return 0;
        }
        catch (Exception ex)
        {
            Log.Warn("Monitor", "Lecture du registre impossible : " + ex.Message);
            return 0;
        }
        finally
        {
            if (buffer != IntPtr.Zero) Marshal.FreeHGlobal(buffer);
        }
    }
}
