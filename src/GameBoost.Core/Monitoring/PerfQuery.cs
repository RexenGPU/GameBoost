using System.Runtime.InteropServices;
using System.Text;
using GameBoost.Core.Logging;

namespace GameBoost.Core.Monitoring;

internal readonly struct PerfSample
{
    public PerfSample(string instance, double value)
    {
        Instance = instance;
        Value = value;
    }

    public string Instance { get; }
    public double Value { get; }
}

internal sealed class PerfQuery : IDisposable
{
    private const uint PdhMoreData = 0x800007D2;
    private const uint PdhFmtDouble = 0x00000200;
    private const uint MaxBufferBytes = 8 * 1024 * 1024;

    private readonly object _sync = new();
    private readonly List<IntPtr> _counters = new();
    private IntPtr _query;
    private bool _disposed;
    private bool _warned;

    private PerfQuery()
    {
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PdhFmtCounterValue
    {
        public int CStatus;
        public long Bits;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PdhFmtCounterValueItem
    {
        public IntPtr SzName;
        public PdhFmtCounterValue Value;
    }

    [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
    private static extern uint PdhOpenQueryW(IntPtr userData, uint flags, out IntPtr query);

    [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
    private static extern uint PdhAddEnglishCounterW(IntPtr query, string counterPath, uint userData, out IntPtr counter);

    [DllImport("pdh.dll")]
    private static extern uint PdhCollectQueryData(IntPtr query);

    [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
    private static extern uint PdhGetFormattedCounterArrayW(IntPtr counter, uint format, ref uint bufferSize, ref uint itemCount, IntPtr itemBuffer);

    [DllImport("pdh.dll")]
    private static extern uint PdhCloseQuery(IntPtr query);

    public static PerfQuery? TryCreate()
    {
        try
        {
            var status = PdhOpenQueryW(IntPtr.Zero, 0, out var query);
            if (status != 0)
            {
                Log.Warn("Monitor", "Ouverture de la session de compteurs impossible (0x" + status.ToString("X8") + ")");
                return null;
            }
            return new PerfQuery { _query = query };
        }
        catch (Exception ex)
        {
            Log.Error("Monitor", "Creation de la session de compteurs impossible", ex);
            return null;
        }
    }

    public bool TryAddEnglish(string counterPath, out int index)
    {
        index = -1;
        lock (_sync)
        {
            if (_disposed || _query == IntPtr.Zero) return false;
            try
            {
                var status = PdhAddEnglishCounterW(_query, counterPath, 0, out var counter);
                if (status != 0) return false;
                _counters.Add(counter);
                index = _counters.Count - 1;
                return true;
            }
            catch (Exception ex)
            {
                Log.Warn("Monitor", "Compteur inaccessible : " + counterPath + " : " + ex.Message);
                return false;
            }
        }
    }

    public bool Collect()
    {
        lock (_sync)
        {
            if (_disposed || _query == IntPtr.Zero) return false;
            try
            {
                var status = PdhCollectQueryData(_query);
                if (status == 0) return true;
                if (!_warned)
                {
                    _warned = true;
                    Log.Warn("Monitor", "Lecture des compteurs impossible (0x" + status.ToString("X8") + ")");
                }
                return false;
            }
            catch (Exception ex)
            {
                if (!_warned)
                {
                    _warned = true;
                    Log.Error("Monitor", "Lecture des compteurs impossible", ex);
                }
                return false;
            }
        }
    }

    public bool TryRead(int index, List<PerfSample> buffer)
    {
        buffer.Clear();
        lock (_sync)
        {
            if (_disposed || index < 0 || index >= _counters.Count) return false;
            var counter = _counters[index];
            IntPtr bufferPtr = IntPtr.Zero;
            try
            {
                uint size = 0;
                uint count = 0;
                var status = PdhGetFormattedCounterArrayW(counter, PdhFmtDouble, ref size, ref count, IntPtr.Zero);
                if (status != PdhMoreData && status != 0) return false;
                if (size == 0 || count == 0 || size > MaxBufferBytes) return false;
                bufferPtr = Marshal.AllocHGlobal((int)size);
                status = PdhGetFormattedCounterArrayW(counter, PdhFmtDouble, ref size, ref count, bufferPtr);
                if (status != 0) return false;
                var itemSize = Marshal.SizeOf<PdhFmtCounterValueItem>();
                for (var i = 0; i < count; i++)
                {
                    var item = Marshal.PtrToStructure<PdhFmtCounterValueItem>(IntPtr.Add(bufferPtr, i * itemSize));
                    if (item.Value.CStatus != 0) continue;
                    var value = BitConverter.Int64BitsToDouble(item.Value.Bits);
                    if (double.IsNaN(value) || double.IsInfinity(value)) continue;
                    var name = Marshal.PtrToStringUni(item.SzName);
                    buffer.Add(new PerfSample(name ?? string.Empty, value));
                }
            }
            catch (Exception ex)
            {
                Log.Warn("Monitor", "Lecture d'un compteur impossible : " + ex.Message);
                buffer.Clear();
            }
            finally
            {
                if (bufferPtr != IntPtr.Zero) Marshal.FreeHGlobal(bufferPtr);
            }
        }
        return buffer.Count > 0;
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed) return;
            _disposed = true;
            try
            {
                if (_query != IntPtr.Zero) PdhCloseQuery(_query);
            }
            catch (Exception ex)
            {
                Log.Warn("Monitor", "Fermeture de la session de compteurs : " + ex.Message);
            }
            _query = IntPtr.Zero;
            _counters.Clear();
        }
    }
}
