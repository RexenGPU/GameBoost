using System.Diagnostics;
using System.Globalization;
using System.Text;
using GameBoost.Core.Logging;
using GameBoost.Core.Models;
using Microsoft.Win32;

namespace GameBoost.Core.Profiles;

public sealed partial class ProfileApplier
{
    private sealed class ConfigTarget
    {
        public string Kind { get; set; } = KindFile;
        public string Path { get; set; } = string.Empty;
        public string Origin { get; set; } = string.Empty;
    }

    private sealed class PendingEdit
    {
        public string Canonical { get; set; } = string.Empty;
        public string ActualKey { get; set; } = string.Empty;
        public string ValueName { get; set; } = string.Empty;
        public int Start { get; set; }
        public int End { get; set; }
        public string OldValue { get; set; } = string.Empty;
        public string NewValue { get; set; } = string.Empty;
    }

    private sealed class TargetPlan
    {
        public ConfigTarget Target { get; set; } = new();
        public List<PendingEdit> Edits { get; } = new();
        public int Recognized { get; set; }
        public int Applicable { get; set; }
        public string? OriginalText { get; set; }
        public string? ModifiedText { get; set; }
        public Encoding Encoding { get; set; } = new UTF8Encoding(false);
    }

    private sealed class IniAnalysis
    {
        public List<PendingEdit> Edits { get; } = new();
        public int Recognized { get; set; }
        public int Applicable { get; set; }
    }

    private sealed class BackupManifest
    {
        public DateTime CreatedAt { get; set; }
        public string GameName { get; set; } = string.Empty;
        public string Slug { get; set; } = string.Empty;
        public List<ManifestEntry> Entries { get; set; } = new();
    }

    private sealed class ManifestEntry
    {
        public string Type { get; set; } = KindFile;
        public string RelativePath { get; set; } = string.Empty;
        public string OriginalPath { get; set; } = string.Empty;
    }

    private static readonly Dictionary<string, string> UnityValueByCanonical = new(StringComparer.OrdinalIgnoreCase)
    {
        [ProfileMappings.KResWidth] = "Screenmanager Resolution Width_px",
        [ProfileMappings.KResHeight] = "Screenmanager Resolution Height_px",
        [ProfileMappings.KFullscreen] = "Is Fullscreen mode"
    };

    private static readonly Dictionary<string, DesiredValue> NoDesired = new(StringComparer.OrdinalIgnoreCase);

    private List<ConfigTarget> LocateTargets(GameInfo game)
    {
        var roots = BuildRoots(game);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var targets = new List<ConfigTarget>();

        foreach (var root in roots)
        {
            foreach (var pattern in ProfileMappings.UnrealFilePatterns)
            {
                try
                {
                    var candidate = Path.Combine(root, pattern.Replace('/', Path.DirectorySeparatorChar));
                    if (File.Exists(candidate) && seen.Add(candidate))
                        targets.Add(new ConfigTarget { Kind = KindFile, Path = candidate, Origin = "Unreal Engine" });
                }
                catch (Exception ex)
                {
                    Log.Warn("Profiles", "Chemin de configuration invalide : " + ex.Message);
                }
            }
        }

        if (!string.IsNullOrWhiteSpace(game.InstallPath) && Directory.Exists(game.InstallPath))
            CollectByName(game.InstallPath, "GameUserSettings.ini", 4, targets, seen);

        if (targets.Count > 0) return targets;

        foreach (var root in roots)
        {
            if (targets.Count >= 5) break;
            foreach (var file in EnumerateFiles(root, 3, 400))
            {
                if (targets.Count >= 5) break;
                if (!seen.Add(file)) continue;
                if (!HasRecognizedKey(file)) continue;
                targets.Add(new ConfigTarget { Kind = KindFile, Path = file, Origin = "Générique" });
            }
        }

        if (targets.Count > 0) return targets;

        var registryKey = FindUnityRegistryKey(game);
        if (!string.IsNullOrEmpty(registryKey))
            targets.Add(new ConfigTarget { Kind = KindRegistry, Path = registryKey, Origin = "Unity" });

        return targets;
    }

