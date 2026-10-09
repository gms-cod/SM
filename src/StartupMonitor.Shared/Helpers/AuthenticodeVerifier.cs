using System;
using System.Collections.Concurrent;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;
using StartupMonitor.Shared.Models;
using StartupMonitor.Shared.Diagnostics;

namespace StartupMonitor.Shared.Helpers;

/// <summary>
/// Результат верификации цифровой подписи Authenticode.
/// </summary>
public record SignatureVerificationResult(
    DigitalSignatureStatus Status,
    bool IsSigned,
    string? SignerName,
    string? IssuerName,
    string? ErrorDetail
);

/// <summary>
/// Высокопроизводительный валидатор цифровых подписей Authenticode (PE-файлов) через WinVerifyTrust API.
/// </summary>
public static class AuthenticodeVerifier
{
    private static readonly Guid WINTRUST_ACTION_GENERIC_VERIFY_V2 =
        new("{00AAC56B-CD44-11d0-8CC2-00C04FC295EE}");

    private const uint WTD_UI_NONE = 2;
    private const uint WTD_REVOKE_NONE = 0; // Быстрая локальная проверка без блокирующих сетевых запросов CRL
    private const uint WTD_CHOICE_FILE = 1;
    private const uint WTD_STATEACTION_IGNORE = 0;
    private const uint WTD_CACHE_ONLY_URL_RETRIEVAL = 0x00001000; // Только локальный кэш CRL/AIA

    // Win32 HRESULT
    private const uint ERROR_SUCCESS = 0;
    private const uint TRUST_E_NOSIGNATURE = 0x800B0100;
    private const uint TRUST_E_BAD_DIGEST = 0x80096010;
    private const uint CERT_E_UNTRUSTEDROOT = 0x800B0109;
    private const uint CERT_E_EXPIRED = 0x800B0101;
    private const uint CERT_E_REVOKED = 0x800B0108;
    private const uint TRUST_E_EXPLICIT_DISTRUST = 0x800B0111;

