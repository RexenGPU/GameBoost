using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using GameBoost.Core.Models;

namespace GameBoost.App.Controls;

public partial class HealthBadge : UserControl
{
    public static readonly DependencyProperty LevelProperty = DependencyProperty.Register(
        nameof(Level), typeof(HealthLevel), typeof(HealthBadge),
        new PropertyMetadata(HealthLevel.Unknown, OnChanged));

    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(
        nameof(Text), typeof(string), typeof(HealthBadge),
        new PropertyMetadata(string.Empty, OnChanged));

    public HealthBadge()
    {
        InitializeComponent();
        Loaded += (_, _) => Apply();
    }

    public HealthLevel Level
    {
        get => (HealthLevel)GetValue(LevelProperty);
        set => SetValue(LevelProperty, value);
    }

    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    private static void OnChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is HealthBadge badge) badge.Apply();
    }

    private void Apply()
    {
        if (Dot is null || Label is null) return;
        Dot.Fill = Level switch
        {
            HealthLevel.Good => Brush("#34D399"),
            HealthLevel.Warning => Brush("#FBBF24"),
            HealthLevel.Critical => Brush("#F87171"),
            _ => Brush("#8B98A5")
        };
        Label.Text = string.IsNullOrWhiteSpace(Text)
            ? Level switch
            {
                HealthLevel.Good => "Optimal",
                HealthLevel.Warning => "À surveiller",
                HealthLevel.Critical => "Problème détecté",
                _ => "Indisponible"
            }
            : Text;
    }

    private static SolidColorBrush Brush(string hex)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        brush.Freeze();
        return brush;
    }
}
