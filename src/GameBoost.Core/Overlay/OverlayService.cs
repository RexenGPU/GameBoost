using GameBoost.Core.Data;
using GameBoost.Core.Logging;
using GameBoost.Core.Models;
using LiteDB;

namespace GameBoost.Core.Overlay;

public sealed class OverlaySettingEntity
{
    [BsonId]
    public string GameId { get; set; } = string.Empty;

    public OverlaySettings Settings { get; set; } = new();
}

public sealed class OverlayService
{
    public static OverlayService Instance { get; } = new();

    private const string CollectionName = "overlay";
    private const string DefaultKey = "__default__";
    private const int MaxOpacity = 100;
    private const int MinOpacity = 0;

    private OverlayService()
    {
    }

    public OverlaySettings GetForGame(string gameId)
    {
        if (string.IsNullOrWhiteSpace(gameId)) return GetDefault();

        var key = gameId.Trim();
        try
        {
            if (Db.TryGetCollection<OverlaySettingEntity>(CollectionName, out var collection))
            {
                var entity = collection.FindById(key);
                if (entity?.Settings is not null)
                {
                    Log.Debug("Overlay", "Reglages overlay lus pour le jeu " + key);
                    return entity.Settings;
                }
            }
        }
        catch (Exception ex)
        {
            Log.Error("Overlay", "Lecture des reglages overlay impossible", ex);
        }

        return GetDefault();
    }

    public void SaveForGame(string gameId, OverlaySettings settings)
    {
        if (settings is null)
        {
            Log.Warn("Overlay", "Reglages overlay manquants, sauvegarde ignoree");
            return;
        }

        if (string.IsNullOrWhiteSpace(gameId))
        {
            Log.Warn("Overlay", "Identifiant de jeu vide, reglages enregistres comme valeurs par defaut");
            SaveDefault(settings);
            return;
        }

        var key = gameId.Trim();
        try
        {
            Normalize(settings);
            if (Db.TryGetCollection<OverlaySettingEntity>(CollectionName, out var collection))
            {
                collection.Upsert(new OverlaySettingEntity { GameId = key, Settings = settings });
                Log.Info("Overlay", "Reglages overlay enregistres pour le jeu " + key);
            }
        }
        catch (Exception ex)
        {
            Log.Error("Overlay", "Enregistrement des reglages overlay impossible", ex);
        }
    }

    public void RemoveForGame(string gameId)
    {
        if (string.IsNullOrWhiteSpace(gameId)) return;

        var key = gameId.Trim();
        try
        {
            if (Db.TryGetCollection<OverlaySettingEntity>(CollectionName, out var collection))
            {
                var removed = collection.Delete(key);
                Log.Info("Overlay", removed
                    ? "Reglages overlay supprimes pour le jeu " + key
                    : "Aucun reglage overlay a supprimer pour le jeu " + key);
            }
        }
        catch (Exception ex)
        {
            Log.Error("Overlay", "Suppression des reglages overlay impossible", ex);
        }
    }

    public OverlaySettings GetDefault()
    {
        try
        {
            if (Db.TryGetCollection<OverlaySettingEntity>(CollectionName, out var collection))
            {
                var entity = collection.FindById(DefaultKey);
                if (entity?.Settings is not null)
                {
                    SynchronizeAppSettings(entity.Settings);
                    return entity.Settings;
                }
            }
        }
        catch (Exception ex)
        {
            Log.Error("Overlay", "Lecture des reglages overlay par defaut impossible", ex);
        }

        var current = SettingsService.Current;
        var fallback = new OverlaySettings
        {
            Enabled = current.OverlayEnabled,
            Opacity = current.OverlayOpacity ?? 85
        };
        Normalize(fallback);
        return fallback;
    }

    public void SaveDefault(OverlaySettings settings)
    {
        if (settings is null)
        {
            Log.Warn("Overlay", "Reglages overlay par defaut manquants, sauvegarde ignoree");
            return;
        }

        try
        {
            Normalize(settings);
            if (Db.TryGetCollection<OverlaySettingEntity>(CollectionName, out var collection))
            {
                collection.Upsert(new OverlaySettingEntity { GameId = DefaultKey, Settings = settings });
            }
            SynchronizeAppSettings(settings);
            Log.Info("Overlay", "Reglages overlay par defaut enregistres");
        }
        catch (Exception ex)
        {
            Log.Error("Overlay", "Enregistrement des reglages par defaut impossible", ex);
        }
    }

    private static void SynchronizeAppSettings(OverlaySettings settings)
    {
        try
        {
            var current = SettingsService.Current;
            if (current.OverlayEnabled == settings.Enabled && current.OverlayOpacity == settings.Opacity) return;
            SettingsService.Update(s =>
            {
                s.OverlayEnabled = settings.Enabled;
                s.OverlayOpacity = settings.Opacity;
            });
            Log.Debug("Overlay", "Parametres applicatifs synchronises avec l'overlay");
        }
        catch (Exception ex)
        {
            Log.Error("Overlay", "Synchronisation des parametres applicatifs impossible", ex);
        }
    }

    private static void Normalize(OverlaySettings settings)
    {
        if (settings.Opacity > MaxOpacity) settings.Opacity = MaxOpacity;
        if (settings.Opacity < MinOpacity) settings.Opacity = MinOpacity;
        if (string.IsNullOrWhiteSpace(settings.Theme)) settings.Theme = "Dark";
    }
}
