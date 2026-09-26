using GameBoost.Core.Data;
using GameBoost.Core.Logging;
using GameBoost.Core.Models;
using GameBoost.Core.Monitoring;

namespace GameBoost.Core.History;

public sealed class HistoryService
{
    public static HistoryService Instance { get; } = new();

    private const string CollectionName = "sessions";
    private const int MaxSeriesPoints = 3600;
    private const int MinFpsSamples = 3;
    private static readonly TimeSpan SampleInterval = TimeSpan.FromSeconds(1);

    private readonly object _sync = new();
    private readonly List<double> _fpsSamples = new();
    private SessionRecord? _active;
    private bool _subscribed;
    private DateTime _lastSampleAt = DateTime.MinValue;
    private int _sampleCount;
    private double _fpsSum;
    private double _fpsMin = double.MaxValue;
    private double _fpsMax = double.MinValue;
    private double _gpuSum;
    private int _gpuCount;
    private double _maxGpuTemp = double.MinValue;
    private double _cpuSum;
    private int _cpuCount;
    private double _maxCpuTemp = double.MinValue;
    private double _frameTimeSum;
    private int _frameTimeCount;
    private long _peakRam;

    private HistoryService()
    {
    }

    public event Action<SessionRecord>? SessionSaved;

    public SessionRecord? ActiveSession
    {
        get { lock (_sync) return _active; }
    }

    public void StartSession(string gameName, string gameId = "", string profileUsed = "")
    {
        if (ActiveSession is not null) StopSession();

        lock (_sync)
        {
            ResetCountersLocked();
            _active = new SessionRecord
            {
                Id = Guid.NewGuid(),
                GameName = string.IsNullOrWhiteSpace(gameName) ? "Session manuelle" : gameName.Trim(),
                GameId = gameId?.Trim() ?? string.Empty,
                ProfileUsed = profileUsed?.Trim() ?? string.Empty,
                StartTime = DateTime.Now
            };
            SubscribeLocked();
            Log.Info("History", "Debut de session : " + _active.GameName);
        }
    }

    public SessionRecord? StopSession(string notes = "")
    {
        SessionRecord record;
        lock (_sync)
        {
            if (_active is null) return null;
            record = FinalizeLocked(notes);
            UnsubscribeLocked();
        }

        SaveRecord(record);
        try
        {
            SystemMonitor.Instance.ResetFpsStats();
        }
        catch (Exception ex)
        {
            Log.Warn("History", "Reinitialisation des mesures d'images impossible : " + ex.Message);
        }

        try
        {
            SessionSaved?.Invoke(record);
        }
        catch (Exception ex)
        {
            Log.Warn("History", "Un abonne a refuse la fin de session : " + ex.Message);
        }

        return record;
    }

    public List<SessionRecord> GetSessions(int limit = 100, string? gameId = null)
    {
        try
        {
            var collection = Db.GetCollection<SessionRecord>(CollectionName);
            var query = collection.FindAll().AsEnumerable();
            if (!string.IsNullOrWhiteSpace(gameId))
            {
                var filter = gameId.Trim();
                query = query.Where(s => string.Equals(s.GameId, filter, StringComparison.OrdinalIgnoreCase));
            }
            query = query.OrderByDescending(s => s.StartTime);
            if (limit > 0) query = query.Take(limit);
            return query.ToList();
        }
        catch (Exception ex)
        {
            Log.Error("History", "Lecture de l'historique impossible", ex);
            return new List<SessionRecord>();
        }
    }

    public SessionRecord? GetSession(Guid id)
    {
        try
        {
            var collection = Db.GetCollection<SessionRecord>(CollectionName);
            return collection.FindById(id);
        }
        catch (Exception ex)
        {
            Log.Error("History", "Lecture de la session impossible", ex);
            return null;
        }
    }

    public bool DeleteSession(Guid id)
    {
        try
        {
            var collection = Db.GetCollection<SessionRecord>(CollectionName);
            var deleted = collection.Delete(id);
            if (!deleted) Log.Warn("History", "Session introuvable pour suppression : " + id);
            return deleted;
        }
        catch (Exception ex)
        {
            Log.Error("History", "Suppression de la session impossible", ex);
            return false;
        }
    }

    public void ClearAll()
    {
        try
        {
            var collection = Db.GetCollection<SessionRecord>(CollectionName);
            var removed = collection.DeleteAll();
            Log.Info("History", "Historique efface : " + removed + " session(s)");
        }
        catch (Exception ex)
        {
            Log.Error("History", "Effacement de l'historique impossible", ex);
        }
    }

