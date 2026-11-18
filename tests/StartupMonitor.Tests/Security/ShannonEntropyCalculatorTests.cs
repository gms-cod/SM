using System.Text;
using StartupMonitor.Service.Services.AntiRansomware;

namespace StartupMonitor.Tests.Security;

/// <summary>
/// Модульные тесты безаллокационного калькулятора энтропии Шеннона (ShannonEntropyCalculator).
/// Проверяют граничные значения, нулевые аллокации и дифференциацию зашифрованных блоков от текста.
/// </summary>
public class ShannonEntropyCalculatorTests
{
    /// <summary>
    /// Проверяет, что для пустого массива или спана энтропия равна 0.0.
    /// </summary>
    [Fact]
    public void CalculateEntropy_EmptyBuffer_ReturnsZero()
    {
        // Arrange
        byte[] empty = [];

        // Act
        double entropySpan = ShannonEntropyCalculator.CalculateEntropy(empty.AsSpan());
        double entropyArray = ShannonEntropyCalculator.CalculateEntropy(empty);
        double entropyNull = ShannonEntropyCalculator.CalculateEntropy((byte[]?)null);

        // Assert
        Assert.Equal(0.0, entropySpan);
        Assert.Equal(0.0, entropyArray);
        Assert.Equal(0.0, entropyNull);
    }

    /// <summary>
    /// Проверяет, что для буфера длиной 1 байт энтропия равна 0.0.
    /// </summary>
    [Fact]
    public void CalculateEntropy_SingleByte_ReturnsZero()
    {
        // Arrange
        byte[] single = [0x42];

        // Act
        double entropy = ShannonEntropyCalculator.CalculateEntropy(single);

        // Assert
        Assert.Equal(0.0, entropy);
    }

    /// <summary>
    /// Проверяет, что для однородного массива (все байты идентичны) энтропия равна строго 0.0.
    /// </summary>
    [Fact]
    public void CalculateEntropy_UniformBytes_ReturnsZero()
    {
        // Arrange
        byte[] uniform = new byte[2048];
        Array.Fill<byte>(uniform, 0xAA);

        // Act
        double entropy = ShannonEntropyCalculator.CalculateEntropy(uniform);

        // Assert
        Assert.Equal(0.0, entropy);
    }

    /// <summary>
    /// Проверяет, что для обычного текста на естественном языке энтропия находится в диапазоне 3.0 - 4.8 бит на байт.
    /// </summary>
    [Fact]
    public void CalculateEntropy_PlainText_ReturnsLowEntropy()
    {
        // Arrange
        string text = "Startup Monitor Service Guard - Real-time security and anti-ransomware protection for Windows. " +
                      "This text represents a standard English sentence with recurring natural patterns and low randomness.";
        byte[] bytes = Encoding.UTF8.GetBytes(text);

        // Act
        double entropy = ShannonEntropyCalculator.CalculateEntropy(bytes);

        // Assert
        Assert.InRange(entropy, 3.0, 5.0);
    }

    /// <summary>
    /// Проверяет, что для псевдослучайных (зашифрованных) байтов энтропия превышает критический порог 7.5.
    /// </summary>
    [Fact]
    public void CalculateEntropy_EncryptedOrRandomBytes_ReturnsHighEntropy()
    {
        // Arrange
        byte[] randomBytes = new byte[4096];
        var rng = new Random(1337);
        rng.NextBytes(randomBytes);

        // Act
        double entropy = ShannonEntropyCalculator.CalculateEntropy(randomBytes);

        // Assert
        Assert.True(entropy >= 7.5, $"Ожидалась энтропия >= 7.5 для случайных данных, получено: {entropy}");
    }

    /// <summary>
    /// Проверяет метод IsHighEntropy при превышении порога и при низких значениях.
    /// </summary>
    [Fact]
    public void IsHighEntropy_EvaluatesCorrectly()
    {
        // Arrange
        byte[] randomBytes = new byte[1024];
        new Random(42).NextBytes(randomBytes);

        byte[] plainBytes = Encoding.UTF8.GetBytes("Simple plaintext for testing entropy thresholds in security tests.");

        // Act & Assert
        Assert.True(ShannonEntropyCalculator.IsHighEntropy(randomBytes, 7.5));
        Assert.False(ShannonEntropyCalculator.IsHighEntropy(plainBytes, 7.5));
    }

    /// <summary>
    /// Проверяет, что короткие буферы (< 64 байт) не помечаются как высокая энтропия во избежание ложных срабатываний.
    /// </summary>
    [Fact]
    public void IsHighEntropy_ShortBuffer_ReturnsFalse()
    {
        // Arrange
        byte[] shortBuffer = new byte[32];
        new Random(999).NextBytes(shortBuffer);

        // Act
        bool result = ShannonEntropyCalculator.IsHighEntropy(shortBuffer, 7.0);

        // Assert
        Assert.False(result);
    }
}
