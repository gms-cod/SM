using System.Runtime.CompilerServices;

namespace StartupMonitor.Service.Services.AntiRansomware;

/// <summary>
/// Высокопроизводительный калькулятор энтропии Шеннона с нулевыми аллокациями в GC (0 байт).
/// Применяется для быстрого выявления зашифрованных блоков данных программ-вымогателей.
/// </summary>
public static class ShannonEntropyCalculator
{
    /// <summary>
    /// Размер предвычисленной таблицы логарифмов по основанию 2 для быстрого безаллокационного доступа.
    /// Покрывает частоты в блоках размером до 64 КБ включительно.
    /// </summary>
    private const int PrecomputedTableSize = 65536;

    /// <summary>
    /// Статическая таблица предвычисленных значений log2(x).
    /// </summary>
    private static readonly double[] s_log2Table = new double[PrecomputedTableSize];

    /// <summary>
    /// Инициализирует предвычисленную таблицу log2.
    /// </summary>
    static ShannonEntropyCalculator()
    {
        s_log2Table[0] = 0.0;
        for (int i = 1; i < PrecomputedTableSize; i++)
        {
            s_log2Table[i] = Math.Log2(i);
        }
    }

    /// <summary>
    /// Рассчитывает информационную энтропию Шеннона для переданного диапазона байтов.
    /// Выполняется строго на стеке (stackalloc int[256]), гарантируя 0 байт аллокаций в куче (GC).
    /// </summary>
    /// <param name="data">Входные данные в виде доступного только для чтения спана байт.</param>
    /// <returns>Энтропия от 0.0 (полная предсказуемость) до 8.0 (максимальная случайность / шифрование).</returns>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public static double CalculateEntropy(ReadOnlySpan<byte> data)
    {
        int length = data.Length;
        if (length <= 1)
        {
            return 0.0;
        }

        // Выделение 1024 байт (256 * 4) на стеке потока без обращения к GC
        Span<int> frequencies = stackalloc int[256];
        frequencies.Clear();

        // Подсчёт частот встречаемости каждого из 256 возможных байтов
        for (int i = 0; i < length; i++)
        {
            frequencies[data[i]]++;
        }

        double log2Length = FastLog2(length);
        double sumFrequenciesWithLog = 0.0;

        // Формула Шеннона: H = - sum( p_i * log2(p_i) ) = log2(N) - (1 / N) * sum( count_i * log2(count_i) )
        for (int i = 0; i < 256; i++)
        {
            int count = frequencies[i];
            if (count > 0)
            {
                sumFrequenciesWithLog += count * FastLog2(count);
            }
        }

        double entropy = log2Length - (sumFrequenciesWithLog / length);

        if (entropy < 0.0)
        {
            return 0.0;
        }

        if (entropy > 8.0)
        {
            return 8.0;
        }

        return entropy;
    }

    /// <summary>
    /// Проверяет, превышает ли энтропия блока данных заданный критический порог шифрования (по умолчанию 7.5).
    /// </summary>
    /// <param name="data">Диапазон байтов для проверки.</param>
    /// <param name="threshold">Порог энтропии (типично 7.2 - 7.6 для зашифрованных шифровальщиками файлов).</param>
    /// <returns>True, если данные обладают высокой энтропией, характерной для шифрования или сильного сжатия.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsHighEntropy(ReadOnlySpan<byte> data, double threshold = 7.5)
    {
        if (data.Length < 64)
        {
            return false;
        }

        return CalculateEntropy(data) >= threshold;
    }

    /// <summary>
    /// Рассчитывает энтропию для переданного массива байт.
    /// </summary>
    /// <param name="buffer">Массив байтов или null.</param>
    /// <returns>Энтропия от 0.0 до 8.0.</returns>
    public static double CalculateEntropy(byte[]? buffer)
    {
        if (buffer == null || buffer.Length == 0)
        {
            return 0.0;
        }

        return CalculateEntropy(buffer.AsSpan());
    }

    /// <summary>
    /// Быстрый расчёт log2(x) с использованием предвычисленной таблицы значений.
    /// </summary>
    /// <param name="value">Положительное целочисленное значение.</param>
    /// <returns>Значение log2.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static double FastLog2(int value)
    {
        if ((uint)value < (uint)PrecomputedTableSize)
        {
            return s_log2Table[value];
        }

        return Math.Log2(value);
    }
}
