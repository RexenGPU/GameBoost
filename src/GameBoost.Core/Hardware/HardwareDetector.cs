using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using GameBoost.Core.Data;
using GameBoost.Core.Disks;
using GameBoost.Core.Logging;
using GameBoost.Core.Models;
using Microsoft.Win32;

namespace GameBoost.Core.Hardware;

public sealed class HardwareDetector
{
    private const string CimScope = @"root\cimv2";

    private static readonly HashSet<string> ExcludedPeripheralClasses = new(StringComparer.OrdinalIgnoreCase)
    {
        "System", "Processor", "DiskDrive", "Volume", "VolumeSnapshot", "SCSIAdapter", "HDC", "Computer",
        "Net", "SoftwareDevice", "SoftwareComponent", "SecurityDevices", "PrintQueue", "Ports", "Monitor",
        "Display", "BIOS", "Firmware", "Battery", "FireWire"
    };

    private static readonly HashSet<string> KeptPeripheralClasses = new(StringComparer.OrdinalIgnoreCase)
    {
        "Keyboard", "Mouse", "HIDClass", "MEDIA", "AudioEndpoint", "AudioProcessingObject", "Audio",
        "USB", "Bluetooth", "WPD"
    };

    private static readonly string[] GamepadDenyList =
    {
        "host", "hôte", "policy", "smbus", "sm bus", "storage", "stockage", "serial", "serie", "parallel",
        "hub", "root", "racine", "ohci", "ehci", "xhci", "ahci", "express", "acpi", "pci", "système",
        "system", "composite", "hébergeur"
    };

    private static readonly string[] PeripheralKindOrder =
    {
        "Manette", "Clavier", "Souris", "Casque", "Audio", "USB", "Autre"
    };

    private static readonly Lazy<HardwareDetector> LazyInstance = new(() => new HardwareDetector());

    public static HardwareDetector Instance => LazyInstance.Value;

    private HardwareDetector()
    {
    }

    public HardwareReport Collect()
    {
        var report = new HardwareReport();
        report.Cpu = Safe(GetCpu, "processeur", new CpuInfo());
        report.Ram = Safe(GetRam, "mémoire vive", new RamInfo());
        report.Motherboard = Safe(GetMotherboard, "carte mère", new MotherboardInfo());
        report.Os = Safe(GetOsInfo, "système d'exploitation", new OsInfo());
        report.DirectXVersion = Safe(GetDirectXVersion, "DirectX", "Inconnue");
        report.Displays = Safe(GetDisplays, "écrans", new List<DisplayInfo>());
        report.Gpu = Safe(() => GetGpu(report.Displays), "carte graphique", new GpuInfo());
        report.Peripherals = Safe(GetPeripherals, "périphériques", new List<PeripheralInfo>());
        report.Storage = Safe(() => DiskAnalyzer.Instance.Analyze(), "stockage", new List<StorageInfo>());
        report.CollectedAt = DateTime.Now;
        return report;
    }

    public Task<HardwareReport> CollectAsync() => Task.Run(Collect);

    public List<DisplayInfo> GetDisplays()
    {
        try
        {
            var displays = DisplayNative.Enumerate();
            if (displays.Count == 0) Log.Warn("Hardware", "Aucun écran détecté");
            return displays;
        }
        catch (Exception ex)
        {
            Log.Error("Hardware", "Détection des écrans impossible", ex);
            return new List<DisplayInfo>();
        }
    }

