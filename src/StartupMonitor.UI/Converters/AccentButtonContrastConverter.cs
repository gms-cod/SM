using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace StartupMonitor.UI.Converters;

/// <summary>Выбирает читаемый чёрный или белый текст для реальных кистей акцентной кнопки.</summary>
public sealed class AccentButtonContrastConverter : IMultiValueConverter
{
    /// <summary>Выбирает цвет с наибольшим минимальным контрастом для переданных состояний и возможных подложек.</summary>
    /// <param name="values">Кисти обычного состояния, наведения или нажатия.</param>
    /// <param name="targetType">Ожидаемый тип свойства.</param>
    /// <param name="parameter">Не используется.</param>
    /// <param name="culture">Культура привязки.</param>
    /// <returns>Чёрная или белая кисть; UnsetValue для ещё не готового ресурса.</returns>
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values is null || values.Length == 0 || values.Any(value => value is not SolidColorBrush))
            return DependencyProperty.UnsetValue;
        var blackWorst = double.PositiveInfinity;
        var whiteWorst = double.PositiveInfinity;
        foreach (var value in values)
        {
            var brush = (SolidColorBrush)value;
            var alpha = Math.Clamp(brush.Color.A / 255d * brush.Opacity, 0d, 1d);
            // Полупрозрачный акцент может лежать на разных карточках: оцениваем обе крайние подложки.
            foreach (var backdrop in new[] { 0d, 1d })
            {
                var r = brush.Color.R / 255d * alpha + backdrop * (1d - alpha);
                var g = brush.Color.G / 255d * alpha + backdrop * (1d - alpha);
                var b = brush.Color.B / 255d * alpha + backdrop * (1d - alpha);
                var luminance = 0.2126 * Linear(r) + 0.7152 * Linear(g) + 0.0722 * Linear(b);
                blackWorst = Math.Min(blackWorst, (luminance + 0.05) / 0.05);
                whiteWorst = Math.Min(whiteWorst, 1.05 / (luminance + 0.05));
            }
        }
        return blackWorst >= whiteWorst ? Brushes.Black : Brushes.White;
    }

    /// <summary>Переводит компонент sRGB в линейное значение яркости.</summary>
    /// <param name="value">Компонент цвета от нуля до единицы.</param>
    /// <returns>Линейный компонент яркости.</returns>
    private static double Linear(double value) => value <= 0.04045 ? value / 12.92 : Math.Pow((value + 0.055) / 1.055, 2.4);

    /// <summary>Обратное преобразование недоступно: цвет текста не определяет цвет фона.</summary>
    /// <param name="value">Цвет текста.</param>
    /// <param name="targetTypes">Типы исходных свойств.</param>
    /// <param name="parameter">Не используется.</param>
    /// <param name="culture">Культура привязки.</param>
    /// <returns>Метод всегда сообщает о неподдерживаемой операции.</returns>
    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) => throw new NotSupportedException();
}
