using GameBoost.Core.Logging;
using GameBoost.Core.Models;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Session;

namespace GameBoost.Core.Monitoring;

public sealed class FpsMeter : IDisposable
{
    private static readonly Guid DxgiProviderGuid = new("ca11c036-0102-4a2d-a6ad-f03cfed5d3c9");
    private const int PresentStartEventId = 42;
    private const int PresentMultiplaneOverlayStartEventId = 55;
    private const int MaxSamples = 6000;
    private const double MaxFrameTimeMs = 5000;

    private readonly object _sync = new();
    private readonly double[] _frameTimes = new double[MaxSamples];
    private int _count;
    private int _head;
    private double _lastTimestamp = -1;
    private int _generation;
    private int _pid;
    private volatile bool _running;
    private volatile bool _stopRequested;
    private Thread? _thread;
    private TraceEventSession? _session;

    public bool IsRunning => _running;

    public void Start(int processId)
    {
        if (processId <= 0)
        {
            Log.Warn("Fps", "Identifiant de processus invalide pour la mesure des images");
            return;
        }

        lock (_sync)
        {
            if (_running && _thread is not null && _thread.IsAlive && _pid == processId) return;
        }

        Stop();

        lock (_sync)
        {
            _pid = processId;
            _generation++;
            _count = 0;
            _head = 0;
            _lastTimestamp = -1;
            _stopRequested = false;
            _running = true;
            var generation = _generation;
            _thread = new Thread(() => Run(generation, processId)) { IsBackground = true, Name = "GameBoost-FpsMeter" };
            _thread.Start();
        }
    }

    public void Stop()
    {
        Thread? thread;
        TraceEventSession? session;
        lock (_sync)
        {
            if (!_running && _thread is null) return;
            _running = false;
            _stopRequested = true;
            _generation++;
            thread = _thread;
            session = _session;
        }

        try
        {
            session?.Source.StopProcessing();
        }
        catch (Exception ex)
        {
            Log.Warn("Fps", "Arret de la session de suivi impossible : " + ex.Message);
        }

        try
        {
            if (thread is not null && thread != Thread.CurrentThread && thread.IsAlive)
                thread.Join(3000);
        }
        catch (Exception ex)
        {
            Log.Warn("Fps", "Arret du thread de mesure impossible : " + ex.Message);
        }

        lock (_sync)
        {
            if (_thread == thread) _thread = null;
            if (ReferenceEquals(_session, session)) _session = null;
        }
    }

    public FpsStats GetStats()
    {
        lock (_sync)
        {
            var stats = new FpsStats { SampleCount = _count };
            if (_count == 0) return stats;

            var values = new double[_count];
            var start = _head - _count;
            for (var i = 0; i < _count; i++)
            {
                var index = start + i;
                while (index < 0) index += MaxSamples;
                values[i] = _frameTimes[index];
            }
            stats.FrameTimesMs = new List<double>(values);

            Array.Sort(values);
            var total = 0.0;
            foreach (var value in values) total += value;
            var average = total / values.Length;
            if (average > 0) stats.Average = 1000.0 / average;

            var shortest = values[0];
            var longest = values[^1];
            if (longest > 0) stats.Minimum = 1000.0 / longest;
            if (shortest > 0) stats.Maximum = 1000.0 / shortest;

            var median = values.Length % 2 == 1
                ? values[values.Length / 2]
                : (values[values.Length / 2 - 1] + values[values.Length / 2]) / 2.0;
            if (median > 0) stats.Median = 1000.0 / median;

            stats.OnePercentLow = WorstSegmentFps(values, 0.01);
            stats.ZeroPointOnePercentLow = WorstSegmentFps(values, 0.001);
            return stats;
        }
    }

    public void Reset()
    {
        lock (_sync)
        {
            _count = 0;
            _head = 0;
            _lastTimestamp = -1;
        }
    }

    public void Dispose() => Stop();

    private static double? WorstSegmentFps(double[] sortedFrameTimes, double ratio)
    {
        if (sortedFrameTimes.Length == 0) return null;
        var take = Math.Max(1, (int)Math.Ceiling(sortedFrameTimes.Length * ratio));
        take = Math.Min(take, sortedFrameTimes.Length);
        var total = 0.0;
        for (var i = sortedFrameTimes.Length - take; i < sortedFrameTimes.Length; i++)
            total += sortedFrameTimes[i];
        var average = total / take;
        return average > 0 ? 1000.0 / average : null;
    }

    private void Run(int generation, int processId)
    {
        TraceEventSession? session = null;
        try
        {
            session = new TraceEventSession("GameBoostFps-" + Guid.NewGuid().ToString("N"))
            {
                StopOnDispose = true
            };
            session.EnableProvider(DxgiProviderGuid, TraceEventLevel.Verbose, ulong.MaxValue);

            lock (_sync)
            {
                if (_generation != generation || _stopRequested)
                {
                    session.Dispose();
                    return;
                }
                _session = session;
            }

            session.Source.AllEvents += data => OnEvent(data, generation, processId);
            session.Source.Process();
        }
        catch (Exception ex)
        {
            Log.Warn("Fps", "Mesure des images par ETW indisponible : " + ex.Message);
        }
        finally
        {
            lock (_sync)
            {
                if (_generation == generation)
                {
                    _running = false;
                    _stopRequested = true;
                    _session = null;
                    if (_thread == Thread.CurrentThread) _thread = null;
                }
            }
            if (session is not null)
            {
                try
                {
                    session.Dispose();
                }
                catch (Exception ex)
                {
                    Log.Warn("Fps", "Fermeture de la session ETW : " + ex.Message);
                }
            }
        }
    }

    private void OnEvent(TraceEvent data, int generation, int processId)
    {
        try
        {
            if (data.ProviderGuid != DxgiProviderGuid) return;
            var eventId = (int)data.ID;
            if (eventId != PresentStartEventId && eventId != PresentMultiplaneOverlayStartEventId) return;
            if (data.Opcode != TraceEventOpcode.Start) return;
            if (data.ProcessID != processId) return;

            var timestamp = data.TimeStampRelativeMSec;
            lock (_sync)
            {
                if (_generation != generation || !_running) return;
                if (_lastTimestamp >= 0)
                {
                    var frameTime = timestamp - _lastTimestamp;
                    if (frameTime > 0 && frameTime < MaxFrameTimeMs)
                    {
                        _frameTimes[_head] = frameTime;
                        _head = (_head + 1) % MaxSamples;
                        if (_count < MaxSamples) _count++;
                    }
                }
                _lastTimestamp = timestamp;
            }
        }
        catch (Exception ex)
        {
            Log.Warn("Fps", "Analyse d'un evenement de presentation : " + ex.Message);
        }
    }
}
