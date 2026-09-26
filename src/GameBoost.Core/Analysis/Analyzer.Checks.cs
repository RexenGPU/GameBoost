using System.Globalization;
using GameBoost.Core.Data;
using GameBoost.Core.Disks;
using GameBoost.Core.Games;
using GameBoost.Core.Hardware;
using GameBoost.Core.Logging;
using GameBoost.Core.Models;
using GameBoost.Core.Monitoring;
using GameBoost.Core.Processes;

namespace GameBoost.Core.Analysis;

public sealed partial class Analyzer
{
    private CheckResult CheckHardwareDetection()
    {
        var result = NewResult("hardware-detection", "Matériel", "Matériel détecté");
        try
        {
            if (_hardware is null)
                _hardware = HardwareDetector.Instance.Collect();

            var missing = new List<string>();
            if (string.IsNullOrWhiteSpace(_hardware.Cpu.Name) || _hardware.Cpu.Name == "Inconnu") missing.Add("le processeur");
            if (string.IsNullOrWhiteSpace(_hardware.Gpu.Name) || _hardware.Gpu.Name == "Inconnue") missing.Add("la carte graphique");
            if (_hardware.Ram.TotalBytes <= 0) missing.Add("la mémoire vive");

            if (missing.Count == 0)
            {
                result.Level = HealthLevel.Good;
                result.Explanation = "GameBoost a identifié les composants principaux de ce PC. Ces informations servent à adapter les conseils et les optimisations.";
                result.Impact = "Aucun : le matériel est reconnu normalement.";
                result.Solution = "Rien à faire.";
                result.CurrentValue = _hardware.Cpu.Name.Trim() + " · " + _hardware.Gpu.Name.Trim() + " · " +
                                       AnalysisNative.FormatBytes(_hardware.Ram.TotalBytes) + " de mémoire vive";
            }
            else
            {
                result.Level = HealthLevel.Warning;
                result.Explanation = "GameBoost n'a pas pu identifier " + string.Join(", ", missing) +
                                      ". Les mesures correspondantes seront affichées comme indisponibles.";
                result.Impact = "Les vérifications qui dépendent de ces composants ne seront pas concluantes.";
                result.Solution = "Vérifiez que vos pilotes sont à jour, puis relancez l'analyse.";
                result.CurrentValue = "Composants manquants : " + string.Join(", ", missing);
            }
        }
        catch (Exception ex)
        {
            return Failed(result, ex);
        }
        return result;
    }

    private CheckResult CheckTotalRam()
    {
        var result = NewResult("ram-total", "Mémoire", "Quantité totale de mémoire vive");
        try
        {
            long total = _hardware?.Ram.TotalBytes ?? 0;
            if (total <= 0 && AnalysisNative.TryGetMemory(out var nativeTotal, out _))
                total = (long)nativeTotal;
            if (total <= 0)
            {
                result.Level = HealthLevel.Unknown;
                result.Explanation = "Windows n'a pas communiqué la quantité de mémoire installée.";
                result.Impact = "Impossible d'estimer si votre PC dispose de assez de mémoire pour les jeux récents.";
                result.Solution = "Relancez l'analyse. Si la mesure reste indisponible, vérifiez vos mémoires dans le Gestionnaire des tâches.";
                result.CurrentValue = "Mesure indisponible";
                return result;
            }

            var go = total / (1024d * 1024d * 1024d);
            result.CurrentValue = AnalysisNative.FormatBytes(total) + " installées (" + go.ToString("0.#", CultureInfo.CurrentCulture) + " Go)";

            if (go < 8)
            {
                result.Level = HealthLevel.Warning;
                result.Explanation = "Votre PC ne dispose que de " + result.CurrentValue +
                                      ". Aujourd'hui, 8 Go reste le minimum confortable pour la plupart des jeux ; en dessous, Windows et le jeu se battent pour la mémoire.";
                result.Impact = "Chargements plus longs, saccades, applications fermées en arrière-plan faute de mémoire.";
                result.Solution = "Conseil honnête : ajoutez une barrette de mémoire si votre carte mère le permet. " +
                                  "En attendant, fermez les applications inutiles avant de jouer (page Applications).";
                result.FixActionId = "navigate:processes";
                result.CanAutoFix = false;
                result.FixDescription = "Ouvrir la page Applications pour libérer de la mémoire";
            }
            else
            {
                result.Level = HealthLevel.Good;
                result.Explanation = "Votre PC dispose de " + result.CurrentValue + ", ce qui est largement suffisant pour jouer.";
                result.Impact = "Aucun : la quantité de mémoire ne freinera pas votre jeu.";
                result.Solution = "Rien à faire.";
            }
        }
        catch (Exception ex)
        {
            return Failed(result, ex);
        }
        return result;
    }

