using System;
using System.Windows;
using System.Windows.Media;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;
using StartupMonitor.Shared.Diagnostics;

namespace StartupMonitor.UI.Services;

/// <summary>
/// Сервис управления темой оформления (Light / Dark) и адаптивными ресурсами палитры.
/// Обеспечивает безупречный вид темной темы (пользовательский эталон) и светлой темы.
/// </summary>
public static class ThemeService
{
    /// <summary>Текущая активная тема оформления.</summary>
    public static ApplicationTheme CurrentTheme { get; private set; } = ApplicationTheme.Dark;

    /// <summary>Флаг первого применения темы приложения.</summary>
    private static bool _isApplied;

    /// <summary>Применяет выбранную тему (Light или Dark) и обновляет все кисти в ресурсах приложения.</summary>
    public static void ApplyTheme(ApplicationTheme theme, bool force = false)
    {
        using var _prof = ExecutionProfiler.Measure("UI.Theme.ApplyTheme");
        if (_isApplied && !force && CurrentTheme == theme) return;

        CurrentTheme = theme;
        _isApplied = true;
        ApplicationThemeManager.Apply(theme, WindowBackdropType.Mica);

        var resources = Application.Current?.Resources;
        if (resources == null) return;

        if (theme == ApplicationTheme.Light)
        {
            ApplyLightTheme(resources);
        }
        else
        {
            ApplyDarkTheme(resources);
        }
    }

    /// <summary>Палитра для тёмной темы (сохраняет эталонное оформление, одобренное пользователем).</summary>
    public static void ApplyDarkTheme(ResourceDictionary res)
    {
        res["WindowBackgroundBrush"] = CreateSolidBrush(0xD0, 0x0E, 0x14, 0x1E);
        res["SidebarBackgroundBrush"] = CreateSolidBrush(0x80, 0x0D, 0x13, 0x1C);
        res["ContentBackgroundBrush"] = CreateSolidBrush(0x80, 0x0D, 0x13, 0x1C); // Совпадает по цвету с меню
        res["SidebarBorderBrush"] = CreateSolidBrush(0x28, 0xFF, 0xFF, 0xFF);

        res["GlassCardBackgroundBrush"] = CreateLinearBrush(
            Color.FromArgb(0x24, 0xFF, 0xFF, 0xFF),
            Color.FromArgb(0x0E, 0xFF, 0xFF, 0xFF));
        res["GlassCardBorderBrush"] = CreateSolidBrush(0x40, 0xFF, 0xFF, 0xFF);

        res["TableContainerBackgroundBrush"] = CreateLinearBrush(
            Color.FromArgb(0x1C, 0xFF, 0xFF, 0xFF),
            Color.FromArgb(0x0A, 0xFF, 0xFF, 0xFF));
        res["TableContainerBorderBrush"] = CreateSolidBrush(0x38, 0xFF, 0xFF, 0xFF);
        res["TableRowBorderBrush"] = CreateSolidBrush(0x20, 0xFF, 0xFF, 0xFF);
        res["TableRowHoverBrush"] = CreateSolidBrush(0x32, 0xFF, 0xFF, 0xFF);

        res["ActionMoreBackgroundBrush"] = CreateSolidBrush(0x18, 0xFF, 0xFF, 0xFF);
        res["ActionMoreBorderBrush"] = CreateSolidBrush(0x3A, 0xFF, 0xFF, 0xFF);
        res["ActionMoreTextBrush"] = CreateSolidBrush(0xFF, 0xF1, 0xF5, 0xF9);
        res["ActionMoreHoverBrush"] = CreateSolidBrush(0x2E, 0xFF, 0xFF, 0xFF);

        res["ActionBlockBackgroundBrush"] = CreateSolidBrush(0x2E, 0xEF, 0x44, 0x44);
        res["ActionBlockBorderBrush"] = CreateSolidBrush(0x66, 0xEF, 0x44, 0x44);
        res["ActionBlockTextBrush"] = CreateSolidBrush(0xFF, 0xF8, 0x71, 0x71);
        res["ActionBlockHoverBrush"] = CreateSolidBrush(0x48, 0xEF, 0x44, 0x44);

        res["FilterPillInactiveBackgroundBrush"] = CreateSolidBrush(0x14, 0xFF, 0xFF, 0xFF);
        res["FilterPillInactiveBorderBrush"] = CreateSolidBrush(0x28, 0xFF, 0xFF, 0xFF);
        res["FilterPillInactiveTextBrush"] = CreateSolidBrush(0xFF, 0xCB, 0xD5, 0xE1);
        res["FilterPillInactiveHoverBrush"] = CreateSolidBrush(0x24, 0xFF, 0xFF, 0xFF);

        // Неактивные кнопки фильтров (контрастный графит и светлый текст)
        res["FilterButtonInactiveBackgroundBrush"] = CreateSolidBrush(0x18, 0xFF, 0xFF, 0xFF);
        res["FilterButtonInactiveBorderBrush"] = CreateSolidBrush(0x30, 0xFF, 0xFF, 0xFF);
        res["FilterButtonInactiveForegroundBrush"] = CreateSolidBrush(0xFF, 0xCB, 0xD5, 0xE1);
        res["FilterButtonInactiveHoverBrush"] = CreateSolidBrush(0x2C, 0xFF, 0xFF, 0xFF);
        res["FilterButtonInactiveHoverBorderBrush"] = CreateSolidBrush(0x48, 0xFF, 0xFF, 0xFF);

        res["BadgeBlueTextBrush"] = CreateSolidBrush(0xFF, 0x60, 0xA5, 0xFA);
        res["BadgeGreenTextBrush"] = CreateSolidBrush(0xFF, 0x34, 0xD3, 0x99);
        res["BadgeRedTextBrush"] = CreateSolidBrush(0xFF, 0xF8, 0x71, 0x71);
        res["BadgeOrangeTextBrush"] = CreateSolidBrush(0xFF, 0xFB, 0xBF, 0x24);

        res["MutedTextBrush"] = CreateSolidBrush(0xFF, 0xCB, 0xD5, 0xE1);
        res["SubtleTextBrush"] = CreateSolidBrush(0xFF, 0x94, 0xA3, 0xB8);

        res["AuroraGlowBottomLeftBrush"] = CreateRadialBrush(
            0.12, 0.88,
            Color.FromArgb(0x4D, 0x00, 0xD2, 0xD3),
            Color.FromArgb(0x2A, 0x00, 0x78, 0xD4),
            Color.FromArgb(0x10, 0x00, 0x25, 0x45));
        res["AuroraGlowTopRightBrush"] = CreateRadialBrush(
            0.88, 0.12,
            Color.FromArgb(0x3D, 0x00, 0x78, 0xD4),
            Color.FromArgb(0x20, 0x00, 0xD2, 0xD3),
            Color.FromArgb(0x0A, 0x00, 0x20, 0x40));
        res["AuroraGlowCenterBrush"] = CreateRadialBrush(
            0.45, 0.25,
            Color.FromArgb(0x18, 0x00, 0x45, 0x80),
            Color.FromArgb(0x0A, 0x00, 0x20, 0x40),
            Color.FromArgb(0x00, 0x00, 0x00, 0x00));
    }

