using System.Runtime.InteropServices;

namespace GameBoost.Core.Processes;

internal static class ProcessNative
{
    public const uint ProcessQueryInformation = 0x0400;
    public const uint ProcessQueryLimitedInformation = 0x1000;
    public const uint TokenQuery = 0x0008;
    public const int TokenElevationClass = 20;
    public const int MemorySegmentGroupLocal = 0;

    [StructLayout(LayoutKind.Sequential)]
    public struct IoCounters
    {
        public ulong ReadOperationCount;
        public ulong WriteOperationCount;
        public ulong OtherOperationCount;
        public ulong ReadTransferCount;
        public ulong WriteTransferCount;
        public ulong OtherTransferCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct TokenElevation
    {
        public int TokenIsElevated;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct VideoMemoryInformation
    {
        public IntPtr VidMemBase;
        public UIntPtr VidMemSize;
        public UIntPtr TotalCommitSize;
        public UIntPtr CurrentCommitSize;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern IntPtr OpenProcess(uint desiredAccess, bool inheritHandle, int processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool GetProcessIoCounters(IntPtr processHandle, out IoCounters counters);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool CloseHandle(IntPtr hObject);

    [DllImport("advapi32.dll", SetLastError = true)]
    public static extern bool OpenProcessToken(IntPtr processHandle, uint desiredAccess, out IntPtr tokenHandle);

    [DllImport("advapi32.dll", SetLastError = true)]
    public static extern bool GetTokenInformation(IntPtr tokenHandle, int tokenInformationClass, out TokenElevation tokenInformation, int tokenInformationLength, out int returnLength);

    [DllImport("ntdll.dll")]
    public static extern int NtQueryVideoMemoryInfo(IntPtr processHandle, uint nodeIndex, int memorySegmentGroup, out VideoMemoryInformation memoryInformation, int memoryInformationLength, out int returnLength);
}
