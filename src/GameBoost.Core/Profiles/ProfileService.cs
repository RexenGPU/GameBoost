using GameBoost.Core.Data;
using GameBoost.Core.Logging;
using GameBoost.Core.Models;

namespace GameBoost.Core.Profiles;

public sealed class ProfileService
{
    public static ProfileService Instance { get; } = new();

    private const string CollectionName = "profiles";

    private ProfileService() { }

    public List<GameProfile> GetProfiles(string gameId)
    {
        if (string.IsNullOrWhiteSpace(gameId)) return new List<GameProfile>();
        try
        {
            if (!Db.TryGetCollection<GameProfile>(CollectionName, out var collection)) return new List<GameProfile>();
            return collection.Find(p => p.GameId == gameId).ToList();
        }
        catch (Exception ex)
        {
            Log.Error("Profiles", "Lecture des profils du jeu impossible", ex);
            return new List<GameProfile>();
        }
    }

    public List<GameProfile> GetAllProfiles()
    {
        try
        {
            if (!Db.TryGetCollection<GameProfile>(CollectionName, out var collection)) return new List<GameProfile>();
            return collection.FindAll().ToList();
        }
        catch (Exception ex)
        {
            Log.Error("Profiles", "Lecture de tous les profils impossible", ex);
            return new List<GameProfile>();
        }
    }

    public GameProfile? GetProfile(Guid id)
    {
        try
        {
            if (!Db.TryGetCollection<GameProfile>(CollectionName, out var collection)) return null;
            return collection.FindById(id);
        }
        catch (Exception ex)
        {
            Log.Error("Profiles", "Lecture du profil impossible", ex);
            return null;
        }
    }

    public GameProfile? GetActiveProfile(string gameId)
    {
        var profiles = GetProfiles(gameId);
        var active = profiles.FirstOrDefault(p => p.IsActiveForBoost);
        if (active is not null) return active;
        return profiles.OrderByDescending(p => p.UpdatedAt).ThenByDescending(p => p.Id).FirstOrDefault();
    }

    public bool SetActive(Guid profileId)
    {
        try
        {
            if (!Db.TryGetCollection<GameProfile>(CollectionName, out var collection)) return false;
            var target = collection.FindById(profileId);
            if (target is null)
            {
                Log.Warn("Profiles", "Profil introuvable : " + profileId);
                return false;
            }

            var siblings = collection.Find(p => p.GameId == target.GameId).ToList();
            if (siblings.All(p => p.Id != target.Id)) siblings.Add(target);

            foreach (var profile in siblings)
            {
                var shouldBeActive = profile.Id == profileId;
                if (profile.IsActiveForBoost == shouldBeActive) continue;
                profile.IsActiveForBoost = shouldBeActive;
                collection.Upsert(profile);
            }
            Log.Info("Profiles", "Profil actif défini : " + target.Name);
            return true;
        }
        catch (Exception ex)
        {
            Log.Error("Profiles", "Activation du profil impossible", ex);
            return false;
        }
    }

    public void Save(GameProfile profile)
    {
        if (profile is null)
        {
            Log.Warn("Profiles", "Profil vide ignoré");
            return;
        }
        profile.UpdatedAt = DateTime.Now;
        try
        {
            if (!Db.TryGetCollection<GameProfile>(CollectionName, out var collection)) return;
            collection.Upsert(profile);
        }
        catch (Exception ex)
        {
            Log.Error("Profiles", "Enregistrement du profil impossible", ex);
        }
    }

    public bool Delete(Guid id)
    {
        try
        {
            if (!Db.TryGetCollection<GameProfile>(CollectionName, out var collection)) return false;
            var deleted = collection.Delete(id);
            if (!deleted) Log.Warn("Profiles", "Profil introuvable pour suppression : " + id);
            return deleted;
        }
        catch (Exception ex)
        {
            Log.Error("Profiles", "Suppression du profil impossible", ex);
            return false;
        }
    }