    public SessionComparison Compare(Guid sessionIdA, Guid sessionIdB)
    {
        var sessionA = GetSession(sessionIdA);
        var sessionB = GetSession(sessionIdB);
        if (sessionA is null) Log.Warn("History", "Session A introuvable pour la comparaison : " + sessionIdA);
        if (sessionB is null) Log.Warn("History", "Session B introuvable pour la comparaison : " + sessionIdB);

        var a = sessionA ?? new SessionRecord { Id = sessionIdA };
        var b = sessionB ?? new SessionRecord { Id = sessionIdB };

        return new SessionComparison
        {
            SessionA = a,
            SessionB = b,
            AvgFpsDelta = Delta(b.AverageFps, a.AverageFps),
            OnePercentLowDelta = Delta(b.OnePercentLowFps, a.OnePercentLowFps),
            GpuUsageDelta = Delta(b.AverageGpuUsagePercent, a.AverageGpuUsagePercent),
            MaxGpuTempDelta = Delta(b.MaxGpuTemperatureC, a.MaxGpuTemperatureC),
            AvgCpuUsageDelta = Delta(b.AverageCpuUsagePercent, a.AverageCpuUsagePercent),
            MaxCpuTempDelta = Delta(b.MaxCpuTemperatureC, a.MaxCpuTemperatureC)
        };
    }

    public void PruneOldSessions()
    {
        try
        {
            var days = SettingsService.Current.HistoryRetentionDays;
            if (days <= 0)
            {
                Log.Info("History", "Conservation de l'historique illimitee, aucun tri");
                return;
            }

            var cutoff = DateTime.Now.AddDays(-days);
            var collection = Db.GetCollection<SessionRecord>(CollectionName);
            var expired = collection.Find(s => s.EndTime < cutoff).ToList();
            if (expired.Count == 0)
            {
                Log.Info("History", "Aucune session ancienne a supprimer");
                return;
            }

            var removed = 0;
            foreach (var session in expired)
            {
                if (collection.Delete(session.Id)) removed++;
            }
            Log.Info("History", "Sessions expirees supprimees : " + removed + " (seuil " + days + " jours)");
        }
        catch (Exception ex)
        {
            Log.Error("History", "Nettoyage de l'historique impossible", ex);
        }
    }

    private void OnSample(MonitorSample sample)
    {
        try
        {
            lock (_sync)
            {
                if (_active is null || sample is null) return;
                var now = sample.Timestamp == default ? DateTime.Now : sample.Timestamp;
                if (_lastSampleAt != DateTime.MinValue && now - _lastSampleAt < SampleInterval) return;
                _lastSampleAt = now;
                RecordSampleLocked(sample);
            }
        }
        catch (Exception ex)
        {
            Log.Error("History", "Enregistrement de la mesure impossible", ex);
        }
    }

    private void RecordSampleLocked(MonitorSample sample)
    {
        var record = _active;
        if (record is null) return;

        _sampleCount++;

        if (sample.Fps is double fps && fps > 0 && !double.IsNaN(fps))
        {
            _fpsSamples.Add(fps);
            _fpsSum += fps;
            if (fps < _fpsMin) _fpsMin = fps;
            if (fps > _fpsMax) _fpsMax = fps;
            record.FpsSeries.Add(fps);
        }

        if (sample.FrameTimeMs is double frameTime && frameTime > 0 && !double.IsNaN(frameTime))
        {
            _frameTimeSum += frameTime;
            _frameTimeCount++;
            record.FrameTimeSeriesMs.Add(frameTime);
        }

        record.GpuUsageSeries.Add(sample.GpuUsagePercent);
        _gpuSum += sample.GpuUsagePercent;
        _gpuCount++;

        if (sample.GpuTemperatureC is double gpuTemp && gpuTemp > 0)
        {
            if (gpuTemp > _maxGpuTemp) _maxGpuTemp = gpuTemp;
            record.GpuTempSeries.Add(gpuTemp);
        }

        _cpuSum += sample.CpuUsagePercent;
        _cpuCount++;
        if (sample.CpuTemperatureC is double cpuTemp && cpuTemp > 0 && cpuTemp > _maxCpuTemp) _maxCpuTemp = cpuTemp;

        if (sample.RamUsedBytes > _peakRam) _peakRam = sample.RamUsedBytes;

        CompressIfNeeded(record.FpsSeries);
        CompressIfNeeded(record.FrameTimeSeriesMs);
        CompressIfNeeded(record.GpuUsageSeries);
        CompressIfNeeded(record.GpuTempSeries);
    }

