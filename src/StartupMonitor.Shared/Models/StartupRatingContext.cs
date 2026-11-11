using System.Security.Cryptography;
using System.Text.Json;

namespace StartupMonitor.Shared.Models;

/// <summary>Строит стабильный отпечаток данных записи, влияющих на оценку ИИ.</summary>
public static class StartupRatingContext
{
    /// <summary>Вычисляет SHA-256 канонического представления контекста без пользовательской оценки и идентификатора.</summary>
    /// <param name="entry">Проверяемая запись автозапуска.</param>
    /// <returns>Версионированный отпечаток для сохранения и сравнения.</returns>
    public static string Compute(StartupEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        var context = new
        {
            entry.Name, entry.FilePath, entry.CommandLine, entry.Location, entry.LocationDetail,
            entry.Status, entry.DelaySeconds, Sha256 = entry.SHA256?.ToUpperInvariant(),
            entry.Publisher, entry.IsSigned, entry.SignatureStatus, entry.SignerName, entry.SignatureIssuer,
            entry.Language, entry.KasperskyZone, entry.KasperskyDetection, entry.KasperskyFileStatus,
            RawData = entry.RawData?.OrderBy(x => x.Key, StringComparer.Ordinal).ToArray(), entry.ThreatInfo
        };
        return "v1:" + Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(context)));
    }
}
