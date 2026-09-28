using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using GameBoost.App.Pages.Settings;
using GameBoost.App.Services;
using GameBoost.Core.Data;
using GameBoost.Core.History;
using GameBoost.Core.Localization;
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
        LocManager.Instance.PropertyChanged += OnCultureChanged;
        Unloaded += OnCultureUnloaded;
    }

    private void OnCultureChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (_loading) return;
        _loading = true;
        try
        {
            ThemeCombo.ItemsSource = new[] { Loc.T("Settings_ThemeDark"), Loc.T("Settings_ThemeLight") };
            ThemeCombo.SelectedIndex = SettingsService.Current.Theme.Equals("Light", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
            LoadLanguages(Loc.CultureCode);
        }
        finally
        {
            _loading = false;
        }
    }

    private void OnCultureUnloaded(object sender, RoutedEventArgs e) =>
        LocManager.Instance.PropertyChanged -= OnCultureChanged;

    private void OnLoaded(object sender, RoutedEventArgs e) => LoadAll();

    private void LoadAll()
    {
        _loading = true;
        try
        {
            var settings = SettingsService.Current;

            LoadLanguages(Loc.CultureCode);
            ThemeCombo.ItemsSource = new[] { Loc.T("Settings_ThemeDark"), Loc.T("Settings_ThemeLight") };
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
            ShellState.Status(Loc.T("Settings_StatusReadFail", ex.Message));
        }
        finally
        {
            _loading = false;
        }
    }

    private void LoadLanguages(string current)
    {
        var codes = new List<string>();
        var labels = new List<string>();
        foreach (var language in Loc.Languages)
        {
            codes.Add(language.Code);
            labels.Add(language.Code == "auto" ? Loc.T("Settings_LanguageAuto") : language.NativeName);
        }
        LanguageCombo.ItemsSource = labels;
        LanguageCombo.Tag = codes;
        LanguageCombo.SelectedIndex = Math.Max(0, codes.IndexOf(current));
    }

    private void OnLanguageChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading) return;
        if (LanguageCombo.Tag is not List<string> codes || LanguageCombo.SelectedIndex < 0) return;
        var code = codes[LanguageCombo.SelectedIndex];
        Loc.SetCulture(code);
        SettingsService.Update(s => s.Language = code);
        ShellState.Status(Loc.T("Settings_StatusLanguage"));
    }

    private void LoadIntervals(int current)
    {
        var values = new List<int>(IntervalValues);
        if (!values.Contains(current)) values.Add(current);
        values.Sort();
        IntervalCombo.ItemsSource = values.Select(value =>
            Loc.T(value > 1 ? "Settings_Minutes" : "Settings_Minute", value)).ToList();
        IntervalCombo.Tag = values;
        IntervalCombo.SelectedIndex = Math.Max(0, values.IndexOf(current));
    }

    private void LoadRetention(int current)
    {
        var values = new List<int>(RetentionValues);
        if (!values.Contains(current)) values.Add(current);
        values.Sort();
        RetentionCombo.ItemsSource = values.Select(value =>
            Loc.T(value > 1 ? "Settings_Days" : "Settings_Day", value)).ToList();
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
            ShellState.Status(Loc.T("Settings_StatusSaved", label));
        }
        catch (Exception ex)
        {
            Log.Error("UI", "Sauvegarde des paramètres", ex);
            ShellState.Status(Loc.T("Settings_StatusSaveFail", ex.Message));
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
            ShellState.Status(Loc.T("Settings_StatusSaved", label));
        }
        catch (Exception ex)
        {
            Log.Error("UI", "Sauvegarde de l'overlay", ex);
            ShellState.Status(Loc.T("Settings_StatusSaveFail", ex.Message));
        }
    }

    private void OnThemeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading) return;
        var light = ThemeCombo.SelectedIndex == 1;
        ThemeService.Apply(light ? "Light" : "Dark");
        ShellState.Status(Loc.T("Settings_StatusSaved", Loc.T(light ? "Settings_ThemeLight" : "Settings_ThemeDark")));
    }

    private void OnStartWithWindowsChanged(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        var enabled = StartWithWindowsCheck.IsChecked == true;
        try
        {
            StartupRegistration.SetEnabled(enabled);
            SaveSetting(Loc.T("Settings_LblStartWithWindows"), settings => settings.StartWithWindows = enabled);
        }
        catch (Exception ex)
        {
            Log.Error("UI", "Modification du démarrage avec Windows", ex);
            ShellState.Status(Loc.T("Settings_StatusStartWinFail", ex.Message));
            _loading = true;
            StartWithWindowsCheck.IsChecked = StartupRegistration.IsEnabled();
            _loading = false;
        }
    }

    private void OnStartElevatedChanged(object sender, RoutedEventArgs e)
    {
        var enabled = StartElevatedToggle.IsChecked == true;
        SaveSetting(Loc.T("Settings_LblStartElevated"), settings => settings.StartElevated = enabled);
    }

    private void OnRelaunchAdminClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var path = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(path))
            {
                ShellState.Status(Loc.T("Settings_StatusNoPath"));
                return;
            }
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true, Verb = "runas" });
            ShellState.Status(Loc.T("Settings_StatusRelaunchAsked"));
        }
        catch (Exception ex)
        {
            Log.Error("UI", "Relance en administrateur", ex);
            ShellState.Status(Loc.T("Settings_StatusRelaunchFail", ex.Message));
        }
    }

    private void OnNotificationsChanged(object sender, RoutedEventArgs e)
    {
        var enabled = NotificationsCheck.IsChecked == true;
        SaveSetting(Loc.T("Settings_LblNotifications"), settings => settings.NotificationsEnabled = enabled);
    }

    private void OnIntervalChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading) return;
        var minutes = SelectedValue(IntervalCombo, 30);
        SaveSetting(Loc.T("Settings_LblInterval", minutes), settings => settings.AnalysisIntervalMinutes = minutes);
    }

    private void OnConfirmBeforeBoostChanged(object sender, RoutedEventArgs e)
    {
        var enabled = ConfirmBeforeBoostCheck.IsChecked == true;
        SaveSetting(Loc.T("Settings_LblConfirmBoost"), settings => settings.ConfirmBeforeBoost = enabled);
    }

    private void OnRelaunchClosedAppsChanged(object sender, RoutedEventArgs e)
    {
        var enabled = RelaunchClosedAppsCheck.IsChecked == true;
        SaveSetting(Loc.T("Settings_LblRelaunchClosed"), settings => settings.RelaunchClosedApps = enabled);
    }

    private void OnClearTempChanged(object sender, RoutedEventArgs e)
    {
        var enabled = ClearTempCheck.IsChecked == true;
        SaveSetting(Loc.T("Settings_LblClearTemp"), settings => settings.ClearTempOnBoost = enabled);
    }

    private void OnAutoCloseEnabledChanged(object sender, RoutedEventArgs e)
    {
        var enabled = AutoCloseEnabledCheck.IsChecked == true;
        SaveSetting(Loc.T("Settings_LblAutoClose"), settings => settings.AutoCloseAppsEnabled = enabled);
    }

    private void OnAddAutoCloseClick(object sender, RoutedEventArgs e)
    {
        var name = (AutoCloseBox.Text ?? string.Empty).Trim();
        if (name.Length == 0)
        {
            ShellState.Status(Loc.T("Settings_StatusEnterName"));
            return;
        }

        try
        {
            var added = false;
            SaveSetting(Loc.T("Settings_LblAutoClose"), settings =>
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
                ShellState.Status(Loc.T("Settings_StatusAlreadyListed", name));
            }
        }
        catch (Exception ex)
        {
            Log.Error("UI", "Ajout d'une application à fermer", ex);
            ShellState.Status(Loc.T("Settings_StatusAddFail", ex.Message));
        }
    }

    private void OnRemoveAutoCloseClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string name }) return;
        SaveSetting(Loc.T("Settings_LblAutoClose"), settings =>
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
            ShellState.Status(Loc.T("Settings_StatusEnterExclusion"));
            return;
        }

        try
        {
            var added = false;
            SaveSetting(Loc.T("Settings_LblExclusions"), settings =>
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
                ShellState.Status(Loc.T("Settings_StatusAlreadyExcluded", name));
            }
        }
        catch (Exception ex)
        {
            Log.Error("UI", "Ajout d'une exclusion", ex);
            ShellState.Status(Loc.T("Settings_StatusAddFail", ex.Message));
        }
    }

    private void OnRemoveExclusionClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string name }) return;
        SaveSetting(Loc.T("Settings_LblExclusions"), settings =>
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
                Title = Loc.T("Settings_DialogGameFolder"),
                Multiselect = false
            };
            if (dialog.ShowDialog() != true) return;

            var folder = dialog.FolderName;
            var added = false;
            SaveSetting(Loc.T("Settings_LblCustomFolders"), settings =>
            {
                if (settings.CustomGameFolders.Any(entry => string.Equals(entry, folder, StringComparison.OrdinalIgnoreCase)))
                    return;
                settings.CustomGameFolders.Add(folder);
                added = true;
            });
            if (added)
            {
                RefreshFolderList();
                ShellState.Status(Loc.T("Settings_StatusFolderAdded"));
            }
            else
            {
                ShellState.Status(Loc.T("Settings_StatusFolderDup"));
            }
        }
        catch (Exception ex)
        {
            Log.Error("UI", "Ajout d'un dossier de jeux", ex);
            ShellState.Status(Loc.T("Settings_StatusAddFail", ex.Message));
        }
    }

    private void OnRemoveFolderClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string folder }) return;
        SaveSetting(Loc.T("Settings_LblCustomFolders"), settings =>
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
            SaveOverlay(Loc.T("Settings_LblOverlay"), o => o.Enabled = OverlayEnabledToggle.IsChecked == true);
        else if (sender == ShowFpsToggle)
            SaveOverlay(Loc.T("Settings_LblOverlayFps"), o => o.ShowFps = ShowFpsToggle.IsChecked == true);
        else if (sender == ShowFrameTimeToggle)
            SaveOverlay(Loc.T("Settings_LblOverlayFrameTime"), o => o.ShowFrameTime = ShowFrameTimeToggle.IsChecked == true);
        else if (sender == ShowCpuToggle)
            SaveOverlay(Loc.T("Settings_LblOverlayCpu"), o => o.ShowCpu = ShowCpuToggle.IsChecked == true);
        else if (sender == ShowGpuToggle)
            SaveOverlay(Loc.T("Settings_LblOverlayGpu"), o => o.ShowGpu = ShowGpuToggle.IsChecked == true);
        else if (sender == ShowRamToggle)
            SaveOverlay(Loc.T("Settings_LblOverlayRam"), o => o.ShowRam = ShowRamToggle.IsChecked == true);
        else if (sender == ShowTempToggle)
            SaveOverlay(Loc.T("Settings_LblOverlayTemp"), o => o.ShowTemperatures = ShowTempToggle.IsChecked == true);
    }

    private void OnOpacityChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_loading) return;
        var value = (int)Math.Round(e.NewValue);
        OpacityLabel.Text = value + " %";
        if (value == _savedOpacity) return;
        _savedOpacity = value;
        SaveOverlay(Loc.T("Settings_LblOpacity"), o => o.Opacity = value);
    }

    private void OnRecordSessionsChanged(object sender, RoutedEventArgs e)
    {
        var enabled = RecordSessionsCheck.IsChecked == true;
        SaveSetting(Loc.T("Settings_LblSessions"), settings => settings.RecordSessions = enabled);
    }

    private void OnRetentionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading) return;
        var days = SelectedValue(RetentionCombo, 90);
        SaveSetting(Loc.T("Settings_LblRetention", days), settings => settings.HistoryRetentionDays = days);
    }

    private void OnPruneHistoryClick(object sender, RoutedEventArgs e)
    {
        try
        {
            HistoryService.Instance.PruneOldSessions();
            ShellState.Status(Loc.T("Settings_StatusPruned", SettingsService.Current.HistoryRetentionDays));
        }
        catch (Exception ex)
        {
            Log.Error("UI", "Nettoyage de l'historique", ex);
            ShellState.Status(Loc.T("Settings_StatusPruneFail", ex.Message));
        }
    }

    private void OnBackupLocationChanged(object sender, TextChangedEventArgs e)
    {
        if (_loading) return;
        var path = (BackupBox.Text ?? string.Empty).Trim();
        SaveSetting(Loc.T("Settings_LblBackupLocation"), settings => settings.BackupLocation = path);
    }

    private void OnBrowseBackupClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var dialog = new OpenFolderDialog
            {
                Title = Loc.T("Settings_DialogBackupFolder"),
                Multiselect = false
            };
            if (dialog.ShowDialog() != true) return;
            BackupBox.Text = dialog.FolderName;
        }
        catch (Exception ex)
        {
            Log.Error("UI", "Choix du dossier des sauvegardes", ex);
            ShellState.Status(Loc.T("Settings_StatusAddFail", ex.Message));
        }
    }

    private void OnOpenBackupClick(object sender, RoutedEventArgs e)
    {
        var path = SettingsService.Current.BackupLocation;
        if (string.IsNullOrWhiteSpace(path)) path = AppPaths.BackupsDir;
        OpenFolder(path, Loc.T("Settings_LblBackupFolder"));
    }

    private void OnOpenLogsClick(object sender, RoutedEventArgs e) => OpenFolder(AppPaths.LogsDir, Loc.T("Settings_LblLogsFolder"));

    private static void OpenFolder(string path, string label)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                ShellState.Status(Loc.T("Settings_StatusEmptyPath", label));
                return;
            }
            Directory.CreateDirectory(path);
            Process.Start(new ProcessStartInfo("explorer.exe", "\"" + path + "\"") { UseShellExecute = true });
            ShellState.Status(Loc.T("Settings_StatusOpening", label));
        }
        catch (Exception ex)
        {
            Log.Error("UI", "Ouverture du " + label, ex);
            ShellState.Status(Loc.T("Settings_StatusOpenFail", ex.Message));
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
            ShellState.Status(Loc.T("Settings_StatusLogLines", lines.Count));
        }
        catch (Exception ex)
        {
            Log.Error("UI", "Lecture des journaux", ex);
            ShellState.Status(Loc.T("Settings_StatusLogReadFail", ex.Message));
        }
    }

    private void OnCopyLogsClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var lines = LogList.ItemsSource as IEnumerable<string>;
            if (lines is null)
            {
                ShellState.Status(Loc.T("Settings_StatusNoLogCopy"));
                return;
            }
            Clipboard.SetText(string.Join(Environment.NewLine, lines));
            ShellState.Status(Loc.T("Settings_StatusCopied"));
        }
        catch (Exception ex)
        {
            Log.Error("UI", "Copie des journaux", ex);
            ShellState.Status(Loc.T("Settings_StatusCopyFail", ex.Message));
        }
    }

    private void OnTrimLogsClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var days = SettingsService.Current.LogRetentionDays;
            Log.TrimAll(days);
            ShellState.Status(Loc.T("Settings_StatusLogsTrimmed", days));
        }
        catch (Exception ex)
        {
            Log.Error("UI", "Nettoyage des journaux", ex);
            ShellState.Status(Loc.T("Settings_StatusPruneFail", ex.Message));
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
