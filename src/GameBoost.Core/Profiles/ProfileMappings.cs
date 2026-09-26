using System.Globalization;
using System.Text;
using GameBoost.Core.Models;

namespace GameBoost.Core.Profiles;

internal enum QualityLevel
{
    Faible,
    Moyen,
    Eleve,
    Ultra
}

internal sealed class DesiredValue
{
    public string Canonical { get; set; } = string.Empty;
    public int? IntValue { get; set; }
    public bool? BoolValue { get; set; }
    public QualityLevel? Level { get; set; }
    public string Display { get; set; } = string.Empty;
}

internal static class ProfileMappings
{
    public const string KFullscreen = "fullscreen";
    public const string KResWidth = "resolution_width";
    public const string KResHeight = "resolution_height";
    public const string KFps = "fps_limit";
    public const string KVsync = "vsync";
    public const string KTexture = "texture_quality";
    public const string KShadow = "shadow_quality";
    public const string KLighting = "lighting_quality";
    public const string KViewDistance = "view_distance";
    public const string KEffects = "effects_quality";
    public const string KFoliage = "foliage_quality";
    public const string KAntiAliasing = "anti_aliasing_quality";
    public const string KRayTracing = "ray_tracing";
    public const string KDlss = "dlss";
    public const string KFsr = "fsr";
    public const string KXess = "xess";

    public static readonly string[] IntKeys = { KResWidth, KResHeight, KFps };
    public static readonly string[] BoolKeys = { KVsync, KFullscreen, KRayTracing, KDlss, KFsr, KXess };
    public static readonly string[] QualityKeys = { KTexture, KShadow, KLighting, KViewDistance, KEffects, KFoliage, KAntiAliasing };

    public static readonly string[] UnrealFilePatterns =
    {
        "Saved/Config/WindowsNoEditor/GameUserSettings.ini",
        "Saved/Config/Windows/GameUserSettings.ini",
        "Saved/Config/LinuxServer/GameUserSettings.ini"
    };

    public static readonly string[] UnityValueNames =
    {
        "Screenmanager Resolution Width_px",
        "Screenmanager Resolution Height_px",
        "Is Fullscreen mode"
    };