    /// <summary>Палитра для светлой темы (чистота, контрастность, читаемость текста и карточек).</summary>
    public static void ApplyLightTheme(ResourceDictionary res)
    {
        res["WindowBackgroundBrush"] = CreateSolidBrush(0xD8, 0xF3, 0xF6, 0xFA);
        // Боковая панель полупрозрачная (эффекты Aurora Glow переходят и на нее, убирая глухой белый цвет)
        res["SidebarBackgroundBrush"] = CreateLinearBrush(
            Color.FromArgb(0x55, 0xFF, 0xFF, 0xFF),
            Color.FromArgb(0x35, 0xF0, 0xF4, 0xF8));
        res["ContentBackgroundBrush"] = CreateSolidBrush(0x00, 0xFF, 0xFF, 0xFF);
        res["SidebarBorderBrush"] = CreateSolidBrush(0x80, 0xCB, 0xD5, 0xE1);

        // Карточки белые, чистые, с контрастной рамкой
        res["GlassCardBackgroundBrush"] = CreateSolidBrush(0xFF, 0xFF, 0xFF, 0xFF);
        res["GlassCardBorderBrush"] = CreateSolidBrush(0xFF, 0xCB, 0xD5, 0xE1);

        // Таблица белая, чистая с четкими разделителями
        res["TableContainerBackgroundBrush"] = CreateSolidBrush(0xFF, 0xFF, 0xFF, 0xFF);
        res["TableContainerBorderBrush"] = CreateSolidBrush(0xFF, 0xCB, 0xD5, 0xE1);
        res["TableRowBorderBrush"] = CreateSolidBrush(0xFF, 0xE2, 0xE8, 0xF0);
        res["TableRowHoverBrush"] = CreateSolidBrush(0xFF, 0xEA, 0xEE, 0xF5);

        // Кнопка "Подробнее": контрастный темный текст #1E293B, мягкий серый фон #F1F5F9, рамка #CBD5E1
        res["ActionMoreBackgroundBrush"] = CreateSolidBrush(0xFF, 0xF1, 0xF5, 0xF9);
        res["ActionMoreBorderBrush"] = CreateSolidBrush(0xFF, 0xCB, 0xD5, 0xE1);
        res["ActionMoreTextBrush"] = CreateSolidBrush(0xFF, 0x1E, 0x29, 0x3B);
        res["ActionMoreHoverBrush"] = CreateSolidBrush(0xFF, 0xE2, 0xE8, 0xF0);

        // Кнопка "Блок": красный текст #DC2626, мягкий розовый фон #FEE2E2, рамка #FCA5A5
        res["ActionBlockBackgroundBrush"] = CreateSolidBrush(0xFF, 0xFE, 0xE2, 0xE2);
        res["ActionBlockBorderBrush"] = CreateSolidBrush(0xFF, 0xFC, 0xA5, 0xA5);
        res["ActionBlockTextBrush"] = CreateSolidBrush(0xFF, 0xDC, 0x26, 0x26);
        res["ActionBlockHoverBrush"] = CreateSolidBrush(0xFF, 0xFE, 0xCA, 0xCA);

        res["MutedTextBrush"] = CreateSolidBrush(0xFF, 0x47, 0x55, 0x69);
        res["SubtleTextBrush"] = CreateSolidBrush(0xFF, 0x64, 0x74, 0x8B);

        // Неактивные кнопки фильтров: слабый пастельный голубой цвет, нежная голубая рамка
        res["FilterButtonInactiveBackgroundBrush"] = CreateSolidBrush(0xFF, 0xEB, 0xF3, 0xFB); // нежный голубой #EBF3FB
        res["FilterButtonInactiveBorderBrush"] = CreateSolidBrush(0xFF, 0xBA, 0xE0, 0xFD);     // контур #BAE0FD
        res["FilterButtonInactiveForegroundBrush"] = CreateSolidBrush(0xFF, 0x02, 0x6A, 0xA7); // контрастный сине-голубой
        res["FilterButtonInactiveHoverBrush"] = CreateSolidBrush(0xFF, 0xD8, 0xEC, 0xFA);      // при наведении
        res["FilterButtonInactiveHoverBorderBrush"] = CreateSolidBrush(0xFF, 0x90, 0xCD, 0xF4);

        // Устаревшие ключи для совместимости
        res["FilterPillInactiveBackgroundBrush"] = res["FilterButtonInactiveBackgroundBrush"];
        res["FilterPillInactiveBorderBrush"] = res["FilterButtonInactiveBorderBrush"];
        res["FilterPillInactiveTextBrush"] = res["FilterButtonInactiveForegroundBrush"];
        res["FilterPillInactiveHoverBrush"] = res["FilterButtonInactiveHoverBrush"];

        // Бейджи с повышенным контрастом для белого фона
        res["BadgeBlueTextBrush"] = CreateSolidBrush(0xFF, 0x02, 0x6A, 0xA7);
        res["BadgeGreenTextBrush"] = CreateSolidBrush(0xFF, 0x05, 0x96, 0x69);
        res["BadgeRedTextBrush"] = CreateSolidBrush(0xFF, 0xDC, 0x26, 0x26);
        res["BadgeOrangeTextBrush"] = CreateSolidBrush(0xFF, 0xD9, 0x77, 0x06);

        // Крупные, сочные, выразительные переливы Aurora Glow на светлом фоне
        res["AuroraGlowBottomLeftBrush"] = CreateRadialBrush(
            0.15, 0.85,
            Color.FromArgb(0x65, 0x00, 0xD2, 0xD3),  // сочный бирюзовый (альфа ~40%)
            Color.FromArgb(0x45, 0x00, 0x86, 0xF0),  // плавный переход в лазурно-голубой
            Color.FromArgb(0x18, 0x38, 0x9B, 0xFF)); // нежное рассеянное свечение

        res["AuroraGlowTopRightBrush"] = CreateRadialBrush(
            0.85, 0.15,
            Color.FromArgb(0x55, 0x00, 0x78, 0xD4),  // насыщенный Windows Blue (альфа ~34%)
            Color.FromArgb(0x38, 0x00, 0xD2, 0xD3),  // бирюза
            Color.FromArgb(0x15, 0x00, 0xA8, 0xFF)); // лазурный

        res["AuroraGlowCenterBrush"] = CreateRadialBrush(
            0.45, 0.25,
            Color.FromArgb(0x35, 0x38, 0x9B, 0xFF),  // лазурный акцент по центру/сверху
            Color.FromArgb(0x18, 0x00, 0xD2, 0xD3),
            Color.FromArgb(0x00, 0xF4, 0xF6, 0xFA));
    }

