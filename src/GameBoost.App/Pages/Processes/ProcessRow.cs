using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using GameBoost.App.Controls;
using GameBoost.Core.Localization;
using GameBoost.Core.Models;

namespace GameBoost.App.Pages.Processes;

public sealed class ProcessRow : INotifyPropertyChanged
{
    private bool _isChecked;
    private bool _isSelected;

    public ProcessRow(ProcessSnapshot snapshot, bool canClose, string closeReason)
    {
        Snapshot = snapshot;
        CanClose = canClose;
        CloseReason = canClose ? Loc.T("Proc_SelectToClose") : closeReason;
    }

    public ProcessSnapshot Snapshot { get; }

    public bool CanClose { get; }

    public string CloseReason { get; }

    public bool IsChecked
    {
        get => _isChecked;
        set
        {
            if (_isChecked == value) return;
            _isChecked = value;
            OnPropertyChanged();
        }
    }

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value) return;
            _isSelected = value;
            OnPropertyChanged();
        }
    }

    public int Pid => Snapshot.ProcessId;

    public string NameLabel => string.IsNullOrWhiteSpace(Snapshot.Name)
        ? Loc.T("Proc_ProcessName", Snapshot.ProcessId)
        : Snapshot.Name;

    public string PidLabel => "PID " + Snapshot.ProcessId.ToString(CultureInfo.InvariantCulture);

    public string CpuLabel => Snapshot.CpuPercent.ToString("F1", CultureInfo.CurrentCulture) + " %";

    public double CpuValue => Snapshot.CpuPercent;

    public string RamLabel => BytesToSizeConverter.Format(Snapshot.MemoryBytes);

    public long RamValue => Snapshot.MemoryBytes;

    public string GpuLabel => Snapshot.GpuPercent is double gpu
        ? gpu.ToString("F0", CultureInfo.CurrentCulture) + " %"
        : "—";

    public double GpuValue => Snapshot.GpuPercent ?? -1;

    public string DiskLabel => BytesToSizeConverter.Format(
        Snapshot.DiskReadBytesPerSec + Snapshot.DiskWriteBytesPerSec) + "/s";

    public double DiskValue => Snapshot.DiskReadBytesPerSec + Snapshot.DiskWriteBytesPerSec;

    public string PublisherLabel => string.IsNullOrWhiteSpace(Snapshot.Publisher) ? "—" : Snapshot.Publisher;

    public bool IsCritical => Snapshot.IsCritical;

    public string WarningTip => Snapshot.IsCritical
        ? Loc.T("Proc_CriticalProtected")
        : string.Empty;

    public bool HasIcon => !string.IsNullOrWhiteSpace(Snapshot.IconPath);

    public string StartTimeLabel => Snapshot.StartTime == default(DateTime)
        ? Loc.T("Proc_StartUnknown")
        : Snapshot.StartTime.ToString("dd/MM/yyyy HH:mm");

    public string PathLabel => string.IsNullOrWhiteSpace(Snapshot.Path) ? Loc.T("Proc_NoPath") : Snapshot.Path;

    public string TitleLabel => string.IsNullOrWhiteSpace(Snapshot.Title) ? Loc.T("Proc_NoWindow") : Snapshot.Title;

    public bool IsElevated => Snapshot.IsElevated;

    public string ElevationLabel => Snapshot.IsElevated ? Loc.T("Proc_Admin") : Loc.T("Proc_StandardUser");

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
