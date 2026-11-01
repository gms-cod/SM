using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using StartupMonitor.UI.Services;

namespace StartupMonitor.UI.Controls;

/// <summary>
/// Нативный высокопроизводительный график сетевой активности и событий безопасности.
/// Отрисовка выполняется через аппаратный DrawingContext (MIL Core) без сторонних библиотек.
/// Кэширует геометрию до изменения данных или размера и не использует фоновый таймер отрисовки.
/// </summary>
public sealed class NetworkTimelineChartControl : FrameworkElement
{
    #region Структуры данных

    /// <summary>
    /// Дискретный бакет сетевой телеметрии во времени (трафик, блокировки, алерты).
    /// </summary>
    public readonly struct ChartDataPoint
    {
        /// <summary>Метка времени (UTC).</summary>
        public readonly DateTime Timestamp;

        /// <summary>Входящий трафик (байт в секунду).</summary>
        public readonly double InboundBps;

        /// <summary>Исходящий трафик (байт в секунду).</summary>
        public readonly double OutboundBps;

        /// <summary>Количество заблокированных сетевых попыток в бакете.</summary>
        public readonly int BlockedCount;

        /// <summary>Количество предупреждений и интерактивных алертов в бакете.</summary>
        public readonly int AlertCount;

        /// <summary>Есть ли действительное измерение скорости в этом временном бакете.</summary>
        public readonly bool HasMeasurement;

        /// <summary>Создаёт временной бакет, отличая отсутствие измерения от измеренного нулевого трафика.</summary>
        /// <param name="timestamp">UTC-время бакета.</param>
        /// <param name="inBps">Измеренная входящая скорость.</param>
        /// <param name="outBps">Измеренная исходящая скорость.</param>
        /// <param name="blocked">Число блокировок.</param>
        /// <param name="alerts">Число предупреждений.</param>
        /// <param name="hasMeasurement">Наличие действительного измерения, включая измеренный ноль.</param>
        public ChartDataPoint(DateTime timestamp, double inBps, double outBps, int blocked, int alerts, bool hasMeasurement = true)
        {
            Timestamp = timestamp;
            InboundBps = inBps;
            OutboundBps = outBps;
            BlockedCount = blocked;
            AlertCount = alerts;
            HasMeasurement = hasMeasurement;
        }
    }

    #endregion

    #region Поля и аппаратные кисти

    private readonly List<ChartDataPoint> _points = new(512);

    // Замороженные перья и кисти (DirectX / Media Integration Layer)
    private static readonly Brush BackgroundGridBrush = Freeze(new SolidColorBrush(Color.FromArgb(20, 255, 255, 255)));
    private static readonly Pen GridLinePen = Freeze(new Pen(BackgroundGridBrush, 1.0));

    // Неоновые акценты кибер-гаммы
    private static readonly Color CyanColor = Color.FromRgb(0x00, 0xD2, 0xD3);
    private static readonly Color CrimsonColor = Color.FromRgb(0xEF, 0x44, 0x44);

    private static readonly Pen CyanGlowPen = Freeze(new Pen(new SolidColorBrush(Color.FromArgb(40, 0x00, 0xD2, 0xD3)), 4.0));
    private static readonly Pen CyanLinePen = Freeze(new Pen(new SolidColorBrush(CyanColor), 1.5));

    private static readonly Pen CrimsonGlowPen = Freeze(new Pen(new SolidColorBrush(Color.FromArgb(50, 0xEF, 0x44, 0x44)), 4.0));
    private static readonly Pen CrimsonLinePen = Freeze(new Pen(new SolidColorBrush(CrimsonColor), 1.8));

