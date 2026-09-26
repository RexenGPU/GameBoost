using System.Diagnostics;
using System.Text.Json;
using GameBoost.Core.Logging;
using GameBoost.Core.Models;

namespace GameBoost.Core.Games;

internal static class XboxDetector
{
    private const int TimeoutMs = 30000;
    private const long MaxExecutableSize = 200L * 1024 * 1024;
    private const long MinExecutableSize = 512L * 1024;

    private static readonly string[] DeniedExecutables =
    {
        "explorer", "ApplicationFrameHost", "WinStore.App", "Settings", "RuntimeBroker",
        "TextInputHost", "SearchHost", "SearchUI", "ShellExperienceHost", "StartMenuExperienceHost",
        "LockApp", "SecurityHealthSystray", "PhoneExperienceHost", "Widgets", "WidgetService",
        "spoolsv", "svchost", "cmd", "powershell", "conhost", "UserOOBEBroker", "ConsentUX",
        "BackgroundTaskHost", "DllHost", "Taskmgr", "WerFault", "smartscreen", "WSReset",
        "SystemSettings", "FileExplorer", "gamebar", "GameBar", "XblAuthManager", "XblGameSave"
    };

    private static readonly string[] DeniedPackagePrefixes =
    {
        "Windows.", "Microsoft.Windows", "Microsoft.UI", "Microsoft.VCLibs", "Microsoft.NET",
        "Microsoft.Win32", "Microsoft.Services", "Microsoft.DesktopAppInstaller", "Microsoft.ScreenSketch",
        "Microsoft.Paint", "Microsoft.MSPaint", "Microsoft.Office", "Microsoft.PowerShell",
        "Microsoft.People", "Microsoft.StorePurchaseApp", "Microsoft.Zune", "Microsoft.Bing",
        "Microsoft.YourPhone", "Microsoft.Cortana", "Microsoft.549981C3F5F10", "Microsoft.GetHelp",
        "Microsoft.Getstarted", "Microsoft.WindowsFeedbackHub", "Microsoft.WindowsMaps",
        "Microsoft.WindowsSoundRecorder", "Microsoft.WindowsCamera", "Microsoft.WindowsAlarms",
        "Microsoft.WindowsCalculator", "Microsoft.Windows.DevHome", "Microsoft.WindowsNotepad",
        "Microsoft.Windows.Photos", "Microsoft.WindowsCommunicationsApps", "Microsoft.WindowsStore",
        "Microsoft.MicrosoftStickyNotes", "Microsoft.MicrosoftToDo", "Microsoft.MicrosoftTeams",
        "MicrosoftTeams", "Microsoft.SkypeApp", "Microsoft.Wallet", "Microsoft.Print3D",
        "Microsoft.MixedReality", "Microsoft.OneConnect", "Microsoft.SkypeApp", "Microsoft.Edge",
        "Microsoft.MicrosoftEdge", "Microsoft.XboxApp", "Microsoft.XboxIdentityProvider",
        "Microsoft.XboxGameOverlay", "Microsoft.XboxGamingOverlay", "Microsoft.XboxSpeechToTextOverlay",
        "Microsoft.GamingApp", "Microsoft.GameInput", "Microsoft.HEIFImageExtension",
        "Microsoft.WebMediaExtensions", "Microsoft.UI.Xaml", "Microsoft.AsyncTextService",
        "MICROSOFT.PPICKER", "Microsoft.WindowsAlarms", "Microsoft.WindowsScan", "Microsoft.WindowsReader",
        "Microsoft.WindowsCalculator", "Microsoft.WindowsTerminal", "Microsoft.PowerToys",
        "Microsoft.WindowsNotepad", "Microsoft.OutlookForWindows", "Microsoft.PeopleExperienceHost",
        "Microsoft.SecHealthUI", "Microsoft.ECApp", "Microsoft.GamingServices", "Microsoft.GameBar",
        "MSTeams", "PythonSoftwareFoundation", "AppleInc", "MicrosoftCorporationII", "Microsoft.WindowsFilePicker"
    };

    private static readonly string WindowsDirectory =
        Environment.GetFolderPath(Environment.SpecialFolder.Windows);

    public static List<GameInfo> Detect()
    {
        var games = new List<GameInfo>();
        var packages = QueryPackages();
        foreach (var package in packages)
        {
            try
            {
                var location = (package.InstallLocation ?? string.Empty).TrimEnd('\\', '/');
                if (location.Length == 0 || !Directory.Exists(location)) continue;
                if (WindowsDirectory.Length > 0 &&
                    location.StartsWith(WindowsDirectory, StringComparison.OrdinalIgnoreCase))
                {
                    Log.Debug("Games", "Application système ignorée : " + location);
                    continue;
                }
                var name = DetectionHelpers.CleanPackageName(package.Name ?? string.Empty);
                if (name.Length == 0) name = DetectionHelpers.CleanPackageName(package.PackageFullName ?? string.Empty);
                if (name.Length == 0 || IsDeniedPackage(name)) continue;
                var executables = CollectLaunchers(location);
                if (executables.Count == 0) continue;
                var executable = SelectMainExecutable(executables, name);
                if (executable.Length == 0)
                {
                    Log.Debug("Games", "Aucun exécutable principal pour le paquet : " + name);
                    continue;
                }
                var game = DetectionHelpers.CreateGame(
                    name, GamePlatform.Xbox, location, executable, executables, package.PackageFullName ?? string.Empty, null);
                DetectionHelpers.Enrich(game);
                games.Add(game);
            }
            catch (Exception ex)
            {
                Log.Warn("Games", "Paquet Microsoft Store illisible : " + (package.Name ?? string.Empty) + " — " + ex.Message);
            }
        }
        return games;
    }

