using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using Wpf.Ui.Controls;
using StartupMonitor.Shared.Models;

namespace StartupMonitor.UI.Converters;

/// <summary>
/// Конвертирует DigitalSignatureStatus в символ иконки Fluent UI SymbolRegular.
/// </summary>
public class SignatureStatusToSymbolConverter : IValueConverter
{
    /// <summary>Convert: Выполняет преобразование данных .</summary>
    /// <param name="value">Входные данные: значение параметра value (object).</param>
    /// <param name="targetType">Входные данные: значение параметра targetType (Type).</param>
    /// <param name="parameter">Входные данные: значение параметра parameter (object).</param>
    /// <param name="culture">Входные данные: значение параметра culture (CultureInfo).</param>
    /// <returns>Выходные данные: результирующее значение типа object.</returns>
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not DigitalSignatureStatus status)
            return SymbolRegular.Shield20;

        return status switch
        {
            DigitalSignatureStatus.Valid => SymbolRegular.ShieldCheckmark20,
            DigitalSignatureStatus.InvalidSignature or DigitalSignatureStatus.ExplicitDistrust or DigitalSignatureStatus.Revoked => SymbolRegular.ShieldDismiss20,
            DigitalSignatureStatus.UntrustedRoot or DigitalSignatureStatus.Expired => SymbolRegular.Warning20,
            _ => SymbolRegular.Shield20
        };
    }

    /// <summary>ConvertBack: Выполняет преобразование данных Back.</summary>
    /// <param name="value">Входные данные: значение параметра value (object).</param>
    /// <param name="targetType">Входные данные: значение параметра targetType (Type).</param>
    /// <param name="parameter">Входные данные: значение параметра parameter (object).</param>
    /// <param name="culture)">Входные данные: значение параметра culture) (CultureInfo).</param>
    /// <returns>Выходные данные: результирующее значение типа object.</returns>
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>
/// Конвертирует DigitalSignatureStatus в цвет текста/иконки.
/// </summary>
public class SignatureStatusToColorConverter : IValueConverter
{
    private static readonly SolidColorBrush GreenBrush = new((Color)ColorConverter.ConvertFromString("#10B981"));
    private static readonly SolidColorBrush RedBrush = new((Color)ColorConverter.ConvertFromString("#EF4444"));
    private static readonly SolidColorBrush AmberBrush = new((Color)ColorConverter.ConvertFromString("#F59E0B"));
    private static readonly SolidColorBrush MutedBrush = new((Color)ColorConverter.ConvertFromString("#94A3B8"));

    /// <summary>SignatureStatusToColorConverter: Выполняет операцию SignatureStatusToColorConverter.</summary>
    static SignatureStatusToColorConverter()
    {
        GreenBrush.Freeze();
        RedBrush.Freeze();
        AmberBrush.Freeze();
        MutedBrush.Freeze();
    }

    /// <summary>Convert: Выполняет преобразование данных .</summary>
    /// <param name="value">Входные данные: значение параметра value (object).</param>
    /// <param name="targetType">Входные данные: значение параметра targetType (Type).</param>
    /// <param name="parameter">Входные данные: значение параметра parameter (object).</param>
    /// <param name="culture">Входные данные: значение параметра culture (CultureInfo).</param>
    /// <returns>Выходные данные: результирующее значение типа object.</returns>
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not DigitalSignatureStatus status)
            return MutedBrush;

        return status switch
        {
            DigitalSignatureStatus.Valid => GreenBrush,
            DigitalSignatureStatus.InvalidSignature or DigitalSignatureStatus.ExplicitDistrust or DigitalSignatureStatus.Revoked => RedBrush,
            DigitalSignatureStatus.UntrustedRoot or DigitalSignatureStatus.Expired => AmberBrush,
            _ => MutedBrush
        };
    }

    /// <summary>ConvertBack: Выполняет преобразование данных Back.</summary>
    /// <param name="value">Входные данные: значение параметра value (object).</param>
    /// <param name="targetType">Входные данные: значение параметра targetType (Type).</param>
    /// <param name="parameter">Входные данные: значение параметра parameter (object).</param>
    /// <param name="culture)">Входные данные: значение параметра culture) (CultureInfo).</param>
    /// <returns>Выходные данные: результирующее значение типа object.</returns>
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>
/// Конвертирует DigitalSignatureStatus в полупрозрачный фон бейджа.
/// </summary>
public class SignatureStatusToBackgroundConverter : IValueConverter
{
    private static readonly SolidColorBrush GreenBg = new((Color)ColorConverter.ConvertFromString("#1810B981"));
    private static readonly SolidColorBrush RedBg = new((Color)ColorConverter.ConvertFromString("#18EF4444"));
    private static readonly SolidColorBrush AmberBg = new((Color)ColorConverter.ConvertFromString("#18F59E0B"));
    private static readonly SolidColorBrush MutedBg = new((Color)ColorConverter.ConvertFromString("#1064748B"));

