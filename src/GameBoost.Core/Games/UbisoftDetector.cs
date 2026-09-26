using GameBoost.Core.Logging;
using GameBoost.Core.Models;
using Microsoft.Win32;

namespace GameBoost.Core.Games;

internal static class UbisoftDetector
{
    private static readonly string[] RegistryPaths =
    {
        @"SOFTWARE\WOW6432Node\Ubisoft\Launcher\Installs",
        @"SOFTWARE\Ubisoft\Launcher\Installs"
    };

    public static List<GameInfo> Detect()
    {
        var games = new List<GameInfo>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var registryPath in RegistryPaths)
        {
            try
            {
                using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
                using var key = baseKey.OpenSubKey(registryPath);
                if (key is null) continue;
                foreach (var subName in key.GetSubKeyNames())
                {
                    try
                    {
                        using var sub = key.OpenSubKey(subName);
                        if (sub is null) continue;
                        var installDir = (sub.GetValue("InstallDir") as string ?? string.Empty)
                            .Replace('/', '\\').TrimEnd('\\');
                        if (installDir.Length == 0 || !Directory.Exists(installDir)) continue;
                        if (!seen.Add(installDir)) continue;
                        var name = sub.GetValue("DisplayName") as string ?? string.Empty;
                        if (name.Length == 0) name = Path.GetFileName(installDir);
                        DateTime? lastLaunch = null;
                        var raw = sub.GetValue("LastLaunch");
                        if (raw is not null)
                        {
                            try { lastLaunch = DetectionHelpers.FromUnix(Convert.ToInt64(raw)); }
                            catch (Exception ex) { Log.Warn("Games", "Horodatage Ubisoft illisible : " + ex.Message); }
                        }
                        var executable = DetectionHelpers.ResolveExecutable(installDir, name, Path.GetFileName(installDir), out var candidates);
                        if (candidates.Count == 0)
                        {
                            Log.Warn("Games", "Aucun exécutable Ubisoft trouvé : " + installDir);
                        }
                        var game = DetectionHelpers.CreateGame(
                            name, GamePlatform.Ubisoft, installDir, executable, candidates, subName, lastLaunch);
                        DetectionHelpers.Enrich(game);
                        games.Add(game);
                    }
                    catch (Exception ex)
                    {
                        Log.Warn("Games", "Entrée Ubisoft illisible : " + subName + " — " + ex.Message);
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Warn("Games", "Registre Ubisoft inaccessible : " + registryPath + " — " + ex.Message);
            }
        }
        return games;
    }
}
