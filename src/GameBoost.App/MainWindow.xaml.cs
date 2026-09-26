using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using GameBoost.App.Controls;
using GameBoost.App.Pages;
using GameBoost.App.Services;
using GameBoost.Core.Logging;
using GameBoost.Core.Monitoring;
using GameBoost.Core.Models;

namespace GameBoost.App;

public partial class MainWindow : Window
{
    private readonly Dictionary<string, Func<UserControl>> _factories = new(StringComparer.OrdinalIgnoreCase)
    {
        ["dashboard"] = () => new DashboardPage(),
        ["analysis"] = () => new AnalysisPage(),
        ["hardware"] = () => new HardwarePage(),
        ["gpu"] = () => new GpuPage(),
        ["storage"] = () => new StoragePage(),
        ["games"] = () => new GamesPage(),
        ["profiles"] = () => new ProfilesPage(),
        ["processes"] = () => new ProcessesPage(),
        ["monitoring"] = () => new MonitoringPage(),
        ["history"] = () => new HistoryPage(),
        ["reports"] = () => new ReportPage(),
        ["settings"] = () => new SettingsPage()
    };

    private readonly Dictionary<string, UserControl> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<RadioButton> _navButtons = new();
    private bool _syncingNav;
    private bool _chipsReady;

    public MainWindow()
    {
        InitializeComponent();

        foreach (var child in NavPanel.Children)
        {
            if (child is RadioButton rb) _navButtons.Add(rb);
        }

        Loaded += OnLoaded;
        SizeChanged += OnWindowSizeChanged;
        StateChanged += OnWindowStateChanged;

        NavigationService.Navigated += OnNavigated;
        SystemMonitor.Instance.SampleUpdated += OnSampleUpdated;

        ShowPage("dashboard");
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _chipsReady = true;
        MaxHeight = SystemParameters.WorkArea.Height;
        ShellState.Status("Bienvenue sur GameBoost — cliquez sur « Analyse » pour diagnostiquer votre PC.");
    }

    private void OnWindowSizeChanged(object sender, SizeChangedEventArgs e)
    {
        var narrow = e.NewSize.Width < 1180;
        SidebarColumn.Width = new GridLength(narrow ? 64 : 232);
        ShellState.Instance.LabelsVisible = !narrow;
        foreach (var rb in _navButtons)
        {
            rb.ToolTip = narrow ? rb.Content + " — " + (rb.ToolTip?.ToString() ?? string.Empty) : rb.Tag switch
            {
                "dashboard" => "Tableau de bord : état du PC en temps réel",
                "analysis" => "Diagnostic complet du système et corrections",
                "hardware" => "Fiche détaillée du matériel détecté",
                "gpu" => "Carte graphique, pilote et technologies compatibles",
                "storage" => "Disques, espace libre, état et températures",
                "games" => "Jeux détectés (Steam, Epic, Ubisoft, Xbox…)",
                "profiles" => "Profils graphiques par jeu",
                "processes" => "Processus en cours et fermeture ciblée",
                "monitoring" => "FPS, frametime et overlay pendant le jeu",
                "history" => "Sessions enregistrées et comparaison A/B",
                "reports" => "Export HTML des analyses et mesures",
                "settings" => "Réglages de GameBoost",
                _ => rb.Content?.ToString() ?? string.Empty
            };
        }
    }

    private void OnWindowStateChanged(object? sender, EventArgs e)
    {
        MaxGlyph.Text = WindowState == WindowState.Maximized ? "\uE923" : "\uE922";
    }

    private void OnNavChecked(object sender, RoutedEventArgs e)
    {
        if (_syncingNav) return;
        if (sender is RadioButton { IsChecked: true, Tag: string key }) NavigationService.Navigate(key);
    }

    private void OnNavigated(string key)
    {
        ShowPage(key);
    }

    private void ShowPage(string key)
    {
        if (!_factories.TryGetValue(key, out var factory)) return;

        if (!_cache.TryGetValue(key, out var page))
        {
            try
            {
                page = factory();
                _cache[key] = page;
            }
            catch (Exception ex)
            {
                Log.Error("UI", "Création de la page " + key, ex);
                ShellState.Status("Impossible d'ouvrir cette page : " + ex.Message);
                return;
            }
        }

        PageHost.Content = page;

        _syncingNav = true;
        foreach (var rb in _navButtons)
            rb.IsChecked = string.Equals(rb.Tag as string, key, StringComparison.OrdinalIgnoreCase);
        _syncingNav = false;

        var transform = new TranslateTransform(0, 10);
        PageHost.RenderTransform = transform;
        PageHost.Opacity = 0;
        PageHost.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(170)));
        transform.BeginAnimation(TranslateTransform.YProperty,
            new DoubleAnimation(10, 0, TimeSpan.FromMilliseconds(170)) { EasingFunction = new QuadraticEase() });
    }

    private void OnSampleUpdated(MonitorSample sample)
    {
        if (!_chipsReady) return;
        try
        {
            Dispatcher.BeginInvoke(() =>
            {
                try
                {
                    ChipCpuValue.Text = sample.CpuUsagePercent.ToString("F0") + " %";
                    ChipRamValue.Text = BytesToSizeConverter.Format(sample.RamUsedBytes) +
                                        (sample.RamTotalBytes > 0
                                            ? " / " + BytesToSizeConverter.Format(sample.RamTotalBytes)
                                            : string.Empty);
                    ChipGpuValue.Text = (sample.GpuTemperatureC.HasValue
                            ? sample.GpuTemperatureC.Value.ToString("F0") + " °C"
                            : "—") +
                        " · " + sample.GpuUsagePercent.ToString("F0") + " %";
                }
                catch
                {
                }
            });
        }
        catch
        {
        }
    }

    private void OnThemeClick(object sender, RoutedEventArgs e)
    {
        ThemeService.Toggle();
        ShellState.Status(ThemeService.CurrentTheme == "Light" ? "Thème clair activé." : "Thème sombre activé.");
    }

    private void OnTitleBarDrag(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            ToggleMaximize();
            return;
        }
        if (e.LeftButton == MouseButtonState.Pressed)
        {
            try
            {
                DragMove();
            }
            catch
            {
            }
        }
    }

    private void ToggleMaximize()
    {
        if (WindowState == WindowState.Maximized)
        {
            WindowState = WindowState.Normal;
        }
        else
        {
            var work = SystemParameters.WorkArea;
            MaxHeight = work.Height;
            WindowState = WindowState.Maximized;
        }
    }

    private void OnMinimize(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void OnMaximize(object sender, RoutedEventArgs e) => ToggleMaximize();

    private void OnClose(object sender, RoutedEventArgs e) => Application.Current.Shutdown();
}
