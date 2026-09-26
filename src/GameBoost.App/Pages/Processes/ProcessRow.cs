using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using GameBoost.App.Controls;
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
        CloseReason = canClose ? "Sélectionner pour fermer ce processus" : closeReason;
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

    public string NameLabel => string.IsNullOrWhiteSpace(Snapshot.Name) ? "Processus " + Snapshot.ProcessId : Snapshot.Name;

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
        ? "Processus critique ou protégé : GameBoost ne le fermera jamais"
        : string.Empty;

    public bool HasIcon => !string.IsNullOrWhiteSpace(Snapshot.IconPath);

    public string StartTimeLabel => Snapshot.StartTime == default(DateTime)
        ? "Heure de démarrage inconnue"
        : Snapshot.StartTime.ToString("dd/MM/yyyy HH:mm");

    public string PathLabel => string.IsNullOrWhiteSpace(Snapshot.Path) ? "Chemin inaccessible" : Snapshot.Path;

    public string TitleLabel => string.IsNullOrWhiteSpace(Snapshot.Title) ? "Aucune fenêtre active" : Snapshot.Title;

    public bool IsElevated => Snapshot.IsElevated;

    public string ElevationLabel => Snapshot.IsElevated ? "Administrateur" : "Utilisateur standard";

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
