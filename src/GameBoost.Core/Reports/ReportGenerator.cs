using System.Globalization;
using System.Net;
using System.Text;
using GameBoost.Core.Data;
using GameBoost.Core.Logging;
using GameBoost.Core.Models;

namespace GameBoost.Core.Reports;

public sealed class ReportGenerator
{
    public static ReportGenerator Instance { get; } = new();

    private static readonly CultureInfo French = CultureInfo.GetCultureInfo("fr-FR");

    private const string Styles = """
        :root {
            --bg: #0d1117;
            --panel: #151b26;
            --panel-alt: #1b2331;
            --border: #263042;
            --text: #e8edf7;
            --muted: #97a3b8;
            --accent: #4da3ff;
            --good: #2ecc71;
            --warn: #f1c40f;
            --crit: #e74c3c;
            --neutral: #7f8c9b;
        }
        * { box-sizing: border-box; }
        body {
            margin: 0;
            background: var(--bg);
            color: var(--text);
            font-family: "Segoe UI", Tahoma, Geneva, Verdana, sans-serif;
            font-size: 15px;
            line-height: 1.55;
        }
        .wrap { max-width: 1060px; margin: 0 auto; padding: 34px 26px 70px; }
        header.report { border-bottom: 1px solid var(--border); padding-bottom: 22px; margin-bottom: 30px; }
        .brand { font-size: 13px; letter-spacing: 3px; text-transform: uppercase; color: var(--accent); margin: 0 0 8px; }
        h1 { font-size: 29px; margin: 0 0 10px; font-weight: 650; }
        .meta { color: var(--muted); font-size: 14px; margin: 0; }
        .summary { margin: 16px 0 0; padding: 12px 16px; background: var(--panel); border: 1px solid var(--border); border-radius: 8px; }
        h2 { font-size: 20px; margin: 38px 0 16px; padding-bottom: 9px; border-bottom: 1px solid var(--border); font-weight: 620; }
        h3 { font-size: 16px; margin: 0 0 6px; }
        .grid { display: grid; grid-template-columns: repeat(auto-fit, minmax(300px, 1fr)); gap: 14px; }
        .card { background: var(--panel); border: 1px solid var(--border); border-radius: 10px; padding: 16px 18px; }
        .card.alt { background: var(--panel-alt); }
        .kv { display: flex; justify-content: space-between; gap: 14px; padding: 5px 0; border-bottom: 1px dashed rgba(255,255,255,.06); font-size: 14px; }
        .kv:last-child { border-bottom: none; }
        .kv .k { color: var(--muted); white-space: nowrap; }
        .kv .v { text-align: right; word-break: break-word; }
        .label { color: var(--muted); font-size: 12px; text-transform: uppercase; letter-spacing: 1px; margin-bottom: 8px; }
        .check { background: var(--panel); border: 1px solid var(--border); border-left-width: 4px; border-radius: 10px; padding: 16px 18px; margin-bottom: 14px; }
        .check.good { border-left-color: var(--good); }
        .check.warning { border-left-color: var(--warn); }
        .check.critical { border-left-color: var(--crit); }
        .check.unknown { border-left-color: var(--neutral); }
        .check-head { display: flex; align-items: center; gap: 12px; flex-wrap: wrap; }
        .dot { width: 13px; height: 13px; border-radius: 50%; display: inline-block; flex: 0 0 auto; box-shadow: 0 0 8px rgba(0,0,0,.45) inset; }
        .dot.good { background: var(--good); }
        .dot.warning { background: var(--warn); }
        .dot.critical { background: var(--crit); }
        .dot.unknown { background: var(--neutral); }
        .pill { font-size: 12px; padding: 3px 11px; border-radius: 999px; border: 1px solid var(--border); color: var(--muted); }
        .pill.good { color: var(--good); border-color: rgba(46,204,113,.5); }
        .pill.warning { color: var(--warn); border-color: rgba(241,196,15,.5); }
        .pill.critical { color: var(--crit); border-color: rgba(231,76,60,.5); }
        .cat { font-size: 12px; color: var(--muted); margin-left: auto; }
        .field { margin-top: 11px; font-size: 14px; }
        .field b { color: var(--accent); font-weight: 600; }
        .value { display: inline-block; background: var(--panel-alt); border: 1px solid var(--border); border-radius: 6px; padding: 2px 9px; font-family: Consolas, "Courier New", monospace; }
        ul.reco { list-style: none; margin: 0; padding: 0; }
        ul.reco li { background: var(--panel); border: 1px solid var(--border); border-radius: 8px; padding: 12px 16px; margin-bottom: 10px; }
        ul.reco li .src { display: block; color: var(--muted); font-size: 13px; margin-top: 4px; }
        ul.plain { margin: 6px 0 0; padding-left: 20px; }
        ul.plain li { margin-bottom: 6px; }
        table { width: 100%; border-collapse: collapse; background: var(--panel); border: 1px solid var(--border); border-radius: 10px; overflow: hidden; font-size: 14px; }
        th, td { padding: 11px 13px; text-align: left; border-bottom: 1px solid var(--border); }
        th { background: var(--panel-alt); color: var(--muted); font-weight: 600; font-size: 12px; text-transform: uppercase; letter-spacing: .6px; }
        tr:last-child td { border-bottom: none; }
        td.num { text-align: right; font-family: Consolas, "Courier New", monospace; white-space: nowrap; }
        .delta { display: inline-block; min-width: 74px; padding: 3px 9px; border-radius: 6px; text-align: right; font-family: Consolas, "Courier New", monospace; }
        .delta.pos { background: rgba(46,204,113,.16); color: var(--good); border: 1px solid rgba(46,204,113,.45); }
        .delta.neg { background: rgba(231,76,60,.16); color: var(--crit); border: 1px solid rgba(231,76,60,.45); }
        .delta.flat { background: rgba(127,140,155,.16); color: var(--muted); border: 1px solid var(--border); }
        .bar { height: 7px; border-radius: 4px; background: var(--panel-alt); margin-top: 6px; overflow: hidden; }
        .bar span { display: block; height: 100%; border-radius: 4px; }
        .bar span.pos { background: var(--good); }
        .bar span.neg { background: var(--crit); }
        .stats { display: grid; grid-template-columns: repeat(auto-fit, minmax(170px, 1fr)); gap: 14px; }
        .stat { background: var(--panel); border: 1px solid var(--border); border-radius: 10px; padding: 16px; text-align: center; }
        .stat .big { font-size: 30px; font-weight: 700; display: block; }
        .stat.good .big { color: var(--good); }
        .stat.warning .big { color: var(--warn); }
        .stat.critical .big { color: var(--crit); }
        .stat .cap { color: var(--muted); font-size: 13px; text-transform: uppercase; letter-spacing: .8px; }
        footer.report { margin-top: 46px; padding-top: 18px; border-top: 1px solid var(--border); color: var(--muted); font-size: 13px; }
        .empty { color: var(--muted); font-style: italic; }
        @media print { body { background: #fff; color: #000; } }
        """;

