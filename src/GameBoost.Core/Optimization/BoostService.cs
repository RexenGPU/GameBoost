using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.Json;
using GameBoost.Core.Analysis;
using GameBoost.Core.Data;
using GameBoost.Core.Games;
using GameBoost.Core.Logging;
using GameBoost.Core.Models;
using GameBoost.Core.Monitoring;
using GameBoost.Core.Overlay;
using GameBoost.Core.Profiles;
using GameBoost.Core.Processes;

namespace GameBoost.Core.Optimization;

public sealed class BoostService
{
    private const string GameModeKeyNote = "gamemode";
    private const string PowerPlanKeyNote = "powerplan";
    private const string OverlayKeyNote = "overlay-prev";
    private const string OverlayGameKeyNote = "overlay-game-prev";
    private const string ProfileBackupKeyNote = "profile-backup";
    private const string PriorityKeyPrefix = "priority:";

    private readonly object _sync = new();
    private BoostSessionState? _session;
    private bool _pendingResolved;
    private GameInfo? _plannedGame;

    public static BoostService Instance { get; } = new();

    private BoostService()
    {
    }

    public BoostSessionState? ActiveSession
    {
        get { lock (_sync) return _session is { IsActive: true } ? _session : null; }
    }

    public bool HasPendingSession
    {
        get
        {
            EnsurePendingResolved();
            return _session is { IsActive: true };
        }
    }

    public BoostSessionState? LoadPendingSession()
    {
        EnsurePendingResolved();
        return _session;
    }

    public BoostPlan BuildPlan(GameInfo? game, IReadOnlyCollection<int> processIdsToClose, bool enableGameMode, bool enablePowerPlan, bool releaseMemory, bool applyProfile, bool enableOverlay, bool clearTempFiles)
    {
        var plan = new BoostPlan();
        if (game is not null)
        {
            plan.GameId = game.Id;
            plan.GameName = game.Name;
        }
        plan.ElevationAvailable = AppPaths.IsElevated;
        lock (_sync) _plannedGame = game;

        var gameProcessId = ResolveGameProcessId(game);
        var profile = applyProfile && game is not null ? ProfileService.Instance.GetActiveProfile(game.Id) : null;
        plan.ProfileName = profile?.Name;

        plan.Steps.Add(BuildCloseStep(processIdsToClose, gameProcessId));
        plan.Steps.Add(BuildPriorityStep(game, gameProcessId));
        plan.Steps.Add(BuildGameModeStep(enableGameMode));
        plan.Steps.Add(BuildPowerPlanStep(enablePowerPlan));
        plan.Steps.Add(BuildMemoryStep(releaseMemory, gameProcessId));
        plan.Steps.Add(BuildProfileStep(game, profile, applyProfile));
        plan.Steps.Add(BuildTempStep(clearTempFiles));
        plan.Steps.Add(BuildOverlayStep(enableOverlay));

        Log.Info("Boost", "Plan construit pour « " + plan.GameName + " » : " +
                          plan.Steps.Count(s => s.Enabled) + " étape(s) active(s) sur " + plan.Steps.Count);
        return plan;
    }

    public Task<BoostSessionState> ExecuteAsync(BoostPlan plan, IProgress<string>? progress = null, CancellationToken ct = default)
    {
        return Task.Run(() => ExecuteCore(plan, progress, ct));
    }

    public Task<BoostSessionState> EndSessionAsync()
    {
        return Task.Run(EndSessionCore);
    }

    private BoostSessionState ExecuteCore(BoostPlan plan, IProgress<string>? progress, CancellationToken ct)
    {
        if (plan is null)
        {
            var invalid = new BoostSessionState { IsActive = false, EndedAt = DateTime.Now };
            invalid.Results.Add(new BoostStepResult { Title = "Boost", Success = false, Message = "Aucun plan fourni, rien n'a été modifié." });
            Log.Warn("Boost", "Exécution refusée : plan vide");
            return invalid;
        }

        var session = new BoostSessionState
        {
            SessionId = Guid.NewGuid(),
            GameId = plan.GameId,
            GameName = plan.GameName,
            StartedAt = DateTime.Now,
            IsActive = true
        };

        lock (_sync)
        {
            _session = session;
            _pendingResolved = true;
        }
        BoostStore.Upsert(session);
        Report(progress, "Démarrage du boost" + (string.IsNullOrWhiteSpace(plan.GameName) ? string.Empty : " pour « " + plan.GameName + " »"));

        var interrupted = false;
        foreach (var step in plan.Steps)
        {
            if (step is null) continue;
            if (ct.IsCancellationRequested)
            {
                interrupted = true;
                break;
            }

            if (!step.Enabled)
            {
                session.Results.Add(new BoostStepResult
                {
                    StepId = step.Id,
                    Title = step.Title,
                    Success = true,
                    Skipped = true,
                    Message = "Étape ignorée : " + (string.IsNullOrWhiteSpace(step.Detail) ? "non sélectionnée." : step.Detail)
                });
                BoostStore.Upsert(session);
                continue;
            }

            BoostStepResult result;
            try
            {
                result = ExecuteStep(plan, session, step);
            }
            catch (Exception ex)
            {
                Log.Error("Boost", "Étape « " + step.Title + " » en échec", ex);
                result = new BoostStepResult
                {
                    StepId = step.Id,
                    Title = step.Title,
                    Success = false,
                    Message = "Échec de l'étape : " + ex.Message
                };
            }

            session.Results.Add(result);
            BoostStore.Upsert(session);
            Report(progress, (result.Success ? "Réussite : " : "Échec : ") + result.Title + " — " + result.Message);
        }

        if (interrupted)
        {
            session.Results.Add(new BoostStepResult
            {
                Title = "Boost interrompu",
                Success = false,
                Message = "Session interrompue par l'utilisateur : les modifications restent actives jusqu'à la fin de session."
            });
        }

        TrackGame(plan, session);
        BoostStore.Upsert(session);
        Log.Info("Boost", "Session " + session.SessionId + " exécutée : " + session.AppliedOptimizations.Count + " optimisation(s) appliquée(s)");
        return session;
    }