    public string GetDirectXVersion()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\DirectX");
            if (key is not null)
            {
                var version = key.GetValue("DirectXVersion") as string;
                if (!string.IsNullOrWhiteSpace(version))
                {
                    var trimmed = version.Trim();
                    return trimmed.StartsWith("DirectX", StringComparison.OrdinalIgnoreCase)
                        ? trimmed
                        : "DirectX " + trimmed;
                }

                var legacy = key.GetValue("Version") as string;
                if (!string.IsNullOrWhiteSpace(legacy) && Version.TryParse(legacy.Trim(), out var parsed) && parsed.Major >= 9)
                    return "DirectX " + legacy.Trim();
            }
        }
        catch (Exception ex)
        {
            Log.Warn("Hardware", "Lecture de la version DirectX impossible : " + ex.Message);
        }

        var system = Environment.GetFolderPath(Environment.SpecialFolder.System);
        foreach (var candidate in new[] { ("d3d12.dll", "DirectX 12"), ("d3d11.dll", "DirectX 11") })
        {
            var path = Path.Combine(system, candidate.Item1);
            if (!File.Exists(path)) continue;
            try
            {
                var fileVersion = FileVersionInfo.GetVersionInfo(path).FileVersion;
                if (!string.IsNullOrWhiteSpace(fileVersion))
                    Log.Debug("Hardware", candidate.Item1 + " version " + fileVersion);
            }
            catch (Exception ex)
            {
                Log.Warn("Hardware", "Version de " + candidate.Item1 + " illisible : " + ex.Message);
            }
            return candidate.Item2;
        }

        Log.Warn("Hardware", "Version de DirectX introuvable");
        return "Inconnue";
    }

    public List<PeripheralInfo> GetPeripherals()
    {
        var peripherals = new List<PeripheralInfo>();
        try
        {
            var rows = WmiHelper.Query(CimScope, "SELECT * FROM Win32_PnPEntity");
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var row in rows)
            {
                var name = WmiHelper.Text(row, string.Empty, "Name").Trim();
                if (name.Length == 0) continue;
                var pnpClass = WmiHelper.Text(row, string.Empty, "PNPClass").Trim();
                if (ExcludedPeripheralClasses.Contains(pnpClass)) continue;
                if (IsBluetoothProfile(name, pnpClass)) continue;
                var kind = ClassifyPeripheral(name, pnpClass);
                if (kind == "Autre" && !KeptPeripheralClasses.Contains(pnpClass)) continue;
                if (!seen.Add(name)) continue;
                peripherals.Add(new PeripheralInfo
                {
                    Name = name,
                    Kind = kind,
                    PnpDeviceId = WmiHelper.Text(row, string.Empty, "PNPDeviceID")
                });
            }

            peripherals = peripherals
                .OrderBy(p => Array.IndexOf(PeripheralKindOrder, p.Kind) < 0 ? PeripheralKindOrder.Length : Array.IndexOf(PeripheralKindOrder, p.Kind))
                .ThenBy(p => p.Name, StringComparer.CurrentCulture)
                .Take(40)
                .ToList();
            if (peripherals.Count == 0) Log.Warn("Hardware", "Aucun périphérique détecté");
        }
        catch (Exception ex)
        {
            Log.Error("Hardware", "Détection des périphériques impossible", ex);
        }
        return peripherals;
    }

    public OsInfo GetOsInfo()
    {
        var info = new OsInfo();
        try
        {
            var rows = WmiHelper.Query(CimScope, "SELECT * FROM Win32_OperatingSystem");
            if (rows.Count > 0)
            {
                var row = rows[0];
                info.Caption = WmiHelper.Text(row, "Inconnu", "Caption");
                info.Version = WmiHelper.Text(row, "Inconnue", "Version");
                info.Build = WmiHelper.Text(row, "Inconnue", "BuildNumber");
                info.Architecture = WmiHelper.Text(row, "Inconnue", "OSArchitecture");
                info.Edition = ReadEdition(row);
            }
            else
            {
                Log.Warn("Hardware", "Win32_OperatingSystem ne retourne aucune donnée");
            }

            var boot = rows.Count > 0 ? WmiHelper.Date(rows[0], "LastBootUpTime") : null;
            if (boot is null || boot.Value > DateTime.Now || boot.Value < DateTime.Now.AddDays(-3650))
                boot = DateTime.Now - TimeSpan.FromMilliseconds(NativeMethods.GetTickCount64());
            info.BootTime = boot.Value;
            info.Uptime = DateTime.Now - info.BootTime;
            if (info.Uptime < TimeSpan.Zero) info.Uptime = TimeSpan.Zero;
            info.GameModeEnabled = ReadGameModeEnabled();
            info.IsElevated = AppPaths.IsElevated;
        }
        catch (Exception ex)
        {
            Log.Error("Hardware", "Collecte du système d'exploitation impossible", ex);
        }
        return info;
    }

    private static CpuInfo GetCpu()
    {
        var info = new CpuInfo();
        var rows = WmiHelper.Query(CimScope, "SELECT * FROM Win32_Processor");
        if (rows.Count == 0)
        {
            Log.Warn("Hardware", "Win32_Processor ne retourne aucune donnée");
            return info;
        }

        foreach (var row in rows)
        {
            info.PhysicalCores += (int)(WmiHelper.Number(row, "NumberOfCores") ?? 0);
            info.LogicalCores += (int)(WmiHelper.Number(row, "NumberOfLogicalProcessors") ?? 0);
        }

        var first = rows[0];
        info.Name = WmiHelper.Text(first, "Inconnu", "Name");
        info.Manufacturer = WmiHelper.Text(first, "Inconnu", "Manufacturer");
        info.BaseClockMHz = WmiHelper.Number(first, "CurrentClockSpeed");
        info.MaxClockMHz = WmiHelper.Number(first, "MaxClockSpeed");
        info.Architecture = MapArchitecture(WmiHelper.Number(first, "Architecture"));
        info.Socket = WmiHelper.Text(first, "Inconnu", "SocketDesignation");
        info.Description = WmiHelper.Text(first, string.Empty, "Description");
        return info;
    }

    private static RamInfo GetRam()
    {
        var info = new RamInfo();
        foreach (var row in WmiHelper.Query(CimScope, "SELECT * FROM Win32_PhysicalMemory"))
        {
            var module = new RamModule
            {
                CapacityBytes = WmiHelper.Number(row, "Capacity") ?? 0,
                SpeedMHz = WmiHelper.Number(row, "Speed") ?? 0,
                Manufacturer = NormalizeName(WmiHelper.Text(row, "Inconnu", "Manufacturer")),
                PartNumber = NormalizeName(WmiHelper.Text(row, "Inconnu", "PartNumber")),
                Slot = NormalizeName(WmiHelper.Text(row, "Inconnu", "DeviceLocator")),
                FormFactor = MapFormFactor(WmiHelper.Number(row, "FormFactor"))
            };
            info.Modules.Add(module);
            if (module.SpeedMHz > info.SpeedMHz) info.SpeedMHz = module.SpeedMHz;
        }

        info.FormFactor = info.Modules.Count > 0 ? info.Modules[0].FormFactor : "Inconnu";
        if (NativeMethods.TryGetMemoryStatus(out var total, out var available))
        {
            info.TotalBytes = (long)total;
            info.AvailableBytes = (long)available;
        }
        else
        {
            Log.Warn("Hardware", "GlobalMemoryStatusEx a échoué (code " + Marshal.GetLastWin32Error() + ")");
        }
        if (info.TotalBytes == 0) Log.Warn("Hardware", "Mémoire physique totale indisponible");
        return info;
    }

    private static MotherboardInfo GetMotherboard()
    {
        var info = new MotherboardInfo();
        var boards = WmiHelper.Query(CimScope, "SELECT * FROM Win32_BaseBoard");
        if (boards.Count > 0)
        {
            info.Manufacturer = WmiHelper.Text(boards[0], "Inconnu", "Manufacturer");
            info.Product = WmiHelper.Text(boards[0], "Inconnue", "Product");
            info.Version = WmiHelper.Text(boards[0], "Inconnue", "Version");
            info.SerialNumber = WmiHelper.Text(boards[0], string.Empty, "SerialNumber");
        }
        else
        {
            Log.Warn("Hardware", "Win32_BaseBoard ne retourne aucune donnée");
        }

        var bios = WmiHelper.Query(CimScope, "SELECT * FROM Win32_BIOS");
        if (bios.Count > 0)
        {
            info.BiosVersion = WmiHelper.Text(bios[0], "Inconnue", "SMBIOSBIOSVersion");
            info.BiosDate = WmiHelper.DateText(bios[0], "Inconnue", "ReleaseDate");
        }
        else
        {
            Log.Warn("Hardware", "Win32_BIOS ne retourne aucune donnée");
        }
        return info;
    }

    private GpuInfo GetGpu(List<DisplayInfo> displays)
    {
        var adapters = LoadGpuAdapters();
        if (adapters.Count == 0)
        {
            Log.Warn("Hardware", "Aucun contrôleur vidéo détecté");
            return new GpuInfo();
        }

        var primaryDisplayName = displays.FirstOrDefault(d => d.Primary && !string.IsNullOrWhiteSpace(d.GpuName))?.GpuName ?? string.Empty;
        var primary = SelectPrimary(adapters, primaryDisplayName);
        foreach (var adapter in adapters)
            adapter.IsPrimary = ReferenceEquals(adapter, primary);

        return new GpuInfo
        {
            Name = primary.Name,
            Vendor = primary.Vendor,
            DriverVersion = primary.DriverVersion,
            DriverDate = primary.DriverDate,
            DedicatedVramBytes = primary.DedicatedVramBytes,
            PnpDeviceId = primary.PnpDeviceId,
            Status = primary.Status,
            IsPrimary = true,
            Capabilities = primary.Capabilities,
            AllAdapters = adapters
        };
    }

    private static List<GpuInfo> LoadGpuAdapters()
    {
        var adapters = new List<GpuInfo>();
        foreach (var row in WmiHelper.Query(CimScope, "SELECT * FROM Win32_VideoController"))
        {
            try
            {
                var name = WmiHelper.Text(row, string.Empty, "Name").Trim();
                if (name.Length == 0)
                {
                    Log.Warn("Hardware", "Contrôleur vidéo sans nom ignoré");
                    continue;
                }
                var pnpDeviceId = WmiHelper.Text(row, string.Empty, "PNPDeviceID");
                var vendor = DetectGpuVendor(pnpDeviceId, name);
                adapters.Add(new GpuInfo
                {
                    Name = name,
                    Vendor = vendor,
                    DriverVersion = WmiHelper.Text(row, "Inconnue", "DriverVersion"),
                    DriverDate = WmiHelper.DateText(row, "Inconnue", "DriverDate"),
                    DedicatedVramBytes = ReadVramBytes(pnpDeviceId, name, row),
                    PnpDeviceId = pnpDeviceId,
                    Status = WmiHelper.Text(row, string.Empty, "Status"),
                    Capabilities = GpuCapabilitiesDetector.Detect(name, vendor)
                });
            }
            catch (Exception ex)
            {
                Log.Error("Hardware", "Lecture d'un contrôleur vidéo impossible", ex);
            }
        }
        return adapters;
    }

    private static GpuInfo SelectPrimary(List<GpuInfo> adapters, string primaryDisplayName)
    {
        if (adapters.Count == 1) return adapters[0];
        var best = adapters[0];
        var bestScore = int.MinValue;
        foreach (var adapter in adapters)
        {
            var drivesDisplay = !string.IsNullOrWhiteSpace(primaryDisplayName) &&
                                string.Equals(adapter.Name, primaryDisplayName, StringComparison.OrdinalIgnoreCase);
            var score = Score(adapter, drivesDisplay);
            if (score <= bestScore) continue;
            bestScore = score;
            best = adapter;
        }
        return best;
    }

    private static int Score(GpuInfo gpu, bool drivesPrimaryDisplay)
    {
        var score = gpu.Vendor switch
        {
            GpuVendor.Nvidia => 100,
            GpuVendor.Amd => 60,
            GpuVendor.Intel => 30,
            GpuVendor.Other => 20,
            _ => 10
        };
        var name = gpu.Name.ToLowerInvariant();
        if (name.Contains("virtual") || name.Contains("basic display") || name.Contains("microsoft basic")) score -= 200;
        if (drivesPrimaryDisplay) score += 5;
        return score;
    }

    private static GpuVendor DetectGpuVendor(string pnpDeviceId, string name)
    {
        if (!string.IsNullOrWhiteSpace(pnpDeviceId))
        {
            var id = pnpDeviceId.ToUpperInvariant();
            if (id.Contains("VEN_10DE")) return GpuVendor.Nvidia;
            if (id.Contains("VEN_1002")) return GpuVendor.Amd;
            if (id.Contains("VEN_8086")) return GpuVendor.Intel;
        }

        if (string.IsNullOrWhiteSpace(name)) return GpuVendor.Unknown;
        if (name.Contains("nvidia", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("geforce", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("rtx", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("gtx", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("quadro", StringComparison.OrdinalIgnoreCase))
            return GpuVendor.Nvidia;
        if (name.Contains("radeon", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("amd", StringComparison.OrdinalIgnoreCase))
            return GpuVendor.Amd;
        if (name.Contains("intel", StringComparison.OrdinalIgnoreCase)) return GpuVendor.Intel;
        return GpuVendor.Other;
    }

    private static long ReadVramBytes(string pnpDeviceId, string name, IReadOnlyDictionary<string, object?> row)
    {
        var fromRegistry = ReadVramFromRegistry(pnpDeviceId, name);
        if (fromRegistry is > 0) return fromRegistry.Value;

        var adapterRam = WmiHelper.Number(row, "AdapterRAM");
        if (adapterRam is null || adapterRam.Value <= 0) return 0;
        if (adapterRam.Value >= 0xFFFFF000L)
        {
            Log.Warn("Hardware", "AdapterRAM de " + name + " est une valeur 32 bits non fiable, mémoire vidéo inconnue");
            return 0;
        }
        return Math.Min(adapterRam.Value, 4L * 1024 * 1024 * 1024 - 1);
    }

    private static long? ReadVramFromRegistry(string pnpDeviceId, string name)
    {
        const string classPath = @"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}";
        try
        {
            using var classKey = Registry.LocalMachine.OpenSubKey(classPath);
            if (classKey is null)
            {
                Log.Warn("Hardware", "Clé de registre des adaptateurs vidéo absente");
                return null;
            }

            foreach (var subKeyName in classKey.GetSubKeyNames())
            {
                if (!Regex.IsMatch(subKeyName, "^[0-9]{4}$")) continue;
                using var subKey = classKey.OpenSubKey(subKeyName);
                if (subKey is null) continue;

                var matching = (subKey.GetValue("MatchingDeviceId") as string)?.Trim();
                var description = (subKey.GetValue("DriverDesc") as string)?.Trim();
                var matched = false;
                if (!string.IsNullOrEmpty(matching) && !string.IsNullOrWhiteSpace(pnpDeviceId))
                    matched = pnpDeviceId.Trim().StartsWith(matching, StringComparison.OrdinalIgnoreCase);
                if (!matched && !string.IsNullOrEmpty(description))
                    matched = string.Equals(description, name.Trim(), StringComparison.OrdinalIgnoreCase);
                if (!matched) continue;

                var value = subKey.GetValue("HardwareInformation.qwMemorySize")
                            ?? subKey.GetValue("HardwareInformation.MemorySize")
                            ?? subKey.GetValue("HardwareInformation.AdapterRAM");
                var bytes = ToInt64(value);
                if (bytes is > 0) return bytes;
            }
        }
        catch (Exception ex)
        {
            Log.Warn("Hardware", "Lecture de la mémoire vidéo dédiée impossible : " + ex.Message);
        }
        return null;
    }

    private static long? ToInt64(object? value)
    {
        switch (value)
        {
            case null:
                return null;
            case long number:
                return number;
            case int number:
                return number;
            case uint number:
                return number;
            case byte[] raw when raw.Length is 4 or 8:
                return raw.Length == 4 ? BitConverter.ToUInt32(raw, 0) : BitConverter.ToInt64(raw, 0);
            case string raw:
                return long.TryParse(raw.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
                    ? parsed
                    : null;
            default:
                return null;
        }
    }

    private static string ClassifyPeripheral(string name, string pnpClass)
    {
        var lower = name.ToLowerInvariant();
        if (IsGamepadName(lower)) return "Manette";
        if (string.Equals(pnpClass, "Keyboard", StringComparison.OrdinalIgnoreCase) ||
            lower.Contains("clavier") || lower.Contains("keyboard"))
            return "Clavier";
        if (string.Equals(pnpClass, "Mouse", StringComparison.OrdinalIgnoreCase) ||
            lower.Contains("souris") || lower.Contains("mouse") || lower.Contains("trackball"))
            return "Souris";
        if (lower.Contains("casque") || lower.Contains("headset") || lower.Contains("écouteur") ||
            lower.Contains("ecouteur") || lower.Contains("earphone") || lower.Contains("earbud"))
            return "Casque";
        if (IsAudioClass(pnpClass) || lower.Contains("audio") || lower.Contains("haut-parleur") ||
            lower.Contains("haut parleur") || lower.Contains("speaker") || lower.Contains("microphone") ||
            lower.Contains("media"))
            return "Audio";
        if (string.Equals(pnpClass, "USB", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(pnpClass, "WPD", StringComparison.OrdinalIgnoreCase) ||
            lower.Contains("usb"))
            return "USB";
        return "Autre";
    }

    private static bool IsBluetoothProfile(string name, string pnpClass)
    {
        if (!string.Equals(pnpClass, "Bluetooth", StringComparison.OrdinalIgnoreCase)) return false;
        return name.StartsWith("Profil", StringComparison.OrdinalIgnoreCase) ||
               name.StartsWith("Service", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsAudioClass(string pnpClass) =>
        string.Equals(pnpClass, "MEDIA", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(pnpClass, "AudioEndpoint", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(pnpClass, "AudioProcessingObject", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(pnpClass, "Audio", StringComparison.OrdinalIgnoreCase);

    private static bool IsGamepadName(string lower)
    {
        if (lower.Contains("xbox") || lower.Contains("dualsense") || lower.Contains("dualshock") ||
            lower.Contains("gamepad") || lower.Contains("manette") || lower.Contains("playstation") ||
            lower.Contains("wireless controller") || lower.Contains("game controller") ||
            lower.Contains("contrôleur de jeu") || lower.Contains("controlleur de jeu") ||
            lower.Contains("xinput") || lower.Contains("jeu vidéo"))
            return true;
        if (Regex.IsMatch(lower, @"\bnav\b")) return true;
        if (!lower.Contains("controller") && !lower.Contains("contrôleur") && !lower.Contains("controlleur"))
            return false;
        foreach (var deny in GamepadDenyList)
        {
            if (lower.Contains(deny)) return false;
        }
        return true;
    }

    private static string MapArchitecture(long? architecture) => architecture switch
    {
        0 => "x86",
        1 => "MIPS",
        2 => "Alpha",
        3 => "PowerPC",
        5 => "ARM",
        6 => "IA-64",
        9 => "x64",
        12 => "ARM64",
        _ => "Inconnue"
    };

    private static string MapFormFactor(long? formFactor) => formFactor switch
    {
        1 => "Autre",
        2 => "SIMM",
        3 => "SIP",
        4 => "Puce",
        5 => "DIP",
        6 => "ZIP",
        7 => "Propriétaire",
        8 => "DIMM",
        9 => "SIMM",
        10 => "SODIMM",
        11 => "SRIMM",
        12 => "SMD",
        13 => "TSOP",
        14 => "Rangée de puces",
        15 => "RIMM",
        16 => "Die",
        _ => "Inconnu"
    };

    private static string NormalizeName(string value)
    {
        var trimmed = value.Trim();
        if (trimmed.Length == 0 || string.Equals(trimmed, "Unknown", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(trimmed, "To be filled by O.E.M.", StringComparison.OrdinalIgnoreCase))
            return "Inconnu";
        return trimmed;
    }

    private static string ReadEdition(IReadOnlyDictionary<string, object?> row)
    {
        var edition = WmiHelper.Text(row, string.Empty, "Edition").Trim();
        if (edition.Length == 0)
        {
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
                edition = (key?.GetValue("EditionID") as string)?.Trim() ?? string.Empty;
            }
            catch (Exception ex)
            {
                Log.Warn("Hardware", "Lecture de l'édition Windows impossible : " + ex.Message);
            }
        }

        if (edition.Length == 0) return "Inconnu";
        return edition switch
        {
            "Core" or "CoreSingleLanguage" => "Familiale",
            "Professional" or "ProfessionalN" or "ProfessionalEducation" or "ProfessionalEducationN" => "Professionnel",
            "Enterprise" or "EnterpriseN" or "EnterpriseEvaluation" => "Entreprise",
            "Education" or "EducationN" => "Éducation",
            "ServerStandard" or "ServerStandardEval" => "Serveur Standard",
            "ServerDatacenter" or "ServerDatacenterEval" => "Serveur Datacenter",
            _ => edition
        };
    }

    private static bool ReadGameModeEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\GameBar");
            var value = key?.GetValue("AutoGameModeEnabled");
            if (value is null) return true;
            return value switch
            {
                bool flag => flag,
                string raw when bool.TryParse(raw.Trim(), out var parsed) => parsed,
                string raw when long.TryParse(raw.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var number) => number != 0,
                _ => WmiHelper.Flag(new Dictionary<string, object?> { ["AutoGameModeEnabled"] = value }, true, "AutoGameModeEnabled")
            };
        }
        catch (Exception ex)
        {
            Log.Warn("Hardware", "Lecture du mode Jeu impossible : " + ex.Message);
            return true;
        }
    }

    private static T Safe<T>(Func<T> factory, string label, T fallback) where T : class
    {
        var watch = Stopwatch.StartNew();
        try
        {
            var value = factory() ?? fallback;
            watch.Stop();
            Log.Debug("Hardware", "Collecte " + label + " terminée en " + watch.ElapsedMilliseconds + " ms");
            return value;
        }
        catch (Exception ex)
        {
            Log.Error("Hardware", "Collecte " + label + " impossible", ex);
            return fallback;
        }
    }
}
