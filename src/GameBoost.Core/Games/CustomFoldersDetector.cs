using GameBoost.Core.Data;
using GameBoost.Core.Logging;
using GameBoost.Core.Models;

namespace GameBoost.Core.Games;

internal static class CustomFoldersDetector
{
    public static List<GameInfo> Detect()
    {
        var games = new List<GameInfo>();
        try
        {
            foreach (var folder in SettingsService.Current.CustomGameFolders)
            {
                try
                {
                    if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
                    {
                        if (!string.IsNullOrWhiteSpace(folder))
                            Log.Warn("Games", "Dossier de jeux personnalisé introuvable : " + folder);
                        continue;
                    }
                    var executables = DetectionHelpers.CollectExecutables(folder, 2)
                        .Where(f => DetectionHelpers.SafeFileSize(f) > 10 * 1024 * 1024)
                        .ToList();
                    var groups = executables.GroupBy(f => Path.GetDirectoryName(f) ?? folder, StringComparer.OrdinalIgnoreCase);
                    foreach (var group in groups)
                    {
                        var directory = group.Key;
                        var candidateList = group.ToList();
                        var folderName = Path.GetFileName(directory.TrimEnd('\\'));
                        var isRoot = string.Equals(directory.TrimEnd('\\'), folder.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);
                        var executable = DetectionHelpers.SelectExecutable(candidateList, folderName, Path.GetFileName(folder), folder);
                        if (executable.Length == 0) continue;
                        var name = isRoot ? Path.GetFileNameWithoutExtension(executable) : folderName;
                        var game = DetectionHelpers.CreateGame(
                            name, GamePlatform.Other, directory, executable, candidateList, string.Empty, null);
                        DetectionHelpers.Enrich(game);
                        games.Add(game);
                    }
                }
                catch (Exception ex)
                {
                    Log.Warn("Games", "Dossier de jeux illisible : " + folder + " — " + ex.Message);
                }
            }
        }
        catch (Exception ex)
        {
            Log.Error("Games", "Détection des dossiers personnalisés impossible", ex);
        }
        return games;
    }
}
