using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace GameBoost.App.Controls;

public class NavItem : RadioButton
{
    public static readonly DependencyProperty IconProperty = DependencyProperty.Register(
        nameof(Icon), typeof(string), typeof(NavItem), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty GlyphProperty = DependencyProperty.Register(
        nameof(Glyph), typeof(string), typeof(NavItem), new PropertyMetadata(string.Empty));

    public string Icon
    {
        get => (string)GetValue(IconProperty);
        set => SetValue(IconProperty, value);
    }

    public string Glyph
    {
        get => (string)GetValue(GlyphProperty);
        set => SetValue(GlyphProperty, value);
    }
}

public sealed class ChartSeries
{
    public string Name { get; set; } = string.Empty;
    public List<double> Values { get; set; } = new();
    public Color Color { get; set; } = Color.FromRgb(0x22, 0xC5, 0x5E);
    public double Thickness { get; set; } = 2;
    public bool Area { get; set; } = true;
}

public class LineChart : FrameworkElement
{
    public static readonly DependencyProperty SeriesProperty = DependencyProperty.Register(
        nameof(Series), typeof(IEnumerable<ChartSeries>), typeof(LineChart),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ShowGridProperty = DependencyProperty.Register(
        nameof(ShowGrid), typeof(bool), typeof(LineChart),
        new FrameworkPropertyMetadata(true, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ShowAxisLabelsProperty = DependencyProperty.Register(
        nameof(ShowAxisLabels), typeof(bool), typeof(LineChart),
        new FrameworkPropertyMetadata(true, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty MaxPointsProperty = DependencyProperty.Register(
        nameof(MaxPoints), typeof(int), typeof(LineChart),
        new FrameworkPropertyMetadata(0, FrameworkPropertyMetadataOptions.AffectsRender));

    public IEnumerable<ChartSeries>? Series
    {
        get => (IEnumerable<ChartSeries>?)GetValue(SeriesProperty);
        set => SetValue(SeriesProperty, value);
    }

    public bool ShowGrid
    {
        get => (bool)GetValue(ShowGridProperty);
        set => SetValue(ShowGridProperty, value);
    }

    public bool ShowAxisLabels
    {
        get => (bool)GetValue(ShowAxisLabelsProperty);
        set => SetValue(ShowAxisLabelsProperty, value);
    }

    public int MaxPoints
    {
        get => (int)GetValue(MaxPointsProperty);
        set => SetValue(MaxPointsProperty, value);
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        var width = ActualWidth;
        var height = ActualHeight;
        if (width <= 4 || height <= 4 || Series is null) return;

        var series = Series.Where(s => s.Values.Count > 0).ToList();
        if (series.Count == 0) return;

        var min = double.MaxValue;
        var max = double.MinValue;
        foreach (var s in series)
        {
            foreach (var v in s.Values)
            {
                if (double.IsNaN(v)) continue;
                if (v < min) min = v;
                if (v > max) max = v;
            }
        }
        if (min == double.MaxValue) return;
        if (Math.Abs(max - min) < 1e-6)
        {
            min = Math.Max(0, min - 1);
            max += 1;
        }
        var pad = (max - min) * 0.08;
        min -= pad;
        max += pad;
        if (min < 0 && series.All(s => s.Values.All(v => v >= 0))) min = 0;

        var labelPad = ShowAxisLabels ? 34d : 4d;
        var plotX = 4d;
        var plotW = Math.Max(8, width - labelPad - plotX);
        var plotY = 4d;
        var plotH = Math.Max(8, height - 8);

        var gridPen = new Pen(new SolidColorBrush(Color.FromArgb(0x18, 0x80, 0x90, 0xA0)), 1);
        var gridBrush = Application.Current?.TryFindResource("ChartGridBrush") as Brush ?? gridPen.Brush;
        gridPen = new Pen(gridBrush, 1);

        if (ShowGrid)
        {
            for (var i = 0; i <= 4; i++)
            {
                var y = plotY + plotH * i / 4;
                dc.DrawLine(gridPen, new Point(plotX, y), new Point(plotX + plotW, y));
            }
        }

        if (ShowAxisLabels)
        {
            var typeface = new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
            var labelBrush = Application.Current?.TryFindResource("TextFaintBrush") as Brush ?? Brushes.Gray;
            var top = new FormattedText(max.ToString("F0"), System.Globalization.CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight, typeface, 10, labelBrush, 1.0);
            var bottom = new FormattedText(min.ToString("F0"), System.Globalization.CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight, typeface, 10, labelBrush, 1.0);
            dc.DrawText(top, new Point(plotX + plotW + 5, plotY));
            dc.DrawText(bottom, new Point(plotX + plotW + 5, plotY + plotH - bottom.Height));
        }

        foreach (var s in series)
        {
            var values = s.Values;
            var step = 1;
            if (MaxPoints > 0 && values.Count > MaxPoints) step = (int)Math.Ceiling(values.Count / (double)MaxPoints);
            var points = new List<Point>(values.Count / step + 2);
            for (var i = 0; i < values.Count; i += step)
            {
                var v = values[i];
                if (double.IsNaN(v)) continue;
                var x = plotX + plotW * (values.Count <= 1 ? 1 : (double)i / (values.Count - 1));
                var ratio = (v - min) / (max - min);
                var y = plotY + plotH * (1 - ratio);
                points.Add(new Point(x, y));
            }
            if (points.Count < 2) continue;

            if (s.Area)
            {
                var fill = new SolidColorBrush(Color.FromArgb(0x2E, s.Color.R, s.Color.G, s.Color.B));
                var geo = new StreamGeometry();
                using (var ctx = geo.Open())
                {
                    ctx.BeginFigure(new Point(points[0].X, plotY + plotH), true, true);
                    foreach (var p in points) ctx.LineTo(p, true, true);
                    ctx.LineTo(new Point(points[^1].X, plotY + plotH), true, true);
                }
                geo.Freeze();
                dc.DrawGeometry(fill, null, geo);
            }

            var pen = new Pen(new SolidColorBrush(s.Color), s.Thickness)
            {
                StartLineCap = PenLineCap.Round,
                EndLineCap = PenLineCap.Round,
                LineJoin = PenLineJoin.Round
            };
            var line = new StreamGeometry();
            using (var ctx = line.Open())
            {
                ctx.BeginFigure(points[0], false, false);
                for (var i = 1; i < points.Count; i++) ctx.LineTo(points[i], true, false);
            }
            line.Freeze();
            dc.DrawGeometry(null, pen, line);

            var last = points[^1];
            dc.DrawEllipse(new SolidColorBrush(s.Color), null, last, 3, 3);
        }
    }
}