    public List<GameProfile> CreatePresets(string gameId, string gameName)
    {
        if (string.IsNullOrWhiteSpace(gameId))
        {
            Log.Warn("Profiles", "Création des profils ignorée : identifiant de jeu vide");
            return new List<GameProfile>();
        }

        try
        {
            if (!Db.TryGetCollection<GameProfile>(CollectionName, out var collection)) return new List<GameProfile>();
            var existing = collection.Find(p => p.GameId == gameId).ToList();
            var presets = new[]
            {
                ProfilePreset.MaximumQuality,
                ProfilePreset.Balanced,
                ProfilePreset.Performance,
                ProfilePreset.Custom
            };

            foreach (var preset in presets)
            {
                if (existing.Any(p => p.Preset == preset)) continue;
                var profile = new GameProfile
                {
                    GameId = gameId,
                    GameName = gameName ?? string.Empty,
                    Name = DisplayName(preset),
                    Preset = preset,
                    Settings = CreatePresetSettings(preset),
                    IsActiveForBoost = false,
                    SupportedByConfigPatch = false,
                    ConfigFileHint = string.Empty
                };
                collection.Insert(profile);
                Log.Info("Profiles", "Profil créé pour " + gameName + " : " + profile.Name);
            }
        }
        catch (Exception ex)
        {
            Log.Error("Profiles", "Création des profils par défaut impossible", ex);
        }

        return GetProfiles(gameId);
    }

    public ProfileSettings CreatePresetSettings(ProfilePreset preset, GpuCapabilities? capabilities = null)
    {
        var rayTracing = capabilities?.RayTracing == true;
        var dlss = capabilities?.Dlss == true;
        var fsr = capabilities?.Fsr == true;
        var xess = capabilities?.Xess == true;

        switch (preset)
        {
            case ProfilePreset.MaximumQuality:
                return new ProfileSettings
                {
                    TextureQuality = "Ultra",
                    ShadowQuality = "Ultra",
                    LightingQuality = "Ultra",
                    RayTracing = rayTracing,
                    Dlss = dlss,
                    Fsr = fsr,
                    Xess = xess,
                    VSync = true,
                    FpsLimit = null,
                    Fullscreen = true,
                    ResolutionWidth = null,
                    ResolutionHeight = null
                };
            case ProfilePreset.Balanced:
                return new ProfileSettings
                {
                    TextureQuality = "Élevé",
                    ShadowQuality = "Élevé",
                    LightingQuality = "Moyen",
                    RayTracing = false,
                    Dlss = dlss,
                    Fsr = fsr,
                    Xess = xess,
                    VSync = true,
                    FpsLimit = null,
                    Fullscreen = true,
                    ResolutionWidth = null,
                    ResolutionHeight = null
                };
            case ProfilePreset.Performance:
                return new ProfileSettings
                {
                    TextureQuality = "Faible",
                    ShadowQuality = "Faible",
                    LightingQuality = "Moyen",
                    RayTracing = false,
                    Dlss = dlss,
                    Fsr = fsr,
                    Xess = xess,
                    VSync = false,
                    FpsLimit = null,
                    Fullscreen = true,
                    ResolutionWidth = null,
                    ResolutionHeight = null
                };
            default:
                return new ProfileSettings
                {
                    TextureQuality = "Non défini",
                    ShadowQuality = "Non défini",
                    LightingQuality = "Non défini",
                    RayTracing = null,
                    Dlss = null,
                    Fsr = null,
                    Xess = null,
                    VSync = null,
                    FpsLimit = null,
                    Fullscreen = null,
                    ResolutionWidth = null,
                    ResolutionHeight = null
                };
        }
    }

    public bool IsConfigSupported(GameInfo game)
    {
        if (game is null) return false;
        return ProfileApplier.Instance.CanApply(game, out _);
    }

    private static string DisplayName(ProfilePreset preset)
    {
        switch (preset)
        {
            case ProfilePreset.MaximumQuality:
                return "Qualité maximale";
            case ProfilePreset.Balanced:
                return "Équilibré";
            case ProfilePreset.Performance:
                return "Performance";
            default:
                return "Personnalisé";
        }
    }
}
