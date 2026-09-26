using System.Globalization;
using System.Text.RegularExpressions;
using GameBoost.Core.Hardware;
using GameBoost.Core.Logging;
using GameBoost.Core.Models;
using Microsoft.Win32;

namespace GameBoost.Core.Disks;

public sealed class DiskAnalyzer
{
    private const string CimScope = @"root\cimv2";
    private const string StorageScope = @"root\Microsoft\Windows\Storage";

    private static readonly Lazy<DiskAnalyzer> LazyInstance = new(() => new DiskAnalyzer());

    public static DiskAnalyzer Instance => LazyInstance.Value;

    private DiskAnalyzer()
    {
    }

    public List<StorageInfo> Analyze() => AnalyzeCore(true);

    public List<StorageInfo> AnalyzeFast() => AnalyzeCore(false);

    private List<StorageInfo> AnalyzeCore(bool withSlowCounters)
    {
        var result = new List<StorageInfo>();
        try
        {
            var disks = LoadDisks();
            if (disks.Count == 0)
            {
                Log.Warn("Disks", "Aucun disque physique détecté");
                return result;
            }

            var volumes = LoadVolumes(out var bootDiskIds);
            var systemLetters = LoadSystemLetters();
            var systemDiskIds = ResolveSystemDiskIds(volumes, systemLetters, bootDiskIds);
            var reliability = withSlowCounters ? LoadReliability() : new Dictionary<int, Dictionary<string, object?>>();
            var throughput = withSlowCounters
                ? DiskThroughput.Sample()
                : new Dictionary<int, (double ReadMBs, double WriteMBs)>();

            foreach (var disk in disks.Values.OrderBy(d => d.Id))
            {
                try
                {
                    result.Add(BuildStorageInfo(disk, volumes, systemLetters, systemDiskIds, reliability, throughput));
                }
                catch (Exception ex)
                {
                    Log.Error("Disks", "Lecture impossible du disque " + disk.Id, ex);
                }
            }
        }
        catch (Exception ex)
        {
            Log.Error("Disks", "Analyse du stockage impossible", ex);
        }
        return result;
    }

    private static StorageInfo BuildStorageInfo(
        DiskRecord disk,
        List<VolumeRecord> volumes,
        HashSet<string> systemLetters,
        HashSet<int> systemDiskIds,
        Dictionary<int, Dictionary<string, object?>> reliability,
        Dictionary<int, (double ReadMBs, double WriteMBs)> throughput)
    {
        var info = new StorageInfo
        {
            Model = disk.Model,
            Manufacturer = disk.Manufacturer,
            Interface = disk.InterfaceType,
            BusType = MapBusType(disk.BusTypeRaw),
            MediaType = MapMediaType(disk.MediaTypeRaw),
            SizeBytes = disk.SizeBytes,
            SerialNumber = disk.SerialNumber,
            IsSystemDisk = systemDiskIds.Contains(disk.Id),
            SmartStatus = disk.HealthRaw is null ? "Non disponible" : MapHealthStatus(disk.HealthRaw.Value)
        };

        foreach (var volume in volumes.Where(v => v.DiskIndex == disk.Id))
        {
            info.Volumes.Add(new VolumeInfo
            {
                Letter = volume.Letter,
                FileSystem = volume.FileSystem,
                SizeBytes = volume.SizeBytes,
                FreeBytes = volume.FreeBytes,
                IsSystem = systemLetters.Contains(volume.Letter)
            });
        }

        AppendHealthWarnings(info, disk);
        if (reliability.TryGetValue(disk.Id, out var counters))
            AppendReliability(info, counters);
        if (throughput.TryGetValue(disk.Id, out var speed))
        {
            info.ReadSpeedMBs = speed.ReadMBs;
            info.WriteSpeedMBs = speed.WriteMBs;
        }
        return info;
    }

