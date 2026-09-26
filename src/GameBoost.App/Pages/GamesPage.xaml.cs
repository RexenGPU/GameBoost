using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using GameBoost.App.Pages.Games;
using GameBoost.App.Services;
using GameBoost.Core.Games;
using GameBoost.Core.Logging;
using GameBoost.Core.Models;
using GameBoost.Core.Overlay;
using Microsoft.Win32;

namespace GameBoost.App.Pages;

public partial class GamesPage : UserControl
{
    private readonly List<GameInfo> _games = new();
    private readonly List<GameCard> _cards = new();
    private GameCard? _selected;
    private bool _loaded;
    private bool _busy;
    private bool _loadingDetail;

    public GamesPage()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_loaded) return;
        _loaded = true;
        LoadLibrary();
    }

    private async void LoadLibrary()
    {
        if (_busy) return;
        SetBusy(true, "Lecture de la bibliothèque de jeux…");
        try
        {
            var games = await Task.Run(() =>
            {
                var found = GameScanner.Instance.GetLibrary();
                foreach (var game in found)
                {
                    if (string.IsNullOrWhiteSpace(game.IconPath) && !string.IsNullOrWhiteSpace(game.ExecutablePath))
                        game.IconPath = GameIconCache.GetIconPath(game.ExecutablePath);
                }
                return found;
            });

            _games.Clear();
            _games.AddRange(games);
            BuildPlatformFilter();
            ApplyFilter();
            ShellState.Status(_games.Count == 0
                ? "Aucun jeu enregistré : lancez un scan."
                : _games.Count + " jeu(s) dans la bibliothèque.");
        }
        catch (Exception ex)
        {
            Log.Error("UI", "Lecture de la bibliothèque de jeux", ex);
            ShellState.Status("Lecture de la bibliothèque impossible : " + ex.Message);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async void OnScanClick(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        SetBusy(true, "Analyse des plateformes en cours (environ 12 secondes)…");
        try
        {
            var games = await Task.Run(() =>
            {
                var found = GameScanner.Instance.Scan();
                foreach (var game in found)
                {
                    if (string.IsNullOrWhiteSpace(game.IconPath) && !string.IsNullOrWhiteSpace(game.ExecutablePath))
                        game.IconPath = GameIconCache.GetIconPath(game.ExecutablePath);
                }
                return found;
            });

            _games.Clear();
            _games.AddRange(games);
            BuildPlatformFilter();
            ApplyFilter();
            ShellState.Status("Analyse terminée : " + _games.Count + " jeu(s) détecté(s).");
        }
        catch (Exception ex)
        {
            Log.Error("UI", "Analyse des jeux", ex);
            ShellState.Status("Analyse impossible : " + ex.Message);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void OnAddExeClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var dialog = new OpenFileDialog
            {
                Title = "Choisir l'exécutable du jeu",
                Filter = "Application (*.exe)|*.exe"
            };
            if (dialog.ShowDialog() != true) return;

            var game = GameScanner.Instance.AddManualExecutable(dialog.FileName);
            if (game is null)
            {
                MessageBox.Show(
                    "Ce fichier n'a pas pu être ajouté.\n\nVérifiez qu'il s'agit bien d'un fichier .exe existant sur ce PC.",
                    "Ajout impossible", MessageBoxButton.OK, MessageBoxImage.Warning);
                ShellState.Status("Ajout refusé : exécutable non valide.");
                return;
            }

            _games.RemoveAll(g => string.Equals(g.Id, game.Id, StringComparison.Ordinal));
            if (string.IsNullOrWhiteSpace(game.IconPath)) game.IconPath = GameIconCache.GetIconPath(game.ExecutablePath);
            _games.Add(game);
            ApplyFilter();
            SelectCard(_cards.FirstOrDefault(c => string.Equals(c.Game.Id, game.Id, StringComparison.Ordinal)));
            ShellState.Status("Jeu ajouté : " + game.Name);
        }
        catch (Exception ex)
        {
            Log.Error("UI", "Ajout manuel d'un exécutable", ex);
            ShellState.Status("Ajout impossible : " + ex.Message);
        }
    }

    private void OnSearchChanged(object sender, TextChangedEventArgs e)
    {
        if (!IsLoaded) return;
        ApplyFilter();
    }

    private void OnPlatformChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded) return;
        ApplyFilter();
    }

    private void BuildPlatformFilter()
    {
        var current = PlatformFilter.SelectedItem as string;
        var labels = new List<string> { "Toutes" };
        foreach (var platform in _games.Select(g => g.Platform).Distinct().OrderBy(p => p))
        {
            labels.Add(PlatformLabel(platform));
        }
        PlatformFilter.ItemsSource = labels;
        PlatformFilter.SelectedItem = current is not null && labels.Contains(current) ? current : "Toutes";
    }

    private static string PlatformLabel(GamePlatform platform) => platform switch
    {
        GamePlatform.Steam => "Steam",
        GamePlatform.Epic => "Epic Games",
        GamePlatform.Ubisoft => "Ubisoft Connect",
        GamePlatform.Xbox => "Xbox",
        GamePlatform.Gog => "GOG",
        GamePlatform.BattleNet => "Battle.net",
        GamePlatform.Riot => "Riot",
        GamePlatform.Manual => "Ajouté manuellement",
        _ => "Autre"
    };

    private void ApplyFilter()
    {
        var query = (SearchBox.Text ?? string.Empty).Trim();
        var platform = PlatformFilter.SelectedItem as string ?? "Toutes";

        _cards.Clear();
        foreach (var game in _games)
        {
            if (platform != "Toutes" && !string.Equals(PlatformLabel(game.Platform), platform, StringComparison.Ordinal))
                continue;
            if (query.Length > 0)
            {
                var inName = game.Name.Contains(query, StringComparison.CurrentCultureIgnoreCase);
                var inPath = (game.InstallPath ?? string.Empty).Contains(query, StringComparison.CurrentCultureIgnoreCase);
                if (!inName && !inPath) continue;
            }
            _cards.Add(new GameCard(game));
        }

        GamesList.ItemsSource = _cards;
        EmptyState.Visibility = _cards.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        ResultCount.Text = _cards.Count <= 1 ? _cards.Count + " jeu" : _cards.Count + " jeux";

        if (_selected is not null)
        {
            var match = _cards.FirstOrDefault(c => string.Equals(c.Game.Id, _selected.Game.Id, StringComparison.Ordinal));
            if (match is not null)
            {
                _selected = match;
                match.IsSelected = true;
                RefreshDetail();
            }
            else
            {
                SelectCard(null);
            }
        }
    }

    private void OnCardClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is Border { Tag: GameCard card }) SelectCard(card);
    }

    private void SelectCard(GameCard? card)
    {
        foreach (var item in _cards) item.IsSelected = false;
        _selected = card;
        if (card is not null) card.IsSelected = true;

        DetailPlaceholder.Visibility = card is null ? Visibility.Visible : Visibility.Collapsed;
        DetailPanel.Visibility = card is null ? Visibility.Collapsed : Visibility.Visible;
        if (card is null) return;
        RefreshDetail();
    }

    private void RefreshDetail()
    {
        if (_selected is null) return;
        var game = _selected.Game;
        DetailPanel.DataContext = _selected;
        ConfigText.Text = string.IsNullOrWhiteSpace(game.KnownSettingsSummary)
            ? "Aucune information connue pour ce jeu : GameBoost ne modifie rien tant qu'aucun fichier de configuration reconnu n'a été trouvé."
            : game.KnownSettingsSummary;
        RunningBadge.Visibility = game.IsRunning ? Visibility.Visible : Visibility.Collapsed;

        _loadingDetail = true;
        try
        {
            var settings = OverlayService.Instance.GetForGame(game.Id);
            OverlayToggle.IsChecked = settings.Enabled;
            OverlayLabel.Text = settings.Enabled ? "Overlay activé" : "Overlay désactivé";
            ArgsBox.Text = game.LaunchArguments ?? string.Empty;
        }
        catch (Exception ex)
        {
            Log.Error("UI", "Lecture du détail du jeu", ex);
            OverlayToggle.IsChecked = false;
            OverlayLabel.Text = "Non disponible";
            ArgsBox.Text = game.LaunchArguments ?? string.Empty;
        }
        finally
        {
            _loadingDetail = false;
        }
    }

    private void OnOverlayChanged(object sender, RoutedEventArgs e)
    {
        if (_loadingDetail || _selected is null) return;
        try
        {
            var game = _selected.Game;
            var enabled = OverlayToggle.IsChecked == true;
            var settings = OverlayService.Instance.GetForGame(game.Id);
            settings.Enabled = enabled;
            OverlayService.Instance.SaveForGame(game.Id, settings);
            game.OverlayEnabled = enabled;
            GameScanner.Instance.UpdateGame(game);
            OverlayLabel.Text = enabled ? "Overlay activé" : "Overlay désactivé";
            ShellState.Status(enabled
                ? "Overlay FPS activé pour « " + game.Name + " »."
                : "Overlay FPS désactivé pour « " + game.Name + " ».");
        }
        catch (Exception ex)
        {
            Log.Error("UI", "Enregistrement de l'overlay du jeu", ex);
            ShellState.Status("Enregistrement de l'overlay impossible : " + ex.Message);
        }
    }

    private void OnSaveGameClick(object sender, RoutedEventArgs e)
    {
        if (_selected is null) return;
        try
        {
            var game = _selected.Game;
            game.LaunchArguments = ArgsBox.Text ?? string.Empty;
            GameScanner.Instance.UpdateGame(game);
            _selected.Refresh();
            ShellState.Status("Jeu enregistré : " + game.Name);
        }
        catch (Exception ex)
        {
            Log.Error("UI", "Enregistrement du jeu", ex);
            ShellState.Status("Enregistrement impossible : " + ex.Message);
        }
    }

    private void OnLaunchClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: GameInfo game }) return;
        try
        {
            if (string.IsNullOrWhiteSpace(game.ExecutablePath) || !File.Exists(game.ExecutablePath))
            {
                ShellState.Status("Aucun exécutable lisible pour « " + game.Name + " ».");
                return;
            }

            var startInfo = new ProcessStartInfo(game.ExecutablePath) { UseShellExecute = true };
            var directory = Path.GetDirectoryName(game.ExecutablePath);
            if (!string.IsNullOrWhiteSpace(directory)) startInfo.WorkingDirectory = directory;
            if (!string.IsNullOrWhiteSpace(game.LaunchArguments)) startInfo.Arguments = game.LaunchArguments;

            Process.Start(startInfo);
            game.LastLaunch = DateTime.Now;
            GameScanner.Instance.UpdateGame(game);
            ApplyFilter();
            ShellState.Status("Lancement de « " + game.Name + " ».");
        }
        catch (Exception ex)
        {
            Log.Error("UI", "Lancement du jeu " + game.Name, ex);
            ShellState.Status("Lancement impossible : " + ex.Message);
        }
    }

    private void OnProfilesClick(object sender, RoutedEventArgs e)
    {
        NavigationService.Navigate("profiles");
        ShellState.Status("Page des profils graphiques.");
    }

    private void OnOpenFolderClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: GameInfo game }) return;
        try
        {
            var target = !string.IsNullOrWhiteSpace(game.ExecutablePath) && File.Exists(game.ExecutablePath)
                ? game.ExecutablePath
                : game.InstallPath;

            if (string.IsNullOrWhiteSpace(target) || (!File.Exists(target) && !Directory.Exists(target)))
            {
                ShellState.Status("Dossier introuvable pour « " + game.Name + " ».");
                return;
            }

            if (File.Exists(target))
            {
                Process.Start(new ProcessStartInfo("explorer.exe", "/select,\"" + target + "\"")
                {
                    UseShellExecute = true
                });
            }
            else
            {
                Process.Start(new ProcessStartInfo("explorer.exe", "\"" + target + "\"")
                {
                    UseShellExecute = true
                });
            }
            ShellState.Status("Explorateur ouvert sur « " + game.Name + " ».");
        }
        catch (Exception ex)
        {
            Log.Error("UI", "Ouverture du dossier du jeu", ex);
            ShellState.Status("Ouverture du dossier impossible : " + ex.Message);
        }
    }

    private void OnRemoveClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: GameInfo game }) return;
        var answer = MessageBox.Show(
            "Retirer « " + game.Name + " » de GameBoost ?\n\nSeule l'entrée de la bibliothèque est supprimée : aucun fichier du jeu n'est touché.",
            "Retirer le jeu", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (answer != MessageBoxResult.Yes)
        {
            ShellState.Status("Retrait annulé.");
            return;
        }

        try
        {
            var removed = GameScanner.Instance.RemoveGame(game.Id);
            if (!removed)
            {
                ShellState.Status("Retrait impossible pour « " + game.Name + " ».");
                return;
            }
            _games.RemoveAll(g => string.Equals(g.Id, game.Id, StringComparison.Ordinal));
            if (_selected is not null && string.Equals(_selected.Game.Id, game.Id, StringComparison.Ordinal))
                SelectCard(null);
            ApplyFilter();
            ShellState.Status("« " + game.Name + " » retiré de la bibliothèque.");
        }
        catch (Exception ex)
        {
            Log.Error("UI", "Retrait du jeu", ex);
            ShellState.Status("Retrait impossible : " + ex.Message);
        }
    }

    private void SetBusy(bool busy, string? status = null)
    {
        _busy = busy;
        ScanProgress.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        ScanButton.IsEnabled = !busy;
        AddButton.IsEnabled = !busy;
        if (busy && !string.IsNullOrWhiteSpace(status)) ShellState.Status(status);
    }
}