    private ReportGenerator()
    {
    }

    public string GetExportDirectory()
    {
        try
        {
            var backup = SettingsService.Current.BackupLocation;
            if (!string.IsNullOrWhiteSpace(backup))
            {
                Directory.CreateDirectory(backup);
                return backup;
            }
        }
        catch (Exception ex)
        {
            Log.Warn("Reports", "Dossier de sauvegarde inutilisable : " + ex.Message);
        }
        return AppPaths.ExportsDir;
    }

    public ReportExportResult ExportHtml(AnalysisReport report, HardwareReport? hardware, IReadOnlyList<SessionRecord>? beforeAfterSessions, string? outputPath = null)
    {
        try
        {
            if (report is null) return Fail("Le rapport d'analyse est introuvable.");
            var source = hardware ?? report.Hardware;
            var html = BuildReportHtml(report, source, beforeAfterSessions);
            return WriteFile(html, outputPath, "rapport");
        }
        catch (Exception ex)
        {
            Log.Error("Reports", "Generation du rapport HTML impossible", ex);
            return Fail("Generation du rapport impossible : " + ex.Message);
        }
    }

    public ReportExportResult ExportSessionHtml(IReadOnlyList<SessionRecord> sessions, string? outputPath = null)
    {
        try
        {
            if (sessions is null || sessions.Count == 0) return Fail("Aucune session a exporter.");
            var html = BuildSessionsHtml(sessions);
            return WriteFile(html, outputPath, "historique");
        }
        catch (Exception ex)
        {
            Log.Error("Reports", "Generation de l'historique HTML impossible", ex);
            return Fail("Generation de l'historique impossible : " + ex.Message);
        }
    }

