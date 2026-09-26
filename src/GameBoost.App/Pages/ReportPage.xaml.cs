using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using GameBoost.App.Services;
using GameBoost.Core.Data;
using GameBoost.Core.History;
using GameBoost.Core.Logging;
using GameBoost.Core.Models;
using GameBoost.Core.Reports;

namespace GameBoost.App.Pages;

public partial class ReportPage : UserControl
{
    private static readonly CultureInfo French = CultureInfo.GetCultureInfo("fr-FR");

    private string? _lastExportPath;

    public ReportPage()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            var latest = GetLatestReport();
            NoAnalysisPanel.Visibility = latest is null ? Visibility.Visible : Visibility.Collapsed;
        }
        catch (Exception ex)
        {
            Log.Error("UI", "Lecture des analyses enregistrees", ex);
            NoAnalysisPanel.Visibility = Visibility.Visible;
        }
        LoadFiles();
    }

    private static AnalysisReport? GetLatestReport()
    {
        return Db.GetCollection<AnalysisReport>("analyses")
            .FindAll()
            .OrderByDescending(r => r.CreatedAt)
            .FirstOrDefault();
    }

    private void OnExportAnalysisClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var report = GetLatestReport();
            if (report is null)
            {
                NoAnalysisPanel.Visibility = Visibility.Visible;
                ShellState.Status("Aucune analyse enregistrée : lancez d'abord une analyse pour exporter un rapport.");
                return;
            }

            var result = ReportGenerator.Instance.ExportHtml(report, report.Hardware,
                HistoryService.Instance.GetSessions(20));
            HandleResult(result, "Rapport d'analyse exporté.");
        }
        catch (Exception ex)
        {
            Log.Error("UI", "Export du rapport d'analyse", ex);
            ShellState.Status("Export impossible : " + ex.Message);
        }
    }

    private void OnExportSessionsClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var sessions = HistoryService.Instance.GetSessions(50);
            if (sessions.Count == 0)
            {
                ShellState.Status("Aucune session à exporter : lancez d'abord une partie depuis le Monitoring.");
                return;
            }

            var result = ReportGenerator.Instance.ExportSessionHtml(sessions);
            HandleResult(result, "Sessions exportées : " + sessions.Count + " session(s).");
        }
        catch (Exception ex)
        {
            Log.Error("UI", "Export des sessions", ex);
            ShellState.Status("Export impossible : " + ex.Message);
        }
    }

    private void HandleResult(ReportExportResult result, string successMessage)
    {
        if (!result.Success)
        {
            ShellState.Status("Export impossible : " + result.Error);
            return;
        }

        _lastExportPath = result.FilePath;
        ExportResultTitle.Text = "Fichier généré";
        ExportResultPath.Text = result.FilePath;
        ExportResultPanel.Visibility = Visibility.Visible;
        NoAnalysisPanel.Visibility = Visibility.Collapsed;
        LoadFiles();
        ShellState.Status(successMessage + " " + result.FilePath);
    }

    private void OnOpenResultClick(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_lastExportPath) || !File.Exists(_lastExportPath))
        {
            ShellState.Status("Aucun fichier à ouvrir : exportez un rapport d'abord.");
            return;
        }
        try
        {
            Process.Start(new ProcessStartInfo(_lastExportPath) { UseShellExecute = true });
            ShellState.Status("Ouverture du fichier dans le navigateur.");
        }
        catch (Exception ex)
        {
            Log.Error("UI", "Ouverture du fichier exporte", ex);
            ShellState.Status("Ouverture impossible : " + ex.Message);
        }
    }

    private void OnOpenFolderClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var directory = ReportGenerator.Instance.GetExportDirectory();
            Directory.CreateDirectory(directory);
            Process.Start(new ProcessStartInfo(directory) { UseShellExecute = true });
            ShellState.Status("Dossier des rapports : " + directory);
        }
        catch (Exception ex)
        {
            Log.Error("UI", "Ouverture du dossier des rapports", ex);
            ShellState.Status("Ouverture impossible : " + ex.Message);
        }
    }

    private void OnGoAnalysisClick(object sender, RoutedEventArgs e)
    {
        NavigationService.Navigate("analysis");
        ShellState.Status("Ouverture de l'Analyse : lancez une vérification pour créer un rapport.");
    }

    private void OnRefreshClick(object sender, RoutedEventArgs e)
    {
        LoadFiles();
        ShellState.Status("Dossier des rapports relu.");
    }

    private void LoadFiles()
    {
        try
        {
            var directory = ReportGenerator.Instance.GetExportDirectory();
            FilesFolderText.Text = "Dossier : " + directory;

            var files = Directory.Exists(directory)
                ? new DirectoryInfo(directory)
                    .EnumerateFiles("*.html")
                    .OrderByDescending(f => f.LastWriteTime)
                    .ToList()
                : new List<FileInfo>();

            FilesPanel.Children.Clear();
            EmptyFilesText.Visibility = files.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

            foreach (var file in files)
            {
                FilesPanel.Children.Add(BuildFileRow(file));
            }
        }
        catch (Exception ex)
        {
            Log.Error("UI", "Lecture du dossier des rapports", ex);
            FilesPanel.Children.Clear();
            EmptyFilesText.Text = "Dossier des rapports illisible : " + ex.Message;
            EmptyFilesText.Visibility = Visibility.Visible;
        }
    }

    private FrameworkElement BuildFileRow(FileInfo file)
    {
        var grid = new Grid { Margin = new Thickness(0, 0, 0, 8) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(130) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });

        var name = StyleText(file.Name, "Mono");
        name.FontSize = 14;
        name.TextTrimming = TextTrimming.CharacterEllipsis;
        name.VerticalAlignment = VerticalAlignment.Center;
        name.Margin = new Thickness(0, 0, 12, 0);
        Grid.SetColumn(name, 0);
        grid.Children.Add(name);

        var date = StyleText(file.LastWriteTime.ToString("dd/MM/yyyy HH:mm", French), "Caption");
        date.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(date, 1);
        grid.Children.Add(date);

        var size = StyleText(GameBoost.App.Controls.BytesToSizeConverter.Format(file.Length), "Mono");
        size.FontSize = 14;
        size.VerticalAlignment = VerticalAlignment.Center;
        size.HorizontalAlignment = HorizontalAlignment.Right;
        Grid.SetColumn(size, 2);
        grid.Children.Add(size);

        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(12, 0, 0, 0)
        };
        actions.Children.Add(BuildRowButton("&#xE774;", "Ouvrir", OnOpenFile, file,
            "Ouvre ce fichier HTML dans le navigateur par défaut."));
        actions.Children.Add(BuildRowButton("&#xE838;", "Dossier", OnOpenFolderClick, file,
            "Ouvre le dossier qui contient ce fichier."));
        Grid.SetColumn(actions, 3);
        grid.Children.Add(actions);

        return grid;
    }

    private static Button BuildRowButton(string glyph, string text, RoutedEventHandler handler,
        object tag, string tooltip)
    {
        var button = new Button
        {
            Tag = tag,
            ToolTip = tooltip,
            Margin = new Thickness(6, 0, 0, 0),
            Padding = new Thickness(9, 3, 9, 3)
        };
        button.SetResourceReference(Button.StyleProperty, "GhostButton");

        var content = new StackPanel { Orientation = Orientation.Horizontal };
        content.Children.Add(new TextBlock
        {
            Text = glyph,
            FontFamily = (System.Windows.Media.FontFamily)Application.Current.FindResource("IconFont"),
            FontSize = 13,
            VerticalAlignment = VerticalAlignment.Center
        });
        content.Children.Add(new TextBlock
        {
            Text = text,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(6, 0, 0, 0)
        });
        button.Content = content;
        button.Click += handler;
        return button;
    }

    private void OnOpenFile(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: FileInfo file } || !file.Exists)
        {
            ShellState.Status("Ce fichier n'existe plus.");
            return;
        }
        try
        {
            Process.Start(new ProcessStartInfo(file.FullName) { UseShellExecute = true });
            ShellState.Status("Ouverture de " + file.Name);
        }
        catch (Exception ex)
        {
            Log.Error("UI", "Ouverture d'un rapport", ex);
            ShellState.Status("Ouverture impossible : " + ex.Message);
        }
    }

    private static TextBlock StyleText(string text, string styleKey)
    {
        var block = new TextBlock { Text = text };
        block.SetResourceReference(TextBlock.StyleProperty, styleKey);
        return block;
    }
}
