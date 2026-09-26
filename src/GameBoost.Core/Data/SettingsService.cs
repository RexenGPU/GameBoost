using System.Text.Json;
using GameBoost.Core.Logging;
using GameBoost.Core.Models;

namespace GameBoost.Core.Data;

public static class SettingsService
{
    private static readonly object Sync = new();
    private static AppSettings? _current;

    public static event Action<AppSettings>? Changed;

    public static AppSettings Current
    {
        get
        {
            lock (Sync)
            {
                return _current ??= Load();
            }
        }
    }

    private static AppSettings Load()
    {
        try
        {
            AppPaths.Ensure();
            if (File.Exists(AppPaths.SettingsPath))
            {
                var json = File.ReadAllText(AppPaths.SettingsPath);
                var settings = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions);
                if (settings is not null) return settings;
            }
        }
        catch (Exception ex)
        {
            Log.Error("Settings", "Lecture des paramètres impossible, valeurs par défaut utilisées", ex);
        }
        return new AppSettings();
    }

    public static void Save(AppSettings settings)
    {
        lock (Sync)
        {
            _current = settings;
            try
            {
                AppPaths.Ensure();
                File.WriteAllText(AppPaths.SettingsPath, JsonSerializer.Serialize(settings, JsonOptions));
            }
            catch (Exception ex)
            {
                Log.Error("Settings", "Sauvegarde des paramètres impossible", ex);
                throw;
            }
        }
        Changed?.Invoke(settings);
    }

    public static void Update(Action<AppSettings> mutate)
    {
        var copy = Current;
        mutate(copy);
        Save(copy);
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };
}
