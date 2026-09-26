using System.Text;
using System.Text.Json;
using GameBoost.Core.Data;
using GameBoost.Core.Logging;
using GameBoost.Core.Models;

namespace GameBoost.Core.Profiles;

public sealed partial class ProfileApplier
{
    public static ProfileApplier Instance { get; } = new();

    private const string KindFile = "file";
    private const string KindRegistry = "registry";

    private static readonly JsonSerializerOptions ManifestJsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    private ProfileApplier() { }

    public bool CanApply(GameInfo game, out string reason)
    {
        reason = string.Empty;
        if (game is null)
        {
            reason = "Jeu introuvable.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(game.InstallPath) && string.IsNullOrWhiteSpace(game.Name))
        {
            reason = "Aucun dossier d'installation ni nom de jeu exploitable pour ce jeu.";
            return false;
        }

        try
        {
            var targets = LocateTargets(game);
            if (targets.Count == 0)
            {
                reason = "Aucun fichier de configuration pris en charge n'a été trouvé pour ce jeu.";
                return false;
            }
            return true;
        }
        catch (Exception ex)
        {
            Log.Error("Profiles", "Analyse des configurations du jeu impossible", ex);
            reason = "Analyse des fichiers de configuration impossible : " + ex.Message;
            return false;
        }
    }

    public ApplyProfileResult Apply(GameProfile profile, GameInfo game, bool dryRun = false)
    {
        var result = new ApplyProfileResult();
        if (profile is null)
        {
            result.Message = "Profil introuvable.";
            return result;
        }
        if (game is null)
        {
            result.Message = "Jeu introuvable.";
            return result;
        }

        List<ConfigTarget> targets;
        try
        {
            targets = LocateTargets(game);
        }
        catch (Exception ex)
        {
            Log.Error("Profiles", "Localisation des configurations impossible", ex);
            result.Message = "Localisation des fichiers de configuration impossible : " + ex.Message;
            return result;
        }

        if (targets.Count == 0)
        {
            Log.Warn("Profiles", "Aucune configuration applicable pour le jeu " + game.Name);
            result.Message = "Aucun fichier de configuration pris en charge n'a été trouvé pour ce jeu.";
            return result;
        }

        var desired = ProfileMappings.BuildDesired(profile.Settings);
        var plans = new List<TargetPlan>();
        var errors = new List<string>();
        var changedKeys = new List<string>();
        var recognizedTotal = 0;
        var applicableTotal = 0;
        var changesTotal = 0;

        foreach (var target in targets)
        {
            try
            {
                var plan = BuildPlan(target, desired);
                if (plan is null) continue;
                recognizedTotal += plan.Recognized;
                applicableTotal += plan.Applicable;
                if (plan.Edits.Count == 0) continue;
                plans.Add(plan);
                changesTotal += plan.Edits.Count;
                foreach (var edit in plan.Edits)
                {
                    var label = ProfileMappings.Label(edit.Canonical);
                    if (!changedKeys.Contains(label)) changedKeys.Add(label);
                }
            }
            catch (Exception ex)
            {
                Log.Error("Profiles", "Analyse de " + target.Path + " impossible", ex);
                errors.Add(Path.GetFileName(target.Path) + " : " + ex.Message);
            }
        }

        if (changesTotal == 0)
        {
            if (applicableTotal > 0)
            {
                result.Success = true;
                result.Message = "Profil déjà appliqué : aucune modification nécessaire.";
            }
            else if (recognizedTotal > 0)
            {
                result.Message = "Aucune clé prise en charge par ce profil dans les fichiers de configuration trouvés.";
                Log.Warn("Profiles", result.Message + " (" + game.Name + ")");
            }
            else
            {
                result.Message = targets.Count > 1
                    ? "Aucune clé reconnue dans les fichiers de configuration trouvés."
                    : "Aucune clé reconnue dans ce fichier.";
                Log.Warn("Profiles", result.Message + " (" + game.Name + ")");
            }
            return result;
        }

        if (dryRun)
        {
            result.Success = true;
            result.ChangedKeys = changedKeys;
            result.Message = "Simulation : " + (changesTotal <= 1
                ? "1 paramètre serait modifié"
                : changesTotal + " paramètres seraient modifiés") + " (aucune écriture effectuée).";
            Log.Info("Profiles", "Simulation du profil " + profile.Name + " : " + changesTotal + " modification(s)");
            return result;
        }

        string slug;
        string backupFolder;
        try
        {
            slug = Slugify(game.Name);
            backupFolder = AppPaths.CreateBackupFolder("profile-" + slug);
        }
        catch (Exception ex)
        {
            Log.Error("Profiles", "Création du dossier de sauvegarde impossible", ex);
            result.Message = "Impossible de créer le dossier de sauvegarde : " + ex.Message;
            return result;
        }

        result.BackupPath = backupFolder;
        result.ChangedKeys = changedKeys;

        var manifest = new BackupManifest
        {
            CreatedAt = DateTime.Now,
            GameName = game.Name,
            Slug = slug
        };

        var index = 0;
        foreach (var plan in plans)
        {
            try
            {
                if (plan.Target.Kind == KindFile)
                {
                    var relative = BackupRelativePath(plan.Target.Path, game.InstallPath, index);
                    var destination = Path.Combine(backupFolder, relative);
                    var directory = Path.GetDirectoryName(destination);
                    if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
                    File.Copy(plan.Target.Path, destination, true);
                    manifest.Entries.Add(new ManifestEntry
                    {
                        Type = KindFile,
                        RelativePath = relative,
                        OriginalPath = plan.Target.Path
                    });
                }
                else
                {
                    var relative = Path.Combine("registry", index + ".reg");
                    var destination = Path.Combine(backupFolder, relative);
                    var directory = Path.GetDirectoryName(destination);
                    if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
                    var export = RunProcess("reg.exe", "export \"" + FullKeyPath(plan.Target.Path) + "\" \"" + destination + "\" /y");
                    if (export.ExitCode != 0) throw new InvalidOperationException(export.Output.Trim());
                    manifest.Entries.Add(new ManifestEntry
                    {
                        Type = KindRegistry,
                        RelativePath = relative,
                        OriginalPath = FullKeyPath(plan.Target.Path)
                    });
                }
            }
            catch (Exception ex)
            {
                Log.Error("Profiles", "Sauvegarde de " + plan.Target.Path + " impossible", ex);
                errors.Add(Path.GetFileName(plan.Target.Path) + " : " + ex.Message);
            }
            index++;
        }

        if (errors.Count > 0)
        {
            result.Message = "Sauvegarde incomplète : aucune modification effectuée (" + errors.Count + " erreur(s)). " + string.Join(" ", errors);
            Log.Error("Profiles", result.Message);
            return result;
        }

        try
        {
            var json = JsonSerializer.Serialize(manifest, ManifestJsonOptions);
            File.WriteAllText(Path.Combine(backupFolder, "manifest.json"), json);
        }
        catch (Exception ex)
        {
            Log.Error("Profiles", "Écriture du manifeste de sauvegarde impossible", ex);
            result.Message = "Écriture du manifeste de sauvegarde impossible : " + ex.Message + " (aucune modification effectuée)";
            return result;
        }

        var applied = 0;
        foreach (var plan in plans)
        {
            try
            {
                if (plan.Target.Kind == KindFile)
                {
                    var bytes = EncodeText(plan.ModifiedText ?? plan.OriginalText ?? string.Empty, plan.Encoding);
                    File.WriteAllBytes(plan.Target.Path, bytes);
                }
                else
                {
                    ApplyRegistryEdits(plan);
                }
                applied += plan.Edits.Count;
            }
            catch (UnauthorizedAccessException ex)
            {
                Log.Error("Profiles", "Accès refusé : " + plan.Target.Path, ex);
                result.RequiresElevation = true;
                errors.Add("Accès refusé : " + plan.Target.Path);
            }
            catch (Exception ex)
            {
                Log.Error("Profiles", "Écriture de " + plan.Target.Path + " impossible", ex);
                errors.Add(Path.GetFileName(plan.Target.Path) + " : " + ex.Message);
            }
        }

        var savedCount = manifest.Entries.Count;
        var savedLabel = savedCount <= 1 ? "(fichier sauvegardé)" : "(fichiers sauvegardés)";

        if (errors.Count == 0 && applied == changesTotal)
        {
            result.Success = true;
            result.Message = "Profil appliqué : " + CountParams(changesTotal) + " " + savedLabel;
            Log.Info("Profiles", result.Message + " [" + profile.Name + "]");
            return result;
        }

        result.Success = false;
        result.Message = "Application partielle : " + applied + " sur " + changesTotal + " paramètre(s) modifié(s) " + savedLabel +
                         (errors.Count > 0 ? ". Erreurs : " + string.Join(" ", errors) : string.Empty);
        Log.Warn("Profiles", result.Message + " [" + profile.Name + "]");
        return result;
    }

    public ApplyProfileResult Revert(string backupFolderPath)
    {
        var result = new ApplyProfileResult();
        if (string.IsNullOrWhiteSpace(backupFolderPath))
        {
            result.Message = "Dossier de sauvegarde non spécifié.";
            return result;
        }

        string folder;
        try
        {
            folder = Path.GetFullPath(backupFolderPath);
        }
        catch (Exception ex)
        {
            result.Message = "Chemin de sauvegarde invalide : " + ex.Message;
            return result;
        }

        if (!Directory.Exists(folder))
        {
            result.Message = "Dossier de sauvegarde introuvable : " + folder;
            return result;
        }

        var manifestPath = Path.Combine(folder, "manifest.json");
        if (!File.Exists(manifestPath))
        {
            result.Message = "Fichier manifest.json introuvable dans ce dossier de sauvegarde.";
            return result;
        }

        BackupManifest? manifest;
        try
        {
            manifest = JsonSerializer.Deserialize<BackupManifest>(File.ReadAllText(manifestPath), ManifestJsonOptions);
        }
        catch (Exception ex)
        {
            Log.Error("Profiles", "Lecture du manifeste impossible", ex);
            result.Message = "Manifeste de sauvegarde illisible : " + ex.Message;
            return result;
        }

        if (manifest is null || manifest.Entries.Count == 0)
        {
            result.Message = "Aucun élément à restaurer dans ce dossier de sauvegarde.";
            return result;
        }

        result.BackupPath = folder;
        var errors = new List<string>();
        var restored = 0;
        var root = folder.EndsWith(Path.DirectorySeparatorChar) ? folder : folder + Path.DirectorySeparatorChar;

        foreach (var entry in manifest.Entries)
        {
            try
            {
                var source = Path.GetFullPath(Path.Combine(folder, entry.RelativePath));
                if (!source.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("Chemin de sauvegarde hors du dossier autorisé.");

                if (string.Equals(entry.Type, KindRegistry, StringComparison.OrdinalIgnoreCase))
                {
                    if (!File.Exists(source)) throw new FileNotFoundException("Copie de sauvegarde introuvable.", source);
                    var imported = RunProcess("reg.exe", "import \"" + source + "\"");
                    if (imported.ExitCode != 0) throw new InvalidOperationException(imported.Output.Trim());
                    restored++;
                    result.ChangedKeys.Add("Registre restauré : " + entry.OriginalPath);
                }
                else
                {
                    if (!File.Exists(source)) throw new FileNotFoundException("Copie de sauvegarde introuvable.", source);
                    var destinationDirectory = Path.GetDirectoryName(entry.OriginalPath);
                    if (!string.IsNullOrEmpty(destinationDirectory)) Directory.CreateDirectory(destinationDirectory);
                    File.Copy(source, entry.OriginalPath, true);
                    restored++;
                    result.ChangedKeys.Add("Fichier restauré : " + Path.GetFileName(entry.OriginalPath));
                }
            }
            catch (Exception ex)
            {
                Log.Error("Profiles", "Restauration de " + entry.OriginalPath + " impossible", ex);
                errors.Add(Path.GetFileName(entry.OriginalPath) + " : " + ex.Message);
            }
        }

        if (errors.Count == 0)
        {
            result.Success = true;
            result.Message = restored == 1 ? "Restauration terminée : 1 élément restauré." : "Restauration terminée : " + restored + " éléments restaurés.";
            Log.Info("Profiles", result.Message);
            return result;
        }

        result.Success = false;
        result.Message = "Restauration incomplète : " + restored + " réussi(s), " + errors.Count + " échec(s). " +
                         string.Join(" ", errors.Take(3));
        return result;
    }

    public List<GameSettingsMapping> GetKnownMappings()
    {
        var unrealAliases = new Dictionary<string, string>(ProfileMappings.Aliases, StringComparer.OrdinalIgnoreCase);
        var unityAliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Screenmanager Resolution Width_px"] = ProfileMappings.KResWidth,
            ["Screenmanager Resolution Height_px"] = ProfileMappings.KResHeight,
            ["Is Fullscreen mode"] = ProfileMappings.KFullscreen
        };

        return new List<GameSettingsMapping>
        {
            new()
            {
                Platform = "Unreal Engine",
                Format = "ini",
                ConfigFilePatterns = (string[])ProfileMappings.UnrealFilePatterns.Clone(),
                KeyAliases = unrealAliases
            },
            new()
            {
                Platform = "Unity",
                Format = "registry",
                ConfigFilePatterns = new[] { @"HKCU\Software\<Societe>\<Produit>" },
                KeyAliases = unityAliases
            },
            new()
            {
                Platform = "Générique",
                Format = "ini",
                ConfigFilePatterns = new[]
                {
                    "<DossierInstallation>\\*.ini",
                    "<DossierInstallation>\\*.cfg",
                    "%LOCALAPPDATA%\\<NomDuJeu>\\**\\*.ini",
                    "%USERPROFILE%\\Documents\\<NomDuJeu>\\**\\*.ini"
                },
                KeyAliases = new Dictionary<string, string>(ProfileMappings.Aliases, StringComparer.OrdinalIgnoreCase)
            }
        };
    }

    private static string CountParams(int count)
    {
        return count <= 1 ? "1 paramètre modifié" : count + " paramètres modifiés";
    }

    private static string FullKeyPath(string keyPath)
    {
        if (string.IsNullOrWhiteSpace(keyPath)) return keyPath;
        if (keyPath.StartsWith("HKEY_", StringComparison.OrdinalIgnoreCase) ||
            keyPath.StartsWith("HKCU", StringComparison.OrdinalIgnoreCase) ||
            keyPath.StartsWith("HKLM", StringComparison.OrdinalIgnoreCase))
            return keyPath;
        return "HKCU\\" + keyPath.TrimStart('\\');
    }

    private static string Slugify(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "jeu";
        var normalized = ProfileMappings.Normalize(value);
        var builder = new StringBuilder(normalized.Length);
        foreach (var c in normalized)
        {
            if (char.IsLetterOrDigit(c)) builder.Append(c);
            else if (c is ' ' or '_' or '.') builder.Append('-');
        }
        var slug = builder.ToString();
        while (slug.Contains("--", StringComparison.Ordinal)) slug = slug.Replace("--", "-");
        slug = slug.Trim('-');
        return slug.Length == 0 ? "jeu" : slug;
    }

    private static string BackupRelativePath(string originalPath, string installPath, int index)
    {
        var fileName = Path.GetFileName(originalPath);
        if (!string.IsNullOrWhiteSpace(installPath))
        {
            try
            {
                var relative = Path.GetRelativePath(installPath, originalPath);
                if (!relative.StartsWith("..", StringComparison.Ordinal) && !Path.IsPathRooted(relative))
                    return Path.Combine("game", relative);
            }
            catch (Exception ex)
            {
                Log.Warn("Profiles", "Chemin relatif de sauvegarde calculable : " + ex.Message);
            }
        }
        return Path.Combine("files", index + "-" + fileName);
    }

    private static byte[] EncodeText(string text, Encoding encoding)
    {
        var body = encoding.GetBytes(text);
        var preamble = encoding.GetPreamble();
        if (preamble.Length == 0) return body;
        var result = new byte[preamble.Length + body.Length];
        preamble.CopyTo(result, 0);
        body.CopyTo(result, preamble.Length);
        return result;
    }
}
