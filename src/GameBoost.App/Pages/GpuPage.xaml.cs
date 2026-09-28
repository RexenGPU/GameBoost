using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using GameBoost.App.Controls;
using GameBoost.App.Services;
using GameBoost.Core.Hardware;
using GameBoost.Core.Localization;
using GameBoost.Core.Logging;
using GameBoost.Core.Models;
using GameBoost.Core.Monitoring;

namespace GameBoost.App.Pages;

public partial class GpuPage : UserControl
{
    private static string DriverTip => Loc.T("Gpu_DriverAgeTip");

    private bool _subscribed;
    private bool _busy;
    private bool _loaded;
    private long _dedicatedVram;
    private double? _temperature;
    private double? _hotspot;
    private double? _clock;
    private double? _fan;
    private double? _load;
    private long _vramUsed;
    private long _vramTotal;

    public GpuPage()
    {
        InitializeComponent();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (!_subscribed)
        {
            SystemMonitor.Instance.SampleUpdated += OnSampleUpdated;
            _subscribed = true;
        }

        try
        {
            if (!SystemMonitor.Instance.IsRunning) SystemMonitor.Instance.Start(1000);
            ApplySample(SystemMonitor.Instance.Current);
        }
        catch (Exception ex)
        {
            Log.Error("UI", "Démarrage des mesures GPU impossible", ex);
            ShellState.Status(Loc.T("Gpu_StatusLiveFail", ex.Message));
        }

        ApplyColumns();
        _ = ReadSensorsAsync();
        if (!_loaded && !_busy) _ = LoadHardwareAsync();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (!_subscribed) return;
        SystemMonitor.Instance.SampleUpdated -= OnSampleUpdated;
        _subscribed = false;
    }

    private void OnSizeChanged(object sender, SizeChangedEventArgs e) => ApplyColumns();

    private void ApplyColumns()
    {
        if (LiveGrid is null || VramGrid is null) return;
        LiveGrid.Columns = ActualWidth >= 1180 ? 4 : ActualWidth >= 760 ? 2 : 1;
        VramGrid.Columns = ActualWidth >= 760 ? 2 : 1;
    }

    private async void OnRefreshClick(object sender, RoutedEventArgs e)
    {
        ShellState.Status(Loc.T("Gpu_StatusDetecting"));
        await LoadHardwareAsync();
    }

    private async Task LoadHardwareAsync()
    {
        if (_busy) return;
        _busy = true;
        RefreshButton.IsEnabled = false;
        LoadingPanel.Visibility = Visibility.Visible;
        try
        {
            var report = await HardwareDetector.Instance.CollectAsync();
            Fill(report);
            _loaded = true;
            ShellState.Status(Loc.T("Gpu_StatusDetected", Known(report.Gpu.Name)));
        }
        catch (Exception ex)
        {
            Log.Error("UI", "Détection de la carte graphique impossible", ex);
            ShellState.Status(Loc.T("Gpu_StatusDetectFail", ex.Message));
            Fill(new HardwareReport());
            _loaded = true;
        }
        finally
        {
            _busy = false;
            RefreshButton.IsEnabled = true;
            LoadingPanel.Visibility = Visibility.Collapsed;
        }
    }