    private static readonly Pen HairlinePen = Freeze(new Pen(new SolidColorBrush(Color.FromArgb(180, 0x00, 0xD2, 0xD3)), 1.0) { DashStyle = DashStyles.Dash });
    private static readonly Brush TooltipBackgroundBrush = Freeze(new SolidColorBrush(Color.FromArgb(235, 0x12, 0x18, 0x24)));
    private static readonly Pen TooltipBorderPen = Freeze(new Pen(new SolidColorBrush(Color.FromArgb(120, 0x00, 0xD2, 0xD3)), 1.0));
    private static readonly Brush TextWhiteBrush = Freeze(new SolidColorBrush(Colors.White));
    private static readonly Brush MarkerGlowBrush = Freeze(new SolidColorBrush(Color.FromArgb(80, 0x00, 0xD2, 0xD3)));
    private static readonly Brush MarkerBrush = Freeze(new SolidColorBrush(CyanColor));
    private static readonly Brush TrafficFillBrush = Freeze(new LinearGradientBrush(
        Color.FromArgb(55, 0x00, 0xD2, 0xD3), Color.FromArgb(2, 0x00, 0xD2, 0xD3), 90.0));
    private static readonly Pen OutboundPen = Freeze(new Pen(new SolidColorBrush(Color.FromRgb(0x60, 0xA5, 0xFA)), 1.5));
    private static readonly Typeface TooltipTypeface = new(new FontFamily("Segoe UI Variable Text, Segoe UI"),
        FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);

    /// <summary>Замороженный рисунок меняется только при обновлении данных или фактического размера.</summary>
    private DrawingGroup? _cachedDrawing;
    private Size _cachedSize;
    private bool _dataDirty = true;
    private double _cachedMinimum;
    private double _cachedSpan = 1024;
    private bool _hasMeasurements;
    private int _hoveredIndex = -1;
    private int _tooltipIndex = -1;
    private string _tooltipCulture = string.Empty;
    private double _tooltipDpi;
    private FormattedText? _cachedTooltip;

    private Point? _cursorPosition;
    private ChartDataPoint? _hoveredPoint;

    #endregion

    #region Dependency Properties

