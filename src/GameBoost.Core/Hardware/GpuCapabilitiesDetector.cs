using System.Text.RegularExpressions;
using GameBoost.Core.Models;

namespace GameBoost.Core.Hardware;

public static class GpuCapabilitiesDetector
{
    private static readonly Regex NvidiaRtx = new(@"RTX\s*(2|3|4|5)\d{3}", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex NvidiaGtx = new(@"GTX\s*(\d{3,4})", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex AmdRx = new(@"RX\s*(5|6|7|8|9)\d{3}", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static GpuCapabilities Detect(string gpuName, GpuVendor vendor)
    {
        var capabilities = new GpuCapabilities();
        var name = (gpuName ?? string.Empty).Trim();
        var effectiveVendor = vendor is GpuVendor.Unknown or GpuVendor.Other ? VendorFromName(name) : vendor;
        var notes = new List<string>();
        switch (effectiveVendor)
        {
            case GpuVendor.Nvidia:
                DetectNvidia(name, capabilities, notes);
                break;
            case GpuVendor.Amd:
                DetectAmd(name, capabilities, notes);
                break;
            case GpuVendor.Intel:
                DetectIntel(name, capabilities, notes);
                break;
            default:
                notes.Add("Détection basée sur le modèle du GPU : modèle \"" + DisplayName(name) + "\" non reconnu, aucune technologie affirmée.");
                break;
        }
        capabilities.Notes = string.Join(" ", notes);
        return capabilities;
    }

    private static void DetectNvidia(string name, GpuCapabilities capabilities, List<string> notes)
    {
        var rtx = NvidiaRtx.Match(name);
        if (rtx.Success)
        {
            capabilities.Dlss = true;
            capabilities.RayTracing = true;
            capabilities.Reflex = true;
            capabilities.Fsr = true;
            capabilities.Xess = true;
            var generation = rtx.Groups[1].Value;
            capabilities.FrameGeneration = generation is "4" or "5";
            var frame = capabilities.FrameGeneration
                ? ", génération de frames disponible selon le jeu"
                : ", pas de génération de frames sur cette génération";
            notes.Add("Détection basée sur le modèle du GPU : " + DisplayName(name) + " identifié en " +
                      Model(rtx.Value) + " → DLSS, Ray Tracing, Reflex, FSR et XeSS pris en charge" + frame + ".");
            return;
        }

        var gtx = NvidiaGtx.Match(name);
        if (gtx.Success)
        {
            capabilities.Reflex = true;
            var model = gtx.Groups[1].Value;
            var modern = model.StartsWith("16", StringComparison.Ordinal) || model.StartsWith("20", StringComparison.Ordinal);
            if (modern) capabilities.Xess = true;
            notes.Add("Détection basée sur le modèle du GPU : " + DisplayName(name) + " → Reflex disponible" +
                      (modern ? ", XeSS pris en charge (GTX 16 ou supérieur)" : string.Empty) +
                      ", pas de DLSS ni de Ray Tracing matériel sur la gamme GTX.");
            return;
        }

        notes.Add("Détection basée sur le modèle du GPU : " + DisplayName(name) +
                  " appartient à la gamme NVIDIA mais le modèle précis n'a pas été identifié, aucune technologie affirmée.");
    }

    private static void DetectAmd(string name, GpuCapabilities capabilities, List<string> notes)
    {
        capabilities.Fsr = true;
        var rx = AmdRx.Match(name);
        if (!rx.Success)
        {
            notes.Add("Détection basée sur le modèle du GPU : " + DisplayName(name) +
                      " → FSR 1 disponible (ouvert à tous les GPU AMD), pas de Ray Tracing matériel identifié sur cette gamme.");
            return;
        }

        capabilities.RayTracing = true;
        var generation = rx.Groups[1].Value;
        if (generation is "7" or "8" or "9")
        {
            capabilities.FrameGeneration = true;
            notes.Add("Détection basée sur le modèle du GPU : " + DisplayName(name) + " identifié en " +
                      Model(rx.Value) + " → Ray Tracing, FSR et FSR Frame Generation selon le jeu pris en charge.");
            return;
        }
        notes.Add("Détection basée sur le modèle du GPU : " + DisplayName(name) + " identifié en " +
                  Model(rx.Value) + " → Ray Tracing et FSR pris en charge, pas de FSR Frame Generation sur cette génération.");
    }

    private static void DetectIntel(string name, GpuCapabilities capabilities, List<string> notes)
    {
        if (!name.Contains("arc", StringComparison.OrdinalIgnoreCase))
        {
            notes.Add("Détection basée sur le modèle du GPU : " + DisplayName(name) +
                      " n'est pas une série Arc, aucune technologie affirmée.");
            return;
        }

        capabilities.RayTracing = true;
        capabilities.Fsr = true;
        capabilities.Xess = true;
        notes.Add("Détection basée sur le modèle du GPU : " + DisplayName(name) + " identifié en Arc → Ray Tracing, XeSS et FSR pris en charge.");
    }

    private static GpuVendor VendorFromName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return GpuVendor.Unknown;
        if (name.Contains("nvidia", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("geforce", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("rtx", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("gtx", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("quadro", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("tesla", StringComparison.OrdinalIgnoreCase))
            return GpuVendor.Nvidia;
        if (name.Contains("radeon", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("amd", StringComparison.OrdinalIgnoreCase))
            return GpuVendor.Amd;
        if (name.Contains("intel", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("arc", StringComparison.OrdinalIgnoreCase))
            return GpuVendor.Intel;
        return GpuVendor.Other;
    }

    private static string DisplayName(string name) =>
        string.IsNullOrWhiteSpace(name) ? "nom du GPU inconnu" : Model(name);

    private static string Model(string value) => Regex.Replace(value, @"\s+", " ").Trim();
}