    private CheckResult CheckRamAvailability()
    {
        var result = NewResult("ram-availability", "Mémoire", "Mémoire vive disponible");
        try
        {
            if (!AnalysisNative.TryGetMemory(out var total, out var available) || total == 0)
            {
                result.Level = HealthLevel.Unknown;
                result.Explanation = "Windows n'a pas communiqué l'état de la mémoire vive, impossible de conclure.";
                result.Impact = "Sans mesure, GameBoost ne peut pas dire si la mémoire sera suffisante pour le jeu.";
                result.Solution = "Relancez l'analyse. Si le problème persiste, redémarrez GameBoost.";
                result.CurrentValue = "Mesure indisponible";
                return result;
            }

            var percent = available * 100.0 / total;
            result.CurrentValue = AnalysisNative.FormatBytes((long)available) + " libres sur " +
                                  AnalysisNative.FormatBytes((long)total) + " (" + AnalysisNative.FormatPercent(percent) + ")";

            if (percent >= 20)
            {
                result.Level = HealthLevel.Good;
                result.Explanation = "Il reste " + AnalysisNative.FormatPercent(percent) +
                                      " de mémoire vive libre. C'est largement suffisant pour lancer un jeu.";
                result.Impact = "Aucun : la mémoire ne freinera pas votre jeu.";
                result.Solution = "Rien à faire.";
            }
            else if (percent >= 10)
            {
                result.Level = HealthLevel.Warning;
                result.Explanation = "Il ne reste que " + AnalysisNative.FormatPercent(percent) +
                                      " de mémoire vive libre. Un jeu exigeant risque de ramer ou de faire fermer d'autres applications.";
                result.Impact = "Ralentissements, images substituées, fermeture brutale de programmes en arrière-plan.";
                result.Solution = "Ouvrez la page Applications de GameBoost et fermez les programmes que vous n'utilisez pas avant de jouer.";
            }
            else
            {
                result.Level = HealthLevel.Critical;
                result.Explanation = "Il ne reste que " + AnalysisNative.FormatPercent(percent) +
                                      " de mémoire vive libre : la mémoire est presque épuisée.";
                result.Impact = "Le jeu peut planter, se bloquer, ou Windows peut fermer des applications de force.";
                result.Solution = "Fermez immédiatement les applications inutiles via la page Applications, puis relancez l'analyse.";
            }

            result.FixActionId = "navigate:processes";
            result.CanAutoFix = false;
            result.FixDescription = "Ouvrir la page Applications pour fermer des programmes";
        }
        catch (Exception ex)
        {
            return Failed(result, ex);
        }
        return result;
    }

    private CheckResult CheckVramAvailability()
    {
        var result = NewResult("vram-availability", "Mémoire", "Mémoire vidéo disponible");
        try
        {
            EnsureMonitorSample();
            var sample = SystemMonitor.Instance.Current;
            if (sample.VramTotalBytes <= 0)
            {
                result.Level = HealthLevel.Unknown;
                result.Explanation = "GameBoost ne connaît pas la taille de la mémoire vidéo de votre carte graphique, impossible de calculer le taux d'utilisation.";
                result.Impact = "Les jeux très gourmands en textures pourraient manquer de mémoire vidéo sans que GameBoost ne puisse vous le signaler.";
                result.Solution = "Lancez une partie quelques secondes puis relancez l'analyse pour que la mesure soit disponible.";
                result.CurrentValue = "Mesure indisponible";
                return result;
            }

            if (sample.VramUsedBytes <= 0)
            {
                result.Level = HealthLevel.Unknown;
                result.Explanation = "La taille de la mémoire vidéo est connue mais son utilisation n'est pas mesurée actuellement.";
                result.Impact = "Impossible de dire si la carte graphique manque de mémoire.";
                result.Solution = "Laissez GameBoost en arrière-plan une minute puis relancez l'analyse.";
                result.CurrentValue = "Utilisation non mesurée sur " + AnalysisNative.FormatBytes(sample.VramTotalBytes);
                return result;
            }

            var percent = sample.VramUsedBytes * 100.0 / sample.VramTotalBytes;
            result.CurrentValue = AnalysisNative.FormatBytes(sample.VramUsedBytes) + " utilisés sur " +
                                  AnalysisNative.FormatBytes(sample.VramTotalBytes) + " (" + AnalysisNative.FormatPercent(percent) + ")";

            if (percent >= 90)
            {
                result.Level = HealthLevel.Warning;
                result.Explanation = "La mémoire vidéo est remplie à " + AnalysisNative.FormatPercent(percent) +
                                      ". Les jeux gourmands risquent de charger les textures plus lentement ou de faire des plans alternés.";
                result.Impact = "Textures de basse qualité, saccades, possible fermeture du jeu faute de mémoire vidéo.";
                result.Solution = "Réduisez la résolution ou la qualité des textures dans les réglages du jeu, et fermez les autres applications utilisant le GPU.";
            }
            else
            {
                result.Level = HealthLevel.Good;
                result.Explanation = "La mémoire vidéo est utilisée à " + AnalysisNative.FormatPercent(percent) +
                                      ", il reste de la marge pour le jeu.";
                result.Impact = "Aucun : la carte graphique dispose de assez de mémoire.";
                result.Solution = "Rien à faire.";
            }
        }
        catch (Exception ex)
        {
            return Failed(result, ex);
        }
        return result;
    }

    private CheckResult CheckHungryProcesses()
    {
        var result = NewResult("cpu-hungry-processes", "Processus", "Programmes gourmands en processeur");
        try
        {
            var sample = EnsureProcessSample();
            var hungry = sample
                .Where(p => p.CpuPercent >= 25 && !_excludedPids.Contains(p.ProcessId))
                .OrderByDescending(p => p.CpuPercent)
                .ToList();

            if (hungry.Count == 0)
            {
                result.Level = HealthLevel.Good;
                result.Explanation = "Aucun programme hors du jeu n'utilise 25 % du processeur ou plus.";
                result.Impact = "Aucun : le processeur est disponible pour le jeu.";
                result.Solution = "Rien à faire.";
                result.CurrentValue = "Aucun programme au-dessus de 25 %";
                return result;
            }

            var details = string.Join(", ", hungry.Take(6).Select(p => p.Name + " " + AnalysisNative.FormatPercent(p.CpuPercent)));
            result.CurrentValue = hungry.Count + " programme(s) au-dessus de 25 % : " + details;
            var pids = string.Join(",", hungry.Select(p => p.ProcessId));
            result.FixActionId = "navigate:processes";
            result.CanAutoFix = false;
            result.FixDescription = "Ouvrir la page Applications pour examiner ces programmes (PID : " + pids + ")";

            var worst = hungry[0];
            if (worst.CpuPercent > 60)
            {
                result.Level = HealthLevel.Critical;
                result.Explanation = "Le programme « " + worst.Name + " » monopolise " + AnalysisNative.FormatPercent(worst.CpuPercent) +
                                      " du processeur, il laisse peu de ressources au jeu.";
                result.Impact = "Images par seconde en baisse, micro-saccades, jeu moins réactif.";
                result.Solution = "Fermez ce programme depuis la page Applications s'il n'est pas nécessaire pendant votre partie.";
            }
            else if (hungry.Count >= 2)
            {
                result.Level = HealthLevel.Warning;
                result.Explanation = hungry.Count + " programmes utilisent chacun au moins 25 % du processeur en même temps que le jeu.";
                result.Impact = "Cumulés, ils réduisent les images par seconde et peuvent provoquer des saccades.";
                result.Solution = "Fermez les programmes inutiles depuis la page Applications avant de jouer.";
            }
            else
            {
                result.Level = HealthLevel.Good;
                result.Explanation = "Un seul programme dépasse 25 % du processeur et aucun n'en monopolise la majorité.";
                result.Impact = "Impact limité sur le jeu.";
                result.Solution = "Rien de particulier à faire, mais vous pouvez fermer ce programme si vous ne l'êtes pas en train de l'utiliser.";
            }
        }
        catch (Exception ex)
        {
            return Failed(result, ex);
        }
        return result;
    }

