using System.ComponentModel;
using System.Runtime.CompilerServices;
using GameBoost.Core.Localization;
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
        GamePlatform.Manual => Loc.T("Games_PlatformManual"),
        _ => Loc.T("Games_PlatformOther")
    };

    public string LastLaunchLabel => Game.LastLaunch is DateTime stamp
        ? stamp.ToString("dd/MM/yyyy HH:mm")
        : Loc.T("Games_Never");

    public string SizeLabel => string.IsNullOrWhiteSpace(Game.InstallSize) ? "—" : Game.InstallSize;

    public bool CanLaunch => !string.IsNullOrWhiteSpace(Game.ExecutablePath);

    public string LaunchToolTip => CanLaunch
        ? Loc.T("Games_LaunchTip", Game.Name)
        : Loc.T("Games_NoExeTip");

    public bool IsRunning => Game.IsRunning;

    public string RunningLabel => Game.IsRunning ? Loc.T("Games_RunningBadge") : string.Empty;

    public string InstallPathLabel => string.IsNullOrWhiteSpace(Game.InstallPath)
        ? Loc.T("Games_UnknownInstallDir")
        : Game.InstallPath;

    public string ExecutableLabel => string.IsNullOrWhiteSpace(Game.ExecutablePath)
        ? Loc.T("Games_UnknownExe")
        : Game.ExecutablePath;

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
