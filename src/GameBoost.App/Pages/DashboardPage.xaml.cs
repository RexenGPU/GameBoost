using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using GameBoost.App.Controls;
using GameBoost.App.Pages.Dashboard;
using GameBoost.App.Services;
using GameBoost.Core.Disks;
using GameBoost.Core.Games;
using GameBoost.Core.Hardware;
using GameBoost.Core.Logging;
using GameBoost.Core.Models;
using GameBoost.Core.Monitoring;
using GameBoost.Core.Optimization;
using GameBoost.Core.Processes;

namespace GameBoost.App.Pages;

public partial class DashboardPage : UserControl
{
    private static readonly BytesRateConverter RateConverter = new();
    private readonly DispatcherTimer _processTimer;
    private bool _subscribed;
    private bool _hardwareBusy;
    private bool _hardwareLoaded;
    private bool _disksBusy;
    private bool _processesBusy;
    private bool _scanningGames;
    private long _diskFreeBytes;
    private long _diskTotalBytes;

    public DashboardPage()
    {
        InitializeComponent();
        _processTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _processTimer.Tick += OnProcessTimerTick;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (!_subscribed)
        {
            SystemMonitor.Instance.SampleUpdated += OnSampleUpdated;
            _subscribed = true;
        }

        try
        {
            if (!SystemMonitor.Instance.IsRunning) SystemMonitor.Instance.Start(1000);
            ApplySample(SystemMonitor.Instance.Current);
        }
        catch (Exception ex)
        {
            Log.Error("UI", "Lecture des mesures temps réel impossible", ex);
            ShellState.Status("Mesures temps réel indisponibles : " + ex.Message);
        }

        RefreshPendingBanner();
        if (!_hardwareLoaded && !_hardwareBusy) _ = LoadHardwareAsync();
        if (!_disksBusy) _ = LoadDisksAsync();
        _ = LoadGamesAsync();
        _ = LoadProcessesAsync();
        _ = EvaluateBoosterAsync();
        _processTimer.Start();
    }

    private void OnProcessTimerTick(object? sender, EventArgs e)
    {
        _ = LoadProcessesAsync();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _processTimer.Stop();
        if (!_subscribed) return;
        SystemMonitor.Instance.SampleUpdated -= OnSampleUpdated;
        _subscribed = false;
    }

    private void OnPageSizeChanged(object sender, SizeChangedEventArgs e)
    {
        var width = e.NewSize.Width;
        MetricsGrid.Columns = width >= 1100 ? 4 : width >= 720 ? 2 : 1;
    }

    private void OnSampleUpdated(MonitorSample sample)
    {
        try
        {
            Dispatcher.BeginInvoke(() => ApplySample(sample));
        }
        catch (Exception ex)
        {
            Log.Warn("UI", "Mise à jour des mesures refusée : " + ex.Message);
        }
    }

    private void ApplySample(MonitorSample sample)
    {
        try
        {
            CpuUsageCard.Value = sample.CpuUsagePercent.ToString("F0", CultureInfo.CurrentCulture);
            CpuUsageCard.ProgressPercent = Math.Clamp(sample.CpuUsagePercent, 0, 100);
            CpuUsageCard.Level = UsageLevel(sample.CpuUsagePercent);

            GpuUsageCard.Value = sample.GpuUsagePercent.ToString("F0", CultureInfo.CurrentCulture);
            GpuUsageCard.ProgressPercent = Math.Clamp(sample.GpuUsagePercent, 0, 100);
            GpuUsageCard.Level = UsageLevel(sample.GpuUsagePercent);

            ApplyTemperature(CpuTempCard, sample.CpuTemperatureC, 80, 90);
            ApplyTemperature(GpuTempCard, sample.GpuTemperatureC, 75, 85);
            ApplyMemory(RamCard, sample.RamUsedBytes, sample.RamTotalBytes);
            ApplyMemory(VramCard, sample.VramUsedBytes, sample.VramTotalBytes);
            ApplyDiskCard();

            var fps = sample.Fps;
            NetFpsCard.Value = fps is double measured
                ? measured.ToString("F0", CultureInfo.CurrentCulture)
                : "—";
            var network = "Réception " + Rate(sample.NetworkReceiveBytesPerSec) +
                          " · Émission " + Rate(sample.NetworkSendBytesPerSec);
            NetFpsCard.SubText = fps is null ? "jeu non détecté" + Environment.NewLine + network : network;
        }
        catch (Exception ex)
        {
            Log.Error("UI", "Affichage des mesures impossible", ex);
        }
    }

