using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text.Json;
using GameBoost.Core.Data;
using GameBoost.Core.Logging;
using GameBoost.Core.Models;

namespace GameBoost.Core.Analysis;

public sealed partial class Analyzer
{
    private static readonly JsonSerializerOptions BackupJsonOptions = new() { WriteIndented = true };

    public FixOutcome ApplyFix(string fixActionId, string? payload = null)
    {
        if (string.IsNullOrWhiteSpace(fixActionId))
        {
            Log.Warn("Analysis", "Correction refusée : aucun identifiant fourni");
            return new FixOutcome { Success = false, Message = "Aucune action de correction fournie. Aucune modification effectuée." };
        }

        try
        {
            var id = fixActionId.Trim();
            if (id.StartsWith("navigate:", StringComparison.OrdinalIgnoreCase)) return NavigateFix(id, payload);
            if (id.Equals("system:game-mode", StringComparison.OrdinalIgnoreCase)) return GameModeFix();
            if (id.Equals("system:power-plan", StringComparison.OrdinalIgnoreCase)) return PowerPlanFix();
            if (id.Equals("system:elevate", StringComparison.OrdinalIgnoreCase))
            {
                return new FixOutcome
                {
                    Success = true,
                    Message = "Relancez GameBoost en administrateur depuis le menu démarrage (l'application le fera pour vous si le paramètre est activé)"
                };
            }

            Log.Warn("Analysis", "Mécanisme de correction inconnu : " + id);
            return new FixOutcome
            {
                Success = false,
                Message = "Mécanisme de correction inconnu : « " + id + " ». Aucune modification effectuée."
            };
        }
        catch (Exception ex)
        {
            Log.Error("Analysis", "Correction " + fixActionId + " impossible", ex);
            return new FixOutcome { Success = false, Message = "La correction a échoué : " + ex.Message };
        }
    }

    private static FixOutcome NavigateFix(string id, string? payload)
    {
        var target = id["navigate:".Length..].Trim();
        var details = string.IsNullOrWhiteSpace(payload)
            ? string.Empty
            : " Éléments concernés : " + payload.Trim() + ".";
        string message;
        switch (target.ToLowerInvariant())
        {
            case "processes":
                message = "Ouvrez la page Applications de GameBoost pour gérer les programmes listés." + details;
                break;
            case "storage":
                message = "Ouvrez la page Stockage de GameBoost pour libérer de l'espace disque." + details;
                break;
            case "gpu":
                message = "Ouvrez la page Matériel de GameBoost, puis récupérez le pilote de la carte graphique sur le site du fabricant." + details;
                break;
            case "display":
                message = "Ouvrez la page Matériel de GameBoost pour voir les informations de l'écran." + details;
                break;
            default:
                message = "Ouvrez la page « " + target + " » de GameBoost." + details;
                break;
        }
        Log.Info("Analysis", "Navigation demandée : " + id);
        return new FixOutcome { Success = true, Message = message };
    }

    private static FixOutcome GameModeFix()
    {
        var (readOk, present, value) = AnalysisNative.ReadGameMode();
        if (!readOk)
        {
            return new FixOutcome
            {
                Success = false,
                Message = "Impossible de lire le Mode Jeu Windows dans le registre.",
                RequiresElevation = !AppPaths.IsElevated
            };
        }

        if (present && value == 1)
        {
            return new FixOutcome { Success = true, Message = "Le Mode Jeu Windows est déjà activé, aucune modification nécessaire." };
        }

        var backupDir = AppPaths.CreateBackupFolder("fix");
        var backupFile = Path.Combine(backupDir, "gamemode.json");
        var backup = new Dictionary<string, object?>
        {
            ["key"] = @"HKEY_CURRENT_USER\" + AnalysisNative.GameBarKeyPath,
            ["valueName"] = AnalysisNative.GameModeValueName,
            ["present"] = present,
            ["value"] = present ? value : null,
            ["savedAt"] = DateTime.Now.ToString("O")
        };
        File.WriteAllText(backupFile, JsonSerializer.Serialize(backup, BackupJsonOptions));

        try
        {
            AnalysisNative.WriteGameMode(1);
        }
        catch (Exception ex)
        {
            Log.Error("Analysis", "Activation du Mode Jeu Windows impossible", ex);
            return new FixOutcome
            {
                Success = false,
                Message = "Activation du Mode Jeu Windows impossible : " + ex.Message,
                RequiresElevation = IsAccessDenied(ex),
                BackupPath = backupDir
            };
        }

        Log.Info("Analysis", "Mode Jeu Windows activé, sauvegarde : " + backupDir);
        return new FixOutcome
        {
            Success = true,
            Message = "Mode Jeu Windows activé. L'ancienne valeur est conservée dans " + backupDir + ".",
            BackupPath = backupDir
        };
    }