    private BoostStepResult ExecuteStep(BoostPlan plan, BoostSessionState session, BoostStep step)
    {
        switch (step.Kind)
        {
            case BoostActionKind.CloseProcesses:
                return ExecuteClose(session, step);
            case BoostActionKind.SetProcessPriority:
                return ExecutePriority(session, step);
            case BoostActionKind.EnableGameMode:
                return ExecuteGameMode(session, step);
            case BoostActionKind.SetPowerPlan:
                return ExecutePowerPlan(session, step);
            case BoostActionKind.ReleaseMemory:
                return ExecuteReleaseMemory(plan, session, step);
            case BoostActionKind.ApplyProfile:
                return ExecuteProfile(plan, session, step);
            case BoostActionKind.ClearTempFiles:
                return ExecuteClearTemp(session, step);
            case BoostActionKind.ToggleOverlay:
                return ExecuteOverlay(session, step);
            default:
                return Failed(step, "Type d'action non pris en charge : " + step.Kind);
        }
    }

    private BoostStepResult ExecuteClose(BoostSessionState session, BoostStep step)
    {
        var pids = ParsePids(step.Payload);
        if (pids.Count == 0) return Skipped(step, "Aucune application sélectionnée.");

        var candidates = new List<(int Pid, string Name, string Path)>();
        foreach (var pid in pids)
        {
            var snapshot = ProcessService.Instance.GetProcess(pid);
            if (snapshot is null)
            {
                Log.Warn("Boost", "Processus " + pid + " introuvable, fermeture ignorée");
                continue;
            }
            if (!ProcessService.Instance.CanClose(snapshot, out var reason))
            {
                Log.Warn("Boost", "Fermeture ignorée pour " + snapshot.Name + " (" + pid + ") : " + reason);
                continue;
            }
            candidates.Add((pid, snapshot.Name, snapshot.Path ?? string.Empty));
        }

        if (candidates.Count == 0) return Skipped(step, "Aucune des applications sélectionnées ne peut être fermée.");

        var (closed, errors) = ProcessService.Instance.CloseMany(candidates.Select(c => c.Pid));
        var failedPids = ExtractFailedPids(errors);
        foreach (var candidate in candidates)
        {
            if (failedPids.Contains(candidate.Pid)) continue;
            if (!string.IsNullOrWhiteSpace(candidate.Name) && !session.ClosedProcesses.Contains(candidate.Name))
                session.ClosedProcesses.Add(candidate.Name);
            if (!string.IsNullOrWhiteSpace(candidate.Path) &&
                !session.RelaunchPaths.Contains(candidate.Path, StringComparer.OrdinalIgnoreCase))
                session.RelaunchPaths.Add(candidate.Path);
        }

        if (closed > 0)
        {
            session.AppliedOptimizations.Add("Applications fermées (" + closed + ")");
            if (SettingsService.Current.RelaunchClosedApps && session.RelaunchPaths.Count > 0)
                step.RevertHint = "L'application sera relancée à la fin de la session";
        }

        var message = closed == 1 ? "1 application fermée." : closed + " applications fermées.";
        if (errors.Count > 0) message += " " + string.Join(" ", errors);
        return new BoostStepResult { StepId = step.Id, Title = step.Title, Success = closed > 0, Message = message };
    }

    private BoostStepResult ExecutePriority(BoostSessionState session, BoostStep step)
    {
        if (!int.TryParse(step.Payload, NumberStyles.Integer, CultureInfo.InvariantCulture, out var pid) || pid <= 0)
            return Failed(step, "Identifiant de processus du jeu invalide.");

        var handle = BoostNative.OpenForPriority(pid);
        if (handle == IntPtr.Zero)
        {
            var error = Marshal.GetLastWin32Error();
            return Failed(step, "Impossible d'ouvrir le processus du jeu (code " + error + ").");
        }

        try
        {
            var previous = BoostNative.GetPriorityClass(handle);
            if (previous == 0) return Failed(step, "Impossible de lire la priorité actuelle du processus.");
            if (!BoostNative.SetProcessPriorityClass(handle, BoostNative.HighPriorityClass))
            {
                var error = Marshal.GetLastWin32Error();
                return Failed(step, "Passage en priorité haute refusé (code " + error + ").");
            }

            session.RevertData[PriorityKeyPrefix + pid] = previous.ToString(CultureInfo.InvariantCulture);
            var name = ProcessService.Instance.GetProcess(pid)?.Name ?? "jeu";
            session.AppliedOptimizations.Add("Priorité haute : " + name);
            return Success(step, "Priorité de « " + name + " » passée de " + BoostNative.PriorityLabel(previous) + " à Haute.");
        }
        finally
        {
            BoostNative.CloseHandle(handle);
        }
    }

