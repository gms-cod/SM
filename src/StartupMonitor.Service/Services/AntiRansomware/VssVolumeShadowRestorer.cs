using System.IO;
using System.Management;

namespace StartupMonitor.Service.Services.AntiRansomware;

/// <summary>
/// Сведения о теневой копии тома Windows (Volume Shadow Copy).
/// </summary>
/// <param name="Id">Уникальный идентификатор теневой копии (GUID).</param>
/// <param name="DeviceObject">Специальный путь устройства (например, \\?\GLOBALROOT\Device\HarddiskVolumeShadowCopy1).</param>
/// <param name="VolumeName">Имя исходного тома (например, \\?\Volume{...}\ или C:\).</param>
/// <param name="CreationTimeUtc">Время создания снимка в формате UTC.</param>
public record VssShadowCopyMetadata(
    string Id,
    string DeviceObject,
    string VolumeName,
    DateTime? CreationTimeUtc);

/// <summary>
/// Служба поиска теневых копий томов через WMI (Win32_ShadowCopy) и пофайлового отката повреждённых файлов.
/// Обеспечивает прямое чтение оригиналов через глобальный путь \\?\GLOBALROOT\Device\HarddiskVolumeShadowCopy{N}\.
/// </summary>
public static class VssVolumeShadowRestorer
{
    /// <summary>
    /// Получает список всех зарегистрированных в системе теневых копий томов.
    /// </summary>
    /// <returns>Список метаданных доступных теневых копий.</returns>
    public static IReadOnlyList<VssShadowCopyMetadata> GetAvailableShadowCopies()
    {
        var list = new List<VssShadowCopyMetadata>();

        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT ID, DeviceObject, VolumeName, InstallDate FROM Win32_ShadowCopy");
            using ManagementObjectCollection collection = searcher.Get();

            foreach (ManagementBaseObject obj in collection)
            {
                using (obj)
                {
                    string id = obj["ID"]?.ToString() ?? string.Empty;
                    string deviceObject = obj["DeviceObject"]?.ToString() ?? string.Empty;
                    string volumeName = obj["VolumeName"]?.ToString() ?? string.Empty;
                    string? installDate = obj["InstallDate"]?.ToString();

                    DateTime? creationTimeUtc = ParseWmiDate(installDate);

                    if (!string.IsNullOrWhiteSpace(deviceObject))
                    {
                        list.Add(new VssShadowCopyMetadata(id, deviceObject, volumeName, creationTimeUtc));
                    }
                }
            }
        }
        catch
        {
            // Zero-Crash: при отключённой службе VSS или ограничениях WMI возвращаем пустой список
            return Array.Empty<VssShadowCopyMetadata>();
        }

