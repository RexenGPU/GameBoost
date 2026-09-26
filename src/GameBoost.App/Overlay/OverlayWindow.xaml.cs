using System.Windows;
using System.Windows.Threading;
using GameBoost.App.Controls;
using GameBoost.Core.Logging;
using GameBoost.Core.Models;
using GameBoost.Core.Monitoring;

namespace GameBoost.App.Overlay;

public partial class OverlayWindow : Window
{
    private const double EdgeMargin = 24;

    private OverlaySettings _settings = new();

    public OverlayWindow()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            Subscribe();
            Place();
        };
        SizeChanged += (_, _) => Place();
    }

    public void ApplySettings(OverlaySettings? settings)
    {
        if (settings is null) return;
        try
        {
            _settings = settings;
            Opacity = Math.Clamp(settings.Opacity, 0, 100) / 100.0;
            ApplyVisibility();
            Place();
        }
        catch (Exception ex)
        {
            Log.Warn("Overlay", "Reglages overlay non appliques : " + ex.Message);
        }
    }

    public void ShowOverlay()
    {
        try
        {
            ApplyVisibility();
            if (!IsVisible) Show();
            Place();
            Render(SystemMonitor.Instance.Current);
        }
        catch (Exception ex)
        {
            Log.Error("Overlay", "Affichage de l'overlay impossible", ex);
        }
    }

    public void HideOverlay()
    {
        try
        {
            if (IsVisible) Hide();
        }
        catch (Exception ex)
        {
            Log.Warn("Overlay", "Masquage de l'overlay impossible : " + ex.Message);
        }
    }

    private void Subscribe()
    {
        SystemMonitor.Instance.SampleUpdated -= OnSample;
        SystemMonitor.Instance.SampleUpdated += OnSample;
    }

    protected override void OnClosed(EventArgs e)
    {
        SystemMonitor.Instance.SampleUpdated -= OnSample;
        base.OnClosed(e);
    }

    private void OnSample(MonitorSample sample)
    {
        try
        {
            Dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(() => Render(sample)));
        }
        catch (Exception ex)
        {
            Log.Warn("Overlay", "Mise a jour ignoree : " + ex.Message);
        }
    }

    private void Render(MonitorSample? sample)
    {
        if (sample is null) return;
        try
        {
            var stats = SystemMonitor.Instance.GetFpsStats();
            var fps = stats?.Average ?? sample.Fps;
            FpsText.Text = fps is double value && value > 0 && !double.IsNaN(value)
                ? value.ToString("F0") + " FPS"
                : "— FPS";

            var low = stats?.OnePercentLow;
            LowText.Text = low is double lowValue && lowValue > 0 && !double.IsNaN(lowValue)
                ? "1 % low : " + lowValue.ToString("F0") + " FPS"
                : string.Empty;
            LowText.Visibility = _settings.ShowFps && LowText.Text.Length > 0
                ? Visibility.Visible
                : Visibility.Collapsed;

            FrameTimeText.Text = sample.FrameTimeMs is double frameTime && frameTime > 0 && !double.IsNaN(frameTime)
                ? frameTime.ToString("F1") + " ms"
                : "— ms";

            CpuText.Text = "CPU " + sample.CpuUsagePercent.ToString("F0") + " %" + Temperature(sample.CpuTemperatureC);
            GpuText.Text = "GPU " + sample.GpuUsagePercent.ToString("F0") + " %" + Temperature(sample.GpuTemperatureC);
            RamText.Text = "RAM " + BytesToSizeConverter.Format(sample.RamUsedBytes);
        }
        catch (Exception ex)
        {
            Log.Warn("Overlay", "Lecture des mesures impossible : " + ex.Message);
        }
    }

    private string Temperature(double? celsius)
    {
        if (!_settings.ShowTemperatures) return string.Empty;
        if (celsius is not double value || value <= 0 || double.IsNaN(value)) return string.Empty;
        return " · " + value.ToString("F0") + " °C";
    }

    private void ApplyVisibility()
    {
        FpsPanel.Visibility = _settings.ShowFps ? Visibility.Visible : Visibility.Collapsed;
        FrameTimeText.Visibility = _settings.ShowFrameTime ? Visibility.Visible : Visibility.Collapsed;
        CpuText.Visibility = _settings.ShowCpu ? Visibility.Visible : Visibility.Collapsed;
        GpuText.Visibility = _settings.ShowGpu ? Visibility.Visible : Visibility.Collapsed;
        RamText.Visibility = _settings.ShowRam ? Visibility.Visible : Visibility.Collapsed;
        if (!_settings.ShowFps) LowText.Visibility = Visibility.Collapsed;
    }

    private void Place()
    {
        try
        {
            if (ActualWidth <= 1 || ActualHeight <= 1) return;

            var area = SystemParameters.WorkArea;
            var requestedX = (double)_settings.PositionX;
            var requestedY = (double)_settings.PositionY;
            var fromRight = requestedX > area.Width / 2;
            var fromBottom = requestedY > area.Height / 2;

            var x = fromRight
                ? area.Width - ActualWidth - EdgeMargin
                : Math.Clamp(requestedX, EdgeMargin, Math.Max(EdgeMargin, area.Width - ActualWidth));
            var y = fromBottom
                ? area.Height - ActualHeight - EdgeMargin
                : Math.Clamp(requestedY, EdgeMargin, Math.Max(EdgeMargin, area.Height - ActualHeight));

            Left = Math.Round(Math.Max(0, x));
            Top = Math.Round(Math.Max(0, y));
        }
        catch (Exception ex)
        {
            Log.Warn("Overlay", "Positionnement de l'overlay impossible : " + ex.Message);
        }
    }
}
