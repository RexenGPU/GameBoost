using System.Collections;
using System.ComponentModel;
using System.Globalization;
using System.Reflection;
using System.Resources;
using GameBoost.Core.Logging;

namespace GameBoost.Core.Localization;

public sealed class LocManager : INotifyPropertyChanged
{
    public static LocManager Instance { get; } = new();

    public string this[string key] => Loc.T(key);

    public event PropertyChangedEventHandler? PropertyChanged;

    internal void RaiseChanged() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
}

public static class Loc
{
    public sealed record LanguageInfo(string Code, string NativeName);

    public static readonly LanguageInfo[] Languages =
    {
        new("auto", "Automatic"),
        new("en", "English"),
        new("fr", "Français"),
        new("de", "Deutsch"),
        new("es", "Español")
    };

    private static readonly string[] KnownCultures = { "en", "fr", "de", "es", "it", "pt-BR", "nl", "pl", "ru", "tr", "ja", "zh-Hans" };

    private static readonly object Sync = new();
    private static readonly List<Assembly> Assemblies = new();
    private static readonly Dictionary<string, Dictionary<string, string>> ByCulture = new(StringComparer.OrdinalIgnoreCase);
    private static Dictionary<string, string>? _table;
    private static CultureInfo _culture = CultureInfo.InvariantCulture;
    private static readonly CultureInfo OriginalUiCulture = CultureInfo.CurrentUICulture;
    private static bool _scanned;

    public static string CultureCode { get; private set; } = "auto";

    public static void RegisterAssembly(Assembly assembly)
    {
        lock (Sync)
        {
            Assemblies.Add(assembly);
            _table = null;
        }
    }

    public static void SetCulture(string code)
    {
        lock (Sync)
        {
            CultureCode = string.IsNullOrWhiteSpace(code) ? "auto" : code;
            if (CultureCode == "auto")
            {
                _culture = OriginalUiCulture;
            }
            else
            {
                try { _culture = CultureInfo.GetCultureInfo(CultureCode); }
                catch { _culture = OriginalUiCulture; }
            }
            try { Thread.CurrentThread.CurrentUICulture = _culture; }
            catch { }
            _table = null;
        }
        LocManager.Instance.RaiseChanged();
    }

    public static string T(string key)
    {
        lock (Sync)
        {
            if (!_scanned) Scan();
            _table ??= BuildTable();
            return _table.TryGetValue(key, out var value) ? value : key;
        }
    }

    public static string T(string key, params object[] args) => string.Format(T(key), args);

    private static void Scan()
    {
        _scanned = true;
        ByCulture["neutral"] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var culture in KnownCultures)
            ByCulture[culture] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var asm in Assemblies)
        {
            LoadResources(asm, string.Empty);
            var dir = Path.GetDirectoryName(asm.Location);
            if (string.IsNullOrEmpty(dir)) continue;
            var assemblyName = asm.GetName().Name;
            if (string.IsNullOrEmpty(assemblyName)) continue;
            foreach (var culture in KnownCultures)
            {
                var satellite = Path.Combine(dir, culture, assemblyName + ".resources.dll");
                if (File.Exists(satellite))
                {
                    try
                    {
                        var before = ByCulture.TryGetValue(culture, out var probe) ? probe.Count : 0;
                        LoadResources(Assembly.LoadFrom(satellite), culture);
                        var after = ByCulture.TryGetValue(culture, out var probe2) ? probe2.Count : 0;
                        Log.Info("Loc", "Satellite " + culture + " charge : " + (after - before) + " cles (" + satellite + ")");
                    }
                    catch (Exception ex) { Log.Error("Loc", "Satellite " + culture + " illisible", ex); }
                }
            }
            foreach (var culture in KnownCultures)
            {
                if (ByCulture.TryGetValue(culture, out var counted) && counted.Count > 0)
                    Log.Info("Loc", "Culture " + culture + " : " + counted.Count + " cles chargees");
            }
        }
    }

    private static void LoadResources(Assembly asm, string forcedCulture)
    {
        string[] names;
        try { names = asm.GetManifestResourceNames(); }
        catch { return; }

        foreach (var name in names)
        {
            if (!name.EndsWith(".resources", StringComparison.OrdinalIgnoreCase)) continue;
            if (name.EndsWith(".g.resources", StringComparison.OrdinalIgnoreCase)) continue;
            if (!name.Contains(".Resources.", StringComparison.Ordinal)) continue;

            var culture = forcedCulture;
            if (string.IsNullOrEmpty(culture))
            {
                var baseName = name[..^".resources".Length];
                var segments = baseName.Split('.');
                var last = segments[^1];
                culture = IsCultureName(last) ? last : "neutral";
            }

            var target = ByCulture.TryGetValue(culture, out var dict)
                ? dict
                : ByCulture[culture] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            try
            {
                using var stream = asm.GetManifestResourceStream(name);
                if (stream is null) continue;
                using var reader = new ResourceReader(stream);
                foreach (DictionaryEntry entry in reader)
                {
                    if (entry.Key is string key && entry.Value is string value)
                        target[key] = value;
                }
            }
            catch { }
        }
    }

    private static bool IsCultureName(string candidate)
    {
        if (string.IsNullOrEmpty(candidate) || candidate.Length < 2) return false;
        if (!KnownCultures.Contains(candidate, StringComparer.OrdinalIgnoreCase)) return false;
        try { CultureInfo.GetCultureInfo(candidate); return true; }
        catch { return false; }
    }

    private static Dictionary<string, string> BuildTable()
    {
        var table = new Dictionary<string, string>(ByCulture.TryGetValue("neutral", out var neutral)
            ? neutral
            : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            StringComparer.OrdinalIgnoreCase);

        var chain = new List<string>();
        var ci = _culture;
        while (ci is not null && !ci.Equals(CultureInfo.InvariantCulture))
        {
            if (!string.IsNullOrEmpty(ci.Name)) chain.Add(ci.Name);
            ci = ci.Parent;
        }
        chain.Reverse();

        foreach (var culture in chain)
        {
            if (!ByCulture.TryGetValue(culture, out var dict)) continue;
            foreach (var entry in dict) table[entry.Key] = entry.Value;
        }
        return table;
    }
}
