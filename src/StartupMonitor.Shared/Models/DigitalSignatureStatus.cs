namespace StartupMonitor.Shared.Models;

/// <summary>
/// Статус криптографической валидации цифровой подписи Authenticode PE-файла.
/// </summary>
public enum DigitalSignatureStatus
{
    /// <summary>Проверка подписи ещё не выполнялась.</summary>
    Unknown = 0,

    /// <summary>Подпись верна, хеш файла совпадает, корневой сертификат доверен ОС Windows (ERROR_SUCCESS).</summary>
    Valid = 1,

    /// <summary>Файл не имеет встроенной цифровой подписи (TRUST_E_NOSIGNATURE).</summary>
    NotSigned = 2,

    /// <summary>Критическая угроза: хеш файла не совпадает с подписью, файл модифицирован/повреждён (TRUST_E_BAD_DIGEST).</summary>
    InvalidSignature = 3,

    /// <summary>Недоверенный корневой центр: самоподписанный сертификат или недоверенный CA (CERT_E_UNTRUSTEDROOT).</summary>
    UntrustedRoot = 4,

    /// <summary>Срок действия сертификата истёк и отсутствует штамп времени (CERT_E_EXPIRED).</summary>
    Expired = 5,

    /// <summary>Сертификат отозван издателем (CERT_E_REVOKED).</summary>
    Revoked = 6,

    /// <summary>Сертификат явно заблокирован администратором или Windows (TRUST_E_EXPLICIT_DISTRUST).</summary>
    ExplicitDistrust = 7
}