    /// <summary>
    /// Временной диапазон отображения (1 час, 24 часа, 7 дней).
    /// </summary>
    public static readonly DependencyProperty TimeWindowProperty =
        DependencyProperty.Register(
            nameof(TimeWindow),
            typeof(TimeSpan),
            typeof(NetworkTimelineChartControl),
            new FrameworkPropertyMetadata(TimeSpan.FromHours(1), FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>
    /// Выбранное временное окно отображения графика.
    /// </summary>
    public TimeSpan TimeWindow
    {
        get => (TimeSpan)GetValue(TimeWindowProperty);
        set => SetValue(TimeWindowProperty, value);
    }

    #endregion

    #region События

    /// <summary>
    /// Событие выбора временного среза пользователем (клик по таймлайну для скраббинга).
    /// </summary>
    public event Action<DateTime>? TimeSliceSelected;

    #endregion

    /// <summary>
    /// Инициализирует новый экземпляр контрола графика временной шкалы сетевой телеметрии.
    /// </summary>
    public NetworkTimelineChartControl()
    {
        ClipToBounds = true;
        SnapsToDevicePixels = true;

        MouseMove += OnChartMouseMove;
        MouseLeave += OnChartMouseLeave;
        MouseLeftButtonDown += OnChartMouseLeftButtonDown;
    }

    /// <summary>
    /// Обновляет массив данных сетевой активности с мгновенной инвалидацией визуального представления.
    /// </summary>
    /// <param name="dataPoints">Последовательность точек данных активности.</param>
    public void SetDataPoints(IEnumerable<ChartDataPoint>? dataPoints)
    {
        VerifyAccess();
        _points.Clear();
        if (dataPoints != null)
        {
            foreach (var point in dataPoints)
            {
                if (_points.Count >= 4096) break;
                var valid = point.HasMeasurement && double.IsFinite(point.InboundBps) && double.IsFinite(point.OutboundBps)
                    && point.InboundBps >= 0 && point.OutboundBps >= 0
                    && point.InboundBps <= long.MaxValue && point.OutboundBps <= long.MaxValue;
                _points.Add(new ChartDataPoint(point.Timestamp, valid ? point.InboundBps : 0,
                    valid ? point.OutboundBps : 0, Math.Max(0, point.BlockedCount), Math.Max(0, point.AlertCount), valid));
            }
        }
        _dataDirty = true;
        _hoveredIndex = -1;
        _tooltipIndex = -1;
        _cursorPosition = null;
        _hoveredPoint = null;
        InvalidateVisual();
    }

    /// <summary>
    /// Аппаратно-ускоренная отрисовка графика через DrawingContext.
    /// </summary>
    /// <param name="dc">Контекст рисования WPF.</param>
    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        var width = ActualWidth;
        var height = ActualHeight;
        if (width < 30 || height < 30) return;
        DrawBackgroundGrid(dc, width, height);
        if (_dataDirty || _cachedDrawing == null || _cachedSize != RenderSize)
            RebuildDrawing(width, height);
        if (_cachedDrawing != null)
            dc.DrawDrawing(_cachedDrawing);
        if (!_hasMeasurements)
        {
            var emptyText = new FormattedText(LocalizationService.Instance["TimelineNoMeasurements"],
                CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, TooltipTypeface, 11,
                TextWhiteBrush, VisualTreeHelper.GetDpi(this).PixelsPerDip);
            dc.DrawText(emptyText, new Point(8, Math.Max(0, (height - emptyText.Height) * 0.5)));
        }
        if (_cursorPosition.HasValue && _hoveredPoint is { HasMeasurement: true } point)
            DrawInteractiveHairline(dc, _cursorPosition.Value.X, height, point, ProjectTraffic(point.InboundBps, height));
    }

    /// <summary>Вычисляет конечный диапазон обеих измеренных скоростей для рабочего алгоритма AGC.</summary>
    /// <param name="minimum">Минимальная конечная неотрицательная скорость.</param>
    /// <param name="maximum">Максимальная конечная неотрицательная скорость.</param>
    /// <returns>Нижняя и верхняя границы без вырожденного диапазона.</returns>
    internal static (double Minimum, double Maximum) CalculateTrafficRange(double minimum, double maximum)
    {
        if (!double.IsFinite(minimum) || !double.IsFinite(maximum) || minimum < 0 || maximum < minimum
            || maximum > long.MaxValue)
            return (0, 1024);
        if (maximum < 10) return (0, 1024);
        var range = maximum - minimum;
        if (range >= maximum * 0.40) return (0, maximum * 1.15);
        var midpoint = (maximum + minimum) * 0.5;
        var span = Math.Max(range * 2, maximum * 0.35);
        var lower = Math.Max(0, midpoint - span * 0.5);
        return (lower, lower + span);
    }

    /// <summary>Проецирует проверенную скорость в вертикальную координату с ограничением границ.</summary>
    /// <param name="value">Конечная измеренная скорость.</param>
    /// <param name="height">Доступная высота графика.</param>
    /// <returns>Координата внутри диапазона рисунка.</returns>
    private double ProjectTraffic(double value, double height)
        => height - 10 - Math.Clamp((value - _cachedMinimum) / _cachedSpan, 0, 1) * Math.Max(10, height - 22);

    /// <summary>Создаёт и замораживает рисунок только при изменении данных или размера.</summary>
    /// <param name="width">Ширина графика в DIP.</param>
    /// <param name="height">Высота графика в DIP.</param>
    private void RebuildDrawing(double width, double height)
    {
        var minimum = double.MaxValue;
        var maximum = 0.0;
        var maxBlocked = 1;
        _hasMeasurements = false;
        foreach (var point in _points)
        {
            maxBlocked = Math.Max(maxBlocked, point.BlockedCount);
            if (!point.HasMeasurement) continue;
            _hasMeasurements = true;
            minimum = Math.Min(minimum, Math.Min(point.InboundBps, point.OutboundBps));
            maximum = Math.Max(maximum, Math.Max(point.InboundBps, point.OutboundBps));
        }
        var range = CalculateTrafficRange(minimum, maximum);
        _cachedMinimum = range.Minimum;
        _cachedSpan = Math.Max(1, range.Maximum - range.Minimum);
        var drawing = new DrawingGroup();
        using (var context = drawing.Open())
        {
            if (_points.Count > 0)
            {
                var step = _points.Count > 1 ? width / (_points.Count - 1) : 0;
                var inbound = BuildTrafficGeometry(step, height, outbound: false, fill: false);
                var outbound = BuildTrafficGeometry(step, height, outbound: true, fill: false);
                var fill = BuildTrafficGeometry(step, height, outbound: false, fill: true);
                context.DrawGeometry(TrafficFillBrush, null, fill);
                context.DrawGeometry(null, CyanGlowPen, inbound);
                context.DrawGeometry(null, CyanLinePen, inbound);
                context.DrawGeometry(null, OutboundPen, outbound);
                var blocked = new StreamGeometry();
                using (var geometry = blocked.Open())
                {
                    for (var i = 0; i < _points.Count; i++)
                    {
                        if (_points[i].BlockedCount == 0) continue;
                        var x = i * step;
                        var barHeight = Math.Min(height * 0.75, _points[i].BlockedCount / (double)maxBlocked
                            * (Math.Max(10, height - 22) * 0.85) + 6);
                        geometry.BeginFigure(new Point(x, height - 10), false, false);
                        geometry.LineTo(new Point(x, height - 10 - barHeight), true, false);
                    }
                }
                blocked.Freeze();
                context.DrawGeometry(null, CrimsonGlowPen, blocked);
                context.DrawGeometry(null, CrimsonLinePen, blocked);
            }
        }
        drawing.Freeze();
        _cachedDrawing = drawing;
        _cachedSize = RenderSize;
        _dataDirty = false;
    }

    /// <summary>Строит сглаженную кривую, разрывая её на каждом бакете без измерения.</summary>
    /// <param name="step">Горизонтальный шаг между временными бакетами.</param>
    /// <param name="height">Высота графика.</param>
    /// <param name="outbound">Выбрать исходящую скорость вместо входящей.</param>
    /// <param name="fill">Замкнуть фигуры для заливки под входящей кривой.</param>
    /// <returns>Замороженная геометрия без интерполяции через отсутствующие измерения.</returns>
    private StreamGeometry BuildTrafficGeometry(double step, double height, bool outbound, bool fill)
    {
        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            for (var start = 0; start < _points.Count; start++)
            {
                if (!_points[start].HasMeasurement) continue;
                var end = start;
                while (end + 1 < _points.Count && _points[end + 1].HasMeasurement) end++;
                Point At(int index) => new(index * step, ProjectTraffic(outbound ? _points[index].OutboundBps : _points[index].InboundBps, height));
                var first = At(start);
                context.BeginFigure(fill ? new Point(first.X, height) : first, fill, fill);
                if (fill) context.LineTo(first, true, false);
                if (end == start) context.LineTo(new Point(first.X + 0.5, first.Y), true, false);
                for (var i = start; i < end; i++)
                {
                    var a = At(i);
                    var b = At(i + 1);
                    var previous = i > start ? At(i - 1) : new Point(2 * a.X - b.X, 2 * a.Y - b.Y);
                    var next = i + 1 < end ? At(i + 2) : new Point(2 * b.X - a.X, 2 * b.Y - a.Y);
                    const double tension = 0.40 / 3.0;
                    var controlA = new Point(a.X + (b.X - previous.X) * tension,
                        Math.Clamp(a.Y + (b.Y - previous.Y) * tension, 12, height - 10));
                    var controlB = new Point(b.X - (next.X - a.X) * tension,
                        Math.Clamp(b.Y - (next.Y - a.Y) * tension, 12, height - 10));
                    context.BezierTo(controlA, controlB, b, true, true);
                }
                if (fill) context.LineTo(new Point(end * step, height), true, false);
                start = end;
            }
        }
        geometry.Freeze();
        return geometry;
    }

