namespace GameBoost.Core.Models;

public sealed class CheckResult
{
    public string Id { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public HealthLevel Level { get; set; } = HealthLevel.Unknown;
    public string Explanation { get; set; } = string.Empty;
    public string Impact { get; set; } = string.Empty;
    public string Solution { get; set; } = string.Empty;
    public string CurrentValue { get; set; } = string.Empty;
    public bool CanAutoFix { get; set; }
    public string? FixActionId { get; set; }
    public string FixDescription { get; set; } = string.Empty;
}

public sealed class AnalysisReport
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public List<CheckResult> Checks { get; set; } = new();
    public HardwareReport? Hardware { get; set; }
    public MonitorSample? Snapshot { get; set; }
    public int GoodCount => Checks.Count(c => c.Level == HealthLevel.Good);
    public int WarningCount => Checks.Count(c => c.Level == HealthLevel.Warning);
    public int CriticalCount => Checks.Count(c => c.Level == HealthLevel.Critical);
    public string OverallSummary { get; set; } = string.Empty;
}

public sealed class FixOutcome
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public bool RequiresElevation { get; set; }
    public string? BackupPath { get; set; }
}

public enum BoostActionKind
{
    CloseProcesses,
    SetProcessPriority,
    EnableGameMode,
    SetPowerPlan,
    ReleaseMemory,
    ApplyProfile,
    ClearTempFiles,
    ToggleOverlay,
    FocusGame
}

public sealed class BoostStep
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public BoostActionKind Kind { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Detail { get; set; } = string.Empty;
    public bool Enabled { get; set; } = true;
    public bool RequiresElevation { get; set; }
    public bool RequiresConfirmation { get; set; }
    public List<string> AffectedItems { get; set; } = new();
    public string RevertHint { get; set; } = string.Empty;
    public string? Payload { get; set; }
}

public sealed class BoostPlan
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string GameId { get; set; } = string.Empty;
    public string GameName { get; set; } = string.Empty;
    public string? ProfileName { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public List<BoostStep> Steps { get; set; } = new();
    public bool ElevationAvailable { get; set; }
}

public sealed class BoostStepResult
{
    public string StepId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public bool Success { get; set; }
    public bool Skipped { get; set; }
    public string Message { get; set; } = string.Empty;
}

public sealed class BoostSessionState
{
    public Guid SessionId { get; set; } = Guid.NewGuid();
    public string GameName { get; set; } = string.Empty;
    public string GameId { get; set; } = string.Empty;
    public DateTime StartedAt { get; set; } = DateTime.Now;
    public DateTime? EndedAt { get; set; }
    public List<BoostStepResult> Results { get; set; } = new();
    public List<string> Backups { get; set; } = new();
    public List<string> ClosedProcesses { get; set; } = new();
    public List<string> RelaunchPaths { get; set; } = new();
    public Dictionary<string, string> RevertData { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public bool IsActive { get; set; }
    public List<string> AppliedOptimizations { get; set; } = new();
}
