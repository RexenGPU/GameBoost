using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using GameBoost.App.Controls;
using GameBoost.App.Overlay;
using GameBoost.App.Services;
using GameBoost.Core.Games;
using GameBoost.Core.History;
using GameBoost.Core.Logging;
using GameBoost.Core.Models;
using GameBoost.Core.Monitoring;
using GameBoost.Core.Overlay;
using GameBoost.Core.Processes;

namespace GameBoost.App.Pages;

public partial class MonitoringPage : UserControl
{
    private const int MaxPoints = 300;
    private const double OverlayWidthGuess = 260;
    private const double OverlayHeightGuess = 150;
    private const double OverlayEdgeMargin = 24;

    private readonly List<double> _frameTimePoints = new();
    private readonly List<double> _fpsPoints = new();
    private readonly List<GameInfo> _games = new();

    private OverlaySettings _overlaySettings = new();
    private GameInfo? _trackedGame;
    private readonly DispatcherTimer _opacitySaveTimer;
    private bool _loaded;
    private bool _subscribed;
    private bool _syncingOverlay;

    public MonitoringPage()
    {
        InitializeComponent();
        GameCombo.SelectionChanged += OnGameSelectionChanged;
        _opacitySaveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _opacitySaveTimer.Tick += OnOpacitySaveTick;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            if (!_subscribed)
            {
                SystemMonitor.Instance.SampleUpdated += OnSampleUpdated;
                HistoryService.Instance.SessionSaved += OnSessionSaved;
                OverlayController.Instance.VisibilityChanged += OnOverlayVisibilityChanged;
                _subscribed = true;
            }

            _loaded = false;
            EnsureMonitor();
            UpdateTrackedUi();
            UpdateSessionUi();
            LoadOverlaySettings();
            StartOverlayIfNeeded();
            ApplySample(SystemMonitor.Instance.Current);
            _loaded = true;
            LoadGames();
        }
        catch (Exception ex)
        {
            Log.Error("UI", "Chargement de la page Monitoring", ex);
            ShellState.Status("Chargement du monitoring impossible : " + ex.Message);
        }
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (_opacitySaveTimer.IsEnabled)
        {
            _opacitySaveTimer.Stop();
            SaveOverlaySettings();
        }
        if (!_subscribed) return;
        SystemMonitor.Instance.SampleUpdated -= OnSampleUpdated;
        HistoryService.Instance.SessionSaved -= OnSessionSaved;
        OverlayController.Instance.VisibilityChanged -= OnOverlayVisibilityChanged;
        _subscribed = false;
    }

    private void EnsureMonitor()
    {
        try
        {
            if (!SystemMonitor.Instance.IsRunning) SystemMonitor.Instance.Start(1000);
            UpdateMonitorUi();
        }
        catch (Exception ex)
        {
            Log.Error("UI", "Demarrage du monitoring", ex);
            ShellState.Status("Le monitoring n'a pas pu démarrer : " + ex.Message);
        }
    }

    private void UpdateMonitorUi()
    {
        var running = SystemMonitor.Instance.IsRunning;
        MonitorStateText.Text = running ? "Monitoring actif" : "Monitoring inactif";
        MonitorToggleLabel.Text = running ? "Arrêter" : "Démarrer";
        MonitorDot.Fill = running ? Brush("GoodBrush") : Brush("TextFaintBrush");
    }

    private void OnMonitorToggleClick(object sender, RoutedEventArgs e)
    {
        try
        {
            if (SystemMonitor.Instance.IsRunning)
            {
                SystemMonitor.Instance.Stop();
                ShellState.Status("Monitoring arrêté : les mesures sont en pause.");
            }
            else
            {
                SystemMonitor.Instance.Start(1000);
                ShellState.Status("Monitoring démarré : une mesure toutes les secondes.");
            }
            UpdateMonitorUi();
        }
        catch (Exception ex)
        {
            Log.Error("UI", "Bascule du monitoring", ex);
            ShellState.Status("Changement d'état impossible : " + ex.Message);
        }
    }

    private async void LoadGames()
    {
        try
        {
            var games = await Task.Run(() => GameScanner.Instance.GetLibrary());
            var current = GameCombo.SelectedItem as GameInfo;
            _games.Clear();
            _games.AddRange(games);
            GameCombo.ItemsSource = null;
            GameCombo.ItemsSource = _games;
            if (current is not null) GameCombo.SelectedItem = _games.FirstOrDefault(g => g.Id == current.Id);
            if (GameCombo.SelectedItem is null && _games.Count > 0) GameCombo.SelectedIndex = 0;
            if (_games.Count == 0)
                ShellState.Status("Aucun jeu détecté : ajoutez-en un depuis la page Jeux.");
        }
        catch (Exception ex)
        {
            Log.Error("UI", "Lecture de la bibliotheque de jeux", ex);
            ShellState.Status("Jeux indisponibles : " + ex.Message);
        }
    }

    private void OnGameSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_loaded) return;
        LoadOverlaySettings();
    }

    private async void OnTrackClick(object sender, RoutedEventArgs e)
    {
        if (GameCombo.SelectedItem is not GameInfo game)
        {
            ShellState.Status("Sélectionnez d'abord un jeu à suivre.");
            return;
        }

        try
        {
            var pid = await Task.Run(() => ProcessService.Instance.FindGameProcessId(game));
            if (pid is null or <= 0)
            {
                ShellState.Status("Le jeu n'est pas lancé — lancez-le puis réessayez.");
                return;
            }

            SystemMonitor.Instance.TrackProcess(pid.Value, game.Name);
            SystemMonitor.Instance.ResetFpsStats();
            _frameTimePoints.Clear();
            _fpsPoints.Clear();
            _trackedGame = game;
            UpdateTrackedUi();
            ApplySample(SystemMonitor.Instance.Current);
            ShellState.Status("Suivi démarré : " + game.Name + " (PID " + pid.Value + ").");
        }
        catch (Exception ex)
        {
            Log.Error("UI", "Suivi du jeu", ex);
            ShellState.Status("Suivi impossible : " + ex.Message);
        }
    }

    private void OnUntrackClick(object sender, RoutedEventArgs e)
    {
        try
        {
            SystemMonitor.Instance.ClearProcess();
            _trackedGame = null;
            UpdateTrackedUi();
            ShellState.Status("Suivi arrêté : aucun jeu n'est mesuré.");
        }
        catch (Exception ex)
        {
            Log.Error("UI", "Arret du suivi", ex);
            ShellState.Status("Arrêt du suivi impossible : " + ex.Message);
        }
    }

    private void UpdateTrackedUi()
    {
        var pid = SystemMonitor.Instance.TrackedProcessId;
        if (_trackedGame is not null && pid is int tracked)
            TrackedText.Text = "Suivi : " + _trackedGame.Name + " · PID " + tracked;
        else if (pid is int id)
            TrackedText.Text = "Suivi : PID " + id;
        else
            TrackedText.Text = "Suivi : aucun processus";
    }

    private void OnSampleUpdated(MonitorSample sample)
    {
        try
        {
            Dispatcher.BeginInvoke(() => ApplySample(sample));
        }
        catch (Exception ex)
        {
            Log.Warn("UI", "Echantillon ignore : " + ex.Message);
        }
    }

    private void ApplySample(MonitorSample? sample)
    {
        if (sample is null) return;
        try
        {
            CardCpu.Value = sample.CpuUsagePercent.ToString("F0");
            CardGpu.Value = sample.GpuUsagePercent.ToString("F0");
            CardRam.Value = BytesToSizeConverter.Format(sample.RamUsedBytes);
            CardRam.SubText = sample.RamTotalBytes > 0
                ? "sur " + BytesToSizeConverter.Format(sample.RamTotalBytes)
                : string.Empty;
            CardCpuTemp.Value = Temperature(sample.CpuTemperatureC);
            CardGpuTemp.Value = Temperature(sample.GpuTemperatureC);
            CardVram.Value = sample.VramUsedBytes > 0
                ? BytesToSizeConverter.Format(sample.VramUsedBytes)
                : "—";
            CardVram.SubText = sample.VramTotalBytes > 0
                ? "sur " + BytesToSizeConverter.Format(sample.VramTotalBytes)
                : string.Empty;

            if (sample.FrameTimeMs is double frameTime && frameTime > 0 && !double.IsNaN(frameTime))
                AppendPoint(_frameTimePoints, frameTime);
            if (sample.Fps is double fps && fps > 0 && !double.IsNaN(fps))
                AppendPoint(_fpsPoints, fps);

            RefreshCharts();
            RefreshFpsPanel(sample);
        }
        catch (Exception ex)
        {
            Log.Warn("UI", "Affichage de l'echantillon ignore : " + ex.Message);
        }
    }

    private static string Temperature(double? celsius)
    {
        if (celsius is not double value || value <= 0 || double.IsNaN(value)) return "—";
        return value.ToString("F0");
    }

    private static void AppendPoint(List<double> points, double value)
    {
        points.Add(value);
        while (points.Count > MaxPoints) points.RemoveAt(0);
    }

    private void RefreshCharts()
    {
        FrameTimeChart.Series = new[]
        {
            new ChartSeries
            {
                Name = "Frametime",
                Values = _frameTimePoints,
                Color = Color.FromRgb(0x60, 0xA5, 0xFA),
                Thickness = 2,
                Area = true
            }
        };
        FpsChart.Series = new[]
        {
            new ChartSeries
            {
                Name = "FPS",
                Values = _fpsPoints,
                Color = Color.FromRgb(0x22, 0xC5, 0x5E),
                Thickness = 2,
                Area = true
            }
        };
    }

    private void RefreshFpsPanel(MonitorSample sample)
    {
        var stats = SystemMonitor.Instance.GetFpsStats();
        var average = stats?.Average ?? sample.Fps;
        var hasAverage = average is double avg && avg > 0 && !double.IsNaN(avg);

        FpsValueText.Text = hasAverage ? average!.Value.ToString("F0") : "—";
        FrameTimeValueText.Text = sample.FrameTimeMs is double frameTime && frameTime > 0 && !double.IsNaN(frameTime)
            ? frameTime.ToString("F1") + " ms"
            : "—";
        OnePercentText.Text = FormatFps(stats?.OnePercentLow);
        MedianFpsText.Text = FormatFps(stats?.Median);
        MinFpsText.Text = FormatFps(stats?.Minimum);
        MaxFpsText.Text = FormatFps(stats?.Maximum);
        SampleCountText.Text = (stats?.SampleCount ?? 0).ToString();

        if (!hasAverage)
            FpsSourceText.Text = "Aucune mesure d'image : suivez un jeu lancé pour obtenir des FPS.";
        else if (stats is null)
            FpsSourceText.Text = "Valeur instantanée du moniteur : suivez un jeu pour obtenir les statistiques détaillées.";
        else
            FpsSourceText.Text = "Statistiques calculées sur " + stats.SampleCount + " échantillon(s) du jeu suivi.";
    }

    private static string FormatFps(double? value)
    {
        if (value is not double number || number <= 0 || double.IsNaN(number)) return "—";
        return number.ToString("F0");
    }

    private void OnResetFpsClick(object sender, RoutedEventArgs e)
    {
        try
        {
            SystemMonitor.Instance.ResetFpsStats();
            _frameTimePoints.Clear();
            _fpsPoints.Clear();
            RefreshCharts();
            ApplySample(SystemMonitor.Instance.Current);
            ShellState.Status("Statistiques d'images réinitialisées.");
        }
        catch (Exception ex)
        {
            Log.Error("UI", "Reinitialisation des statistiques", ex);
            ShellState.Status("Réinitialisation impossible : " + ex.Message);
        }
    }

    private void OnStartSessionClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var game = SelectedGameOrNull() ?? _trackedGame;
            var name = string.IsNullOrWhiteSpace(game?.Name) ? "Session manuelle" : game!.Name;
            HistoryService.Instance.StartSession(name, game?.Id ?? string.Empty);
            UpdateSessionUi();
            ShellState.Status("Enregistrement démarré : " + name + ".");
        }
        catch (Exception ex)
        {
            Log.Error("UI", "Demarrage de la session", ex);
            ShellState.Status("Démarrage impossible : " + ex.Message);
        }
    }

    private void OnStopSessionClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var record = HistoryService.Instance.StopSession();
            UpdateSessionUi();
            if (record is null)
            {
                ShellState.Status("Aucune session en cours d'enregistrement.");
                return;
            }

            ShellState.Status(record.AverageFps is double fps
                ? "Session enregistrée : " + record.GameName + " · " + fps.ToString("F0") + " FPS en moyenne."
                : "Session enregistrée : " + record.GameName + " · FPS non enregistrés.");
        }
        catch (Exception ex)
        {
            Log.Error("UI", "Arret de la session", ex);
            ShellState.Status("Arrêt impossible : " + ex.Message);
        }
    }

    private void OnSessionSaved(SessionRecord record)
    {
        try
        {
            Dispatcher.BeginInvoke(UpdateSessionUi);
        }
        catch (Exception ex)
        {
            Log.Warn("UI", "Mise a jour de session ignoree : " + ex.Message);
        }
    }

    private void UpdateSessionUi()
    {
        var active = HistoryService.Instance.ActiveSession;
        if (active is null)
        {
            SessionStateText.Text = "Aucune session en cours.";
            StartSessionButton.IsEnabled = true;
            StopSessionButton.IsEnabled = false;
        }
        else
        {
            SessionStateText.Text = "Session en cours depuis " + active.StartTime.ToString("HH:mm") + ".";
            StartSessionButton.IsEnabled = false;
            StopSessionButton.IsEnabled = true;
        }

        var game = SelectedGameOrNull() ?? _trackedGame;
        SessionGameText.Text = game is null
            ? "Jeu : aucun sélectionné"
            : "Jeu : " + game.Name + (active is not null ? " · enregistrement en cours" : string.Empty);
    }

    private GameInfo? SelectedGameOrNull() => GameCombo.SelectedItem as GameInfo;

    private void LoadOverlaySettings()
    {
        try
        {
            var game = SelectedGameOrNull();
            _overlaySettings = game is null
                ? OverlayService.Instance.GetDefault()
                : OverlayService.Instance.GetForGame(game.Id);
            SyncOverlayUi();
            UpdateSessionUi();
        }
        catch (Exception ex)
        {
            Log.Error("UI", "Lecture des reglages d'overlay", ex);
            ShellState.Status("Réglages overlay indisponibles : " + ex.Message);
        }
    }

    private void SyncOverlayUi()
    {
        _syncingOverlay = true;
        OverlayEnabledToggle.IsChecked = _overlaySettings.Enabled;
        OverlayFpsToggle.IsChecked = _overlaySettings.ShowFps;
        OverlayFrameTimeToggle.IsChecked = _overlaySettings.ShowFrameTime;
        OverlayCpuToggle.IsChecked = _overlaySettings.ShowCpu;
        OverlayGpuToggle.IsChecked = _overlaySettings.ShowGpu;
        OverlayRamToggle.IsChecked = _overlaySettings.ShowRam;
        OverlayTempToggle.IsChecked = _overlaySettings.ShowTemperatures;
        OpacitySlider.Value = Math.Clamp(_overlaySettings.Opacity, 30, 100);
        OpacityValueText.Text = Math.Clamp(_overlaySettings.Opacity, 30, 100) + " %";

        var area = SystemParameters.WorkArea;
        var right = _overlaySettings.PositionX > area.Width / 2;
        var bottom = _overlaySettings.PositionY > area.Height / 2;
        if (right && bottom) CornerBottomRight.IsChecked = true;
        else if (right) CornerTopRight.IsChecked = true;
        else if (bottom) CornerBottomLeft.IsChecked = true;
        else CornerTopLeft.IsChecked = true;

        _syncingOverlay = false;
        UpdateOverlayBadge();
    }

    private void StartOverlayIfNeeded()
    {
        try
        {
            if (_overlaySettings.Enabled) OverlayController.Instance.Start(null);
            else OverlayController.Instance.Stop();
            UpdateOverlayBadge();
        }
        catch (Exception ex)
        {
            Log.Error("UI", "Demarrage de l'overlay", ex);
            ShellState.Status("Overlay impossible : " + ex.Message);
        }
    }

    private void OnOverlayOptionChanged(object sender, RoutedEventArgs e)
    {
        if (!_loaded || _syncingOverlay) return;
        try
        {
            _overlaySettings.Enabled = OverlayEnabledToggle.IsChecked == true;
            _overlaySettings.ShowFps = OverlayFpsToggle.IsChecked == true;
            _overlaySettings.ShowFrameTime = OverlayFrameTimeToggle.IsChecked == true;
            _overlaySettings.ShowCpu = OverlayCpuToggle.IsChecked == true;
            _overlaySettings.ShowGpu = OverlayGpuToggle.IsChecked == true;
            _overlaySettings.ShowRam = OverlayRamToggle.IsChecked == true;
            _overlaySettings.ShowTemperatures = OverlayTempToggle.IsChecked == true;

            SaveOverlaySettings();

            if (ReferenceEquals(sender, OverlayEnabledToggle))
            {
                if (_overlaySettings.Enabled) OverlayController.Instance.Start(null);
                else OverlayController.Instance.Stop();
                UpdateOverlayBadge();
                ShellState.Status(_overlaySettings.Enabled
                    ? "Overlay activé : GameBoost surveille les jeux lancés."
                    : "Overlay désactivé.");
            }
        }
        catch (Exception ex)
        {
            Log.Error("UI", "Reglage d'overlay", ex);
            ShellState.Status("Réglage overlay impossible : " + ex.Message);
        }
    }

    private void OnOpacityChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_loaded || _syncingOverlay) return;
        try
        {
            _overlaySettings.Opacity = (int)Math.Round(Math.Clamp(OpacitySlider.Value, 30, 100));
            OpacityValueText.Text = _overlaySettings.Opacity + " %";
            _opacitySaveTimer.Stop();
            _opacitySaveTimer.Start();
        }
        catch (Exception ex)
        {
            Log.Error("UI", "Reglage de l'opacite", ex);
            ShellState.Status("Opacité impossible à régler : " + ex.Message);
        }
    }

    private void OnOpacitySaveTick(object? sender, EventArgs e)
    {
        _opacitySaveTimer.Stop();
        SaveOverlaySettings();
    }

    private void OnCornerChecked(object sender, RoutedEventArgs e)
    {
        if (!_loaded || _syncingOverlay || sender is not RadioButton { IsChecked: true, Tag: string tag }) return;
        try
        {
            var area = SystemParameters.WorkArea;
            var right = tag is "tr" or "br";
            var bottom = tag is "bl" or "br";
            _overlaySettings.PositionX = (int)Math.Round(right
                ? area.Width - OverlayWidthGuess - OverlayEdgeMargin
                : OverlayEdgeMargin);
            _overlaySettings.PositionY = (int)Math.Round(bottom
                ? area.Height - OverlayHeightGuess - OverlayEdgeMargin
                : OverlayEdgeMargin);
            SaveOverlaySettings();
            ShellState.Status("Position de l'overlay mise à jour.");
        }
        catch (Exception ex)
        {
            Log.Error("UI", "Position de l'overlay", ex);
            ShellState.Status("Position impossible à appliquer : " + ex.Message);
        }
    }

    private void SaveOverlaySettings()
    {
        try
        {
            var game = SelectedGameOrNull();
            if (game is null) OverlayService.Instance.SaveDefault(_overlaySettings);
            else OverlayService.Instance.SaveForGame(game.Id, _overlaySettings);
            OverlayController.Instance.RefreshSettings(game);
            UpdateOverlayBadge();
        }
        catch (Exception ex)
        {
            Log.Error("UI", "Enregistrement des reglages d'overlay", ex);
            ShellState.Status("Réglages overlay non enregistrés : " + ex.Message);
        }
    }

    private void OnPreviewOverlayClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var wasRunning = OverlayController.Instance.IsRunning;
            var game = SelectedGameOrNull();
            OverlayController.Instance.Toggle(game);
            UpdateOverlayBadge();

            if (wasRunning)
                ShellState.Status("Overlay masqué.");
            else if (OverlayController.Instance.IsRunning)
                ShellState.Status("Overlay affiché" + (game is null ? string.Empty : " pour " + game.Name) + ".");
            else if (game is not null)
                ShellState.Status("L'overlay ne s'est pas affiché : vérifiez qu'il est activé pour ce jeu.");
            else
                ShellState.Status("Surveillance lancée : l'overlay s'affichera dès qu'un jeu avec l'overlay activé sera détecté.");
        }
        catch (Exception ex)
        {
            Log.Error("UI", "Apercu de l'overlay", ex);
            ShellState.Status("Aperçu impossible : " + ex.Message);
        }
    }

    private void OnOverlayVisibilityChanged(bool visible)
    {
        try
        {
            Dispatcher.BeginInvoke(UpdateOverlayBadge);
        }
        catch (Exception ex)
        {
            Log.Warn("UI", "Etat d'overlay ignore : " + ex.Message);
        }
    }

    private void UpdateOverlayBadge()
    {
        var running = OverlayController.Instance.IsRunning;
        OverlayBadgeText.Text = running ? "Overlay : actif" : "Overlay : inactif";
        OverlayDot.Fill = running ? Brush("GoodBrush") : Brush("TextFaintBrush");
    }

    private void OnOpenGamesClick(object sender, RoutedEventArgs e) => NavigationService.Navigate("games");

    private void OnOpenHistoryClick(object sender, RoutedEventArgs e) => NavigationService.Navigate("history");

    private static Brush Brush(string key)
    {
        return Application.Current?.TryFindResource(key) as Brush ?? Brushes.Gray;
    }
}