    private static string BuildReportHtml(AnalysisReport report, HardwareReport? hardware, IReadOnlyList<SessionRecord>? sessions)
    {
        var sb = new StringBuilder();
        AppendHead(sb, "Rapport d'analyse", report.CreatedAt);
        sb.Append("<body><div class=\"wrap\">");

        sb.Append("<header class=\"report\">");
        sb.Append("<p class=\"brand\">GameBoost</p>");
        sb.Append("<h1>Rapport d'analyse de performance</h1>");
        sb.Append("<p class=\"meta\">Généré le ").Append(E(report.CreatedAt.ToString("dddd d MMMM yyyy 'à' HH:mm:ss", French)));
        sb.Append(" · Identifiant ").Append(E(report.Id.ToString("N"))).Append("</p>");
        if (!string.IsNullOrWhiteSpace(report.OverallSummary))
            sb.Append("<div class=\"summary\">").Append(E(report.OverallSummary)).Append("</div>");
        sb.Append("</header>");

        sb.Append("<section><h2>Configuration</h2>");
        sb.Append(BuildConfiguration(hardware));
        sb.Append("</section>");

        sb.Append("<section><h2>Problèmes détectés</h2>");
        sb.Append(BuildChecks(report.Checks));
        sb.Append("</section>");

        sb.Append("<section><h2>Recommandations</h2>");
        sb.Append(BuildRecommendations(report.Checks));
        sb.Append("</section>");

        sb.Append("<section><h2>Optimisations appliquées</h2>");
        sb.Append(BuildOptimizations(sessions));
        sb.Append("</section>");

        sb.Append("<section><h2>Mesures avant / après</h2>");
        sb.Append(BuildBeforeAfter(sessions));
        sb.Append("</section>");

        sb.Append("<section><h2>Statistiques globales de l'analyse</h2>");
        sb.Append(BuildStats(report));
        sb.Append("</section>");

        sb.Append("<footer class=\"report\">");
        sb.Append("<p>Les performances varient selon les jeux et les conditions : GameBoost ne promet aucun gain de FPS particulier.</p>");
        sb.Append("<p>Rapport produit par GameBoost · ").Append(E(DateTime.Now.ToString("dd/MM/yyyy HH:mm", French))).Append("</p>");
        sb.Append("</footer>");

        sb.Append("</div></body></html>");
        return sb.ToString();
    }

