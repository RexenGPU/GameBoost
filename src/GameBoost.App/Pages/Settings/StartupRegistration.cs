using System.IO;
using System.Reflection;
using System.Text.Json;
using GameBoost.Core.Data;
using GameBoost.Core.Logging;
using Microsoft.Win32;

namespace GameBoost.App.Pages.Settings;

public static class StartupRegistration
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "GameBoost";

    public static bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, false);
        return key?.GetValue(ValueName) is not null;
    }

    public static string ReadValue()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, false);
            return key?.GetValue(ValueName)?.ToString() ?? string.Empty;
        }
        catch (Exception ex)
        {
            Log.Error("Settings", "Lecture du démarrage automatique impossible", ex);
            return string.Empty;
        }
    }

    public static void SetEnabled(bool enabled)
    {
        var previous = ReadValue();
        Backup(previous, enabled);

        if (enabled)
        {
            var path = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(path))
            {
                var entry = Assembly.GetEntryAssembly()?.Location;
                if (string.IsNullOrWhiteSpace(entry))
                    throw new InvalidOperationException("Chemin de l'application introuvable.");
                path = entry;
            }
            using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath, true);
            key.SetValue(ValueName, "\"" + path + "\"");
        }
        else
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath, true);
            key.DeleteValue(ValueName, false);
        }
    }

    private static void Backup(string previousValue, bool newValue)
    {
        try
        {
            var folder = AppPaths.CreateBackupFolder("settings");
            var payload = JsonSerializer.Serialize(new
            {
                date = DateTime.Now,
                cle = "HKCU\\" + RunKeyPath,
                valeur = ValueName,
                ancienneValeur = previousValue,
                nouvelleActive = newValue
            }, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(Path.Combine(folder, "demarrage-windows.json"), payload);
        }
        catch (Exception ex)
        {
            Log.Error("Settings", "Sauvegarde de l'état du démarrage automatique impossible", ex);
        }
    }
}
