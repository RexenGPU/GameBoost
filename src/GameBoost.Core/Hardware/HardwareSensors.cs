using GameBoost.Core.Logging;
using LibreHardwareMonitor.Hardware;

namespace GameBoost.Core.Hardware;

public sealed class SensorSnapshot
{
    public bool Available { get; set; }
    public bool ElevationRequired { get; set; }
    public string? Error { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.Now;
    public double? CpuTemperatureC { get; set; }
    public double? CpuPackagePowerW { get; set; }
    public double? CpuClockMHz { get; set; }
    public double? CpuLoadPercent { get; set; }
    public double? GpuTemperatureC { get; set; }
    public double? GpuHotspotTemperatureC { get; set; }
    public double? GpuClockMHz { get; set; }
    public double? GpuLoadPercent { get; set; }
    public double? GpuFanPercent { get; set; }
    public double? MemoryUsedBytes { get; set; }
}

public sealed class HardwareSensors : IDisposable
{
    private static readonly Lazy<HardwareSensors> LazyInstance = new(() => new HardwareSensors());
    public static HardwareSensors Instance => LazyInstance.Value;

    private readonly object _sync = new();
    private Computer? _computer;
    private bool _open;
    private bool _startAttempted;
    private SensorSnapshot _last = new();

    public bool IsOpen
    {
        get { lock (_sync) return _open; }
    }

    public bool ElevationRequired { get; private set; }
    public string? LastError { get; private set; }

    public void Start()
    {
        lock (_sync)
        {
            if (_open || _startAttempted) return;
            _startAttempted = true;
            try
            {
                _computer = new Computer
                {
                    IsCpuEnabled = true,
                    IsGpuEnabled = true,
                    IsMemoryEnabled = true,
                    IsMotherboardEnabled = false,
                    IsControllerEnabled = false,
                    IsNetworkEnabled = false,
                    IsStorageEnabled = false,
                    IsBatteryEnabled = false,
                    IsPsuEnabled = false
                };
                _computer.Open();
                _open = true;
                LastError = null;
                ElevationRequired = false;
                Log.Info("Sensors", "Capteurs matériels ouverts");
            }
            catch (Exception ex)
            {
                _open = false;
                LastError = ex.Message;
                ElevationRequired = IsAccessDenied(ex);
                Log.Warn("Sensors", "Ouverture des capteurs impossible : " + ex.Message);
            }
        }
    }

    public void Restart()
    {
        lock (_sync)
        {
            StopUnsafe();
            _startAttempted = false;
        }
        Start();
    }

    public void Stop()
    {
        lock (_sync) StopUnsafe();
    }

    private void StopUnsafe()
    {
        try
        {
            _computer?.Close();
        }
        catch (Exception ex)
        {
            Log.Warn("Sensors", "Fermeture des capteurs : " + ex.Message);
        }
        _computer = null;
        _open = false;
        _startAttempted = false;
    }

    public SensorSnapshot Read()
    {
        lock (_sync)
        {
            if (!_open)
            {
                if (!_startAttempted) Start();
                if (!_open)
                {
                    return new SensorSnapshot
                    {
                        Available = false,
                        ElevationRequired = ElevationRequired,
                        Error = LastError ?? "Capteurs matériels indisponibles"
                    };
                }
            }

            var snapshot = new SensorSnapshot { Available = true };
            try
            {
                foreach (var hardware in _computer!.Hardware)
                {
                    UpdateRecursive(hardware);
                    Collect(hardware, snapshot);
                    foreach (var sub in hardware.SubHardware)
                        Collect(sub, snapshot);
                }
                snapshot.Timestamp = DateTime.Now;
                _last = snapshot;
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
                Log.Warn("Sensors", "Lecture des capteurs : " + ex.Message);
                snapshot.Available = _last.Available;
                snapshot.ElevationRequired = ElevationRequired;
                snapshot.Error = _last.Available ? null : ex.Message;
                if (!snapshot.Available && IsAccessDenied(ex)) ElevationRequired = true;
                return snapshot;
            }
            return snapshot;
        }
    }

    private static void UpdateRecursive(IHardware hardware)
    {
        hardware.Update();
        foreach (var sub in hardware.SubHardware)
            UpdateRecursive(sub);
    }