    private static string BuildConfiguration(HardwareReport? hardware)
    {
        if (hardware is null)
            return "<p class=\"empty\">Configuration matérielle non disponible pour ce rapport.</p>";

        var sb = new StringBuilder("<div class=\"grid\">");

        sb.Append("<div class=\"card\"><div class=\"label\">Processeur</div>");
        sb.Append(Kv("Modèle", hardware.Cpu.Name));
        sb.Append(Kv("Cœurs / threads", hardware.Cpu.PhysicalCores + " / " + hardware.Cpu.LogicalCores));
        if (hardware.Cpu.BaseClockMHz is double baseClock) sb.Append(Kv("Horloge de base", baseClock.ToString("0.#", French) + " MHz"));
        if (hardware.Cpu.MaxClockMHz is double maxClock) sb.Append(Kv("Horloge maximale", maxClock.ToString("0.#", French) + " MHz"));
        sb.Append(Kv("Architecture", hardware.Cpu.Architecture));
        sb.Append(Kv("Socket", hardware.Cpu.Socket));
        sb.Append("</div>");

        sb.Append("<div class=\"card\"><div class=\"label\">Carte graphique</div>");
        sb.Append(Kv("Modèle", hardware.Gpu.Name));
        sb.Append(Kv("Mémoire dédiée", Bytes(hardware.Gpu.DedicatedVramBytes)));
        sb.Append(Kv("Pilote", hardware.Gpu.DriverVersion));
        sb.Append(Kv("Constructeur", hardware.Gpu.Vendor.ToString()));
        if (hardware.Gpu.AllAdapters.Count > 1) sb.Append(Kv("Adaptateurs détectés", hardware.Gpu.AllAdapters.Count.ToString(French)));
        sb.Append("</div>");

        sb.Append("<div class=\"card\"><div class=\"label\">Mémoire vive</div>");
        sb.Append(Kv("Totale", Bytes(hardware.Ram.TotalBytes)));
        sb.Append(Kv("Disponible", Bytes(hardware.Ram.AvailableBytes)));
        if (hardware.Ram.SpeedMHz > 0) sb.Append(Kv("Fréquence", hardware.Ram.SpeedMHz.ToString("0.#", French) + " MHz"));
        sb.Append(Kv("Format", hardware.Ram.FormFactor));
        if (hardware.Ram.Modules.Count > 0) sb.Append(Kv("Barrettes", hardware.Ram.Modules.Count.ToString(French)));
        sb.Append("</div>");

        sb.Append("<div class=\"card\"><div class=\"label\">Système d'exploitation</div>");
        sb.Append(Kv("Système", hardware.Os.Caption));
        sb.Append(Kv("Version", hardware.Os.Version));
        sb.Append(Kv("Build", hardware.Os.Build));
        sb.Append(Kv("Édition", hardware.Os.Edition));
        sb.Append(Kv("Architecture", hardware.Os.Architecture));
        sb.Append(Kv("DirectX", hardware.DirectXVersion));
        sb.Append(Kv("Mode jeu", hardware.Os.GameModeEnabled ? "Activé" : "Désactivé"));
        sb.Append("</div>");

        sb.Append("<div class=\"card\"><div class=\"label\">Écrans</div>");
        if (hardware.Displays.Count == 0)
        {
            sb.Append("<p class=\"empty\">Aucun écran détecté.</p>");
        }
        else
        {
            foreach (var display in hardware.Displays)
            {
                var name = string.IsNullOrWhiteSpace(display.DeviceName) ? "Écran" : display.DeviceName;
                var resolution = display.Width > 0 && display.Height > 0
                    ? display.Width + " × " + display.Height
                    : "Résolution inconnue";
                var refresh = display.RefreshRate > 0 ? " @ " + display.RefreshRate + " Hz" : string.Empty;
                var suffix = display.Primary ? " (principal)" : string.Empty;
                sb.Append("<div class=\"kv\"><span class=\"k\">").Append(E(name)).Append(suffix)
                  .Append("</span><span class=\"v\">").Append(E(resolution + refresh)).Append("</span></div>");
            }
        }
        sb.Append("</div>");

        sb.Append("<div class=\"card\"><div class=\"label\">Disques</div>");
        if (hardware.Storage.Count == 0)
        {
            sb.Append("<p class=\"empty\">Aucun disque détecté.</p>");
        }
        else
        {
            foreach (var disk in hardware.Storage)
            {
                var details = Bytes(disk.SizeBytes) + " · " + disk.BusType + " · " + MediaLabel(disk.MediaType);
                if (disk.IsSystemDisk) details += " · système";
                if (disk.HealthPercent is int health) details += " · santé " + health + " %";
                sb.Append("<div class=\"kv\"><span class=\"k\">").Append(E(disk.Model))
                  .Append("</span><span class=\"v\">").Append(E(details)).Append("</span></div>");
            }
        }
        sb.Append("</div>");

        sb.Append("</div>");
        return sb.ToString();
    }

