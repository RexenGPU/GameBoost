using System.Windows;
using System.Windows.Threading;
using GameBoost.App.Services;
using GameBoost.Core.Data;
using GameBoost.Core.Localization;
using GameBoost.Core.Logging;
using GameBoost.Core.Monitoring;
using GameBoost.Core.Optimization;

namespace GameBoost.App;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        AppPaths.Ensure();
        Loc.RegisterAssembly(typeof(GameBoost.Core.Logging.Log).Assembly);
        Loc.RegisterAssembly(typeof(App).Assembly);
        Loc.SetCulture(SettingsService.Current.Language);
        Log.Info("App", "Langue demandee : " + SettingsService.Current.Language + " -> culture active : " + Loc.CultureCode);
        Log.Init(AppPaths.LogsDir, SettingsService.Current.LogRetentionDays);
        Log.Info("App", "Démarrage de GameBoost " + AssemblyVersion());

        ThemeService.Apply(SettingsService.Current.Theme);
        ShellState.Instance.IsElevated = AppPaths.IsElevated;

        DispatcherUnhandledException += OnDispatcherException;
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

        try
        {
            Db.Open();
        }
        catch (Exception ex)
        {
            Log.Error("App", "Base locale indisponible", ex);
        }

        try
        {
            SystemMonitor.Instance.Start(1000);
        }
        catch (Exception ex)
        {
            Log.Error("App", "Démarrage du monitoring impossible", ex);
        }

        System.Threading.Tasks.Task.Run(() =>
        {
            try
            {
                GameBoost.Core.Hardware.HardwareSensors.Instance.Start();
            }
            catch (Exception ex)
            {
                Log.Warn("App", "Capteurs matériels : " + ex.Message);
            }
        });

        var window = new MainWindow();
        MainWindow = window;
        window.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try
        {
            var boost = BoostService.Instance;
            if (boost.ActiveSession is not null)
            {
                Log.Info("App", "Fin de session Boost avant fermeture");
                boost.EndSessionAsync().Wait(TimeSpan.FromSeconds(8));
            }
        }
        catch (Exception ex)
        {
            Log.Warn("App", "Fin de session Boost : " + ex.Message);
        }

        try
        {
            SystemMonitor.Instance.Stop();
        }
        catch
        {
        }

        try
        {
            Db.Close();
        }
        catch
        {
        }

        Log.Info("App", "Arrêt de GameBoost");
        base.OnExit(e);
    }

    private void OnDispatcherException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Log.Error("App", "Exception non gérée (interface)", e.Exception);
        try
        {
            MessageBox.Show(
                Loc.T("App_ErrorBody", e.Exception.Message),
                Loc.T("App_ErrorTitle"), MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        catch
        {
        }
        e.Handled = true;
    }

    private void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception ex) Log.Fatal("App", "Exception fatale", ex);
    }

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        Log.Error("App", "Tâche asynchrone non observée", e.Exception);
        e.SetObserved();
    }

    private static string AssemblyVersion()
    {
        var v = typeof(App).Assembly.GetName().Version;
        return v is null ? "1.0.0" : $"{v.Major}.{v.Minor}.{v.Build}";
    }
}