    // Кэш проверок подписей: (нормализованный путь + LastWriteTimeUtc) -> SignatureVerificationResult
    private static readonly ConcurrentDictionary<string, SignatureVerificationResult> Cache = new(StringComparer.OrdinalIgnoreCase);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WINTRUST_FILE_INFO
    {
        public uint cbStruct;
        [MarshalAs(UnmanagedType.LPWStr)]
        public string pcwszFilePath;
        public IntPtr hFile;
        public IntPtr pgKnownSubject;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WINTRUST_DATA : IDisposable
    {
        public uint cbStruct;
        public IntPtr pPolicyCallbackData;
        public IntPtr pSIPClientData;
        public uint dwUIChoice;
        public uint fdwRevocationChecks;
        public uint dwUnionChoice;
        public IntPtr pFile;
        public uint dwStateAction;
        public IntPtr hWVTStateData;
        public IntPtr pwszURLReference;
        public uint dwProvFlags;
        public uint dwUIContext;
        public IntPtr pSignatureSettings;

        /// <summary>WINTRUST_DATA: Выполняет операцию WINTRUST_DATA.</summary>
        /// <param name="fileInfo">Входные данные: значение параметра fileInfo (WINTRUST_FILE_INFO).</param>
        public WINTRUST_DATA(WINTRUST_FILE_INFO fileInfo)
        {
            cbStruct = (uint)Marshal.SizeOf<WINTRUST_DATA>();
            pPolicyCallbackData = IntPtr.Zero;
            pSIPClientData = IntPtr.Zero;
            dwUIChoice = WTD_UI_NONE;
            fdwRevocationChecks = WTD_REVOKE_NONE;
            dwUnionChoice = WTD_CHOICE_FILE;
            dwStateAction = WTD_STATEACTION_IGNORE;
            hWVTStateData = IntPtr.Zero;
            pwszURLReference = IntPtr.Zero;
            dwProvFlags = WTD_CACHE_ONLY_URL_RETRIEVAL;
            dwUIContext = 0;
            pSignatureSettings = IntPtr.Zero;

            pFile = Marshal.AllocHGlobal(Marshal.SizeOf<WINTRUST_FILE_INFO>());
            Marshal.StructureToPtr(fileInfo, pFile, false);
        }

        /// <summary>Dispose: Освобождает занятые ресурсы объекта.</summary>
        public void Dispose()
        {
            if (pFile != IntPtr.Zero)
            {
                Marshal.DestroyStructure<WINTRUST_FILE_INFO>(pFile);
                Marshal.FreeHGlobal(pFile);
                pFile = IntPtr.Zero;
            }
        }
    }

    [DllImport("wintrust.dll", ExactSpelling = true, SetLastError = false, CharSet = CharSet.Unicode)]
    private static extern uint WinVerifyTrust(
        IntPtr hwnd,
        [In] ref Guid pgActionID,
        ref WINTRUST_DATA pWVTData
    );

    /// <summary>
    /// Проверяет цифровую подпись Authenticode для указанного исполняемого файла с использованием кэша.
    /// </summary>
    public static SignatureVerificationResult VerifyFile(string? filePath)
    {
        using var _profAuth = ExecutionProfiler.Profile("Service.AuthenticodeVerify");
        using var _prof = ExecutionProfiler.Measure("Shared.Authenticode.VerifyFile");
        if (string.IsNullOrWhiteSpace(filePath))
            return new SignatureVerificationResult(DigitalSignatureStatus.Unknown, false, null, null, "Путь не указан");

        string cleanPath = filePath.Trim().Trim('"');
        try
        {
            if (!File.Exists(cleanPath))
                return new SignatureVerificationResult(DigitalSignatureStatus.Unknown, false, null, null, "Файл не найден");

            long lastWriteTicks = File.GetLastWriteTimeUtc(cleanPath).Ticks;
            string cacheKey = $"{cleanPath}|{lastWriteTicks}";

            if (Cache.TryGetValue(cacheKey, out var cached))
                return cached;

            var result = PerformVerification(cleanPath);
            Cache[cacheKey] = result;

            // Ограничиваем размер кэша от разрастания
            if (Cache.Count > 2000)
            {
                Cache.Clear();
                Cache[cacheKey] = result;
            }

            return result;
        }
        catch (Exception ex)
        {
            return new SignatureVerificationResult(DigitalSignatureStatus.Unknown, false, null, null, ex.Message);
        }
    }

    /// <summary>PerformVerification: Выполняет операцию PerformVerification.</summary>
    /// <param name="filePath">Входные данные: значение параметра filePath (string).</param>
    /// <returns>Выходные данные: результирующее значение типа SignatureVerificationResult.</returns>
    private static SignatureVerificationResult PerformVerification(string filePath)
    {
        var fileInfo = new WINTRUST_FILE_INFO
        {
            cbStruct = (uint)Marshal.SizeOf<WINTRUST_FILE_INFO>(),
            pcwszFilePath = filePath,
            hFile = IntPtr.Zero,
            pgKnownSubject = IntPtr.Zero
        };

        var wvtData = new WINTRUST_DATA(fileInfo);
        var actionId = WINTRUST_ACTION_GENERIC_VERIFY_V2;
        uint hr;
        try
        {
            hr = WinVerifyTrust(IntPtr.Zero, ref actionId, ref wvtData);
        }
        finally
        {
            wvtData.Dispose();
        }

        var status = hr switch
        {
            ERROR_SUCCESS => DigitalSignatureStatus.Valid,
            TRUST_E_NOSIGNATURE => DigitalSignatureStatus.NotSigned,
            0x800B0003 => DigitalSignatureStatus.NotSigned, // TRUST_E_SUBJECT_FORM_UNKNOWN
            TRUST_E_BAD_DIGEST => DigitalSignatureStatus.InvalidSignature,
            CERT_E_UNTRUSTEDROOT => DigitalSignatureStatus.UntrustedRoot,
            CERT_E_EXPIRED => DigitalSignatureStatus.Expired,
            CERT_E_REVOKED => DigitalSignatureStatus.Revoked,
            TRUST_E_EXPLICIT_DISTRUST => DigitalSignatureStatus.ExplicitDistrust,
            _ => DigitalSignatureStatus.Unknown
        };

        bool isValid = status == DigitalSignatureStatus.Valid;
        string? signerName = null;
        string? issuerName = null;

        // Если файл имеет подпись (даже истекшую или недоверенную), считываем имя подписанта и УЦ
        if (status != DigitalSignatureStatus.NotSigned)
        {
            try
            {
                using var rawCert = X509Certificate.CreateFromSignedFile(filePath);
                using var cert = new X509Certificate2(rawCert);
                signerName = cert.GetNameInfo(X509NameType.SimpleName, false);
                issuerName = cert.GetNameInfo(X509NameType.SimpleName, true);

                if (string.IsNullOrEmpty(signerName))
                {
                    // Fallback на Subject
                    signerName = cert.Subject;
                }
            }
            catch { }
        }

        return new SignatureVerificationResult(
            status,
            isValid,
            signerName,
            issuerName,
            hr != ERROR_SUCCESS ? $"0x{hr:X8}" : null
        );
    }
}
