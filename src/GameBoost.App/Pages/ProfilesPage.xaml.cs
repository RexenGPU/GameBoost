using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using GameBoost.App.Pages.Profiles;
using GameBoost.App.Services;
using GameBoost.Core.Data;
using GameBoost.Core.Games;
using GameBoost.Core.Hardware;
using GameBoost.Core.Localization;
using GameBoost.Core.Logging;
using GameBoost.Core.Models;
using GameBoost.Core.Profiles;

namespace GameBoost.App.Pages;

public partial class ProfilesPage : UserControl
{
    private static readonly string[] ResolutionOptions =
        { "KEEP", "1920×1080", "2560×1440", "3840×2160", "1280×720" };

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
        RefreshResolutionOptions(null);
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

    private void RefreshResolutionOptions(string? extra)
    {
        var options = new List<string> { Loc.T("Prof_ResolutionKeep") };
        for (var i = 1; i < ResolutionOptions.Length; i++) options.Add(ResolutionOptions[i]);
        if (extra is not null && !options.Contains(extra)) options.Add(extra);
        ResolutionCombo.ItemsSource = options;
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
                ShellState.Status(Loc.T("Prof_StatusNoGames"));
                return;
            }
            if (GameCombo.SelectedIndex < 0) GameCombo.SelectedIndex = 0;
            ShellState.Status(Loc.T("Prof_StatusGamesCount", _games.Count));
        }
        catch (Exception ex)
        {
            Log.Error("UI", "Chargement des jeux pour les profils", ex);
            ShellState.Status(Loc.T("Prof_StatusGamesFail", ex.Message));
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
            ShellState.Status(Loc.T("Prof_StatusProfilesFail", ex.Message));
        }
    }

    private void OnCreatePresetsClick(object sender, RoutedEventArgs e)
    {
        if (_game is null) return;
        try
        {
            var created = ProfileService.Instance.CreatePresets(_game.Id, _game.Name);
            ShellState.Status(created.Count <= 1
                ? Loc.T("Prof_StatusCreated1", created.Count)
                : Loc.T("Prof_StatusCreatedMany", created.Count, _game.Name));
            LoadProfiles();
        }
        catch (Exception ex)
        {
            Log.Error("UI", "Création des profils par défaut", ex);
            ShellState.Status(Loc.T("Prof_StatusCreateFail", ex.Message));
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
                ShellState.Status(Loc.T("Prof_StatusActiveFail"));
                LoadProfiles();
                return;
            }
            _activeId = card.Profile.Id;
            ShellState.Status(Loc.T("Prof_StatusActive", card.Profile.Name));
            LoadProfiles();
        }
        catch (Exception ex)
        {
            Log.Error("UI", "Activation du profil", ex);
            ShellState.Status(Loc.T("Prof_StatusActiveFail2", ex.Message));
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
                Name = source.Name + Loc.T("Prof_CopySuffix"),
                Preset = source.Preset,
                Settings = CloneSettings(source.Settings),
                IsActiveForBoost = false,
                SupportedByConfigPatch = source.SupportedByConfigPatch,
                ConfigFileHint = source.ConfigFileHint
            };
            ProfileService.Instance.Save(copy);
            ShellState.Status(Loc.T("Prof_StatusDuplicated", copy.Name));
            _editingId = copy.Id;
            LoadProfiles();
            var created = _cards.FirstOrDefault(c => c.Profile.Id == copy.Id);
            if (created is not null) SelectProfile(created, true);
        }
        catch (Exception ex)
        {
            Log.Error("UI", "Duplication du profil", ex);
            ShellState.Status(Loc.T("Prof_StatusDuplicateFail", ex.Message));
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
                : Loc.T("Prof_ResolutionKeep");
            RefreshResolutionOptions(resolution);
            ResolutionCombo.SelectedItem = resolution;

            TextureCombo.SelectedItem = settings.TextureQuality ?? QualityOptions[0];
            ShadowCombo.SelectedItem = settings.ShadowQuality ?? QualityOptions[0];
            LightingCombo.SelectedItem = settings.LightingQuality ?? QualityOptions[0];

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
            if (RtToggle.IsChecked == true && !capabilities.RayTracing) missing.Add(Loc.T("Prof_CapRt"));
            if (DlssToggle.IsChecked == true && !capabilities.Dlss) missing.Add("DLSS");
            if (FsrToggle.IsChecked == true && !capabilities.Fsr) missing.Add("FSR");
            if (XessToggle.IsChecked == true && !capabilities.Xess) missing.Add("XeSS");

            if (missing.Count == 0)
            {
                CapabilityBanner.Visibility = Visibility.Collapsed;
                return;
            }

            CapabilityText.Text = string.Join(", ", missing) + Loc.T("Prof_CapabilitySuffix");
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
                ShellState.Status(Loc.T("Prof_StatusProfileMissing"));
                return;
            }
            _editing = stored;
            var card = new ProfileCard(stored);
            LoadEditor(card);
            ShellState.Status(Loc.T("Prof_StatusResetDone"));
        }
        catch (Exception ex)
        {
            Log.Error("UI", "Réinitialisation du profil", ex);
            ShellState.Status(Loc.T("Prof_StatusResetFail", ex.Message));
        }
    }

    private void OnSaveProfileClick(object sender, RoutedEventArgs e)
    {
        if (_editing is null) return;
        try
        {
            var settings = _editing.Settings ?? new ProfileSettings();
            _editing.Settings = settings;

            var resolution = ResolutionCombo.SelectedItem as string ?? Loc.T("Prof_ResolutionKeep");
            if (resolution == Loc.T("Prof_ResolutionKeep"))
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

            settings.TextureQuality = TextureCombo.SelectedItem as string ?? QualityOptions[0];
            settings.ShadowQuality = ShadowCombo.SelectedItem as string ?? QualityOptions[0];
            settings.LightingQuality = LightingCombo.SelectedItem as string ?? QualityOptions[0];
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
                ShellState.Status(Loc.T("Prof_StatusBadFps"));
            }

            ProfileService.Instance.Save(_editing);
            ShellState.Status(Loc.T("Prof_StatusSaved", _editing.Name));
            LoadProfiles();
        }
        catch (Exception ex)
        {
            Log.Error("UI", "Enregistrement du profil", ex);
            ShellState.Status(Loc.T("Prof_StatusSaveFail", ex.Message));
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
        ApplySupportText.Text = Loc.T("Prof_ApplyChecking", game.Name);

        try
        {
            var supported = await Task.Run(() => ProfileApplier.Instance.CanApply(game, out var reason)
                ? (true, string.Empty)
                : (false, reason));
            if (_game is null || _editing is null) return;

            if (supported.Item1)
            {
                ApplySupportText.Text = Loc.T("Prof_ApplySupported", game.Name);
                ApplySupportDetail.Visibility = Visibility.Collapsed;
                DryRunButton.IsEnabled = true;
                ApplyButton.IsEnabled = true;
            }
            else
            {
                ApplySupportText.Text = Loc.T("Prof_ApplyUnsupported", supported.Item2);
                ApplySupportDetail.Visibility = Visibility.Visible;
            }
        }
        catch (Exception ex)
        {
            Log.Error("UI", "Vérification de la prise en charge du jeu", ex);
            ApplySupportText.Text = Loc.T("Prof_ApplyCheckFail", ex.Message);
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
            ApplyMessage.Text = Loc.T("Prof_DryRunFail", ex.Message);
            ApplyMessage.Visibility = Visibility.Visible;
            ShellState.Status(Loc.T("Prof_DryRunFail", ex.Message));
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
                : "- " + Loc.T("Prof_NoChanges");

            var answer = MessageBox.Show(
                Loc.T("Prof_ApplyBody", profile.Name, game.Name, keys),
                Loc.T("Prof_ApplyTitle2"), MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (answer != MessageBoxResult.Yes)
            {
                ShellState.Status(Loc.T("Prof_StatusApplyCancelled"));
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
            ApplyMessage.Text = Loc.T("Prof_ApplyFail", ex.Message);
            ApplyMessage.Visibility = Visibility.Visible;
            ShellState.Status(Loc.T("Prof_ApplyFail", ex.Message));
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
            ShellState.Status(Loc.T("Prof_NoBackup"));
            return;
        }

        var answer = MessageBox.Show(
            Loc.T("Prof_RevertBody", _lastBackupPath),
            Loc.T("Prof_RevertTitle"), MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (answer != MessageBoxResult.Yes)
        {
            ShellState.Status(Loc.T("Prof_StatusRevertCancelled"));
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
            ShellState.Status(Loc.T("Prof_StatusRevertFail", ex.Message));
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
            BackupPathText.Text = Loc.T("Prof_BackupLabel", result.BackupPath);
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