    /// <summary>SignatureStatusToBackgroundConverter: Выполняет операцию SignatureStatusToBackgroundConverter.</summary>
    static SignatureStatusToBackgroundConverter()
    {
        GreenBg.Freeze();
        RedBg.Freeze();
        AmberBg.Freeze();
        MutedBg.Freeze();
    }

    /// <summary>Convert: Выполняет преобразование данных .</summary>
    /// <param name="value">Входные данные: значение параметра value (object).</param>
    /// <param name="targetType">Входные данные: значение параметра targetType (Type).</param>
    /// <param name="parameter">Входные данные: значение параметра parameter (object).</param>
    /// <param name="culture">Входные данные: значение параметра culture (CultureInfo).</param>
    /// <returns>Выходные данные: результирующее значение типа object.</returns>
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not DigitalSignatureStatus status)
            return MutedBg;

        return status switch
        {
            DigitalSignatureStatus.Valid => GreenBg,
            DigitalSignatureStatus.InvalidSignature or DigitalSignatureStatus.ExplicitDistrust or DigitalSignatureStatus.Revoked => RedBg,
            DigitalSignatureStatus.UntrustedRoot or DigitalSignatureStatus.Expired => AmberBg,
            _ => MutedBg
        };
    }

    /// <summary>ConvertBack: Выполняет преобразование данных Back.</summary>
    /// <param name="value">Входные данные: значение параметра value (object).</param>
    /// <param name="targetType">Входные данные: значение параметра targetType (Type).</param>
    /// <param name="parameter">Входные данные: значение параметра parameter (object).</param>
    /// <param name="culture)">Входные данные: значение параметра culture) (CultureInfo).</param>
    /// <returns>Выходные данные: результирующее значение типа object.</returns>
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>
/// Конвертирует DigitalSignatureStatus в цвет границы бейджа.
/// </summary>
public class SignatureStatusToBorderConverter : IValueConverter
{
    private static readonly SolidColorBrush GreenBorder = new((Color)ColorConverter.ConvertFromString("#3310B981"));
    private static readonly SolidColorBrush RedBorder = new((Color)ColorConverter.ConvertFromString("#33EF4444"));
    private static readonly SolidColorBrush AmberBorder = new((Color)ColorConverter.ConvertFromString("#33F59E0B"));
    private static readonly SolidColorBrush MutedBorder = new((Color)ColorConverter.ConvertFromString("#2064748B"));

    /// <summary>SignatureStatusToBorderConverter: Выполняет операцию SignatureStatusToBorderConverter.</summary>
    static SignatureStatusToBorderConverter()
    {
        GreenBorder.Freeze();
        RedBorder.Freeze();
        AmberBorder.Freeze();
        MutedBorder.Freeze();
    }

