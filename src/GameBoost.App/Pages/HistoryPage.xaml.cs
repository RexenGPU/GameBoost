using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using GameBoost.App.Controls;
using GameBoost.App.Services;
using GameBoost.Core.History;
using GameBoost.Core.Logging;
using GameBoost.Core.Models;
using GameBoost.Core.Reports;

namespace GameBoost.App.Pages;

public partial class HistoryPage : UserControl
{
    private static readonly CultureInfo French = CultureInfo.GetCultureInfo("fr-FR");

    private readonly List<SessionRecord> _sessions = new();
    private readonly List<RadioButton> _radiosA = new();
    private readonly List<RadioButton> _radiosB = new();

    private SessionRecord? _selectedA;
    private SessionRecord? _selectedB;
    private SessionRecord? _detail;

    public HistoryPage()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        LoadSessions();
    }

    private void LoadSessions()
    {
        try
        {
            _sessions.Clear();
            _sessions.AddRange(HistoryService.Instance.GetSessions(200));
            if (_selectedA is not null && _sessions.All(s => s.Id != _selectedA.Id)) _selectedA = null;
            if (_selectedB is not null && _sessions.All(s => s.Id != _selectedB.Id)) _selectedB = null;
            RenderSessions();
            UpdateSelectionUi();
            EmptyPanel.Visibility = _sessions.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            if (_detail is not null && _sessions.All(s => s.Id != _detail.Id))
            {
                _detail = null;
                DetailPanel.Visibility = Visibility.Collapsed;
            }
        }
        catch (Exception ex)
        {
            Log.Error("UI", "Lecture de l'historique", ex);
            ShellState.Status("Historique indisponible : " + ex.Message);
        }
    }

    private void RenderSessions()
    {
        SessionsPanel.Children.Clear();
        _radiosA.Clear();
        _radiosB.Clear();

        if (_sessions.Count == 0)
        {
            SessionsPanel.Children.Add(StyleText("Aucune session à afficher pour le moment.", "BodyMuted"));
            return;
        }

        foreach (var session in _sessions)
        {
            var row = new Border
            {
                Tag = session,
                Margin = new Thickness(0, 0, 0, 8),
                Padding = new Thickness(14, 10, 14, 10),
                Cursor = Cursors.Hand
            };
            row.SetResourceReference(Border.StyleProperty, "CardFlat");
            row.MouseLeftButtonUp += OnSessionRowClick;
            row.Child = BuildRow(session);
            SessionsPanel.Children.Add(row);
        }
    }

    private FrameworkElement BuildRow(SessionRecord session)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(104) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(136) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(124) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(88) });

        var selector = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        selector.Children.Add(BuildRadio("A", session, true));
        selector.Children.Add(BuildRadio("B", session, false));
        Grid.SetColumn(selector, 0);
        grid.Children.Add(selector);

        var identity = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 16, 0) };
        identity.Children.Add(StyleText(
            string.IsNullOrWhiteSpace(session.GameName) ? "Session" : session.GameName, "H3"));
        identity.Children.Add(StyleText(
            session.StartTime.ToString("dd/MM/yyyy HH:mm", French) + " · " + Duration(session.StartTime, session.EndTime),
            "Caption"));
        if (!string.IsNullOrWhiteSpace(session.ProfileUsed))
        {
            var chip = new Border
            {
                Margin = new Thickness(0, 6, 0, 0),
                Padding = new Thickness(9, 2, 9, 2),
                CornerRadius = new CornerRadius(999),
                HorizontalAlignment = HorizontalAlignment.Left
            };
            chip.SetResourceReference(Border.StyleProperty, "CardFlat");
            chip.Child = StyleText(session.ProfileUsed, "Caption");
            identity.Children.Add(chip);
        }
        Grid.SetColumn(identity, 1);
        grid.Children.Add(identity);

        grid.Children.Add(ValueCell(session.AverageFps is null ? null : session.AverageFps.Value.ToString("F1"),
            session.AverageFps is null ? "FPS non enregistrés" : null, 2));
        grid.Children.Add(ValueCell(
            FormatPair(session.OnePercentLowFps, session.MinimumFps, session.MaximumFps), null, 3));
        grid.Children.Add(ValueCell(
            FormatPair(session.MaxGpuTemperatureC, session.AverageGpuUsagePercent, null, " °C", " %"), null, 4));
        grid.Children.Add(ValueCell(Duration(session.StartTime, session.EndTime), null, 5, 12));

        return grid;
    }

    private RadioButton BuildRadio(string column, SessionRecord session, bool isA)
    {
        var radio = new RadioButton
        {
            Content = column,
            Tag = session,
            GroupName = isA ? "historyA" : "historyB",
            Margin = new Thickness(0, 0, 6, 0),
            VerticalAlignment = VerticalAlignment.Center,
            ToolTip = isA
                ? "Désigne cette session comme session A de la comparaison."
                : "Désigne cette session comme session B de la comparaison."
        };
        radio.SetResourceReference(RadioButton.StyleProperty, "Chip");
        if (isA)
        {
            radio.Checked += OnSelectA;
            _radiosA.Add(radio);
            if (_selectedA is not null && _selectedA.Id == session.Id) radio.IsChecked = true;
        }
        else
        {
            radio.Checked += OnSelectB;
            _radiosB.Add(radio);
            if (_selectedB is not null && _selectedB.Id == session.Id) radio.IsChecked = true;
        }
        return radio;
    }

    private FrameworkElement ValueCell(string? value, string? fallback, int column, double rightMargin = 0)
    {
        var text = string.IsNullOrWhiteSpace(value)
            ? StyleText(fallback ?? "—", "Caption")
            : StyleText(value, "Mono");
        if (!string.IsNullOrWhiteSpace(value)) text.FontSize = 15;
        text.HorizontalAlignment = HorizontalAlignment.Right;
        text.TextAlignment = TextAlignment.Right;
        text.VerticalAlignment = VerticalAlignment.Center;
        text.Margin = new Thickness(0, 0, rightMargin, 0);
        text.SetResourceReference(TextBlock.ForegroundProperty, string.IsNullOrWhiteSpace(value)
            ? "TextFaintBrush"
            : "TextBrush");
        Grid.SetColumn(text, column);
        return text;
    }

    private void OnSelectA(object sender, RoutedEventArgs e)
    {
        try
        {
            if (sender is not RadioButton { IsChecked: true, Tag: SessionRecord session }) return;
            _selectedA = session;
            foreach (var radio in _radiosA)
                if (!ReferenceEquals(radio, sender)) radio.IsChecked = false;
            UpdateSelectionUi();
        }
        catch (Exception ex)
        {
            Log.Error("UI", "Selection de la session A", ex);
            ShellState.Status("Sélection impossible : " + ex.Message);
        }
    }

    private void OnSelectB(object sender, RoutedEventArgs e)
    {
        try
        {
            if (sender is not RadioButton { IsChecked: true, Tag: SessionRecord session }) return;
            _selectedB = session;
            foreach (var radio in _radiosB)
                if (!ReferenceEquals(radio, sender)) radio.IsChecked = false;
            UpdateSelectionUi();
        }
        catch (Exception ex)
        {
            Log.Error("UI", "Selection de la session B", ex);
            ShellState.Status("Sélection impossible : " + ex.Message);
        }
    }

    private void UpdateSelectionUi()
    {
        SelectionCountText.Text = "A : " + Label(_selectedA) + " · B : " + Label(_selectedB);
        CompareButton.IsEnabled = _selectedA is not null && _selectedB is not null;

        static string Label(SessionRecord? session) => session is null
            ? "aucune"
            : session.StartTime.ToString("dd/MM HH:mm", French) + " " +
              (string.IsNullOrWhiteSpace(session.GameName) ? "session" : session.GameName);
    }

    private void OnSessionRowClick(object sender, MouseButtonEventArgs e)
    {
        if (IsInsideRadio(e.OriginalSource as DependencyObject)) return;
        if (sender is not Border { Tag: SessionRecord session }) return;
        try
        {
            RenderDetail(session);
        }
        catch (Exception ex)
        {
            Log.Error("UI", "Affichage du detail de la session", ex);
            ShellState.Status("Détail impossible : " + ex.Message);
        }
    }

    private static bool IsInsideRadio(DependencyObject? source)
    {
        for (var depth = 0; depth < 12 && source is not null; depth++)
        {
            if (source is RadioButton) return true;
            try
            {
                source = VisualTreeHelper.GetParent(source);
            }
            catch
            {
                return false;
            }
        }
        return false;
    }

    private void RenderDetail(SessionRecord session)
    {
        _detail = session;
        DetailTitle.Text = string.IsNullOrWhiteSpace(session.GameName) ? "Session" : session.GameName;
        DetailMeta.Text = session.StartTime.ToString("dddd d MMMM yyyy 'à' HH:mm", French) +
                          " · " + Duration(session.StartTime, session.EndTime) +
                          " · profil : " + (string.IsNullOrWhiteSpace(session.ProfileUsed) ? "aucun" : session.ProfileUsed);

        DetailFps.Text = session.AverageFps is double fps ? fps.ToString("F1") + " FPS" : "FPS non enregistrés";
        DetailLow.Text = Format(session.OnePercentLowFps);
        DetailMinMax.Text = session.MinimumFps is double min && session.MaximumFps is double max
            ? min.ToString("F0") + " · " + max.ToString("F0")
            : "—";
        DetailDuration.Text = Duration(session.StartTime, session.EndTime);
        DetailGpu.Text = Format(session.AverageGpuUsagePercent, " %") + " · " + Format(session.MaxGpuTemperatureC, " °C", "F0");
        DetailCpu.Text = Format(session.AverageCpuUsagePercent, " %") + " · " + Format(session.MaxCpuTemperatureC, " °C", "F0");
        DetailRam.Text = session.PeakRamUsedBytes > 0
            ? BytesToSizeConverter.Format(session.PeakRamUsedBytes)
            : "—";
        DetailProfile.Text = string.IsNullOrWhiteSpace(session.ProfileUsed) ? "—" : session.ProfileUsed;

        if (session.FrameTimeSeriesMs is { Count: >= 2 } frameTimes)
        {
            DetailFrameTimeChart.Series = new[]
            {
                new ChartSeries
                {
                    Name = "Frametime",
                    Values = frameTimes,
                    Color = Color.FromRgb(0x60, 0xA5, 0xFA),
                    Thickness = 2,
                    Area = true
                }
            };
            DetailFrameTimeChart.Visibility = Visibility.Visible;
            DetailFrameTimeHint.Visibility = Visibility.Collapsed;
        }
        else
        {
            DetailFrameTimeChart.Visibility = Visibility.Collapsed;
            DetailFrameTimeHint.Visibility = Visibility.Visible;
        }

        DetailOptimizationsPanel.Children.Clear();
        var optimizations = session.OptimizationsApplied ?? new List<string>();
        if (optimizations.Count == 0)
        {
            DetailOptimizationsPanel.Children.Add(StyleText("Aucune optimisation appliquée pendant cette session.", "BodyMuted"));
        }
        else
        {
            foreach (var optimization in optimizations)
            {
                if (string.IsNullOrWhiteSpace(optimization)) continue;
                DetailOptimizationsPanel.Children.Add(StyleText("• " + optimization, "BodyMuted"));
            }
        }

        DetailNotes.Text = string.IsNullOrWhiteSpace(session.Notes) ? "Aucune note pour cette session." : session.Notes;
        DetailPanel.Visibility = Visibility.Visible;
    }

    private void OnCompareClick(object sender, RoutedEventArgs e)
    {
        if (_selectedA is null || _selectedB is null) return;
        try
        {
            var comparison = HistoryService.Instance.Compare(_selectedA.Id, _selectedB.Id);
            RenderComparison(comparison);
            ComparisonPanel.Visibility = Visibility.Visible;
            ShellState.Status("Comparaison affichée entre deux sessions.");
        }
        catch (Exception ex)
        {
            Log.Error("UI", "Comparaison des sessions", ex);
            ShellState.Status("Comparaison impossible : " + ex.Message);
        }
    }

    private void RenderComparison(SessionComparison comparison)
    {
        var a = comparison.SessionA;
        var b = comparison.SessionB;

        ComparisonTable.Children.Clear();
        ComparisonTable.ColumnDefinitions.Clear();
        ComparisonTable.RowDefinitions.Clear();
        ComparisonTable.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) });
        ComparisonTable.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        ComparisonTable.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        ComparisonTable.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        ComparisonTable.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        AddComparisonCell(0, 0, "Mesure", "Label", null, null, 0);
        AddComparisonCell(1, 0, "Session A", "H3", null, SessionHeader(a), 0);
        AddComparisonCell(2, 0, "Session B", "H3", null, SessionHeader(b), 0);
        AddComparisonCell(3, 0, "Écart (B − A)", "H3", null, null, 0);

        var rows = new (string Label, string A, string B, double? Delta, string Unit, bool HigherIsBetter)[]
        {
            ("FPS moyen", Format(a.AverageFps), Format(b.AverageFps), comparison.AvgFpsDelta, string.Empty, true),
            ("1 % low", Format(a.OnePercentLowFps), Format(b.OnePercentLowFps), comparison.OnePercentLowDelta, string.Empty, true),
            ("Utilisation GPU moyenne", Format(a.AverageGpuUsagePercent, " %"), Format(b.AverageGpuUsagePercent, " %"), comparison.GpuUsageDelta, " %", false),
            ("Température GPU max", Format(a.MaxGpuTemperatureC, " °C", "F0"), Format(b.MaxGpuTemperatureC, " °C", "F0"), comparison.MaxGpuTempDelta, " °C", false),
            ("Utilisation CPU moyenne", Format(a.AverageCpuUsagePercent, " %"), Format(b.AverageCpuUsagePercent, " %"), comparison.AvgCpuUsageDelta, " %", false),
            ("Température CPU max", Format(a.MaxCpuTemperatureC, " °C", "F0"), Format(b.MaxCpuTemperatureC, " °C", "F0"), comparison.MaxCpuTempDelta, " °C", false)
        };

        for (var i = 0; i < rows.Length; i++)
        {
            var row = i + 1;
            ComparisonTable.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            AddComparisonCell(0, row, rows[i].Label, "Body", null, null, 7);
            AddComparisonCell(1, row, rows[i].A, "Mono", null, null, 7);
            AddComparisonCell(2, row, rows[i].B, "Mono", null, null, 7);
            AddComparisonCell(3, row, FormatDelta(rows[i].Delta, rows[i].Unit), "Mono",
                DeltaBrush(rows[i].Delta, rows[i].HigherIsBetter), null, 7);
        }

        var series = BuildCompareSeries(a, b);
        if (series is not null)
        {
            CompareChart.Series = series;
            CompareChart.Visibility = Visibility.Visible;
            CompareChartHint.Visibility = Visibility.Collapsed;
        }
        else
        {
            CompareChart.Series = null;
            CompareChart.Visibility = Visibility.Collapsed;
            CompareChartHint.Text = "Comparaison graphique indisponible : au moins une des deux sessions ne contient pas de série de FPS.";
            CompareChartHint.Visibility = Visibility.Visible;
        }
    }

    private static string SessionHeader(SessionRecord session)
    {
        return session.StartTime.ToString("dd/MM HH:mm", French) + " · " +
               (string.IsNullOrWhiteSpace(session.GameName) ? "session" : session.GameName);
    }

    private void AddComparisonCell(int column, int row, string text, string styleKey, string? brushKey,
        string? subtext, double bottomMargin)
    {
        FrameworkElement content = StyleText(text, styleKey);
        if (!string.IsNullOrWhiteSpace(subtext))
        {
            var stack = new StackPanel();
            stack.Children.Add((TextBlock)content);
            stack.Children.Add(StyleText(subtext, "Caption"));
            content = stack;
        }

        content.Margin = new Thickness(0, 7, 10, bottomMargin);
        if (content is TextBlock block)
        {
            block.TextWrapping = TextWrapping.Wrap;
            if (brushKey is not null) block.SetResourceReference(TextBlock.ForegroundProperty, brushKey);
        }
        Grid.SetColumn(content, column);
        Grid.SetRow(content, row);
        ComparisonTable.Children.Add(content);
    }

    private static string? DeltaBrush(double? delta, bool higherIsBetter)
    {
        if (delta is null || Math.Abs(delta.Value) < 0.005) return "TextMutedBrush";
        if (higherIsBetter) return delta.Value > 0 ? "GoodBrush" : "DangerBrush";
        return delta.Value > 0 ? "WarnBrush" : "GoodBrush";
    }

    private static string FormatDelta(double? delta, string unit)
    {
        if (delta is null) return "—";
        var sign = delta.Value > 0 ? "+" : string.Empty;
        return sign + delta.Value.ToString("0.##", French) + unit;
    }

    private IEnumerable<ChartSeries>? BuildCompareSeries(SessionRecord a, SessionRecord b)
    {
        if (a.FpsSeries is not { Count: >= 2 } seriesA || b.FpsSeries is not { Count: >= 2 } seriesB) return null;
        var length = Math.Min(seriesA.Count, seriesB.Count);
        var valuesA = seriesA.Take(length).ToList();
        var valuesB = seriesB.Take(length).ToList();
        return new[]
        {
            new ChartSeries
            {
                Name = "Session A",
                Values = valuesA,
                Color = Color.FromRgb(0x60, 0xA5, 0xFA),
                Thickness = 2,
                Area = false
            },
            new ChartSeries
            {
                Name = "Session B",
                Values = valuesB,
                Color = Color.FromRgb(0x22, 0xC5, 0x5E),
                Thickness = 2,
                Area = false
            }
        };
    }

    private void OnRefreshClick(object sender, RoutedEventArgs e)
    {
        LoadSessions();
        ShellState.Status(_sessions.Count + " session(s) dans l'historique.");
    }

    private void OnExportSelectionClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var selected = new List<SessionRecord>();
            if (_selectedA is not null) selected.Add(_selectedA);
            if (_selectedB is not null && selected.All(s => s.Id != _selectedB.Id)) selected.Add(_selectedB);
            if (selected.Count == 0)
            {
                ShellState.Status("Sélectionnez une session en A et/ou en B avant d'exporter.");
                return;
            }

            var result = ReportGenerator.Instance.ExportSessionHtml(selected);
            if (!result.Success)
            {
                ShellState.Status("Export impossible : " + result.Error);
                return;
            }
            ShellState.Status("Sélection exportée : " + result.FilePath);
        }
        catch (Exception ex)
        {
            Log.Error("UI", "Export de la selection", ex);
            ShellState.Status("Export impossible : " + ex.Message);
        }
    }

    private void OnExportDetailClick(object sender, RoutedEventArgs e)
    {
        if (_detail is null)
        {
            ShellState.Status("Aucune session sélectionnée à exporter.");
            return;
        }
        try
        {
            var result = ReportGenerator.Instance.ExportSessionHtml(new List<SessionRecord> { _detail });
            if (!result.Success)
            {
                ShellState.Status("Export impossible : " + result.Error);
                return;
            }
            Process.Start(new ProcessStartInfo(result.FilePath) { UseShellExecute = true });
            ShellState.Status("Session exportée : " + result.FilePath);
        }
        catch (Exception ex)
        {
            Log.Error("UI", "Export de la session", ex);
            ShellState.Status("Export impossible : " + ex.Message);
        }
    }

    private void OnClearClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var first = MessageBox.Show(
                "Vider tout l'historique des sessions ?\n\nLes mesures enregistrées seront supprimées de ce PC.",
                "Vider l'historique", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (first != MessageBoxResult.Yes)
            {
                ShellState.Status("Effacement annulé.");
                return;
            }

            var second = MessageBox.Show(
                "Confirmation définitive : toutes les sessions seront supprimées et ne pourront pas être récupérées.\n\nContinuer ?",
                "Confirmation", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (second != MessageBoxResult.Yes)
            {
                ShellState.Status("Effacement annulé.");
                return;
            }

            HistoryService.Instance.ClearAll();
            _selectedA = null;
            _selectedB = null;
            _detail = null;
            DetailPanel.Visibility = Visibility.Collapsed;
            ComparisonPanel.Visibility = Visibility.Collapsed;
            LoadSessions();
            ShellState.Status("Historique vidé.");
        }
        catch (Exception ex)
        {
            Log.Error("UI", "Effacement de l'historique", ex);
            ShellState.Status("Effacement impossible : " + ex.Message);
        }
    }

    private void OnHowToRecordClick(object sender, RoutedEventArgs e)
    {
        NavigationService.Navigate("monitoring");
        ShellState.Status("Ouverture du Monitoring : utilisez « Démarrer l'enregistrement » pendant votre partie.");
    }

    private static string Format(double? value, string suffix = "", string format = "0.##")
    {
        if (value is not double number || double.IsNaN(number)) return "—";
        return number.ToString(format, French) + suffix;
    }

    private static string FormatPair(double? first, double? second, double? third)
    {
        if (first is null) return "—";
        var text = first.Value.ToString("F0", French);
        if (second is double other) text += " · " + other.ToString("F0", French);
        if (third is double last) text += " · " + last.ToString("F0", French);
        return text;
    }

    private static string FormatPair(double? temperature, double? usage, double? unused, string tempSuffix, string usageSuffix)
    {
        if (temperature is null && usage is null) return "—";
        var text = temperature is double temp ? temp.ToString("F0", French) + tempSuffix : "—";
        if (usage is double value) text += " · " + value.ToString("F0", French) + usageSuffix;
        return text;
    }

    private static string Duration(DateTime start, DateTime end)
    {
        var span = end - start;
        if (span <= TimeSpan.Zero) return "—";
        if (span.TotalHours >= 1) return (int)span.TotalHours + " h " + span.Minutes + " min";
        if (span.TotalMinutes >= 1) return span.Minutes + " min " + span.Seconds + " s";
        return span.Seconds + " s";
    }

    private static TextBlock StyleText(string text, string styleKey)
    {
        var block = new TextBlock { Text = text };
        block.SetResourceReference(TextBlock.StyleProperty, styleKey);
        return block;
    }
}
