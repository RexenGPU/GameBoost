using System.ComponentModel;
using System.Runtime.CompilerServices;
using GameBoost.Core.Localization;
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
        ProfilePreset.MaximumQuality => Loc.T("Prof_PresetQuality"),
        ProfilePreset.Balanced => Loc.T("Prof_PresetBalanced"),
        ProfilePreset.Performance => Loc.T("Prof_PresetPerformance"),
        _ => Loc.T("Prof_PresetCustom")
    };

    public string ActiveLabel => _isActive ? Loc.T("Prof_LabelActive") : Loc.T("Prof_LabelInactive");

    public string UpdatedLabel => Loc.T("Prof_UpdatedLabel", Profile.UpdatedAt.ToString("dd/MM/yyyy HH:mm"));

    public static List<string> BuildBullets(ProfileSettings settings)
    {
        var bullets = new List<string>();
        if (settings is null) return bullets;

        bullets.Add(settings.ResolutionWidth is int w && settings.ResolutionHeight is int h
            ? Loc.T("Prof_BulletResolution", w, h)
            : Loc.T("Prof_BulletResKeep"));

        AddQuality(bullets, Loc.T("Prof_LblTextures"), settings.TextureQuality);
        AddQuality(bullets, Loc.T("Prof_LblShadows"), settings.ShadowQuality);
        AddQuality(bullets, Loc.T("Prof_LblLighting"), settings.LightingQuality);

        AddFlag(bullets, "Ray tracing", settings.RayTracing);
        AddFlag(bullets, "DLSS", settings.Dlss);
        AddFlag(bullets, "FSR", settings.Fsr);
        AddFlag(bullets, "XeSS", settings.Xess);
        AddFlag(bullets, "V-Sync", settings.VSync);
        AddFlag(bullets, Loc.T("Prof_LblFullscreen"), settings.Fullscreen);

        if (settings.FpsLimit is int fps) bullets.Add(Loc.T("Prof_BulletFps", fps));
        if (!string.IsNullOrWhiteSpace(settings.ExtraNotes))
            bullets.Add(Loc.T("Prof_BulletNotes", settings.ExtraNotes));

        if (bullets.Count == 0) bullets.Add(Loc.T("Prof_NoSettings"));
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
        bullets.Add(label + " : " + Loc.T(value.Value ? "Prof_FlagOn" : "Prof_FlagOff"));
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