    private static void AppendHealthWarnings(StorageInfo info, DiskRecord disk)
    {
        if (disk.HealthRaw is null)
            info.SmartWarnings.Add("Santé SMART non communiquée par le disque (donnée indisponible).");
        else if (disk.HealthRaw == 0)
            info.SmartWarnings.Add("État de santé inconnu (HealthStatus = 0).");
        else if (disk.HealthRaw == 2)
            info.SmartWarnings.Add("État de santé en alerte (HealthStatus = 2).");
        else if (disk.HealthRaw == 3)
            info.SmartWarnings.Add("État de santé critique (HealthStatus = 3).");
        else if (disk.HealthRaw != 1)
            info.SmartWarnings.Add("État de santé non reconnu (HealthStatus = " + disk.HealthRaw.Value.ToString(CultureInfo.CurrentCulture) + ").");

        if (disk.OperationalStatus.Count > 0 && !disk.OperationalStatus.Contains(0) && !disk.OperationalStatus.Contains(2))
            info.SmartWarnings.Add("État opérationnel anormal : " + string.Join(", ", disk.OperationalStatus) + ".");
    }

    private static void AppendReliability(StorageInfo info, Dictionary<string, object?> counters)
    {
        var temperature = WmiHelper.Real(counters, "Temperature");
        if (temperature is not null) info.TemperatureC = temperature;

        var wear = WmiHelper.Real(counters, "Wear");
        if (wear is not null)
        {
            var value = Math.Clamp(wear.Value, 0, 100);
            info.HealthPercent = (int)Math.Round(100 - value);
            info.SmartWarnings.Add((value > 20 ? "Usure du disque élevée : " : "Usure du disque : ") +
                                   value.ToString("0", CultureInfo.CurrentCulture) + " %.");
        }

        var hours = WmiHelper.Number(counters, "PowerOnHours");
        if (hours is not null)
            info.SmartWarnings.Add("Durée de fonctionnement : " + hours.Value.ToString("N0", CultureInfo.CurrentCulture) + " h.");

        var readErrors = WmiHelper.Number(counters, "ReadErrorsTotal", "ReadErrorTotal");
        if (readErrors is > 0)
            info.SmartWarnings.Add("Erreurs de lecture cumulées détectées : " + readErrors.Value.ToString("N0", CultureInfo.CurrentCulture) + ".");

        var writeErrors = WmiHelper.Number(counters, "WriteErrorsTotal");
        if (writeErrors is > 0)
            info.SmartWarnings.Add("Erreurs d'écriture cumulées détectées : " + writeErrors.Value.ToString("N0", CultureInfo.CurrentCulture) + ".");
    }

    private static Dictionary<int, DiskRecord> LoadDisks()
    {
        var disks = new Dictionary<int, DiskRecord>();
        foreach (var row in WmiHelper.Query(StorageScope, "SELECT * FROM MSFT_PhysicalDisk"))
        {
            var id = WmiHelper.Number(row, "DeviceId");
            if (id is null)
            {
                Log.Warn("Disks", "MSFT_PhysicalDisk sans identifiant ignoré");
                continue;
            }
            var record = new DiskRecord
            {
                Id = (int)id.Value,
                Model = WmiHelper.Text(row, "Inconnu", "FriendlyName", "Model"),
                Manufacturer = WmiHelper.Text(row, "Inconnu", "Manufacturer"),
                InterfaceType = "Inconnu",
                BusTypeRaw = (int?)WmiHelper.Number(row, "BusType"),
                MediaTypeRaw = (int?)WmiHelper.Number(row, "MediaType"),
                HealthRaw = (int?)WmiHelper.Number(row, "HealthStatus"),
                OperationalStatus = WmiHelper.IntList(row, "OperationalStatus"),
                SizeBytes = WmiHelper.Number(row, "Size") ?? 0,
                SerialNumber = WmiHelper.Text(row, string.Empty, "SerialNumber").Trim()
            };
            if (string.IsNullOrWhiteSpace(record.Manufacturer)) record.Manufacturer = "Inconnu";
            disks[record.Id] = record;
        }

        foreach (var row in WmiHelper.Query(CimScope, "SELECT * FROM Win32_DiskDrive"))
        {
            var id = WmiHelper.Number(row, "Index");
            if (id is null) continue;
            var index = (int)id.Value;
            if (!disks.TryGetValue(index, out var record))
            {
                record = new DiskRecord { Id = index };
                disks[index] = record;
            }

            var model = WmiHelper.Text(row, "Inconnu", "Model", "Name");
            if (record.Model == "Inconnu" && model != "Inconnu") record.Model = model;
            if (record.InterfaceType == "Inconnu")
            {
                var interfaceType = WmiHelper.Text(row, "Inconnu", "InterfaceType");
                if (interfaceType != "Inconnu") record.InterfaceType = interfaceType;
            }
            if (record.SizeBytes <= 0) record.SizeBytes = WmiHelper.Number(row, "Size") ?? 0;
            if (record.SerialNumber.Length == 0)
                record.SerialNumber = WmiHelper.Text(row, string.Empty, "SerialNumber").Trim();
        }

        if (disks.Count == 0) Log.Warn("Disks", "Aucun disque retourné par WMI");
        return disks;
    }

