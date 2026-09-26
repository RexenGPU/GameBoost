using System.Drawing;
using System.Drawing.Imaging;
using System.Security.Cryptography;
using System.Text;
using GameBoost.Core.Data;
using GameBoost.Core.Logging;

namespace GameBoost.Core.Processes;

internal static class ProcessIconCache
{
    private static readonly object Sync = new();
    private static readonly Dictionary<string, string> Cache = new(StringComparer.OrdinalIgnoreCase);

    public static string GetIconPath(string processName, string executablePath)
    {
        if (string.IsNullOrWhiteSpace(executablePath)) return string.Empty;

        lock (Sync)
        {
            if (Cache.TryGetValue(executablePath, out var cached)) return cached;
        }

        var result = string.Empty;
        try
        {
            if (File.Exists(executablePath))
            {
                var directory = Path.Combine(AppPaths.CacheDir, "icons");
                Directory.CreateDirectory(directory);
                var fileName = BuildFileName(processName, executablePath);
                var target = Path.Combine(directory, fileName);
                if (!File.Exists(target))
                {
                    using var icon = Icon.ExtractAssociatedIcon(executablePath);
                    if (icon is not null)
                    {
                        using var bitmap = icon.ToBitmap();
                        bitmap.Save(target, ImageFormat.Png);
                    }
                }
                if (File.Exists(target)) result = target;
            }
        }
        catch (Exception ex)
        {
            Log.Warn("Processes", "Icône indisponible pour " + executablePath + " : " + ex.Message);
            result = string.Empty;
        }

        lock (Sync)
        {
            Cache[executablePath] = result;
        }
        return result;
    }

    private static string BuildFileName(string processName, string executablePath)
    {
        var safeName = new StringBuilder();
        foreach (var character in string.IsNullOrWhiteSpace(processName) ? "process" : processName)
        {
            safeName.Append(char.IsLetterOrDigit(character) || character is '-' or '_' ? character : '_');
        }
        while (safeName.Length > 0 && safeName[^1] == '_') safeName.Length--;
        if (safeName.Length == 0) safeName.Append("process");

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(executablePath.ToLowerInvariant()));
        var suffix = Convert.ToHexString(hash, 0, 6).ToLowerInvariant();
        return safeName + "-" + suffix + ".png";
    }
}
