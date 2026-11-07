using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace StartupMonitor.UI.Services;

/// <summary>Тип криптографического хеша файла.</summary>
public enum FileHashKind
{
    Sha256,
    Sha1,
    Md5
}

/// <summary>Набор контрольных сумм одного файла (SHA-256, SHA-1, MD5).</summary>
public readonly record struct FileHashes(string Sha256, string Sha1, string Md5)
{
    /// <summary>Пустой результат (файл не найден или операция отменена).</summary>
    public static FileHashes Empty => new(string.Empty, string.Empty, string.Empty);
}

/// <summary>
/// Хелпер для быстрого вычисления криптографических хешей файлов (SHA-256, SHA-1, MD5)
/// и генерации надежных паролей с оценкой стойкости/энтропии.
/// Вычисления распараллеливаются на все доступные ядра процессора.
/// </summary>
public static class HashAndSecurityHelper
{
    /// <summary>Размер буфера чтения (1 МБ) — оптимум для последовательного дискового I/O.</summary>
    private const int HashBufferSize = 1024 * 1024;

    /// <summary>Вычисляет SHA-256 хеш файла.</summary>
    public static string ComputeFileSha256(string filePath) => ComputeHash(filePath, FileHashKind.Sha256);

    /// <summary>Вычисляет SHA-1 хеш файла.</summary>
    public static string ComputeFileSha1(string filePath) => ComputeHash(filePath, FileHashKind.Sha1);

    /// <summary>Вычисляет MD5 хеш файла.</summary>
    public static string ComputeFileMd5(string filePath) => ComputeHash(filePath, FileHashKind.Md5);

    /// <summary>Вычисляет SHA-256 хеш файла с поддержкой отмены.</summary>
    public static string ComputeFileSha256(string filePath, CancellationToken ct) => ComputeHash(filePath, FileHashKind.Sha256, ct);

    /// <summary>Вычисляет SHA-1 хеш файла с поддержкой отмены.</summary>
    public static string ComputeFileSha1(string filePath, CancellationToken ct) => ComputeHash(filePath, FileHashKind.Sha1, ct);

    /// <summary>Вычисляет MD5 хеш файла с поддержкой отмены.</summary>
    public static string ComputeFileMd5(string filePath, CancellationToken ct) => ComputeHash(filePath, FileHashKind.Md5, ct);

    /// <summary>
    /// Параллельно вычисляет SHA-256, SHA-1 и MD5, используя все ядра процессора
    /// (каждый алгоритм выполняется в собственном рабочем потоке).
    /// </summary>
    public static FileHashes ComputeAllHashes(string filePath, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            return FileHashes.Empty;

        string sha256 = string.Empty;
        string sha1 = string.Empty;
        string md5 = string.Empty;

        Parallel.Invoke(
            new ParallelOptions
            {
                CancellationToken = ct,
                MaxDegreeOfParallelism = Math.Max(3, Environment.ProcessorCount)
            },
            () => sha256 = ComputeHash(filePath, FileHashKind.Sha256, ct),
            () => sha1 = ComputeHash(filePath, FileHashKind.Sha1, ct),
            () => md5 = ComputeHash(filePath, FileHashKind.Md5, ct));

        return new FileHashes(sha256, sha1, md5);
    }

    /// <summary>Асинхронная обёртка над <see cref="ComputeAllHashes"/>.</summary>
    public static Task<FileHashes> ComputeAllHashesAsync(string filePath, CancellationToken ct = default)
        => Task.Run(() => ComputeAllHashes(filePath, ct), ct);

    /// <summary>
    /// Параллельно (на всех ядрах) вычисляет полный набор хешей для набора файлов.
    /// Каждый файл читается один раз: три алгоритма считаются за один проход (IncrementalHash).
    /// </summary>
    public static async Task<IReadOnlyDictionary<string, FileHashes>> ComputeFilesHashesAsync(
        IEnumerable<string> filePaths,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(filePaths);

        var results = new ConcurrentDictionary<string, FileHashes>(StringComparer.OrdinalIgnoreCase);

        await Parallel.ForEachAsync(
            filePaths,
            new ParallelOptions
            {
                CancellationToken = ct,
                MaxDegreeOfParallelism = Environment.ProcessorCount
            },
            (path, token) =>
            {
                results[path] = ComputeAllHashesSinglePass(path, token);
                return ValueTask.CompletedTask;
            }).ConfigureAwait(false);

        return results;
    }