    private static List<VolumeRecord> LoadVolumes(out HashSet<int> bootDiskIds)
    {
        var volumes = new List<VolumeRecord>();
        bootDiskIds = new HashSet<int>();
        var partitions = new Dictionary<string, (int DiskIndex, bool Boot)>(StringComparer.OrdinalIgnoreCase);
        var letterToPartition = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var row in WmiHelper.Query(CimScope, "SELECT * FROM Win32_DiskPartition"))
        {
            var id = WmiHelper.Text(row, string.Empty, "DeviceID").Trim();
            var index = WmiHelper.Number(row, "DiskIndex");
            if (id.Length == 0 || index is null) continue;
            var boot = WmiHelper.Flag(row, false, "BootPartition");
            partitions[id] = ((int)index.Value, boot);
            if (boot) bootDiskIds.Add((int)index.Value);
        }

        foreach (var row in WmiHelper.Query(CimScope, "SELECT * FROM Win32_LogicalDiskToPartition"))
        {
            var partitionId = ExtractQuoted(WmiHelper.Text(row, string.Empty, "Antecedent"));
            var target = ExtractQuoted(WmiHelper.Text(row, string.Empty, "Dependent"));
            if (partitionId is null || target is null) continue;
            if (!target.EndsWith(":", StringComparison.Ordinal)) continue;
            letterToPartition[target] = partitionId;
        }

        foreach (var row in WmiHelper.Query(CimScope, "SELECT * FROM Win32_LogicalDisk"))
        {
            var letter = WmiHelper.Text(row, string.Empty, "DeviceID", "DriveLetter").Trim();
            if (!letter.EndsWith(":", StringComparison.Ordinal)) continue;
            var diskIndex = -1;
            var boot = false;
            if (letterToPartition.TryGetValue(letter, out var partitionId) && partitions.TryGetValue(partitionId, out var partition))
            {
                diskIndex = partition.DiskIndex;
                boot = partition.Boot;
            }
            if (diskIndex < 0) Log.Warn("Disks", "Volume " + letter + " non associé à un disque physique");
            volumes.Add(new VolumeRecord
            {
                Letter = letter,
                FileSystem = WmiHelper.Text(row, string.Empty, "FileSystem").Trim(),
                SizeBytes = WmiHelper.Number(row, "Size") ?? 0,
                FreeBytes = WmiHelper.Number(row, "FreeSpace") ?? 0,
                DiskIndex = diskIndex,
                BootPartition = boot
            });
        }