    private BoostStepResult ExecuteGameMode(BoostSessionState session, BoostStep step)
    {
        var (readOk, present, value) = AnalysisNative.ReadGameMode();
        if (!readOk) return Failed(step, "Lecture du registre impossible, aucune modification effectuée.");

        try
        {
            AnalysisNative.WriteGameMode(1);
        }
        catch (Exception ex)
        {
            Log.Error("Boost", "Activation du Mode Jeu Windows impossible", ex);
            return Failed(step, "Activation du Mode Jeu Windows impossible : " + ex.Message);
        }

        session.RevertData[GameModeKeyNote] = present ? value.ToString(CultureInfo.InvariantCulture) : "absent";
        session.AppliedOptimizations.Add("Mode Jeu Windows activé");
        var previous = present ? value.ToString(CultureInfo.InvariantCulture) : "absent";
        return Success(step, "Mode Jeu Windows activé (ancienne valeur : " + previous + ").");
    }

    private BoostStepResult ExecutePowerPlan(BoostSessionState session, BoostStep step)
    {
        var (readExit, readOutput) = AnalysisNative.RunCommand("powercfg.exe", "/getactivescheme");
        if (readExit != 0) return Failed(step, "Lecture du plan d'alimentation actif impossible (code " + readExit + ").");
        var previousGuid = AnalysisNative.ExtractGuid(readOutput);
        if (previousGuid.Length == 0) return Failed(step, "Plan d'alimentation actif non identifiable.");

        var attempt = AnalysisNative.RunCommand("powercfg.exe", "/setactive " + AnalysisNative.HighPerformanceScheme);
        if (attempt.ExitCode != 0)
        {
            Log.Error("Boost", "Bascule du plan d'alimentation refusée (code " + attempt.ExitCode + ")");
            return Failed(step, "Bascule vers « Hautes performances » refusée par Windows (code " + attempt.ExitCode + ").");
        }

        session.RevertData[PowerPlanKeyNote] = previousGuid;
        session.AppliedOptimizations.Add("Plan « Hautes performances » activé");
        var previousLabel = AnalysisNative.DescribeScheme(previousGuid);
        if (string.IsNullOrWhiteSpace(previousLabel)) previousLabel = previousGuid;
        return Success(step, "Plan « Hautes performances » activé (ancien plan « " + previousLabel + " » restauré à la fin).");
    }

    private BoostStepResult ExecuteReleaseMemory(BoostPlan plan, BoostSessionState session, BoostStep step)
    {
        var neverClose = new HashSet<string>(ProcessService.Instance.GetNeverCloseList(), StringComparer.OrdinalIgnoreCase);
        var gameProcessId = ResolveGameProcessId(ResolveGame(plan));
        var ownProcessId = Environment.ProcessId;
        var targets = new List<ProcessSnapshot>();
        foreach (var snapshot in ProcessService.Instance.GetProcesses())
        {
            if (snapshot.ProcessId <= 4) continue;
            if (snapshot.ProcessId == gameProcessId) continue;
            if (snapshot.ProcessId == ownProcessId) continue;
            if (snapshot.IsCritical) continue;
            if (neverClose.Contains(snapshot.Name)) continue;
            if (snapshot.MemoryBytes <= 0) continue;
            targets.Add(snapshot);
        }

        if (targets.Count == 0) return Skipped(step, "Aucun processus libérable hors applications protégées.");

        var released = 0;
        long freed = 0;
        foreach (var target in targets)
        {
            var handle = BoostNative.OpenForMemory(target.ProcessId);
            if (handle == IntPtr.Zero) continue;
            try
            {
                if (!BoostNative.EmptyWorkingSet(handle)) continue;
                released++;
                freed += target.MemoryBytes;
            }
            finally
            {
                BoostNative.CloseHandle(handle);
            }
        }

        if (released == 0) return Failed(step, "Aucune mémoire n'a pu être libérée (droits insuffisants).");

        session.AppliedOptimizations.Add("Mémoire vive libérée (" + released + " processus)");
        return Success(step, released + " processus libérés de " + AnalysisNative.FormatBytes(freed) +
                             " de mémoire vive cumulée (les applications en ont besoin à nouveau, la mémoire se re-remplit naturellement).");
    }

    private BoostStepResult ExecuteProfile(BoostPlan plan, BoostSessionState session, BoostStep step)
    {
        var game = ResolveGame(plan);
        if (game is null) return Failed(step, "Jeu introuvable dans la bibliothèque GameBoost.");
        var profile = ProfileService.Instance.GetActiveProfile(plan.GameId);
        if (profile is null) return Failed(step, "Aucun profil actif pour ce jeu.");

        var outcome = ProfileApplier.Instance.Apply(profile, game);
        if (!outcome.Success)
        {
            Log.Warn("Boost", "Profil non appliqué : " + outcome.Message);
            return Failed(step, outcome.Message);
        }

        if (!string.IsNullOrWhiteSpace(outcome.BackupPath))
        {
            if (!session.Backups.Contains(outcome.BackupPath)) session.Backups.Add(outcome.BackupPath);
            session.RevertData[ProfileBackupKeyNote] = outcome.BackupPath;
        }

        session.AppliedOptimizations.Add("Profil « " + profile.Name + " » appliqué");
        return Success(step, outcome.Message);
    }

