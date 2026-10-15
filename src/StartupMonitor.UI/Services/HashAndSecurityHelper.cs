using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace StartupMonitor.UI.Services;

/// <summary>
/// Хелпер для быстрого вычисления криптографических хешей файлов (SHA-256, SHA-1, MD5)
/// и генерации надежных паролей с оценкой стойкости/энтропии.
/// </summary>
public static class HashAndSecurityHelper
{
    /// <summary>
    /// Вычисляет SHA-256 хеш файла.
    /// </summary>
    public static string ComputeFileSha256(string filePath)
    {
        if (!File.Exists(filePath)) return string.Empty;
        using var stream = File.OpenRead(filePath);
        using var sha = SHA256.Create();
        byte[] hash = sha.ComputeHash(stream);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    /// <summary>
    /// Вычисляет SHA-1 хеш файла.
    /// </summary>
    public static string ComputeFileSha1(string filePath)
    {
        if (!File.Exists(filePath)) return string.Empty;
        using var stream = File.OpenRead(filePath);
        using var sha = SHA1.Create();
        byte[] hash = sha.ComputeHash(stream);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    /// <summary>
    /// Вычисляет MD5 хеш файла.
    /// </summary>
    public static string ComputeFileMd5(string filePath)
    {
        if (!File.Exists(filePath)) return string.Empty;
        using var stream = File.OpenRead(filePath);
        using var md5 = MD5.Create();
        byte[] hash = md5.ComputeHash(stream);
        return Convert.ToHexString(hash).ToLowerInvariant();
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

        var result = new StringBuilder(length);
        byte[] randomBytes = new byte[length];
        RandomNumberGenerator.Fill(randomBytes);

        for (int i = 0; i < length; i++)
        {
            int index = randomBytes[i] % pool.Length;
            result.Append(pool[index]);
        }

        return result.ToString();
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
}