    /// <summary>
    /// Отрисовывает горизонтальные и вертикальные направляющие линии фоновой сетки.
    /// </summary>
    private static void DrawBackgroundGrid(DrawingContext dc, double width, double height)
    {
        // 4 горизонтальные линии уровня
        double yStep = height / 4.0;
        for (int i = 1; i <= 3; i++)
        {
            double y = Math.Round(i * yStep) + 0.5;
            dc.DrawLine(GridLinePen, new Point(0, y), new Point(width, y));
        }

        // Вертикальные линии временных интервалов
        double xStep = width / 6.0;
        for (int i = 1; i <= 5; i++)
        {
            double x = Math.Round(i * xStep) + 0.5;
            dc.DrawLine(GridLinePen, new Point(x, 0), new Point(x, height));
        }
    }

    /// <summary>
    /// Отрисовывает вертикальную линию сканирования и парящий кибер-тултип со срезом метрик.
    /// </summary>
    private void DrawInteractiveHairline(DrawingContext dc, double x, double height, ChartDataPoint pt, double markerY)
    {
        dc.DrawLine(HairlinePen, new Point(x, 0), new Point(x, height));
        dc.DrawEllipse(MarkerGlowBrush, null, new Point(x, markerY), 7, 7);
        dc.DrawEllipse(MarkerBrush, null, new Point(x, markerY), 3.5, 3.5);
        var culture = CultureInfo.CurrentUICulture.Name;
        var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        if (_cachedTooltip == null || _tooltipIndex != _hoveredIndex || _tooltipCulture != culture || _tooltipDpi != dpi)
        {
            var loc = LocalizationService.Instance;
            var blocked = pt.BlockedCount > 0 ? string.Format(CultureInfo.CurrentUICulture,
                loc["TimelineBlockedFormat"], pt.BlockedCount) : string.Empty;
            var text = string.Format(CultureInfo.CurrentUICulture, loc["TimelineTooltipFormat"],
                pt.Timestamp.ToLocalTime().ToString("HH:mm:ss", CultureInfo.CurrentUICulture),
                FormatBytesPerSec(pt.InboundBps), FormatBytesPerSec(pt.OutboundBps), blocked);
            _cachedTooltip = new FormattedText(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
                TooltipTypeface, 11.5, TextWhiteBrush, dpi);
            _tooltipIndex = _hoveredIndex;
            _tooltipCulture = culture;
            _tooltipDpi = dpi;
        }
        var boxWidth = _cachedTooltip.Width + 18;
        var boxHeight = _cachedTooltip.Height + 10;
        if (height < boxHeight + 16 || ActualWidth < boxWidth + 16) return;
        var boxX = Math.Clamp(x + 10, 8, ActualWidth - boxWidth - 8);
        var boxY = Math.Clamp(markerY - boxHeight - 8, 8, height - boxHeight - 8);
        dc.DrawRoundedRectangle(TooltipBackgroundBrush, TooltipBorderPen,
            new Rect(boxX, boxY, boxWidth, boxHeight), 6, 6);
        dc.DrawText(_cachedTooltip, new Point(boxX + 9, boxY + 5));
    }