    /// <summary>Convert: Выполняет преобразование данных .</summary>
    /// <param name="value">Входные данные: значение параметра value (object).</param>
    /// <param name="targetType">Входные данные: значение параметра targetType (Type).</param>
    /// <param name="parameter">Входные данные: значение параметра parameter (object).</param>
    /// <param name="culture">Входные данные: значение параметра culture (CultureInfo).</param>
    /// <returns>Выходные данные: результирующее значение типа object.</returns>
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not DigitalSignatureStatus status)
            return MutedBorder;

        return status switch
        {
            DigitalSignatureStatus.Valid => GreenBorder,
            DigitalSignatureStatus.InvalidSignature or DigitalSignatureStatus.ExplicitDistrust or DigitalSignatureStatus.Revoked => RedBorder,
            DigitalSignatureStatus.UntrustedRoot or DigitalSignatureStatus.Expired => AmberBorder,
            _ => MutedBorder
        };
    }

    /// <summary>ConvertBack: Выполняет преобразование данных Back.</summary>
    /// <param name="value">Входные данные: значение параметра value (object).</param>
    /// <param name="targetType">Входные данные: значение параметра targetType (Type).</param>
    /// <param name="parameter">Входные данные: значение параметра parameter (object).</param>
    /// <param name="culture)">Входные данные: значение параметра culture) (CultureInfo).</param>
    /// <returns>Выходные данные: результирующее значение типа object.</returns>
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>
/// Формирует подробную подсказку (тултип) о цифровой подписи Authenticode.
/// </summary>
public class SignatureStatusToTooltipConverter : IValueConverter
{
    /// <summary>Convert: Выполняет преобразование данных .</summary>
    /// <param name="value">Входные данные: значение параметра value (object).</param>
    /// <param name="targetType">Входные данные: значение параметра targetType (Type).</param>
    /// <param name="parameter">Входные данные: значение параметра parameter (object).</param>
    /// <param name="culture">Входные данные: значение параметра culture (CultureInfo).</param>
    /// <returns>Выходные данные: результирующее значение типа object.</returns>
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is StartupEntry entry)
        {
            return FormatTooltip(entry.SignatureStatus, entry.SignerName, entry.SignatureIssuer, entry.Publisher);
        }
        if (value is ProcessInfo proc)
        {
            return FormatTooltip(proc.SignatureStatus, proc.SignerName, proc.SignatureIssuer, proc.Publisher);
        }
        if (value is DigitalSignatureStatus status)
        {
            return FormatTooltip(status, null, null, null);
        }
        return "Статус цифровой подписи не определён";
    }

    /// <summary>FormatTooltip: Выполняет операцию FormatTooltip.</summary>
    /// <param name="status">Входные данные: значение параметра status (DigitalSignatureStatus).</param>
    /// <param name="signer">Входные данные: значение параметра signer (string?).</param>
    /// <param name="issuer">Входные данные: значение параметра issuer (string?).</param>
    /// <param name="fallback">Входные данные: значение параметра fallback (string?).</param>
    /// <returns>Выходные данные: результирующее значение типа string.</returns>
    private static string FormatTooltip(DigitalSignatureStatus status, string? signer, string? issuer, string? fallback)
    {
        var name = !string.IsNullOrEmpty(signer) ? signer : (!string.IsNullOrEmpty(fallback) ? fallback : "Не указан");
        return status switch
        {
            DigitalSignatureStatus.Valid =>
                $"✔ Подлинная цифровая подпись Authenticode\nПодписант: {name}" +
                (!string.IsNullOrEmpty(issuer) ? $"\nЦентр сертификации (CA): {issuer}" : "") +
                "\nЦепочка сертификатов проверена и доверена Windows.",

            DigitalSignatureStatus.InvalidSignature =>
                $"⚠ КРИТИЧЕСКАЯ УГРОЗА: Подпись повреждена или нарушена!\nФайл был модифицирован после подписания (Tampered binary).",

            DigitalSignatureStatus.UntrustedRoot =>
                $"⚠ Недоверенный корневой центр сертификации\nПодписант: {name}" +
                (!string.IsNullOrEmpty(issuer) ? $"\nУЦ: {issuer}" : "") +
                "\nСертификат является самоподписанным или выдан недоверенным CA.",

            DigitalSignatureStatus.Expired =>
                $"⏳ Истёк срок действия сертификата\nПодписант: {name}" +
                "\nШтамп времени отсутствует или просрочен.",

            DigitalSignatureStatus.Revoked =>
                $"⛔ Сертификат отозван издателем (Revoked)\nПодписант: {name}\nИспользование сертификата аннулировано.",

            DigitalSignatureStatus.ExplicitDistrust =>
                $"⛔ Сертификат явно заблокирован администратором или Windows (Disallowed).",

            DigitalSignatureStatus.NotSigned =>
                $"⚪ Файл не имеет цифровой подписи Authenticode" +
                (!string.IsNullOrEmpty(fallback) ? $"\n(В PE-ресурсах указано: {fallback})" : ""),

            _ => "Проверка подписи не проводилась"
        };
    }

    /// <summary>ConvertBack: Выполняет преобразование данных Back.</summary>
    /// <param name="value">Входные данные: значение параметра value (object).</param>
    /// <param name="targetType">Входные данные: значение параметра targetType (Type).</param>
    /// <param name="parameter">Входные данные: значение параметра parameter (object).</param>
    /// <param name="culture)">Входные данные: значение параметра culture) (CultureInfo).</param>
    /// <returns>Выходные данные: результирующее значение типа object.</returns>
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}
