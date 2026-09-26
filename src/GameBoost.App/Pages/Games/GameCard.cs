using System.ComponentModel;
using System.Runtime.CompilerServices;
using GameBoost.Core.Models;

namespace GameBoost.App.Pages.Games;

public sealed class GameCard : INotifyPropertyChanged
{
    private bool _isSelected;

    public GameCard(GameInfo game)
    {
        Game = game;
    }

    public GameInfo Game { get; }

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value) return;
            _isSelected = value;
            OnPropertyChanged();
        }
    }

    public string PlatformLabel => Game.Platform switch
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

    public string LastLaunchLabel => Game.LastLaunch is DateTime stamp
        ? stamp.ToString("dd/MM/yyyy HH:mm")
        : "Jamais / inconnu";

    public string SizeLabel => string.IsNullOrWhiteSpace(Game.InstallSize) ? "—" : Game.InstallSize;

    public bool CanLaunch => !string.IsNullOrWhiteSpace(Game.ExecutablePath);

    public string LaunchToolTip => CanLaunch
        ? "Lancer « " + Game.Name + " »"
        : "Aucun exécutable connu pour ce jeu : ajoutez-en un manuellement";

    public bool IsRunning => Game.IsRunning;

    public string RunningLabel => Game.IsRunning ? "En cours d'exécution" : string.Empty;

    public string InstallPathLabel => string.IsNullOrWhiteSpace(Game.InstallPath) ? "Dossier d'installation inconnu" : Game.InstallPath;

    public string ExecutableLabel => string.IsNullOrWhiteSpace(Game.ExecutablePath) ? "Exécutable inconnu" : Game.ExecutablePath;

    public void Refresh()
    {
        OnPropertyChanged(nameof(LastLaunchLabel));
        OnPropertyChanged(nameof(SizeLabel));
        OnPropertyChanged(nameof(CanLaunch));
        OnPropertyChanged(nameof(LaunchToolTip));
        OnPropertyChanged(nameof(IsRunning));
        OnPropertyChanged(nameof(RunningLabel));
        OnPropertyChanged(nameof(InstallPathLabel));
        OnPropertyChanged(nameof(ExecutableLabel));
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