    private BoostStepResult ExecuteClearTemp(BoostSessionState session, BoostStep step)
    {
        var root = Path.GetTempPath();
        var cutoff = DateTime.Now.AddHours(-48);
        var deleted = 0;
        var skipped = 0;
        long freed = 0;
        CleanDirectory(root, cutoff, ref deleted, ref freed, ref skipped);

        var message = deleted == 0
            ? "Aucun fichier temporaire de plus de 48 h n'a pu être supprimé."
            : (deleted == 1 ? "1 fichier temporaire supprimé (" : deleted + " fichiers temporaires supprimés (") +
              AnalysisNative.FormatBytes(freed) + ")";
        if (skipped > 0) message += ", " + skipped + " ignoré(s) (fichiers utilisés ou protégés).";

        if (deleted > 0)
        {
            session.AppliedOptimizations.Add("Fichiers temporaires nettoyés (" + deleted + ")");
            return Success(step, message);
        }
        return skipped > 0 ? Skipped(step, message) : Failed(step, message);
    }

    private BoostStepResult ExecuteOverlay(BoostSessionState session, BoostStep step)
    {
        var previousGlobal = SettingsService.Current.OverlayEnabled;
        var gameId = session.GameId;
        var previousGameSettings = string.Empty;
        if (!string.IsNullOrWhiteSpace(gameId))
        {
            try
            {
                var current = OverlayService.Instance.GetForGame(gameId);
                previousGameSettings = JsonSerializer.Serialize(current);
            }
            catch (Exception ex)
            {
                Log.Warn("Boost", "Réglage d'overlay du jeu illisible : " + ex.Message);
            }
        }

        try
        {
            SettingsService.Update(settings => settings.OverlayEnabled = true);
        }
        catch (Exception ex)
        {
            Log.Error("Boost", "Activation de l'overlay impossible", ex);
            return Failed(step, "Activation de l'overlay impossible : " + ex.Message);
        }

        if (!string.IsNullOrWhiteSpace(gameId))
        {
            try
            {
                var settings = string.IsNullOrEmpty(previousGameSettings)
                    ? OverlayService.Instance.GetForGame(gameId)
                    : JsonSerializer.Deserialize<OverlaySettings>(previousGameSettings) ?? OverlayService.Instance.GetForGame(gameId);
                settings.Enabled = true;
                OverlayService.Instance.SaveForGame(gameId, settings);
            }
            catch (Exception ex)
            {
                Log.Warn("Boost", "Réglage d'overlay du jeu non appliqué : " + ex.Message);
            }
        }

        session.RevertData[OverlayKeyNote] = previousGlobal ? "true" : "false";
        if (previousGameSettings.Length > 0) session.RevertData[OverlayGameKeyNote] = previousGameSettings;
        session.AppliedOptimizations.Add("Overlay FPS activé");
        return Success(step, "Overlay FPS activé pour la session (réglage précédent : " +
                             (previousGlobal ? "activé" : "désactivé") + ", restauré à la fin).");
    }

    private BoostSessionState EndSessionCore()
    {
        BoostSessionState? session;
        lock (_sync)
        {
            EnsurePendingResolved();
            session = _session;
        }

        if (session is null)
        {
            var empty = new BoostSessionState { IsActive = false, EndedAt = DateTime.Now };
            empty.Results.Add(new BoostStepResult { Title = "Fin de session", Success = false, Message = "Aucune session de boost à terminer." });
            Log.Warn("Boost", "Fin de session demandée sans session active");
            return empty;
        }

        if (!session.IsActive) return session;

        RestorePriority(session);
        RestorePowerPlan(session);
        RestoreGameMode(session);
        RestoreProfile(session);
        RestoreOverlay(session);
        RelaunchClosedApps(session);

        try
        {
            SystemMonitor.Instance.ClearProcess();
        }
        catch (Exception ex)
        {
            Log.Warn("Boost", "Arrêt du suivi du jeu impossible : " + ex.Message);
        }

        session.IsActive = false;
        session.EndedAt = DateTime.Now;
        BoostStore.Upsert(session);
        Log.Info("Boost", "Session " + session.SessionId + " terminée à " + session.EndedAt.Value.ToString("O"));
        return session;
    }

    private void RestorePriority(BoostSessionState session)
    {
        var keys = session.RevertData.Keys
            .Where(key => key.StartsWith(PriorityKeyPrefix, StringComparison.OrdinalIgnoreCase))
            .ToList();
        foreach (var key in keys)
        {
            var title = "Restauration : priorité du jeu";
            try
            {
                if (!int.TryParse(key[PriorityKeyPrefix.Length..], NumberStyles.Integer, CultureInfo.InvariantCulture, out var pid))
                {
                    AddRevert(session, title, false, "Identifiant de processus invalide dans les données de restauration.");
                    continue;
                }
                if (!uint.TryParse(session.RevertData[key], NumberStyles.Integer, CultureInfo.InvariantCulture, out var previous))
                {
                    AddRevert(session, title, false, "Ancienne priorité illisible pour le PID " + pid + ".");
                    continue;
                }

                var handle = BoostNative.OpenForPriority(pid);
                if (handle == IntPtr.Zero)
                {
                    AddRevert(session, title, false, "Le processus " + pid + " n'existe plus : aucune priorité à restaurer.");
                    continue;
                }
                try
                {
                    if (!BoostNative.SetProcessPriorityClass(handle, previous))
                    {
                        var error = Marshal.GetLastWin32Error();
                        AddRevert(session, title, false, "Restauration de la priorité refusée (code " + error + ").");
                        continue;
                    }
                    AddRevert(session, title, true, "Priorité restaurée à " + BoostNative.PriorityLabel(previous) + " pour le PID " + pid + ".");
                }
                finally
                {
                    BoostNative.CloseHandle(handle);
                }
            }
            catch (Exception ex)
            {
                Log.Error("Boost", "Restauration de la priorité impossible", ex);
                AddRevert(session, title, false, "Restauration impossible : " + ex.Message);
            }
        }
    }

