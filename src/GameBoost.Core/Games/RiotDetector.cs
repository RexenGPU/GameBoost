using GameBoost.Core.Data;
using GameBoost.Core.Logging;
using GameBoost.Core.Models;

namespace GameBoost.Core.Games;

internal static class RiotDetector
{
    private static readonly string Root = @"C:\Riot Games";

    private static readonly (string Path, string Name)[] KnownExecutables =
    {
        (@"C:\Riot Games\League of Legends\LeagueClient.exe", "League of Legends"),
        (@"C:\Riot Games\VALORANT\live\Shipping\VALORANT-Win64-Shipping.exe", "VALORANT"),
        (@"C:\Riot Games\Legends of Runeterra\Live\LoR.exe", "Legends of Runeterra"),
        (@"C:\Riot Games\Teamfight Tactics\TFT.exe", "Teamfight Tactics")
    };

    private static readonly string[] IgnoredFolders = { "Riot Client", "RiotClientServices" };

    public static List<GameInfo> Detect()
    {
        var games = new List<GameInfo>();
        try
        {
            if (!Directory.Exists(Root)) return games;
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var (path, name) in KnownExecutables)
            {
                try
                {
                    if (!File.Exists(path)) continue;
                    var installDir = Path.GetDirectoryName(path) ?? string.Empty;
                    if (!seen.Add(installDir)) continue;
                    var game = DetectionHelpers.CreateGame(
                        name, GamePlatform.Riot, installDir, path, new List<string> { path }, string.Empty, null);
                    DetectionHelpers.Enrich(game);
                    games.Add(game);
                }
                catch (Exception ex)
                {
                    Log.Warn("Games", "Jeu Riot introuvable : " + path + " — " + ex.Message);
                }
            }

            foreach (var folder in Directory.EnumerateDirectories(Root))
            {
                try
                {
                    var folderName = Path.GetFileName(folder);
                    if (IgnoredFolders.Contains(folderName, StringComparer.OrdinalIgnoreCase)) continue;
                    var candidates = DetectionHelpers.CollectExecutables(folder, 2)
                        .Where(f => DetectionHelpers.SafeFileSize(f) > 10 * 1024 * 1024)
                        .ToList();
                    if (candidates.Count == 0) continue;
                    var executable = DetectionHelpers.SelectExecutable(candidates, folderName, folderName, folder);
                    if (executable.Length == 0) continue;
                    var installDir = Path.GetDirectoryName(executable) ?? folder;
                    if (seen.Contains(installDir)) continue;
                    if (games.Any(g => string.Equals(g.ExecutablePath, executable, StringComparison.OrdinalIgnoreCase))) continue;
                    seen.Add(installDir);
                    var game = DetectionHelpers.CreateGame(
                        folderName, GamePlatform.Riot, installDir, executable, candidates, string.Empty, null);
                    DetectionHelpers.Enrich(game);
                    games.Add(game);
                }
                catch (Exception ex)
                {
                    Log.Warn("Games", "Dossier Riot illisible : " + folder + " — " + ex.Message);
                }
            }
        }
        catch (Exception ex)
        {
            Log.Error("Games", "Détection Riot impossible", ex);
        }
        return games;
    }
}
