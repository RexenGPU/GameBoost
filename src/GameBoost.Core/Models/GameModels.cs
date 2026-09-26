namespace GameBoost.Core.Models;

public enum GamePlatform
{
    Steam,
    Epic,
    Ubisoft,
    Xbox,
    Gog,
    BattleNet,
    Riot,
    Manual,
    Other
}

public sealed class GameInfo
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = string.Empty;
    public GamePlatform Platform { get; set; } = GamePlatform.Manual;
    public string InstallPath { get; set; } = string.Empty;
    public string ExecutablePath { get; set; } = string.Empty;
    public string IconPath { get; set; } = string.Empty;
    public string InstallSize { get; set; } = string.Empty;
    public DateTime? LastLaunch { get; set; }
    public string LaunchArguments { get; set; } = string.Empty;
    public string KnownSettingsSummary { get; set; } = string.Empty;
    public bool OverlayEnabled { get; set; } = true;
    public bool IsManuallyAdded { get; set; }
    public string PlatformId { get; set; } = string.Empty;
    public List<string> ExecutableCandidates { get; set; } = new();
    public bool IsRunning { get; set; }
    public int RunningProcessId { get; set; }
}

public enum ProfilePreset
{
    MaximumQuality,
    Balanced,
    Performance,
    Custom
}

public sealed class ProfileSettings
{
    public int? ResolutionWidth { get; set; }
    public int? ResolutionHeight { get; set; }
    public string TextureQuality { get; set; } = "Non défini";
    public string ShadowQuality { get; set; } = "Non défini";
    public string LightingQuality { get; set; } = "Non défini";
    public bool? RayTracing { get; set; }
    public bool? Dlss { get; set; }
    public bool? Fsr { get; set; }
    public bool? Xess { get; set; }
    public bool? VSync { get; set; }
    public int? FpsLimit { get; set; }
    public bool? Fullscreen { get; set; }
    public string ExtraNotes { get; set; } = string.Empty;
}

public sealed class GameProfile
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string GameId { get; set; } = string.Empty;
    public string GameName { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public ProfilePreset Preset { get; set; } = ProfilePreset.Balanced;
    public ProfileSettings Settings { get; set; } = new();
    public DateTime UpdatedAt { get; set; } = DateTime.Now;
    public bool IsActiveForBoost { get; set; }
    public bool SupportedByConfigPatch { get; set; }
    public string ConfigFileHint { get; set; } = string.Empty;
}

public sealed class ApplyProfileResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public string? BackupPath { get; set; }
    public List<string> ChangedKeys { get; set; } = new();
    public bool RequiresElevation { get; set; }
}
