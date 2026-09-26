using GameBoost.Core.Logging;
using GameBoost.Core.Models;
using Microsoft.Win32;

namespace GameBoost.Core.Games;

internal static class BattleNetDetector
{
    private static readonly string[] KnownGames =
    {
        "World of Warcraft", "Diablo III", "Diablo IV", "Overwatch", "Overwatch 2",
        "Hearthstone", "StarCraft", "StarCraft II", "Call of Duty"
    };

    private static readonly string[] RegistryPaths =
    {
        @"SOFTWARE\WOW6432Node\Blizzard Entertainment",
        @"SOFTWARE\Blizzard Entertainment"
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
                        if (subName.Equals("Battle.net", StringComparison.OrdinalIgnoreCase)) continue;
                        using var sub = key.OpenSubKey(subName);
                        if (sub is null) continue;
                        var installPath = (sub.GetValue("InstallPath") as string ?? string.Empty).TrimEnd('\\', '/');
                        if (installPath.Length == 0 || !Directory.Exists(installPath)) continue;
                        if (!seen.Add(installPath)) continue;
                        var name = sub.GetValue("DisplayName") as string ?? subName;
                        var executable = DetectionHelpers.ResolveExecutable(installPath, name, subName, out var candidates);
                        if (executable.Length == 0)
                        {
                            Log.Warn("Games", "Aucun exécutable Blizzard trouvé : " + installPath);
                        }
                        var game = DetectionHelpers.CreateGame(
                            name, GamePlatform.BattleNet, installPath, executable, candidates, subName, null);
                        DetectionHelpers.Enrich(game);
                        games.Add(game);
                    }
                    catch (Exception ex)
                    {
                        Log.Warn("Games", "Entrée Blizzard illisible : " + subName + " — " + ex.Message);
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Warn("Games", "Registre Blizzard inaccessible : " + registryPath + " — " + ex.Message);
            }
        }

        if (games.Count == 0)
        {
            foreach (var known in KnownGames)
            {
                try
                {
                    using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
                    using var key = baseKey.OpenSubKey(@"SOFTWARE\WOW6432Node\Blizzard Entertainment\" + known);
                    var installPath = (key?.GetValue("InstallPath") as string ?? string.Empty).TrimEnd('\\', '/');
                    if (installPath.Length == 0 || !Directory.Exists(installPath)) continue;
                    if (!seen.Add(installPath)) continue;
                    var executable = DetectionHelpers.ResolveExecutable(installPath, known, known, out var candidates);
                    var game = DetectionHelpers.CreateGame(
                        known, GamePlatform.BattleNet, installPath, executable, candidates, known, null);
                    DetectionHelpers.Enrich(game);
                    games.Add(game);
                }
                catch (Exception ex)
                {
                    Log.Warn("Games", "Jeu Blizzard inaccessible : " + known + " — " + ex.Message);
                }
            }
        }
        return games;
    }
}