    private static string BuildChecks(IReadOnlyList<CheckResult> checks)
    {
        if (checks is null || checks.Count == 0)
            return "<p class=\"empty\">Aucun contrôle n'a été effectué pour ce rapport.</p>";

        var sb = new StringBuilder();
        foreach (var check in checks)
        {
            var level = LevelKey(check.Level);
            sb.Append("<div class=\"check ").Append(level).Append("\">");
            sb.Append("<div class=\"check-head\">");
            sb.Append("<span class=\"dot ").Append(level).Append("\"></span>");
            sb.Append("<h3>").Append(E(string.IsNullOrWhiteSpace(check.Title) ? check.Id : check.Title)).Append("</h3>");
            sb.Append("<span class=\"pill ").Append(level).Append("\">").Append(E(LevelLabel(check.Level))).Append("</span>");
            if (!string.IsNullOrWhiteSpace(check.Category))
                sb.Append("<span class=\"cat\">").Append(E(check.Category)).Append("</span>");
            sb.Append("</div>");

            if (!string.IsNullOrWhiteSpace(check.Explanation))
                sb.Append("<div class=\"field\"><b>Explication :</b> ").Append(E(check.Explanation)).Append("</div>");
            if (!string.IsNullOrWhiteSpace(check.Impact))
                sb.Append("<div class=\"field\"><b>Impact :</b> ").Append(E(check.Impact)).Append("</div>");
            if (!string.IsNullOrWhiteSpace(check.Solution))
                sb.Append("<div class=\"field\"><b>Solution :</b> ").Append(E(check.Solution)).Append("</div>");
            if (!string.IsNullOrWhiteSpace(check.CurrentValue))
                sb.Append("<div class=\"field\"><b>Valeur actuelle :</b> <span class=\"value\">")
                  .Append(E(check.CurrentValue)).Append("</span></div>");
            sb.Append("</div>");
        }
        return sb.ToString();
    }

    private static string BuildRecommendations(IReadOnlyList<CheckResult> checks)
    {
        if (checks is null || checks.Count == 0)
            return "<p class=\"empty\">Aucune recommandation disponible.</p>";

        var items = checks
            .Where(c => c.Level != HealthLevel.Good && !string.IsNullOrWhiteSpace(c.Solution))
            .ToList();
        if (items.Count == 0)
            return "<p class=\"empty\">Aucune recommandation particulière : les contrôles ne signalent rien à corriger.</p>";

        var sb = new StringBuilder("<ul class=\"reco\">");
        foreach (var item in items)
        {
            sb.Append("<li>").Append(E(item.Solution));
            sb.Append("<span class=\"src\">").Append(E(LevelLabel(item.Level)));
            if (!string.IsNullOrWhiteSpace(item.Title)) sb.Append(" · ").Append(E(item.Title));
            sb.Append("</span></li>");
        }
        sb.Append("</ul>");
        return sb.ToString();
    }

    private static string BuildOptimizations(IReadOnlyList<SessionRecord>? sessions)
    {
        var optimizations = new List<string>();
        if (sessions is not null)
        {
            foreach (var session in sessions)
            {
                if (session?.OptimizationsApplied is not List<string> applied) continue;
                foreach (var entry in applied)
                {
                    if (string.IsNullOrWhiteSpace(entry)) continue;
                    var trimmed = entry.Trim();
                    if (!optimizations.Contains(trimmed, StringComparer.OrdinalIgnoreCase))
                        optimizations.Add(trimmed);
                }
            }
        }

        if (optimizations.Count == 0)
            return "<p class=\"empty\">Aucune optimisation appliquée n'est enregistrée pour ce rapport.</p>";

        var sb = new StringBuilder("<ul class=\"plain\">");
        foreach (var optimization in optimizations)
            sb.Append("<li>").Append(E(optimization)).Append("</li>");
        sb.Append("</ul>");
        return sb.ToString();
    }

