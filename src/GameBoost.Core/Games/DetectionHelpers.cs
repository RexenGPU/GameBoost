using System.Diagnostics;
using System.Globalization;
using System.Text;
using GameBoost.Core.Logging;
using GameBoost.Core.Models;

namespace GameBoost.Core.Games;

internal static class DetectionHelpers
{
    private static readonly string[] SupportExeTokens =
    {
        "unins", "uninstall", "vcredist", "vc_redist", "dxsetup", "directx", "dotnetfx",
        "crashreport", "crashpad", "unitycrashhandler", "ue4prereq", "ue_prereq", "prereq",
        "easyanticheat", "redist", "oalinst", "dxwebsetup", "reportlauncher",
        "unrealcefsubprocess", "setup", "installer", "eac", "subprocess",
        "vconsole", "createdump", "qtwebengine", "notifications", "browser",
        "elementviewer", "studiomdl", "hlfaceposer", "hlmv", "shadercompile", "ndp"
    };

    public static string NormalizeName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        var builder = new StringBuilder(value.Length);
        foreach (var c in value)
        {
            if (char.IsLetterOrDigit(c)) builder.Append(char.ToLowerInvariant(c));
        }
        return builder.ToString();
    }

    public static bool IsSupportExe(string? fileNameWithoutExtension)
    {
        var name = NormalizeName(fileNameWithoutExtension);
        if (name.Length == 0) return false;
        foreach (var token in SupportExeTokens)
        {
            var normalized = NormalizeName(token);
            if (normalized.Length == 0) continue;
            if (name == normalized) return true;
            if (normalized.Length >= 4 && name.Contains(normalized, StringComparison.Ordinal)) return true;
            if (normalized.Length == 3 && name.StartsWith(normalized, StringComparison.Ordinal)) return true;
        }
        return false;
    }

    public static List<string> CollectExecutables(string root, int maxDepth, int maxCount = 250)
    {
        var results = new List<string>();
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root)) return results;
        var watch = Stopwatch.StartNew();
        CollectExecutablesRecursive(root, 0, maxDepth, maxCount, results, watch);
        return results;
    }

    private static void CollectExecutablesRecursive(string directory, int depth, int maxDepth, int maxCount, List<string> results, Stopwatch watch)
    {
        if (results.Count >= maxCount || watch.ElapsedMilliseconds > 2000) return;
        try
        {
            foreach (var file in Directory.EnumerateFiles(directory, "*.exe", SearchOption.TopDirectoryOnly))
            {
                results.Add(file);
                if (results.Count >= maxCount || watch.ElapsedMilliseconds > 2000) return;
            }
        }
        catch (Exception ex)
        {
            Log.Warn("Games", "Dossier illisible : " + directory + " — " + ex.Message);
        }
        if (depth >= maxDepth || watch.ElapsedMilliseconds > 2000) return;
        try
        {
            foreach (var sub in Directory.EnumerateDirectories(directory))
            {
                CollectExecutablesRecursive(sub, depth + 1, maxDepth, maxCount, results, watch);
                if (results.Count >= maxCount || watch.ElapsedMilliseconds > 2000) return;
            }
        }
        catch (Exception ex)
        {
            Log.Warn("Games", "Sous-dossier illisible : " + directory + " — " + ex.Message);
        }
    }

    public static string ResolveExecutable(string installDir, string gameName, string folderName, out List<string> candidates)
    {
        candidates = CollectExecutables(installDir, 1);
        var best = SelectExecutable(candidates, gameName, folderName, installDir);
        if (best.Length > 0 && !IsSupportExe(Path.GetFileNameWithoutExtension(best))) return best;
        var deep = CollectExecutables(installDir, 3);
        if (deep.Count > 0)
        {
            var deepBest = SelectExecutable(deep, gameName, folderName, installDir);
            if (deepBest.Length > 0 && !IsSupportExe(Path.GetFileNameWithoutExtension(deepBest)))
            {
                candidates = deep;
                return deepBest;
            }
        }
        return string.Empty;
    }

    public static string SelectExecutable(IReadOnlyList<string> executables, string gameName, string folderName, string root = "")
    {
        if (executables.Count == 0) return string.Empty;
        var targetGame = NormalizeName(gameName);
        var targetFolder = NormalizeName(folderName);
        var best = string.Empty;
        var bestScore = double.MinValue;
        long bestSize = -1;
        foreach (var exe in executables)
        {
            var rawName = Path.GetFileNameWithoutExtension(exe);
            var name = NormalizeName(rawName);
            double score = 0;
            if (targetGame.Length >= 3 && name == targetGame) score += 120;
            else if (targetGame.Length >= 4 && name.Length >= 4 &&
                     (name.Contains(targetGame, StringComparison.Ordinal) || targetGame.Contains(name, StringComparison.Ordinal)) &&
                     name.Length <= targetGame.Length + 12)
                score += 70;
            if (targetFolder.Length >= 3 && name == targetFolder) score += 100;
            else if (targetFolder.Length >= 4 && name.Length >= 4 &&
                     (name.Contains(targetFolder, StringComparison.Ordinal) || targetFolder.Contains(name, StringComparison.Ordinal)) &&
                     name.Length <= targetFolder.Length + 12)
                score += 60;
            long size = 0;
            try { size = new FileInfo(exe).Length; } catch { size = 0; }
            score += Math.Min(size / (1024.0 * 1024.0), 400.0) * 0.01;
            score += (4 - Math.Min(ComputeDepth(root, exe), 4)) * 6;
            if (IsSupportExe(rawName)) score -= 90;
            if (score > bestScore || (Math.Abs(score - bestScore) < 0.0001 && size > bestSize))
            {
                best = exe;
                bestScore = score;
                bestSize = size;
            }
        }
        return best;
    }

    private static int ComputeDepth(string root, string exe)
    {
        if (string.IsNullOrWhiteSpace(root)) return 0;
        try
        {
            var relative = Path.GetRelativePath(root, exe);
            if (relative.StartsWith("..", StringComparison.Ordinal)) return 4;
            var parts = relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return Math.Max(0, parts.Length - 1);
        }
        catch
        {
            return 0;
        }
    }

    public static string MeasureInstallSize(string directory)
    {
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory)) return string.Empty;
        long total = 0;
        var count = 0;
        var truncated = false;
        var watch = Stopwatch.StartNew();
        var stack = new Stack<string>();
        stack.Push(directory);
        try
        {
            while (stack.Count > 0)
            {
                var current = stack.Pop();
                try
                {
                    foreach (var file in Directory.EnumerateFiles(current, "*", SearchOption.TopDirectoryOnly))
                    {
                        try { total += new FileInfo(file).Length; } catch { }
                        count++;
                        if (count > 5000 || watch.ElapsedMilliseconds > 1500)
                        {
                            truncated = true;
                            break;
                        }
                    }
                    if (truncated) break;
                    foreach (var sub in Directory.EnumerateDirectories(current)) stack.Push(sub);
                }
                catch
                {
                }
                if (watch.ElapsedMilliseconds > 1500)
                {
                    truncated = true;
                    break;
                }
            }
        }
        catch (Exception ex)
        {
            Log.Warn("Games", "Taille du dossier impossible : " + directory + " — " + ex.Message);
            return string.Empty;
        }
        if (count == 0) return string.Empty;
        var text = FormatSize(total);
        return truncated ? "≈ " + text : text;
    }

    public static string FormatSize(long bytes)
    {
        string[] units = { "o", "Ko", "Mo", "Go", "To" };
        double value = bytes;
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }
        return value.ToString(unit == 0 ? "0" : "0.#", CultureInfo.GetCultureInfo("fr-FR")) + " " + units[unit];
    }

    public static long SafeFileSize(string path)
    {
        try { return new FileInfo(path).Length; } catch { return 0; }
    }

    public static string BuildSettingsSummary(string installDir, string gameName)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(installDir) && Directory.Exists(installDir))
            {
                var found = FindConfigFile(installDir, installDir, gameName, 3);
                if (found.Length > 0) return "Configuration utilisateur trouvée: " + found;
            }

            var token = NormalizeName(gameName);
            if (token.Length >= 4)
            {
                var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                if (Directory.Exists(local))
                {
                    foreach (var dir in Directory.EnumerateDirectories(local))
                    {
                        var normalized = NormalizeName(Path.GetFileName(dir));
                        if (normalized.Length < 6) continue;
                        if (!normalized.Contains(token, StringComparison.Ordinal)) continue;
                        var found = FindConfigFile(dir, local, gameName, 2);
                        if (found.Length > 0) return "Configuration utilisateur trouvée: " + found;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Log.Warn("Games", "Recherche de configuration impossible : " + ex.Message);
        }
        return string.Empty;
    }

    private static string FindConfigFile(string root, string relativeBase, string gameName, int maxDepth)
    {
        var token = NormalizeName(gameName);
        var fallback = string.Empty;
        var count = 0;
        var watch = Stopwatch.StartNew();
        var stack = new Stack<(string Path, int Depth)>();
        stack.Push((root, 0));
        while (stack.Count > 0 && count < 4000 && watch.ElapsedMilliseconds < 1200)
        {
            var (current, depth) = stack.Pop();
            try
            {
                foreach (var file in Directory.EnumerateFiles(current, "*.ini", SearchOption.TopDirectoryOnly))
                {
                    count++;
                    var fileName = Path.GetFileName(file);
                    if (fileName.Equals("GameUserSettings.ini", StringComparison.OrdinalIgnoreCase))
                        return SafeRelative(relativeBase, file);
                    var normalized = NormalizeName(Path.GetFileNameWithoutExtension(file));
                    var isConfig = normalized.Contains("config", StringComparison.Ordinal) ||
                                   normalized.Contains("settings", StringComparison.Ordinal);
                    var matchesGame = token.Length >= 4 &&
                                      (normalized == token ||
                                       (normalized.Contains(token, StringComparison.Ordinal) &&
                                        (isConfig || normalized.Length <= token.Length + 8)));
                    if (matchesGame || (isConfig && fallback.Length == 0))
                        fallback = SafeRelative(relativeBase, file);
                    if (count >= 4000 || watch.ElapsedMilliseconds >= 1200) break;
                }
            }
            catch
            {
            }
            if (depth < maxDepth && watch.ElapsedMilliseconds < 1200)
            {
                try
                {
                    foreach (var sub in Directory.EnumerateDirectories(current)) stack.Push((sub, depth + 1));
                }
                catch
                {
                }
            }
        }
        return fallback;
    }

    private static string SafeRelative(string relativeBase, string file)
    {
        try { return Path.GetRelativePath(relativeBase, file); } catch { return file; }
    }

    public static DateTime? FromUnix(long seconds)
    {
        if (seconds < 100000000 || seconds > 4102444800) return null;
        try { return DateTimeOffset.FromUnixTimeSeconds(seconds).LocalDateTime; } catch { return null; }
    }

    public static string CleanPackageName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return string.Empty;
        var value = name.Trim();
        var underscore = value.IndexOf('_');
        if (underscore > 0) value = value[..underscore];
        var dash = value.IndexOf('-');
        if (dash > 0 && value.Length - dash <= 5 && value[(dash + 1)..].All(char.IsDigit)) value = value[..dash];
        return value.Trim();
    }

    public static void Enrich(GameInfo game)
    {
        try
        {
            if (game.InstallSize.Length == 0 && game.InstallPath.Length > 0)
                game.InstallSize = MeasureInstallSize(game.InstallPath);
            if (game.KnownSettingsSummary.Length == 0 && game.InstallPath.Length > 0)
                game.KnownSettingsSummary = BuildSettingsSummary(game.InstallPath, game.Name);
            if (game.IconPath.Length == 0)
            {
                if (game.ExecutablePath.Length > 0) game.IconPath = GameIconCache.GetIconPath(game.ExecutablePath);
                if (game.IconPath.Length == 0 && game.InstallPath.Length > 0)
                    game.IconPath = GameIconCache.GetFolderIconPath(game.InstallPath);
            }
        }
        catch (Exception ex)
        {
            Log.Warn("Games", "Enrichissement du jeu impossible : " + game.Name + " — " + ex.Message);
        }
    }

    public static GameInfo CreateGame(string name, GamePlatform platform, string installPath, string executable, List<string> candidates, string platformId, DateTime? lastLaunch)
    {
        return new GameInfo
        {
            Name = name.Trim(),
            Platform = platform,
            InstallPath = installPath,
            ExecutablePath = executable,
            ExecutableCandidates = candidates,
            PlatformId = platformId,
            LastLaunch = lastLaunch
        };
    }
}