    private static FixOutcome PowerPlanFix()
    {
        var (readExit, readOutput) = AnalysisNative.RunCommand("powercfg.exe", "/getactivescheme");
        if (readExit != 0)
        {
            return new FixOutcome
            {
                Success = false,
                Message = "Lecture du plan d'alimentation actif impossible (code " + readExit + ").",
                RequiresElevation = !AppPaths.IsElevated
            };
        }

        var currentGuid = AnalysisNative.ExtractGuid(readOutput);
        var currentLabel = AnalysisNative.DescribeScheme(currentGuid);
        if (string.IsNullOrWhiteSpace(currentLabel)) currentLabel = AnalysisNative.ExtractSchemeLabel(readOutput);
        if (string.IsNullOrWhiteSpace(currentLabel)) currentLabel = "Plan inconnu";

        if (currentGuid.Equals(AnalysisNative.HighPerformanceScheme, StringComparison.OrdinalIgnoreCase) ||
            readOutput.Contains("hautes performances", StringComparison.OrdinalIgnoreCase) ||
            readOutput.Contains("high performance", StringComparison.OrdinalIgnoreCase))
        {
            return new FixOutcome { Success = true, Message = "Le plan « Hautes performances » est déjà actif, aucune modification nécessaire." };
        }

        var backupDir = AppPaths.CreateBackupFolder("fix");
        var backupFile = Path.Combine(backupDir, "powerplan.json");
        var backup = new Dictionary<string, object?>
        {
            ["schemeGuid"] = currentGuid,
            ["schemeName"] = currentLabel,
            ["rawOutput"] = readOutput.Trim(),
            ["savedAt"] = DateTime.Now.ToString("O")
        };
        File.WriteAllText(backupFile, JsonSerializer.Serialize(backup, BackupJsonOptions));

        var switchExit = -1;
        var usedFallback = false;
        var attempt = AnalysisNative.RunCommand("powercfg.exe", "/setactive " + AnalysisNative.HighPerformanceScheme);
        switchExit = attempt.ExitCode;
        if (switchExit != 0)
        {
            var fallback = AnalysisNative.RunCommand("powercfg.exe", "/setactive SCHEME_MIN");
            if (fallback.ExitCode == 0)
            {
                switchExit = 0;
                usedFallback = true;
            }
        }

        if (switchExit != 0)
        {
            Log.Error("Analysis", "Bascule du plan d'alimentation refusée (code " + switchExit + ")");
            return new FixOutcome
            {
                Success = false,
                Message = "Bascule vers « Hautes performances » refusée par Windows (code " + switchExit +
                          "). Votre plan « " + currentLabel + " » est conservé.",
                RequiresElevation = !AppPaths.IsElevated,
                BackupPath = backupDir
            };
        }

        var (verifyExit, verifyOutput) = AnalysisNative.RunCommand("powercfg.exe", "/getactivescheme");
        var newGuid = verifyExit == 0 ? AnalysisNative.ExtractGuid(verifyOutput) : string.Empty;
        var newLabel = AnalysisNative.DescribeScheme(newGuid);
        if (string.IsNullOrWhiteSpace(newLabel)) newLabel = usedFallback ? "Plan sélectionné par Windows" : "Hautes performances";

        Log.Info("Analysis", "Plan d'alimentation basculé de " + currentGuid + " vers " + newGuid + ", sauvegarde : " + backupDir);
        return new FixOutcome
        {
            Success = true,
            Message = "Plan « " + newLabel + " » activé. Votre plan d'origine « " + currentLabel + " » est conservé dans " + backupDir + ".",
            BackupPath = backupDir
        };
    }

    private static bool IsAccessDenied(Exception ex)
    {
        for (var current = ex; current is not null; current = current.InnerException)
        {
            if (current is UnauthorizedAccessException) return true;
            if (current is Win32Exception win32 && (win32.NativeErrorCode == 5 || (uint)win32.NativeErrorCode == 0xC0000022u)) return true;
            if (current is ExternalException && current.Message.Contains("refus", StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }
}
