using System.Runtime.InteropServices;

namespace GameBoost.Core.Hardware;

internal static class NativeMethods
{
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

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx buffer);

    [DllImport("kernel32.dll")]
    internal static extern ulong GetTickCount64();

    internal static bool TryGetMemoryStatus(out ulong totalPhys, out ulong availablePhys)
    {
        totalPhys = 0;
        availablePhys = 0;
        var status = new MemoryStatusEx { Length = (uint)Marshal.SizeOf<MemoryStatusEx>() };
        if (!GlobalMemoryStatusEx(ref status)) return false;
        totalPhys = status.TotalPhys;
        availablePhys = status.AvailPhys;
        return true;
    }
}