    /// <summary>Меняет интерактивный срез только при переходе курсора в другой бакет.</summary>
    /// <param name="sender">Источник события мыши.</param>
    /// <param name="e">Координаты курсора.</param>
    private void OnChartMouseMove(object sender, MouseEventArgs e)
    {
        if (_points.Count < 2 || ActualWidth <= 0) return;
        var position = e.GetPosition(this);
        var step = ActualWidth / (_points.Count - 1);
        var index = Math.Clamp((int)Math.Round(position.X / step), 0, _points.Count - 1);
        if (index == _hoveredIndex) return;
        _hoveredIndex = index;
        _cursorPosition = new Point(index * step, position.Y);
        _hoveredPoint = _points[index];
        InvalidateVisual();
    }

    private void OnChartMouseLeave(object sender, MouseEventArgs e)
    {
        _hoveredIndex = -1;
        _cursorPosition = null;
        _hoveredPoint = null;
        InvalidateVisual();
    }

    private void OnChartMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_hoveredPoint.HasValue)
        {
            TimeSliceSelected?.Invoke(_hoveredPoint.Value.Timestamp);
        }
    }

    /// <summary>
    /// Форматирует скорость передачи данных в понятный вид (Б/с, КБ/с, МБ/с).
    /// </summary>
    private static string FormatBytesPerSec(double bytesPerSec)
    {
        var loc = LocalizationService.Instance;
        if (bytesPerSec < 1024) return $"{bytesPerSec:0} {loc["UnitBytesPerSecond"]}";
        if (bytesPerSec < 1024 * 1024) return $"{bytesPerSec / 1024:F1} {loc["UnitKilobytesPerSecond"]}";
        return $"{bytesPerSec / (1024 * 1024):F2} {loc["UnitMegabytesPerSecond"]}";
    }

    /// <summary>
    /// Замораживает графический ресурс для передачи в подсистему аппаратного ускорения WPF (MIL).
    /// </summary>
    private static T Freeze<T>(T freezable) where T : Freezable
    {
        if (freezable.CanFreeze) freezable.Freeze();
        return freezable;
    }
}

