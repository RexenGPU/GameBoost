using System.Diagnostics;
using GameBoost.Core.Data;
using GameBoost.Core.Logging;
using GameBoost.Core.Models;

namespace GameBoost.Core.Games;

public sealed class GameScanner
{
    public static GameScanner Instance { get; } = new GameScanner();

    private static readonly GamePlatform[] ScanOrder =
    {
        GamePlatform.Steam,
        GamePlatform.Epic,
        GamePlatform.Ubisoft,
        GamePlatform.Xbox,
        GamePlatform.Gog,
        GamePlatform.BattleNet,
        GamePlatform.Riot,
        GamePlatform.Other
    };

    private GameScanner()
    {
    }

    public List<GameInfo> Scan()
    {
        var detected = new List<GameInfo>();
        var completed = new HashSet<GamePlatform>();
        foreach (var platform in ScanOrder)
        {
            try
            {
                var found = Detect(platform);
                detected.AddRange(found);
                completed.Add(platform);
                Log.Info("Games", "Plateforme " + platform + " : " + found.Count + " jeu(x) détecté(s)");
            }
            catch (Exception ex)
            {
                Log.Error("Games", "Détection impossible pour la plateforme " + platform, ex);
            }
        }

        try
        {
            var merged = Merge(detected, completed, out var detectedGames);
            Persist(detectedGames);
            return merged;
        }
        catch (Exception ex)
        {
            Log.Error("Games", "Fusion de la bibliothèque impossible", ex);
            return Sort(detected);
        }
    }

    public List<GameInfo> ScanPlatform(GamePlatform platform)
    {
        try
        {
            if (platform == GamePlatform.Manual)
            {
                var manual = LoadDatabase()
                    .Where(g => g.IsManuallyAdded || g.Platform == GamePlatform.Manual)
                    .ToList();
                return Sort(manual);
            }

            var detected = Detect(platform);
            var completed = new HashSet<GamePlatform> { platform };
            var merged = Merge(detected, completed, out _);
            return Sort(merged.Where(g => g.Platform == platform).ToList());
        }
        catch (Exception ex)
        {
            Log.Error("Games", "Détection impossible pour la plateforme " + platform, ex);
            return new List<GameInfo>();
        }
    }

    public List<GameInfo> GetLibrary()
    {
        var games = LoadDatabase();
        RefreshRunning(games);
        return Sort(games);
    }

