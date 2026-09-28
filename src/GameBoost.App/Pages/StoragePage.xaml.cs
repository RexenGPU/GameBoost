using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using GameBoost.App.Controls;
using GameBoost.App.Services;
using GameBoost.Core.Disks;
using GameBoost.Core.Localization;
using GameBoost.Core.Logging;
using GameBoost.Core.Models;

namespace GameBoost.App.Pages;

public partial class StoragePage : UserControl
{
    private bool _busy;
    private bool _loaded;
    private List<StorageInfo> _disks = new();

    public StoragePage()
    {
        InitializeComponent();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (!_loaded && !_busy) _ = LoadAsync();
    }

    private async void OnRefreshClick(object sender, RoutedEventArgs e)
    {
        ShellState.Status(Loc.T("Sto_StatusAnalyzing"));
        await LoadAsync();
    }

    private async Task LoadAsync()
    {
        if (_busy) return;
        _busy = true;
        RefreshButton.IsEnabled = false;
        LoadingPanel.Visibility = Visibility.Visible;
        DisksPanel.Visibility = Visibility.Collapsed;
        AlertHost.Children.Clear();
        try
        {
            var disks = await Task.Run(() => DiskAnalyzer.Instance.Analyze());
            _disks = disks ?? new List<StorageInfo>();
            _loaded = true;
            Render();
            ShellState.Status(_disks.Count > 0
                ? Loc.T("Sto_StatusAnalyzed", _disks.Count.ToString(CultureInfo.CurrentCulture))
                : Loc.T("Sto_Empty"));
        }
        catch (Exception ex)
        {
            Log.Error("UI", "Analyse des disques impossible", ex);
            ShellState.Status(Loc.T("Sto_StatusAnalyzeFail", ex.Message));
            _disks = new List<StorageInfo>();
            Render();
        }
        finally
        {
            _busy = false;
            RefreshButton.IsEnabled = true;
            LoadingPanel.Visibility = Visibility.Collapsed;
            DisksPanel.Visibility = Visibility.Visible;
        }
    }

    private void Render()
    {
        try
        {
            DisksPanel.Children.Clear();
            if (_disks.Count == 0)
            {
                DisksEmpty.Visibility = Visibility.Visible;
                DisksPanel.Children.Add(DisksEmpty);
            }
            else
            {
                DisksEmpty.Visibility = Visibility.Collapsed;
                for (var i = 0; i < _disks.Count; i++) DisksPanel.Children.Add(BuildDiskCard(_disks[i], i));
            }

            AlertHost.Children.Clear();
            var alert = BuildAlert();
            if (alert is not null) AlertHost.Children.Add(alert);
        }
        catch (Exception ex)
        {
            Log.Error("UI", "Affichage des disques impossible", ex);
            ShellState.Status(Loc.T("Sto_StatusRenderFail", ex.Message));
        }
    }

    private UIElement? BuildAlert()
    {
        var volume = FindSystemVolume();
        if (volume is null || volume.SizeBytes <= 0) return null;

        var freePercent = Math.Clamp(volume.FreeBytes * 100.0 / volume.SizeBytes, 0, 100);
        if (freePercent >= 20) return null;

        var critical = freePercent < 10;
        var letter = string.IsNullOrWhiteSpace(volume.Letter) ? Loc.T("Sto_SystemVolume") : volume.Letter;

        var card = new Border
        {
            Style = StyleOf("CardFlat"),
            Margin = new Thickness(0, 16, 0, 0),
            Background = critical ? BrushOf("DangerBgBrush") : BrushOf("WarnBgBrush"),
            BorderBrush = critical ? BrushOf("DangerBrush") : BrushOf("WarnBrush")
        };

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        grid.Children.Add(new TextBlock
        {
            Text = "\uE7BA",
            FontFamily = FontOf(),
            FontSize = 17,
            Foreground = critical ? BrushOf("DangerBrush") : BrushOf("WarnBrush"),
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 1, 14, 0)
        });

        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(new TextBlock
        {
            Text = critical
                ? Loc.T("Sto_AlertCritical", letter, freePercent.ToString("F1", CultureInfo.CurrentCulture))
                : Loc.T("Sto_AlertWarning", letter, freePercent.ToString("F1", CultureInfo.CurrentCulture)),
            Style = StyleOf("Body"),
            FontWeight = FontWeights.SemiBold
        });
        text.Children.Add(new TextBlock
        {
            Text = Loc.T("Sto_AlertNote"),
            Style = StyleOf("Caption"),
            Margin = new Thickness(0, 4, 0, 0)
        });
        text.Children.Add(new TextBlock
        {
            Text = critical ? Loc.T("Sto_AlertTipCritical") : Loc.T("Sto_AlertTipWarning"),
            Style = StyleOf("Caption"),
            Margin = new Thickness(0, 3, 0, 0),
            Foreground = BrushOf("TextBrush")
        });
        Grid.SetColumn(text, 1);
        grid.Children.Add(text);