        if (volumes.Count == 0) Log.Warn("Disks", "Aucun volume logique détecté");
        return volumes;
    }

    private static HashSet<string> LoadSystemLetters()
    {
        var letters = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var windowsRoot = Path.GetPathRoot(Environment.GetFolderPath(Environment.SpecialFolder.Windows));
            if (!string.IsNullOrWhiteSpace(windowsRoot)) letters.Add(windowsRoot.TrimEnd('\\'));

            using var key = Registry.LocalMachine.OpenSubKey(
                @"SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management");
            if (key?.GetValue("PagingFiles") is string[] entries)
            {
                foreach (var entry in entries)
                {
                    if (string.IsNullOrWhiteSpace(entry)) continue;
                    var path = entry.Trim().Split(' ', '\t')[0].Trim();
                    if (path.Length < 2 || path[1] != ':') continue;
                    if (path[0] == '?') continue;
                    letters.Add(path[..2]);
                }
            }
        }
        catch (Exception ex)
        {
            Log.Warn("Disks", "Détection des volumes système impossible : " + ex.Message);
        }
        return letters;
    }

    private static HashSet<int> ResolveSystemDiskIds(List<VolumeRecord> volumes, HashSet<string> systemLetters, HashSet<int> bootDiskIds)
    {
        var ids = new HashSet<int>();
        foreach (var volume in volumes)
        {
            if (volume.DiskIndex >= 0 && systemLetters.Contains(volume.Letter)) ids.Add(volume.DiskIndex);
        }
        if (ids.Count > 0) return ids;

        foreach (var id in bootDiskIds) ids.Add(id);
        if (ids.Count == 0)
            Log.Warn("Disks", "Disque système non identifié");
        else
            Log.Warn("Disks", "Disque système identifié via la partition de démarrage");
        return ids;
    }

    private static Dictionary<int, Dictionary<string, object?>> LoadReliability()
    {
        var map = new Dictionary<int, Dictionary<string, object?>>();
        foreach (var row in WmiHelper.Query(StorageScope, "SELECT * FROM MSFT_StorageReliabilityCounter"))
        {
            var id = WmiHelper.Number(row, "DeviceId");
            if (id is null) continue;
            map[(int)id.Value] = row;
        }
        if (map.Count == 0)
            Log.Warn("Disks", "Compteurs de fiabilité SMART indisponibles (droits insuffisants ou matériel non compatible)");
        return map;
    }

    private static string? ExtractQuoted(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var match = Regex.Match(text, "\"([^\"]+)\"");
        return match.Success ? match.Groups[1].Value : null;
    }

    private static string MapBusType(int? busType) => busType switch
    {
        1 => "SCSI",
        2 => "ATA",
        3 => "SATA",
        4 => "NVMe",
        5 => "RAID",
        6 => "FC",
        7 => "USB",
        8 => "RAID",
        9 => "iSCSI",
        10 => "SAS",
        11 => "SATA",
        12 => "UFS",
        13 => "MMC",
        14 => "NVMe",
        15 => "SCM",
        16 => "UFS",
        _ => "Inconnu"
    };

    private static DiskMediaType MapMediaType(int? mediaType) => mediaType switch
    {
        3 => DiskMediaType.Hdd,
        4 => DiskMediaType.Ssd,
        _ => DiskMediaType.Unknown
    };

    private static string MapHealthStatus(int healthStatus) => healthStatus switch
    {
        1 => "Sain",
        2 => "Attention",
        3 => "Critique",
        _ => "Inconnu"
    };

    private sealed class DiskRecord
    {
        public int Id { get; set; }
        public string Model { get; set; } = "Inconnu";
        public string Manufacturer { get; set; } = "Inconnu";
        public string InterfaceType { get; set; } = "Inconnu";
        public int? BusTypeRaw { get; set; }
        public int? MediaTypeRaw { get; set; }
        public int? HealthRaw { get; set; }
        public List<int> OperationalStatus { get; set; } = new();
        public long SizeBytes { get; set; }
        public string SerialNumber { get; set; } = string.Empty;
    }

    private sealed class VolumeRecord
    {
        public string Letter { get; set; } = string.Empty;
        public string FileSystem { get; set; } = string.Empty;
        public long SizeBytes { get; set; }
        public long FreeBytes { get; set; }
        public int DiskIndex { get; set; } = -1;
        public bool BootPartition { get; set; }
    }
}