    private static void Collect(IHardware hardware, SensorSnapshot snapshot)
    {
        foreach (var sensor in hardware.Sensors)
        {
            if (sensor.Value is null) continue;
            var value = sensor.Value.Value;
            switch (hardware.HardwareType)
            {
                case HardwareType.Cpu:
                    CollectCpu(sensor, value, snapshot);
                    break;
                case HardwareType.GpuNvidia:
                case HardwareType.GpuAmd:
                case HardwareType.GpuIntel:
                    CollectGpu(sensor, value, snapshot);
                    break;
                case HardwareType.Memory:
                    if (sensor.SensorType == SensorType.Data && sensor.Name.Contains("Used", StringComparison.OrdinalIgnoreCase))
                        snapshot.MemoryUsedBytes ??= value * 1024d * 1024d * 1024d;
                    break;
            }
        }
    }

    private static void CollectCpu(ISensor sensor, float value, SensorSnapshot snapshot)
    {
        var name = sensor.Name;
        if (sensor.SensorType == SensorType.Temperature)
        {
            if (IsPreferredCpuTemp(name))
                snapshot.CpuTemperatureC = value;
            else if (snapshot.CpuTemperatureC is null && value > 0)
                snapshot.CpuTemperatureC = value;
        }
        else if (sensor.SensorType == SensorType.Clock && IsCoreClock(name))
        {
            snapshot.CpuClockMHz = Math.Max(snapshot.CpuClockMHz ?? 0, value);
        }
        else if (sensor.SensorType == SensorType.Load && name.Contains("Total", StringComparison.OrdinalIgnoreCase))
        {
            snapshot.CpuLoadPercent = value;
        }
        else if (sensor.SensorType == SensorType.Power && (name.Contains("Package", StringComparison.OrdinalIgnoreCase) || name.Contains("CPU Power", StringComparison.OrdinalIgnoreCase)))
        {
            snapshot.CpuPackagePowerW = value;
        }
    }

    private static void CollectGpu(ISensor sensor, float value, SensorSnapshot snapshot)
    {
        var name = sensor.Name;
        switch (sensor.SensorType)
        {
            case SensorType.Temperature:
                if (name.Contains("Hot Spot", StringComparison.OrdinalIgnoreCase) || name.Contains("Junction", StringComparison.OrdinalIgnoreCase))
                    snapshot.GpuHotspotTemperatureC = value;
                else if (name.Contains("GPU Core", StringComparison.OrdinalIgnoreCase) || name.Contains("GPU", StringComparison.OrdinalIgnoreCase))
                    snapshot.GpuTemperatureC = value;
                else if (snapshot.GpuTemperatureC is null && value > 0)
                    snapshot.GpuTemperatureC = value;
                break;
            case SensorType.Clock when name.Contains("GPU Core", StringComparison.OrdinalIgnoreCase) || name.Contains("GPU", StringComparison.OrdinalIgnoreCase):
                snapshot.GpuClockMHz = value;
                break;
            case SensorType.Load when name.Contains("GPU Core", StringComparison.OrdinalIgnoreCase) || name.Contains("GPU", StringComparison.OrdinalIgnoreCase):
                snapshot.GpuLoadPercent = value;
                break;
            case SensorType.Control when name.Contains("Fan", StringComparison.OrdinalIgnoreCase):
                snapshot.GpuFanPercent = value;
                break;
        }
    }

    private static bool IsPreferredCpuTemp(string name) =>
        name.Contains("Package", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("Tctl", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("Tdie", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("CPU Die", StringComparison.OrdinalIgnoreCase);

    private static bool IsCoreClock(string name) =>
        name.StartsWith("Core", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("Bus Speed", StringComparison.OrdinalIgnoreCase) == false && name.Contains("Total", StringComparison.OrdinalIgnoreCase);

    private static bool IsAccessDenied(Exception ex)
    {
        for (var e = ex; e is not null; e = e.InnerException)
        {
            if (e is UnauthorizedAccessException) return true;
            if (e is System.ComponentModel.Win32Exception w32 && (w32.NativeErrorCode == 5 || (uint)w32.NativeErrorCode == 0xC0000022u)) return true;
            if (e.Message.Contains("denied", StringComparison.OrdinalIgnoreCase) ||
                e.Message.Contains("refus", StringComparison.OrdinalIgnoreCase) ||
                e.Message.Contains("administrator", StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    public void Dispose() => Stop();
}