    private static bool IsDeniedPackage(string name)
    {
        foreach (var prefix in DeniedPackagePrefixes)
        {
            if (name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    private static bool IsDeniedExecutable(string fileNameWithoutExtension)
    {
        foreach (var denied in DeniedExecutables)
        {
            if (fileNameWithoutExtension.Equals(denied, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return DetectionHelpers.IsSupportExe(fileNameWithoutExtension);
    }

    private static List<string> CollectLaunchers(string location)
    {
        var result = new List<string>();
        var watch = Stopwatch.StartNew();
        try
        {
            foreach (var file in Directory.EnumerateFiles(location, "*.exe", SearchOption.TopDirectoryOnly))
            {
                var fileName = Path.GetFileNameWithoutExtension(file);
                if (IsDeniedExecutable(fileName)) continue;
                var size = DetectionHelpers.SafeFileSize(file);
                if (size < MinExecutableSize) continue;
                result.Add(file);
                if (result.Count >= 60 || watch.ElapsedMilliseconds > 2000) break;
            }
        }
        catch (Exception ex)
        {
            Log.Warn("Games", "Lecture du paquet impossible : " + location + " — " + ex.Message);
        }
        return result;
    }

    private static string SelectMainExecutable(List<string> executables, string packageName)
    {
        var target = DetectionHelpers.NormalizeName(packageName);
        string? bestByName = null;
        foreach (var exe in executables)
        {
            if (DetectionHelpers.SafeFileSize(exe) > MaxExecutableSize) continue;
            var name = DetectionHelpers.NormalizeName(Path.GetFileNameWithoutExtension(exe));
            if (target.Length >= 3 && (name == target || (name.Length >= 4 && target.Contains(name, StringComparison.Ordinal))))
            {
                bestByName = exe;
                break;
            }
        }
        if (bestByName is not null) return bestByName;
        string best = string.Empty;
        long bestSize = 0;
        foreach (var exe in executables)
        {
            var size = DetectionHelpers.SafeFileSize(exe);
            if (size <= 0 || size > MaxExecutableSize) continue;
            if (size > bestSize)
            {
                bestSize = size;
                best = exe;
            }
        }
        return best;
    }

    private static List<AppxPackage> QueryPackages()
    {
        var packages = new List<AppxPackage>();
        const string command = "Get-AppxPackage | Select-Object Name,PackageFullName,InstallLocation | ConvertTo-Json -Compress";
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            startInfo.ArgumentList.Add("-NoProfile");
            startInfo.ArgumentList.Add("-NonInteractive");
            startInfo.ArgumentList.Add("-Command");
            startInfo.ArgumentList.Add(command);

            using var process = Process.Start(startInfo);
            if (process is null)
            {
                Log.Warn("Games", "Impossible de lancer PowerShell pour les paquets Windows");
                return packages;
            }
            var outputTask = process.StandardOutput.ReadToEndAsync();
            var errorTask = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(TimeoutMs))
            {
                try { process.Kill(true); } catch { }
                Log.Warn("Games", "Délai dépassé pour la lecture des paquets Windows");
                return packages;
            }
            var output = outputTask.Wait(5000) ? outputTask.Result : string.Empty;
            if (!errorTask.Wait(2000) || (errorTask.Result ?? string.Empty).Trim().Length > 0)
            {
                var error = errorTask.IsCompleted ? errorTask.Result : string.Empty;
                if (!string.IsNullOrWhiteSpace(error)) Log.Warn("Games", "PowerShell : " + error.Trim());
            }
            if (string.IsNullOrWhiteSpace(output)) return packages;
            return ParsePackages(output);
        }
        catch (Exception ex)
        {
            Log.Error("Games", "Détection des applications Microsoft Store impossible", ex);
            return packages;
        }
    }

    private static List<AppxPackage> ParsePackages(string json)
    {
        var packages = new List<AppxPackage>();
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind == JsonValueKind.Object)
            {
                var single = ReadPackage(root);
                if (single is not null) packages.Add(single);
            }
            else if (root.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in root.EnumerateArray())
                {
                    if (item.ValueKind != JsonValueKind.Object) continue;
                    var package = ReadPackage(item);
                    if (package is not null) packages.Add(package);
                }
            }
        }
        catch (Exception ex)
        {
            Log.Error("Games", "Réponse des paquets Windows illisible", ex);
        }
        return packages;
    }

    private static AppxPackage? ReadPackage(JsonElement element)
    {
        var name = GetString(element, "Name");
        var fullName = GetString(element, "PackageFullName");
        var location = GetString(element, "InstallLocation");
        if (fullName.Length == 0 && name.Length == 0) return null;
        return new AppxPackage { Name = name, PackageFullName = fullName, InstallLocation = location };
    }

    private static string GetString(JsonElement element, string property)
    {
        if (element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String)
            return value.GetString() ?? string.Empty;
        return string.Empty;
    }

    private sealed class AppxPackage
    {
        public string Name { get; set; } = string.Empty;
        public string PackageFullName { get; set; } = string.Empty;
        public string InstallLocation { get; set; } = string.Empty;
    }
}