    private void RestorePowerPlan(BoostSessionState session)
    {
        if (!session.RevertData.TryGetValue(PowerPlanKeyNote, out var previousGuid) || string.IsNullOrWhiteSpace(previousGuid))
            return;
        var title = "Restauration : plan d'alimentation";
        try
        {
            var attempt = AnalysisNative.RunCommand("powercfg.exe", "/setactive " + previousGuid);
            if (attempt.ExitCode != 0)
            {
                AddRevert(session, title, false, "Restauration du plan d'alimentation refusée (code " + attempt.ExitCode + ").");
                return;
            }
            var label = AnalysisNative.DescribeScheme(previousGuid);
            if (string.IsNullOrWhiteSpace(label)) label = previousGuid;
            AddRevert(session, title, true, "Plan d'alimentation « " + label + " » restauré.");
        }
        catch (Exception ex)
        {
            Log.Error("Boost", "Restauration du plan d'alimentation impossible", ex);
            AddRevert(session, title, false, "Restauration impossible : " + ex.Message);
        }
    }

    private void RestoreGameMode(BoostSessionState session)
    {
        if (!session.RevertData.TryGetValue(GameModeKeyNote, out var previous) || string.IsNullOrWhiteSpace(previous))
            return;
        var title = "Restauration : Mode Jeu Windows";
        try
        {
            if (previous.Equals("absent", StringComparison.OrdinalIgnoreCase))
            {
                AnalysisNative.DeleteGameMode();
                AddRevert(session, title, true, "Valeur AutoGameModeEnabled supprimée : le réglage Windows d'origine est rétabli.");
            }
            else if (int.TryParse(previous, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
            {
                AnalysisNative.WriteGameMode(value);
                AddRevert(session, title, true, "Mode Jeu Windows restauré à sa valeur précédente (" + value + ").");
            }
            else
            {
                AddRevert(session, title, false, "Valeur précédente illisible : « " + previous + " ».");
            }
        }
        catch (Exception ex)
        {
            Log.Error("Boost", "Restauration du Mode Jeu Windows impossible", ex);
            AddRevert(session, title, false, "Restauration impossible : " + ex.Message);
        }
    }

    private void RestoreProfile(BoostSessionState session)
    {
        if (!session.RevertData.TryGetValue(ProfileBackupKeyNote, out var backupPath) || string.IsNullOrWhiteSpace(backupPath))
            return;
        var title = "Restauration : profil de jeu";
        try
        {
            var outcome = ProfileApplier.Instance.Revert(backupPath);
            AddRevert(session, title, outcome.Success, outcome.Message);
        }
        catch (Exception ex)
        {
            Log.Error("Boost", "Restauration du profil impossible", ex);
            AddRevert(session, title, false, "Restauration impossible : " + ex.Message);
        }
    }

    private void RestoreOverlay(BoostSessionState session)
    {
        if (!session.RevertData.TryGetValue(OverlayKeyNote, out var previous) || string.IsNullOrWhiteSpace(previous))
            return;
        var title = "Restauration : overlay";
        var value = previous.Equals("true", StringComparison.OrdinalIgnoreCase);
        try
        {
            SettingsService.Update(settings => settings.OverlayEnabled = value);
            AddRevert(session, title, true, "Overlay " + (value ? "activé" : "désactivé") + " comme avant la session.");
        }
        catch (Exception ex)
        {
            Log.Error("Boost", "Restauration de l'overlay impossible", ex);
            AddRevert(session, title, false, "Restauration impossible : " + ex.Message);
        }

        if (string.IsNullOrWhiteSpace(session.GameId)) return;
        if (!session.RevertData.TryGetValue(OverlayGameKeyNote, out var previousGame) || string.IsNullOrWhiteSpace(previousGame))
            return;
        try
        {
            var settings = JsonSerializer.Deserialize<OverlaySettings>(previousGame);
            if (settings is null)
            {
                AddRevert(session, title, false, "Réglage d'overlay du jeu illisible : aucune restauration effectuée.");
                return;
            }
            OverlayService.Instance.SaveForGame(session.GameId, settings);
            AddRevert(session, title, true, "Réglage d'overlay du jeu restauré (overlay " + (settings.Enabled ? "activé" : "désactivé") + ").");
        }
        catch (Exception ex)
        {
            Log.Error("Boost", "Restauration du réglage d'overlay du jeu impossible", ex);
            AddRevert(session, title, false, "Restauration du réglage d'overlay du jeu impossible : " + ex.Message);
        }
    }

    private void RelaunchClosedApps(BoostSessionState session)
    {
        if (session.RelaunchPaths.Count == 0) return;
        var title = "Relance des applications fermées";
        if (!SettingsService.Current.RelaunchClosedApps)
        {
            AddRevert(session, title, true, "Relance ignorée : le paramètre « Relancer les applications fermées » est désactivé.");
            return;
        }

        var started = 0;
        var skipped = 0;
        var failed = 0;
        foreach (var path in session.RelaunchPaths.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                {
                    skipped++;
                    Log.Warn("Boost", "Exécutable introuvable pour la relance : " + path);
                    continue;
                }
                if (IsPathRunning(path))
                {
                    skipped++;
                    Log.Info("Boost", "Déjà en cours, relance ignorée : " + path);
                    continue;
                }
                Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true });
                started++;
            }
            catch (Exception ex)
            {
                failed++;
                Log.Error("Boost", "Relance impossible : " + path, ex);
            }
        }