    private static void ApplyTemperature(StatCard card, double? temperature, double warnAt, double criticalAt)
    {
        if (temperature is not double value)
        {
            card.Value = "Non disponible";
            card.Unit = string.Empty;
            card.SubText = "élévation requise";
            card.ProgressPercent = double.NaN;
            card.Level = null;
            return;
        }

        card.Value = value.ToString("F0", CultureInfo.CurrentCulture);
        card.Unit = "°C";
        card.SubText = string.Empty;
        card.ProgressPercent = Math.Clamp(value, 0, 120) * 100.0 / 120;
        card.Level = value >= criticalAt ? HealthLevel.Critical
            : value >= warnAt ? HealthLevel.Warning
            : HealthLevel.Good;
    }

    private static void ApplyMemory(StatCard card, long used, long total)
    {
        if (total <= 0)
        {
            card.Value = "Non disponible";
            card.Unit = string.Empty;
            card.SubText = string.Empty;
            card.ProgressPercent = double.NaN;
            card.Level = null;
            return;
        }

        card.Value = BytesToSizeConverter.Format(used);
        card.Unit = string.Empty;
        card.SubText = "sur " + BytesToSizeConverter.Format(total);
        var usedPercent = used * 100.0 / total;
        card.ProgressPercent = usedPercent;
        card.Level = FreeLevel(100 - usedPercent);
    }

    private void ApplyDiskCard()
    {
        if (_diskTotalBytes <= 0)
        {
            DiskCard.Value = "Non disponible";
            DiskCard.Unit = string.Empty;
            DiskCard.SubText = string.Empty;
            DiskCard.ProgressPercent = double.NaN;
            DiskCard.Level = null;
            return;
        }

        DiskCard.Value = BytesToSizeConverter.Format(_diskFreeBytes);
        DiskCard.Unit = string.Empty;
        DiskCard.SubText = "sur " + BytesToSizeConverter.Format(_diskTotalBytes);
        var freePercent = _diskFreeBytes * 100.0 / _diskTotalBytes;
        DiskCard.ProgressPercent = freePercent;
        DiskCard.Level = FreeLevel(freePercent);
    }

    private static HealthLevel UsageLevel(double usage) =>
        usage >= 95 ? HealthLevel.Critical : usage >= 80 ? HealthLevel.Warning : HealthLevel.Good;

    private static HealthLevel FreeLevel(double freePercent) =>
        freePercent < 10 ? HealthLevel.Critical : freePercent < 20 ? HealthLevel.Warning : HealthLevel.Good;

    private static string Rate(double bytes)
    {
        var value = RateConverter.Convert(bytes, typeof(string), string.Empty, CultureInfo.CurrentCulture);
        return value as string ?? "—";
    }

    private async Task LoadHardwareAsync()
    {
        if (_hardwareBusy) return;
        _hardwareBusy = true;
        HardwareLoadingPanel.Visibility = Visibility.Visible;
        HardwarePanel.Visibility = Visibility.Collapsed;
        try
        {
            var report = await HardwareDetector.Instance.CollectAsync();
            _hardwareLoaded = true;
            FillHardware(report);
            ShellState.Status("Matériel détecté.");
        }
        catch (Exception ex)
        {
            Log.Error("UI", "Détection du matériel impossible", ex);
            ShellState.Status("Détection du matériel impossible : " + ex.Message);
            FillHardware(null);
        }
        finally
        {
            _hardwareBusy = false;
            HardwareLoadingPanel.Visibility = Visibility.Collapsed;
            HardwarePanel.Visibility = Visibility.Visible;
        }
    }

