using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using GameBoost.App.Controls;
using GameBoost.App.Services;
using GameBoost.Core.Data;
using GameBoost.Core.Games;
using GameBoost.Core.History;
using GameBoost.Core.Logging;
using GameBoost.Core.Models;
using GameBoost.Core.Optimization;
using GameBoost.Core.Profiles;
using GameBoost.Core.Processes;

namespace GameBoost.App.Pages.Dashboard;

public partial class BoostDialog : Window
{
    private readonly List<GameInfo> _games = new();
    private readonly List<(CheckBox Box, ProcessSnapshot Process)> _processRows = new();
    private GameInfo? _selectedGame;
    private BoostPlan? _plan;
    private bool _loaded;
    private bool _busyPlan;
    private bool _running;
    private bool _sessionActive;
    private bool _historyStarted;
    private int _resultCountAfterRun;

    public BoostDialog()
    {
        InitializeComponent();
        try
        {
            Owner = Application.Current?.MainWindow;
        }
        catch (Exception ex)
        {
            Log.Warn("UI", "Fenêtre parente indisponible : " + ex.Message);
        }
        Loaded += OnDialogLoaded;
    }

    private async void OnDialogLoaded(object sender, RoutedEventArgs e)
    {
        Loaded -= OnDialogLoaded;
        if (_loaded) return;
        _loaded = true;

        try
        {
            ElevationBanner.Visibility = AppPaths.IsElevated ? Visibility.Collapsed : Visibility.Visible;
        }
        catch (Exception ex)
        {
            Log.Warn("UI", "Niveau d'élévation illisible : " + ex.Message);
        }

        try
        {
            var settings = SettingsService.Current;
            TgGameMode.IsChecked = true;
            TgPower.IsChecked = true;
            TgMemory.IsChecked = true;
            TgProfile.IsChecked = true;
            TgOverlay.IsChecked = true;
            TgTemp.IsChecked = settings.ClearTempOnBoost;
        }
        catch (Exception ex)
        {
            Log.Error("UI", "Paramètres indisponibles", ex);
            ShellState.Status("Paramètres indisponibles : " + ex.Message);
        }

        try
        {
            var library = await Task.Run(() => GameScanner.Instance.GetLibrary());
            _games.Clear();
            _games.AddRange(library);
            FillGameCombo();
        }
        catch (Exception ex)
        {
            Log.Error("UI", "Bibliothèque de jeux indisponible", ex);
            ShellState.Status("Bibliothèque de jeux indisponible : " + ex.Message);
            FillGameCombo();
        }

        try
        {
            var processes = await Task.Run(() => ProcessService.Instance.GetProcesses());
            FillProcessList(processes);
        }
        catch (Exception ex)
        {
            Log.Error("UI", "Liste des processus indisponible", ex);
            ShellState.Status("Liste des processus indisponible : " + ex.Message);
            ProcessCountText.Text = "Liste des processus non disponible.";
        }

        UpdateApplyState();
    }

    private void FillGameCombo()
    {
        var options = new List<GameOption> { new("Aucun jeu spécifique", null) };
        foreach (var game in _games) options.Add(new(game.Name, game));
        GameCombo.ItemsSource = options;
        GameCombo.SelectedIndex = 0;
    }

    private async void OnGameSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _selectedGame = (GameCombo.SelectedItem as GameOption)?.Game;
        ProfileHint.Text = string.Empty;
        if (_selectedGame is null) return;