    public static readonly Dictionary<string, string> Aliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["VSync"] = KVsync,
        ["VerticalSync"] = KVsync,
        ["bVSyncEnabled"] = KVsync,
        ["vSyncCount"] = KVsync,
        ["Fullscreen"] = KFullscreen,
        ["FullscreenMode"] = KFullscreen,
        ["Is Fullscreen mode"] = KFullscreen,
        ["ResolutionWidth"] = KResWidth,
        ["Width"] = KResWidth,
        ["ResX"] = KResWidth,
        ["ResolutionSizeX"] = KResWidth,
        ["Screenmanager Resolution Width_px"] = KResWidth,
        ["ResolutionHeight"] = KResHeight,
        ["Height"] = KResHeight,
        ["ResY"] = KResHeight,
        ["ResolutionSizeY"] = KResHeight,
        ["Screenmanager Resolution Height_px"] = KResHeight,
        ["FrameLimit"] = KFps,
        ["FPSLimit"] = KFps,
        ["MaxFPS"] = KFps,
        ["FrameRateLimit"] = KFps,
        ["TextureQuality"] = KTexture,
        ["TexturesQuality"] = KTexture,
        ["sg.TextureQuality"] = KTexture,
        ["ShadowQuality"] = KShadow,
        ["Shadows"] = KShadow,
        ["sg.ShadowQuality"] = KShadow,
        ["LightingQuality"] = KLighting,
        ["Lighting"] = KLighting,
        ["PostProcessQuality"] = KLighting,
        ["sg.PostProcessQuality"] = KLighting,
        ["sg.LightingQuality"] = KLighting,
        ["ViewDistanceQuality"] = KViewDistance,
        ["sg.ViewDistanceQuality"] = KViewDistance,
        ["EffectsQuality"] = KEffects,
        ["sg.EffectsQuality"] = KEffects,
        ["FoliageQuality"] = KFoliage,
        ["sg.FoliageQuality"] = KFoliage,
        ["AntiAliasingQuality"] = KAntiAliasing,
        ["sg.AntiAliasingQuality"] = KAntiAliasing,
        ["AntiAliasing"] = KAntiAliasing,
        ["RayTracing"] = KRayTracing,
        ["RTX"] = KRayTracing,
        ["DLSS"] = KDlss,
        ["FSR"] = KFsr,
        ["XeSS"] = KXess
    };

    public static readonly Dictionary<string, string> Labels = new(StringComparer.Ordinal)
    {
        [KFullscreen] = "Mode plein écran",
        [KResWidth] = "Résolution - largeur",
        [KResHeight] = "Résolution - hauteur",
        [KFps] = "Limite d'images par seconde",
        [KVsync] = "Synchronisation verticale (VSync)",
        [KTexture] = "Qualité des textures",
        [KShadow] = "Qualité des ombres",
        [KLighting] = "Qualité de l'éclairage",
        [KViewDistance] = "Distance d'affichage",
        [KEffects] = "Qualité des effets",
        [KFoliage] = "Qualité de la végétation",
        [KAntiAliasing] = "Anti-crénelage (AA)",
        [KRayTracing] = "Ray tracing",
        [KDlss] = "DLSS",
        [KFsr] = "FSR",
        [KXess] = "XeSS"
    };

    private static readonly (string Positive, string Negative)[] BoolPairs =
    {
        ("true", "false"),
        ("yes", "no"),
        ("on", "off"),
        ("enabled", "disabled"),
        ("enable", "disable"),
        ("t", "f"),
        ("y", "n"),
        ("1", "0")
    };

    private static readonly HashSet<string> FaibleTokens = new(StringComparer.Ordinal) { "faible", "low", "basse", "basse qualite", "minimal", "min" };
    private static readonly HashSet<string> MoyenTokens = new(StringComparer.Ordinal) { "moyen", "moyenne", "medium", "med", "normal", "average", "mid" };
    private static readonly HashSet<string> EleveTokens = new(StringComparer.Ordinal) { "eleve", "elevee", "high", "haute", "good", "very high", "veryhigh" };
    private static readonly HashSet<string> UltraTokens = new(StringComparer.Ordinal) { "ultra", "epic", "cinematic", "max", "maximum", "extreme" };

    public static string Label(string canonical)
    {
        return Labels.TryGetValue(canonical, out var label) ? label : canonical;
    }

    public static Dictionary<string, DesiredValue> BuildDesired(ProfileSettings? settings)
    {
        var map = new Dictionary<string, DesiredValue>(StringComparer.OrdinalIgnoreCase);
        if (settings is null) return map;

        if (settings.ResolutionWidth is int width && width > 0)
            map[KResWidth] = new DesiredValue { Canonical = KResWidth, IntValue = width, Display = width.ToString(CultureInfo.InvariantCulture) };
        if (settings.ResolutionHeight is int height && height > 0)
            map[KResHeight] = new DesiredValue { Canonical = KResHeight, IntValue = height, Display = height.ToString(CultureInfo.InvariantCulture) };
        if (settings.FpsLimit is int fps && fps > 0)
            map[KFps] = new DesiredValue { Canonical = KFps, IntValue = fps, Display = fps.ToString(CultureInfo.InvariantCulture) };
        if (settings.VSync is bool vsync)
            map[KVsync] = new DesiredValue { Canonical = KVsync, BoolValue = vsync, Display = vsync ? "activé" : "désactivé" };
        if (settings.Fullscreen is bool fullscreen)
            map[KFullscreen] = new DesiredValue { Canonical = KFullscreen, BoolValue = fullscreen, Display = fullscreen ? "activé" : "désactivé" };
        if (settings.RayTracing is bool rayTracing)
            map[KRayTracing] = new DesiredValue { Canonical = KRayTracing, BoolValue = rayTracing, Display = rayTracing ? "activé" : "désactivé" };
        if (settings.Dlss is bool dlss)
            map[KDlss] = new DesiredValue { Canonical = KDlss, BoolValue = dlss, Display = dlss ? "activé" : "désactivé" };
        if (settings.Fsr is bool fsr)
            map[KFsr] = new DesiredValue { Canonical = KFsr, BoolValue = fsr, Display = fsr ? "activé" : "désactivé" };
        if (settings.Xess is bool xess)
            map[KXess] = new DesiredValue { Canonical = KXess, BoolValue = xess, Display = xess ? "activé" : "désactivé" };

        AddQuality(map, KTexture, settings.TextureQuality);
        AddQuality(map, KShadow, settings.ShadowQuality);
        AddQuality(map, KLighting, settings.LightingQuality);
        return map;
    }

    private static void AddQuality(Dictionary<string, DesiredValue> map, string canonical, string? value)
    {
        var level = ParseLevel(value);
        if (level is null) return;
        map[canonical] = new DesiredValue { Canonical = canonical, Level = level, Display = DisplayFor(level.Value) };
    }

    public static QualityLevel? ParseLevel(string? value)
    {
        var normalized = Normalize(value ?? string.Empty);
        if (normalized.Length == 0) return null;
        if (normalized is "non defini" or "nondefini" or "indetermine" or "default" or "par defaut") return null;
        if (FaibleTokens.Contains(normalized)) return QualityLevel.Faible;
        if (MoyenTokens.Contains(normalized)) return QualityLevel.Moyen;
        if (EleveTokens.Contains(normalized)) return QualityLevel.Eleve;
        if (UltraTokens.Contains(normalized)) return QualityLevel.Ultra;
        return null;
    }

    public static bool TryFormatValue(string canonical, string actualKey, DesiredValue desired, string existingRaw, out string newValue)
    {
        var existing = (existingRaw ?? string.Empty).Trim();
        newValue = existing;
        if (Array.IndexOf(IntKeys, canonical) >= 0) return TryFormatInt(desired, existing, out newValue);
        if (Array.IndexOf(BoolKeys, canonical) >= 0) return TryFormatBool(canonical, actualKey, desired, existing, out newValue);
        if (Array.IndexOf(QualityKeys, canonical) >= 0) return TryFormatQuality(desired, existing, out newValue);
        return false;
    }

    private static bool TryFormatInt(DesiredValue desired, string existing, out string newValue)
    {
        newValue = existing;
        if (desired.IntValue is not int target) return false;
        if (int.TryParse(existing, NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
        {
            newValue = target.ToString(CultureInfo.InvariantCulture);
            return true;
        }
        if (TryParseDecimalLike(existing, out var decimals, out var separator))
        {
            var formatted = target.ToString("F" + decimals, CultureInfo.InvariantCulture);
            if (separator == ',') formatted = formatted.Replace('.', ',');
            newValue = formatted;
            return true;
        }
        return false;
    }

    private static bool TryParseDecimalLike(string value, out int decimals, out char separator)
    {
        decimals = 0;
        separator = '.';
        var trimmed = value.Trim();
        if (trimmed.Length == 0) return false;
        var index = trimmed.IndexOfAny(new[] { '.', ',' });
        if (index < 0) return false;
        if (trimmed.IndexOfAny(new[] { '.', ',' }, index + 1) >= 0) return false;
        var fraction = trimmed[(index + 1)..];
        var head = trimmed[..index];
        if (fraction.Length == 0 || head.Length == 0) return false;
        foreach (var c in fraction)
            if (!char.IsDigit(c)) return false;
        for (var i = 0; i < head.Length; i++)
        {
            var c = head[i];
            if (!char.IsDigit(c) && !(i == 0 && (c == '-' || c == '+'))) return false;
        }
        decimals = fraction.Length;
        separator = trimmed[index];
        return true;
    }

    private static bool TryFormatBool(string canonical, string actualKey, DesiredValue desired, string existing, out string newValue)
    {
        newValue = existing;
        if (desired.BoolValue is not bool target) return false;

        if (string.Equals(canonical, KFullscreen, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(actualKey, "FullscreenMode", StringComparison.OrdinalIgnoreCase))
        {
            if (int.TryParse(existing, NumberStyles.Integer, CultureInfo.InvariantCulture, out var mode) && mode >= 0 && mode <= 2)
            {
                newValue = target ? "0" : "1";
                return true;
            }
        }

        if (string.Equals(canonical, KFullscreen, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(actualKey, "Is Fullscreen mode", StringComparison.OrdinalIgnoreCase))
        {
            if (int.TryParse(existing, NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
            {
                newValue = target ? "1" : "0";
                return true;
            }
        }

        if (TryBoolPair(existing, out var positive, out var negative))
        {
            newValue = ApplyCaseStyle(target ? positive : negative, existing);
            return true;
        }
        return false;
    }

    private static bool TryBoolPair(string existing, out string positive, out string negative)
    {
        positive = string.Empty;
        negative = string.Empty;
        var trimmed = existing.Trim();
        if (trimmed.Length == 0) return false;
        foreach (var pair in BoolPairs)
        {
            if (string.Equals(trimmed, pair.Positive, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(trimmed, pair.Negative, StringComparison.OrdinalIgnoreCase))
            {
                positive = pair.Positive;
                negative = pair.Negative;
                return true;
            }
        }
        return false;
    }

    private static string ApplyCaseStyle(string token, string model)
    {
        if (token.Length == 0) return token;
        var reference = model.Trim();
        if (reference.Length <= 1) return token;
        if (reference == reference.ToUpperInvariant()) return token.ToUpperInvariant();
        if (reference == reference.ToLowerInvariant()) return token.ToLowerInvariant();
        if (char.IsUpper(reference[0]) && reference[1..] == reference[1..].ToLowerInvariant())
            return char.ToUpperInvariant(token[0]) + token[1..].ToLowerInvariant();
        return token;
    }

    private static bool TryFormatQuality(DesiredValue desired, string existing, out string newValue)
    {
        newValue = existing;
        if (desired.Level is not QualityLevel level) return false;
        var trimmed = existing.Trim();
        if (trimmed.Length == 0) return false;

        if (int.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out var intQuality))
        {
            var current = LevelFromInt(intQuality);
            newValue = current == level ? trimmed : IntForLevel(level).ToString(CultureInfo.InvariantCulture);
            return true;
        }

        if (double.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out var doubleQuality) &&
            doubleQuality is >= 0 and <= 4 && Math.Abs(doubleQuality - Math.Round(doubleQuality)) < 0.0001)
        {
            var current = LevelFromInt((int)Math.Round(doubleQuality));
            newValue = current == level ? trimmed : IntForLevel(level).ToString(CultureInfo.InvariantCulture);
            return true;
        }

        var currentLevel = LevelFromText(trimmed);
        if (currentLevel is null) return false;
        newValue = currentLevel == level ? trimmed : TokenForLevel(level, trimmed);
        return true;
    }

    private static QualityLevel? LevelFromInt(int value)
    {
        return value switch
        {
            0 => QualityLevel.Faible,
            1 => QualityLevel.Moyen,
            2 => QualityLevel.Eleve,
            3 => QualityLevel.Ultra,
            4 => QualityLevel.Ultra,
            _ => null
        };
    }

    private static int IntForLevel(QualityLevel level)
    {
        return level switch
        {
            QualityLevel.Faible => 0,
            QualityLevel.Moyen => 1,
            QualityLevel.Eleve => 2,
            _ => 3
        };
    }

    private static QualityLevel? LevelFromText(string value)
    {
        var normalized = Normalize(value);
        if (normalized.Length == 0) return null;
        if (FaibleTokens.Contains(normalized)) return QualityLevel.Faible;
        if (MoyenTokens.Contains(normalized)) return QualityLevel.Moyen;
        if (EleveTokens.Contains(normalized)) return QualityLevel.Eleve;
        if (UltraTokens.Contains(normalized)) return QualityLevel.Ultra;
        return null;
    }

    private static string TokenForLevel(QualityLevel level, string model)
    {
        var token = level switch
        {
            QualityLevel.Faible => "Low",
            QualityLevel.Moyen => "Medium",
            QualityLevel.Eleve => "High",
            _ => "Ultra"
        };
        return ApplyCaseStyle(token, model);
    }

    private static string DisplayFor(QualityLevel level)
    {
        return level switch
        {
            QualityLevel.Faible => "Faible",
            QualityLevel.Moyen => "Moyen",
            QualityLevel.Eleve => "Élevé",
            _ => "Ultra"
        };
    }

    public static string Normalize(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        var formD = value.Trim().Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(formD.Length);
        foreach (var c in formD)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark) continue;
            builder.Append(char.ToLowerInvariant(c));
        }
        var result = builder.ToString().Replace('\u00A0', ' ');
        while (result.Contains("  ", StringComparison.Ordinal)) result = result.Replace("  ", " ");
        return result.Trim();
    }
}
