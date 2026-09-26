using GameBoost.Core.Hardware;
using GameBoost.Core.Logging;
using GameBoost.Core.Models;

namespace GameBoost.Core.Monitoring;

public sealed class SystemMonitor : IDisposable
{
    private const string VideoClassRegistry = @"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}";

    private static readonly Lazy<SystemMonitor> LazyInstance = new(() => new SystemMonitor());

    private readonly object _sync = new();
    private readonly FpsMeter _fps = new();
    private readonly List<PerfSample> _buffer = new(600);
    private MonitorSample _current = new();
    private System.Threading.Timer? _timer;
    private PerfQuery? _perf;
    private int _iCpu = -1;
    private int _iNetRx = -1;
    private int _iNetTx = -1;
    private int _iDiskRead = -1;
    private int _iDiskWrite = -1;
    private int _iVramUsed = -1;
    private int _iGpu = -1;
    private bool _gpuAttempted;
    private bool _running;
    private bool _disposed;
    private int _inTick;
    private long _vramTotalBytes;
    private int _vramRetry;
    private int? _trackedPid;
    private string? _trackedName;

    public static SystemMonitor Instance => LazyInstance.Value;

    private SystemMonitor()
    {
    }

    public event Action<MonitorSample>? SampleUpdated;

    public bool IsRunning
    {
        get { lock (_sync) return _running; }
    }

    public MonitorSample Current
    {
        get { lock (_sync) return _current; }
    }

    public int? TrackedProcessId
    {
        get { lock (_sync) return _trackedPid; }
    }

    public void Start(int intervalMs = 1000)
    {
        lock (_sync)
        {
            if (_disposed || _running) return;
            if (intervalMs < 200) intervalMs = 200;

            try
            {
                InitializeCounters();
            }
            catch (Exception ex)
            {
                Log.Error("Monitor", "Initialisation des compteurs impossible", ex);
            }

            _running = true;
            try
            {
                _timer = new System.Threading.Timer(Tick, null, intervalMs, intervalMs);
            }
            catch (Exception ex)
            {
                _running = false;
                _timer = null;
                Log.Error("Monitor", "Demarrage du moniteur impossible", ex);
            }
        }
    }

    public void Stop()
    {
        System.Threading.Timer? timer;
        PerfQuery? perf;
        lock (_sync)
        {
            if (!_running && _timer is null) return;
            _running = false;
            timer = _timer;
            _timer = null;
            perf = _perf;
            _perf = null;
            ResetCounters();
        }

        try
        {
            timer?.Dispose();
        }
        catch (Exception ex)
        {
            Log.Warn("Monitor", "Arret du minuteur impossible : " + ex.Message);
        }

        try
        {
            perf?.Dispose();
        }
        catch (Exception ex)
        {
            Log.Warn("Monitor", "Fermeture des compteurs impossible : " + ex.Message);
        }
    }

    public void TrackProcess(int processId, string name)
    {
        if (processId <= 0)
        {
            Log.Warn("Monitor", "Identifiant de processus invalide pour le suivi des images");
            return;
        }

        lock (_sync)
        {
            _trackedPid = processId;
            _trackedName = string.IsNullOrWhiteSpace(name) ? string.Empty : name.Trim();
        }

        try
        {
            _fps.Start(processId);
        }
        catch (Exception ex)
        {
            Log.Error("Monitor", "Demarrage de la mesure des images impossible", ex);
        }
    }

    public void ClearProcess()
    {
        lock (_sync)
        {
            _trackedPid = null;
            _trackedName = null;
        }

        try
        {
            _fps.Stop();
        }
        catch (Exception ex)
        {
            Log.Warn("Monitor", "Arret de la mesure des images impossible : " + ex.Message);
        }
    }

    public FpsStats? GetFpsStats()
    {
        lock (_sync)
        {
            if (_trackedPid is null) return null;
        }

        try
        {
            return _fps.GetStats();
        }
        catch (Exception ex)
        {
            Log.Error("Monitor", "Lecture des statistiques d'images impossible", ex);
            return null;
        }
    }

    public void ResetFpsStats()
    {
        try
        {
            _fps.Reset();
        }
        catch (Exception ex)
        {
            Log.Warn("Monitor", "Reinitialisation des statistiques d'images impossible : " + ex.Message);
        }
    }

    public void Dispose()
    {
        Stop();
        lock (_sync)
        {
            if (_disposed) return;
            _disposed = true;
        }

        try
        {
            _fps.Dispose();
        }
        catch (Exception ex)
        {
            Log.Warn("Monitor", "Fermeture du compteur d'images impossible : " + ex.Message);
        }
    }

    private void ResetCounters()
    {
        _iCpu = -1;
        _iNetRx = -1;
        _iNetTx = -1;
        _iDiskRead = -1;
        _iDiskWrite = -1;
        _iVramUsed = -1;
        _iGpu = -1;
        _gpuAttempted = false;
        _buffer.Clear();
    }