        try
        {
            var game = _selectedGame;
            var profile = await Task.Run(() => ProfileService.Instance.GetActiveProfile(game.Id));
            ProfileHint.Text = profile is null
                ? "Aucun profil actif pour ce jeu : l'étape « Profil » sera ignorée."
                : "Profil actif : " + profile.Name + " (" + PresetLabel(profile.Preset) + ").";
        }
        catch (Exception ex)
        {
            Log.Warn("UI", "Profil actif illisible : " + ex.Message);
            ProfileHint.Text = "Profil actif non disponible.";
        }
    }

    private void FillProcessList(List<ProcessSnapshot> processes)
    {
        ProcessListHost.Children.Clear();
        _processRows.Clear();

        List<ProcessSnapshot> selected;
        try
        {
            var settings = SettingsService.Current;
            var candidates = processes
                .Where(p => p.ProcessId > 4 && !p.IsCritical)
                .OrderByDescending(p => p.CpuPercent)
                .ToList();
            selected = candidates.Take(60).ToList();
            foreach (var process in candidates)
            {
                if (selected.Contains(process)) continue;
                if (settings.AutoCloseApps.Contains(process.Name, StringComparer.OrdinalIgnoreCase))
                    selected.Add(process);
            }
        }
        catch (Exception ex)
        {
            Log.Warn("UI", "Sélection des processus impossible : " + ex.Message);
            selected = processes.Take(40).ToList();
        }

        var anyChecked = false;
        foreach (var process in selected)
        {
            var canClose = false;
            var reason = string.Empty;
            try
            {
                canClose = ProcessService.Instance.CanClose(process, out reason);
            }
            catch (Exception ex)
            {
                Log.Warn("UI", "Règle de fermeture illisible : " + ex.Message);
                reason = "Règle de fermeture illisible";
            }

            var box = new CheckBox
            {
                Style = TryStyle("Check"),
                IsEnabled = canClose,
                IsChecked = canClose && IsAutoClose(process.Name),
                Margin = new Thickness(0, 0, 0, 8),
                Content = BuildProcessLabel(process),
                ToolTip = canClose
                    ? "Coché : cette application sera fermée pendant la session et relancée à la fin si le réglage est actif"
                    : "Fermeture impossible : " + reason
            };
            ProcessListHost.Children.Add(box);
            _processRows.Add((box, process));
            if (box.IsChecked == true) anyChecked = true;
        }

        TgClose.IsChecked = anyChecked;
        var closable = _processRows.Count(r => r.Box.IsEnabled);
        ProcessCountText.Text = _processRows.Count == 0
            ? "Aucune application détectée en cours d'exécution."
            : _processRows.Count + " application(s) en cours, " + closable + " fermable(s).";
    }

    private static bool IsAutoClose(string name)
    {
        try
        {
            return SettingsService.Current.AutoCloseApps.Contains(name, StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception ex)
        {
            Log.Warn("UI", "Paramètres d'auto-fermeture illisibles : " + ex.Message);
            return false;
        }
    }

    private static string BuildProcessLabel(ProcessSnapshot process)
    {
        return (string.IsNullOrWhiteSpace(process.Name) ? "Processus sans nom" : process.Name) +
               "  ·  PID " + process.ProcessId +
               "  ·  " + process.CpuPercent.ToString("F1", CultureInfo.CurrentCulture) + " %  ·  " +
               BytesToSizeConverter.Format(process.MemoryBytes);
    }

    private void OnSummaryClick(object sender, RoutedEventArgs e)
    {
        if (_busyPlan || _running) return;
        _ = BuildPlanAsync();
    }

    private async Task BuildPlanAsync()
    {
        _busyPlan = true;
        SummaryButton.IsEnabled = false;
        try
        {
            var game = _selectedGame;
            var close = TgClose.IsChecked == true;
            var gameMode = TgGameMode.IsChecked == true;
            var power = TgPower.IsChecked == true;
            var memory = TgMemory.IsChecked == true;
            var profile = TgProfile.IsChecked == true;
            var overlay = TgOverlay.IsChecked == true;
            var temp = TgTemp.IsChecked == true;
            var pids = close
                ? _processRows.Where(r => r.Box.IsChecked == true && r.Box.IsEnabled)
                    .Select(r => r.Process.ProcessId)
                    .Distinct()
                    .ToList()
                : new List<int>();

            _plan = await Task.Run(() => BoostService.Instance.BuildPlan(
                game, pids, gameMode, power, memory, profile, overlay, temp));
            RenderSummary(_plan);
            UpdateApplyState();
            ShowStep(2);
        }
        catch (Exception ex)
        {
            Log.Error("UI", "Construction du plan impossible", ex);
            ShellState.Status("Impossible de préparer le résumé : " + ex.Message);
        }
        finally
        {
            _busyPlan = false;
            SummaryButton.IsEnabled = true;
        }
    }

    private void RenderSummary(BoostPlan plan)
    {
        SummaryHost.Children.Clear();
        foreach (var step in plan.Steps)
        {
            var card = new Border
            {
                Style = TryStyle("CardFlat"),
                Margin = new Thickness(0, 0, 0, 10),
                Opacity = step.Enabled ? 1 : 0.65
            };
            var stack = new StackPanel();

            var header = new Grid();
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var title = new TextBlock
            {
                Style = TryStyle("H3"),
                Text = step.Title,
                TextWrapping = TextWrapping.Wrap,
                VerticalAlignment = VerticalAlignment.Center
            };
            header.Children.Add(title);

            if (!string.IsNullOrWhiteSpace(step.RevertHint))
            {
                var reversible = !step.RevertHint.Contains("non réversible", StringComparison.OrdinalIgnoreCase);
                var badge = new Border
                {
                    Background = TryBrush(reversible ? "GoodBgBrush" : "DangerBgBrush"),
                    CornerRadius = new CornerRadius(7),
                    Padding = new Thickness(9, 3, 9, 3),
                    Margin = new Thickness(12, 0, 0, 0),
                    VerticalAlignment = VerticalAlignment.Center
                };
                badge.Child = new TextBlock
                {
                    Style = TryStyle("Caption"),
                    Text = reversible ? "restauré automatiquement" : "non réversible",
                    Foreground = TryBrush(reversible ? "GoodBrush" : "DangerBrush")
                };
                Grid.SetColumn(badge, 1);
                header.Children.Add(badge);
            }

            stack.Children.Add(header);

            if (!string.IsNullOrWhiteSpace(step.Detail))
            {
                var detail = new TextBlock
                {
                    Style = TryStyle("BodyMuted"),
                    Text = step.Enabled ? step.Detail : "Étape ignorée — " + step.Detail,
                    Margin = new Thickness(0, 7, 0, 0)
                };
                if (!step.Enabled) detail.Foreground = TryBrush("WarnBrush");
                stack.Children.Add(detail);
            }

            if (step.Enabled && step.AffectedItems.Count > 0)
            {
                var bullets = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };
                foreach (var item in step.AffectedItems)
                {
                    bullets.Children.Add(new TextBlock
                    {
                        Style = TryStyle("Caption"),
                        Text = "• " + item,
                        Margin = new Thickness(0, 0, 0, 4)
                    });
                }
                stack.Children.Add(bullets);
            }

            card.Child = stack;
            SummaryHost.Children.Add(card);
        }
    }

    private void UpdateApplyState()
    {
        try
        {
            var needsConfirm = SettingsService.Current.ConfirmBeforeBoost;
            UnderstoodCheck.Visibility = needsConfirm ? Visibility.Visible : Visibility.Collapsed;
            var understood = !needsConfirm || UnderstoodCheck.IsChecked == true;
            var hasSteps = _plan is not null && _plan.Steps.Any(step => step.Enabled);
            ApplyButton.IsEnabled = understood && hasSteps;
            ApplyButton.ToolTip = !hasSteps
                ? "Aucune étape du boost ne peut être appliquée avec cette configuration."
                : !understood
                    ? "Cochez « J'ai compris » pour appliquer les optimisations."
                    : "Appliquer les optimisations listées dans le résumé.";
        }
        catch (Exception ex)
        {
            Log.Warn("UI", "État du bouton d'application impossible : " + ex.Message);
            ApplyButton.IsEnabled = _plan is not null;
        }
    }

    private void OnUnderstoodChanged(object sender, RoutedEventArgs e) => UpdateApplyState();

    private void OnBackClick(object sender, RoutedEventArgs e)
    {
        if (_running) return;
        _plan = null;
        ShowStep(1);
    }

    private async void OnApplyClick(object sender, RoutedEventArgs e)
    {
        if (_plan is null || _running) return;
        var confirmed = false;
        try
        {
            confirmed = !SettingsService.Current.ConfirmBeforeBoost || UnderstoodCheck.IsChecked == true;
        }
        catch (Exception ex)
        {
            Log.Warn("UI", "Confirmation impossible à lire : " + ex.Message);
        }
        if (!confirmed) return;

        ShowStep(3);
        PrepareRunView();
        await ExecutePlanAsync(_plan);
    }

    private void PrepareRunView()
    {
        RunProgress.IsIndeterminate = false;
        RunProgress.Minimum = 0;
        RunProgress.Maximum = Math.Max(1, (_plan?.Steps.Count ?? 0) + 1);
        RunProgress.Value = 0;
        RunLabel.Text = "Préparation…";
        ResultsTitle.Visibility = Visibility.Collapsed;
        BackupsTitle.Visibility = Visibility.Collapsed;
        AppliedTitle.Visibility = Visibility.Collapsed;
        RestorePanel.Visibility = Visibility.Collapsed;
        ResultsHost.Children.Clear();
        BackupsHost.Children.Clear();
        AppliedHost.Children.Clear();
        RestoreHost.Children.Clear();
        CloseButton.Visibility = Visibility.Collapsed;
        EndSessionButton.Visibility = Visibility.Visible;
        EndSessionButton.IsEnabled = false;
    }

    private async Task ExecutePlanAsync(BoostPlan plan)
    {
        _running = true;
        var progress = new Progress<string>(message =>
        {
            RunLabel.Text = message;
            if (RunProgress.Value < RunProgress.Maximum) RunProgress.Value += 1;
        });

        try
        {
            var session = await BoostService.Instance.ExecuteAsync(plan, progress, CancellationToken.None);
            _sessionActive = session.IsActive;
            RenderResults(session);
            RunLabel.Text = session.IsActive
                ? "Session active : les optimisations restent en place jusqu'à la fin de session."
                : "Exécution terminée.";
            StartHistory(plan);
            ShellState.Status(session.IsActive
                ? "Boost appliqué : " + session.AppliedOptimizations.Count + " optimisation(s) en place."
                : "Boost terminé sans session active.");
        }
        catch (Exception ex)
        {
            Log.Error("UI", "Exécution du boost impossible", ex);
            ShellState.Status("Le boost a échoué : " + ex.Message);
            RunLabel.Text = "Échec : " + ex.Message;
            try
            {
                _sessionActive = BoostService.Instance.ActiveSession is not null;
            }
            catch
            {
                _sessionActive = false;
            }
        }
        finally
        {
            _running = false;
            EndSessionButton.Visibility = _sessionActive ? Visibility.Visible : Visibility.Collapsed;
            EndSessionButton.IsEnabled = _sessionActive;
            CloseButton.Visibility = _sessionActive ? Visibility.Collapsed : Visibility.Visible;
        }
    }

    private void RenderResults(BoostSessionState session)
    {
        ResultsTitle.Visibility = Visibility.Visible;
        BackupsTitle.Visibility = Visibility.Visible;
        AppliedTitle.Visibility = Visibility.Visible;
        RunProgress.Value = RunProgress.Maximum;

        ResultsHost.Children.Clear();
        foreach (var result in session.Results) ResultsHost.Children.Add(BuildResultRow(result));
        _resultCountAfterRun = session.Results.Count;

        BackupsHost.Children.Clear();
        if (session.Backups.Count == 0)
        {
            BackupsHost.Children.Add(new TextBlock
            {
                Style = TryStyle("BodyMuted"),
                Text = "Aucune sauvegarde n'a été nécessaire."
            });
        }
        else
        {
            foreach (var backup in session.Backups)
            {
                BackupsHost.Children.Add(new TextBlock
                {
                    Style = TryStyle("Mono"),
                    Text = backup,
                    Margin = new Thickness(0, 0, 0, 5),
                    TextWrapping = TextWrapping.Wrap
                });
            }
        }

        AppliedHost.Children.Clear();
        if (session.AppliedOptimizations.Count == 0)
        {
            AppliedHost.Children.Add(new TextBlock
            {
                Style = TryStyle("BodyMuted"),
                Text = "Aucune optimisation n'a pu être appliquée."
            });
        }
        else
        {
            foreach (var item in session.AppliedOptimizations)
            {
                AppliedHost.Children.Add(new TextBlock
                {
                    Style = TryStyle("Body"),
                    Text = "• " + item,
                    Margin = new Thickness(0, 0, 0, 5)
                });
            }
        }
    }

    private UIElement BuildResultRow(BoostStepResult result)
    {
        var grid = new Grid { Margin = new Thickness(0, 0, 0, 11) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var icon = new TextBlock
        {
            FontFamily = IconFont(),
            FontSize = 15,
            Text = result.Skipped ? "\uE7C3" : result.Success ? "\uE896" : "\uE897",
            Foreground = TryBrush(result.Skipped ? "TextMutedBrush" : result.Success ? "GoodBrush" : "DangerBrush"),
            Margin = new Thickness(0, 1, 11, 0),
            VerticalAlignment = VerticalAlignment.Top
        };
        grid.Children.Add(icon);

        var stack = new StackPanel();
        stack.Children.Add(new TextBlock
        {
            Style = TryStyle("Body"),
            Text = result.Title,
            FontWeight = FontWeights.SemiBold
        });
        if (!string.IsNullOrWhiteSpace(result.Message))
        {
            stack.Children.Add(new TextBlock
            {
                Style = TryStyle("Caption"),
                Text = result.Message,
                Margin = new Thickness(0, 3, 0, 0)
            });
        }
        Grid.SetColumn(stack, 1);
        grid.Children.Add(stack);
        return grid;
    }

    private async void OnEndSessionClick(object sender, RoutedEventArgs e)
    {
        if (_running) return;
        _running = true;
        EndSessionButton.IsEnabled = false;
        try
        {
            var session = await BoostService.Instance.EndSessionAsync();
            _sessionActive = session.IsActive;
            RenderRestore(session);
            StopHistory();
            RunLabel.Text = session.IsActive
                ? "La session est toujours active : vérifiez les messages ci-dessous."
                : "Session terminée : les réglages d'origine sont restaurés.";
            ShellState.Status(session.IsActive
                ? "La session n'a pas pu être terminée."
                : "Session Boost terminée : réglages restaurés.");
        }
        catch (Exception ex)
        {
            Log.Error("UI", "Fin de session impossible", ex);
            ShellState.Status("Fin de session impossible : " + ex.Message);
            RunLabel.Text = "Échec : " + ex.Message;
        }
        finally
        {
            _running = false;
            EndSessionButton.Visibility = _sessionActive ? Visibility.Visible : Visibility.Collapsed;
            EndSessionButton.IsEnabled = _sessionActive;
            CloseButton.Visibility = Visibility.Visible;
        }
    }

    private void RenderRestore(BoostSessionState session)
    {
        RestorePanel.Visibility = Visibility.Visible;
        RestoreHost.Children.Clear();
        var added = session.Results.Skip(_resultCountAfterRun).ToList();
        if (added.Count == 0)
        {
            RestoreHost.Children.Add(new TextBlock
            {
                Style = TryStyle("BodyMuted"),
                Text = "Aucune restauration à effectuer : rien n'avait été modifié."
            });
            return;
        }

        foreach (var result in added) RestoreHost.Children.Add(BuildResultRow(result));
    }

    private void StartHistory(BoostPlan plan)
    {
        if (_historyStarted || _selectedGame is null) return;
        try
        {
            if (!SettingsService.Current.RecordSessions) return;
            HistoryService.Instance.StartSession(_selectedGame.Name, _selectedGame.Id, plan.ProfileName ?? string.Empty);
            _historyStarted = true;
        }
        catch (Exception ex)
        {
            Log.Warn("UI", "Enregistrement de la session impossible : " + ex.Message);
        }
    }

    private void StopHistory()
    {
        if (!_historyStarted) return;
        _historyStarted = false;
        try
        {
            HistoryService.Instance.StopSession("Session Boost");
        }
        catch (Exception ex)
        {
            Log.Warn("UI", "Fin d'enregistrement impossible : " + ex.Message);
        }
    }

    private void ShowStep(int step)
    {
        StepAPanel.Visibility = step == 1 ? Visibility.Visible : Visibility.Collapsed;
        StepBPanel.Visibility = step == 2 ? Visibility.Visible : Visibility.Collapsed;
        StepCPanel.Visibility = step == 3 ? Visibility.Visible : Visibility.Collapsed;
        FooterA.Visibility = step == 1 ? Visibility.Visible : Visibility.Collapsed;
        FooterB.Visibility = step == 2 ? Visibility.Visible : Visibility.Collapsed;
        FooterC.Visibility = step == 3 ? Visibility.Visible : Visibility.Collapsed;
        if (step == 1) StepAPanel.ScrollToTop();
        if (step == 2) StepBPanel.ScrollToTop();
        if (step == 3) StepCPanel.ScrollToTop();
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (_running)
        {
            e.Cancel = true;
            ShellState.Status("Veuillez attendre la fin de l'opération en cours.");
            return;
        }

        if (!_sessionActive)
        {
            StopHistory();
            return;
        }

        var answer = MessageBox.Show(
            "Une session Boost est active sur votre PC. Terminer la session et remettre les réglages d'origine ?",
            "GameBoost", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (answer != MessageBoxResult.Yes)
        {
            e.Cancel = true;
            return;
        }

        try
        {
            var task = BoostService.Instance.EndSessionAsync();
            if (!task.Wait(TimeSpan.FromSeconds(12)))
            {
                e.Cancel = true;
                ShellState.Status("La restauration prend trop de temps : fermeture annulée.");
                return;
            }

            var session = task.Result;
            if (session.IsActive)
            {
                e.Cancel = true;
                ShellState.Status("La session n'a pas pu être terminée : fermeture annulée.");
                return;
            }

            _sessionActive = false;
            StopHistory();
            ShellState.Status("Session Boost terminée : réglages restaurés.");
        }
        catch (Exception ex)
        {
            Log.Error("UI", "Restauration à la fermeture impossible", ex);
            ShellState.Status("Restauration impossible : " + ex.Message);
            e.Cancel = true;
        }
    }

    private void OnCloseClick(object sender, RoutedEventArgs e)
    {
        try
        {
            Close();
        }
        catch (Exception ex)
        {
            Log.Warn("UI", "Fermeture impossible : " + ex.Message);
        }
    }

    private void OnTitleBarDrag(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2) return;
        if (e.LeftButton != MouseButtonState.Pressed) return;
        try
        {
            DragMove();
        }
        catch (Exception ex)
        {
            Log.Warn("UI", "Déplacement de la fenêtre impossible : " + ex.Message);
        }
    }

    private static string PresetLabel(ProfilePreset preset) => preset switch
    {
        ProfilePreset.MaximumQuality => "Qualité maximale",
        ProfilePreset.Balanced => "Équilibré",
        ProfilePreset.Performance => "Performance",
        _ => "Personnalisé"
    };

    private static Style? TryStyle(string key) => Application.Current?.TryFindResource(key) as Style;

    private static Brush? TryBrush(string key) => Application.Current?.TryFindResource(key) as Brush;

    private static FontFamily IconFont() =>
        Application.Current?.TryFindResource("IconFont") as FontFamily ?? new FontFamily("Segoe MDL2 Assets");

    private sealed class GameOption
    {
        public GameOption(string label, GameInfo? game)
        {
            Label = label;
            Game = game;
        }

        public string Label { get; }
        public GameInfo? Game { get; }
    }
}