    private static string BuildBeforeAfter(IReadOnlyList<SessionRecord>? sessions)
    {
        if (sessions is null || sessions.Count < 2)
            return "<p class=\"empty\">Deux sessions sont nécessaires pour afficher une comparaison avant / après.</p>";

        var sessionA = sessions[0];
        var sessionB = sessions[sessions.Count - 1];
        if (sessionA is null || sessionB is null)
            return "<p class=\"empty\">Deux sessions sont nécessaires pour afficher une comparaison avant / après.</p>";

        var rows = new List<(string Label, double? A, double? B, string Suffix)>
        {
            ("FPS moyen", sessionA.AverageFps, sessionB.AverageFps, ""),
            ("1 % low", sessionA.OnePercentLowFps, sessionB.OnePercentLowFps, ""),
            ("Température GPU max", sessionA.MaxGpuTemperatureC, sessionB.MaxGpuTemperatureC, " °C"),
            ("Utilisation GPU moyenne", sessionA.AverageGpuUsagePercent, sessionB.AverageGpuUsagePercent, " %")
        };

        double maxAbs = 0;
        foreach (var row in rows)
        {
            if (row.A is not double a || row.B is not double b) continue;
            var delta = Math.Abs(b - a);
            if (delta > maxAbs) maxAbs = delta;
        }

        var sb = new StringBuilder();
        sb.Append("<table><thead><tr>");
        sb.Append("<th>Mesure</th><th>Session A</th><th>Session B</th><th>Delta (B - A)</th>");
        sb.Append("</tr></thead><tbody>");

        sb.Append("<tr><td colspan=\"4\">")
          .Append("<strong>").Append(E(sessionA.GameName)).Append("</strong> du ")
          .Append(E(sessionA.StartTime.ToString("dd/MM/yyyy HH:mm", French)))
          .Append(" &rarr; <strong>").Append(E(sessionB.GameName)).Append("</strong> du ")
          .Append(E(sessionB.StartTime.ToString("dd/MM/yyyy HH:mm", French)))
          .Append("</td></tr>");

        foreach (var row in rows)
        {
            sb.Append("<tr><td>").Append(E(row.Label)).Append("</td>");
            sb.Append("<td class=\"num\">").Append(Num(row.A, row.Suffix)).Append("</td>");
            sb.Append("<td class=\"num\">").Append(Num(row.B, row.Suffix)).Append("</td>");

            if (row.A is not double a || row.B is not double b)
            {
                sb.Append("<td class=\"num\"><span class=\"delta flat\">—</span></td></tr>");
                continue;
            }

            var delta = Math.Round(b - a, 2);
            var css = delta > 0 ? "pos" : delta < 0 ? "neg" : "flat";
            var width = maxAbs > 0 ? Math.Max(4.0, Math.Abs(delta) / maxAbs * 100.0) : 4.0;
            sb.Append("<td class=\"num\"><span class=\"delta ").Append(css).Append("\">")
              .Append(FormatDelta(delta, row.Suffix)).Append("</span>");
            sb.Append("<div class=\"bar\"><span class=\"").Append(css == "flat" ? "pos" : css)
              .Append("\" style=\"width:").Append(width.ToString("0", French)).Append("%\"></span></div></td></tr>");
        }

        sb.Append("</tbody></table>");
        sb.Append("<p class=\"meta\">Deltas calculés session B - session A : positif en vert, négatif en rouge. ");
        sb.Append("Les valeurs absentes sont affichées comme non mesurées.</p>");
        return sb.ToString();
    }

    private static string BuildStats(AnalysisReport report)
    {
        var good = report.GoodCount;
        var warning = report.WarningCount;
        var critical = report.CriticalCount;
        var total = report.Checks?.Count ?? 0;

        var sb = new StringBuilder("<div class=\"stats\">");
        sb.Append("<div class=\"stat good\"><span class=\"big\">").Append(good.ToString(French))
          .Append("</span><span class=\"cap\">Conformes</span></div>");
        sb.Append("<div class=\"stat warning\"><span class=\"big\">").Append(warning.ToString(French))
          .Append("</span><span class=\"cap\">Avertissements</span></div>");
        sb.Append("<div class=\"stat critical\"><span class=\"big\">").Append(critical.ToString(French))
          .Append("</span><span class=\"cap\">Critiques</span></div>");
        sb.Append("<div class=\"stat\"><span class=\"big\">").Append(total.ToString(French))
          .Append("</span><span class=\"cap\">Contrôles effectués</span></div>");
        sb.Append("</div>");
        return sb.ToString();
    }