    private static List<string> BuildRoots(GameInfo game)
    {
        var roots = new List<string>();
        foreach (var name in CandidateNames(game))
        {
            try
            {
                var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                AddRoot(roots, Path.Combine(local, name));
                AddRoot(roots, Path.Combine(documents, name));
            }
            catch (Exception ex)
            {
                Log.Warn("Profiles", "Dossier secondaire invalide pour " + name + " : " + ex.Message);
            }
        }
        AddRoot(roots, game.InstallPath);
        return roots;
    }

    private static void AddRoot(List<string> roots, string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        try
        {
            var full = Path.GetFullPath(path);
            if (roots.Any(r => string.Equals(r, full, StringComparison.OrdinalIgnoreCase))) return;
            roots.Add(full);
        }
        catch (Exception ex)
        {
            Log.Warn("Profiles", "Chemin racine invalide : " + ex.Message);
        }
    }

    private static List<string> CandidateNames(GameInfo game)
    {
        var names = new List<string>();
        void Add(string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return;
            var trimmed = value.Trim();
            if (trimmed.Length == 0) return;
            if (trimmed.IndexOfAny(Path.GetInvalidPathChars()) >= 0) return;
            if (names.Any(n => string.Equals(n, trimmed, StringComparison.OrdinalIgnoreCase))) return;
            names.Add(trimmed);
        }

        Add(game.Name);
        if (!string.IsNullOrWhiteSpace(game.ExecutablePath))
            Add(Path.GetFileNameWithoutExtension(game.ExecutablePath));
        if (!string.IsNullOrWhiteSpace(game.InstallPath))
        {
            try
            {
                var directory = new DirectoryInfo(game.InstallPath);
                Add(directory.Name);
                Add(directory.Parent?.Name);
            }
            catch (Exception ex)
            {
                Log.Warn("Profiles", "Dossier d'installation illisible : " + ex.Message);
            }
        }
        return names;
    }

    private static void CollectByName(string root, string fileName, int depth, List<ConfigTarget> targets, HashSet<string> seen)
    {
        foreach (var file in EnumerateFilesByName(root, fileName, depth))
        {
            if (targets.Count >= 10) return;
            if (!seen.Add(file)) continue;
            targets.Add(new ConfigTarget { Kind = KindFile, Path = file, Origin = "Unreal Engine" });
        }
    }

    private static IEnumerable<string> EnumerateFilesByName(string root, string fileName, int maxDepth)
    {
        if (!Directory.Exists(root)) yield break;
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var stack = new Stack<(string Path, int Depth)>();
        stack.Push((root, 0));
        while (stack.Count > 0)
        {
            var (directory, depth) = stack.Pop();
            if (!visited.Add(directory)) continue;
            string[] files;
            try
            {
                files = Directory.GetFiles(directory, fileName);
            }
            catch (Exception ex)
            {
                Log.Warn("Profiles", "Dossier ignoré (" + directory + ") : " + ex.Message);
                continue;
            }
            foreach (var file in files) yield return file;
            if (depth >= maxDepth) continue;
            foreach (var child in Subdirectories(directory)) stack.Push((child, depth + 1));
        }
    }

