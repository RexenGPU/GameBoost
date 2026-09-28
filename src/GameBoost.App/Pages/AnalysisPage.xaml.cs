using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using GameBoost.App.Pages.Analysis;
using GameBoost.App.Services;
using GameBoost.Core.Analysis;
using GameBoost.Core.Data;
using GameBoost.Core.History;
using GameBoost.Core.Localization;
using GameBoost.Core.Logging;
using GameBoost.Core.Models;
using GameBoost.Core.Reports;

namespace GameBoost.App.Pages;

public partial class AnalysisPage : UserControl
{
    private static readonly string[] NavigationKeys =
    {
        "dashboard", "analysis", "hardware", "gpu", "storage", "games",
        "profiles", "processes", "monitoring", "history", "reports", "settings"
    };

    private static readonly CultureInfo French = CultureInfo.GetCultureInfo("fr-FR");

    private readonly List<CheckItem> _checks = new();
    private AnalysisReport? _report;
    private Action<AnalysisProgress>? _progressHandler;
    private string _filter = "all";
    private string _exportPath = string.Empty;
    private bool _busy;
    private bool _loaded;

    public AnalysisPage()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_loaded) return;
        _loaded = true;
        LoadRecent();
        var latest = RecentList.ItemsSource is IEnumerable<RecentAnalysis> items ? items.FirstOrDefault() : null;
        if (latest is not null)
        {
            _report = latest.Report;
            RenderReport();
            ShellState.Status(Loc.T("Anal_StatusLatestShown", latest.DateLabel));
        }
    }

    private async void OnAnalyzeClick(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        _busy = true;
        AnalyzeButton.IsEnabled = false;
        ProgressFill.Value = 0;
        PercentText.Text = "0 %";
        StageText.Text = Loc.T("Anal_Running");
        DetailText.Text = Loc.T("Anal_Preparing");
        ProgressPanel.Visibility = Visibility.Visible;
        ElevationPanel.Visibility = Visibility.Collapsed;

        _progressHandler = OnAnalysisProgress;
        Analyzer.Instance.Progress += _progressHandler;
        try
        {
            var report = await Analyzer.Instance.RunAsync();
            _report = report;
            SaveReport(report);
            RenderReport();
            LoadRecent();
            ShellState.Status(Loc.T("Anal_StatusDone", report.GoodCount, report.WarningCount, report.CriticalCount));
        }
        catch (Exception ex)
        {
            Log.Error("UI", "Analyse de la configuration", ex);
            ShellState.Status(Loc.T("Anal_StatusFail", ex.Message));
        }
        finally
        {
            if (_progressHandler is not null) Analyzer.Instance.Progress -= _progressHandler;
            _progressHandler = null;
            _busy = false;
            AnalyzeButton.IsEnabled = true;
            ProgressPanel.Visibility = Visibility.Collapsed;
        }
    }

    private void OnAnalysisProgress(AnalysisProgress progress)
    {
        if (progress is null) return;
        try
        {
            Dispatcher.BeginInvoke(() =>
            {
                ProgressFill.Value = Math.Clamp(progress.Percent, 0, 100);
                PercentText.Text = progress.Percent + " %";
                if (!string.IsNullOrWhiteSpace(progress.Stage)) StageText.Text = progress.Stage;
                DetailText.Text = progress.Detail ?? string.Empty;
            });
        }
        catch (Exception ex)
        {
            Log.Warn("UI", "Mise a jour de la progression ignoree : " + ex.Message);
        }
    }

    private void OnRelaunchClick(object sender, RoutedEventArgs e) => OnAnalyzeClick(sender, e);

    private void SaveReport(AnalysisReport report)
    {
        try
        {
            var collection = Db.GetCollection<AnalysisReport>("analyses");
            collection.Upsert(report);
            var all = collection.FindAll().OrderByDescending(r => r.CreatedAt).ToList();
            for (var i = 20; i < all.Count; i++) collection.Delete(all[i].Id);
        }
        catch (Exception ex)
        {
            Log.Error("UI", "Enregistrement du rapport d'analyse", ex);
            ShellState.Status(Loc.T("Anal_StatusSaveFail", ex.Message));
        }
    }

    private void LoadRecent()
    {
        try
        {
            var collection = Db.GetCollection<AnalysisReport>("analyses");
            var items = collection.FindAll()
                .OrderByDescending(r => r.CreatedAt)
                .Take(10)
                .Select(r => new RecentAnalysis(r))
                .ToList();
            RecentList.ItemsSource = items;
            RecentSection.Visibility = items.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        catch (Exception ex)
        {
            Log.Error("UI", "Lecture des dernières analyses", ex);
            ShellState.Status(Loc.T("Anal_StatusHistoryFail", ex.Message));
        }
    }

    private void RenderReport()
    {
        if (_report is null) return;

        GoodCountText.Text = Loc.T("Anal_GoodCount", _report.GoodCount);
        WarningCountText.Text = Loc.T("Anal_WatchCount", _report.WarningCount);
        CriticalCountText.Text = Loc.T("Anal_ProblemCount", _report.CriticalCount);
        ReportDateText.Text = Loc.T("Anal_ReportDate", _report.CreatedAt.ToString("dddd d MMMM yyyy 'à' HH:mm", French));
        SummaryText.Text = string.IsNullOrWhiteSpace(_report.OverallSummary)
            ? Loc.T("Anal_SummaryMissing")
            : _report.OverallSummary;
        SummaryPanel.Visibility = Visibility.Visible;
        ExportReportButton.IsEnabled = true;

        _checks.Clear();
        foreach (var check in _report.Checks) _checks.Add(new CheckItem(check));
        RenderChecks();
    }

    private void RenderChecks()
    {
        IEnumerable<CheckItem> items = _checks;
        if (_filter == "problems") items = items.Where(c => c.Level != HealthLevel.Good);
        else if (_filter == "good") items = items.Where(c => c.Level == HealthLevel.Good);

        var ordered = items
            .OrderBy(c => SeverityRank(c.Level))
            .ThenBy(c => c.Category, StringComparer.OrdinalIgnoreCase)
            .ThenBy(c => c.Title, StringComparer.OrdinalIgnoreCase)
            .ToList();

        ChecksList.ItemsSource = ordered;
        FilterCountText.Text = Loc.T("Anal_CheckCount", ordered.Count);
        NoMatchText.Visibility = _report is not null && ordered.Count == 0
            ? Visibility.Visible
            : Visibility.Collapsed;
        EmptyHintText.Visibility = _report is null ? Visibility.Visible : Visibility.Collapsed;
    }

    private static int SeverityRank(HealthLevel level)
    {
        return level switch
        {
            HealthLevel.Critical => 0,
            HealthLevel.Warning => 1,
            HealthLevel.Unknown => 2,
            _ => 3
        };
    }

    private void OnFilterChecked(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded || sender is not RadioButton { IsChecked: true, Tag: string tag }) return;
        _filter = tag;
        RenderChecks();
    }

    private async void OnFixClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: CheckItem item }) return;
        var actionId = item.Result.FixActionId;
        if (string.IsNullOrWhiteSpace(actionId)) return;

        if (actionId.StartsWith("navigate:", StringComparison.OrdinalIgnoreCase))
        {
            var target = actionId["navigate:".Length..].Trim();
            if (!NavigationKeys.Contains(target, StringComparer.OrdinalIgnoreCase))
            {
                ShellState.Status(Loc.T("Anal_StatusPageMissing", target));
                return;
            }
            NavigationService.Navigate(target);
            var hint = string.IsNullOrWhiteSpace(item.Result.FixDescription)
                ? Loc.T("Anal_FollowSteps")
                : item.Result.FixDescription;
            ShellState.Status(Loc.T("Anal_StatusNavigate", target, hint));
            return;
        }

        try
        {
            var outcome = await Task.Run(() => Analyzer.Instance.ApplyFix(actionId, item.Result.Id));
            var elevate = actionId.Equals("system:elevate", StringComparison.OrdinalIgnoreCase);
            item.ApplyOutcome(outcome, outcome.Success && !elevate && !outcome.RequiresElevation);

            if (elevate || outcome.RequiresElevation)
            {
                ElevationText.Text = outcome.RequiresElevation
                    ? outcome.Message + " " + Loc.T("Anal_ElevateHint")
                    : outcome.Message;
                ElevationPanel.Visibility = Visibility.Visible;
            }

            RenderChecks();
            ShellState.Status(outcome.Message);
        }
        catch (Exception ex)
        {
            Log.Error("UI", "Correction " + actionId, ex);
            ShellState.Status(Loc.T("Anal_StatusFixFail", ex.Message));
        }
    }

    private void OnRelaunchElevatedClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var path = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(path))
                throw new InvalidOperationException(Loc.T("Anal_ExeMissing"));
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true, Verb = "runas" });
            ShellState.Status(Loc.T("Anal_StatusElevateAsked"));
        }
        catch (Exception ex)
        {
            Log.Error("UI", "Relance en administrateur", ex);
            ShellState.Status(Loc.T("Anal_StatusRelaunchFail", ex.Message));
        }
    }

    private void OnExportReportClick(object sender, RoutedEventArgs e)
    {
        if (_report is null)
        {
            ShellState.Status(Loc.T("Anal_StatusNothingToExport"));
            return;
        }
        try
        {
            var sessions = HistoryService.Instance.GetSessions(10);
            var result = ReportGenerator.Instance.ExportHtml(_report, _report.Hardware, sessions);
            if (!result.Success)
            {
                ShellState.Status(Loc.T("Anal_StatusExportFail", result.Error));
                return;
            }
            _exportPath = result.FilePath;
            ExportPathText.Text = result.FilePath;
            ExportPanel.Visibility = Visibility.Visible;
            ShellState.Status(Loc.T("Anal_StatusExportDone", result.FilePath));
        }
        catch (Exception ex)
        {
            Log.Error("UI", "Export du rapport d'analyse", ex);
            ShellState.Status(Loc.T("Anal_StatusExportFail", ex.Message));
        }
    }

    private void OnOpenReportClick(object sender, RoutedEventArgs e) => OpenPath(_exportPath, Loc.T("Anal_LblReport"));

    private void OnOpenFolderClick(object sender, RoutedEventArgs e) =>
        OpenPath(ReportGenerator.Instance.GetExportDirectory(), Loc.T("Anal_LblReportsFolder"));

    private static void OpenPath(string path, string label)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                ShellState.Status(Loc.T("Anal_StatusNothingToOpen", label));
                return;
            }
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Log.Error("UI", "Ouverture du " + label, ex);
            ShellState.Status(Loc.T("Anal_StatusOpenFail", ex.Message));
        }
    }

    private void OnRecentClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: RecentAnalysis item }) return;
        try
        {
            _report = item.Report;
            RenderReport();
            ShellState.Status(Loc.T("Anal_StatusRecentLoaded", item.DateLabel));
        }
        catch (Exception ex)
        {
            Log.Error("UI", "Chargement d'un rapport enregistré", ex);
            ShellState.Status(Loc.T("Anal_StatusLoadFail", ex.Message));
        }
    }
}