    /// <summary>
    /// Генерирует криптографически стойкий случайный пароль.
    /// </summary>
    public static string GeneratePassword(int length, bool useUpper, bool useDigits, bool useSymbols)
    {
        const string lower = "abcdefghijklmnopqrstuvwxyz";
        const string upper = "ABCDEFGHIJKLMNOPQRSTUVWXYZ";
        const string digits = "0123456789";
        const string symbols = "!@#$%^&*()_-+=[{]};:>|./?";

        var sbPool = new StringBuilder(lower);
        if (useUpper) sbPool.Append(upper);
        if (useDigits) sbPool.Append(digits);
        if (useSymbols) sbPool.Append(symbols);

        string pool = sbPool.ToString();
        if (pool.Length == 0) pool = lower;

        length = Math.Clamp(length, 4, 128);

        var result = new char[length];
        var requiredChars = new List<char>();

        // Обязательно добавляем минимум по 1 символу из каждого запрошенного набора
        if (useUpper)
        {
            byte[] b = new byte[1];
            RandomNumberGenerator.Fill(b);
            requiredChars.Add(upper[b[0] % upper.Length]);
        }
        if (useDigits)
        {
            byte[] b = new byte[1];
            RandomNumberGenerator.Fill(b);
            requiredChars.Add(digits[b[0] % digits.Length]);
        }
        if (useSymbols)
        {
            byte[] b = new byte[1];
            RandomNumberGenerator.Fill(b);
            requiredChars.Add(symbols[b[0] % symbols.Length]);
        }

        // Заполняем обязательные позиции
        int pos = 0;
        foreach (var ch in requiredChars)
        {
            if (pos < length)
            {
                result[pos++] = ch;
            }
        }

        // Остальные позиции заполняем случайными символами из общего пула
        int remaining = length - pos;
        if (remaining > 0)
        {
            byte[] randomBytes = new byte[remaining];
            RandomNumberGenerator.Fill(randomBytes);
            for (int i = 0; i < remaining; i++)
            {
                result[pos++] = pool[randomBytes[i] % pool.Length];
            }
        }

        // Перемешиваем символы алгоритмом Фишера-Йетса для равномерного распределения
        byte[] shuffleBytes = new byte[length];
        RandomNumberGenerator.Fill(shuffleBytes);
        for (int i = length - 1; i > 0; i--)
        {
            int j = shuffleBytes[i] % (i + 1);
            (result[i], result[j]) = (result[j], result[i]);
        }

        return new string(result);
    }

    /// <summary>
    /// Оценивает стойкость пароля (от 0 до 100%).
    /// </summary>
    public static (int Score, string StrengthText) EvaluatePasswordStrength(string password)
    {
        if (string.IsNullOrEmpty(password)) return (0, "Пустой");

        int score = 0;
        if (password.Length >= 8) score += 20;
        if (password.Length >= 12) score += 20;
        if (password.Length >= 16) score += 15;

        bool hasLower = false, hasUpper = false, hasDigit = false, hasSymbol = false;
        foreach (char c in password)
        {
            if (char.IsLower(c)) hasLower = true;
            else if (char.IsUpper(c)) hasUpper = true;
            else if (char.IsDigit(c)) hasDigit = true;
            else hasSymbol = true;
        }

        if (hasLower) score += 10;
        if (hasUpper) score += 10;
        if (hasDigit) score += 10;
        if (hasSymbol) score += 15;

        score = Math.Min(100, score);

        string text = score switch
        {
            < 40 => "Слабый",
            < 70 => "Средний",
            < 90 => "Надежный",
            _ => "Очень стойкий"
        };

        return (score, text);
    }

    /// <summary>Преобразует тип хеша в имя алгоритма для <see cref="IncrementalHash"/>.</summary>
    private static HashAlgorithmName ToAlgorithmName(FileHashKind kind) => kind switch
    {
        FileHashKind.Sha256 => HashAlgorithmName.SHA256,
        FileHashKind.Sha1 => HashAlgorithmName.SHA1,
        _ => HashAlgorithmName.MD5
    };

    /// <summary>
    /// Потоковое (без загрузки файла в память) вычисление одного хеша с проверкой отмены
    /// на каждом блоке чтения.
    /// </summary>
    private static string ComputeHash(string filePath, FileHashKind kind, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            return string.Empty;

        try
        {
            using var stream = new FileStream(
                filePath, FileMode.Open, FileAccess.Read, FileShare.Read,
                HashBufferSize, FileOptions.SequentialScan);

            using var incremental = IncrementalHash.CreateHash(ToAlgorithmName(kind));

            byte[] buffer = ArrayPool<byte>.Shared.Rent(HashBufferSize);
            try
            {
                int read;
                while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
                {
                    ct.ThrowIfCancellationRequested();
                    incremental.AppendData(buffer, 0, read);
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }

            ct.ThrowIfCancellationRequested();
            return Convert.ToHexString(incremental.GetHashAndReset()).ToLowerInvariant();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return string.Empty;
        }
    }

    /// <summary>
    /// Вычисляет SHA-256, SHA-1 и MD5 за один проход по файлу (одно чтение, три параллельных
    /// аккумулятора). Оптимально для пакетной обработки множества файлов.
    /// </summary>
    private static FileHashes ComputeAllHashesSinglePass(string filePath, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            return FileHashes.Empty;

        try
        {
            using var stream = new FileStream(
                filePath, FileMode.Open, FileAccess.Read, FileShare.Read,
                HashBufferSize, FileOptions.SequentialScan);

            using var sha256 = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            using var sha1 = IncrementalHash.CreateHash(HashAlgorithmName.SHA1);
            using var md5 = IncrementalHash.CreateHash(HashAlgorithmName.MD5);

            byte[] buffer = ArrayPool<byte>.Shared.Rent(HashBufferSize);
            try
            {
                int read;
                while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
                {
                    ct.ThrowIfCancellationRequested();
                    sha256.AppendData(buffer, 0, read);
                    sha1.AppendData(buffer, 0, read);
                    md5.AppendData(buffer, 0, read);
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }

            ct.ThrowIfCancellationRequested();
            return new FileHashes(
                Convert.ToHexString(sha256.GetHashAndReset()).ToLowerInvariant(),
                Convert.ToHexString(sha1.GetHashAndReset()).ToLowerInvariant(),
                Convert.ToHexString(md5.GetHashAndReset()).ToLowerInvariant());
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return FileHashes.Empty;
        }
    }
}