    private void FillHardware(HardwareReport? report)
    {
        if (report is null)
        {
            HwCpuText.Text = "Non disponible";
            HwGpuText.Text = "Non disponible";
            HwGpuSubText.Text = string.Empty;
            HwRamText.Text = "Non disponible";
            HwOsText.Text = "Non disponible";
            HwDisplayText.Text = "Non disponible";
            return;
        }

        var cpuName = Known(report.Cpu.Name);
        var cores = report.Cpu.PhysicalCores > 0
            ? report.Cpu.PhysicalCores + " cœurs / " + report.Cpu.LogicalCores + " threads"
            : "Cœurs non disponibles";
        HwCpuText.Text = cpuName == "Non disponible" ? cpuName : cpuName + " · " + cores;

        HwGpuText.Text = Known(report.Gpu.Name) + " · " +
                         (report.Gpu.DedicatedVramBytes > 0
                             ? BytesToSizeConverter.Format(report.Gpu.DedicatedVramBytes)
                             : "VRAM non disponible");
        HwGpuSubText.Text = VendorLabel(report.Gpu.Vendor) + " · " +
                            (Known(report.Gpu.DriverVersion) == "Non disponible"
                                ? "pilote non disponible"
                                : "pilote " + report.Gpu.DriverVersion);

        HwRamText.Text = report.Ram.TotalBytes > 0
            ? BytesToSizeConverter.Format(report.Ram.TotalBytes)
            : "Non disponible";

        HwOsText.Text = Known(report.Os.Caption) +
                        (Known(report.Os.Build) == "Non disponible" ? string.Empty : " (build " + report.Os.Build + ")") +
                        " · DirectX " + Known(report.DirectXVersion);

        var display = report.Displays.FirstOrDefault(d => d.Primary) ?? report.Displays.FirstOrDefault();
        HwDisplayText.Text = display is null || display.Width <= 0 || display.Height <= 0
            ? "Non disponible"
            : display.Width + " × " + display.Height +
              (display.RefreshRate > 0 ? " @ " + display.RefreshRate + " Hz" : string.Empty);
    }

    private async Task<bool> LoadDisksAsync()
    {
        if (_disksBusy) return _diskTotalBytes > 0;
        _disksBusy = true;
        try
        {
            var disks = await Task.Run(() => DiskAnalyzer.Instance.AnalyzeFast());
            var system = disks.FirstOrDefault(d => d.IsSystemDisk) ?? disks.FirstOrDefault();
            var volume = system?.Volumes.FirstOrDefault(v => v.IsSystem) ?? system?.Volumes.FirstOrDefault();
            if (volume is null || volume.SizeBytes <= 0)
            {
                _diskFreeBytes = 0;
                _diskTotalBytes = 0;
            }
            else
            {
                _diskFreeBytes = volume.FreeBytes;
                _diskTotalBytes = volume.SizeBytes;
            }
            ApplyDiskCard();
            return _diskTotalBytes > 0;
        }
        catch (Exception ex)
        {
            Log.Error("UI", "Lecture de l'espace disque impossible", ex);
            ShellState.Status("Espace disque indisponible : " + ex.Message);
            return false;
        }
        finally
        {
            _disksBusy = false;
        }
    }

    private async void OnRefreshDisksClick(object sender, RoutedEventArgs e)
    {
        var ok = await LoadDisksAsync();
        ShellState.Status(ok ? "Espace disque libre actualisé." : "Espace disque non disponible.");
    }

    private async Task LoadProcessesAsync()
    {
        if (_processesBusy) return;
        _processesBusy = true;
        try
        {
            var processes = await Task.Run(() => ProcessService.Instance.GetProcesses());
            var top = processes
                .Where(p => p.CpuPercent > 0.5)
                .OrderByDescending(p => p.CpuPercent)
                .Take(5)
                .ToList();
            RenderProcesses(top);
        }
        catch (Exception ex)
        {
            Log.Error("UI", "Lecture des processus impossible", ex);
            ShellState.Status("Processus indisponibles : " + ex.Message);
        }
        finally
        {
            _processesBusy = false;
        }
    }

    private void RenderProcesses(IReadOnlyList<ProcessSnapshot> processes)
    {
        ProcessList.Children.Clear();
        ProcessEmpty.Visibility = processes.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        foreach (var process in processes)
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
            }