    private static string BuildSessionsHtml(IReadOnlyList<SessionRecord> sessions)
    {
        var sb = new StringBuilder();
        AppendHead(sb, "Historique des sessions", DateTime.Now);
        sb.Append("<body><div class=\"wrap\">");

        sb.Append("<header class=\"report\">");
        sb.Append("<p class=\"brand\">GameBoost</p>");
        sb.Append("<h1>Historique des sessions de jeu</h1>");
        sb.Append("<p class=\"meta\">Généré le ").Append(E(DateTime.Now.ToString("dddd d MMMM yyyy 'à' HH:mm:ss", French)));
        sb.Append(" · ").Append(sessions.Count.ToString(French)).Append(" session(s)</p>");
        sb.Append("</header>");

        sb.Append("<section><h2>Sessions enregistrées</h2>");
        sb.Append("<table><thead><tr><th>Date</th><th>Jeu</th><th>Durée</th><th>FPS moyen</th><th>1 % low</th>");
        sb.Append("<th>FPS min</th><th>FPS max</th><th>GPU</th><th>GPU max</th><th>CPU</th><th>RAM max</th><th>Profil</th></tr></thead><tbody>");

        foreach (var session in sessions)
        {
            sb.Append("<tr><td>").Append(E(session.StartTime.ToString("dd/MM/yyyy HH:mm", French))).Append("</td>");
            sb.Append("<td>").Append(E(string.IsNullOrWhiteSpace(session.GameName) ? "Session" : session.GameName)).Append("</td>");
            sb.Append("<td class=\"num\">").Append(Duration(session.StartTime, session.EndTime)).Append("</td>");
            sb.Append("<td class=\"num\">").Append(Num(session.AverageFps)).Append("</td>");
            sb.Append("<td class=\"num\">").Append(Num(session.OnePercentLowFps)).Append("</td>");
            sb.Append("<td class=\"num\">").Append(Num(session.MinimumFps)).Append("</td>");
            sb.Append("<td class=\"num\">").Append(Num(session.MaximumFps)).Append("</td>");
            sb.Append("<td class=\"num\">").Append(Num(session.AverageGpuUsagePercent, " %")).Append("</td>");
            sb.Append("<td class=\"num\">").Append(Num(session.MaxGpuTemperatureC, "0.0", " °C")).Append("</td>");
            sb.Append("<td class=\"num\">").Append(Num(session.AverageCpuUsagePercent, " %")).Append("</td>");
            sb.Append("<td class=\"num\">").Append(Bytes(session.PeakRamUsedBytes)).Append("</td>");
            sb.Append("<td>").Append(E(session.ProfileUsed)).Append("</td></tr>");
        }

        sb.Append("</tbody></table></section>");

        var detailed = sessions.Where(s =>
            !string.IsNullOrWhiteSpace(s.Notes) ||
            (s.OptimizationsApplied is List<string> applied && applied.Count > 0)).ToList();

        if (detailed.Count > 0)
        {
            sb.Append("<section><h2>Notes et optimisations</h2>");
            foreach (var session in detailed)
            {
                sb.Append("<div class=\"card\" style=\"margin-bottom:12px\">");
                sb.Append("<div class=\"label\">").Append(E(session.GameName)).Append(" · ")
                  .Append(E(session.StartTime.ToString("dd/MM/yyyy HH:mm", French))).Append("</div>");
                if (!string.IsNullOrWhiteSpace(session.Notes))
                    sb.Append("<div class=\"field\"><b>Notes :</b> ").Append(E(session.Notes)).Append("</div>");
                if (session.OptimizationsApplied is List<string> applied && applied.Count > 0)
                {
                    sb.Append("<div class=\"field\"><b>Optimisations :</b><ul class=\"plain\">");
                    foreach (var entry in applied) sb.Append("<li>").Append(E(entry)).Append("</li>");
                    sb.Append("</ul></div>");
                }
                sb.Append("</div>");
            }
            sb.Append("</section>");
        }

        sb.Append("<footer class=\"report\">");
        sb.Append("<p>Les performances varient selon les jeux et les conditions : GameBoost ne promet aucun gain de FPS particulier.</p>");
        sb.Append("</footer>");

        sb.Append("</div></body></html>");
        return sb.ToString();
    }

