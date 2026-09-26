namespace GameBoost.Core.Models;

public enum HealthLevel
{
    Good,
    Warning,
    Critical,
    Unknown
}

public enum GpuVendor
{
    Nvidia,
    Amd,
    Intel,
    Other,
    Unknown
}

public enum DiskMediaType
{
    Ssd,
    Hdd,
    Unknown
}

public sealed class HardwareReport
{
    public DateTime CollectedAt { get; set; } = DateTime.Now;
    public CpuInfo Cpu { get; set; } = new();
    public GpuInfo Gpu { get; set; } = new();
    public RamInfo Ram { get; set; } = new();
    public MotherboardInfo Motherboard { get; set; } = new();
    public OsInfo Os { get; set; } = new();
    public string DirectXVersion { get; set; } = "Inconnue";
    public List<DisplayInfo> Displays { get; set; } = new();
    public List<StorageInfo> Storage { get; set; } = new();
    public List<PeripheralInfo> Peripherals { get; set; } = new();
}

public sealed class CpuInfo
{
    public string Name { get; set; } = "Inconnu";
    public string Manufacturer { get; set; } = "Inconnu";
    public int PhysicalCores { get; set; }
    public int LogicalCores { get; set; }
    public double? BaseClockMHz { get; set; }
    public double? MaxClockMHz { get; set; }
    public string Architecture { get; set; } = "Inconnue";
    public string Socket { get; set; } = "Inconnu";
    public string Description { get; set; } = string.Empty;
}

public sealed class GpuInfo
{
    public string Name { get; set; } = "Inconnue";
    public GpuVendor Vendor { get; set; } = GpuVendor.Unknown;
    public string DriverVersion { get; set; } = "Inconnue";
    public string DriverDate { get; set; } = "Inconnue";
    public long DedicatedVramBytes { get; set; }
    public string PnpDeviceId { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public bool IsPrimary { get; set; }
    public List<GpuInfo> AllAdapters { get; set; } = new();
    public GpuCapabilities Capabilities { get; set; } = new();
}

public sealed class GpuCapabilities
{
    public bool Dlss { get; set; }
    public bool FrameGeneration { get; set; }
    public bool RayTracing { get; set; }
    public bool Reflex { get; set; }
    public bool Fsr { get; set; }
    public bool Xess { get; set; }
    public string Notes { get; set; } = string.Empty;
}

public sealed class RamInfo
{
    public long TotalBytes { get; set; }
    public long AvailableBytes { get; set; }
    public double SpeedMHz { get; set; }
    public string FormFactor { get; set; } = "Inconnu";
    public List<RamModule> Modules { get; set; } = new();
}

public sealed class RamModule
{
    public long CapacityBytes { get; set; }
    public double SpeedMHz { get; set; }
    public string Manufacturer { get; set; } = "Inconnu";
    public string PartNumber { get; set; } = "Inconnu";
    public string Slot { get; set; } = "Inconnu";
    public string FormFactor { get; set; } = "Inconnu";
}

public sealed class MotherboardInfo
{
    public string Manufacturer { get; set; } = "Inconnu";
    public string Product { get; set; } = "Inconnue";
    public string Version { get; set; } = "Inconnue";
    public string SerialNumber { get; set; } = string.Empty;
    public string BiosVersion { get; set; } = "Inconnue";
    public string BiosDate { get; set; } = "Inconnue";
}

public sealed class OsInfo
{
    public string Caption { get; set; } = "Inconnu";
    public string Version { get; set; } = "Inconnue";
    public string Build { get; set; } = "Inconnue";
    public string Edition { get; set; } = "Inconnue";
    public string Architecture { get; set; } = "Inconnue";
    public DateTime BootTime { get; set; }
    public TimeSpan Uptime { get; set; }
    public bool GameModeEnabled { get; set; }
    public bool IsElevated { get; set; }
}

public sealed class DisplayInfo
{
    public string DeviceName { get; set; } = string.Empty;
    public int Width { get; set; }
    public int Height { get; set; }
    public int RefreshRate { get; set; }
    public bool Primary { get; set; }
    public int BitsPerPixel { get; set; }
    public string GpuName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
}

public sealed class StorageInfo
{
    public string Model { get; set; } = "Inconnu";
    public string Manufacturer { get; set; } = "Inconnu";
    public string Interface { get; set; } = "Inconnu";
    public string BusType { get; set; } = "Inconnu";
    public DiskMediaType MediaType { get; set; } = DiskMediaType.Unknown;
    public long SizeBytes { get; set; }
    public List<VolumeInfo> Volumes { get; set; } = new();
    public string SerialNumber { get; set; } = string.Empty;
    public int? HealthPercent { get; set; }
    public string SmartStatus { get; set; } = "Non disponible";
    public double? TemperatureC { get; set; }
    public double? ReadSpeedMBs { get; set; }
    public double? WriteSpeedMBs { get; set; }
    public bool IsSystemDisk { get; set; }
    public List<string> SmartWarnings { get; set; } = new();
}

public sealed class VolumeInfo
{
    public string Letter { get; set; } = string.Empty;
    public string FileSystem { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public long FreeBytes { get; set; }
    public bool IsSystem { get; set; }
    public double UsedPercent => SizeBytes <= 0 ? 0 : Math.Round((SizeBytes - FreeBytes) * 100.0 / SizeBytes, 1);
}

public sealed class PeripheralInfo
{
    public string Name { get; set; } = "Inconnu";
    public string Kind { get; set; } = "Autre";
    public string PnpDeviceId { get; set; } = string.Empty;
}
