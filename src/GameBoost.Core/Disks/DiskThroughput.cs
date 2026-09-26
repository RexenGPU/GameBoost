using System.Globalization;
using GameBoost.Core.Hardware;
using GameBoost.Core.Logging;

namespace GameBoost.Core.Disks;

internal static class DiskThroughput
{
    private const int SampleCount = 4;
    private const int SampleIntervalMs = 500;

    internal static Dictionary<int, (double ReadMBs, double WriteMBs)> Sample()
    {
        var result = new Dictionary<int, (double ReadMBs, double WriteMBs)>();
        var totals = new Dictionary<int, (double Read, double Write, int Count, string Instance)>();
        try
        {
            for (var sample = 0; sample < SampleCount; sample++)
            {
                if (sample > 0) Thread.Sleep(SampleIntervalMs);
                var rows = WmiHelper.Query(@"root\cimv2", "SELECT * FROM Win32_PerfFormattedData_PerfDisk_PhysicalDisk");
                if (rows.Count == 0) continue;
                foreach (var row in rows)
                {
                    var instance = WmiHelper.Text(row, string.Empty, "Name").Trim();
                    if (instance.Length == 0 || instance.StartsWith("_", StringComparison.Ordinal)) continue;
                    var separator = instance.IndexOf(' ');
                    var head = separator > 0 ? instance[..separator] : instance;
                    if (!int.TryParse(head, NumberStyles.Integer, CultureInfo.InvariantCulture, out var disk)) continue;
                    var read = WmiHelper.Real(row, "DiskReadBytesPersec") ?? 0;
                    var write = WmiHelper.Real(row, "DiskWritePersec", "DiskWriteBytesPersec") ?? 0;
                    if (!totals.TryGetValue(disk, out var entry))
                    {
                        totals[disk] = (read, write, 1, instance);
                    }
                    else if (string.Equals(entry.Instance, instance, StringComparison.OrdinalIgnoreCase))
                    {
                        totals[disk] = (entry.Read + read, entry.Write + write, entry.Count + 1, entry.Instance);
                    }
                }
            }

            foreach (var (disk, entry) in totals)
            {
                if (entry.Count <= 0) continue;
                result[disk] = (Math.Round(entry.Read / entry.Count / 1048576d, 1),
                    Math.Round(entry.Write / entry.Count / 1048576d, 1));
            }
        }
        catch (Exception ex)
        {
            Log.Warn("Disks", "Mesure des débits impossible : " + ex.Message);
            return new Dictionary<int, (double ReadMBs, double WriteMBs)>();
        }

        if (result.Count == 0) Log.Warn("Disks", "Aucun compteur de débit disponible pour les disques");
        return result;
    }
}
