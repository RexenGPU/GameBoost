using System.ComponentModel;
using GameBoost.Core.Models;

namespace GameBoost.App.Pages.Analysis;

public sealed class CheckItem : INotifyPropertyChanged
{
    private HealthLevel _level;
    private string _autoFixLabel;
    private string _outcomeLabel;
    private string _backupLabel;

    public CheckResult Result { get; }

    public CheckItem(CheckResult result)
    {
        Result = result;
        _level = result.Level;
        _autoFixLabel = BuildAutoFixLabel(result);
        _outcomeLabel = string.Empty;
        _backupLabel = string.Empty;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public HealthLevel Level
    {
        get => _level;
        set
        {
            if (_level == value) return;
            _level = value;
            Result.Level = value;
            Raise(nameof(Level));
        }
    }

    public string Title => Result.Title;
    public string Category => Result.Category;
    public string Explanation => Result.Explanation;
    public string ImpactLabel => string.IsNullOrWhiteSpace(Result.Impact) ? string.Empty : Result.Impact;
    public string SolutionLabel => string.IsNullOrWhiteSpace(Result.Solution) ? string.Empty : Result.Solution;

    public string CurrentValueLabel => string.IsNullOrWhiteSpace(Result.CurrentValue)
        ? string.Empty
        : "Valeur : " + Result.CurrentValue;

    public string AutoFixLabel
    {
        get => _autoFixLabel;
        private set
        {
            if (_autoFixLabel == value) return;
            _autoFixLabel = value;
            Raise(nameof(AutoFixLabel));
        }
    }

    public string ManualLabel => string.IsNullOrWhiteSpace(_autoFixLabel)
        ? "Correction manuelle : appliquez la procédure ci-dessus."
        : string.Empty;

    public string OutcomeLabel
    {
        get => _outcomeLabel;
        private set
        {
            if (_outcomeLabel == value) return;
            _outcomeLabel = value;
            Raise(nameof(OutcomeLabel));
        }
    }

    public string BackupLabel
    {
        get => _backupLabel;
        private set
        {
            if (_backupLabel == value) return;
            _backupLabel = value;
            Raise(nameof(BackupLabel));
        }
    }

    public void ApplyOutcome(FixOutcome outcome, bool makeGood)
    {
        if (outcome is null) return;
        OutcomeLabel = outcome.Message ?? string.Empty;
        BackupLabel = string.IsNullOrWhiteSpace(outcome.BackupPath)
            ? string.Empty
            : "Sauvegarde : " + outcome.BackupPath;
        if (makeGood) Level = HealthLevel.Good;
        if (outcome.Success) AutoFixLabel = string.Empty;
        Raise(nameof(ManualLabel));
    }

    private static string BuildAutoFixLabel(CheckResult result)
    {
        if (!result.CanAutoFix || string.IsNullOrWhiteSpace(result.FixActionId)) return string.Empty;
        return string.IsNullOrWhiteSpace(result.FixDescription) ? "Appliquer la correction" : result.FixDescription;
    }

    private void Raise(string name)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}

public sealed class RecentAnalysis
{
    public AnalysisReport Report { get; }

    public RecentAnalysis(AnalysisReport report)
    {
        Report = report;
        DateLabel = report.CreatedAt.ToString("dd/MM/yyyy HH:mm",
            System.Globalization.CultureInfo.GetCultureInfo("fr-FR"));
        CountersLabel = "🟢 " + report.GoodCount + " optimal   ·   🟡 " + report.WarningCount +
                        " à surveiller   ·   🔴 " + report.CriticalCount + " problèmes";
        SummaryLabel = string.IsNullOrWhiteSpace(report.OverallSummary)
            ? "Récapitulatif non disponible"
            : report.OverallSummary;
    }

    public string DateLabel { get; }
    public string CountersLabel { get; }
    public string SummaryLabel { get; }
}
