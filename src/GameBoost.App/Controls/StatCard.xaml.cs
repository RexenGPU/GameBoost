using System.Windows;
using System.Windows.Controls;
using GameBoost.Core.Models;

namespace GameBoost.App.Controls;

public partial class StatCard : UserControl
{
    public static readonly DependencyProperty TitleProperty = DependencyProperty.Register(
        nameof(Title), typeof(string), typeof(StatCard), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
        nameof(Value), typeof(string), typeof(StatCard), new PropertyMetadata("—"));

    public static readonly DependencyProperty UnitProperty = DependencyProperty.Register(
        nameof(Unit), typeof(string), typeof(StatCard), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty SubTextProperty = DependencyProperty.Register(
        nameof(SubText), typeof(string), typeof(StatCard), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty IconProperty = DependencyProperty.Register(
        nameof(Icon), typeof(string), typeof(StatCard), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty ProgressPercentProperty = DependencyProperty.Register(
        nameof(ProgressPercent), typeof(double), typeof(StatCard),
        new PropertyMetadata(double.NaN, OnProgressChanged));

    public static readonly DependencyProperty LevelProperty = DependencyProperty.Register(
        nameof(Level), typeof(HealthLevel?), typeof(StatCard),
        new PropertyMetadata(null, OnProgressChanged));

    public StatCard()
    {
        InitializeComponent();
        Loaded += (_, _) => ApplyProgress();
    }

    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public string Value
    {
        get => (string)GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public string Unit
    {
        get => (string)GetValue(UnitProperty);
        set => SetValue(UnitProperty, value);
    }

    public string SubText
    {
        get => (string)GetValue(SubTextProperty);
        set => SetValue(SubTextProperty, value);
    }

    public string Icon
    {
        get => (string)GetValue(IconProperty);
        set => SetValue(IconProperty, value);
    }

    public double ProgressPercent
    {
        get => (double)GetValue(ProgressPercentProperty);
        set => SetValue(ProgressPercentProperty, value);
    }

    public HealthLevel? Level
    {
        get => (HealthLevel?)GetValue(LevelProperty);
        set => SetValue(LevelProperty, value);
    }

    private static void OnProgressChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is StatCard card) card.ApplyProgress();
    }

    private void ApplyProgress()
    {
        if (Bar is null || IconText is null) return;
        IconText.Text = Icon;
        if (double.IsNaN(ProgressPercent))
        {
            Bar.Visibility = Visibility.Collapsed;
            return;
        }
        Bar.Visibility = Visibility.Visible;
        Bar.Value = Math.Clamp(ProgressPercent, 0, 100);
        Bar.Foreground = Level switch
        {
            HealthLevel.Good => TryBrush("GoodBrush"),
            HealthLevel.Warning => TryBrush("WarnBrush"),
            HealthLevel.Critical => TryBrush("DangerBrush"),
            _ => TryBrush("AccentBrush")
        };
    }

    private static System.Windows.Media.Brush TryBrush(string key)
    {
        return Application.Current?.TryFindResource(key) as System.Windows.Media.Brush
               ?? System.Windows.Media.Brushes.Gray;
    }
}
