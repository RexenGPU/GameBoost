using System.Text;
using System.Text.RegularExpressions;
using GameBoost.Core.Logging;
using GameBoost.Core.Models;

namespace GameBoost.Core.Games;

internal static class SteamDetector
{
    private static readonly Regex VdfLine = new(
        "^\\s*\"(\\d{1,12})\"\\s*$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex AppIdField = new(
        "\"appid\"\\s*\"(\\d+)\"", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex LastPlayField = new(
        "\"(?:LastPlayTime|LastPlayed|LastLaunch)\"\\s*\"(\\d+)\"",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public static List<GameInfo> Detect()
    {
        var games = new List<GameInfo>();
        var libraries = GetLibraryFolders();
        var lastLaunch = LoadLastPlayTimes(libraries);
        foreach (var library in libraries)
        {
            try
            {
                var steamApps = Path.Combine(library, "steamapps");
                if (!Directory.Exists(steamApps)) continue;
                foreach (var manifest in Directory.EnumerateFiles(steamApps, "appmanifest_*.acf"))
                {
                    try
                    {
                        var content = File.ReadAllText(manifest);
                        var name = ReadVdfField(content, "name");
                        var installDir = ReadVdfField(content, "installdir");
                        var appId = ReadVdfField(content, "appid");
                        if (installDir.Length == 0)
                        {
                            Log.Warn("Games", "Manifeste Steam sans dossier d'installation : " + manifest);
                            continue;
                        }
                        if (name.Length == 0) name = installDir;
                        var installPath = Path.Combine(steamApps, "common", installDir);
                        if (!Directory.Exists(installPath))
                        {
                            Log.Warn("Games", "Jeu Steam absent du disque : " + installPath);
                            continue;
                        }
                        var executable = DetectionHelpers.ResolveExecutable(installPath, name, installDir, out var candidates);
                        DateTime? launch = null;
                        if (appId.Length > 0 && lastLaunch.TryGetValue(appId, out var found)) launch = found;
                        var game = DetectionHelpers.CreateGame(name, GamePlatform.Steam, installPath, executable, candidates, appId, launch);
                        DetectionHelpers.Enrich(game);
                        games.Add(game);
                    }
                    catch (Exception ex)
                    {
                        Log.Warn("Games", "Manifeste Steam illisible : " + manifest + " — " + ex.Message);
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Warn("Games", "Bibliothèque Steam illisible : " + library + " — " + ex.Message);
            }
        }
        return games;
    }

    public static List<string> GetLibraryFolders()
    {
        var libraries = new List<string>();
        try
        {
            var steamPath = GetSteamPath();
            if (steamPath.Length > 0) AddUnique(libraries, steamPath);
            if (steamPath.Length > 0)
            {
                var file = Path.Combine(steamPath, "steamapps", "libraryfolders.vdf");
                if (File.Exists(file)) ParseLibraryFolders(file, libraries);
            }
        }
        catch (Exception ex)
        {
            Log.Warn("Games", "Lecture des bibliothèques Steam impossible : " + ex.Message);
        }
        return libraries;
    }

    private static string GetSteamPath()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
            var value = key?.GetValue("SteamPath") as string;
            if (!string.IsNullOrWhiteSpace(value))
            {
                var path = value.Replace('/', '\\').TrimEnd('\\');
                if (Directory.Exists(path)) return path;
            }
        }
        catch (Exception ex)
        {
            Log.Warn("Games", "Registre Steam inaccessible : " + ex.Message);
        }
        string[] fallbacks = { @"C:\Program Files (x86)\Steam", @"C:\Program Files\Steam" };
        foreach (var fallback in fallbacks)
        {
            if (Directory.Exists(fallback)) return fallback;
        }
        return string.Empty;
    }

    private static void ParseLibraryFolders(string file, List<string> libraries)
    {
        var content = File.ReadAllText(file);
        foreach (Match match in Regex.Matches(content, "\"path\"\\s*\"((?:[^\"\\\\]|\\\\.)*)\"", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
        {
            var path = UnescapeVdf(match.Groups[1].Value).Replace('/', '\\').TrimEnd('\\');
            if (path.Length > 0) AddUnique(libraries, path);
        }
        foreach (Match match in Regex.Matches(content, "\"\\d+\"\\s*\"((?:[A-Za-z]:[\\\\/]|\\\\\\\\)[^\"]*)\"", RegexOptions.CultureInvariant))
        {
            var path = UnescapeVdf(match.Groups[1].Value).Replace('/', '\\').TrimEnd('\\');
            if (path.Length > 0) AddUnique(libraries, path);
        }
    }

    private static void AddUnique(List<string> libraries, string path)
    {
        foreach (var existing in libraries)
        {
            if (string.Equals(existing, path, StringComparison.OrdinalIgnoreCase)) return;
        }
        libraries.Add(path);
    }

    private static Dictionary<string, DateTime> LoadLastPlayTimes(List<string> libraries)
    {
        var map = new Dictionary<string, DateTime>(StringComparer.Ordinal);
        var file = FindLocalConfig(libraries);
        if (file is null) return map;
        try
        {
            using var reader = new StreamReader(file);
            string? current = null;
            string? line;
            while ((line = reader.ReadLine()) is not null)
            {
                var match = VdfLine.Match(line);
                if (match.Success)
                {
                    current = match.Groups[1].Value;
                    continue;
                }
                match = AppIdField.Match(line);
                if (match.Success)
                {
                    current = match.Groups[1].Value;
                    continue;
                }
                match = LastPlayField.Match(line);
                if (!match.Success || current is null) continue;
                if (!long.TryParse(match.Groups[1].Value, out var seconds)) continue;
                var stamp = DetectionHelpers.FromUnix(seconds);
                if (stamp is null) continue;
                if (!map.ContainsKey(current)) map[current] = stamp.Value;
            }
        }
        catch (Exception ex)
        {
            Log.Warn("Games", "Lecture de localconfig.vdf impossible : " + ex.Message);
        }
        return map;
    }

    private static string? FindLocalConfig(List<string> libraries)
    {
        var candidates = new List<string>();
        try
        {
            var local = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Valve", "Steam", "config", "localconfig.vdf");
            if (File.Exists(local)) candidates.Add(local);
        }
        catch (Exception ex)
        {
            Log.Warn("Games", "Chemin localconfig introuvable : " + ex.Message);
        }
        foreach (var library in libraries)
        {
            try
            {
                var userdata = Path.Combine(library, "userdata");
                if (!Directory.Exists(userdata)) continue;
                foreach (var user in Directory.EnumerateDirectories(userdata))
                {
                    var file = Path.Combine(user, "config", "localconfig.vdf");
                    if (File.Exists(file)) candidates.Add(file);
                }
            }
            catch (Exception ex)
            {
                Log.Warn("Games", "userdata illisible : " + library + " — " + ex.Message);
            }
        }
        if (candidates.Count == 0) return null;
        string newest = candidates[0];
        var newestTime = DateTime.MinValue;
        foreach (var candidate in candidates)
        {
            try
            {
                var time = File.GetLastWriteTimeUtc(candidate);
                if (time > newestTime)
                {
                    newestTime = time;
                    newest = candidate;
                }
            }
            catch
            {
            }
        }
        return newest;
    }

    internal static string ReadVdfField(string content, string field)
    {
        var match = Regex.Match(content,
            "\"" + Regex.Escape(field) + "\"\\s*\"((?:[^\"\\\\]|\\\\.)*)\"",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        return match.Success ? UnescapeVdf(match.Groups[1].Value) : string.Empty;
    }

    internal static string UnescapeVdf(string value)
    {
        if (value.IndexOf('\\') < 0) return value;
        var builder = new StringBuilder(value.Length);
        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];
            if (c != '\\' || i + 1 >= value.Length)
            {
                builder.Append(c);
                continue;
            }
            var next = value[++i];
            builder.Append(next switch
            {
                'n' => '\n',
                't' => '\t',
                'r' => '\r',
                _ => next
            });
        }
        return builder.ToString();
    }
}