    private void Fill(HardwareReport report)
    {
        try
        {
            var gpu = report.Gpu;
            _dedicatedVram = gpu.DedicatedVramBytes;
            var known = Known(gpu.Name) != Loc.T("Hw_NotAvailable");
            EmptyPanel.Visibility = known ? Visibility.Collapsed : Visibility.Visible;
            ContentPanel.Visibility = known ? Visibility.Visible : Visibility.Collapsed;
            if (!known)
            {
                RenderLive();
                return;
            }

            GpuNameText.Text = Known(gpu.Name);
            VendorHost.Children.Clear();
            VendorHost.Children.Add(VendorBadge(gpu.Vendor));

            BannerFields.Children.Clear();
            var driverPanel = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            driverPanel.Children.Add(new TextBlock
            {
                Text = Known(gpu.DriverVersion),
                Style = StyleOf("Body")
            });
            driverPanel.Children.Add(DriverAgeBadge(gpu.DriverDate));
            BannerFields.Children.Add(Row(Loc.T("Gpu_Driver"), driverPanel,
                Loc.T("Gpu_DriverTip")));
            BannerFields.Children.Add(Row(Loc.T("Gpu_DriverDate"), Known(gpu.DriverDate)));
            BannerFields.Children.Add(Row(Loc.T("Gpu_DedicatedVram"), _dedicatedVram > 0 ? FormatSize(_dedicatedVram) : Loc.T("Hw_NotAvailable"),
                Loc.T("Gpu_DedicatedVramTip")));
            BannerFields.Children.Add(Row(Loc.T("Gpu_PnpId"), Known(gpu.PnpDeviceId),
                Loc.T("Gpu_PnpIdTip"), true));

            FillCapabilities(gpu.Capabilities);
            FillAdapters(gpu.AllAdapters, gpu);
            RenderLive();
        }
        catch (Exception ex)
        {
            Log.Error("UI", "Affichage de la fiche GPU impossible", ex);
            ShellState.Status(Loc.T("Gpu_StatusRenderFail", ex.Message));
        }
    }

