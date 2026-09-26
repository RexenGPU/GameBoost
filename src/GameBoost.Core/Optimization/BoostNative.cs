using System.Runtime.InteropServices;

namespace GameBoost.Core.Optimization;

internal static class BoostNative
{
    internal const uint ProcessQueryInformation = 0x0400;
    internal const uint ProcessQueryLimitedInformation = 0x1000;
    internal const uint ProcessSetInformation = 0x0200;
    internal const uint ProcessSetQuota = 0x0100;
    internal const uint HighPriorityClass = 0x00000080;
    internal const uint NormalPriorityClass = 0x00000020;
    internal const uint IdlePriorityClass = 0x00000040;
    internal const uint BelowNormalPriorityClass = 0x00004000;
    internal const uint AboveNormalPriorityClass = 0x00008000;
    internal const uint RealtimePriorityClass = 0x00000100;

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern IntPtr OpenProcess(uint desiredAccess, bool inheritHandle, int processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool CloseHandle(IntPtr hObject);

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern uint GetPriorityClass(IntPtr hProcess);

    [DllImport("kernel32.dll", SetLastError = true, EntryPoint = "SetPriorityClass")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetProcessPriorityClass(IntPtr hProcess, uint dwPriorityClass);

    [DllImport("psapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool EmptyWorkingSet(IntPtr hProcess);

    internal static IntPtr OpenForPriority(int processId)
    {
        var handle = OpenProcess(ProcessQueryInformation | ProcessSetInformation, false, processId);
        if (handle == IntPtr.Zero)
            handle = OpenProcess(ProcessQueryLimitedInformation | ProcessSetInformation, false, processId);
        return handle;
    }

    internal static IntPtr OpenForMemory(int processId)
    {
        var handle = OpenProcess(ProcessQueryInformation | ProcessSetQuota, false, processId);
        if (handle == IntPtr.Zero)
            handle = OpenProcess(ProcessQueryLimitedInformation | ProcessSetQuota, false, processId);
        return handle;
    }

    internal static string PriorityLabel(uint priorityClass)
    {
        if (priorityClass == 0) return "Inconnue";
        if ((priorityClass & HighPriorityClass) != 0) return "Haute";
        if ((priorityClass & RealtimePriorityClass) != 0) return "Temps réel";
        if ((priorityClass & AboveNormalPriorityClass) != 0) return "Supérieure à la normale";
        if ((priorityClass & NormalPriorityClass) != 0) return "Normale";
        if ((priorityClass & BelowNormalPriorityClass) != 0) return "Inférieure à la normale";
        if ((priorityClass & IdlePriorityClass) != 0) return "Faible";
        return "0x" + priorityClass.ToString("X");
    }
}
