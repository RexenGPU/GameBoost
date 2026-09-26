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
            ShellState.Status("Dernière analyse du " + latest.DateLabel + " affichée sans relancer de vérification.");
        }
    }

    private async void OnAnalyzeClick(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        _busy = true;
        AnalyzeButton.IsEnabled = false;
        ProgressFill.Value = 0;
        PercentText.Text = "0 %";
        StageText.Text = "Analyse en cours…";
        DetailText.Text = "Préparation des vérifications…";
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
            ShellState.Status("Analyse terminée : " + report.GoodCount + " optimal, " +
                              report.WarningCount + " à surveiller, " + report.CriticalCount + " problèmes.");
        }
        catch (Exception ex)
        {
            Log.Error("UI", "Analyse de la configuration", ex);
            ShellState.Status("L'analyse a échoué : " + ex.Message);
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
            ShellState.Status("Le rapport n'a pas pu être enregistré : " + ex.Message);
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
            ShellState.Status("Historique des analyses indisponible : " + ex.Message);
        }
    }

    private void RenderReport()
    {
        if (_report is null) return;

        GoodCountText.Text = "🟢 " + _report.GoodCount + " optimal";
        WarningCountText.Text = "🟡 " + _report.WarningCount + " à surveiller";
        CriticalCountText.Text = "🔴 " + _report.CriticalCount + " problèmes";
        ReportDateText.Text = "Analyse du " + _report.CreatedAt.ToString("dddd d MMMM yyyy 'à' HH:mm", French);
        SummaryText.Text = string.IsNullOrWhiteSpace(_report.OverallSummary)
            ? "Récapitulatif non disponible pour ce rapport."
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
        FilterCountText.Text = ordered.Count + " vérification(s)";
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
                ShellState.Status("Page « " + target + " » indisponible : ouvrez-la depuis le menu de gauche.");
                return;
            }
            NavigationService.Navigate(target);
            var hint = string.IsNullOrWhiteSpace(item.Result.FixDescription)
                ? "Suivez la procédure indiquée sur cette page."
                : item.Result.FixDescription;
            ShellState.Status("Ouverture de la page « " + target + " » — " + hint);
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
                    ? outcome.Message + " Relancez GameBoost en administrateur pour appliquer cette correction."
                    : outcome.Message;
                ElevationPanel.Visibility = Visibility.Visible;
            }

            RenderChecks();
            ShellState.Status(outcome.Message);
        }
        catch (Exception ex)
        {
            Log.Error("UI", "Correction " + actionId, ex);
            ShellState.Status("La correction a échoué : " + ex.Message);
        }
    }

    private void OnRelaunchElevatedClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var path = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(path))
                throw new InvalidOperationException("Chemin de GameBoost introuvable.");
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true, Verb = "runas" });
            ShellState.Status("Relance en administrateur demandée.");
        }
        catch (Exception ex)
        {
            Log.Error("UI", "Relance en administrateur", ex);
            ShellState.Status("Relance impossible : " + ex.Message);
        }
    }

    private void OnExportReportClick(object sender, RoutedEventArgs e)
    {
        if (_report is null)
        {
            ShellState.Status("Aucun rapport d'analyse à exporter : lancez d'abord une analyse.");
            return;
        }
        try
        {
            var sessions = HistoryService.Instance.GetSessions(10);
            var result = ReportGenerator.Instance.ExportHtml(_report, _report.Hardware, sessions);
            if (!result.Success)
            {
                ShellState.Status("Export impossible : " + result.Error);
                return;
            }
            _exportPath = result.FilePath;
            ExportPathText.Text = result.FilePath;
            ExportPanel.Visibility = Visibility.Visible;
            ShellState.Status("Rapport HTML généré : " + result.FilePath);
        }
        catch (Exception ex)
        {
            Log.Error("UI", "Export du rapport d'analyse", ex);
            ShellState.Status("Export impossible : " + ex.Message);
        }
    }

    private void OnOpenReportClick(object sender, RoutedEventArgs e) => OpenPath(_exportPath, "rapport");

    private void OnOpenFolderClick(object sender, RoutedEventArgs e) =>
        OpenPath(ReportGenerator.Instance.GetExportDirectory(), "dossier des rapports");

    private static void OpenPath(string path, string label)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                ShellState.Status("Aucun " + label + " à ouvrir.");
                return;
            }
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Log.Error("UI", "Ouverture du " + label, ex);
            ShellState.Status("Ouverture impossible : " + ex.Message);
        }
    }

    private void OnRecentClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: RecentAnalysis item }) return;
        try
        {
            _report = item.Report;
            RenderReport();
            ShellState.Status("Analyse du " + item.DateLabel + " rechargée sans nouvelle vérification.");
        }
        catch (Exception ex)
        {
            Log.Error("UI", "Chargement d'un rapport enregistré", ex);
            ShellState.Status("Chargement impossible : " + ex.Message);
        }
    }
}