    private static void AppendHead(StringBuilder sb, string title, DateTime date)
    {
        sb.Append("<!DOCTYPE html><html lang=\"fr\"><head><meta charset=\"utf-8\">");
        sb.Append("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">");
        sb.Append("<title>GameBoost - ").Append(E(title)).Append(" ")
          .Append(E(date.ToString("dd-MM-yyyy HH:mm", French))).Append("</title>");
        sb.Append("<style>").Append(Styles).Append("</style></head>");
    }

    private ReportExportResult WriteFile(string html, string? outputPath, string prefix)
    {
        try
        {
            var path = ResolvePath(outputPath, prefix);
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
            File.WriteAllText(path, html, new UTF8Encoding(false));
            Log.Info("Reports", "Fichier HTML exporte : " + path);
            return new ReportExportResult { Success = true, FilePath = path, Error = string.Empty };
        }
        catch (Exception ex)
        {
            Log.Error("Reports", "Ecriture du fichier HTML impossible", ex);
            return Fail("Ecriture du fichier impossible : " + ex.Message);
        }
    }

    private string ResolvePath(string? outputPath, string prefix)
    {
        var fileName = prefix + "-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".html";
        if (string.IsNullOrWhiteSpace(outputPath))
            return Path.Combine(GetExportDirectory(), fileName);

        var candidate = outputPath.Trim();
        var isDirectory = Directory.Exists(candidate) ||
                          candidate.EndsWith(Path.DirectorySeparatorChar) ||
                          candidate.EndsWith(Path.AltDirectorySeparatorChar);

        if (isDirectory)
        {
            Directory.CreateDirectory(candidate);
            return Path.Combine(candidate, fileName);
        }

        if (string.IsNullOrWhiteSpace(Path.GetExtension(candidate))) candidate += ".html";
        return candidate;
    }

    private static ReportExportResult Fail(string message)
    {
        Log.Warn("Reports", message);
        return new ReportExportResult { Success = false, FilePath = string.Empty, Error = message };
    }

    private static string Kv(string key, string? value)
    {
        var text = string.IsNullOrWhiteSpace(value) ? "Non disponible" : value;
        return "<div class=\"kv\"><span class=\"k\">" + E(key) + "</span><span class=\"v\">" + E(text) + "</span></div>";
    }

    private static string Num(double? value, string suffix = "") => Num(value, "0.##", suffix);

    private static string Num(double? value, string format, string suffix)
    {
        if (value is not double v || double.IsNaN(v) || double.IsInfinity(v)) return "—";
        return v.ToString(format, French) + suffix;
    }

    private static string FormatDelta(double value, string suffix)
    {
        var sign = value > 0 ? "+" : string.Empty;
        var body = suffix.Contains("°C") || suffix.Contains("%")
            ? value.ToString("0.0", French)
            : value.ToString("0.##", French);
        return sign + body + suffix;
    }

    private static string Bytes(long bytes)
    {
        if (bytes <= 0) return "—";
        string[] units = { "o", "Ko", "Mo", "Go", "To" };
        double value = bytes;
        var index = 0;
        while (value >= 1024 && index < units.Length - 1)
        {
            value /= 1024;
            index++;
        }
        return value.ToString(index == 0 ? "0" : "0.0", French) + " " + units[index];
    }

    private static string Duration(DateTime start, DateTime end)
    {
        var span = end - start;
        if (span <= TimeSpan.Zero) return "—";
        if (span.TotalHours >= 1) return (int)span.TotalHours + " h " + span.Minutes + " min";
        if (span.TotalMinutes >= 1) return span.Minutes + " min " + span.Seconds + " s";
        return span.Seconds + " s";
    }

    private static string MediaLabel(DiskMediaType mediaType)
    {
        return mediaType switch
        {
            DiskMediaType.Ssd => "SSD",
            DiskMediaType.Hdd => "HDD",
            _ => "Type inconnu"
        };
    }

    private static string LevelKey(HealthLevel level)
    {
        return level switch
        {
            HealthLevel.Good => "good",
            HealthLevel.Warning => "warning",
            HealthLevel.Critical => "critical",
            _ => "unknown"
        };
    }

    private static string LevelLabel(HealthLevel level)
    {
        return level switch
        {
            HealthLevel.Good => "Conforme",
            HealthLevel.Warning => "Attention",
            HealthLevel.Critical => "Critique",
            _ => "Non évalué"
        };
    }

    private static string E(string? value) => WebUtility.HtmlEncode(value ?? string.Empty);
}
