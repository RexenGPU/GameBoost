using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using GameBoost.Core.Data;
using GameBoost.Core.Models;

namespace GameBoost.App.Services;

public sealed class ShellState : INotifyPropertyChanged
{
    public static ShellState Instance { get; } = new();

    private bool _labelsVisible = true;
    private string _statusMessage = "";
    private bool _isElevated;

    public bool LabelsVisible
    {
        get => _labelsVisible;
        set => Set(ref _labelsVisible, value);
    }

    public string StatusMessage
    {
        get => _statusMessage;
        set => Set(ref _statusMessage, value);
    }

    public bool IsElevated
    {
        get => _isElevated;
        set => Set(ref _isElevated, value);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public static void Status(string message)
    {
        Instance.StatusMessage = DateTime.Now.ToString("HH:mm:ss") + " — " + message;
    }
}

public static class NavigationService
{
    public static string CurrentKey { get; private set; } = "dashboard";

    public static event Action<string>? Navigated;

    public static void Navigate(string key)
    {
        if (string.Equals(key, CurrentKey, StringComparison.OrdinalIgnoreCase)) return;
        CurrentKey = key;
        Navigated?.Invoke(key);
    }
}

public static class ThemeService
{
    public static event Action<string>? ThemeChanged;

    public static string CurrentTheme => SettingsService.Current.Theme;

    public static void Apply(string theme)
    {
        if (Application.Current is null) return;
        var dictionaries = Application.Current.Resources.MergedDictionaries;
        for (var i = 0; i < dictionaries.Count; i++)
        {
            var source = dictionaries[i].Source.ToString();
            if (source.EndsWith("Dark.xaml", StringComparison.OrdinalIgnoreCase) ||
                source.EndsWith("Light.xaml", StringComparison.OrdinalIgnoreCase))
            {
                var target = theme.Equals("Light", StringComparison.OrdinalIgnoreCase)
                    ? "pack://application:,,,/Themes/Light.xaml"
                    : "pack://application:,,,/Themes/Dark.xaml";
                dictionaries[i] = new ResourceDictionary { Source = new Uri(target, UriKind.Absolute) };
                break;
            }
        }
        try
        {
            SettingsService.Update(s => s.Theme = theme);
        }
        catch
        {
        }
        ThemeChanged?.Invoke(theme);
    }

    public static void Toggle()
    {
        Apply(CurrentTheme.Equals("Light", StringComparison.OrdinalIgnoreCase) ? "Dark" : "Light");
    }
}
