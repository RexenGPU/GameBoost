namespace GameBoost.Core.Models;

public sealed class MonitorSample
{
    public DateTime Timestamp { get; set; } = DateTime.Now;
    public double CpuUsagePercent { get; set; }
    public double? CpuTemperatureC { get; set; }
    public double? CpuClockMHz { get; set; }
    public double GpuUsagePercent { get; set; }
    public double? GpuTemperatureC { get; set; }
    public double? GpuClockMHz { get; set; }
    public double? GpuFanPercent { get; set; }
    public long RamUsedBytes { get; set; }
    public long RamTotalBytes { get; set; }
    public long VramUsedBytes { get; set; }
    public long VramTotalBytes { get; set; }
    public double NetworkSendBytesPerSec { get; set; }
    public double NetworkReceiveBytesPerSec { get; set; }
    public double DiskReadBytesPerSec { get; set; }
    public double DiskWriteBytesPerSec { get; set; }
    public double? Fps { get; set; }
    public double? FrameTimeMs { get; set; }
    public int? MonitoredProcessId { get; set; }
    public string? MonitoredProcessName { get; set; }
}

public sealed class FpsStats
{
    public double? Average { get; set; }
    public double? Minimum { get; set; }
    public double? Maximum { get; set; }
    public double? OnePercentLow { get; set; }
    public double? ZeroPointOnePercentLow { get; set; }
    public double? Median { get; set; }
    public int SampleCount { get; set; }
    public List<double> FrameTimesMs { get; set; } = new();
}

public sealed class ProcessSnapshot
{
    public int ProcessId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public double CpuPercent { get; set; }
    public long MemoryBytes { get; set; }
    public double? GpuPercent { get; set; }
    public long? GpuDedicatedBytes { get; set; }
    public double DiskReadBytesPerSec { get; set; }
    public double DiskWriteBytesPerSec { get; set; }
    public double NetworkBytesPerSec { get; set; }
    public DateTime StartTime { get; set; }
    public bool IsElevated { get; set; }
    public bool IsCritical { get; set; }
    public bool IsStoreApp { get; set; }
    public bool IsSelected { get; set; }
    public string Publisher { get; set; } = string.Empty;
    public string IconPath { get; set; } = string.Empty;
}

public sealed class SessionRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string GameName { get; set; } = "Session manuelle";
    public string GameId { get; set; } = string.Empty;
    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }
    public double? AverageFps { get; set; }
    public double? MinimumFps { get; set; }
    public double? MaximumFps { get; set; }
    public double? OnePercentLowFps { get; set; }
    public double? AverageGpuUsagePercent { get; set; }
    public double? MaxGpuTemperatureC { get; set; }
    public double? AverageCpuUsagePercent { get; set; }
    public double? MaxCpuTemperatureC { get; set; }
    public double? AverageFrameTimeMs { get; set; }
    public long PeakRamUsedBytes { get; set; }
    public List<double> FpsSeries { get; set; } = new();
    public List<double> FrameTimeSeriesMs { get; set; } = new();
    public List<double> GpuUsageSeries { get; set; } = new();
    public List<double> GpuTempSeries { get; set; } = new();
    public string ProfileUsed { get; set; } = string.Empty;
    public List<string> OptimizationsApplied { get; set; } = new();
    public string Notes { get; set; } = string.Empty;
}

public sealed class SessionComparison
{
    public SessionRecord SessionA { get; set; } = new();
    public SessionRecord SessionB { get; set; } = new();
    public double? AvgFpsDelta { get; set; }
    public double? OnePercentLowDelta { get; set; }
    public double? GpuUsageDelta { get; set; }
    public double? MaxGpuTempDelta { get; set; }
    public double? AvgCpuUsageDelta { get; set; }
    public double? MaxCpuTempDelta { get; set; }
}