        var button = new Button
        {
            Content = Loc.T("Sto_OpenExplorer"),
            Style = StyleOf("SecondaryButton"),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(16, 0, 0, 0),
            ToolTip = Loc.T("Sto_OpenExplorerTip")
        };
        button.Click += (_, _) => OpenExplorer(VolumePath(volume),
            "Ouverture de l'explorateur sur " + letter + " impossible");
        Grid.SetColumn(button, 2);
        grid.Children.Add(button);

        card.Child = grid;
        return card;
    }

    private VolumeInfo? FindSystemVolume()
    {
        foreach (var disk in _disks)
        {
            if (!disk.IsSystemDisk) continue;
            foreach (var volume in disk.Volumes)
                if (volume.IsSystem) return volume;
            if (disk.Volumes.Count > 0) return disk.Volumes[0];
        }

        foreach (var disk in _disks)
            foreach (var volume in disk.Volumes)
                if (volume.IsSystem) return volume;

        return null;
    }

    private UIElement BuildDiskCard(StorageInfo disk, int index)
    {
        var card = new Border { Style = StyleOf("Card"), Margin = new Thickness(0, 0, 0, 14) };
        var panel = new StackPanel();

        var title = new StackPanel { Orientation = Orientation.Horizontal };
        title.Children.Add(new TextBlock
        {
            Text = "\uE8B7",
            FontFamily = FontOf(),
            FontSize = 15,
            Foreground = BrushOf("AccentBrush"),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 9, 0)
        });
        title.Children.Add(new TextBlock
        {
            Text = Known(disk.Model),
            Style = StyleOf("H3"),
            TextWrapping = TextWrapping.Wrap
        });
        if (disk.IsSystemDisk)
            title.Children.Add(Badge(Loc.T("Sto_SystemDisk"), BrushOf("GoodBgBrush"), BrushOf("GoodBrush"),
                Loc.T("Sto_SystemDiskTip"), new Thickness(10, 0, 0, 0)));
        panel.Children.Add(title);

        panel.Children.Add(new TextBlock
        {
            Text = Loc.T("Sto_DiskHeader", (index + 1).ToString(CultureInfo.CurrentCulture), Known(disk.Manufacturer)),
            Style = StyleOf("Caption"),
            Margin = new Thickness(0, 5, 0, 0)
        });

        var badges = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 11, 0, 0) };
        badges.Children.Add(MediaTypeBadge(disk.MediaType));
        badges.Children.Add(Badge(Known(disk.BusType), BrushOf("CardAltBrush"), BrushOf("TextMutedBrush"),
            Loc.T("Sto_BusTypeTip"), new Thickness(0, 0, 8, 0)));
        panel.Children.Add(badges);

        panel.Children.Add(Row(Loc.T("Sto_Capacity"), disk.SizeBytes > 0 ? FormatSize(disk.SizeBytes) : Loc.T("Hw_NotAvailable"),
            Loc.T("Sto_CapacityTip")));

        if (disk.Volumes.Count == 0)
        {
            panel.Children.Add(Row(Loc.T("Sto_Volumes"), Loc.T("Hw_NotAvailable")));
        }
        else
        {
            foreach (var volume in disk.Volumes) panel.Children.Add(BuildVolumeRow(volume));
        }

        panel.Children.Add(Row(Loc.T("Sto_Temperature"), TemperatureValue(disk.TemperatureC),
            Loc.T("Sto_TemperatureTip")));
        panel.Children.Add(Row(Loc.T("Sto_Health"), HealthValue(disk), HealthTip(disk.HealthPercent)));

        if (disk.SmartWarnings.Count > 0)
        {
            foreach (var warning in disk.SmartWarnings) panel.Children.Add(BuildWarning(warning));
        }

        panel.Children.Add(Row(Loc.T("Sto_Speeds"), SpeedText(disk),
            Loc.T("Sto_SpeedsTip")));

        var root = disk.Volumes.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v.Letter));
        var button = new Button
        {
            Content = Loc.T("Sto_OpenRoot"),
            Style = StyleOf("SecondaryButton"),
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 14, 0, 0)
        };
        if (root is null)
        {
            button.IsEnabled = false;
            button.ToolTip = Loc.T("Sto_OpenRootDisabledTip");
        }
        else
        {
            var path = VolumePath(root);
            button.ToolTip = Loc.T("Sto_OpenRootTip", root.Letter);
            button.Click += (_, _) => OpenExplorer(path, "Ouverture de la racine " + root.Letter + " impossible");
        }
        panel.Children.Add(button);

        card.Child = panel;
        return card;
    }

    private UIElement BuildVolumeRow(VolumeInfo volume)
    {
        var panel = new StackPanel { Margin = new Thickness(0, 13, 0, 0) };

        var head = new Grid();
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var left = new StackPanel { Orientation = Orientation.Horizontal };
        left.Children.Add(new TextBlock
        {
            Text = string.IsNullOrWhiteSpace(volume.Letter) ? Loc.T("Sto_VolumeNoLetter") : volume.Letter,
            Style = StyleOf("Body"),
            FontWeight = FontWeights.SemiBold
        });
        if (!string.IsNullOrWhiteSpace(volume.FileSystem))
            left.Children.Add(new TextBlock
            {
                Text = "· " + Known(volume.FileSystem),
                Style = StyleOf("Caption"),
                Margin = new Thickness(6, 2, 0, 0)
            });
        head.Children.Add(left);

        var right = new TextBlock
        {
            Text = Loc.T("Sto_FreeOfTotal",
                volume.FreeBytes > 0 ? FormatSize(volume.FreeBytes) : "0 " + Loc.T("Unit_B"),
                volume.SizeBytes > 0 ? FormatSize(volume.SizeBytes) : Loc.T("Hw_NotAvailable")),
            Style = StyleOf("Caption"),
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(right, 1);
        head.Children.Add(right);
        panel.Children.Add(head);

        var used = volume.SizeBytes > 0 ? volume.UsedPercent : 0;
        var bar = new ProgressBar
        {
            Style = StyleOf("SlimProgress"),
            Minimum = 0,
            Maximum = 100,
            Value = Math.Clamp(used, 0, 100),
            Margin = new Thickness(0, 6, 0, 0),
            Foreground = used >= 90 ? BrushOf("DangerBrush") : used >= 80 ? BrushOf("WarnBrush") : BrushOf("GoodBrush"),
            ToolTip = Loc.T("Sto_UsedPercent", used.ToString("F1", CultureInfo.CurrentCulture))
        };
        panel.Children.Add(bar);
        return panel;
    }

    private UIElement BuildWarning(string warning)
    {
        var grid = new Grid { Margin = new Thickness(0, 8, 0, 0) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        grid.Children.Add(new TextBlock
        {
            Text = "\uE7BA",
            FontFamily = FontOf(),
            FontSize = 13,
            Foreground = BrushOf("WarnBrush"),
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 1, 8, 0)
        });
        grid.Children.Add(new TextBlock
        {
            Text = warning,
            Style = StyleOf("BodyMuted"),
            TextWrapping = TextWrapping.Wrap
        });
        return grid;
    }

    private static UIElement Row(string label, string value, string? tip = null)
    {
        var grid = new Grid { Margin = new Thickness(0, 9, 0, 0) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        grid.Children.Add(new TextBlock
        {
            Text = label,
            Style = StyleOf("Label"),
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.Wrap
        });
        grid.Children.Add(new TextBlock
        {
            Text = value,
            Style = StyleOf("Body"),
            TextWrapping = TextWrapping.Wrap
        });
        if (tip is not null) grid.ToolTip = tip;
        return grid;
    }

    private static UIElement Row(string label, UIElement value, string? tip = null)
    {
        var grid = new Grid { Margin = new Thickness(0, 9, 0, 0) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        grid.Children.Add(new TextBlock
        {
            Text = label,
            Style = StyleOf("Label"),
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.Wrap
        });

        var host = new Grid { VerticalAlignment = VerticalAlignment.Center };
        host.Children.Add(value);
        Grid.SetColumn(host, 1);
        grid.Children.Add(host);

        if (tip is not null) grid.ToolTip = tip;
        return grid;
    }

    private static UIElement TemperatureValue(double? temperature)
    {
        if (temperature is not double value)
            return new TextBlock { Text = Loc.T("Hw_NotAvailable"), Style = StyleOf("Body") };
        var background = value >= 50 ? BrushOf("DangerBgBrush") : value >= 40 ? BrushOf("WarnBgBrush") : BrushOf("GoodBgBrush");
        var foreground = value >= 50 ? BrushOf("DangerBrush") : value >= 40 ? BrushOf("WarnBrush") : BrushOf("GoodBrush");
        return Badge(value.ToString("F0", CultureInfo.CurrentCulture) + " °C", background, foreground,
            Loc.T("Sto_TempBadgeTip"), new Thickness(0));
    }

    private static UIElement HealthValue(StorageInfo disk)
    {
        var host = new StackPanel { Orientation = Orientation.Horizontal };
        host.Children.Add(new HealthBadge
        {
            Level = HealthFrom(disk.HealthPercent),
            Text = Known(disk.SmartStatus)
        });
        if (disk.HealthPercent is int percent)
            host.Children.Add(new TextBlock
            {
                Text = percent.ToString(CultureInfo.CurrentCulture) + " %",
                Style = StyleOf("Caption"),
                Margin = new Thickness(10, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center
            });
        return host;
    }

    private static HealthLevel HealthFrom(int? percent)
    {
        if (percent is null) return HealthLevel.Unknown;
        if (percent.Value >= 90) return HealthLevel.Good;
        if (percent.Value >= 70) return HealthLevel.Warning;
        return HealthLevel.Critical;
    }

    private static string HealthTip(int? percent) =>
        percent is null
            ? Loc.T("Sto_HealthTipNa")
            : Loc.T("Sto_HealthTipKnown");

    private static string SpeedText(StorageInfo disk)
    {
        var read = disk.ReadSpeedMBs;
        var write = disk.WriteSpeedMBs;
        if (read is null && write is null) return Loc.T("Sto_NotMeasured");

        var parts = new List<string>();
        if (read is double readValue)
            parts.Add(Loc.T("Sto_ReadSpeed", readValue.ToString("F1", CultureInfo.CurrentCulture) + " " + Loc.T("Unit_MBs")));
        if (write is double writeValue)
            parts.Add(Loc.T("Sto_WriteSpeed", writeValue.ToString("F1", CultureInfo.CurrentCulture) + " " + Loc.T("Unit_MBs")));
        return string.Join(" · ", parts);
    }

    private static Border MediaTypeBadge(DiskMediaType mediaType)
    {
        switch (mediaType)
        {
            case DiskMediaType.Ssd:
                return Badge("SSD", BrushOf("GoodBgBrush"), BrushOf("GoodBrush"),
                    Loc.T("Sto_SsdTip"), new Thickness(0, 0, 8, 0));
            case DiskMediaType.Hdd:
                return Badge("HDD", BrushOf("InfoBgBrush"), BrushOf("InfoBrush"),
                    Loc.T("Sto_HddTip"), new Thickness(0, 0, 8, 0));
            default:
                return Badge(Loc.T("Sto_MediaUnknown"), BrushOf("CardAltBrush"), BrushOf("TextMutedBrush"),
                    Loc.T("Sto_MediaUnknownTip"), new Thickness(0, 0, 8, 0));
        }
    }

    private static string VolumePath(VolumeInfo volume)
    {
        var letter = volume.Letter.Trim();
        if (letter.Length == 0) return string.Empty;
        return letter.EndsWith(":", StringComparison.Ordinal) ? letter + "\\" : letter;
    }

    private static void OpenExplorer(string? arguments, string failure)
    {
        try
        {
            var info = new ProcessStartInfo { FileName = "explorer.exe", UseShellExecute = true };
            if (!string.IsNullOrWhiteSpace(arguments)) info.Arguments = arguments;
            Process.Start(info);
            ShellState.Status(Loc.T("Sto_StatusExplorerOpened"));
        }
        catch (Exception ex)
        {
            Log.Error("UI", failure, ex);
            ShellState.Status(Loc.T("Sto_StatusOpenExplorerFail", ex.Message));
        }
    }

    private static Border Badge(string text, Brush background, Brush foreground, string? tip = null,
        Thickness? margin = null)
    {
        var badge = new Border
        {
            Background = background,
            CornerRadius = new CornerRadius(7),
            Padding = new Thickness(9, 3, 9, 3),
            Margin = margin ?? new Thickness(8, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock
            {
                Text = text,
                FontFamily = FontOf(),
                FontSize = 11.5,
                FontWeight = FontWeights.SemiBold,
                Foreground = foreground
            }
        };
        if (tip is not null) badge.ToolTip = tip;
        return badge;
    }

    private static string FormatSize(long bytes) => BytesToSizeConverter.Format(bytes);

    private static string Known(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return Loc.T("Hw_NotAvailable");
        var trimmed = value.Trim();
        if (trimmed.Equals("Inconnu", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Equals("Inconnue", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Equals("Inconnus", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Equals("Inconnues", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Equals("Unknown", StringComparison.OrdinalIgnoreCase))
            return Loc.T("Hw_NotAvailable");
        return trimmed;
    }

    private static Style? StyleOf(string key) => Application.Current?.TryFindResource(key) as Style;

    private static Brush BrushOf(string key) =>
        Application.Current?.TryFindResource(key) as Brush ?? Brushes.Gray;

    private static FontFamily FontOf() =>
        Application.Current?.TryFindResource("AppFont") as FontFamily ?? new FontFamily("Segoe UI");
}