    private static IEnumerable<string> EnumerateFiles(string root, int maxDepth, int maxFiles)
    {
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root)) yield break;
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var stack = new Stack<(string Path, int Depth)>();
        var yielded = 0;
        stack.Push((root, 0));
        while (stack.Count > 0 && yielded < maxFiles)
        {
            var (directory, depth) = stack.Pop();
            if (!visited.Add(directory)) continue;
            string[] files;
            try
            {
                files = Directory.GetFiles(directory);
            }
            catch (Exception ex)
            {
                Log.Warn("Profiles", "Dossier ignoré (" + directory + ") : " + ex.Message);
                continue;
            }
            foreach (var file in files)
            {
                if (yielded >= maxFiles) yield break;
                var extension = Path.GetExtension(file);
                if (string.Equals(extension, ".ini", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(extension, ".cfg", StringComparison.OrdinalIgnoreCase))
                {
                    yielded++;
                    yield return file;
                }
            }
            if (depth >= maxDepth) continue;
            foreach (var child in Subdirectories(directory)) stack.Push((child, depth + 1));
        }
    }

    private static List<string> Subdirectories(string directory)
    {
        var result = new List<string>();
        try
        {
            foreach (var child in Directory.GetDirectories(directory))
            {
                try
                {
                    if ((File.GetAttributes(child) & FileAttributes.ReparsePoint) != 0) continue;
                }
                catch
                {
                    continue;
                }
                result.Add(child);
            }
        }
        catch (Exception ex)
        {
            Log.Warn("Profiles", "Sous-dossiers illisibles (" + directory + ") : " + ex.Message);
        }
        return result;
    }

    private static bool HasRecognizedKey(string path)
    {
        if (!TryReadText(path, out var text, out _)) return false;
        var analysis = AnalyzeText(text, NoDesired);
        return analysis.Recognized > 0;
    }

    private TargetPlan? BuildPlan(ConfigTarget target, Dictionary<string, DesiredValue> desired)
    {
        if (target.Kind == KindRegistry) return BuildRegistryPlan(target, desired);

        if (!TryReadText(target.Path, out var text, out var encoding)) return null;
        var analysis = AnalyzeText(text, desired);
        var plan = new TargetPlan
        {
            Target = target,
            OriginalText = text,
            Encoding = encoding,
            Recognized = analysis.Recognized,
            Applicable = analysis.Applicable
        };
        foreach (var edit in analysis.Edits) plan.Edits.Add(edit);

        if (plan.Edits.Count > 0)
        {
            var builder = new StringBuilder(text);
            foreach (var edit in plan.Edits.OrderByDescending(e => e.Start))
            {
                builder.Remove(edit.Start, edit.End - edit.Start);
                builder.Insert(edit.Start, edit.NewValue);
            }
            plan.ModifiedText = builder.ToString();
        }
        else
        {
            plan.ModifiedText = text;
        }
        return plan;
    }

    private static TargetPlan? BuildRegistryPlan(ConfigTarget target, Dictionary<string, DesiredValue> desired)
    {
        using var key = Registry.CurrentUser.OpenSubKey(target.Path, false);
        if (key is null) return null;
        var plan = new TargetPlan { Target = target };
        var existingNames = key.GetValueNames();

        foreach (var valueName in ProfileMappings.UnityValueNames)
        {
            var actualName = existingNames.FirstOrDefault(n => string.Equals(n, valueName, StringComparison.OrdinalIgnoreCase));
            if (string.IsNullOrEmpty(actualName)) continue;
            plan.Recognized++;
            if (!ProfileMappings.Aliases.TryGetValue(valueName, out var canonical)) continue;
            if (!desired.TryGetValue(canonical, out var desiredValue)) continue;
            plan.Applicable++;

            var raw = key.GetValue(actualName);
            if (raw is null) continue;
            var existing = Convert.ToString(raw, CultureInfo.InvariantCulture) ?? string.Empty;
            if (!ProfileMappings.TryFormatValue(canonical, actualName, desiredValue, existing, out var newValue)) continue;
            if (string.Equals(newValue, existing.Trim(), StringComparison.Ordinal)) continue;
            plan.Edits.Add(new PendingEdit
            {
                Canonical = canonical,
                ActualKey = actualName,
                ValueName = actualName,
                OldValue = existing.Trim(),
                NewValue = newValue
            });
        }
        return plan;
    }

    private static void ApplyRegistryEdits(TargetPlan plan)
    {
        using var key = Registry.CurrentUser.OpenSubKey(plan.Target.Path, true);
        if (key is null) throw new InvalidOperationException("Clé de registre introuvable : " + plan.Target.Path);
        foreach (var edit in plan.Edits)
        {
            if (!int.TryParse(edit.NewValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number))
                throw new InvalidOperationException("Valeur non prise en charge pour " + edit.ValueName);
            var kind = key.GetValueKind(edit.ValueName);
            key.SetValue(edit.ValueName, number, kind);
        }
    }

    private static string? FindUnityRegistryKey(GameInfo game)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in CandidateNames(game))
        {
            var normalized = ProfileMappings.Normalize(name);
            if (normalized.Length > 0) names.Add(normalized);
        }
        if (names.Count == 0) return null;

        try
        {
            using var software = Registry.CurrentUser.OpenSubKey("Software");
            if (software is null) return null;
            var examined = 0;
            foreach (var companyName in software.GetSubKeyNames())
            {
                if (examined > 8000) break;
                var companyMatches = names.Contains(ProfileMappings.Normalize(companyName));
                using var company = software.OpenSubKey(companyName);
                if (company is null) continue;
                if (companyMatches && LooksLikeUnity(company))
                    return "Software\\" + companyName;

                foreach (var productName in company.GetSubKeyNames())
                {
                    if (examined > 8000) break;
                    if (!companyMatches && !names.Contains(ProfileMappings.Normalize(productName))) continue;
                    examined++;
                    using var product = company.OpenSubKey(productName);
                    if (product is null) continue;
                    if (!LooksLikeUnity(product)) continue;
                    return "Software\\" + companyName + "\\" + productName;
                }
            }
        }
        catch (Exception ex)
        {
            Log.Warn("Profiles", "Lecture du registre utilisateur impossible : " + ex.Message);
        }
        return null;
    }

    private static bool LooksLikeUnity(RegistryKey key)
    {
        var values = key.GetValueNames();
        foreach (var valueName in ProfileMappings.UnityValueNames)
        {
            if (values.Any(v => string.Equals(v, valueName, StringComparison.OrdinalIgnoreCase))) return true;
        }
        return false;
    }

    private static bool TryReadText(string path, out string text, out Encoding encoding)
    {
        text = string.Empty;
        encoding = new UTF8Encoding(false);
        try
        {
            var bytes = File.ReadAllBytes(path);
            encoding = DetectEncoding(bytes, out var bomLength);
            using var stream = new MemoryStream(bytes, bomLength, bytes.Length - bomLength);
            using var reader = new StreamReader(stream, encoding, false);
            text = reader.ReadToEnd();
            return true;
        }
        catch (Exception ex)
        {
            Log.Error("Profiles", "Lecture de " + path + " impossible", ex);
            return false;
        }
    }

    private static Encoding DetectEncoding(byte[] bytes, out int bomLength)
    {
        bomLength = 0;
        if (StartsWith(bytes, 0xFF, 0xFE, 0x00, 0x00))
        {
            bomLength = 4;
            return new UTF32Encoding(false, true);
        }
        if (StartsWith(bytes, 0x00, 0x00, 0xFE, 0xFF))
        {
            bomLength = 4;
            return new UTF32Encoding(true, true);
        }
        if (StartsWith(bytes, 0xEF, 0xBB, 0xBF))
        {
            bomLength = 3;
            return new UTF8Encoding(true);
        }
        if (StartsWith(bytes, 0xFF, 0xFE))
        {
            bomLength = 2;
            return Encoding.Unicode;
        }
        if (StartsWith(bytes, 0xFE, 0xFF))
        {
            bomLength = 2;
            return Encoding.BigEndianUnicode;
        }
        try
        {
            var strict = new UTF8Encoding(false, true);
            strict.GetCharCount(bytes);
            return new UTF8Encoding(false);
        }
        catch (ArgumentException)
        {
            return Encoding.Latin1;
        }
    }

    private static bool StartsWith(byte[] bytes, params byte[] prefix)
    {
        if (bytes.Length < prefix.Length) return false;
        for (var i = 0; i < prefix.Length; i++)
        {
            if (bytes[i] != prefix[i]) return false;
        }
        return true;
    }

    private static IniAnalysis AnalyzeText(string text, Dictionary<string, DesiredValue> desired)
    {
        var analysis = new IniAnalysis();
        if (string.IsNullOrEmpty(text)) return analysis;
        var position = 0;
        var length = text.Length;
        while (position < length)
        {
            var lineEnd = position;
            while (lineEnd < length && text[lineEnd] != '\r' && text[lineEnd] != '\n') lineEnd++;
            var line = text.Substring(position, lineEnd - position);
            AnalyzeLine(line, position, desired, analysis);
            if (lineEnd >= length) break;
            if (text[lineEnd] == '\r' && lineEnd + 1 < length && text[lineEnd + 1] == '\n') position = lineEnd + 2;
            else position = lineEnd + 1;
        }
        return analysis;
    }

    private static void AnalyzeLine(string line, int lineStart, Dictionary<string, DesiredValue> desired, IniAnalysis analysis)
    {
        var index = 0;
        while (index < line.Length && (line[index] == ' ' || line[index] == '\t')) index++;
        if (index >= line.Length) return;
        var first = line[index];
        if (first is ';' or '#' or '[' or ']' or '@') return;
        if (first == '/') return;

        var separator = -1;
        for (var i = index; i < line.Length; i++)
        {
            var c = line[i];
            if (c == '=' || c == ':')
            {
                separator = i;
                break;
            }
            if (c is ';' or '#' or '[') break;
        }
        if (separator < 0) return;

        var key = line.Substring(index, separator - index).Trim();
        if (key.Length == 0) return;
        if (!ProfileMappings.Aliases.TryGetValue(key, out var canonical)) return;
        analysis.Recognized++;
        if (!desired.TryGetValue(canonical, out var desiredValue)) return;
        analysis.Applicable++;

        var valueStart = separator + 1;
        while (valueStart < line.Length && (line[valueStart] == ' ' || line[valueStart] == '\t')) valueStart++;
        var valueEnd = line.Length;
        for (var i = valueStart; i < line.Length; i++)
        {
            var c = line[i];
            if ((c == ';' || c == '#') && (i == valueStart || line[i - 1] == ' ' || line[i - 1] == '\t'))
            {
                valueEnd = i;
                break;
            }
        }
        while (valueEnd > valueStart && (line[valueEnd - 1] == ' ' || line[valueEnd - 1] == '\t')) valueEnd--;

        var existing = line.Substring(valueStart, valueEnd - valueStart);
        if (!ProfileMappings.TryFormatValue(canonical, key, desiredValue, existing, out var newValue)) return;
        if (string.Equals(newValue, existing, StringComparison.Ordinal)) return;

        analysis.Edits.Add(new PendingEdit
        {
            Canonical = canonical,
            ActualKey = key,
            Start = lineStart + valueStart,
            End = lineStart + valueEnd,
            OldValue = existing,
            NewValue = newValue
        });
    }

    private static (int ExitCode, string Output) RunProcess(string fileName, string arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Impossible de démarrer " + fileName);
        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(20000))
        {
            try
            {
                process.Kill(true);
            }
            catch (Exception ex)
            {
                Log.Warn("Profiles", "Arrêt de " + fileName + " impossible : " + ex.Message);
            }
            throw new TimeoutException("Délai dépassé pour " + fileName);
        }
        var output = (outputTask.Result ?? string.Empty).Trim() + Environment.NewLine + (errorTask.Result ?? string.Empty).Trim();
        return (process.ExitCode, output);
    }
}