    private void FillCapabilities(GpuCapabilities capabilities)
    {
        TechPanel.Children.Clear();
        var missing = new List<string>();

        AddCapability("DLSS", Loc.T("Gpu_TechDlss"), capabilities.Dlss, "DLSS", missing);
        AddCapability("Frame Generation", Loc.T("Gpu_TechFrameGen"), capabilities.FrameGeneration, "Frame Generation", missing);
        AddCapability("Ray Tracing", Loc.T("Gpu_TechRayTracing"), capabilities.RayTracing, "Ray Tracing", missing);
        AddCapability("Reflex", Loc.T("Gpu_TechReflex"), capabilities.Reflex, "Reflex", missing);
        AddCapability("FSR", Loc.T("Gpu_TechFsr"), capabilities.Fsr, "FSR", missing);
        AddCapability("XeSS", Loc.T("Gpu_TechXess"), capabilities.Xess, "XeSS", missing);

        if (missing.Count > 0)
        {
            TechUnavailable.Text = Loc.T("Gpu_TechUnavailable", string.Join(", ", missing));
            TechUnavailable.Visibility = Visibility.Visible;
        }
        else
        {
            TechUnavailable.Visibility = Visibility.Collapsed;
        }

        var notes = (capabilities.Notes ?? string.Empty).Trim();
        TechNotes.Text = notes;
        TechNotes.Visibility = notes.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void AddCapability(string name, string description, bool supported, string label, List<string> missing)
    {
        if (!supported)
        {
            missing.Add(label);
            return;
        }

        var card = new Border
        {
            Style = StyleOf("CardFlat"),
            Padding = new Thickness(13, 11, 13, 11),
            Margin = new Thickness(0, 0, 10, 10),
            MinWidth = 220,
            MaxWidth = 360,
            ToolTip = description
        };
        var panel = new StackPanel();
        panel.Children.Add(new TextBlock
        {
            Text = name,
            Style = StyleOf("Body"),
            FontWeight = FontWeights.SemiBold
        });
        panel.Children.Add(Badge(Loc.T("Gpu_Compatible"),
            BrushOf("GoodBgBrush"), BrushOf("GoodBrush"),
            Loc.T("Gpu_CompatibleTip"), new Thickness(0, 7, 0, 0)));
        card.Child = panel;
        TechPanel.Children.Add(card);
    }

    private void FillAdapters(List<GpuInfo> adapters, GpuInfo primary)
    {
        AdaptersList.Children.Clear();
        var list = adapters ?? new List<GpuInfo>();
        if (list.Count == 0)
        {
            AdaptersList.Children.Add(new TextBlock { Text = Loc.T("Hw_NotAvailable"), Style = StyleOf("BodyMuted") });
            return;
        }

        foreach (var adapter in list)
        {
            var card = new Border
            {
                Style = StyleOf("CardFlat"),
                Padding = new Thickness(14),
                Margin = new Thickness(0, 0, 0, 9)
            };
            var panel = new StackPanel();

            var title = new StackPanel { Orientation = Orientation.Horizontal };
            title.Children.Add(new TextBlock
            {
                Text = Known(adapter.Name),
                Style = StyleOf("Body"),
                FontWeight = FontWeights.SemiBold
            });
            if (adapter.IsPrimary || ReferenceEquals(adapter, primary))
                title.Children.Add(Badge(Loc.T("Gpu_Primary"), BrushOf("GoodBgBrush"), BrushOf("GoodBrush"),
                    Loc.T("Gpu_PrimaryTip"), new Thickness(9, 0, 0, 0)));
            panel.Children.Add(title);

            panel.Children.Add(new TextBlock
            {
                Text = Known(adapter.PnpDeviceId),
                Style = StyleOf("Mono"),
                Margin = new Thickness(0, 5, 0, 0),
                TextWrapping = TextWrapping.Wrap
            });
            panel.Children.Add(new TextBlock
            {
                Text = DriverText(adapter.DriverVersion, adapter.DriverDate),
                Style = StyleOf("Caption"),
                Margin = new Thickness(0, 4, 0, 0)
            });

            card.Child = panel;
            AdaptersList.Children.Add(card);
        }
    }

    private async Task ReadSensorsAsync()
    {
        try
        {
            var snapshot = await Task.Run(() => HardwareSensors.Instance.Read());
            if (snapshot.GpuTemperatureC is double temperature) _temperature ??= temperature;
            if (snapshot.GpuHotspotTemperatureC is double hotspot) _hotspot ??= hotspot;
            if (snapshot.GpuClockMHz is double clock) _clock ??= clock;
            if (snapshot.GpuFanPercent is double fan) _fan ??= fan;
            if (snapshot.GpuLoadPercent is double load) _load ??= load;
            RenderLive();
        }
        catch (Exception ex)
        {
            Log.Error("UI", "Lecture des capteurs GPU impossible", ex);
            ShellState.Status(Loc.T("Gpu_StatusSensorsFail", ex.Message));
        }
    }

    private void OnSampleUpdated(MonitorSample sample)
    {
        try
        {
            Dispatcher.BeginInvoke(() => ApplySample(sample));
        }
        catch (Exception ex)
        {
            Log.Warn("UI", "Mise à jour des mesures GPU refusée : " + ex.Message);
        }
    }

    private void ApplySample(MonitorSample sample)
    {
        try
        {
            if (sample.GpuTemperatureC is double temperature) _temperature = temperature;
            if (sample.GpuClockMHz is double clock) _clock = clock;
            if (sample.GpuFanPercent is double fan) _fan = fan;
            _load = sample.GpuUsagePercent;
            if (sample.VramTotalBytes > 0) _vramTotal = sample.VramTotalBytes;
            _vramUsed = sample.VramUsedBytes;
            RenderLive();
        }
        catch (Exception ex)
        {
            Log.Error("UI", "Affichage des mesures GPU impossible", ex);
        }
    }

    private void RenderLive()
    {
        try
        {
            if (_temperature is double temperature)
            {
                TempCard.Value = temperature.ToString("F0", CultureInfo.CurrentCulture);
                TempCard.Unit = "°C";
                TempCard.SubText = _hotspot is double hotspot
                    ? Loc.T("Gpu_Hotspot", hotspot.ToString("F0", CultureInfo.CurrentCulture))
                    : string.Empty;
                TempCard.ProgressPercent = Math.Clamp(temperature, 0, 120) * 100.0 / 120;
                TempCard.Level = temperature >= 85 ? HealthLevel.Critical
                    : temperature >= 75 ? HealthLevel.Warning
                    : HealthLevel.Good;
            }
            else
            {
                TempCard.Value = Loc.T("Hw_NotAvailable");
                TempCard.Unit = string.Empty;
                TempCard.SubText = Loc.T("Gpu_RequiresAdmin");
                TempCard.ProgressPercent = double.NaN;
                TempCard.Level = null;
            }

            if (_load is double load)
            {
                LoadCard.Value = load.ToString("F0", CultureInfo.CurrentCulture);
                LoadCard.Unit = "%";
                LoadCard.SubText = string.Empty;
                LoadCard.ProgressPercent = Math.Clamp(load, 0, 100);
                LoadCard.Level = load >= 95 ? HealthLevel.Critical
                    : load >= 80 ? HealthLevel.Warning
                    : HealthLevel.Good;
            }
            else
            {
                LoadCard.Value = Loc.T("Hw_NotAvailable");
                LoadCard.Unit = string.Empty;
                LoadCard.SubText = string.Empty;
                LoadCard.ProgressPercent = double.NaN;
                LoadCard.Level = null;
            }

            if (_clock is double clock)
            {
                ClockCard.Value = clock.ToString("F0", CultureInfo.CurrentCulture);
                ClockCard.Unit = "MHz";
                ClockCard.SubText = string.Empty;
                ClockCard.ProgressPercent = double.NaN;
                ClockCard.Level = null;
            }
            else
            {
                ClockCard.Value = Loc.T("Hw_NotAvailable");
                ClockCard.Unit = string.Empty;
                ClockCard.SubText = string.Empty;
                ClockCard.ProgressPercent = double.NaN;
                ClockCard.Level = null;
            }

            if (_fan is double fan)
            {
                FanCard.Value = fan.ToString("F0", CultureInfo.CurrentCulture);
                FanCard.Unit = "%";
                FanCard.SubText = string.Empty;
                FanCard.ProgressPercent = Math.Clamp(fan, 0, 100);
                FanCard.Level = null;
            }
            else
            {
                FanCard.Value = Loc.T("Hw_NotAvailable");
                FanCard.Unit = string.Empty;
                FanCard.SubText = string.Empty;
                FanCard.ProgressPercent = double.NaN;
                FanCard.Level = null;
            }

            var total = _vramTotal > 0 ? _vramTotal : _dedicatedVram;
            if (_vramUsed > 0 && total > 0)
            {
                var used = Math.Min(_vramUsed, total);
                var percent = Math.Clamp(used * 100.0 / total, 0, 100);
                VramUsedCard.Value = FormatSize(used);
                VramUsedCard.Unit = string.Empty;
                VramUsedCard.SubText = Loc.T("Gpu_VramOfTotal", FormatSize(total));
                VramUsedCard.ProgressPercent = percent;
                VramUsedCard.Level = percent >= 90 ? HealthLevel.Critical
                    : percent >= 75 ? HealthLevel.Warning
                    : HealthLevel.Good;
            }
            else
            {
                VramUsedCard.Value = Loc.T("Hw_NotAvailable");
                VramUsedCard.Unit = string.Empty;
                VramUsedCard.SubText = string.Empty;
                VramUsedCard.ProgressPercent = double.NaN;
                VramUsedCard.Level = null;
            }

            if (total > 0)
            {
                VramTotalCard.Value = FormatSize(total);
                VramTotalCard.Unit = string.Empty;
                VramTotalCard.SubText = Loc.T("Gpu_VramDeclaredByDriver");
                VramTotalCard.ProgressPercent = double.NaN;
                VramTotalCard.Level = null;
            }
            else
            {
                VramTotalCard.Value = Loc.T("Hw_NotAvailable");
                VramTotalCard.Unit = string.Empty;
                VramTotalCard.SubText = string.Empty;
                VramTotalCard.ProgressPercent = double.NaN;
                VramTotalCard.Level = null;
            }
        }
        catch (Exception ex)
        {
            Log.Error("UI", "Rendu des mesures GPU impossible", ex);
        }
    }

    private static UIElement Row(string label, string value, string? tip = null, bool mono = false)
    {
        var text = new TextBlock
        {
            Text = value,
            Style = StyleOf(mono ? "Mono" : "Body"),
            TextWrapping = TextWrapping.Wrap
        };
        return Row(label, text, tip);
    }

    private static UIElement Row(string label, UIElement value, string? tip = null)
    {
        var grid = new Grid { Margin = new Thickness(0, 8, 0, 0) };
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

    private static Border VendorBadge(GpuVendor vendor)
    {
        switch (vendor)
        {
            case GpuVendor.Nvidia:
                return Badge("NVIDIA", BrushOf("CardAltBrush"), Frozen("#76B900"),
                    Loc.T("Gpu_VendorPciTip"), new Thickness(10, 0, 0, 0));
            case GpuVendor.Amd:
                return Badge("AMD", BrushOf("DangerBgBrush"), BrushOf("DangerBrush"),
                    Loc.T("Gpu_VendorPciTip"), new Thickness(10, 0, 0, 0));
            case GpuVendor.Intel:
                return Badge("Intel", BrushOf("InfoBgBrush"), BrushOf("InfoBrush"),
                    Loc.T("Gpu_VendorPciTip"), new Thickness(10, 0, 0, 0));
            case GpuVendor.Other:
                return Badge(Loc.T("Gpu_OtherVendor"), BrushOf("CardAltBrush"), BrushOf("TextMutedBrush"),
                    Loc.T("Gpu_OtherVendorTip"), new Thickness(10, 0, 0, 0));
            default:
                return Badge(Loc.T("Gpu_UnknownVendor"), BrushOf("CardAltBrush"), BrushOf("TextMutedBrush"),
                    Loc.T("Gpu_UnknownVendorTip"), new Thickness(10, 0, 0, 0));
        }
    }

    private static Border DriverAgeBadge(string driverDate)
    {
        var date = ParseDriverDate(driverDate);
        if (date is null)
            return Badge(Loc.T("Gpu_DriverAgeNa"), BrushOf("CardAltBrush"), BrushOf("TextMutedBrush"),
                DriverTip, new Thickness(9, 0, 0, 0));

        var months = (int)Math.Round((DateTime.Now - date.Value).TotalDays / 30.44);
        if (months < 0) months = 0;

        Brush background;
        Brush foreground;
        if (months < 18)
        {
            background = BrushOf("GoodBgBrush");
            foreground = BrushOf("GoodBrush");
        }
        else if (months < 30)
        {
            background = BrushOf("WarnBgBrush");
            foreground = BrushOf("WarnBrush");
        }
        else
        {
            background = BrushOf("DangerBgBrush");
            foreground = BrushOf("DangerBrush");
        }

        return Badge(Loc.T("Gpu_DriverAgeMonths", months.ToString(CultureInfo.CurrentCulture)),
            background, foreground, DriverTip, new Thickness(9, 0, 0, 0));
    }

    private static DateTime? ParseDriverDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var text = value.Trim();
        if (DateTime.TryParseExact(text, "dd/MM/yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var exact))
            return exact;
        if (DateTime.TryParse(text, CultureInfo.CurrentCulture, DateTimeStyles.None, out var current))
            return current;
        if (DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out var invariant))
            return invariant;
        return null;
    }

    private static string DriverText(string version, string date)
    {
        var knownVersion = Known(version);
        var knownDate = Known(date);
        var na = Loc.T("Hw_NotAvailable");
        if (knownVersion == na && knownDate == na) return Loc.T("Gpu_DriverNa");
        if (knownDate == na) return Loc.T("Gpu_DriverValue", knownVersion);
        if (knownVersion == na) return knownDate;
        return Loc.T("Gpu_DriverValueDate", knownVersion, knownDate);
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

    private static SolidColorBrush Frozen(string hex)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        brush.Freeze();
        return brush;
    }
}