    /// <summary>CreateSolidBrush: Выполняет операцию CreateSolidBrush.</summary>
    /// <param name="a">Входные данные: значение параметра a (byte).</param>
    /// <param name="r">Входные данные: значение параметра r (byte).</param>
    /// <param name="g">Входные данные: значение параметра g (byte).</param>
    /// <param name="b">Входные данные: значение параметра b (byte).</param>
    /// <returns>Выходные данные: результирующее значение типа SolidColorBrush.</returns>
    private static SolidColorBrush CreateSolidBrush(byte a, byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromArgb(a, r, g, b));
        brush.Freeze();
        return brush;
    }

    /// <summary>CreateLinearBrush: Выполняет операцию CreateLinearBrush.</summary>
    /// <param name="c1">Входные данные: значение параметра c1 (Color).</param>
    /// <param name="c2">Входные данные: значение параметра c2 (Color).</param>
    /// <returns>Выходные данные: результирующее значение типа LinearGradientBrush.</returns>
    private static LinearGradientBrush CreateLinearBrush(Color c1, Color c2)
    {
        var brush = new LinearGradientBrush(c1, c2, new Point(0, 0), new Point(0, 1));
        brush.Freeze();
        return brush;
    }

    /// <summary>CreateRadialBrush: Выполняет операцию CreateRadialBrush.</summary>
    /// <param name="cx">Входные данные: значение параметра cx (double).</param>
    /// <param name="cy">Входные данные: значение параметра cy (double).</param>
    /// <param name="c1">Входные данные: значение параметра c1 (Color).</param>
    /// <param name="c2">Входные данные: значение параметра c2 (Color).</param>
    /// <param name="c3">Входные данные: значение параметра c3 (Color).</param>
    /// <returns>Выходные данные: результирующее значение типа RadialGradientBrush.</returns>
    private static RadialGradientBrush CreateRadialBrush(double cx, double cy, Color c1, Color c2, Color c3)
    {
        var brush = new RadialGradientBrush
        {
            Center = new Point(cx, cy),
            RadiusX = 0.75,
            RadiusY = 0.75,
            GradientStops = new GradientStopCollection
            {
                /// <summary>GradientStop: Выполняет операцию GradientStop.</summary>
                /// <param name="c1">Входные данные: значение параметра c1 ().</param>
                /// <param name="0.0">Входные данные: значение параметра 0.0 ().</param>
                new GradientStop(c1, 0.0),
                /// <summary>GradientStop: Выполняет операцию GradientStop.</summary>
                /// <param name="c2">Входные данные: значение параметра c2 ().</param>
                /// <param name="0.45">Входные данные: значение параметра 0.45 ().</param>
                new GradientStop(c2, 0.45),
                /// <summary>GradientStop: Выполняет операцию GradientStop.</summary>
                /// <param name="c3">Входные данные: значение параметра c3 ().</param>
                /// <param name="0.75">Входные данные: значение параметра 0.75 ().</param>
                new GradientStop(c3, 0.75),
                /// <summary>GradientStop: Выполняет операцию GradientStop.</summary>
                /// <param name="0)">Входные данные: значение параметра 0) (Color.FromArgb(0, 0, 0,).</param>
                /// <param name="1.0">Входные данные: значение параметра 1.0 ().</param>
                new GradientStop(Color.FromArgb(0, 0, 0, 0), 1.0),
            }
        };
        brush.Freeze();
        return brush;
    }
}
