using System.ComponentModel;
using System.Runtime.CompilerServices;
using GameBoost.Core.Models;

namespace GameBoost.App.Pages.Profiles;

public sealed class ProfileCard : INotifyPropertyChanged
{
    private bool _isActive;

    public ProfileCard(GameProfile profile)
    {
        Profile = profile;
        Bullets = BuildBullets(profile.Settings);
        _isActive = profile.IsActiveForBoost;
    }

    public GameProfile Profile { get; }

    public List<string> Bullets { get; }

    public bool IsActive
    {
        get => _isActive;
        set
        {
            if (_isActive == value) return;
            _isActive = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ActiveLabel));
        }
    }

    public string PresetLabel => Profile.Preset switch
    {
        ProfilePreset.MaximumQuality => "Qualité maximale",
        ProfilePreset.Balanced => "Équilibré",
        ProfilePreset.Performance => "Performance",
        _ => "Personnalisé"
    };

    public string ActiveLabel => _isActive ? "Profil actif" : "Inactif";

    public string UpdatedLabel => "Modifié le " + Profile.UpdatedAt.ToString("dd/MM/yyyy HH:mm");

    public static List<string> BuildBullets(ProfileSettings settings)
    {
        var bullets = new List<string>();
        if (settings is null) return bullets;

        bullets.Add(settings.ResolutionWidth is int w && settings.ResolutionHeight is int h
            ? "Résolution : " + w + "×" + h
            : "Résolution : conservée dans le jeu");

        AddQuality(bullets, "Textures", settings.TextureQuality);
        AddQuality(bullets, "Ombres", settings.ShadowQuality);
        AddQuality(bullets, "Éclairage", settings.LightingQuality);

        AddFlag(bullets, "Ray tracing", settings.RayTracing);
        AddFlag(bullets, "DLSS", settings.Dlss);
        AddFlag(bullets, "FSR", settings.Fsr);
        AddFlag(bullets, "XeSS", settings.Xess);
        AddFlag(bullets, "V-Sync", settings.VSync);
        AddFlag(bullets, "Plein écran", settings.Fullscreen);

        if (settings.FpsLimit is int fps) bullets.Add("Limite FPS : " + fps);
        if (!string.IsNullOrWhiteSpace(settings.ExtraNotes))
            bullets.Add("Notes : " + settings.ExtraNotes);

        if (bullets.Count == 0) bullets.Add("Aucun réglage défini");
        return bullets;
    }

    private static void AddQuality(List<string> bullets, string label, string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value == "Non défini") return;
        bullets.Add(label + " : " + value);
    }

    private static void AddFlag(List<string> bullets, string label, bool? value)
    {
        if (value is null) return;
        bullets.Add(label + " : " + (value.Value ? "activé" : "désactivé"));
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