    private CheckResult CheckBackgroundApps()
    {
        var result = NewResult("background-apps", "Processus", "Applications actives en arrière-plan");
        try
        {
            var sample = EnsureProcessSample();
            var active = sample
                .Where(p => p.ProcessId > 4 && p.CpuPercent >= 1 && !_excludedPids.Contains(p.ProcessId))
                .OrderByDescending(p => p.CpuPercent)
                .ToList();

            result.CurrentValue = active.Count + " application(s) avec au moins 1 % de processeur";

            if (active.Count > 8)
            {
                result.Level = HealthLevel.Warning;
                result.Explanation = active.Count +
                    " applications tournent encore en arrière-plan avec au moins 1 % de processeur : " +
                    string.Join(", ", active.Take(5).Select(p => p.Name)) + ".";
                result.Impact = "Chacune consomme un peu de mémoire et de temps processeur au détriment du jeu.";
                result.Solution = "Fermez celles dont vous n'avez pas besoin pendant la partie, depuis la page Applications.";
                result.FixActionId = "navigate:processes";
                result.CanAutoFix = false;
                result.FixDescription = "Ouvrir la page Applications";
            }
            else
            {
                result.Level = HealthLevel.Good;
                result.Explanation = active.Count == 0
                    ? "Aucune autre application n'est active en arrière-plan en ce moment."
                    : active.Count + " application(s) active(s) en arrière-plan, ce qui reste raisonnable.";
                result.Impact = "Aucun impact notable sur le jeu.";
                result.Solution = "Rien à faire.";
            }
        }
        catch (Exception ex)
        {
            return Failed(result, ex);
        }
        return result;
    }

    private CheckResult CheckAbnormalProcess()
    {
        var result = NewResult("abnormal-process-cpu", "Processus", "Processus anormalement gourmand");
        try
        {
            var sample = EnsureProcessSample();
            var abnormal = sample
                .Where(p => p.CpuPercent > 50 && !_excludedPids.Contains(p.ProcessId))
                .OrderByDescending(p => p.CpuPercent)
                .ToList();

            if (abnormal.Count == 0)
            {
                result.Level = HealthLevel.Good;
                result.Explanation = "Aucun processus hors du jeu ne dépasse 50 % du processeur en ce moment.";
                result.Impact = "Aucun : rien ne monopolise le processeur.";
                result.Solution = "Rien à faire.";
                result.CurrentValue = "Aucun processus au-dessus de 50 %";
                return result;
            }

            var top = abnormal[0];
            result.Level = HealthLevel.Critical;
            result.CurrentValue = string.Join(", ", abnormal.Take(5).Select(p => p.Name + " " + AnalysisNative.FormatPercent(p.CpuPercent)));
            result.Explanation = "Le processus « " + top.Name + " » (PID " + top.ProcessId + ") consomme " +
                                 AnalysisNative.FormatPercent(top.CpuPercent) + " du processeur, ce qui est anormalement élevé pour un programme en arrière-plan.";
            result.Impact = "Le jeu perdra des images par seconde tant que ce processus tourne.";
            result.Solution = "Ouvrez la page Applications, identifiez ce programme et fermez-le s'il n'est pas indispensable. " +
                              (top.MemoryBytes > 0 ? "Il occupe aussi " + AnalysisNative.FormatBytes(top.MemoryBytes) + " de mémoire." : string.Empty);
            result.FixActionId = "navigate:processes";
            result.CanAutoFix = false;
            result.FixDescription = "Ouvrir la page Applications";
        }
        catch (Exception ex)
        {
            return Failed(result, ex);
        }
        return result;
    }