    public GameInfo? AddManualExecutable(string exePath)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(exePath))
            {
                Log.Warn("Games", "Ajout manuel refusé : chemin vide");
                return null;
            }
            var full = Path.GetFullPath(exePath.Trim().Trim('"'));
            if (!File.Exists(full))
            {
                Log.Warn("Games", "Ajout manuel refusé : exécutable introuvable " + full);
                return null;
            }
            if (!full.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                Log.Warn("Games", "Ajout manuel refusé : ce n'est pas un exécutable " + full);
                return null;
            }

            var directory = Path.GetDirectoryName(full) ?? string.Empty;
            var game = new GameInfo
            {
                Name = Path.GetFileNameWithoutExtension(full),
                Platform = GamePlatform.Manual,
                InstallPath = directory,
                ExecutablePath = full,
                ExecutableCandidates = new List<string> { full },
                IsManuallyAdded = true
            };
            DetectionHelpers.Enrich(game);
            Persist(new[] { game });
            Log.Info("Games", "Jeu ajouté manuellement : " + game.Name);
            return game;
        }
        catch (Exception ex)
        {
            Log.Error("Games", "Ajout manuel impossible : " + exePath, ex);
            return null;
        }
    }

    public bool RemoveGame(string id)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(id)) return false;
            if (!Db.TryGetCollection<GameInfo>("games", out var collection)) return false;
            var removed = collection.Delete(id);
            Log.Info("Games", removed ? "Jeu supprimé de la base" : "Jeu absent de la base : " + id);
            return removed;
        }
        catch (Exception ex)
        {
            Log.Error("Games", "Suppression du jeu impossible : " + id, ex);
            return false;
        }
    }

    public void UpdateGame(GameInfo game)
    {
        try
        {
            if (game is null)
            {
                Log.Warn("Games", "Mise à jour refusée : jeu nul");
                return;
            }
            Persist(new[] { game });
        }
        catch (Exception ex)
        {
            Log.Error("Games", "Mise à jour du jeu impossible : " + game?.Name, ex);
        }
    }

    public void Rescan()
    {
        try
        {
            var games = Scan();
            Persist(games);
            Log.Info("Games", "Analyse terminée : " + games.Count + " jeu(s) en base");
        }
        catch (Exception ex)
        {
            Log.Error("Games", "Analyse complète impossible", ex);
        }
    }

    private static List<GameInfo> Detect(GamePlatform platform)
    {
        switch (platform)
        {
            case GamePlatform.Steam: return SteamDetector.Detect();
            case GamePlatform.Epic: return EpicDetector.Detect();
            case GamePlatform.Ubisoft: return UbisoftDetector.Detect();
            case GamePlatform.Xbox: return XboxDetector.Detect();
            case GamePlatform.Gog: return GogDetector.Detect();
            case GamePlatform.BattleNet: return BattleNetDetector.Detect();
            case GamePlatform.Riot: return RiotDetector.Detect();
            case GamePlatform.Other: return CustomFoldersDetector.Detect();
            default: return new List<GameInfo>();
        }
    }

    private static List<GameInfo> Merge(List<GameInfo> detected, HashSet<GamePlatform> completed, out List<GameInfo> detectedGames)
    {
        var database = LoadDatabase();
        var databaseByPath = new Dictionary<string, GameInfo>(StringComparer.OrdinalIgnoreCase);
        var databaseByKey = new Dictionary<string, GameInfo>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in database)
        {
            if (entry.InstallPath.Length > 0 && !databaseByPath.ContainsKey(entry.InstallPath))
                databaseByPath[entry.InstallPath] = entry;
            var key = entry.Platform + "|" + entry.PlatformId;
            if (entry.PlatformId.Length > 0 && !databaseByKey.ContainsKey(key))
                databaseByKey[key] = entry;
        }

        var result = new List<GameInfo>();
        var pathsInResult = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var keysInResult = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var usedIds = new HashSet<string>(StringComparer.Ordinal);
        detectedGames = new List<GameInfo>();

        foreach (var game in detected)
        {
            var duplicate = false;
            if (game.InstallPath.Length > 0)
            {
                duplicate = !pathsInResult.Add(game.InstallPath);
            }
            else if (game.PlatformId.Length > 0)
            {
                duplicate = !keysInResult.Add(game.Platform + "|" + game.PlatformId);
            }
            if (duplicate)
            {
                Log.Debug("Games", "Doublon ignoré : " + game.Name);
                continue;
            }

            GameInfo? existing = null;
            if (game.InstallPath.Length > 0) databaseByPath.TryGetValue(game.InstallPath, out existing);
            if (existing is null && game.PlatformId.Length > 0)
                databaseByKey.TryGetValue(game.Platform + "|" + game.PlatformId, out existing);
            if (existing is not null)
            {
                game.Id = existing.Id;
                game.OverlayEnabled = existing.OverlayEnabled;
                game.LaunchArguments = existing.LaunchArguments;
                game.IsManuallyAdded = existing.IsManuallyAdded;
                if (game.LastLaunch is null) game.LastLaunch = existing.LastLaunch;
                if (game.IconPath.Length == 0) game.IconPath = existing.IconPath;
                usedIds.Add(existing.Id);
            }

            detectedGames.Add(game);
            result.Add(game);
        }

        foreach (var entry in database)
        {
            if (usedIds.Contains(entry.Id)) continue;
            if (entry.InstallPath.Length > 0 && !pathsInResult.Add(entry.InstallPath))
            {
                Log.Debug("Games", "Entrée en double ignorée : " + entry.Name);
                continue;
            }
            if (entry.PlatformId.Length > 0) keysInResult.Add(entry.Platform + "|" + entry.PlatformId);

            if (!entry.IsManuallyAdded && entry.Platform != GamePlatform.Manual && completed.Contains(entry.Platform))
            {
                if (entry.InstallPath.Length > 0 && !Directory.Exists(entry.InstallPath))
                {
                    Log.Info("Games", "Jeu désinstallé retiré de la vue : " + entry.Name);
                    continue;
                }
            }
            result.Add(entry);
        }

        return Sort(result);
    }

    private static List<GameInfo> LoadDatabase()
    {
        try
        {
            if (!Db.TryGetCollection<GameInfo>("games", out var collection)) return new List<GameInfo>();
            var games = new List<GameInfo>();
            foreach (var game in collection.FindAll())
            {
                if (Normalize(game) is not null) games.Add(game);
            }
            return games;
        }
        catch (Exception ex)
        {
            Log.Error("Games", "Lecture de la base des jeux impossible", ex);
            return new List<GameInfo>();
        }
    }

    private static GameInfo? Normalize(GameInfo? game)
    {
        if (game is null) return null;
        game.Name = game.Name ?? string.Empty;
        game.InstallPath = game.InstallPath ?? string.Empty;
        game.ExecutablePath = game.ExecutablePath ?? string.Empty;
        game.IconPath = game.IconPath ?? string.Empty;
        game.InstallSize = game.InstallSize ?? string.Empty;
        game.LaunchArguments = game.LaunchArguments ?? string.Empty;
        game.KnownSettingsSummary = game.KnownSettingsSummary ?? string.Empty;
        game.PlatformId = game.PlatformId ?? string.Empty;
        game.Id = string.IsNullOrWhiteSpace(game.Id) ? Guid.NewGuid().ToString("N") : game.Id;
        if (game.ExecutableCandidates is null) game.ExecutableCandidates = new List<string>();
        return game;
    }

    private static void Persist(IEnumerable<GameInfo> games)
    {
        try
        {
            if (!Db.TryGetCollection<GameInfo>("games", out var collection)) return;
            foreach (var game in games)
            {
                try
                {
                    collection.Upsert(game);
                }
                catch (Exception ex)
                {
                    Log.Error("Games", "Sauvegarde impossible pour " + game.Name, ex);
                }
            }
        }
        catch (Exception ex)
        {
            Log.Error("Games", "Accès à la base des jeux impossible", ex);
        }
    }

    private static void RefreshRunning(List<GameInfo> games)
    {
        foreach (var game in games)
        {
            game.IsRunning = false;
            game.RunningProcessId = 0;
            if (string.IsNullOrWhiteSpace(game.ExecutablePath)) continue;
            var name = Path.GetFileNameWithoutExtension(game.ExecutablePath);
            if (name.Length == 0) continue;
            Process[] processes;
            try
            {
                processes = Process.GetProcessesByName(name);
            }
            catch (Exception ex)
            {
                Log.Debug("Games", "Processus illisible pour " + game.Name + " : " + ex.Message);
                continue;
            }
            foreach (var process in processes)
            {
                try
                {
                    if (!process.HasExited && game.RunningProcessId == 0)
                    {
                        game.IsRunning = true;
                        game.RunningProcessId = process.Id;
                    }
                }
                catch
                {
                }
                finally
                {
                    process.Dispose();
                }
            }
        }
    }

    private static List<GameInfo> Sort(List<GameInfo> games)
    {
        games.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase));
        return games;
    }
}
