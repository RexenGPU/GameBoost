using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using GameBoost.App.Controls;
using GameBoost.App.Services;
using GameBoost.Core.Data;
using GameBoost.Core.Hardware;
using GameBoost.Core.Logging;
using GameBoost.Core.Models;

namespace GameBoost.App.Pages;

public partial class HardwarePage : UserControl
{
    private static readonly string[] ModuleHeaders = { "CAPACITÉ", "FRÉQUENCE", "FABRICANT", "RÉFÉRENCE", "EMPLACEMENT" };
    private static readonly double[] ModuleWeights = { 1.1, 1.0, 1.5, 2.0, 1.3 };

    private readonly List<Border> _cards = new();
    private bool? _wide;
    private bool _busy;
    private bool _loaded;
    private List<PeripheralInfo> _peripherals = new();

    public HardwarePage()
    {
        InitializeComponent();
        _cards.Add(CpuCard);
        _cards.Add(RamCard);
        _cards.Add(MotherboardCard);
        _cards.Add(SystemCard);
        _cards.Add(DisplaysCard);
        _cards.Add(PeripheralsCard);
        _cards.Add(GpuCard);
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            ElevationBanner.Visibility = AppPaths.IsElevated ? Visibility.Collapsed : Visibility.Visible;
        }
        catch (Exception ex)
        {
            Log.Error("UI", "Lecture du niveau d'élévation impossible", ex);
            ShellState.Status("Niveau d'élévation indisponible : " + ex.Message);
        }

