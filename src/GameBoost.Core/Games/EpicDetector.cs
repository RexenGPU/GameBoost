using System.Text.Json;
using GameBoost.Core.Logging;
using GameBoost.Core.Models;

namespace GameBoost.Core.Games;

internal static class EpicDetector
{
    private static readonly string ManifestDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "Epic", "EpicGamesLauncher", "Data", "Manifests");

    public static List<GameInfo> Detect()
    {
        var games = new List<GameInfo>();
        try
        {
            if (!Directory.Exists(ManifestDirectory)) return games;
            foreach (var file in Directory.EnumerateFiles(ManifestDirectory, "*.item"))
            {
                try
                {
                    using var document = JsonDocument.Parse(File.ReadAllText(file));
                    var root = document.RootElement;
                    var name = GetString(root, "DisplayName");
                    var install = GetString(root, "InstallLocation").TrimEnd('\\', '/');
                    var launchExecutable = GetString(root, "LaunchExecutable");
                    var catalogId = GetString(root, "CatalogItemId");
                    if (install.Length == 0)
                    {
                        Log.Warn("Games", "Manifeste Epic sans dossier d'installation : " + file);
                        continue;
                    }
                    if (!Directory.Exists(install))
                    {
                        Log.Warn("Games", "Jeu Epic absent du disque : " + install);
                        continue;
                    }
                    if (name.Length == 0) name = Path.GetFileName(install);
                    var executable = string.Empty;
                    if (launchExecutable.Length > 0)
                    {
                        try
                        {
                            var candidate = Path.GetFullPath(Path.Combine(install, launchExecutable));
                            if (File.Exists(candidate)) executable = candidate;
                        }
                        catch (Exception ex)
                        {
                            Log.Warn("Games", "Exécutable Epic invalide : " + launchExecutable + " — " + ex.Message);
                        }
                    }
                    var candidateList = executable.Length > 0
                        ? new List<string> { executable }
                        : DetectionHelpers.CollectExecutables(install, 1);
                    if (executable.Length == 0)
                        executable = DetectionHelpers.ResolveExecutable(install, name, Path.GetFileName(install), out candidateList);
                    var game = DetectionHelpers.CreateGame(
                        name, GamePlatform.Epic, install, executable, candidateList, catalogId, null);
                    DetectionHelpers.Enrich(game);
                    games.Add(game);
                }
                catch (Exception ex)
                {
                    Log.Warn("Games", "Manifeste Epic illisible : " + file + " — " + ex.Message);
                }
            }
        }
        catch (Exception ex)
        {
            Log.Error("Games", "Détection Epic impossible", ex);
        }
        return games;
    }

    private static string GetString(JsonElement root, string property)
    {
        if (root.ValueKind == JsonValueKind.Object &&
            root.TryGetProperty(property, out var value) &&
            value.ValueKind == JsonValueKind.String)
        {
            return value.GetString() ?? string.Empty;
        }
        return string.Empty;
    }
}