    private void InitializeCounters()
    {
        _perf?.Dispose();
        _perf = null;
        ResetCounters();

        _perf = PerfQuery.TryCreate();
        if (_perf is null)
        {
            Log.Warn("Monitor", "Compteurs de performance indisponibles, seules les donnees de base seront lues");
            return;
        }

        if (!_perf.TryAddEnglish(@"\Processor(_Total)\% Processor Utility", out _iCpu) &&
            !_perf.TryAddEnglish(@"\Processor Information(_Total)\% Processor Utility", out _iCpu) &&
            !_perf.TryAddEnglish(@"\Processor(_Total)\% Processor Time", out _iCpu))
        {
            _iCpu = -1;
            Log.Warn("Monitor", "Taux d'utilisation du processeur indisponible");
        }

        var hasReceive = _perf.TryAddEnglish(@"\Network Interface(*)\Bytes Received/sec", out _iNetRx);
        var hasSend = _perf.TryAddEnglish(@"\Network Interface(*)\Bytes Send/sec", out _iNetTx);
        if (!hasReceive || !hasSend) Log.Warn("Monitor", "Compteurs de reseau indisponibles");

        if (!TryAddDiskCounters()) Log.Warn("Monitor", "Compteurs de disque indisponibles");

        if (!_perf.TryAddEnglish(@"\GPU Adapter Memory(*)\Dedicated Usage", out _iVramUsed))
        {
            _iVramUsed = -1;
            Log.Warn("Monitor", "Compteurs de memoire video indisponibles");
        }

        _perf.Collect();
        Thread.Sleep(150);
        _perf.Collect();

        _vramTotalBytes = ReadVramTotalBytes();
        _vramRetry = 0;
    }

    private bool TryAddDiskCounters()
    {
        if (_perf is null) return false;
        return TryAddDiskPair(@"\LogicalDisk(_Total)", "Disk Read Bytes/sec", "Disk Write Bytes/sec") ||
               TryAddDiskPair(@"\LogicalDisk(_Total)", "Bytes Read/sec", "Bytes Written/sec") ||
               TryAddDiskPair(@"\PhysicalDisk(_Total)", "Disk Read Bytes/sec", "Disk Write Bytes/sec") ||
               TryAddDiskPair(@"\PhysicalDisk(_Total)", "Bytes Read/sec", "Bytes Written/sec");
    }

