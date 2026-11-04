using System;
using System.IO;
using StartupMonitor.Shared.Helpers;

namespace StartupMonitor.Shared.Models;

/// <summary>
/// Информация о загруженном в процесс динамическом модуле (DLL).
/// </summary>
public class ProcessModuleInfo
{
    /// <summary>Имя модуля (например, ntdll.dll).</summary>
    public string ModuleName { get; set; } = string.Empty;

    /// <summary>Полный путь к файлу модуля на диске.</summary>
    public string FilePath { get; set; } = string.Empty;

    /// <summary>Базовый адрес загрузки модуля в памяти процесса (шестнадцатеричный, например 0x7FFBC1200000).</summary>
    public string BaseAddress { get; set; } = string.Empty;

    /// <summary>Размер модуля в виртуальной памяти (байт).</summary>
    public long ModuleSize { get; set; }

    /// <summary>Форматированный размер модуля для отображения (КБ, МБ).</summary>
    public string DisplaySize => ModuleSize switch
    {
        >= 1024 * 1024 => (ModuleSize / (1024.0 * 1024.0)).ToString("F2", System.Globalization.CultureInfo.InvariantCulture) + " MB",
        >= 1024 => (ModuleSize / 1024.0).ToString("F1", System.Globalization.CultureInfo.InvariantCulture) + " KB",
        > 0 => $"{ModuleSize} B",
        _ => "—"
    };

    /// <summary>Версия файла из PE-заголовка.</summary>
    public string? FileVersion { get; set; }

    /// <summary>Описание файла из ресурсов версии.</summary>
    public string? FileDescription { get; set; }

    /// <summary>Компания-разработчик из ресурсов версии.</summary>
    public string? CompanyName { get; set; }

    /// <summary>Признак наличия валидной цифровой подписи Authenticode.</summary>
    public bool IsSigned { get; set; }

    /// <summary>Криптографический статус проверки подписи Authenticode.</summary>
    public DigitalSignatureStatus SignatureStatus { get; set; } = DigitalSignatureStatus.Unknown;

    /// <summary>Имя субъекта (Signer CN), подписавшего модуль.</summary>
    public string? SignerName { get; set; }

    /// <summary>Имя удостоверяющего центра (Issuer CN).</summary>
    public string? IssuerName { get; set; }

    /// <summary>Признак расположения файла в подозрительной временной папке (Temp, AppData\Local\Temp, Users\Public и др.).</summary>
    public bool IsTempPath { get; set; }

    /// <summary>Признак системного расположения (System32, SysWOW64, WinSxS).</summary>
    public bool IsSystem { get; set; }
}
