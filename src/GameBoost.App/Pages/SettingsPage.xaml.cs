using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using GameBoost.App.Pages.Settings;
using GameBoost.App.Services;
using GameBoost.Core.Data;
using GameBoost.Core.History;
using GameBoost.Core.Logging;
using GameBoost.Core.Models;
using GameBoost.Core.Overlay;
using Microsoft.Win32;

namespace GameBoost.App.Pages;

public partial class SettingsPage : UserControl
{
    private static readonly int[] IntervalValues = { 5, 15, 30, 60 };
    private static readonly int[] RetentionValues = { 30, 60, 90, 180 };

    private bool _loading;
    private int _savedOpacity = 85;

    public SettingsPage()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e) => LoadAll();

    private void LoadAll()
    {
        _loading = true;
        try
        {
            var settings = SettingsService.Current;

            ThemeCombo.ItemsSource = new[] { "Sombre", "Clair" };
            ThemeCombo.SelectedIndex = settings.Theme.Equals("Light", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
            StartWithWindowsCheck.IsChecked = StartupRegistration.IsEnabled();
            StartElevatedToggle.IsChecked = settings.StartElevated;
            NotificationsCheck.IsChecked = settings.NotificationsEnabled;

            LoadIntervals(settings.AnalysisIntervalMinutes);
            ConfirmBeforeBoostCheck.IsChecked = settings.ConfirmBeforeBoost;
            RelaunchClosedAppsCheck.IsChecked = settings.RelaunchClosedApps;
            ClearTempCheck.IsChecked = settings.ClearTempOnBoost;
            AutoCloseEnabledCheck.IsChecked = settings.AutoCloseAppsEnabled;
            RefreshAutoCloseList();
            RefreshExclusionList();
            RefreshFolderList();
            RunningPicker.ItemsSource = GetRunningProcessNames();

            var overlay = OverlayService.Instance.GetDefault();
            OverlayEnabledToggle.IsChecked = overlay.Enabled;
            ShowFpsToggle.IsChecked = overlay.ShowFps;
            ShowFrameTimeToggle.IsChecked = overlay.ShowFrameTime;
            ShowCpuToggle.IsChecked = overlay.ShowCpu;
            ShowGpuToggle.IsChecked = overlay.ShowGpu;
            ShowRamToggle.IsChecked = overlay.ShowRam;
            ShowTempToggle.IsChecked = overlay.ShowTemperatures;
            _savedOpacity = Math.Clamp(overlay.Opacity, 30, 100);
            OpacitySlider.Value = _savedOpacity;
            OpacityLabel.Text = _savedOpacity + " %";

            RecordSessionsCheck.IsChecked = settings.RecordSessions;
            LoadRetention(settings.HistoryRetentionDays);

            BackupBox.Text = settings.BackupLocation ?? string.Empty;
            VersionText.Text = typeof(SettingsPage).Assembly.GetName().Version?.ToString(3) ?? "1.0.0";
        }
        catch (Exception ex)
        {
            Log.Error("UI", "Lecture des paramètres", ex);
            ShellState.Status("Lecture des paramètres impossible : " + ex.Message);
        }
        finally
        {
            _loading = false;
        }
    }

    private void LoadIntervals(int current)
    {
        var values = new List<int>(IntervalValues);
        if (!values.Contains(current)) values.Add(current);
        values.Sort();
        IntervalCombo.ItemsSource = values.Select(value => value + (value > 1 ? " minutes" : " minute")).ToList();
        IntervalCombo.Tag = values;
        IntervalCombo.SelectedIndex = Math.Max(0, values.IndexOf(current));
    }

    private void LoadRetention(int current)
    {
        var values = new List<int>(RetentionValues);
        if (!values.Contains(current)) values.Add(current);
        values.Sort();
        RetentionCombo.ItemsSource = values.Select(value => value + (value > 1 ? " jours" : " jour")).ToList();
        RetentionCombo.Tag = values;
        RetentionCombo.SelectedIndex = Math.Max(0, values.IndexOf(current));
    }

    private static int SelectedValue(ComboBox combo, int fallback)
    {
        if (combo.Tag is not List<int> values || combo.SelectedIndex < 0 || combo.SelectedIndex >= values.Count)
            return fallback;
        return values[combo.SelectedIndex];
    }

    private void SaveSetting(string label, Action<AppSettings> mutate)
    {
        if (_loading) return;
        try
        {
            SettingsService.Update(mutate);
            ShellState.Status("Modifié : " + label);
        }
        catch (Exception ex)
        {
            Log.Error("UI", "Sauvegarde des paramètres", ex);
            ShellState.Status("Sauvegarde impossible : " + ex.Message);
        }
    }

    private void SaveOverlay(string label, Action<OverlaySettings> mutate)
    {
        if (_loading) return;
        try
        {
            var overlay = OverlayService.Instance.GetDefault();
            mutate(overlay);
            OverlayService.Instance.SaveDefault(overlay);
            ShellState.Status("Modifié : " + label);
        }
        catch (Exception ex)
        {
            Log.Error("UI", "Sauvegarde de l'overlay", ex);
            ShellState.Status("Sauvegarde impossible : " + ex.Message);
        }
    }

    private void OnThemeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading) return;
        var light = ThemeCombo.SelectedIndex == 1;
        ThemeService.Apply(light ? "Light" : "Dark");
        ShellState.Status("Modifié : thème " + (light ? "clair" : "sombre"));
    }

    private void OnStartWithWindowsChanged(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        var enabled = StartWithWindowsCheck.IsChecked == true;
        try
        {
            StartupRegistration.SetEnabled(enabled);
            SaveSetting("démarrage avec Windows", settings => settings.StartWithWindows = enabled);
        }
        catch (Exception ex)
        {
            Log.Error("UI", "Modification du démarrage avec Windows", ex);
            ShellState.Status("Démarrage avec Windows impossible : " + ex.Message);
            _loading = true;
            StartWithWindowsCheck.IsChecked = StartupRegistration.IsEnabled();
            _loading = false;
        }
    }

    private void OnStartElevatedChanged(object sender, RoutedEventArgs e)
    {
        var enabled = StartElevatedToggle.IsChecked == true;
        SaveSetting("démarrage en administrateur", settings => settings.StartElevated = enabled);
    }

    private void OnRelaunchAdminClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var path = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(path))
            {
                ShellState.Status("Chemin de l'application introuvable : relance impossible.");
                return;
            }
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true, Verb = "runas" });
            ShellState.Status("Relance en administrateur demandée.");
        }
        catch (Exception ex)
        {
            Log.Error("UI", "Relance en administrateur", ex);
            ShellState.Status("Relance annulée ou impossible : " + ex.Message);
        }
    }

    private void OnNotificationsChanged(object sender, RoutedEventArgs e)
    {
        var enabled = NotificationsCheck.IsChecked == true;
        SaveSetting("notifications", settings => settings.NotificationsEnabled = enabled);
    }

    private void OnIntervalChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading) return;
        var minutes = SelectedValue(IntervalCombo, 30);
        SaveSetting("fréquence des analyses (" + minutes + " min)", settings => settings.AnalysisIntervalMinutes = minutes);
    }

    private void OnConfirmBeforeBoostChanged(object sender, RoutedEventArgs e)
    {
        var enabled = ConfirmBeforeBoostCheck.IsChecked == true;
        SaveSetting("confirmation avant boost", settings => settings.ConfirmBeforeBoost = enabled);
    }

    private void OnRelaunchClosedAppsChanged(object sender, RoutedEventArgs e)
    {
        var enabled = RelaunchClosedAppsCheck.IsChecked == true;
        SaveSetting("relance des applications fermées", settings => settings.RelaunchClosedApps = enabled);
    }

    private void OnClearTempChanged(object sender, RoutedEventArgs e)
    {
        var enabled = ClearTempCheck.IsChecked == true;
        SaveSetting("nettoyage temporaire au boost", settings => settings.ClearTempOnBoost = enabled);
    }

    private void OnAutoCloseEnabledChanged(object sender, RoutedEventArgs e)
    {
        var enabled = AutoCloseEnabledCheck.IsChecked == true;
        SaveSetting("fermeture automatique des applications", settings => settings.AutoCloseAppsEnabled = enabled);
    }

    private void OnAddAutoCloseClick(object sender, RoutedEventArgs e)
    {
        var name = (AutoCloseBox.Text ?? string.Empty).Trim();
        if (name.Length == 0)
        {
            ShellState.Status("Saisissez le nom d'une application à fermer.");
            return;
        }

        try
        {
            var added = false;
            SaveSetting("applications fermées automatiquement", settings =>
            {
                if (settings.AutoCloseApps.Any(entry => string.Equals(entry, name, StringComparison.OrdinalIgnoreCase)))
                    return;
                settings.AutoCloseApps.Add(name);
                added = true;
            });
            if (added)
            {
                AutoCloseBox.Text = string.Empty;
                RefreshAutoCloseList();
            }
            else
            {
                ShellState.Status("« " + name + " » est déjà dans la liste.");
            }
        }
        catch (Exception ex)
        {
            Log.Error("UI", "Ajout d'une application à fermer", ex);
            ShellState.Status("Ajout impossible : " + ex.Message);
        }
    }

    private void OnRemoveAutoCloseClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string name }) return;
        SaveSetting("applications fermées automatiquement", settings =>
            settings.AutoCloseApps.RemoveAll(entry => string.Equals(entry, name, StringComparison.OrdinalIgnoreCase)));
        RefreshAutoCloseList();
    }

    private void OnRunningPicked(object sender, SelectionChangedEventArgs e)
    {
        if (_loading) return;
        if (RunningPicker.SelectedItem is string name) AutoCloseBox.Text = name;
    }

    private void RefreshAutoCloseList()
    {
        AutoCloseList.ItemsSource = null;
        AutoCloseList.ItemsSource = SettingsService.Current.AutoCloseApps.ToList();
    }

    private void OnAddExclusionClick(object sender, RoutedEventArgs e)
    {
        var name = (ExclusionBox.Text ?? string.Empty).Trim();
        if (name.Length == 0)
        {
            ShellState.Status("Saisissez un élément à exclure.");
            return;
        }

        try
        {
            var added = false;
            SaveSetting("exclusions", settings =>
            {
                if (settings.Exclusions.Any(entry => string.Equals(entry, name, StringComparison.OrdinalIgnoreCase)))
                    return;
                settings.Exclusions.Add(name);
                added = true;
            });
            if (added)
            {
                ExclusionBox.Text = string.Empty;
                RefreshExclusionList();
            }
            else
            {
                ShellState.Status("« " + name + " » est déjà exclu.");
            }
        }
        catch (Exception ex)
        {
            Log.Error("UI", "Ajout d'une exclusion", ex);
            ShellState.Status("Ajout impossible : " + ex.Message);
        }
    }

    private void OnRemoveExclusionClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string name }) return;
        SaveSetting("exclusions", settings =>
            settings.Exclusions.RemoveAll(entry => string.Equals(entry, name, StringComparison.OrdinalIgnoreCase)));
        RefreshExclusionList();
    }

    private void RefreshExclusionList()
    {
        ExclusionList.ItemsSource = null;
        ExclusionList.ItemsSource = SettingsService.Current.Exclusions.ToList();
    }

    private void OnAddFolderClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var dialog = new OpenFolderDialog
            {
                Title = "Choisir un dossier de jeux",
                Multiselect = false
            };
            if (dialog.ShowDialog() != true) return;

            var folder = dialog.FolderName;
            var added = false;
            SaveSetting("dossiers de jeux personnalisés", settings =>
            {
                if (settings.CustomGameFolders.Any(entry => string.Equals(entry, folder, StringComparison.OrdinalIgnoreCase)))
                    return;
                settings.CustomGameFolders.Add(folder);
                added = true;
            });
            if (added)
            {
                RefreshFolderList();
                ShellState.Status("Dossier ajouté : il sera scanné au prochain scan des jeux.");
            }
            else
            {
                ShellState.Status("Ce dossier est déjà dans la liste.");
            }
        }
        catch (Exception ex)
        {
            Log.Error("UI", "Ajout d'un dossier de jeux", ex);
            ShellState.Status("Ajout impossible : " + ex.Message);
        }
    }

    private void OnRemoveFolderClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string folder }) return;
        SaveSetting("dossiers de jeux personnalisés", settings =>
            settings.CustomGameFolders.RemoveAll(entry => string.Equals(entry, folder, StringComparison.OrdinalIgnoreCase)));
        RefreshFolderList();
    }

    private void RefreshFolderList()
    {
        FolderList.ItemsSource = null;
        FolderList.ItemsSource = SettingsService.Current.CustomGameFolders.ToList();
    }

    private void OnOverlayChanged(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        if (sender == OverlayEnabledToggle)
            SaveOverlay("overlay", o => o.Enabled = OverlayEnabledToggle.IsChecked == true);
        else if (sender == ShowFpsToggle)
            SaveOverlay("overlay : FPS", o => o.ShowFps = ShowFpsToggle.IsChecked == true);
        else if (sender == ShowFrameTimeToggle)
            SaveOverlay("overlay : temps par image", o => o.ShowFrameTime = ShowFrameTimeToggle.IsChecked == true);
        else if (sender == ShowCpuToggle)
            SaveOverlay("overlay : CPU", o => o.ShowCpu = ShowCpuToggle.IsChecked == true);
        else if (sender == ShowGpuToggle)
            SaveOverlay("overlay : GPU", o => o.ShowGpu = ShowGpuToggle.IsChecked == true);
        else if (sender == ShowRamToggle)
            SaveOverlay("overlay : RAM", o => o.ShowRam = ShowRamToggle.IsChecked == true);
        else if (sender == ShowTempToggle)
            SaveOverlay("overlay : températures", o => o.ShowTemperatures = ShowTempToggle.IsChecked == true);
    }

    private void OnOpacityChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_loading) return;
        var value = (int)Math.Round(e.NewValue);
        OpacityLabel.Text = value + " %";
        if (value == _savedOpacity) return;
        _savedOpacity = value;
        SaveOverlay("opacité de l'overlay", o => o.Opacity = value);
    }

    private void OnRecordSessionsChanged(object sender, RoutedEventArgs e)
    {
        var enabled = RecordSessionsCheck.IsChecked == true;
        SaveSetting("enregistrement des sessions", settings => settings.RecordSessions = enabled);
    }

    private void OnRetentionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading) return;
        var days = SelectedValue(RetentionCombo, 90);
        SaveSetting("rétention de l'historique (" + days + " j)", settings => settings.HistoryRetentionDays = days);
    }

    private void OnPruneHistoryClick(object sender, RoutedEventArgs e)
    {
        try
        {
            HistoryService.Instance.PruneOldSessions();
            ShellState.Status("Historique nettoyé selon la rétention de " +
                              SettingsService.Current.HistoryRetentionDays + " jours.");
        }
        catch (Exception ex)
        {
            Log.Error("UI", "Nettoyage de l'historique", ex);
            ShellState.Status("Nettoyage impossible : " + ex.Message);
        }
    }

    private void OnBackupLocationChanged(object sender, TextChangedEventArgs e)
    {
        if (_loading) return;
        var path = (BackupBox.Text ?? string.Empty).Trim();
        SaveSetting("emplacement des sauvegardes", settings => settings.BackupLocation = path);
    }

    private void OnBrowseBackupClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var dialog = new OpenFolderDialog
            {
                Title = "Choisir le dossier des sauvegardes",
                Multiselect = false
            };
            if (dialog.ShowDialog() != true) return;
            BackupBox.Text = dialog.FolderName;
        }
        catch (Exception ex)
        {
            Log.Error("UI", "Choix du dossier des sauvegardes", ex);
            ShellState.Status("Sélection impossible : " + ex.Message);
        }
    }

    private void OnOpenBackupClick(object sender, RoutedEventArgs e)
    {
        var path = SettingsService.Current.BackupLocation;
        if (string.IsNullOrWhiteSpace(path)) path = AppPaths.BackupsDir;
        OpenFolder(path, "dossier des sauvegardes");
    }

    private void OnOpenLogsClick(object sender, RoutedEventArgs e) => OpenFolder(AppPaths.LogsDir, "dossier des journaux");

    private static void OpenFolder(string path, string label)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                ShellState.Status("Chemin vide pour " + label + ".");
                return;
            }
            Directory.CreateDirectory(path);
            Process.Start(new ProcessStartInfo("explorer.exe", "\"" + path + "\"") { UseShellExecute = true });
            ShellState.Status("Ouverture du " + label + ".");
        }
        catch (Exception ex)
        {
            Log.Error("UI", "Ouverture du " + label, ex);
            ShellState.Status("Ouverture impossible : " + ex.Message);
        }
    }

    private void OnShowLogsClick(object sender, RoutedEventArgs e)
    {
        try
        {
            if (LogPanel.Visibility == Visibility.Visible)
            {
                LogPanel.Visibility = Visibility.Collapsed;
                return;
            }
            var lines = Log.ReadRecent(200);
            LogList.ItemsSource = lines.ToList();
            LogPanel.Visibility = Visibility.Visible;
            ShellState.Status(lines.Count + " ligne(s) de journal affichée(s).");
        }
        catch (Exception ex)
        {
            Log.Error("UI", "Lecture des journaux", ex);
            ShellState.Status("Lecture des journaux impossible : " + ex.Message);
        }
    }

    private void OnCopyLogsClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var lines = LogList.ItemsSource as IEnumerable<string>;
            if (lines is null)
            {
                ShellState.Status("Aucun journal à copier.");
                return;
            }
            Clipboard.SetText(string.Join(Environment.NewLine, lines));
            ShellState.Status("Journal copié dans le presse-papiers.");
        }
        catch (Exception ex)
        {
            Log.Error("UI", "Copie des journaux", ex);
            ShellState.Status("Copie impossible : " + ex.Message);
        }
    }

    private void OnTrimLogsClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var days = SettingsService.Current.LogRetentionDays;
            Log.TrimAll(days);
            ShellState.Status("Journaux nettoyés : conservation de " + days + " jours.");
        }
        catch (Exception ex)
        {
            Log.Error("UI", "Nettoyage des journaux", ex);
            ShellState.Status("Nettoyage impossible : " + ex.Message);
        }
    }

    private static List<string> GetRunningProcessNames()
    {
        var names = new List<string>();
        Process[] processes;
        try
        {
            processes = Process.GetProcesses();
        }
        catch (Exception ex)
        {
            Log.Error("UI", "Enumération des processus en cours", ex);
            return names;
        }

        foreach (var process in processes)
        {
            try
            {
                var name = process.ProcessName;
                if (!string.IsNullOrWhiteSpace(name)) names.Add(name);
            }
            catch
            {
            }
            finally
            {
                process.Dispose();
            }
        }

        names.Sort(StringComparer.CurrentCultureIgnoreCase);
        return names.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }
}