    private bool TryAddDiskPair(string objectName, string readCounter, string writeCounter)
    {
        if (_perf is null) return false;
        if (!_perf.TryAddEnglish(objectName + @"\" + readCounter, out _iDiskRead)) return false;
        return _perf.TryAddEnglish(objectName + @"\" + writeCounter, out _iDiskWrite);
    }

    private void Tick(object? state)
    {
        if (Interlocked.Exchange(ref _inTick, 1) == 1) return;
        MonitorSample? sample = null;
        try
        {
            lock (_sync)
            {
                if (!_running) return;
                sample = BuildSample();
                _current = sample;
            }
        }
        catch (Exception ex)
        {
            Log.Error("Monitor", "Collecte des mesures impossible", ex);
        }
        finally
        {
            Interlocked.Exchange(ref _inTick, 0);
        }

        if (sample is null) return;
        try
        {
            SampleUpdated?.Invoke(sample);
        }
        catch (Exception ex)
        {
            Log.Warn("Monitor", "Un abonne a refuse la mise a jour : " + ex.Message);
        }
    }

    private MonitorSample BuildSample()
    {
        var sample = new MonitorSample { Timestamp = DateTime.Now };
        var perf = _perf;
        var collected = perf is not null && perf.Collect();

        if (collected && _iCpu >= 0 && perf!.TryRead(_iCpu, _buffer))
            sample.CpuUsagePercent = Clamp(_buffer[0].Value, 0, 100);

        if (MonitorNative.TryReadMemory(out var used, out var total))
        {
            sample.RamUsedBytes = used;
            sample.RamTotalBytes = total;
        }

        if (collected && _iNetRx >= 0 && perf!.TryRead(_iNetRx, _buffer))
            sample.NetworkReceiveBytesPerSec = SumNetwork(_buffer);
        if (collected && _iNetTx >= 0 && perf!.TryRead(_iNetTx, _buffer))
            sample.NetworkSendBytesPerSec = SumNetwork(_buffer);

        if (collected && _iDiskRead >= 0 && perf!.TryRead(_iDiskRead, _buffer))
            sample.DiskReadBytesPerSec = Math.Max(0, Total(_buffer));
        if (collected && _iDiskWrite >= 0 && perf!.TryRead(_iDiskWrite, _buffer))
            sample.DiskWriteBytesPerSec = Math.Max(0, Total(_buffer));

        if (collected && _iVramUsed >= 0 && perf!.TryRead(_iVramUsed, _buffer))
            sample.VramUsedBytes = (long)Math.Max(0, Total(_buffer));

        if (_vramTotalBytes <= 0 && ++_vramRetry % 60 == 0) _vramTotalBytes = ReadVramTotalBytes();
        sample.VramTotalBytes = _vramTotalBytes;

        var sensors = ReadSensors();
        sample.CpuTemperatureC = PositiveOrNull(sensors.CpuTemperatureC);
        sample.CpuClockMHz = PositiveOrNull(sensors.CpuClockMHz);
        sample.GpuTemperatureC = PositiveOrNull(sensors.GpuTemperatureC);
        sample.GpuClockMHz = PositiveOrNull(sensors.GpuClockMHz);
        sample.GpuFanPercent = PositiveOrNull(sensors.GpuFanPercent);

        if (sensors.Available && sensors.GpuLoadPercent is double gpuLoad)
            sample.GpuUsagePercent = Clamp(gpuLoad, 0, 100);
        else
            sample.GpuUsagePercent = ReadGpuFallback();

        if (_trackedPid is int tracked)
        {
            sample.MonitoredProcessId = tracked;
            sample.MonitoredProcessName = _trackedName;
            try
            {
                var stats = _fps.GetStats();
                if (stats.SampleCount > 0 && stats.Average is double fps && fps > 0)
                {
                    sample.Fps = fps;
                    sample.FrameTimeMs = 1000.0 / fps;
                }
            }
            catch (Exception ex)
            {
                Log.Warn("Monitor", "Lecture des images impossible : " + ex.Message);
            }
        }

        return sample;
    }

    private double ReadGpuFallback()
    {
        if (_perf is null) return 0;

        if (_iGpu < 0 && !_gpuAttempted)
        {
            _gpuAttempted = true;
            if (!_perf.TryAddEnglish(@"\GPU Engine(*)\Utilization Percentage", out _iGpu))
            {
                _iGpu = -1;
                Log.Warn("Monitor", "Compteur d'utilisation du processeur graphique indisponible");
                return 0;
            }

            _perf.Collect();
            Thread.Sleep(80);
            _perf.Collect();
        }

        if (_iGpu < 0 || !_perf.TryRead(_iGpu, _buffer)) return 0;
        var sum = 0.0;
        foreach (var sample in _buffer)
        {
            if (IsPrimaryEngine(sample.Instance)) sum += sample.Value;
        }
        return Clamp(sum, 0, 100);
    }

    private static bool IsPrimaryEngine(string instance)
    {
        var index = instance.IndexOf("engtype_", StringComparison.OrdinalIgnoreCase);
        if (index < 0) return false;
        var engineType = instance[(index + 8)..];
        return engineType.Equals("3D", StringComparison.OrdinalIgnoreCase) ||
               engineType.Equals("VideoDecode", StringComparison.OrdinalIgnoreCase);
    }

    private static double SumNetwork(List<PerfSample> samples)
    {
        var total = 0.0;
        foreach (var sample in samples)
        {
            if (sample.Instance.Contains("loopback", StringComparison.OrdinalIgnoreCase)) continue;
            total += sample.Value;
        }
        return Math.Max(0, total);
    }

    private static double Total(List<PerfSample> samples)
    {
        var total = 0.0;
        foreach (var sample in samples) total += sample.Value;
        return total;
    }

    private static SensorSnapshot ReadSensors()
    {
        try
        {
            return HardwareSensors.Instance.Read();
        }
        catch (Exception ex)
        {
            Log.Warn("Monitor", "Lecture des capteurs materiels impossible : " + ex.Message);
            return new SensorSnapshot { Available = false };
        }
    }

    private static long ReadVramTotalBytes()
    {
        try
        {
            var value = MonitorNative.ReadRegistryQword(VideoClassRegistry + @"\0000", "HardwareInformation.qwMemorySize");
            if (value > 0) return value;

            long best = 0;
            for (var i = 1; i <= 31; i++)
            {
                var candidate = MonitorNative.ReadRegistryQword(VideoClassRegistry + "\\" + i.ToString("D4"), "HardwareInformation.qwMemorySize");
                if (candidate > best) best = candidate;
            }
            return best;
        }
        catch (Exception ex)
        {
            Log.Warn("Monitor", "Lecture de la memoire video impossible : " + ex.Message);
            return 0;
        }
    }

    private static double? PositiveOrNull(double? value) =>
        value is double measured && measured > 0 && !double.IsNaN(measured) ? measured : null;

    private static double Clamp(double value, double min, double max)
    {
        if (double.IsNaN(value)) return min;
        if (value < min) return min;
        return value > max ? max : value;
    }
}

