using System.Globalization;
using System.Management;
using GameBoost.Core.Logging;

namespace GameBoost.Core.Hardware;

public static class WmiHelper
{
    public static List<Dictionary<string, object?>> Query(string scope, string wql)
    {
        var rows = new List<Dictionary<string, object?>>();
        try
        {
            using var searcher = new ManagementObjectSearcher(scope, wql);
            using var collection = searcher.Get();
            foreach (ManagementBaseObject item in collection)
            {
                var row = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
                foreach (PropertyData property in item.Properties)
                    row[property.Name] = property.Value;
                rows.Add(row);
            }
        }
        catch (Exception ex)
        {
            Log.Warn("Wmi", "Requête impossible (" + scope + ") : " + wql + " - " + ex.Message);
        }
        return rows;
    }

    public static object? Value(IReadOnlyDictionary<string, object?> row, params string[] names)
    {
        foreach (var name in names)
        {
            if (row.TryGetValue(name, out var value) && value is not null && value is not DBNull)
                return value;
        }
        return null;
    }

    public static string Text(IReadOnlyDictionary<string, object?> row, string fallback, params string[] names)
    {
        var text = Value(row, names) switch
        {
            null => null,
            string raw => raw.Trim(),
            DateTime date => date.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture),
            Array => null,
            var other => Convert.ToString(other, CultureInfo.InvariantCulture)?.Trim()
        };
        return string.IsNullOrEmpty(text) ? fallback : text;
    }

    public static string DateText(IReadOnlyDictionary<string, object?> row, string fallback, params string[] names)
    {
        var date = Date(row, names);
        return date is null ? fallback : date.Value.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);
    }

    public static DateTime? Date(IReadOnlyDictionary<string, object?> row, params string[] names)
    {
        switch (Value(row, names))
        {
            case null:
                return null;
            case DateTime date:
                return date;
            case string raw:
                var text = raw.Trim();
                if (text.Length == 0) return null;
                try
                {
                    return ManagementDateTimeConverter.ToDateTime(text);
                }
                catch
                {
                }
                return DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)
                    ? parsed
                    : null;
            default:
                return null;
        }
    }

    public static long? Number(IReadOnlyDictionary<string, object?> row, params string[] names)
    {
        switch (Value(row, names))
        {
            case null:
            case Array:
                return null;
            case bool flag:
                return flag ? 1 : 0;
            case string raw:
                var text = raw.Trim();
                if (long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
                    return parsed;
                if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var real))
                    return (long)Math.Round(real);
                return null;
            default:
                try
                {
                    return Convert.ToInt64(Value(row, names), CultureInfo.InvariantCulture);
                }
                catch
                {
                    return null;
                }
        }
    }

    public static double? Real(IReadOnlyDictionary<string, object?> row, params string[] names)
    {
        switch (Value(row, names))
        {
            case null:
            case Array:
                return null;
            case bool flag:
                return flag ? 1 : 0;
            case string raw:
                var text = raw.Trim();
                return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
                    ? parsed
                    : null;
            default:
                try
                {
                    return Convert.ToDouble(Value(row, names), CultureInfo.InvariantCulture);
                }
                catch
                {
                    return null;
                }
        }
    }

    public static bool Flag(IReadOnlyDictionary<string, object?> row, bool fallback, params string[] names)
    {
        switch (Value(row, names))
        {
            case null:
            case Array:
                return fallback;
            case bool flag:
                return flag;
            case string raw:
                var text = raw.Trim();
                if (bool.TryParse(text, out var parsed)) return parsed;
                if (long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number)) return number != 0;
                return fallback;
            default:
                try
                {
                    return Convert.ToInt64(Value(row, names), CultureInfo.InvariantCulture) != 0;
                }
                catch
                {
                    return fallback;
                }
        }
    }

    public static List<int> IntList(IReadOnlyDictionary<string, object?> row, params string[] names)
    {
        var values = new List<int>();
        switch (Value(row, names))
        {
            case null:
                return values;
            case Array array:
                foreach (var item in array)
                {
                    if (int.TryParse(Convert.ToString(item, CultureInfo.InvariantCulture), out var parsed))
                        values.Add(parsed);
                }
                return values;
            case string raw:
                foreach (var part in raw.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    if (int.TryParse(part.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
                        values.Add(parsed);
                }
                return values;
            default:
                var single = Number(row, names);
                if (single is not null) values.Add((int)single.Value);
                return values;
        }
    }
}
