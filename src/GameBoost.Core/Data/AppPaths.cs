using System.Security.Principal;

namespace GameBoost.Core.Data;

public static class AppPaths
{
    public static string Root { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GameBoost");

    public static string LogsDir => Path.Combine(Root, "logs");
    public static string DatabasePath => Path.Combine(Root, "gameboost.db");
    public static string BackupsDir => Path.Combine(Root, "backups");
    public static string ExportsDir => Path.Combine(Root, "exports");
    public static string CacheDir => Path.Combine(Root, "cache");
    public static string SettingsPath => Path.Combine(Root, "settings.json");

    public static void Ensure()
    {
        Directory.CreateDirectory(Root);
        Directory.CreateDirectory(LogsDir);
        Directory.CreateDirectory(BackupsDir);
        Directory.CreateDirectory(ExportsDir);
        Directory.CreateDirectory(CacheDir);
    }

    public static bool IsElevated
    {
        get
        {
            try
            {
                using var identity = WindowsIdentity.GetCurrent();
                var principal = new WindowsPrincipal(identity);
                return principal.IsInRole(WindowsBuiltInRole.Administrator);
            }
            catch
            {
                return false;
            }
        }
    }

    public static string CreateBackupFolder(string name)
    {
        var dir = Path.Combine(BackupsDir, $"{name}-{DateTime.Now:yyyyMMdd-HHmmss}");
        Directory.CreateDirectory(dir);
        return dir;
    }
}
