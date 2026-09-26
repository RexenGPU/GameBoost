using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using GameBoost.App.Pages.Profiles;
using GameBoost.App.Services;
using GameBoost.Core.Data;
using GameBoost.Core.Games;
using GameBoost.Core.Hardware;
using GameBoost.Core.Logging;
using GameBoost.Core.Models;
using GameBoost.Core.Profiles;

namespace GameBoost.App.Pages;

public partial class ProfilesPage : UserControl
{
    private static readonly string[] ResolutionOptions =
        { "Conserver la résolution du jeu", "1920×1080", "2560×1440", "3840×2160", "1280×720" };

    private static readonly string[] QualityOptions = { "Non défini", "Faible", "Moyen", "Élevé", "Ultra" };

    private readonly List<GameInfo> _games = new();
    private List<ProfileCard> _cards = new();
    private GameInfo? _game;
    private GameProfile? _editing;
    private Guid? _editingId;
    private Guid? _activeId;
    private bool _loading;
    private bool _loaded;
    private string? _lastBackupPath;
    private Task<HardwareReport>? _hardwareTask;

    public ProfilesPage()
    {
        InitializeComponent();
        ResolutionCombo.ItemsSource = ResolutionOptions;
        TextureCombo.ItemsSource = QualityOptions;
        ShadowCombo.ItemsSource = QualityOptions;
        LightingCombo.ItemsSource = QualityOptions;
        Loaded += OnLoaded;
        try
        {
            _hardwareTask = HardwareDetector.Instance.CollectAsync();
        }
        catch (Exception ex)
        {
            Log.Error("UI", "Lecture du matériel pour les profils", ex);
            _hardwareTask = null;
        }
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_loaded) return;
        _loaded = true;
        LoadGames();
    }

    private async void LoadGames()
    {
        try
        {
            var games = await Task.Run(() => GameScanner.Instance.GetLibrary());
            _games.Clear();
            _games.AddRange(games);
            GameCombo.ItemsSource = null;
            GameCombo.ItemsSource = _games;
            NoGamesText.Visibility = _games.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            if (_games.Count == 0)
            {
                GameCombo.SelectedItem = null;
                _game = null;
                ResetSelection();
                LoadProfiles();
                ShellState.Status("Aucun jeu : rien à configurer pour l'instant.");
                return;
            }
            if (GameCombo.SelectedIndex < 0) GameCombo.SelectedIndex = 0;
            ShellState.Status(_games.Count + " jeu(s) disponible(s) pour les profils.");
        }
        catch (Exception ex)
        {
            Log.Error("UI", "Chargement des jeux pour les profils", ex);
            ShellState.Status("Chargement des jeux impossible : " + ex.Message);
        }
    }

    private void OnRefreshGamesClick(object sender, RoutedEventArgs e) => LoadGames();

    private void OnGameChanged(object sender, SelectionChangedEventArgs e)
    {
        _game = GameCombo.SelectedItem as GameInfo;
        ResetSelection();
        LoadProfiles();
    }

    private void ResetSelection()
    {
        _editing = null;
        _editingId = null;
        _activeId = null;
        _lastBackupPath = null;
        EditorPanel.Visibility = Visibility.Collapsed;
        ApplyPanel.Visibility = Visibility.Collapsed;
    }

    private async void LoadProfiles()
    {
        if (_game is null)
        {
            CardsList.ItemsSource = null;
            _cards.Clear();
            NoProfilesPanel.Visibility = Visibility.Collapsed;
            CardsScroll.Visibility = Visibility.Collapsed;
            return;
        }

        var game = _game;
        try
        {
            var profiles = await Task.Run(() => ProfileService.Instance.GetProfiles(game.Id));
            _cards = profiles.Select(p => new ProfileCard(p)).ToList();
            _activeId = _cards.FirstOrDefault(c => c.Profile.IsActiveForBoost)?.Profile.Id;

            CardsList.ItemsSource = _cards;
            CardsScroll.Visibility = _cards.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
            NoProfilesPanel.Visibility = _cards.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

            var keep = _editingId;
            var match = keep is null ? null : _cards.FirstOrDefault(c => c.Profile.Id == keep.Value);
            if (match is not null)
            {
                SelectProfile(match, EditorPanel.Visibility == Visibility.Visible);
            }
            else
            {
                _editing = null;
                _editingId = null;
                EditorPanel.Visibility = Visibility.Collapsed;
                ApplyPanel.Visibility = Visibility.Collapsed;
            }
        }
        catch (Exception ex)
        {
            Log.Error("UI", "Lecture des profils du jeu", ex);
            ShellState.Status("Lecture des profils impossible : " + ex.Message);
        }
    }

    private void OnCreatePresetsClick(object sender, RoutedEventArgs e)
    {
        if (_game is null) return;
        try
        {
            var created = ProfileService.Instance.CreatePresets(_game.Id, _game.Name);
            ShellState.Status(created.Count <= 1
                ? created.Count + " profil(s) créé(s)."
                : created.Count + " profils créés pour « " + _game.Name + " ».");
            LoadProfiles();
        }
        catch (Exception ex)
        {
            Log.Error("UI", "Création des profils par défaut", ex);
            ShellState.Status("Création impossible : " + ex.Message);
        }
    }

    private void OnCardClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is Border { Tag: ProfileCard card }) SelectProfile(card, true);
    }

    private void OnEditClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: ProfileCard card }) SelectProfile(card, true);
    }

    private void SelectProfile(ProfileCard card, bool openEditor)
    {
        _loading = true;
        try
        {
            _editing = card.Profile;
            _editingId = card.Profile.Id;
            if (openEditor)
            {
                EditorPanel.Visibility = Visibility.Visible;
                LoadEditor(card);
            }
        }
        finally
        {
            _loading = false;
        }
        RefreshApplySection();
    }

    private void OnCloseEditorClick(object sender, RoutedEventArgs e)
    {
        EditorPanel.Visibility = Visibility.Collapsed;
    }

    private void OnActiveChecked(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        if (sender is not RadioButton { Tag: ProfileCard card }) return;
        if (card.Profile.Id == _activeId) return;
        try
        {
            var ok = ProfileService.Instance.SetActive(card.Profile.Id);
            if (!ok)
            {
                ShellState.Status("Activation du profil impossible.");
                LoadProfiles();
                return;
            }
            _activeId = card.Profile.Id;
            ShellState.Status("Profil actif : " + card.Profile.Name);
            LoadProfiles();
        }
        catch (Exception ex)
        {
            Log.Error("UI", "Activation du profil", ex);
            ShellState.Status("Activation impossible : " + ex.Message);
        }
    }

    private void OnDuplicateClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: ProfileCard card }) return;
        try
        {
            var source = card.Profile;
            var copy = new GameProfile
            {
                Id = Guid.NewGuid(),
                GameId = source.GameId,
                GameName = source.GameName,
                Name = source.Name + " (copie)",
                Preset = source.Preset,
                Settings = CloneSettings(source.Settings),
                IsActiveForBoost = false,
                SupportedByConfigPatch = source.SupportedByConfigPatch,
                ConfigFileHint = source.ConfigFileHint
            };
            ProfileService.Instance.Save(copy);
            ShellState.Status("Profil dupliqué : " + copy.Name);
            _editingId = copy.Id;
            LoadProfiles();
            var created = _cards.FirstOrDefault(c => c.Profile.Id == copy.Id);
            if (created is not null) SelectProfile(created, true);
        }
        catch (Exception ex)
        {
            Log.Error("UI", "Duplication du profil", ex);
            ShellState.Status("Duplication impossible : " + ex.Message);
        }
    }

    private static ProfileSettings CloneSettings(ProfileSettings source)
    {
        if (source is null) return new ProfileSettings();
        return new ProfileSettings
        {
            ResolutionWidth = source.ResolutionWidth,
            ResolutionHeight = source.ResolutionHeight,
            TextureQuality = source.TextureQuality,
            ShadowQuality = source.ShadowQuality,
            LightingQuality = source.LightingQuality,
            RayTracing = source.RayTracing,
            Dlss = source.Dlss,
            Fsr = source.Fsr,
            Xess = source.Xess,
            VSync = source.VSync,
            FpsLimit = source.FpsLimit,
            Fullscreen = source.Fullscreen,
            ExtraNotes = source.ExtraNotes ?? string.Empty
        };
    }

    private void LoadEditor(ProfileCard card)
    {
        var settings = card.Profile.Settings ?? new ProfileSettings();
        _loading = true;
        try
        {
            EditorTitle.Text = card.Profile.Name;
            EditorSubTitle.Text = card.PresetLabel + " · " + card.UpdatedLabel;

            var resolution = settings.ResolutionWidth is int w && settings.ResolutionHeight is int h
                ? w + "×" + h
                : ResolutionOptions[0];
            var options = new List<string>(ResolutionOptions);
            if (!options.Contains(resolution)) options.Add(resolution);
            ResolutionCombo.ItemsSource = options;
            ResolutionCombo.SelectedItem = resolution;

            TextureCombo.SelectedItem = settings.TextureQuality ?? "Non défini";
            ShadowCombo.SelectedItem = settings.ShadowQuality ?? "Non défini";
            LightingCombo.SelectedItem = settings.LightingQuality ?? "Non défini";

            RtToggle.IsChecked = settings.RayTracing;
            DlssToggle.IsChecked = settings.Dlss;
            FsrToggle.IsChecked = settings.Fsr;
            XessToggle.IsChecked = settings.Xess;
            VSyncToggle.IsChecked = settings.VSync;
            FullscreenToggle.IsChecked = settings.Fullscreen;

            FpsBox.Text = settings.FpsLimit?.ToString() ?? string.Empty;
            NotesBox.Text = settings.ExtraNotes ?? string.Empty;
        }
        finally
        {
            _loading = false;
        }
        UpdateCapabilityBanner();
    }

    private void OnEditorChanged(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        UpdateCapabilityBanner();
    }

    private async void UpdateCapabilityBanner()
    {
        if (_editing is null || EditorPanel.Visibility != Visibility.Visible)
        {
            CapabilityBanner.Visibility = Visibility.Collapsed;
            return;
        }

        try
        {
            var report = await GetHardwareAsync();
            var capabilities = report?.Gpu?.Capabilities;
            if (capabilities is null)
            {
                CapabilityBanner.Visibility = Visibility.Collapsed;
                return;
            }

            var missing = new List<string>();
            if (RtToggle.IsChecked == true && !capabilities.RayTracing) missing.Add("le ray tracing");
            if (DlssToggle.IsChecked == true && !capabilities.Dlss) missing.Add("DLSS");
            if (FsrToggle.IsChecked == true && !capabilities.Fsr) missing.Add("FSR");
            if (XessToggle.IsChecked == true && !capabilities.Xess) missing.Add("XeSS");

            if (missing.Count == 0)
            {
                CapabilityBanner.Visibility = Visibility.Collapsed;
                return;
            }

            CapabilityText.Text = string.Join(", ", missing) +
                                  " : ce réglage n'est pas disponible sur votre matériel — le jeu l'ignorera probablement.";
            CapabilityBanner.Visibility = Visibility.Visible;
        }
        catch (Exception ex)
        {
            Log.Error("UI", "Lecture des capacités du GPU", ex);
            CapabilityBanner.Visibility = Visibility.Collapsed;
        }
    }

    private async Task<HardwareReport?> GetHardwareAsync()
    {
        var task = _hardwareTask;
        if (task is null)
        {
            try
            {
                task = HardwareDetector.Instance.CollectAsync();
            }
            catch
            {
                return null;
            }
            _hardwareTask = task;
        }

        try
        {
            return await task;
        }
        catch (Exception ex)
        {
            Log.Error("UI", "Collecte du matériel impossible", ex);
            return null;
        }
    }

    private void OnResetEditorClick(object sender, RoutedEventArgs e)
    {
        if (_editing is null) return;
        try
        {
            var stored = ProfileService.Instance.GetProfile(_editing.Id);
            if (stored is null)
            {
                ShellState.Status("Profil introuvable : réinitialisation impossible.");
                return;
            }
            _editing = stored;
            var card = new ProfileCard(stored);
            LoadEditor(card);
            ShellState.Status("Modifications non enregistrées annulées.");
        }
        catch (Exception ex)
        {
            Log.Error("UI", "Réinitialisation du profil", ex);
            ShellState.Status("Réinitialisation impossible : " + ex.Message);
        }
    }

    private void OnSaveProfileClick(object sender, RoutedEventArgs e)
    {
        if (_editing is null) return;
        try
        {
            var settings = _editing.Settings ?? new ProfileSettings();
            _editing.Settings = settings;

            var resolution = ResolutionCombo.SelectedItem as string ?? ResolutionOptions[0];
            if (resolution == ResolutionOptions[0])
            {
                settings.ResolutionWidth = null;
                settings.ResolutionHeight = null;
            }
            else
            {
                var parts = resolution.Split('×');
                if (parts.Length == 2 && int.TryParse(parts[0], out var width) && int.TryParse(parts[1], out var height))
                {
                    settings.ResolutionWidth = width;
                    settings.ResolutionHeight = height;
                }
                else
                {
                    settings.ResolutionWidth = null;
                    settings.ResolutionHeight = null;
                }
            }

            settings.TextureQuality = TextureCombo.SelectedItem as string ?? "Non défini";
            settings.ShadowQuality = ShadowCombo.SelectedItem as string ?? "Non défini";
            settings.LightingQuality = LightingCombo.SelectedItem as string ?? "Non défini";
            settings.RayTracing = RtToggle.IsChecked;
            settings.Dlss = DlssToggle.IsChecked;
            settings.Fsr = FsrToggle.IsChecked;
            settings.Xess = XessToggle.IsChecked;
            settings.VSync = VSyncToggle.IsChecked;
            settings.Fullscreen = FullscreenToggle.IsChecked;
            settings.ExtraNotes = NotesBox.Text ?? string.Empty;

            var fpsText = (FpsBox.Text ?? string.Empty).Trim();
            if (fpsText.Length == 0)
            {
                settings.FpsLimit = null;
            }
            else if (int.TryParse(fpsText, out var fps) && fps > 0)
            {
                settings.FpsLimit = fps;
            }
            else
            {
                settings.FpsLimit = null;
                ShellState.Status("Limite FPS invalide : valeur ignorée, enregistrement effectué sans limite.");
            }

            ProfileService.Instance.Save(_editing);
            ShellState.Status("Profil enregistré : " + _editing.Name);
            LoadProfiles();
        }
        catch (Exception ex)
        {
            Log.Error("UI", "Enregistrement du profil", ex);
            ShellState.Status("Enregistrement impossible : " + ex.Message);
        }
    }

    private async void RefreshApplySection()
    {
        if (_game is null || _editing is null)
        {
            ApplyPanel.Visibility = Visibility.Collapsed;
            return;
        }

        var game = _game;
        ApplyPanel.Visibility = Visibility.Visible;
        ElevationNote.Visibility = AppPaths.IsElevated ? Visibility.Collapsed : Visibility.Visible;
        DryRunButton.IsEnabled = false;
        ApplyButton.IsEnabled = false;
        ApplySupportText.Text = "Analyse des fichiers de configuration de « " + game.Name + " »…";

        try
        {
            var supported = await Task.Run(() => ProfileApplier.Instance.CanApply(game, out var reason)
                ? (true, string.Empty)
                : (false, reason));
            if (_game is null || _editing is null) return;

            if (supported.Item1)
            {
                ApplySupportText.Text = "Configuration prise en charge : GameBoost peut écrire les réglages de ce profil dans les fichiers de « " + game.Name + " ».";
                ApplySupportDetail.Visibility = Visibility.Collapsed;
                DryRunButton.IsEnabled = true;
                ApplyButton.IsEnabled = true;
            }
            else
            {
                ApplySupportText.Text = "Non applicable : " + supported.Item2;
                ApplySupportDetail.Visibility = Visibility.Visible;
            }
        }
        catch (Exception ex)
        {
            Log.Error("UI", "Vérification de la prise en charge du jeu", ex);
            ApplySupportText.Text = "Vérification impossible : " + ex.Message;
        }
    }

    private async void OnDryRunClick(object sender, RoutedEventArgs e)
    {
        if (_game is null || _editing is null) return;
        var game = _game;
        var profile = _editing;
        DryRunButton.IsEnabled = false;
        try
        {
            var result = await Task.Run(() => ProfileApplier.Instance.Apply(profile, game, true));
            ShowApplyResult(result);
            ShellState.Status(result.Message);
        }
        catch (Exception ex)
        {
            Log.Error("UI", "Simulation d'application du profil", ex);
            ApplyMessage.Text = "Simulation impossible : " + ex.Message;
            ApplyMessage.Visibility = Visibility.Visible;
            ShellState.Status("Simulation impossible : " + ex.Message);
        }
        finally
        {
            DryRunButton.IsEnabled = true;
        }
    }

    private async void OnApplyClick(object sender, RoutedEventArgs e)
    {
        if (_game is null || _editing is null) return;
        var game = _game;
        var profile = _editing;

        try
        {
            var preview = await Task.Run(() => ProfileApplier.Instance.Apply(profile, game, true));
            var keys = preview.ChangedKeys.Count > 0
                ? preview.ChangedKeys.Take(12).Aggregate("- ", (current, key) => current + "\n- " + key)
                : "- Aucune modification identifiée pour l'instant";

            var answer = MessageBox.Show(
                "Appliquer le profil « " + profile.Name + " » à « " + game.Name + " » ?\n\n" +
                "Réglages qui seraient écrits :\n" + keys + "\n\n" +
                "Une sauvegarde sera créée avant toute modification, et pourra être restaurée depuis cette page.",
                "Appliquer le profil", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (answer != MessageBoxResult.Yes)
            {
                ShellState.Status("Application annulée : rien n'a été modifié.");
                return;
            }

            ApplyButton.IsEnabled = false;
            var result = await Task.Run(() => ProfileApplier.Instance.Apply(profile, game, false));
            ShowApplyResult(result);
            if (!string.IsNullOrWhiteSpace(result.BackupPath))
            {
                _lastBackupPath = result.BackupPath;
                RevertButton.Visibility = Visibility.Visible;
            }
            ShellState.Status(result.Message);
        }
        catch (Exception ex)
        {
            Log.Error("UI", "Application du profil", ex);
            ApplyMessage.Text = "Application impossible : " + ex.Message;
            ApplyMessage.Visibility = Visibility.Visible;
            ShellState.Status("Application impossible : " + ex.Message);
        }
        finally
        {
            ApplyButton.IsEnabled = true;
        }
    }

    private async void OnRevertClick(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_lastBackupPath))
        {
            ShellState.Status("Aucune sauvegarde à restaurer pour cette session.");
            return;
        }

        var answer = MessageBox.Show(
            "Restaurer les fichiers de configuration enregistrés dans :\n" + _lastBackupPath + "\n\n" +
            "Les modifications appliquées par GameBoost seront annulées.",
            "Restaurer la sauvegarde", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (answer != MessageBoxResult.Yes)
        {
            ShellState.Status("Restauration annulée.");
            return;
        }

        var path = _lastBackupPath;
        try
        {
            RevertButton.IsEnabled = false;
            var result = await Task.Run(() => ProfileApplier.Instance.Revert(path));
            ApplyMessage.Text = result.Message;
            ApplyMessage.Visibility = Visibility.Visible;
            ChangedKeysList.ItemsSource = result.ChangedKeys;
            ChangedKeysScroll.Visibility = result.ChangedKeys.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            ShellState.Status(result.Message);
        }
        catch (Exception ex)
        {
            Log.Error("UI", "Restauration de la sauvegarde", ex);
            ShellState.Status("Restauration impossible : " + ex.Message);
        }
        finally
        {
            RevertButton.IsEnabled = true;
        }
    }

    private void ShowApplyResult(ApplyProfileResult result)
    {
        ApplyMessage.Text = result.Message;
        ApplyMessage.Visibility = Visibility.Visible;
        ChangedKeysList.ItemsSource = result.ChangedKeys;
        ChangedKeysScroll.Visibility = result.ChangedKeys.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

        if (string.IsNullOrWhiteSpace(result.BackupPath))
        {
            BackupPathText.Visibility = Visibility.Collapsed;
        }
        else
        {
            BackupPathText.Text = "Sauvegarde : " + result.BackupPath;
            BackupPathText.Visibility = Visibility.Visible;
            _lastBackupPath = result.BackupPath;
            RevertButton.Visibility = Visibility.Visible;
        }

        if (result.RequiresElevation)
        {
            ElevationNote.Visibility = Visibility.Visible;
        }
    }
}
