namespace GameBoost.Core.Models;

public sealed class AppSettings
{
    public string Theme { get; set; } = "Dark";
    public bool StartWithWindows { get; set; }
    public bool StartElevated { get; set; }
    public int AnalysisIntervalMinutes { get; set; } = 30;
    public bool OverlayEnabled { get; set; } = true;
    public bool NotificationsEnabled { get; set; } = true;
    public bool AutoCloseAppsEnabled { get; set; }
    public List<string> AutoCloseApps { get; set; } = new();
    public List<string> NeverCloseApps { get; set; } = new() { "explorer", "dwm", "csrss", "svchost", "lsass", "System", "fontdrvhost", "ShellExperienceHost", "SearchHost", "TextInputHost", "dwm", "GameBoost" };
    public List<string> Exclusions { get; set; } = new();
    public List<string> CustomGameFolders { get; set; } = new();
    public string BackupLocation { get; set; } = string.Empty;
    public int LogRetentionDays { get; set; } = 14;
    public bool ShowElevationBanner { get; set; } = true;
    public bool ConfirmBeforeBoost { get; set; } = true;
    public bool MonitorFpsOverlayInGame { get; set; } = true;
    public int HistoryRetentionDays { get; set; } = 90;
    public bool RecordSessions { get; set; } = true;
    public bool RelaunchClosedApps { get; set; } = true;
    public bool ClearTempOnBoost { get; set; }
    public int? OverlayOpacity { get; set; } = 85;
    public string Language { get; set; } = "fr";
}

public sealed class OverlaySettings
{
    public bool Enabled { get; set; } = true;
    public bool ShowFps { get; set; } = true;
    public bool ShowFrameTime { get; set; } = true;
    public bool ShowCpu { get; set; } = true;
    public bool ShowGpu { get; set; } = true;
    public bool ShowRam { get; set; } = true;
    public bool ShowTemperatures { get; set; } = true;
    public int Opacity { get; set; } = 85;
    public int PositionX { get; set; } = 24;
    public int PositionY { get; set; } = 24;
    public string Theme { get; set; } = "Dark";
}

public sealed class AnalysisProgress
{
    public string Stage { get; set; } = string.Empty;
    public int Percent { get; set; }
    public string Detail { get; set; } = string.Empty;
}

public sealed class ReportExportResult
{
    public bool Success { get; set; }
    public string FilePath { get; set; } = string.Empty;
    public string Error { get; set; } = string.Empty;
}

public sealed class UserNotification
{
    public DateTime Timestamp { get; set; } = DateTime.Now;
    public string Title { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public HealthLevel Level { get; set; } = HealthLevel.Unknown;
}

public sealed class GameSettingsMapping
{
    public string Platform { get; set; } = string.Empty;
    public string[] ConfigFilePatterns { get; set; } = Array.Empty<string>();
    public Dictionary<string, string> KeyAliases { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public string Format { get; set; } = "ini";
}