    private CheckResult CheckSystemDiskSpace()
    {
        var result = NewResult("system-disk-space", "Stockage", "Espace disque du volume système");
        try
        {
            var disks = DiskAnalyzer.Instance.AnalyzeFast();
            var volume = disks.SelectMany(d => d.Volumes).FirstOrDefault(v => v.IsSystem);
            if (volume is null || volume.SizeBytes <= 0)
            {
                result.Level = HealthLevel.Unknown;
                result.Explanation = "GameBoost n'a pas trouvé le volume système, impossible de mesurer l'espace libre.";
                result.Impact = "Impossible de savoir si Windows et le jeu ont assez d'espace disponible.";
                result.Solution = "Relancez l'analyse. Si le problème persiste, vérifiez que votre disque est bien détecté dans Windows.";
                result.CurrentValue = "Volume système introuvable";
                return result;
            }

            var freePercent = volume.FreeBytes * 100.0 / volume.SizeBytes;
            result.CurrentValue = volume.Letter + " : " + AnalysisNative.FormatBytes(volume.FreeBytes) + " libres sur " +
                                  AnalysisNative.FormatBytes(volume.SizeBytes) + " (" + AnalysisNative.FormatPercent(freePercent) + ")";
            result.FixActionId = "navigate:storage";
            result.CanAutoFix = false;
            result.FixDescription = "Ouvrir la page Stockage";

            if (freePercent < 10)
            {
                result.Level = HealthLevel.Critical;
                result.Explanation = "Il ne reste que " + AnalysisNative.FormatPercent(freePercent) +
                                      " de libre sur le disque système. En dessous de 10 %, Windows ralentit et le jeu peut manquer de place pour ses fichiers.";
                result.Impact = "Téléchargements et mises à jour bloqués, ralentissements généraux, erreurs « espace insuffisant ».";
                result.Solution = "Ouvrez la page Stockage de GameBoost, puis désinstallez des programmes ou videz la corbeille.";
            }
            else if (freePercent < 20)
            {
                result.Level = HealthLevel.Warning;
                result.Explanation = "Il reste " + AnalysisNative.FormatPercent(freePercent) +
                                      " de libre sur le disque système, la marge est faible.";
                result.Impact = "Une mise à jour de jeu importante pourrait échouer faute de place.";
                result.Solution = "Libérez de la place via la page Stockage avant d'installer un nouveau jeu.";
            }
            else
            {
                result.Level = HealthLevel.Good;
                result.Explanation = "Le disque système dispose de " + AnalysisNative.FormatPercent(freePercent) + " d'espace libre.";
                result.Impact = "Aucun : l'espace disque est largement suffisant.";
                result.Solution = "Rien à faire.";
            }
        }
        catch (Exception ex)
        {
            return Failed(result, ex);
        }
        return result;
    }

    private CheckResult CheckStorageHealth()
    {
        var result = NewResult("storage-health", "Stockage", "État du stockage");
        try
        {
            var disks = DiskAnalyzer.Instance.Analyze();
            if (disks.Count == 0)
            {
                result.Level = HealthLevel.Unknown;
                result.Explanation = "Aucun disque n'a pu être interrogé. C'est généralement dû à un accès refusé : des droits administrateur sont requis.";
                result.Impact = "Impossible de savoir si vos disques sont en bonne santé.";
                result.Solution = "Relancez GameBoost en administrateur pour obtenir l'état SMART des disques.";
                result.CurrentValue = "Analyse indisponible (droits administrateur requis)";
                return result;
            }

            var criticals = new List<string>();
            var warnings = new List<string>();
            var parts = new List<string>();
            foreach (var disk in disks)
            {
                var label = string.IsNullOrWhiteSpace(disk.Model) ? "Disque" : disk.Model.Trim();
                var health = string.IsNullOrWhiteSpace(disk.SmartStatus) ? "Inconnu" : disk.SmartStatus;
                var temperature = disk.TemperatureC is double value ? ", " + value.ToString("0", CultureInfo.CurrentCulture) + " °C" : string.Empty;
                parts.Add(label + " : " + health + temperature);

                if (health == "Attention" || health == "Critique")
                    criticals.Add(label + " signale un état de santé « " + health + " »");

                foreach (var warning in disk.SmartWarnings)
                {
                    if (warning.Contains("erreurs", StringComparison.OrdinalIgnoreCase) &&
                        (warning.Contains("lecture", StringComparison.OrdinalIgnoreCase) || warning.Contains("écriture", StringComparison.OrdinalIgnoreCase)))
                        criticals.Add(label + " : " + warning);
                    else if (warning.Contains("État opérationnel anormal", StringComparison.OrdinalIgnoreCase))
                        criticals.Add(label + " : " + warning);
                    else if (warning.Contains("Usure du disque élevée", StringComparison.OrdinalIgnoreCase))
                        warnings.Add(label + " : " + warning);
                }

                if (disk.TemperatureC is >= 50)
                    warnings.Add(label + " chauffe à " + disk.TemperatureC.Value.ToString("0", CultureInfo.CurrentCulture) + " °C");
            }

            result.CurrentValue = string.Join(" · ", parts);

            if (criticals.Count > 0)
            {
                result.Level = HealthLevel.Critical;
                result.Explanation = "Le stockage signale un problème sérieux : " + string.Join(" ", criticals.Distinct());
                result.Impact = "Risque de pertes de données, de ralentissements ou d'échecs de chargement dans le jeu.";
                result.Solution = "Sauvegardez vos données importantes, puis vérifiez le disque avec l'outil de diagnostic du fabricant.";
            }
            else if (warnings.Count > 0)
            {
                result.Level = HealthLevel.Warning;
                result.Explanation = "Le stockage fonctionne mais mérite votre attention : " + string.Join(" ", warnings.Distinct());
                result.Impact = "Un disque chaud ou usé peut ralentir les chargements et perdre en fiabilité.";
                result.Solution = "Vérifiez la ventilation de votre PC et surveillez l'usure du disque dans la page Stockage.";
            }
            else if (disks.All(d => IsUnknownHealth(d.SmartStatus)))
            {
                result.Level = HealthLevel.Unknown;
                result.Explanation = "L'état SMART n'est pas accessible pour le moment : Windows ne renvoie aucune santé de disque " +
                                      "(lecture réservée aux droits administrateur selon les pilotes).";
                result.Impact = "Impossible de confirmer la santé des disques.";
                result.Solution = "Relancez GameBoost en administrateur pour tenter la lecture de l'état SMART.";
            }
            else
            {
                result.Level = HealthLevel.Good;
                result.Explanation = "Tous les disques signalent un état de santé normal et aucune erreur n'est enregistrée.";
                result.Impact = "Aucun : le stockage est en bon état.";
                result.Solution = "Rien à faire.";
            }
        }
        catch (Exception ex)
        {
            return Failed(result, ex);
        }
        return result;
    }