        AddRevert(session, title, failed == 0,
            started + " application(s) relancée(s)" +
            (skipped > 0 ? ", " + skipped + " ignorée(s)" : string.Empty) +
            (failed > 0 ? ", " + failed + " en échec" : string.Empty) + ".");
    }

    private static bool IsPathRunning(string path)
    {
        try
        {
            foreach (var process in Process.GetProcesses())
            {
                try
                {
                    var current = process.MainModule?.FileName;
                    if (!string.IsNullOrWhiteSpace(current) &&
                        string.Equals(current, path, StringComparison.OrdinalIgnoreCase))
                        return true;
                }
                catch
                {
                }
                finally
                {
                    process.Dispose();
                }
            }
        }
        catch (Exception ex)
        {
            Log.Warn("Boost", "Liste des processus en cours impossible : " + ex.Message);
        }
        return false;
    }

    private static void CleanDirectory(string directory, DateTime cutoff, ref int deleted, ref long freed, ref int skipped)
    {
        try
        {
            foreach (var file in Directory.EnumerateFiles(directory))
            {
                try
                {
                    var info = new FileInfo(file);
                    if (info.LastWriteTime >= cutoff) continue;
                    if ((info.Attributes & FileAttributes.ReadOnly) != 0)
                    {
                        skipped++;
                        continue;
                    }
                    var length = info.Length;
                    info.Delete();
                    deleted++;
                    freed += length;
                }
                catch
                {
                    skipped++;
                }
            }

            foreach (var child in Directory.EnumerateDirectories(directory))
                CleanDirectory(child, cutoff, ref deleted, ref freed, ref skipped);
        }
        catch
        {
            skipped++;
        }
    }

    private void TrackGame(BoostPlan plan, BoostSessionState session)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(plan.GameId)) return;
            var pid = 0;
            foreach (var key in session.RevertData.Keys)
            {
                if (!key.StartsWith(PriorityKeyPrefix, StringComparison.OrdinalIgnoreCase)) continue;
                if (int.TryParse(key[PriorityKeyPrefix.Length..], NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
                    pid = parsed;
            }
            if (pid <= 0) pid = ResolveGameProcessId(ResolveGame(plan));
            if (pid <= 0)
            {
                Log.Info("Boost", "Jeu non lancé : aucun processus à suivre pour la session");
                return;
            }

            var name = ProcessService.Instance.GetProcess(pid)?.Name;
            if (string.IsNullOrWhiteSpace(name)) name = plan.GameName;
            SystemMonitor.Instance.TrackProcess(pid, name);
            Log.Info("Boost", "Moniteur ciblé sur " + name + " (PID " + pid + ")");
        }
        catch (Exception ex)
        {
            Log.Warn("Boost", "Suivi du jeu impossible : " + ex.Message);
        }
    }

    private BoostStep BuildCloseStep(IReadOnlyCollection<int> processIdsToClose, int gameProcessId)
    {
        var step = new BoostStep
        {
            Kind = BoostActionKind.CloseProcesses,
            Title = "Applications à fermer",
            Description = "Fermer les applications sélectionnées",
            RequiresConfirmation = true
        };

        var pids = new List<int>();
        var names = new List<string>();
        var excluded = new List<string>();
        foreach (var pid in (processIdsToClose ?? Array.Empty<int>()).Distinct())
        {
            if (pid <= 0) continue;
            var snapshot = ProcessService.Instance.GetProcess(pid);
            if (snapshot is null)
            {
                excluded.Add("PID " + pid + " introuvable");
                continue;
            }
            if (pid == gameProcessId)
            {
                excluded.Add(snapshot.Name + " (processus du jeu)");
                continue;
            }
            if (!ProcessService.Instance.CanClose(snapshot, out var reason))
            {
                excluded.Add(snapshot.Name + " (" + reason + ")");
                continue;
            }
            pids.Add(pid);
            names.Add(string.IsNullOrWhiteSpace(snapshot.Name) ? pid.ToString(CultureInfo.InvariantCulture) : snapshot.Name);
        }

        step.Payload = string.Join(",", pids);
        step.AffectedItems = names;
        step.Enabled = pids.Count > 0;

        if (pids.Count == 0)
        {
            step.Detail = excluded.Count > 0
                ? "Aucune application fermissible : " + string.Join(", ", excluded)
                : "Aucune application sélectionnée.";
        }
        else
        {
            step.Detail = names.Count + (names.Count <= 1 ? " application : " : " applications : ") + string.Join(", ", names);
            if (excluded.Count > 0) step.Detail += ". Exclus : " + string.Join(", ", excluded);
        }

        if (pids.Count > 0)
            step.RevertHint = SettingsService.Current.RelaunchClosedApps
                ? "L'application sera relancée à la fin de la session"
                : "L'application ne sera pas relancée à la fin de la session";
        return step;
    }

    private static BoostStep BuildPriorityStep(GameInfo? game, int gameProcessId)
    {
        var step = new BoostStep
        {
            Kind = BoostActionKind.SetProcessPriority,
            Title = "Priorité du jeu",
            Description = "Augmenter la priorité du processus du jeu"
        };

        if (game is null)
        {
            step.Enabled = false;
            step.Detail = "Aucun jeu sélectionné — cette action sera ignorée.";
            return step;
        }
        if (gameProcessId <= 0)
        {
            step.Enabled = false;
            step.Detail = "Le jeu n'est pas lancé — cette action sera ignorée.";
            return step;
        }

        var label = GameProcessLabel(game, gameProcessId);
        step.Enabled = true;
        step.Payload = gameProcessId.ToString(CultureInfo.InvariantCulture);
        step.AffectedItems.Add(label);
        step.Detail = "Priorité haute pour " + label + " (restaurée à Normal en fin de session)";
        step.RevertHint = "La priorité revient à sa valeur d'origine à la fin de la session";
        return step;
    }

    private static BoostStep BuildGameModeStep(bool enabled)
    {
        var step = new BoostStep
        {
            Kind = BoostActionKind.EnableGameMode,
            Title = "Mode Jeu Windows",
            Description = "Activer le Mode Jeu Windows",
            Enabled = enabled
        };
        step.Detail = enabled
            ? @"HKCU\Software\Microsoft\GameBar AutoGameModeEnabled = 1 (ancienne valeur sauvegardée)"
            : "Option non sélectionnée : le registre ne sera pas modifié.";
        step.RevertHint = "L'ancienne valeur du registre est restaurée à la fin de la session";
        return step;
    }

    private static BoostStep BuildPowerPlanStep(bool enabled)
    {
        var step = new BoostStep
        {
            Kind = BoostActionKind.SetPowerPlan,
            Title = "Plan d'alimentation",
            Description = "Activer le plan « Hautes performances »",
            Enabled = enabled
        };
        step.Detail = enabled
            ? "Plan « Hautes performances » activé temporairement (ancien plan restauré à la fin)"
            : "Option non sélectionnée : le plan d'alimentation reste inchangé.";
        step.RevertHint = "L'ancien plan d'alimentation est restauré à la fin de la session";
        return step;
    }

    private BoostStep BuildMemoryStep(bool enabled, int gameProcessId)
    {
        var step = new BoostStep
        {
            Kind = BoostActionKind.ReleaseMemory,
            Title = "Mémoire vive",
            Description = "Libérer la mémoire vive des applications en arrière-plan (hors applications protégées)",
            Enabled = enabled
        };

        if (!enabled)
        {
            step.Detail = "Option non sélectionnée : aucune mémoire ne sera libérée.";
            return step;
        }

        var neverClose = new HashSet<string>(ProcessService.Instance.GetNeverCloseList(), StringComparer.OrdinalIgnoreCase);
        List<ProcessSnapshot> targets;
        try
        {
            targets = ProcessService.Instance.GetProcesses()
                .Where(p => p.ProcessId > 4 && p.ProcessId != gameProcessId && p.ProcessId != Environment.ProcessId && !p.IsCritical &&
                            !neverClose.Contains(p.Name) && p.MemoryBytes >= 32L * 1024 * 1024)
                .OrderByDescending(p => p.MemoryBytes)
                .Take(20)
                .ToList();
        }
        catch (Exception ex)
        {
            Log.Warn("Boost", "Cibles de mémoire indisponibles : " + ex.Message);
            targets = new List<ProcessSnapshot>();
        }

        step.AffectedItems = targets.Select(p => p.Name + " (" + AnalysisNative.FormatBytes(p.MemoryBytes) + ")").ToList();
        step.Detail = targets.Count == 0
            ? "Aucune application en arrière-plan à libérer pour le moment."
            : targets.Count + " cible(s) : " + string.Join(", ", step.AffectedItems.Take(8)) +
              (targets.Count > 8 ? ", …" : string.Empty);
        step.RevertHint = "Les applications réclament la mémoire qu'elles ont besoin, aucune restauration nécessaire";
        return step;
    }

    private static BoostStep BuildProfileStep(GameInfo? game, GameProfile? profile, bool applyProfile)
    {
        var step = new BoostStep
        {
            Kind = BoostActionKind.ApplyProfile,
            Title = "Profil de jeu",
            Description = profile is null ? "Appliquer le profil du jeu" : "Appliquer le profil « " + profile.Name + " »",
            Enabled = profile is not null
        };

        if (!applyProfile)
        {
            step.Enabled = false;
            step.Detail = "Option non sélectionnée : le fichier de configuration du jeu ne sera pas modifié.";
        }
        else if (game is null)
        {
            step.Enabled = false;
            step.Detail = "Aucun jeu sélectionné — cette action sera ignorée.";
        }
        else if (profile is null)
        {
            step.Enabled = false;
            step.Detail = "Aucun profil actif";
        }
        else
        {
            step.Detail = "Profil « " + profile.Name + " » (résolution, qualité, V-Sync…). " +
                          "Si GameBoost connaît le fichier de configuration du jeu, une copie de sauvegarde sera créée avant modification.";
            step.AffectedItems.Add(profile.Name);
            step.RevertHint = "Les fichiers de configuration sont restaurés depuis la sauvegarde à la fin de la session";
        }
        return step;
    }

    private static BoostStep BuildTempStep(bool enabled)
    {
        var step = new BoostStep
        {
            Kind = BoostActionKind.ClearTempFiles,
            Title = "Fichiers temporaires",
            Description = "Supprimer les fichiers temporaires de plus de 48 h (%TEMP%) — action non réversible, mais sans danger",
            Enabled = enabled,
            RequiresConfirmation = true
        };
        step.Detail = enabled
            ? "Cible : " + Path.GetTempPath() + " (fichiers de plus de 48 h)"
            : "Option non sélectionnée : aucun fichier ne sera supprimé.";
        step.RevertHint = "Action non réversible : les fichiers temporaires supprimés ne peuvent pas être récupérés";
        return step;
    }

    private static BoostStep BuildOverlayStep(bool enabled)
    {
        var step = new BoostStep
        {
            Kind = BoostActionKind.ToggleOverlay,
            Title = "Overlay FPS",
            Description = "Afficher l'overlay FPS pendant la session",
            Enabled = enabled
        };
        step.Detail = enabled
            ? "Le réglage « Afficher l'overlay » est activé pendant la session, puis restauré à la fin."
            : "Option non sélectionnée : l'overlay reste dans son état actuel.";
        step.RevertHint = "Le réglage de l'overlay est restauré à la fin de la session";
        return step;
    }

    private static string GameProcessLabel(GameInfo game, int processId)
    {
        try
        {
            var snapshot = ProcessService.Instance.GetProcess(processId);
            if (!string.IsNullOrWhiteSpace(snapshot?.Name)) return snapshot!.Name;
        }
        catch (Exception ex)
        {
            Log.Warn("Boost", "Nom du processus du jeu indisponible : " + ex.Message);
        }

        if (!string.IsNullOrWhiteSpace(game.ExecutablePath))
        {
            var fileName = Path.GetFileName(game.ExecutablePath);
            if (!string.IsNullOrWhiteSpace(fileName)) return fileName;
        }
        return string.IsNullOrWhiteSpace(game.Name) ? "jeu" : game.Name;
    }

    private int ResolveGameProcessId(GameInfo? game)
    {
        if (game is null) return 0;
        try
        {
            var pid = ProcessService.Instance.FindGameProcessId(game);
            if (pid is int found && found > 0) return found;
            return game.IsRunning && game.RunningProcessId > 0 ? game.RunningProcessId : 0;
        }
        catch (Exception ex)
        {
            Log.Warn("Boost", "Recherche du processus du jeu impossible : " + ex.Message);
            return 0;
        }
    }

    private GameInfo? ResolveGame(BoostPlan plan)
    {
        if (string.IsNullOrWhiteSpace(plan.GameId)) return null;
        lock (_sync)
        {
            if (_plannedGame is not null && _plannedGame.Id == plan.GameId) return _plannedGame;
        }

        try
        {
            return GameScanner.Instance.GetLibrary().FirstOrDefault(g => g.Id == plan.GameId);
        }
        catch (Exception ex)
        {
            Log.Warn("Boost", "Lecture de la bibliothèque de jeux impossible : " + ex.Message);
            return null;
        }
    }

    private void EnsurePendingResolved()
    {
        lock (_sync)
        {
            if (_pendingResolved) return;
            _pendingResolved = true;
            var pending = BoostStore.FindActive();
            if (pending is null)
            {
                Log.Debug("Boost", "Aucune session de boost en attente dans la base");
                return;
            }
            _session = pending;
            Log.Warn("Boost", "Session de boost en attente détectée : " + pending.SessionId +
                              " (démarrée le " + pending.StartedAt.ToString("O") + ")");
        }
    }

    private void AddRevert(BoostSessionState session, string title, bool success, string message)
    {
        session.Results.Add(new BoostStepResult { Title = title, Success = success, Message = message });
        BoostStore.Upsert(session);
        if (success) Log.Info("Boost", title + " : " + message);
        else Log.Warn("Boost", title + " : " + message);
    }

    private static BoostStepResult Success(BoostStep step, string message) =>
        new() { StepId = step.Id, Title = step.Title, Success = true, Message = message };

    private static BoostStepResult Failed(BoostStep step, string message)
    {
        Log.Warn("Boost", step.Title + " : " + message);
        return new BoostStepResult { StepId = step.Id, Title = step.Title, Success = false, Message = message };
    }

    private static BoostStepResult Skipped(BoostStep step, string message) =>
        new() { StepId = step.Id, Title = step.Title, Success = true, Skipped = true, Message = message };

    private static void Report(IProgress<string>? progress, string message)
    {
        try
        {
            progress?.Report(message);
        }
        catch (Exception ex)
        {
            Log.Warn("Boost", "Un abonné à la progression a refusé la mise à jour : " + ex.Message);
        }
    }

    private static List<int> ParsePids(string? payload)
    {
        var pids = new List<int>();
        if (string.IsNullOrWhiteSpace(payload)) return pids;
        foreach (var token in payload.Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries))
        {
            if (int.TryParse(token.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var pid) && pid > 0)
                pids.Add(pid);
        }
        return pids;
    }

    private static HashSet<int> ExtractFailedPids(IReadOnlyList<string> errors)
    {
        var failed = new HashSet<int>();
        foreach (var error in errors)
        {
            if (!error.StartsWith("PID ", StringComparison.OrdinalIgnoreCase)) continue;
            var separator = error.IndexOf(':');
            if (separator < 0) continue;
            var token = error[4..separator].Trim();
            if (int.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out var pid)) failed.Add(pid);
        }
        return failed;
    }
}
