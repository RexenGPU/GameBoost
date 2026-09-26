using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using GameBoost.Core.Data;
using GameBoost.Core.Logging;
using GameBoost.Core.Models;
using GameBoost.Core.Monitoring;

namespace GameBoost.Core.Processes;

public sealed class ProcessService
{
    private const uint DesiredAccess = ProcessNative.ProcessQueryInformation | ProcessNative.ProcessQueryLimitedInformation;
    private const long MinimumDeltaMs = 10;
    private const double GpuCacheMs = 400;

    private static readonly HashSet<string> CriticalNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "csrss", "smss", "wininit", "winlogon", "lsass", "services", "System", "Registry",
        "fontdrvhost", "dwm", "svchost", "SecurityHealthService", "LsaIso", "MemCompression", "Idle"
    };

    private static readonly Lazy<ProcessService> LazyInstance = new(() => new ProcessService());

    private readonly object _sync = new();
    private readonly Dictionary<int, (TimeSpan Cpu, DateTime At)> _cpuCache = new();
    private readonly Dictionary<int, (ProcessNative.IoCounters Io, DateTime At)> _ioCache = new();
    private readonly Dictionary<int, string> _pathByPid = new();
    private readonly Dictionary<string, PathDetails> _pathDetails = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<int, bool> _elevation = new();
    private readonly HashSet<int> _accessFailureLogged = new();
    private readonly List<PerfSample> _buffer = new(600);
    private PerfQuery? _gpuQuery;
    private int _gpuIndex = -1;
    private bool _gpuPrimed;
    private bool _gpuUnavailable;
    private DateTime _gpuStamp = DateTime.MinValue;
    private Dictionary<int, double>? _gpuLast;
    private bool _videoMemoryAvailable = true;

    private ProcessService()
    {
    }

    public static ProcessService Instance => LazyInstance.Value;

    public List<ProcessSnapshot> GetProcesses()
    {
        var result = new List<ProcessSnapshot>();
        var now = DateTime.UtcNow;
        Dictionary<int, double>? gpuByPid = null;

        try
        {
            gpuByPid = ReadGpuByPid();
        }
        catch (Exception ex)
        {
            Log.Error("Processes", "Lecture de l'utilisation GPU impossible", ex);
        }

        Process[] processes;
        try
        {
            processes = Process.GetProcesses();
        }
        catch (Exception ex)
        {
            Log.Error("Processes", "Enumeration des processus impossible", ex);
            return result;
        }

        var seenPids = new HashSet<int>();
        var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            foreach (var process in processes)
            {
                try
                {
                    var snapshot = BuildSnapshot(process, now, gpuByPid, seenPaths);
                    if (snapshot is null) continue;
                    result.Add(snapshot);
                    seenPids.Add(snapshot.ProcessId);
                }
                catch (Exception ex)
                {
                    Log.Warn("Processes", "Lecture d'un processus impossible : " + ex.Message);
                }
            }
        }
        finally
        {
            foreach (var process in processes)
            {
                try
                {
                    process.Dispose();
                }
                catch
                {
                }
            }
        }

        Purge(seenPids, seenPaths);
        return result;
    }

    public ProcessSnapshot? GetProcess(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            Dictionary<int, double>? gpuByPid = null;
            try
            {
                gpuByPid = ReadGpuByPid();
            }
            catch (Exception ex)
            {
                Log.Error("Processes", "Lecture de l'utilisation GPU impossible", ex);
            }
            return BuildSnapshot(process, DateTime.UtcNow, gpuByPid, null);
        }
        catch (ArgumentException)
        {
            return null;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
        catch (Exception ex)
        {
            Log.Error("Processes", "Lecture du processus " + processId + " impossible", ex);
            return null;
        }
    }

    public bool CloseProcess(int processId, out string message)
    {
        if (processId == Environment.ProcessId)
        {
            message = "Impossible de fermer l'application elle-même.";
            Log.Warn("Processes", "Fermeture refusee pour l'application en cours");
            return false;
        }

        var snapshot = GetProcess(processId);
        if (snapshot is null)
        {
            message = "Processus introuvable.";
            return false;
        }

        if (!CanClose(snapshot, out var reason))
        {
            message = reason;
            Log.Warn("Processes", "Fermeture refusee pour " + snapshot.Name + " (" + processId + ") : " + reason);
            return false;
        }

        var displayName = string.IsNullOrWhiteSpace(snapshot.Name) ? processId.ToString(CultureInfo.InvariantCulture) : snapshot.Name;
        try
        {
            using var process = Process.GetProcessById(processId);
            try
            {
                if (process.HasExited)
                {
                    message = "Processus deja ferme.";
                    return true;
                }
            }
            catch
            {
            }

            process.Kill(entireProcessTree: true);
            message = "Processus « " + displayName + " » ferme.";
            Log.Info("Processes", message + " (PID " + processId + ")");
            return true;
        }
        catch (InvalidOperationException)
        {
            message = "Processus deja ferme.";
            return true;
        }
        catch (ArgumentException)
        {
            message = "Processus deja ferme.";
            return true;
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            Log.Error("Processes", "Fermeture de " + displayName + " refusee", ex);
            message = "Fermeture refusee pour « " + displayName + " » : " + ex.Message;
            return false;
        }
        catch (Exception ex)
        {
            Log.Error("Processes", "Fermeture de " + displayName + " impossible", ex);
            message = "Fermeture impossible pour « " + displayName + " » : " + ex.Message;
            return false;
        }
    }

    public (int closed, List<string> errors) CloseMany(IEnumerable<int> processIds)
    {
        var closed = 0;
        var errors = new List<string>();
        if (processIds is null) return (0, errors);

        var attempted = new HashSet<int>();
        foreach (var processId in processIds)
        {
            if (!attempted.Add(processId)) continue;
            if (CloseProcess(processId, out var message))
            {
                closed++;
            }
            else
            {
                errors.Add("PID " + processId + " : " + message);
            }
        }
        return (closed, errors);
    }

    public bool CanClose(ProcessSnapshot process, out string reason)
    {
        reason = string.Empty;
        if (process is null)
        {
            reason = "Processus introuvable.";
            return false;
        }

        var name = NormalizeName(process.Name);
        if (string.IsNullOrEmpty(name))
        {
            reason = "Processus introuvable.";
            return false;
        }

        if (CriticalNames.Contains(name))
        {
            reason = "Processus critique";
            return false;
        }

        if (IsInNeverCloseList(name))
        {
            reason = "Dans la liste « Ne jamais fermer »";
            return false;
        }

        if (process.IsElevated && IsSystemPath(process.Path))
        {
            reason = "Processus systeme Windows";
            return false;
        }

        return true;
    }

    public IReadOnlyList<string> GetNeverCloseList()
    {
        try
        {
            return SettingsService.Current.NeverCloseApps;
        }
        catch (Exception ex)
        {
            Log.Error("Processes", "Lecture de la liste de protection impossible", ex);
            return Array.Empty<string>();
        }
    }

    public void AddNeverClose(string name)
    {
        var normalized = NormalizeName(name);
        if (string.IsNullOrEmpty(normalized)) return;
        try
        {
            SettingsService.Update(settings =>
            {
                if (!settings.NeverCloseApps.Any(entry => string.Equals(NormalizeName(entry), normalized, StringComparison.OrdinalIgnoreCase)))
                    settings.NeverCloseApps.Add(normalized);
            });
        }
        catch (Exception ex)
        {
            Log.Error("Processes", "Ajout a la liste de protection impossible", ex);
        }
    }

    public void RemoveNeverClose(string name)
    {
        var normalized = NormalizeName(name);
        if (string.IsNullOrEmpty(normalized)) return;
        try
        {
            SettingsService.Update(settings =>
                settings.NeverCloseApps.RemoveAll(entry => string.Equals(NormalizeName(entry), normalized, StringComparison.OrdinalIgnoreCase)));
        }
        catch (Exception ex)
        {
            Log.Error("Processes", "Retrait de la liste de protection impossible", ex);
        }
    }

    public ProcessSnapshot? FindGameProcess(GameInfo game)
    {
        if (game is null) return null;

        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        AddCandidateName(names, game.ExecutablePath);
        foreach (var candidate in game.ExecutableCandidates ?? new List<string>())
            AddCandidateName(names, candidate);

        var installPath = game.InstallPath ?? string.Empty;
        var hasInstallPath = !string.IsNullOrWhiteSpace(installPath);

        if (names.Count == 0 && !hasInstallPath) return null;

        Process[] processes;
        try
        {
            processes = Process.GetProcesses();
        }
        catch (Exception ex)
        {
            Log.Error("Processes", "Enumeration des processus impossible", ex);
            return null;
        }

        ProcessSnapshot? best = null;
        long bestMemory = -1;
        try
        {
            foreach (var process in processes)
            {
                try
                {
                    var processName = SafeProcessName(process);
                    var path = ResolvePath(process, processName, null);
                    var nameMatch = !string.IsNullOrEmpty(processName) && names.Contains(processName);
                    var pathMatch = hasInstallPath && path.Length > 0 &&
                                    path.StartsWith(installPath, StringComparison.OrdinalIgnoreCase);
                    if (!nameMatch && !pathMatch) continue;

                    long memory = 0;
                    try
                    {
                        memory = process.WorkingSet64;
                    }
                    catch
                    {
                    }

                    if (memory <= bestMemory) continue;
                    var snapshot = BuildSnapshot(process, DateTime.UtcNow, null, null, cpuOverride: null);
                    if (snapshot is null) continue;
                    best = snapshot;
                    bestMemory = memory;
                }
                catch (Exception ex)
                {
                    Log.Warn("Processes", "Comparaison d'un processus impossible : " + ex.Message);
                }
            }
        }
        finally
        {
            foreach (var process in processes)
            {
                try
                {
                    process.Dispose();
                }
                catch
                {
                }
            }
        }

        return best;
    }

    public int? FindGameProcessId(GameInfo game) => FindGameProcess(game)?.ProcessId;

    public void AnnotateRunningGames(List<GameInfo> games)
    {
        if (games is null) return;
        foreach (var game in games)
        {
            if (game is null) continue;
            try
            {
                var process = FindGameProcess(game);
                game.IsRunning = process is not null;
                game.RunningProcessId = process?.ProcessId ?? 0;
            }
            catch (Exception ex)
            {
                Log.Warn("Processes", "Detection de « " + game.Name + " » impossible : " + ex.Message);
                game.IsRunning = false;
                game.RunningProcessId = 0;
            }
        }
    }

    private ProcessSnapshot? BuildSnapshot(Process process, DateTime now, Dictionary<int, double>? gpuByPid, HashSet<string>? seenPaths, double? cpuOverride = null)
    {
        int pid;
        try
        {
            pid = process.Id;
        }
        catch
        {
            return null;
        }

        var name = SafeProcessName(process);
        var path = ResolvePath(process, name, seenPaths);
        var details = GetPathDetails(path, name);

        long memory = 0;
        try
        {
            memory = process.WorkingSet64;
        }
        catch (Exception ex)
        {
            Log.Warn("Processes", "Memoire du processus " + pid + " indisponible : " + ex.Message);
        }

        var title = string.Empty;
        try
        {
            title = process.MainWindowTitle ?? string.Empty;
        }
        catch
        {
        }

        var startTime = default(DateTime);
        try
        {
            startTime = process.StartTime;
        }
        catch
        {
        }

        var handle = ProcessNative.OpenProcess(DesiredAccess, false, pid);
        try
        {
            var snapshot = new ProcessSnapshot
            {
                ProcessId = pid,
                Name = name,
                Title = title,
                Path = path,
                MemoryBytes = memory,
                StartTime = startTime,
                IsStoreApp = details.IsStoreApp,
                Publisher = details.Publisher,
                IconPath = details.IconPath,
                IsElevated = IsProcessElevated(pid, handle),
                NetworkBytesPerSec = 0,
                GpuDedicatedBytes = ReadVideoMemoryBytes(pid, handle),
                CpuPercent = cpuOverride ?? ComputeCpuPercent(pid, process, now)
            };

            if (handle != IntPtr.Zero && ProcessNative.GetProcessIoCounters(handle, out var counters))
            {
                var io = ComputeIo(pid, counters, now);
                snapshot.DiskReadBytesPerSec = io.Read;
                snapshot.DiskWriteBytesPerSec = io.Write;
            }

            if (gpuByPid is not null)
                snapshot.GpuPercent = gpuByPid.TryGetValue(pid, out var usage) ? usage : 0.0;

            snapshot.IsCritical = CriticalNames.Contains(NormalizeName(name)) || IsInNeverCloseList(name);
            return snapshot;
        }
        catch (Exception ex)
        {
            Log.Error("Processes", "Construction de l'instantane du processus " + pid + " impossible", ex);
            return null;
        }
        finally
        {
            if (handle != IntPtr.Zero)
            {
                try
                {
                    ProcessNative.CloseHandle(handle);
                }
                catch
                {
                }
            }
        }
    }

    private double ComputeCpuPercent(int pid, Process process, DateTime now)
    {
        TimeSpan total;
        try
        {
            total = process.TotalProcessorTime;
        }
        catch (Exception ex)
        {
            lock (_sync)
            {
                if (_accessFailureLogged.Add(pid))
                    Log.Debug("Processes", "Temps processeur du PID " + pid + " indisponible : " + ex.Message);
            }
            return 0;
        }

        double percent = 0;
        lock (_sync)
        {
            if (_cpuCache.TryGetValue(pid, out var previous))
            {
                var elapsed = (now - previous.At).TotalMilliseconds;
                if (elapsed >= MinimumDeltaMs)
                {
                    var used = (total - previous.Cpu).TotalMilliseconds;
                    percent = 100.0 * used / elapsed / Math.Max(1, Environment.ProcessorCount);
                }
            }
            _cpuCache[pid] = (total, now);
        }

        if (double.IsNaN(percent) || percent < 0) return 0;
        return percent > 100 ? 100 : percent;
    }

    private (double Read, double Write) ComputeIo(int pid, ProcessNative.IoCounters counters, DateTime now)
    {
        double read = 0;
        double write = 0;
        lock (_sync)
        {
            if (_ioCache.TryGetValue(pid, out var previous))
            {
                var elapsed = (now - previous.At).TotalMilliseconds;
                if (elapsed >= MinimumDeltaMs)
                {
                    var readDelta = counters.ReadTransferCount - previous.Io.ReadTransferCount;
                    var writeDelta = counters.WriteTransferCount - previous.Io.WriteTransferCount;
                    if (readDelta > 0) read = readDelta * 1000.0 / elapsed;
                    if (writeDelta > 0) write = writeDelta * 1000.0 / elapsed;
                }
            }
            _ioCache[pid] = (counters, now);
        }
        return (read, write);
    }

    private bool IsProcessElevated(int pid, IntPtr handle)
    {
        if (handle == IntPtr.Zero) return false;
        lock (_sync)
        {
            if (_elevation.TryGetValue(pid, out var cached)) return cached;
        }

        var elevated = false;
        IntPtr token = IntPtr.Zero;
        try
        {
            if (ProcessNative.OpenProcessToken(handle, ProcessNative.TokenQuery, out token))
            {
                var length = Marshal.SizeOf<ProcessNative.TokenElevation>();
                if (ProcessNative.GetTokenInformation(token, ProcessNative.TokenElevationClass, out var information, length, out _))
                    elevated = information.TokenIsElevated != 0;
            }
        }
        catch (Exception ex)
        {
            Log.Warn("Processes", "Niveau de privilège du PID " + pid + " indisponible : " + ex.Message);
        }
        finally
        {
            if (token != IntPtr.Zero)
            {
                try
                {
                    ProcessNative.CloseHandle(token);
                }
                catch
                {
                }
            }
        }

        lock (_sync)
        {
            _elevation[pid] = elevated;
        }
        return elevated;
    }

    private long? ReadVideoMemoryBytes(int pid, IntPtr handle)
    {
        if (!_videoMemoryAvailable || handle == IntPtr.Zero) return null;
        try
        {
            var status = ProcessNative.NtQueryVideoMemoryInfo(
                handle, 0, ProcessNative.MemorySegmentGroupLocal,
                out var information, Marshal.SizeOf<ProcessNative.VideoMemoryInformation>(), out _);
            if (status != 0) return null;
            var committed = information.CurrentCommitSize.ToUInt64();
            return committed <= long.MaxValue ? (long)committed : null;
        }
        catch (EntryPointNotFoundException ex)
        {
            if (DisableVideoMemoryOnce())
                Log.Warn("Processes", "Memoire video par processus indisponible sur ce systeme : " + ex.Message);
            return null;
        }
        catch (DllNotFoundException ex)
        {
            if (DisableVideoMemoryOnce())
                Log.Warn("Processes", "Memoire video par processus indisponible sur ce systeme : " + ex.Message);
            return null;
        }
        catch (Exception ex)
        {
            lock (_sync)
            {
                if (_accessFailureLogged.Add(pid))
                    Log.Debug("Processes", "Memoire video du PID " + pid + " impossible : " + ex.Message);
            }
            return null;
        }
    }

    private bool DisableVideoMemoryOnce()
    {
        lock (_sync)
        {
            if (!_videoMemoryAvailable) return false;
            _videoMemoryAvailable = false;
            return true;
        }
    }

    private Dictionary<int, double>? ReadGpuByPid()
    {
        lock (_sync)
        {
            if (_gpuUnavailable) return _gpuLast;
            if (_gpuQuery is null)
            {
                _gpuQuery = PerfQuery.TryCreate();
                if (_gpuQuery is null || !_gpuQuery.TryAddEnglish(@"\GPU Engine(*)\Utilization Percentage", out _gpuIndex))
                {
                    _gpuIndex = -1;
                    _gpuQuery?.Dispose();
                    _gpuQuery = null;
                    _gpuUnavailable = true;
                    Log.Warn("Processes", "Compteurs GPU par processus indisponibles");
                    return null;
                }
                _gpuQuery.Collect();
                _gpuPrimed = false;
            }

            if (_gpuLast is not null && (DateTime.UtcNow - _gpuStamp).TotalMilliseconds < GpuCacheMs)
                return _gpuLast;

            if (!_gpuQuery.Collect()) return _gpuLast;
            if (!_gpuPrimed)
            {
                _gpuPrimed = true;
                return null;
            }

            if (!_gpuQuery.TryRead(_gpuIndex, _buffer)) return _gpuLast;

            var map = new Dictionary<int, double>();
            foreach (var sample in _buffer)
            {
                var pid = ParsePid(sample.Instance);
                if (pid is not int processId) continue;
                if (!IsTrackedEngine(sample.Instance)) continue;
                var value = sample.Value;
                if (double.IsNaN(value) || value < 0) continue;
                var total = map.TryGetValue(processId, out var existing) ? existing + value : value;
                map[processId] = total > 100 ? 100 : total;
            }

            _gpuLast = map;
            _gpuStamp = DateTime.UtcNow;
            return map;
        }
    }

    private static int? ParsePid(string instance)
    {
        var parts = instance.Split('_');
        for (var i = 0; i < parts.Length - 1; i++)
        {
            if (!parts[i].Equals("pid", StringComparison.OrdinalIgnoreCase)) continue;
            var token = parts[i + 1];
            if (token.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            {
                return int.TryParse(token.AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var hex)
                    ? hex
                    : null;
            }
            return int.TryParse(token, NumberStyles.None, CultureInfo.InvariantCulture, out var value) ? value : null;
        }
        return null;
    }

    private static bool IsTrackedEngine(string instance)
    {
        var index = instance.IndexOf("engtype_", StringComparison.OrdinalIgnoreCase);
        if (index < 0) return false;
        var engineType = instance[(index + 8)..];
        return engineType.Equals("3D", StringComparison.OrdinalIgnoreCase) ||
               engineType.Equals("VideoDecode", StringComparison.OrdinalIgnoreCase) ||
               engineType.Equals("VideoEncode", StringComparison.OrdinalIgnoreCase) ||
               engineType.Equals("Copy", StringComparison.OrdinalIgnoreCase);
    }

    private string ResolvePath(Process process, string name, HashSet<string>? seenPaths)
    {
        var pid = -1;
        try
        {
            pid = process.Id;
        }
        catch
        {
            return string.Empty;
        }

        lock (_sync)
        {
            if (_pathByPid.TryGetValue(pid, out var cached)) return cached;
        }

        var path = string.Empty;
        try
        {
            path = process.MainModule?.FileName ?? string.Empty;
        }
        catch (Exception ex)
        {
            lock (_sync)
            {
                if (_accessFailureLogged.Add(pid))
                    Log.Debug("Processes", "Chemin du PID " + pid + " inaccessible : " + ex.Message);
            }
        }

        lock (_sync)
        {
            _pathByPid[pid] = path;
        }
        if (!string.IsNullOrEmpty(path)) seenPaths?.Add(path);
        return path;
    }

    private PathDetails GetPathDetails(string path, string name)
    {
        if (string.IsNullOrEmpty(path)) return PathDetails.Empty;

        lock (_sync)
        {
            if (_pathDetails.TryGetValue(path, out var cached)) return cached;
        }

        var details = new PathDetails();
        try
        {
            details.IsStoreApp = path.Contains(@"\WindowsApps\", StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex)
        {
            Log.Warn("Processes", "Analyse du chemin " + path + " impossible : " + ex.Message);
        }

        try
        {
            if (File.Exists(path))
            {
                var version = FileVersionInfo.GetVersionInfo(path);
                details.Publisher = version.CompanyName ?? string.Empty;
            }
        }
        catch (Exception ex)
        {
            Log.Warn("Processes", "Editeur de " + path + " inaccessible : " + ex.Message);
        }

        details.IconPath = ProcessIconCache.GetIconPath(name, path);

        lock (_sync)
        {
            _pathDetails[path] = details;
        }
        return details;
    }

    private void Purge(HashSet<int> pids, HashSet<string> paths)
    {
        lock (_sync)
        {
            foreach (var key in _cpuCache.Keys.Where(key => !pids.Contains(key)).ToList()) _cpuCache.Remove(key);
            foreach (var key in _ioCache.Keys.Where(key => !pids.Contains(key)).ToList()) _ioCache.Remove(key);
            foreach (var key in _pathByPid.Keys.Where(key => !pids.Contains(key)).ToList()) _pathByPid.Remove(key);
            foreach (var key in _elevation.Keys.Where(key => !pids.Contains(key)).ToList()) _elevation.Remove(key);
            foreach (var key in _pathDetails.Keys.Where(key => !paths.Contains(key)).ToList()) _pathDetails.Remove(key);
            foreach (var key in _accessFailureLogged.Where(key => !pids.Contains(key)).ToList()) _accessFailureLogged.Remove(key);
        }
    }

    private static void AddCandidateName(HashSet<string> names, string? candidate)
    {
        if (string.IsNullOrWhiteSpace(candidate)) return;
        var fileName = Path.GetFileNameWithoutExtension(candidate.Trim());
        if (!string.IsNullOrEmpty(fileName)) names.Add(fileName);
    }

    private static string SafeProcessName(Process process)
    {
        try
        {
            return process.ProcessName ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string NormalizeName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return string.Empty;
        var trimmed = name.Trim();
        var fileName = Path.GetFileName(trimmed);
        if (string.IsNullOrWhiteSpace(fileName)) return trimmed;
        return Path.GetFileNameWithoutExtension(fileName);
    }

    private static bool IsSystemPath(string? path) =>
        !string.IsNullOrEmpty(path) &&
        (path.Contains(@"\Windows\System32\", StringComparison.OrdinalIgnoreCase) ||
         path.Contains(@"\Windows\SysWOW64\", StringComparison.OrdinalIgnoreCase) ||
         path.Contains(@"\Windows\System32\", StringComparison.OrdinalIgnoreCase));

    private bool IsInNeverCloseList(string name)
    {
        if (string.IsNullOrEmpty(name)) return false;
        try
        {
            foreach (var entry in SettingsService.Current.NeverCloseApps)
            {
                if (string.Equals(NormalizeName(entry), name, StringComparison.OrdinalIgnoreCase)) return true;
            }
        }
        catch (Exception ex)
        {
            Log.Error("Processes", "Lecture de la liste de protection impossible", ex);
        }
        return false;
    }

    private sealed class PathDetails
    {
        public static readonly PathDetails Empty = new();
        public string Publisher = string.Empty;
        public bool IsStoreApp;
        public string IconPath = string.Empty;
    }
}