    private static bool IsUnknownHealth(string? status) =>
        string.IsNullOrWhiteSpace(status) ||
        status.Equals("Non disponible", StringComparison.OrdinalIgnoreCase) ||
        status.Equals("Inconnu", StringComparison.OrdinalIgnoreCase);

    private CheckResult CheckCpuTemperature()
    {
        return CheckTemperature("cpu-temperature", "Température du processeur", 90, 80, "processeur");
    }

    private CheckResult CheckGpuTemperature()
    {
        return CheckTemperature("gpu-temperature", "Température de la carte graphique", 85, 75, "carte graphique");
    }

    private CheckResult CheckTemperature(string id, string title, double critical, double warning, string label)
    {
        var result = NewResult(id, "Températures", title);
        try
        {
            var snapshot = HardwareSensors.Instance.Read();
            var temperature = id == "cpu-temperature" ? snapshot.CpuTemperatureC : snapshot.GpuTemperatureC;
            if (temperature is null || temperature.Value <= 0)
            {
                var unreadable = temperature is not null;
                result.Level = HealthLevel.Unknown;
                result.Explanation = unreadable
                    ? "Le capteur de température du " + label + " ne renvoie aucune valeur exploitable (0 °C relevé). GameBoost ne considère pas cette valeur comme une mesure."
                    : "Mesure indisponible sans administrateur : les capteurs de température du " + label +
                      " ne sont pas accessibles dans l'état actuel.";
                result.Impact = "GameBoost ne peut pas vous prévenir d'une surchauffe pendant la partie.";
                if (snapshot.Available && unreadable)
                {
                    result.Solution = "Ce matériel ne communique pas sa température à Windows : GameBoost ne peut pas la mesurer, " +
                                      "même en administrateur. Suivez la température avec l'outil du fabricant si nécessaire.";
                    result.CurrentValue = "Aucune valeur exploitable (0 °C relevé)";
                }
                else
                {
                    result.Solution = "Relancez GameBoost en administrateur pour lire les températures.";
                    result.CurrentValue = unreadable ? "Aucune valeur exploitable (0 °C relevé)" :
                        snapshot.Available ? "Aucune température renvoyée par le capteur" : "Capteurs matériels inaccessibles";
                    result.FixActionId = "system:elevate";
                    result.CanAutoFix = true;
                    result.FixDescription = "Relancer GameBoost en administrateur";
                }
                return result;
            }

            result.CurrentValue = temperature.Value.ToString("0", CultureInfo.CurrentCulture) + " °C";
            if (temperature.Value >= critical)
            {
                result.Level = HealthLevel.Critical;
                result.Explanation = "Le " + label + " atteint " + result.CurrentValue + ", ce qui est au-dessus de la limite critique de " +
                                     critical.ToString("0", CultureInfo.CurrentCulture) + " °C.";
                result.Impact = "Le matériel réduit automatiquement sa fréquence pour se protéger : fortes saccades et perte de performance dans le jeu.";
                result.Solution = "Fermez le jeu, laissez le PC refroidir, nettoyez la ventilation et vérifiez que les grilles ne sont pas obstruées.";
            }
            else if (temperature.Value >= warning)
            {
                result.Level = HealthLevel.Warning;
                result.Explanation = "Le " + label + " atteint " + result.CurrentValue + ", au-dessus de " +
                                     warning.ToString("0", CultureInfo.CurrentCulture) + " °C, la température devient préoccupante.";
                result.Impact = "Un throttling thermique peut intervenir en pleine partie et faire baisser les images par seconde.";
                result.Solution = "Améliorez la ventilation de la pièce, évitez de jouer sur une surface molle, et vérifiez la poussière dans le PC.";
            }
            else
            {
                result.Level = HealthLevel.Good;
                result.Explanation = "Le " + label + " fonctionne à " + result.CurrentValue + ", une valeur normale en utilisation.";
                result.Impact = "Aucun : pas de risque de surchauffe.";
                result.Solution = "Rien à faire.";
            }
        }
        catch (Exception ex)
        {
            return Failed(result, ex);
        }
        return result;
    }