    private SessionRecord FinalizeLocked(string notes)
    {
        var record = _active!;
        record.EndTime = DateTime.Now;
        record.Notes = notes ?? string.Empty;
        record.PeakRamUsedBytes = _peakRam;
        record.OptimizationsApplied ??= new List<string>();

        var hasFps = _fpsSamples.Count >= MinFpsSamples;
        if (hasFps)
        {
            record.AverageFps = Math.Round(_fpsSum / _fpsSamples.Count, 2);
            record.MinimumFps = Math.Round(_fpsMin, 2);
            record.MaximumFps = Math.Round(_fpsMax, 2);
            record.OnePercentLowFps = ComputeOnePercentLow(_fpsSamples);
        }
        else
        {
            record.AverageFps = null;
            record.MinimumFps = null;
            record.MaximumFps = null;
            record.OnePercentLowFps = null;
            record.FpsSeries.Clear();
            record.FrameTimeSeriesMs.Clear();
            Log.Info("History", "Session sans mesure d'images suffisante, enregistrement sans FPS");
        }

        if (_gpuCount > 0) record.AverageGpuUsagePercent = Math.Round(_gpuSum / _gpuCount, 2);
        if (_maxGpuTemp > double.MinValue) record.MaxGpuTemperatureC = Math.Round(_maxGpuTemp, 1);
        if (_cpuCount > 0) record.AverageCpuUsagePercent = Math.Round(_cpuSum / _cpuCount, 2);
        if (_maxCpuTemp > double.MinValue) record.MaxCpuTemperatureC = Math.Round(_maxCpuTemp, 1);

        if (hasFps && _frameTimeCount > 0)
            record.AverageFrameTimeMs = Math.Round(_frameTimeSum / _frameTimeCount, 2);
        else
            record.AverageFrameTimeMs = null;

        Log.Info("History", "Session terminee : " + _sampleCount + " mesure(s), " + _fpsSamples.Count + " image(s)");
        _active = null;
        return record;
    }

    private void ResetCountersLocked()
    {
        _fpsSamples.Clear();
        _lastSampleAt = DateTime.MinValue;
        _sampleCount = 0;
        _fpsSum = 0;
        _fpsMin = double.MaxValue;
        _fpsMax = double.MinValue;
        _gpuSum = 0;
        _gpuCount = 0;
        _maxGpuTemp = double.MinValue;
        _cpuSum = 0;
        _cpuCount = 0;
        _maxCpuTemp = double.MinValue;
        _frameTimeSum = 0;
        _frameTimeCount = 0;
        _peakRam = 0;
    }

    private void SubscribeLocked()
    {
        if (_subscribed) return;
        SystemMonitor.Instance.SampleUpdated += OnSample;
        _subscribed = true;
    }

    private void UnsubscribeLocked()
    {
        if (!_subscribed) return;
        SystemMonitor.Instance.SampleUpdated -= OnSample;
        _subscribed = false;
    }

    private void SaveRecord(SessionRecord record)
    {
        try
        {
            var collection = Db.GetCollection<SessionRecord>(CollectionName);
            collection.Insert(record);
            Log.Info("History", "Session enregistree dans la base : " + record.GameName + " (" + record.Id + ")");
        }
        catch (Exception ex)
        {
            Log.Error("History", "Enregistrement de la session impossible", ex);
        }
    }

    private static double? Delta(double? valueB, double? valueA)
    {
        if (valueB is not double b || valueA is not double a) return null;
        return Math.Round(b - a, 2);
    }

    private static double? ComputeOnePercentLow(List<double> samples)
    {
        if (samples.Count == 0) return null;
        var sorted = new List<double>(samples);
        sorted.Sort();
        var take = Math.Max(1, (int)Math.Ceiling(sorted.Count * 0.01));
        if (take > sorted.Count) take = sorted.Count;
        var total = 0.0;
        for (var i = 0; i < take; i++) total += sorted[i];
        return Math.Round(total / take, 2);
    }

    private static void CompressIfNeeded(List<double> series)
    {
        if (series.Count <= MaxSeriesPoints) return;
        var compressed = new List<double>(series.Count / 2 + 4);
        var index = 0;
        for (; index + 3 < series.Count; index += 4)
        {
            var first = series[index];
            var second = series[index + 1];
            var third = series[index + 2];
            var fourth = series[index + 3];
            var low = Math.Min(Math.Min(first, second), Math.Min(third, fourth));
            var high = Math.Max(Math.Max(first, second), Math.Max(third, fourth));
            compressed.Add(low);
            compressed.Add(high);
        }
        for (; index < series.Count; index++) compressed.Add(series[index]);
        series.Clear();
        series.AddRange(compressed);
    }
}