        ApplyLayout();
        if (!_loaded && !_busy) _ = LoadAsync();
    }

    private void OnSizeChanged(object sender, SizeChangedEventArgs e) => ApplyLayout();

    private void ApplyLayout()
    {
        var wide = ContentGrid.ActualWidth >= 1120;
        if (_wide == wide) return;
        _wide = wide;
        ContentGrid.ColumnDefinitions[1].Width = wide ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
        RightColumn.Visibility = wide ? Visibility.Visible : Visibility.Collapsed;
        LeftColumn.Margin = wide ? new Thickness(0, 0, 7, 0) : new Thickness(0);
        LeftColumn.Children.Clear();
        RightColumn.Children.Clear();
        for (var i = 0; i < _cards.Count; i++)
        {
            if (wide && i % 2 == 1) RightColumn.Children.Add(_cards[i]);
            else LeftColumn.Children.Add(_cards[i]);
        }
    }

    private async void OnRefreshClick(object sender, RoutedEventArgs e)
    {
        ShellState.Status("Nouvelle détection du matériel…");
        await LoadAsync();
    }

    private async Task LoadAsync()
    {
        if (_busy) return;
        _busy = true;
        RefreshButton.IsEnabled = false;
        LoadingPanel.Visibility = Visibility.Visible;
        ContentGrid.Visibility = Visibility.Collapsed;
        try
        {
            var report = await HardwareDetector.Instance.CollectAsync();
            Fill(report);
            _loaded = true;
            ShellState.Status("Matériel détecté le " + report.CollectedAt.ToString("HH:mm:ss", CultureInfo.CurrentCulture) + ".");
        }
        catch (Exception ex)
        {
            Log.Error("UI", "Détection du matériel impossible", ex);
            ShellState.Status("Détection du matériel impossible : " + ex.Message);
            Fill(new HardwareReport());
            _loaded = true;
        }
        finally
        {
            _busy = false;
            RefreshButton.IsEnabled = true;
            LoadingPanel.Visibility = Visibility.Collapsed;
            ContentGrid.Visibility = Visibility.Visible;
            ContentGrid.UpdateLayout();
            ApplyLayout();
        }
    }

    private void Fill(HardwareReport report)
    {
        try
        {
            FillCpu(report.Cpu);
            FillRam(report.Ram);
            FillMotherboard(report.Motherboard);
            FillSystem(report.Os, report.DirectXVersion);
            FillDisplays(report.Displays);
            FillGpu(report.Gpu);
            _peripherals = report.Peripherals ?? new List<PeripheralInfo>();
            RenderPeripherals();
        }
        catch (Exception ex)
        {
            Log.Error("UI", "Affichage de la fiche matérielle impossible", ex);
            ShellState.Status("Affichage du matériel impossible : " + ex.Message);
        }
    }

    private void FillCpu(CpuInfo cpu)
    {
        CpuFields.Children.Clear();
        CpuFields.Children.Add(Row("NOM", Known(cpu.Name)));
        CpuFields.Children.Add(Row("FABRICANT", Known(cpu.Manufacturer)));
        CpuFields.Children.Add(Row("CŒURS / THREADS",
            cpu.PhysicalCores > 0
                ? cpu.PhysicalCores.ToString(CultureInfo.CurrentCulture) + " cœurs / " +
                  cpu.LogicalCores.ToString(CultureInfo.CurrentCulture) + " threads"
                : "Non disponible",
            "Un cœur est une unité de calcul du processeur ; les threads permettent de traiter deux tâches par cœur."));
        CpuFields.Children.Add(Row("HORLOGE BASE / MAX", ClockText(cpu.BaseClockMHz, cpu.MaxClockMHz),
            "Fréquence de fonctionnement du processeur, exprimée en mégahertz (MHz)."));
        CpuFields.Children.Add(Row("ARCHITECTURE", Known(cpu.Architecture)));
        CpuFields.Children.Add(Row("SOCKET", Known(cpu.Socket)));
        CpuFields.Children.Add(Row("DESCRIPTION", Known(cpu.Description)));
    }

    private void FillRam(RamInfo ram)
    {
        RamFields.Children.Clear();
        RamFields.Children.Add(Row("TOTAL", ram.TotalBytes > 0 ? Size(ram.TotalBytes) : "Non disponible"));
        RamFields.Children.Add(Row("DISPONIBLE", ram.AvailableBytes > 0 ? Size(ram.AvailableBytes) : "Non disponible"));
        RamFields.Children.Add(Row("FRÉQUENCE", ram.SpeedMHz > 0
            ? ram.SpeedMHz.ToString("N0", CultureInfo.CurrentCulture) + " MHz"
            : "Non disponible"));
        RamFields.Children.Add(Row("FACTEUR DE FORME", Known(ram.FormFactor)));

        var total = ram.TotalBytes;
        var used = total - ram.AvailableBytes;
        if (total <= 0 || used < 0)
        {
            RamFields.Children.Add(Row("UTILISATION", "Non disponible"));
        }
        else
        {
            var percent = Math.Clamp(used * 100.0 / total, 0, 100);
            var bar = new StackPanel { Margin = new Thickness(0, 4, 0, 0) };
            bar.Children.Add(new ProgressBar
            {
                Style = StyleOf("SlimProgress"),
                Minimum = 0,
                Maximum = 100,
                Value = percent,
                ToolTip = "Part de la mémoire occupée : total moins disponible."
            });
            bar.Children.Add(new TextBlock
            {
                Text = percent.ToString("F1", CultureInfo.CurrentCulture) + " % utilisée",
                Style = StyleOf("Caption"),
                Margin = new Thickness(0, 6, 0, 0)
            });
            RamFields.Children.Add(Row("UTILISATION", bar));
        }

        RamModules.Children.Clear();
        if (ram.Modules.Count == 0)
        {
            RamModules.Children.Add(new TextBlock { Text = "Non disponible", Style = StyleOf("BodyMuted") });
            return;
        }

        RamModules.Children.Add(ModuleRow(ModuleHeaders, true));
        foreach (var module in ram.Modules)
        {
            RamModules.Children.Add(ModuleRow(new[]
            {
                module.CapacityBytes > 0 ? Size(module.CapacityBytes) : "Non disponible",
                module.SpeedMHz > 0 ? module.SpeedMHz.ToString("N0", CultureInfo.CurrentCulture) + " MHz" : "Non disponible",
                Known(module.Manufacturer),
                Known(module.PartNumber),
                Known(module.Slot)
            }, false));
        }
    }

    private void FillMotherboard(MotherboardInfo board)
    {
        MotherboardFields.Children.Clear();
        MotherboardFields.Children.Add(Row("FABRICANT", Known(board.Manufacturer)));
        MotherboardFields.Children.Add(Row("MODÈLE", Known(board.Product)));
        MotherboardFields.Children.Add(Row("VERSION", Known(board.Version)));
        MotherboardFields.Children.Add(Row("BIOS", BiosText(board.BiosVersion, board.BiosDate),
            "Version et date du BIOS telles que rapportées par le fabricant de la carte mère."));
        MotherboardFields.Children.Add(Row("NUMÉRO DE SÉRIE", Known(board.SerialNumber),
            "Identifiant local de la carte mère, jamais transmis hors de votre PC.", true));
    }

    private void FillSystem(OsInfo os, string directX)
    {
        SystemFields.Children.Clear();
        SystemFields.Children.Add(Row("SYSTÈME", Known(os.Caption)));
        SystemFields.Children.Add(Row("VERSION", VersionText(os.Version, os.Build)));
        SystemFields.Children.Add(Row("ÉDITION", Known(os.Edition)));
        SystemFields.Children.Add(Row("ARCHITECTURE", Known(os.Architecture)));
        SystemFields.Children.Add(Row("DEPUIS LE DÉMARRAGE", UptimeText(os.Uptime),
            "Durée pendant laquelle le PC est allumé sans redémarrage."));
        SystemFields.Children.Add(Row("MODE JEU", Badge(os.GameModeEnabled ? "Activé" : "Désactivé",
            os.GameModeEnabled ? BrushOf("GoodBgBrush") : BrushOf("CardAltBrush"),
            os.GameModeEnabled ? BrushOf("GoodBrush") : BrushOf("TextMutedBrush"),
            "Réglage Windows qui priorise les jeux en arrière-plan. La valeur affichée est lue dans le registre.",
            new Thickness(0))));
        SystemFields.Children.Add(Row("DIRECTX", Known(directX),
            "Interface graphique utilisée par les jeux. DirectX 12 est la version courante sur Windows récent."));
        SystemFields.Children.Add(Row("NIVEAU D'ÉLÉVATION", Badge(
            AppPaths.IsElevated ? "Administrateur" : "Utilisateur standard",
            AppPaths.IsElevated ? BrushOf("GoodBgBrush") : BrushOf("WarnBgBrush"),
            AppPaths.IsElevated ? BrushOf("GoodBrush") : BrushOf("WarnBrush"),
            AppPaths.IsElevated
                ? "GameBoost tourne avec les droits administrateur."
                : "Sans administrateur, les températures et la santé SMART ne peuvent pas être lues.",
            new Thickness(0))));
    }

    private void FillDisplays(List<DisplayInfo> displays)
    {
        DisplaysList.Children.Clear();
        var list = displays ?? new List<DisplayInfo>();
        DisplaysEmpty.Visibility = list.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        for (var i = 0; i < list.Count; i++) DisplaysList.Children.Add(BuildDisplay(list[i], i));
    }

    private void FillGpu(GpuInfo gpu)
    {
        GpuFields.Children.Clear();
        GpuFields.Children.Add(Row("NOM", Known(gpu.Name)));
        GpuFields.Children.Add(Row("CONSTRUCTEUR", VendorLabel(gpu.Vendor)));
        GpuFields.Children.Add(Row("MÉMOIRE DÉDIÉE", gpu.DedicatedVramBytes > 0 ? Size(gpu.DedicatedVramBytes) : "Non disponible"));
        GpuFields.Children.Add(Row("PILOTE", DriverText(gpu.DriverVersion, gpu.DriverDate)));
        GpuFields.Children.Add(Row("ADAPTATEURS", gpu.AllAdapters.Count > 0
            ? gpu.AllAdapters.Count.ToString(CultureInfo.CurrentCulture) + " détecté(s)"
            : "Non disponible"));
    }

    private UIElement BuildDisplay(DisplayInfo display, int index)
    {
        var card = new Border
        {
            Style = StyleOf("CardFlat"),
            Padding = new Thickness(14),
            Margin = new Thickness(0, 0, 0, 10)
        };
        var panel = new StackPanel();

        var title = new StackPanel { Orientation = Orientation.Horizontal };
        title.Children.Add(new TextBlock
        {
            Text = string.IsNullOrWhiteSpace(display.Description)
                ? (string.IsNullOrWhiteSpace(display.DeviceName) ? "Écran " + (index + 1).ToString(CultureInfo.CurrentCulture) : display.DeviceName)
                : display.Description,
            Style = StyleOf("H3")
        });
        if (display.Primary)
            title.Children.Add(Badge("Principal", BrushOf("GoodBgBrush"), BrushOf("GoodBrush"), "Écran utilisé comme écran principal par Windows."));
        panel.Children.Add(title);

        panel.Children.Add(Row("RÉSOLUTION",
            display.Width > 0 && display.Height > 0
                ? display.Width.ToString(CultureInfo.CurrentCulture) + " × " + display.Height.ToString(CultureInfo.CurrentCulture)
                : "Non disponible"));
        panel.Children.Add(Row("TAUX D'AFFICHAGE", display.RefreshRate > 0
            ? display.RefreshRate.ToString(CultureInfo.CurrentCulture) + " Hz"
            : "Non disponible",
            "Nombre d'images affichées par seconde par l'écran."));
        panel.Children.Add(Row("COULEURS", display.BitsPerPixel > 0
            ? display.BitsPerPixel.ToString(CultureInfo.CurrentCulture) + " bits"
            : "Non disponible"));
        panel.Children.Add(Row("CARTE GRAPHIQUE", Known(display.GpuName)));
        if (!string.IsNullOrWhiteSpace(display.DeviceName) && !string.IsNullOrWhiteSpace(display.Description))
            panel.Children.Add(Row("IDENTIFIANT", display.DeviceName, mono: true));

        card.Child = panel;
        return card;
    }

    private UIElement BuildPeripheralRow(PeripheralInfo peripheral)
    {
        var card = new Border
        {
            Style = StyleOf("CardFlat"),
            Padding = new Thickness(12, 9, 12, 9),
            Margin = new Thickness(0, 0, 0, 7)
        };
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var name = new TextBlock
        {
            Text = Known(peripheral.Name),
            Style = StyleOf("Body"),
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center
        };
        grid.Children.Add(name);

        var kind = Badge(Known(peripheral.Kind), BrushOf("CardAltBrush"), BrushOf("TextMutedBrush"),
            "Type de périphérique tel que classé par Windows.", new Thickness(10, 0, 0, 0));
        Grid.SetColumn(kind, 1);
        grid.Children.Add(kind);

        card.Child = grid;
        return card;
    }

    private void OnPeripheralSearchChanged(object sender, TextChangedEventArgs e)
    {
        if (PeripheralsList is null) return;
        RenderPeripherals();
    }

    private void RenderPeripherals()
    {
        try
        {
            var query = (PeripheralSearch?.Text ?? string.Empty).Trim();
            IEnumerable<PeripheralInfo> items = _peripherals;
            if (query.Length > 0)
                items = items.Where(p => p.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                                         p.Kind.Contains(query, StringComparison.OrdinalIgnoreCase));
            var visible = items.Take(40).ToList();

            PeripheralsList.Children.Clear();
            foreach (var peripheral in visible) PeripheralsList.Children.Add(BuildPeripheralRow(peripheral));
            PeripheralEmpty.Visibility = visible.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            PeripheralCount.Text = visible.Count == 0
                ? string.Empty
                : visible.Count.ToString(CultureInfo.CurrentCulture) + (visible.Count > 1 ? " périphériques" : " périphérique");
        }
        catch (Exception ex)
        {
            Log.Error("UI", "Affichage des périphériques impossible", ex);
            ShellState.Status("Périphériques indisponibles : " + ex.Message);
        }
    }

    private void OnElevationHelpClick(object sender, RoutedEventArgs e) => NavigationService.Navigate("settings");

    private void OnOpenGpuClick(object sender, RoutedEventArgs e) => NavigationService.Navigate("gpu");

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
        var grid = new Grid { Margin = new Thickness(0, 7, 0, 0) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var labelText = new TextBlock
        {
            Text = label,
            Style = StyleOf("Label"),
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.Wrap
        };
        grid.Children.Add(labelText);

        var host = new Grid { VerticalAlignment = VerticalAlignment.Center };
        host.Children.Add(value);
        Grid.SetColumn(host, 1);
        grid.Children.Add(host);

        if (tip is not null) grid.ToolTip = tip;
        return grid;
    }

    private static UIElement ModuleRow(IReadOnlyList<string> cells, bool header)
    {
        var grid = new Grid { Margin = new Thickness(0, header ? 0 : 4, 0, 0) };
        for (var i = 0; i < ModuleWeights.Length; i++)
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(ModuleWeights[i], GridUnitType.Star) });

        for (var i = 0; i < cells.Count && i < ModuleWeights.Length; i++)
        {
            var cell = new TextBlock
            {
                Text = cells[i],
                Style = header ? StyleOf("Label") : StyleOf("Caption"),
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center
            };
            if (!header) cell.Foreground = BrushOf("TextBrush");
            if (i > 0) cell.Margin = new Thickness(8, 0, 0, 0);
            Grid.SetColumn(cell, i);
            grid.Children.Add(cell);
        }
        return grid;
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

    private static string ClockText(double? baseClock, double? maxClock)
    {
        var basis = baseClock is > 0 ? baseClock.Value.ToString("N0", CultureInfo.CurrentCulture) : null;
        var max = maxClock is > 0 ? maxClock.Value.ToString("N0", CultureInfo.CurrentCulture) : null;
        if (basis is null && max is null) return "Non disponible";
        if (basis is null) return "max. " + max + " MHz";
        if (max is null) return basis + " MHz";
        return basis + " / " + max + " MHz";
    }

    private static string BiosText(string version, string date)
    {
        var knownVersion = Known(version);
        var knownDate = Known(date);
        if (knownVersion == "Non disponible" && knownDate == "Non disponible") return "Non disponible";
        if (knownVersion == "Non disponible") return knownDate;
        if (knownDate == "Non disponible") return knownVersion;
        return knownVersion + " · " + knownDate;
    }

    private static string VersionText(string version, string build)
    {
        var knownVersion = Known(version);
        var knownBuild = Known(build);
        if (knownVersion == "Non disponible" && knownBuild == "Non disponible") return "Non disponible";
        if (knownBuild == "Non disponible") return knownVersion;
        if (knownVersion == "Non disponible") return "build " + knownBuild;
        return knownVersion + " · build " + knownBuild;
    }

    private static string DriverText(string version, string date)
    {
        var knownVersion = Known(version);
        var knownDate = Known(date);
        if (knownVersion == "Non disponible" && knownDate == "Non disponible") return "Non disponible";
        if (knownDate == "Non disponible") return knownVersion;
        if (knownVersion == "Non disponible") return knownDate;
        return knownVersion + " · " + knownDate;
    }

    private static string UptimeText(TimeSpan uptime)
    {
        if (uptime <= TimeSpan.Zero) return "Non disponible";
        if (uptime.TotalMinutes < 1) return "moins d'une minute";
        var parts = new List<string>();
        if (uptime.Days > 0) parts.Add(uptime.Days.ToString(CultureInfo.CurrentCulture) + " j");
        if (uptime.Hours > 0) parts.Add(uptime.Hours.ToString(CultureInfo.CurrentCulture) + " h");
        parts.Add(uptime.Minutes.ToString(CultureInfo.CurrentCulture) + " min");
        return string.Join(" ", parts);
    }

    private static string Size(long bytes) => BytesToSizeConverter.Format(bytes);

    private static string Known(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "Non disponible";
        var trimmed = value.Trim();
        if (trimmed.Equals("Inconnu", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Equals("Inconnue", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Equals("Inconnus", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Equals("Inconnues", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Equals("Unknown", StringComparison.OrdinalIgnoreCase))
            return "Non disponible";
        return trimmed;
    }

    private static string VendorLabel(GpuVendor vendor) => vendor switch
    {
        GpuVendor.Nvidia => "NVIDIA",
        GpuVendor.Amd => "AMD",
        GpuVendor.Intel => "Intel",
        GpuVendor.Other => "Autre constructeur",
        _ => "Non disponible"
    };

    private static Style? StyleOf(string key) => Application.Current?.TryFindResource(key) as Style;

    private static Brush BrushOf(string key) =>
        Application.Current?.TryFindResource(key) as Brush ?? Brushes.Gray;

    private static FontFamily FontOf() =>
        Application.Current?.TryFindResource("AppFont") as FontFamily ?? new FontFamily("Segoe UI");
}
