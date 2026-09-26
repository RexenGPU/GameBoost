using GameBoost.Core.Logging;
using GameBoost.Core.Models;
using Microsoft.Win32;

namespace GameBoost.Core.Games;

internal static class GogDetector
{
    private static readonly string[] UninstallPaths =
    {
        @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
        @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"
    };

    public static List<GameInfo> Detect()
    {
        var games = new List<GameInfo>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var uninstallPath in UninstallPaths)
        {
            try
            {
                using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
                using var key = baseKey.OpenSubKey(uninstallPath);
                if (key is null) continue;
                foreach (var subName in key.GetSubKeyNames())
                {
                    try
                    {
                        using var sub = key.OpenSubKey(subName);
                        if (sub is null) continue;
                        var displayName = sub.GetValue("DisplayName") as string ?? string.Empty;
                        var publisher = sub.GetValue("Publisher") as string ?? string.Empty;
                        if (!IsGogEntry(displayName, publisher)) continue;
                        var installDir = (sub.GetValue("InstallLocation") as string ?? string.Empty).TrimEnd('\\', '/');
                        var iconValue = sub.GetValue("DisplayIcon") as string ?? string.Empty;
                        var iconExe = ExtractIconPath(iconValue);
                        if (installDir.Length == 0 && iconExe.Length > 0)
                            installDir = (Path.GetDirectoryName(iconExe) ?? string.Empty).TrimEnd('\\', '/');
                        if (installDir.Length == 0 || !Directory.Exists(installDir))
                        {
                            Log.Warn("Games", "Installation GOG introuvable : " + displayName);
                            continue;
                        }
                        if (!seen.Add(installDir)) continue;
                        var name = displayName.Replace("(GOG)", string.Empty).Trim();
                        if (name.Length == 0) name = Path.GetFileName(installDir);
                        var candidateList = DetectionHelpers.CollectExecutables(installDir, 1);
                        var executable = string.Empty;
                        if (iconExe.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) &&
                            File.Exists(iconExe) &&
                            !DetectionHelpers.IsSupportExe(Path.GetFileNameWithoutExtension(iconExe)))
                        {
                            executable = iconExe;
                        }
                        if (executable.Length == 0)
                            executable = DetectionHelpers.ResolveExecutable(installDir, name, Path.GetFileName(installDir), out candidateList);
                        var game = DetectionHelpers.CreateGame(
                            name, GamePlatform.Gog, installDir, executable, candidateList, subName, null);
                        DetectionHelpers.Enrich(game);
                        games.Add(game);
                    }
                    catch (Exception ex)
                    {
                        Log.Warn("Games", "Entrée de désinstallation illisible : " + subName + " — " + ex.Message);
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Warn("Games", "Registre des désinstallations inaccessible : " + uninstallPath + " — " + ex.Message);
            }
        }
        return games;
    }

    private static bool IsGogEntry(string displayName, string publisher)
    {
        if (displayName.Contains("(GOG)", StringComparison.OrdinalIgnoreCase)) return true;
        if (publisher.IndexOf("GOG", StringComparison.OrdinalIgnoreCase) >= 0) return true;
        if (publisher.IndexOf("CD Projekt", StringComparison.OrdinalIgnoreCase) >= 0) return true;
        return false;
    }

    private static string ExtractIconPath(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        var text = value.Trim();
        if (text.StartsWith('"'))
        {
            var end = text.IndexOf('"', 1);
            return end > 1 ? text[1..end] : text.Trim('"');
        }
        var comma = text.IndexOf(',');
        if (comma > 0) text = text[..comma];
        return text.Trim();
    }
}
