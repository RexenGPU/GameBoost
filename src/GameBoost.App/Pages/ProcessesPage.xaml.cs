using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using GameBoost.App.Pages.Processes;
using GameBoost.App.Services;
using GameBoost.Core.Logging;
using GameBoost.Core.Models;
using GameBoost.Core.Processes;

namespace GameBoost.App.Pages;

public partial class ProcessesPage : UserControl
{
    private static readonly string[] SortLabels =
        { "Nom", "CPU %", "RAM", "GPU %" };

    private readonly List<ProcessRow> _rows = new();
    private readonly List<string> _neverClose = new();
    private readonly DispatcherTimer _timer;
    private string _sortKey = "cpu";
    private bool _sortDescending = true;
    private bool _refreshing;
    private bool _loaded;
    private int? _selectedPid;

    public ProcessesPage()
    {
        InitializeComponent();
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _timer.Tick += OnTimerTick;
        UpdateSortHeaders();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (!_loaded)
        {
            _loaded = true;
            ReloadNeverClose();
        }
        if (AutoToggle.IsChecked == true) _timer.Start();
        Refresh();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e) => _timer.Stop();

    private void OnTimerTick(object? sender, EventArgs e) => Refresh();

    private void OnAutoToggleChanged(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded) return;
        if (AutoToggle.IsChecked == true)
        {
            _timer.Start();
            ShellState.Status("Actualisation automatique activée (2 s).");
        }
        else
        {
            _timer.Stop();
            ShellState.Status("Actualisation automatique désactivée.");
        }
    }

    private async void Refresh()
    {
        if (_refreshing) return;
        _refreshing = true;
        try
        {
            var processes = await Task.Run(() => ProcessService.Instance.GetProcesses());
            Rebuild(processes);
        }
        catch (Exception ex)
        {
            Log.Error("UI", "Lecture des processus", ex);
            ShellState.Status("Lecture des processus impossible : " + ex.Message);
        }
        finally
        {
            _refreshing = false;
        }
    }

    private void Rebuild(List<ProcessSnapshot> processes)
    {
        var previous = new Dictionary<int, bool>();
        foreach (var row in _rows)
        {
            if (row.CanClose) previous[row.Pid] = row.IsChecked;
        }

        _rows.Clear();
        foreach (var snapshot in processes)
        {
            var canClose = ProcessService.Instance.CanClose(snapshot, out var reason);
            var row = new ProcessRow(snapshot, canClose, reason);
            if (previous.TryGetValue(row.Pid, out var checkedState)) row.IsChecked = checkedState;
            _rows.Add(row);
        }

        ApplyView();
    }

    private void ApplyView()
    {
        var query = (SearchBox.Text ?? string.Empty).Trim();
        var sorted = SortRows(_rows);

        var visible = query.Length == 0
            ? sorted.ToList()
            : sorted.Where(row =>
                row.NameLabel.Contains(query, StringComparison.CurrentCultureIgnoreCase) ||
                row.TitleLabel.Contains(query, StringComparison.CurrentCultureIgnoreCase) ||
                row.PublisherLabel.Contains(query, StringComparison.CurrentCultureIgnoreCase)).ToList();

        foreach (var row in _rows) row.IsSelected = false;
        ProcessRow? selected = null;
        if (_selectedPid is int pid) selected = visible.FirstOrDefault(row => row.Pid == pid);
        if (selected is not null) selected.IsSelected = true;

        ProcessList.ItemsSource = visible;
        EmptyText.Visibility = visible.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        var totalCpu = _rows.Sum(row => row.CpuValue);
        var summary = _rows.Count + " processus · " + totalCpu.ToString("F0") + " % CPU cumulé";
        if (query.Length > 0) summary += " · " + visible.Count + " correspondance(s) au filtre";
        SummaryText.Text = summary;

        CloseSelectedButton.IsEnabled = _rows.Any(row => row.IsChecked);
        FillDetail(selected);
    }

    private IEnumerable<ProcessRow> SortRows(IEnumerable<ProcessRow> rows)
    {
        switch (_sortKey)
        {
            case "name":
                return _sortDescending
                    ? rows.OrderByDescending(row => row.NameLabel, StringComparer.CurrentCultureIgnoreCase)
                    : rows.OrderBy(row => row.NameLabel, StringComparer.CurrentCultureIgnoreCase);
            case "ram":
                return _sortDescending
                    ? rows.OrderByDescending(row => row.RamValue)
                    : rows.OrderBy(row => row.RamValue);
            case "gpu":
                return _sortDescending
                    ? rows.OrderByDescending(row => row.GpuValue)
                    : rows.OrderBy(row => row.GpuValue);
            default:
                return _sortDescending
                    ? rows.OrderByDescending(row => row.CpuValue)
                    : rows.OrderBy(row => row.CpuValue);
        }
    }

    private void OnSortClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string key }) return;
        if (string.Equals(key, _sortKey, StringComparison.Ordinal))
        {
            _sortDescending = !_sortDescending;
        }
        else
        {
            _sortKey = key;
            _sortDescending = !string.Equals(key, "name", StringComparison.Ordinal);
        }
        UpdateSortHeaders();
        ApplyView();
        ShellState.Status("Tri : " + (key switch
        {
            "name" => "nom",
            "ram" => "mémoire",
            "gpu" => "GPU",
            _ => "processeur"
        }) + (_sortDescending ? " décroissant." : " croissant."));
    }

    private void UpdateSortHeaders()
    {
        var labels = new[] { SortNameButton, SortCpuButton, SortRamButton, SortGpuButton };
        var keys = new[] { "name", "cpu", "ram", "gpu" };
        for (var i = 0; i < labels.Length; i++)
        {
            var suffix = keys[i] == _sortKey ? (_sortDescending ? " ▼" : " ▲") : string.Empty;
            labels[i].Content = SortLabels[i] + suffix;
        }
    }

    private void OnSearchChanged(object sender, TextChangedEventArgs e)
    {
        if (!_loaded) return;
        ApplyView();
    }

    private void OnRefreshClick(object sender, RoutedEventArgs e)
    {
        Refresh();
        ShellState.Status("Actualisation demandée.");
    }

    private void OnRowCheckChanged(object sender, RoutedEventArgs e)
    {
        if (sender is CheckBox { Tag: ProcessRow row, IsChecked: bool value }) row.IsChecked = value;
        CloseSelectedButton.IsEnabled = _rows.Any(row => row.IsChecked);
    }

    private void OnRowClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Border { Tag: ProcessRow row }) return;
        _selectedPid = row.Pid;
        foreach (var item in _rows) item.IsSelected = false;
        row.IsSelected = true;
        FillDetail(row);
    }

    private void FillDetail(ProcessRow? row)
    {
        if (row is null)
        {
            DetailPlaceholder.Visibility = Visibility.Visible;
            DetailPanel.Visibility = Visibility.Collapsed;
            return;
        }

        DetailPlaceholder.Visibility = Visibility.Collapsed;
        DetailPanel.Visibility = Visibility.Visible;
        DetailName.Text = row.NameLabel;
        DetailTitle.Text = row.TitleLabel;
        DetailPid.Text = row.PidLabel;
        DetailStart.Text = row.StartTimeLabel;
        DetailPublisher.Text = row.PublisherLabel;
        DetailElevation.Text = row.ElevationLabel;
        DetailPath.Text = row.PathLabel;
    }

    private void OnOpenLocationClick(object sender, RoutedEventArgs e)
    {
        if (_selectedPid is not int pid) return;
        var row = _rows.FirstOrDefault(item => item.Pid == pid);
        if (row is null)
        {
            ShellState.Status("Processus introuvable.");
            return;
        }

        try
        {
            var path = row.Snapshot.Path;
            if (string.IsNullOrWhiteSpace(path))
            {
                ShellState.Status("Chemin inaccessible pour ce processus.");
                return;
            }
            if (File.Exists(path))
            {
                Process.Start(new ProcessStartInfo("explorer.exe", "/select,\"" + path + "\"")
                {
                    UseShellExecute = true
                });
                ShellState.Status("Explorateur ouvert sur " + row.NameLabel + ".");
            }
            else if (Directory.Exists(path))
            {
                Process.Start(new ProcessStartInfo("explorer.exe", "\"" + path + "\"")
                {
                    UseShellExecute = true
                });
            }
            else
            {
                ShellState.Status("Fichier introuvable : " + path);
            }
        }
        catch (Exception ex)
        {
            Log.Error("UI", "Ouverture de l'emplacement du processus", ex);
            ShellState.Status("Ouverture impossible : " + ex.Message);
        }
    }

    private void OnCopyPathClick(object sender, RoutedEventArgs e)
    {
        if (_selectedPid is not int pid) return;
        var row = _rows.FirstOrDefault(item => item.Pid == pid);
        if (row is null) return;
        try
        {
            if (string.IsNullOrWhiteSpace(row.Snapshot.Path))
            {
                ShellState.Status("Chemin inaccessible pour ce processus.");
                return;
            }
            Clipboard.SetText(row.Snapshot.Path);
            ShellState.Status("Chemin copié dans le presse-papiers.");
        }
        catch (Exception ex)
        {
            Log.Error("UI", "Copie du chemin du processus", ex);
            ShellState.Status("Copie impossible : " + ex.Message);
        }
    }

    private void OnSelectHungryClick(object sender, RoutedEventArgs e)
    {
        var count = 0;
        foreach (var row in _rows)
        {
            var select = row.CanClose && row.CpuValue > 10;
            row.IsChecked = select;
            if (select) count++;
        }
        CloseSelectedButton.IsEnabled = count > 0;
        ShellState.Status(count == 0
            ? "Aucun processus fermable n'utilise plus de 10 % du processeur."
            : count + " processus gourmand(s) sélectionné(s).");
    }

    private void OnCloseSelectedClick(object sender, RoutedEventArgs e)
    {
        var selected = _rows.Where(row => row.IsChecked && row.CanClose).ToList();
        if (selected.Count == 0)
        {
            ShellState.Status("Aucun processus sélectionné.");
            return;
        }

        var names = string.Join("\n", selected.Take(15).Select(row => "• " + row.NameLabel + " (" + row.PidLabel + ")"));
        if (selected.Count > 15) names += "\n… et " + (selected.Count - 15) + " autre(s)";

        var answer = MessageBox.Show(
            "Fermer " + selected.Count + " processus ?\n\n" + names +
            "\n\nLes applications non enregistrées peuvent perdre des données non sauvegardées.",
            "Fermer la sélection", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (answer != MessageBoxResult.Yes)
        {
            ShellState.Status("Fermeture annulée.");
            return;
        }

        try
        {
            var pids = selected.Select(row => row.Pid).ToList();
            var (closed, errors) = ProcessService.Instance.CloseMany(pids);
            if (errors.Count > 0)
            {
                MessageBox.Show(
                    "Processus fermés : " + closed + "\n\nÉchecs :\n" + string.Join("\n", errors.Take(15)),
                    "Résultat de la fermeture", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            ShellState.Status("Fermeture terminée : " + closed + " fermé(s), " + errors.Count + " échec(s).");
            foreach (var row in _rows) row.IsChecked = false;
            Refresh();
        }
        catch (Exception ex)
        {
            Log.Error("UI", "Fermeture des processus sélectionnés", ex);
            ShellState.Status("Fermeture impossible : " + ex.Message);
        }
    }

    private void ReloadNeverClose()
    {
        try
        {
            _neverClose.Clear();
            _neverClose.AddRange(ProcessService.Instance.GetNeverCloseList());
            NeverCloseList.ItemsSource = null;
            NeverCloseList.ItemsSource = _neverClose;
        }
        catch (Exception ex)
        {
            Log.Error("UI", "Lecture de la liste « ne jamais fermer »", ex);
            ShellState.Status("Lecture de la liste de protection impossible : " + ex.Message);
        }
    }

    private void OnAddNeverCloseClick(object sender, RoutedEventArgs e)
    {
        var name = (NeverCloseBox.Text ?? string.Empty).Trim();
        if (name.Length == 0)
        {
            ShellState.Status("Saisissez un nom de processus à protéger.");
            return;
        }

        try
        {
            ProcessService.Instance.AddNeverClose(name);
            NeverCloseBox.Text = string.Empty;
            ReloadNeverClose();
            ShellState.Status("« " + name + " » ne sera jamais fermé par GameBoost.");
            Refresh();
        }
        catch (Exception ex)
        {
            Log.Error("UI", "Ajout à la liste de protection", ex);
            ShellState.Status("Ajout impossible : " + ex.Message);
        }
    }

    private void OnRemoveNeverCloseClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string name }) return;
        try
        {
            ProcessService.Instance.RemoveNeverClose(name);
            ReloadNeverClose();
            ShellState.Status("« " + name + " » retiré de la liste de protection.");
            Refresh();
        }
        catch (Exception ex)
        {
            Log.Error("UI", "Retrait de la liste de protection", ex);
            ShellState.Status("Retrait impossible : " + ex.Message);
        }
    }
}