            var row = new Grid { Margin = new Thickness(0, 0, 0, 11), VerticalAlignment = VerticalAlignment.Center };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var warning = new TextBlock
            {
                FontFamily = IconFont(),
                FontSize = 13,
                Text = canClose ? string.Empty : "\uE7BA",
                Foreground = BrushOf("WarnBrush"),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 7, 0)
            };
            if (!canClose) warning.ToolTip = string.IsNullOrWhiteSpace(reason) ? "Fermeture impossible" : reason;
            row.Children.Add(warning);

            var name = new TextBlock
            {
                Style = StyleOf("Body"),
                Text = string.IsNullOrWhiteSpace(process.Name) ? "Processus sans nom" : process.Name,
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(name, 1);
            row.Children.Add(name);

            var pid = new TextBlock
            {
                Style = StyleOf("Caption"),
                Text = "PID " + process.ProcessId,
                Margin = new Thickness(12, 0, 14, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(pid, 2);
            row.Children.Add(pid);

            var cpu = new TextBlock
            {
                Style = StyleOf("Caption"),
                Text = process.CpuPercent.ToString("F1", CultureInfo.CurrentCulture) + " %",
                Foreground = BrushOf("TextBrush"),
                MinWidth = 46,
                TextAlignment = TextAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(cpu, 3);
            row.Children.Add(cpu);

            var memory = new TextBlock
            {
                Style = StyleOf("Caption"),
                Text = BytesToSizeConverter.Format(process.MemoryBytes),
                MinWidth = 64,
                TextAlignment = TextAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(memory, 4);
            row.Children.Add(memory);

            ProcessList.Children.Add(row);
        }
    }

    private async Task LoadGamesAsync()
    {
        try
        {
            var games = await Task.Run(() => GameScanner.Instance.GetLibrary());
            var top = games
                .OrderByDescending(g => g.LastLaunch.HasValue)
                .ThenByDescending(g => g.LastLaunch ?? DateTime.MinValue)
                .Take(6)
                .ToList();
            RenderGames(top);
        }
        catch (Exception ex)
        {
            Log.Error("UI", "Lecture de la bibliothèque de jeux impossible", ex);
            ShellState.Status("Bibliothèque de jeux indisponible : " + ex.Message);
        }
    }

    private void RenderGames(IReadOnlyList<GameInfo> games)
    {
        GamesList.Children.Clear();
        GamesEmptyPanel.Visibility = games.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        foreach (var game in games) GamesList.Children.Add(BuildGameCard(game));
    }

    private UIElement BuildGameCard(GameInfo game)
    {
        var card = new Border
        {
            Style = StyleOf("CardFlat"),
            Margin = new Thickness(0, 0, 0, 9),
            Padding = new Thickness(12),
            Cursor = Cursors.Hand,
            ToolTip = "Ouvrir la fiche « Jeux » de GameBoost"
        };
        card.MouseLeftButtonUp += (_, _) => NavigationService.Navigate("games");

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var icon = BuildGameIcon(game);
        grid.Children.Add(icon);

        var text = new StackPanel
        {
            Margin = new Thickness(12, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
        text.Children.Add(new TextBlock
        {
            Style = StyleOf("Body"),
            Text = string.IsNullOrWhiteSpace(game.Name) ? "Jeu sans nom" : game.Name,
            TextTrimming = TextTrimming.CharacterEllipsis
        });
        text.Children.Add(new TextBlock
        {
            Style = StyleOf("Caption"),
            Text = PlatformLabel(game.Platform),
            Margin = new Thickness(0, 2, 0, 0)
        });
        Grid.SetColumn(text, 1);
        grid.Children.Add(text);

        card.Child = grid;
        return card;
    }

    private UIElement BuildGameIcon(GameInfo game)
    {
        try
        {
            var path = GameIconCache.GetIconPath(game.ExecutablePath);
            if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
            {
                return new Image
                {
                    Source = new BitmapImage(new Uri(path)),
                    Width = 30,
                    Height = 30,
                    Stretch = Stretch.Uniform,
                    VerticalAlignment = VerticalAlignment.Center
                };
            }
        }
        catch (Exception ex)
        {
            Log.Warn("UI", "Icône de jeu impossible : " + ex.Message);
        }

        return new TextBlock
        {
            FontFamily = IconFont(),
            FontSize = 17,
            Text = "\uE7FC",
            Foreground = BrushOf("TextMutedBrush"),
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            Width = 30,
            Height = 30,
            TextAlignment = TextAlignment.Center
        };
    }

    private async void OnScanGamesClick(object sender, RoutedEventArgs e)
    {
        if (_scanningGames) return;
        _scanningGames = true;
        ScanGamesButton.IsEnabled = false;
        ScanProgress.Visibility = Visibility.Visible;
        ShellState.Status("Recherche de vos jeux en cours (environ 12 secondes)…");
        try
        {
            var found = await Task.Run(() => GameScanner.Instance.Scan());
            ShellState.Status("Recherche terminée : " + found.Count + " jeu(s) détecté(s).");
            await LoadGamesAsync();
        }
        catch (Exception ex)
        {
            Log.Error("UI", "Recherche des jeux impossible", ex);
            ShellState.Status("La recherche de jeux a échoué : " + ex.Message);
        }
        finally
        {
            _scanningGames = false;
            ScanGamesButton.IsEnabled = true;
            ScanProgress.Visibility = Visibility.Collapsed;
        }
    }

    private async Task EvaluateBoosterAsync()
    {
        try
        {
            var plan = await Task.Run(() => BoostService.Instance.BuildPlan(
                null, Array.Empty<int>(), true, true, true, true, true, false));
            var available = plan.Steps.Any(step => step.Enabled);
            BoosterButton.IsEnabled = available;
            BoosterButton.ToolTip = available
                ? "Ouvrir l'assistant de boost : chaque étape est résumée avant application, sans modification sans votre accord."
                : "Aucune optimisation n'est possible pour le moment : aucune étape du boost ne serait appliquée.";
        }
        catch (Exception ex)
        {
            Log.Error("UI", "Évaluation du boost impossible", ex);
            BoosterButton.IsEnabled = true;
        }
    }

    private void RefreshPendingBanner()
    {
        try
        {
            PendingBanner.Visibility = BoostService.Instance.HasPendingSession
                ? Visibility.Visible
                : Visibility.Collapsed;
        }
        catch (Exception ex)
        {
            Log.Error("UI", "Vérification de la session en attente impossible", ex);
            PendingBanner.Visibility = Visibility.Collapsed;
        }
    }

    private async void OnRestorePendingClick(object sender, RoutedEventArgs e)
    {
        var answer = MessageBox.Show(
            "Une session Boost précédente n'a pas été terminée. Terminer la session et remettre les réglages d'origine ?",
            "GameBoost", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (answer != MessageBoxResult.Yes) return;

        try
        {
            BoostService.Instance.LoadPendingSession();
            var session = await BoostService.Instance.EndSessionAsync();
            ShellState.Status(session.IsActive
                ? "La session en attente n'a pas pu être terminée."
                : "Session Boost en attente terminée : les réglages d'origine sont restaurés.");
        }
        catch (Exception ex)
        {
            Log.Error("UI", "Restauration de la session impossible", ex);
            ShellState.Status("Restauration impossible : " + ex.Message);
        }
        finally
        {
            RefreshPendingBanner();
        }
    }

    private void OnAnalyzeClick(object sender, RoutedEventArgs e) => NavigationService.Navigate("analysis");

    private void OnHardwareDetailsClick(object sender, RoutedEventArgs e) => NavigationService.Navigate("hardware");

    private void OnProcessesClick(object sender, RoutedEventArgs e) => NavigationService.Navigate("processes");

    private void OnBoosterClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var dialog = new BoostDialog();
            dialog.Owner = Window.GetWindow(this);
            dialog.ShowDialog();
            RefreshPendingBanner();
            _ = LoadGamesAsync();
            _ = LoadProcessesAsync();
        }
        catch (Exception ex)
        {
            Log.Error("UI", "Ouverture de l'assistant de boost impossible", ex);
            ShellState.Status("Impossible d'ouvrir l'assistant de boost : " + ex.Message);
        }
    }

    private static string PlatformLabel(GamePlatform platform) => platform switch
    {
        GamePlatform.Manual => "Manuel",
        GamePlatform.Gog => "GOG",
        GamePlatform.BattleNet => "Battle.net",
        GamePlatform.Other => "Autre",
        _ => platform.ToString()
    };

    private static string Known(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "Non disponible";
        var trimmed = value.Trim();
        if (trimmed.Equals("Inconnu", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Equals("Inconnue", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Equals("Inconnus", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Equals("Inconnues", StringComparison.OrdinalIgnoreCase))
            return "Non disponible";
        return trimmed;
    }

    private static string VendorLabel(GpuVendor vendor) => vendor switch
    {
        GpuVendor.Nvidia => "NVIDIA",
        GpuVendor.Amd => "AMD",
        GpuVendor.Intel => "Intel",
        GpuVendor.Other => "Autre constructeur",
        _ => "Constructeur inconnu"
    };

    private static Style? StyleOf(string key) => Application.Current?.TryFindResource(key) as Style;

    private static FontFamily IconFont() =>
        Application.Current?.TryFindResource("IconFont") as FontFamily ?? new FontFamily("Segoe MDL2 Assets");

    private static Brush BrushOf(string key) =>
        Application.Current?.TryFindResource(key) as Brush ?? Brushes.Gray;
}