    private CheckResult CheckGpuDriverAge()
    {
        var result = NewResult("gpu-driver-age", "Pilotes", "Ancienneté du pilote de la carte graphique");
        try
        {
            if (_hardware is null)
                _hardware = HardwareDetector.Instance.Collect();

            var gpu = _hardware.Gpu;
            var driverDate = string.IsNullOrWhiteSpace(gpu.DriverDate) ? string.Empty : gpu.DriverDate.Trim();
            if (driverDate.Length == 0 || driverDate == "Inconnue" || driverDate == "Inconnu")
            {
                result.Level = HealthLevel.Unknown;
                result.Explanation = "Windows n'a pas communiqué la date du pilote de la carte graphique.";
                result.Impact = "Impossible de savoir si votre pilote est à jour.";
                result.Solution = "Mettez à jour vos pilotes depuis le gestionnaire de périphériques ou le site du fabricant.";
                result.CurrentValue = "Date du pilote indisponible";
                return result;
            }

            if (!DateTime.TryParseExact(driverDate, "dd/MM/yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
            {
                result.Level = HealthLevel.Unknown;
                result.Explanation = "La date du pilote (" + driverDate + ") n'a pas pu être interprétée.";
                result.Impact = "Impossible de calculer l'ancienneté du pilote.";
                result.Solution = "Vérifiez la date du pilote dans le gestionnaire de périphériques.";
                result.CurrentValue = driverDate;
                return result;
            }

            var months = (int)Math.Round((DateTime.Now - parsed).TotalDays / 30.44);
            var version = string.IsNullOrWhiteSpace(gpu.DriverVersion) ? "version inconnue" : gpu.DriverVersion.Trim();
            result.CurrentValue = "Pilote " + version + " du " + driverDate + " (" + months + " mois) — " + gpu.Name;

            var vendorSite = gpu.Vendor switch
            {
                GpuVendor.Nvidia => "nvidia.com/drivers",
                GpuVendor.Amd => "amd.com/support",
                GpuVendor.Intel => "intel.com/support",
                _ => "le site du fabricant de votre carte graphique"
            };
            result.FixActionId = "navigate:gpu";
            result.CanAutoFix = false;
            result.FixDescription = "Ouvrir la page Matériel pour voir les informations de la carte graphique";

            if (months > 30)
            {
                result.Level = HealthLevel.Critical;
                result.Explanation = "Votre pilote de carte graphique date de " + months + " mois, ce qui est très ancien pour un jeu récent.";
                result.Impact = "Bugs graphiques, absence de profils pour les nouveaux jeux et performances en deçà de ce que votre carte peut donner.";
                result.Solution = "Téléchargez le dernier pilote sur " + vendorSite + " et installez-le, puis redémarrez le PC.";
            }
            else if (months > 18)
            {
                result.Level = HealthLevel.Warning;
                result.Explanation = "Votre pilote de carte graphique date de " + months + " mois, il dépasse 18 mois.";
                result.Impact = "Les optimisations récentes des jeux sont probablement absentes de ce pilote.";
                result.Solution = "Mettez à jour le pilote depuis " + vendorSite + ".";
            }
            else
            {
                result.Level = HealthLevel.Good;
                result.Explanation = "Le pilote date de " + months + " mois, il est suffisamment récent pour jouer correctement.";
                result.Impact = "Aucun : le pilote est à jour.";
                result.Solution = "Rien à faire.";
            }
        }
        catch (Exception ex)
        {
            return Failed(result, ex);
        }
        return result;
    }

    private CheckResult CheckGameMode()
    {
        var result = NewResult("windows-game-mode", "Windows", "Mode Jeu Windows");
        try
        {
            var (success, present, value) = AnalysisNative.ReadGameMode();
            if (!success)
            {
                result.Level = HealthLevel.Unknown;
                result.Explanation = "La valeur du Mode Jeu Windows n'a pas pu être lue dans le registre.";
                result.Impact = "Impossible de savoir si Windows priorise le jeu en ce moment.";
                result.Solution = "Relancez l'analyse. Si le problème persiste, relancez GameBoost en administrateur.";
                result.CurrentValue = "Lecture impossible";
                return result;
            }

            result.FixActionId = "system:game-mode";
            result.CanAutoFix = true;
            result.FixDescription = "Active le Mode Jeu Windows (réversible)";

            if (present && value == 0)
            {
                result.Level = HealthLevel.Warning;
                result.Explanation = "Le Mode Jeu Windows est désactivé (AutoGameModeEnabled = 0). Windows ne priorise donc pas les ressources du jeu.";
                result.Impact = "Windows peut laisser des tâches en arrière-plan consommer du processeur pendant votre partie.";
                result.Solution = "Activez le Mode Jeu Windows avec le bouton ci-contre, ou dans Paramètres Windows > Jeu > Mode Jeu.";
                result.CurrentValue = "Désactivé (0)";
            }
            else
            {
                result.Level = HealthLevel.Good;
                result.Explanation = present
                    ? "Le Mode Jeu Windows est activé (AutoGameModeEnabled = 1)."
                    : "La clé AutoGameModeEnabled est absente : Windows utilise alors le Mode Jeu activé par défaut.";
                result.Impact = "Aucun : Windows donne la priorité aux ressources du jeu.";
                result.Solution = "Rien à faire.";
                result.CurrentValue = present ? "Activé (1)" : "Valeur absente (activé par défaut Windows)";
            }
        }
        catch (Exception ex)
        {
            return Failed(result, ex);
        }
        return result;
    }

    private CheckResult CheckPowerPlan()
    {
        var result = NewResult("power-plan", "Windows", "Plan d'alimentation");
        try
        {
            var (exitCode, output) = AnalysisNative.RunCommand("powercfg.exe", "/getactivescheme");
            if (exitCode != 0)
            {
                result.Level = HealthLevel.Unknown;
                result.Explanation = "La commande powercfg n'a pas pu lire le plan d'alimentation actif.";
                result.Impact = "Impossible de savoir si Windows limite la puissance de votre PC.";
                result.Solution = "Relancez l'analyse ; si le problème persiste, relancez GameBoost en administrateur.";
                result.CurrentValue = "Commande powercfg inaccessible";
                return result;
            }

            var guid = AnalysisNative.ExtractGuid(output);
            var known = AnalysisNative.DescribeScheme(guid);
            var label = string.IsNullOrWhiteSpace(known) ? AnalysisNative.ExtractSchemeLabel(output) : known;
            if (string.IsNullOrWhiteSpace(label)) label = "Plan personnalisé";
            result.CurrentValue = label + (guid.Length > 0 ? " (" + guid + ")" : string.Empty);

            result.FixActionId = "system:power-plan";
            result.CanAutoFix = true;
            result.FixDescription = "Bascule vers Hautes performances (l'ancien plan est restauré à la fin du boost)";

            if (guid.Equals(AnalysisNative.HighPerformanceScheme, StringComparison.OrdinalIgnoreCase) ||
                label.Contains("hautes performances", StringComparison.OrdinalIgnoreCase) ||
                label.Contains("high performance", StringComparison.OrdinalIgnoreCase))
            {
                result.Level = HealthLevel.Good;
                result.Explanation = "Le plan « Hautes performances » est actif : le processeur n'est pas bridé.";
                result.Impact = "Aucun : Windows laisse le matériel tourner à pleine puissance.";
                result.Solution = "Rien à faire.";
            }
            else if (guid.Equals(AnalysisNative.BalancedScheme, StringComparison.OrdinalIgnoreCase) ||
                     label.Contains("équilibré", StringComparison.OrdinalIgnoreCase) ||
                     label.Contains("balanced", StringComparison.OrdinalIgnoreCase) ||
                     guid.Equals(AnalysisNative.PowerSaverScheme, StringComparison.OrdinalIgnoreCase) ||
                     label.Contains("économie", StringComparison.OrdinalIgnoreCase) ||
                     label.Contains("power saver", StringComparison.OrdinalIgnoreCase))
            {
                result.Level = HealthLevel.Warning;
                result.Explanation = "Le plan « " + label + " » est actif : les performances peuvent être bridées pour faire des économies d'énergie.";
                result.Impact = "Processeur moins cadencé, images par seconde plus basses et possible latence accrue dans le jeu.";
                result.Solution = "Basculez vers le plan « Hautes performances » avec le bouton ci-contre. GameBoost restaure votre plan d'origine à la fin du boost.";
            }
            else
            {
                result.Level = HealthLevel.Unknown;
                result.Explanation = "Le plan actif (" + label + ") n'est pas l'un des plans standard de Windows : GameBoost ne peut pas dire s'il est optimisé pour le jeu.";
                result.Impact = "Un plan personnalisé peut limiter la puissance du processeur sans que cela soit visible.";
                result.Solution = "Vérifiez dans les paramètres d'alimentation Windows que le processeur n'est pas limité à un pourcentage bas.";
                result.CanAutoFix = true;
            }
        }
        catch (Exception ex)
        {
            return Failed(result, ex);
        }
        return result;
    }

    private CheckResult CheckStartupEntries()
    {
        var result = NewResult("startup-entries", "Windows", "Programmes au démarrage de Windows");
        try
        {
            var entries = AnalysisNative.ReadStartupEntries();
            result.CurrentValue = entries.Count + (entries.Count <= 1 ? " entrée" : " entrées") +
                                  (entries.Count > 0 ? " : " + string.Join(", ", entries.Take(8)) : string.Empty);

            if (entries.Count > 6)
            {
                result.Level = HealthLevel.Warning;
                result.Explanation = entries.Count + " programmes démarrent avec Windows (" + string.Join(", ", entries.Take(8)) +
                                      (entries.Count > 8 ? ", …" : string.Empty) + "). Ils consomment de la mémoire et du processeur avant même que vous ne jouiez.";
                result.Impact = "Moins de mémoire disponible au démarrage et processus inutiles en arrière-plan pendant la partie.";
                result.Solution = "Ouvrez le Gestionnaire des tâches (Ctrl + Maj + Échap), onglet Démarrage, et désactivez les entrées dont vous n'avez pas besoin. GameBoost ne modifie pas cette liste automatiquement.";
                result.CanAutoFix = false;
            }
            else
            {
                result.Level = HealthLevel.Good;
                result.Explanation = entries.Count == 0
                    ? "Aucun programme supplémentaire ne démarre avec Windows."
                    : entries.Count + " programmes démarrent avec Windows, ce nombre est raisonnable.";
                result.Impact = "Aucun impact notable sur les performances de votre PC.";
                result.Solution = "Rien à faire.";
            }
        }
        catch (Exception ex)
        {
            return Failed(result, ex);
        }
        return result;
    }

    private CheckResult CheckElevation()
    {
        var result = NewResult("admin-elevation", "Windows", "Exécution en tant qu'administrateur");
        try
        {
            result.FixActionId = "system:elevate";
            result.CanAutoFix = true;
            result.FixDescription = "Relancer GameBoost en administrateur";

            if (AppPaths.IsElevated)
            {
                result.Level = HealthLevel.Good;
                result.Explanation = "GameBoost tourne avec les droits administrateur : toutes les mesures sont accessibles.";
                result.Impact = "Aucun : les vérifications sont complètes.";
                result.Solution = "Rien à faire.";
                result.CurrentValue = "Administrateur";
            }
            else
            {
                result.Level = HealthLevel.Warning;
                result.Explanation = "GameBoost fonctionne sans administrateur : certaines mesures sont indisponibles (températures, santé SMART des disques, détails matériels).";
                result.Impact = "Certaines vérifications afficheront « indisponible » au lieu d'une vraie mesure.";
                result.Solution = "Relancez GameBoost en administrateur depuis le menu démarrage pour obtenir des mesures complètes.";
                result.CurrentValue = "Utilisateur standard";
            }
        }
        catch (Exception ex)
        {
            return Failed(result, ex);
        }
        return result;
    }

    private CheckResult CheckRefreshRate()
    {
        var result = NewResult("display-refresh-rate", "Affichage", "Taux de rafraîchissement de l'écran");
        try
        {
            var display = SelectPrimaryDisplay();
            if (display is null)
            {
                result.Level = HealthLevel.Unknown;
                result.Explanation = "Aucun écran n'a été détecté, impossible de comparer les fréquences.";
                result.Impact = "Impossible de savoir si votre écran utilise sa pleine capacité.";
                result.Solution = "Vérifiez la connexion de votre écran puis relancez l'analyse.";
                result.CurrentValue = "Aucun écran détecté";
                return result;
            }

            var max = AnalysisNative.DetectMaxMode(display.DeviceName, display.Width, display.Height);
            if (max is null || max.Frequency <= 0 || display.RefreshRate <= 0)
            {
                result.Level = HealthLevel.Unknown;
                result.Explanation = "La fréquence maximale prise en charge par cet écran n'a pas pu être déterminée.";
                result.Impact = "Impossible de vérifier si votre écran fonctionne à son meilleur taux de rafraîchissement.";
                result.Solution = "Comparez les valeurs dans Paramètres Windows > Système > Affichage > Paramètres d'affichage avancés.";
                result.CurrentValue = display.RefreshRate + " Hz (maximum non déterminé)";
                return result;
            }

            result.CurrentValue = display.RefreshRate + " Hz utilisé(s) sur " + max.Frequency + " Hz possible(s)";
            if (display.RefreshRate < max.Frequency)
            {
                result.Level = HealthLevel.Warning;
                result.Explanation = "Votre écran peut fonctionner à " + max.Frequency + " Hz mais Windows utilise " + display.RefreshRate + " Hz.";
                result.Impact = "Images par seconde plafonnées inutilement et image moins fluide qu'elle ne pourrait l'être.";
                result.Solution = "Ouvrez Paramètres Windows > Système > Affichage > Paramètres d'affichage avancés > Propriétés du moniteur, " +
                                  "choisissez " + max.Frequency + " Hz dans la liste « Taux de rafraîchissement », puis appliquez. " +
                                  "GameBoost ne modifie pas ces réglages automatiquement.";
                result.CanAutoFix = false;
            }
            else
            {
                result.Level = HealthLevel.Good;
                result.Explanation = "Votre écran utilise son taux de rafraîchissement maximal (" + max.Frequency + " Hz).";
                result.Impact = "Aucun : l'écran est réglé au maximum.";
                result.Solution = "Rien à faire.";
            }
        }
        catch (Exception ex)
        {
            return Failed(result, ex);
        }
        return result;
    }

    private CheckResult CheckDisplayResolution()
    {
        var result = NewResult("display-resolution", "Affichage", "Résolution de l'écran");
        try
        {
            var display = SelectPrimaryDisplay();
            if (display is null)
            {
                result.Level = HealthLevel.Unknown;
                result.Explanation = "Aucun écran n'a été détecté, impossible de comparer les résolutions.";
                result.Impact = "Impossible de savoir si Windows utilise la résolution native de l'écran.";
                result.Solution = "Vérifiez la connexion de votre écran puis relancez l'analyse.";
                result.CurrentValue = "Aucun écran détecté";
                return result;
            }

            var max = AnalysisNative.DetectMaxMode(display.DeviceName, display.Width, display.Height);
            result.CurrentValue = display.Width + " x " + display.Height +
                                  (max is null ? string.Empty : " utilisé(s) sur " + max.Width + " x " + max.Height + " pris en charge");

            if (max is null || (long)max.Width * max.Height <= (long)display.Width * display.Height)
            {
                result.Level = HealthLevel.Good;
                result.Explanation = max is null
                    ? "La résolution maximale n'a pas été déterminée, mais Windows utilise la résolution courante de l'écran."
                    : "Windows utilise la résolution la plus élevée prise en charge par cet écran.";
                result.Impact = "Aucun : la résolution est correctement réglée.";
                result.Solution = "Rien à faire.";
                return result;
            }

            result.Level = HealthLevel.Warning;
            result.Explanation = "Votre affichage prend en charge " + max.Width + " x " + max.Height +
                                  " mais Windows utilise " + display.Width + " x " + display.Height + ".";
            result.Impact = "L'image est affichée moins nettement que possible, et certains jeux repartent de cette résolution.";
            result.Solution = "Ouvrez Paramètres Windows > Système > Affichage, choisissez la résolution recommandée dans « Résolution d'écran », " +
                              "puis appliquez. GameBoost ne modifie pas ces réglages automatiquement.";
            result.CanAutoFix = false;
        }
        catch (Exception ex)
        {
            return Failed(result, ex);
        }
        return result;
    }

    private DisplayInfo? SelectPrimaryDisplay()
    {
        var displays = HardwareDetector.Instance.GetDisplays();
        return displays.FirstOrDefault(d => d.Primary) ?? displays.FirstOrDefault();
    }

    private void EnsureMonitorSample()
    {
        try
        {
            var monitor = SystemMonitor.Instance;
            var current = monitor.Current;
            if (current.RamTotalBytes > 0 && (DateTime.Now - current.Timestamp).TotalSeconds < 5) return;

            if (!monitor.IsRunning)
            {
                monitor.Start();
                _startedMonitor = true;
            }

            var deadline = DateTime.UtcNow.AddSeconds(5);
            while (DateTime.UtcNow < deadline)
            {
                var sample = monitor.Current;
                if (sample.RamTotalBytes > 0 && sample.Timestamp > current.Timestamp) return;
                Thread.Sleep(150);
            }
            Log.Warn("Analysis", "Aucune mesure du moniteur reçue dans le délai imparti");
        }
        catch (Exception ex)
        {
            Log.Warn("Analysis", "Démarrage du moniteur impossible : " + ex.Message);
        }
    }

    private List<ProcessSnapshot> EnsureProcessSample()
    {
        if (_processSample is not null) return _processSample;

        _excludedPids = CollectExcludedProcessIds();
        _ = ProcessService.Instance.GetProcesses();
        Thread.Sleep(700);
        _processSample = ProcessService.Instance.GetProcesses();
        Log.Debug("Analysis", "Instantané des processus : " + _processSample.Count + " processus mesurés");
        return _processSample;
    }

    private HashSet<int> CollectExcludedProcessIds()
    {
        var excluded = new HashSet<int>();
        try
        {
            var tracked = SystemMonitor.Instance.TrackedProcessId;
            if (tracked is int pid && pid > 0) excluded.Add(pid);
        }
        catch (Exception ex)
        {
            Log.Warn("Analysis", "Processus suivi introuvable : " + ex.Message);
        }

        try
        {
            foreach (var game in GameScanner.Instance.GetLibrary())
            {
                if (game.IsRunning && game.RunningProcessId > 0) excluded.Add(game.RunningProcessId);
            }
        }
        catch (Exception ex)
        {
            Log.Warn("Analysis", "Bibliothèque de jeux indisponible pour l'exclusion : " + ex.Message);
        }
        return excluded;
    }
}