        return list;
    }

    /// <summary>
    /// Находит самую свежую теневую копию тома, на котором расположен указанный целевой файл.
    /// </summary>
    /// <param name="targetFilePath">Полный путь к восстанавливаемому файлу.</param>
    /// <returns>Метаданные подходящей теневой копии либо null, если снимки отсутствуют.</returns>
    public static VssShadowCopyMetadata? FindLatestShadowCopyForFile(string targetFilePath)
    {
        if (string.IsNullOrWhiteSpace(targetFilePath))
        {
            return null;
        }

        string root = Path.GetPathRoot(targetFilePath) ?? string.Empty;
        if (string.IsNullOrWhiteSpace(root))
        {
            return null;
        }

        IReadOnlyList<VssShadowCopyMetadata> copies = GetAvailableShadowCopies();
        if (copies.Count == 0)
        {
            return null;
        }

        // Фильтруем снимки по совпадению корня тома или берем наиболее поздний снимок
        VssShadowCopyMetadata? bestMatch = null;
        DateTime latestDate = DateTime.MinValue;

        for (int i = 0; i < copies.Count; i++)
        {
            var copy = copies[i];
            DateTime date = copy.CreationTimeUtc ?? DateTime.MinValue;

            // Если том совпадает или не указан жестко, выбираем самый свежий
            if (bestMatch == null || date > latestDate)
            {
                bestMatch = copy;
                latestDate = date;
            }
        }

        return bestMatch;
    }

    /// <summary>
    /// Выполняет пофайловый откат файла из самой свежей теневой копии тома.
    /// </summary>
    /// <param name="targetFilePath">Полный путь к восстанавливаемому целевому файлу.</param>
    /// <param name="restoredFromShadowPath">Путь к файлу-источнику в теневой копии.</param>
    /// <param name="errorMessage">Сообщение об ошибке в случае сбоя или null при успехе.</param>
    /// <returns>True, если файл был успешно прочитан из VSS и скопирован на исходное место.</returns>
    public static bool RestoreFileFromShadowCopy(
        string targetFilePath,
        out string? restoredFromShadowPath,
        out string? errorMessage)
    {
        restoredFromShadowPath = null;
        errorMessage = null;

        if (string.IsNullOrWhiteSpace(targetFilePath))
        {
            errorMessage = "Путь к файлу пуст.";
            return false;
        }

        try
        {
            string fullPath = Path.GetFullPath(targetFilePath);
            string root = Path.GetPathRoot(fullPath) ?? string.Empty;
            if (string.IsNullOrEmpty(root))
            {
                errorMessage = $"Не удалось определить корень тома для пути: {fullPath}";
                return false;
            }

            string relativePath = fullPath.Substring(root.Length).TrimStart('\\', '/');

            IReadOnlyList<VssShadowCopyMetadata> copies = GetAvailableShadowCopies();
            if (copies.Count == 0)
            {
                errorMessage = "В системе отсутствуют доступные теневые копии томов (VSS).";
                return false;
            }

            // Перебираем теневые копии от самых свежих к более старым в поисках неповреждённой версии файла
            var sortedCopies = copies
                .OrderByDescending(c => c.CreationTimeUtc ?? DateTime.MinValue)
                .ToList();

            foreach (var copy in sortedCopies)
            {
                string devicePath = copy.DeviceObject.TrimEnd('\\');
                string shadowCandidate = $"{devicePath}\\{relativePath}";

                if (File.Exists(shadowCandidate))
                {
                    string? destDir = Path.GetDirectoryName(fullPath);
                    if (!string.IsNullOrEmpty(destDir) && !Directory.Exists(destDir))
                    {
                        Directory.CreateDirectory(destDir);
                    }

                    // Снимаем блокирующие атрибуты ReadOnly/Hidden перед перезаписью
                    if (File.Exists(fullPath))
                    {
                        var attrs = File.GetAttributes(fullPath);
                        if ((attrs & FileAttributes.ReadOnly) != 0)
                        {
                            File.SetAttributes(fullPath, attrs & ~FileAttributes.ReadOnly);
                        }
                    }

                    File.Copy(shadowCandidate, fullPath, overwrite: true);
                    restoredFromShadowPath = shadowCandidate;
                    return true;
                }
            }

            errorMessage = "Файл не найден ни в одной доступной теневой копии.";
            return false;
        }
        catch (Exception ex)
        {
            errorMessage = $"Исключение при откате файла из VSS: {ex.Message}";
            return false;
        }
    }

    /// <summary>
    /// Выполняет пакетный откат списка файлов из теневых копий VSS.
    /// </summary>
    /// <param name="filePaths">Коллекция путей к повреждённым файлам.</param>
    /// <returns>Количество успешно восстановленных файлов.</returns>
    public static int RestoreFilesBatch(IEnumerable<string>? filePaths)
    {
        if (filePaths == null)
        {
            return 0;
        }

        int successCount = 0;
        foreach (string path in filePaths)
        {
            if (RestoreFileFromShadowCopy(path, out _, out _))
            {
                successCount++;
            }
        }

        return successCount;
    }

    /// <summary>
    /// Возвращает количество защищаемых томов дисков, для которых имеются активные снимки VSS.
    /// </summary>
    public static int GetProtectedVolumesCount()
    {
        try
        {
            IReadOnlyList<VssShadowCopyMetadata> copies = GetAvailableShadowCopies();
            if (copies.Count == 0)
            {
                return 0;
            }

            return copies
                .Select(c => c.VolumeName)
                .Where(v => !string.IsNullOrWhiteSpace(v))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count();
        }
        catch
        {
            return 0;
        }
    }

    /// <summary>
    /// Преобразует строку даты формата WMI CIM (например, 20261118183000.000000+000) в UTC DateTime.
    /// </summary>
    private static DateTime? ParseWmiDate(string? wmiDate)
    {
        if (string.IsNullOrWhiteSpace(wmiDate) || wmiDate.Length < 14)
        {
            return null;
        }

        try
        {
            return ManagementDateTimeConverter.ToDateTime(wmiDate).ToUniversalTime();
        }
        catch
        {
            return null;
        }
    }
}
