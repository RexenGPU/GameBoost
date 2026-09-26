using System.Windows;
using System.Windows.Threading;
using GameBoost.App.Services;
using GameBoost.Core.Games;
using GameBoost.Core.Logging;
using GameBoost.Core.Models;
using GameBoost.Core.Overlay;
using GameBoost.Core.Processes;

namespace GameBoost.App.Overlay;

public sealed class OverlayController
{
    private static readonly TimeSpan DetectionInterval = TimeSpan.FromSeconds(3);

    public static OverlayController Instance { get; } = new();

    private readonly object _sync = new();
    private OverlayWindow? _window;
    private DispatcherTimer? _timer;
    private GameInfo? _active;
    private bool _explicitRequest;
    private int _detecting;

    public bool IsRunning { get; private set; }

    public event Action<bool>? VisibilityChanged;

    private OverlayController()
    {
    }

    public void Start(GameInfo? game = null)
    {
        try
        {
            if (game is null)
            {
                StartDetection();
                return;
            }
            RunOnDispatcher(() => ShowForGame(game, true));
        }
        catch (Exception ex)
        {
            Fail("demarrage", ex);
        }
    }

    public void Stop()
    {
        try
        {
            RunOnDispatcher(StopCore);
        }
        catch (Exception ex)
        {
            Fail("arret", ex);
        }
    }

    public void Toggle(GameInfo? game = null)
    {
        try
        {
            if (IsRunning)
            {
                RunOnDispatcher(StopCore);
                return;
            }
            if (game is null)
            {
                StartDetection();
                return;
            }
            RunOnDispatcher(() => ShowForGame(game, true));
        }
        catch (Exception ex)
        {
            Fail("bascule", ex);
        }
    }

    public void RefreshSettings(GameInfo? game = null)
    {
        try
        {
            RunOnDispatcher(() => RefreshCore(game));
        }
        catch (Exception ex)
        {
            Fail("rafraichissement", ex);
        }
    }

    private void ShowForGame(GameInfo game, bool explicitRequest)
    {
        try
        {
            if (_window is null) _window = new OverlayWindow();
            var settings = OverlayService.Instance.GetForGame(game.Id);
            _window.ApplySettings(settings);
            _window.ShowOverlay();
            _active = game;
            if (explicitRequest) _explicitRequest = true;
            SetRunning(true);
        }
        catch (Exception ex)
        {
            Fail("affichage", ex);
        }
    }

    private void StopCore()
    {
        try
        {
            StopDetection();
            _window?.HideOverlay();
            _active = null;
            _explicitRequest = false;
            SetRunning(false);
        }
        catch (Exception ex)
        {
            Fail("arret", ex);
        }
    }

    private void RefreshCore(GameInfo? game)
    {
        try
        {
            if (_window is null) return;
            var target = game ?? _active;
            var settings = target is null
                ? OverlayService.Instance.GetDefault()
                : OverlayService.Instance.GetForGame(target.Id);
            _window.ApplySettings(settings);
        }
        catch (Exception ex)
        {
            Fail("rafraichissement", ex);
        }
    }

    private void StartDetection()
    {
        RunOnDispatcher(() =>
        {
            try
            {
                if (_timer is null)
                {
                    _timer = new DispatcherTimer(DispatcherPriority.Background)
                    {
                        Interval = DetectionInterval
                    };
                    _timer.Tick += OnDetectionTick;
                }
                if (!_timer.IsEnabled) _timer.Start();
                _ = DetectAsync();
            }
            catch (Exception ex)
            {
                Fail("demarrage de la surveillance", ex);
            }
        });
    }

    private void StopDetection()
    {
        if (_timer is null) return;
        _timer.Stop();
        _timer.Tick -= OnDetectionTick;
        _timer = null;
    }

    private void OnDetectionTick(object? sender, EventArgs e)
    {
        _ = DetectAsync();
    }

    private async Task DetectAsync()
    {
        if (Interlocked.Exchange(ref _detecting, 1) == 1) return;
        try
        {
            var found = await Task.Run(FindOverlayGame);
            RunOnDispatcher(() =>
            {
                if (found is null)
                {
                    if (!_explicitRequest) HideFromDetection();
                    return;
                }
                if (IsRunning && _active is not null && _active.Id == found.Id) return;
                ShowForGame(found, false);
            });
        }
        catch (Exception ex)
        {
            Fail("surveillance des jeux", ex);
        }
        finally
        {
            Interlocked.Exchange(ref _detecting, 0);
        }
    }

    private static GameInfo? FindOverlayGame()
    {
        List<GameInfo> library;
        try
        {
            library = GameScanner.Instance.GetLibrary();
        }
        catch (Exception ex)
        {
            Log.Warn("Overlay", "Bibliotheque de jeux illisible : " + ex.Message);
            return null;
        }

        foreach (var game in library)
        {
            try
            {
                var running = game.IsRunning || ProcessService.Instance.FindGameProcessId(game) is > 0;
                if (!running) continue;
                if (!OverlayService.Instance.GetForGame(game.Id).Enabled) continue;
                return game;
            }
            catch (Exception ex)
            {
                Log.Warn("Overlay", "Jeu ignore pendant la surveillance : " + ex.Message);
            }
        }
        return null;
    }

    private void HideFromDetection()
    {
        try
        {
            if (_window is null || !IsRunning) return;
            _window.HideOverlay();
            _active = null;
            SetRunning(false);
        }
        catch (Exception ex)
        {
            Fail("masquage", ex);
        }
    }

    private void SetRunning(bool value)
    {
        if (IsRunning == value) return;
        IsRunning = value;
        try
        {
            VisibilityChanged?.Invoke(value);
        }
        catch (Exception ex)
        {
            Log.Warn("Overlay", "Un abonne a refuse la notification de visibilite : " + ex.Message);
        }
    }

    private static void RunOnDispatcher(Action action)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
        {
            action();
            return;
        }
        dispatcher.Invoke(action);
    }

    private static void Fail(string context, Exception ex)
    {
        Log.Error("Overlay", "Echec de " + context, ex);
        ShellState.Status("Overlay impossible (" + context + ") : " + ex.Message);
    }
}
