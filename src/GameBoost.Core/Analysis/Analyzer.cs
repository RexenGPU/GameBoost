using GameBoost.Core.Logging;
using GameBoost.Core.Models;
using GameBoost.Core.Monitoring;

namespace GameBoost.Core.Analysis;

public sealed partial class Analyzer
{
    private readonly object _sync = new();

    private HardwareReport? _hardware;
    private List<ProcessSnapshot>? _processSample;
    private HashSet<int> _excludedPids = new();
    private bool _startedMonitor;

    public static Analyzer Instance { get; } = new();

    public event Action<AnalysisProgress>? Progress;

    private Analyzer()
    {
    }

    public Task<AnalysisReport> RunAsync(CancellationToken ct = default)
    {
        return Task.Run(() => RunCore(ct));
    }

    public AnalysisReport Run()
    {
        return RunCore(CancellationToken.None);
    }

    private AnalysisReport RunCore(CancellationToken ct)
    {
        try
        {
            return RunStages(ct);
        }
        catch (Exception ex)
        {
            Log.Error("Analysis", "Analyse interrompue par une erreur imprévue", ex);
            var fallback = new AnalysisReport
            {
                CreatedAt = DateTime.Now,
                OverallSummary = "L'analyse n'a pas pu se terminer : " + ex.Message
            };
            fallback.Checks.Add(new CheckResult
            {
                Id = "analysis-failed",
                Category = "Système",
                Title = "Analyse interrompue",
                Level = HealthLevel.Unknown,
                Explanation = "Une erreur interne a interrompu l'analyse : " + ex.Message,
                Impact = "Les autres vérifications de ce rapport peuvent être incomplètes.",
                Solution = "Relancez l'analyse. Si l'erreur persiste, ouvrez le journal GameBoost.",
                CurrentValue = "Analyse incomplète"
            });
            return fallback;
        }
    }

    private AnalysisReport RunStages(CancellationToken ct)
    {
        lock (_sync)
        {
            _hardware = null;
            _processSample = null;
            _excludedPids = new HashSet<int>();
            _startedMonitor = false;

            var report = new AnalysisReport { CreatedAt = DateTime.Now };
            var stages = new (string Name, Action Body)[]
            {
                ("Matériel", () => RunStage(report, CheckHardwareDetection)),
                ("Mémoire", () => RunStage(report, CheckTotalRam, CheckRamAvailability, CheckVramAvailability)),
                ("Processus", () => RunStage(report, CheckHungryProcesses, CheckBackgroundApps, CheckAbnormalProcess)),
                ("Stockage", () => RunStage(report, CheckSystemDiskSpace, CheckStorageHealth)),
                ("Températures", () => RunStage(report, CheckCpuTemperature, CheckGpuTemperature)),
                ("Pilotes", () => RunStage(report, CheckGpuDriverAge)),
                ("Windows", () => RunStage(report, CheckGameMode, CheckPowerPlan, CheckStartupEntries, CheckElevation)),
                ("Affichage", () => RunStage(report, CheckRefreshRate, CheckDisplayResolution))
            };

            var currentStage = stages[^1].Name;
            try
            {
                for (var index = 0; index < stages.Length; index++)
                {
                    currentStage = stages[index].Name;
                    Publish(currentStage, index * 100 / stages.Length, string.Empty);
                    if (ct.IsCancellationRequested)
                    {
                        Log.Warn("Analysis", "Analyse interrompue par l'utilisateur après " + currentStage);
                        break;
                    }
                    stages[index].Body();
                }
            }
            catch (Exception ex)
            {
                Log.Error("Analysis", "Erreur imprévue pendant l'analyse", ex);
            }

            Publish(currentStage, 100, "Vérifications terminées");
            try
            {
                FinalizeReport(report);
            }
            catch (Exception ex)
            {
                Log.Error("Analysis", "Finalisation du rapport impossible", ex);
                if (string.IsNullOrWhiteSpace(report.OverallSummary))
                    report.OverallSummary = "Analyse terminée mais le récapitulatif n'a pas pu être calculé : " + ex.Message;
            }
            return report;
        }
    }

    private void FinalizeReport(AnalysisReport report)
    {
        report.Hardware = _hardware;
        try
        {
            var monitor = SystemMonitor.Instance;
            report.Snapshot = monitor.IsRunning ? monitor.Current : null;
        }
        catch (Exception ex)
        {
            Log.Warn("Analysis", "Instantané de surveillance indisponible : " + ex.Message);
        }

        if (_startedMonitor)
        {
            try
            {
                SystemMonitor.Instance.Stop();
            }
            catch (Exception ex)
            {
                Log.Warn("Analysis", "Arrêt du moniteur impossible : " + ex.Message);
            }
            _startedMonitor = false;
        }

        report.OverallSummary = report.Checks.Count == 0
            ? "Aucune vérification n'a pu être exécutée."
            : report.Checks.Count + " vérifications : " + report.GoodCount + " conformes, " +
              report.WarningCount + " avertissements, " + report.CriticalCount + " critiques, " +
              report.Checks.Count(c => c.Level == HealthLevel.Unknown) + " non concluantes.";

        Log.Info("Analysis", "Analyse terminée : " + report.OverallSummary);
    }

    private static void RunStage(AnalysisReport report, params Func<CheckResult>[] checks)
    {
        foreach (var check in checks)
        {
            try
            {
                report.Checks.Add(check());
            }
            catch (Exception ex)
            {
                Log.Error("Analysis", "Échec de la vérification " + check.Method.Name, ex);
                report.Checks.Add(new CheckResult
                {
                    Id = check.Method.Name,
                    Category = "Système",
                    Title = "Vérification impossible à terminer",
                    Level = HealthLevel.Unknown,
                    Explanation = "Cette vérification a échoué : " + ex.Message,
                    Impact = "Le résultat de cette vérification n'est pas fiable pour le moment.",
                    Solution = "Relancez l'analyse. Si l'erreur persiste, ouvrez le journal GameBoost.",
                    CurrentValue = "Indisponible"
                });
            }
        }
    }

    private void Publish(string stage, int percent, string detail)
    {
        try
        {
            Progress?.Invoke(new AnalysisProgress { Stage = stage, Percent = Math.Clamp(percent, 0, 100), Detail = detail });
        }
        catch (Exception ex)
        {
            Log.Warn("Analysis", "Un abonné à la progression a refusé la mise à jour : " + ex.Message);
        }
    }

    private static CheckResult NewResult(string id, string category, string title)
    {
        return new CheckResult
        {
            Id = id,
            Category = category,
            Title = title,
            Level = HealthLevel.Unknown
        };
    }

    private static CheckResult Failed(CheckResult result, Exception ex)
    {
        Log.Error("Analysis", "Vérification " + result.Id + " impossible", ex);
        result.Level = HealthLevel.Unknown;
        result.Explanation = "Cette mesure n'a pas pu être obtenue : " + ex.Message;
        result.Impact = "Sans mesure, GameBoost ne peut pas conclure sur ce point.";
        result.Solution = "Relancez l'analyse ; si l'erreur persiste, redémarrez GameBoost.";
        result.CurrentValue = "Indisponible";
        result.CanAutoFix = false;
        result.FixActionId = null;
        result.FixDescription = string.Empty;
        return result;
    }
}
