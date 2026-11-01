using System.Text;

namespace StartupMonitor.Shared.Models;

/// <summary>Читает строковые кадры IPC с ограничением памяти и сохраняет остаток следующего кадра.</summary>
public sealed class BoundedLineReader
{
    /// <summary>Максимальный размер кадра: допускает крупные снимки, но исключает неограниченный вход.</summary>
    public const int MaximumFrameCharacters = 8 * 1024 * 1024;
    private readonly TextReader _reader;
    private readonly int _maximumCharacters;
    private readonly char[] _buffer = new char[4096];
    private int _position;
    private int _length;

    /// <summary>Инициализирует ограниченный читатель без передачи владения исходным потоком.</summary>
    /// <param name="reader">Последовательно используемый текстовый поток.</param>
    /// <param name="maximumCharacters">Предельное количество символов одного кадра.</param>
    public BoundedLineReader(TextReader reader, int maximumCharacters = MaximumFrameCharacters)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumCharacters);
        _reader = reader;
        _maximumCharacters = maximumCharacters;
    }

    /// <summary>Читает завершённый переводом строки кадр; неполный или чрезмерный кадр отклоняется.</summary>
    /// <param name="cancellationToken">Отмена ожидания очередного фрагмента.</param>
    /// <returns>Кадр без перевода строки либо null при штатном EOF между кадрами.</returns>
    public async ValueTask<string?> ReadLineAsync(CancellationToken cancellationToken = default)
    {
        var result = new StringBuilder();
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_position == _length)
            {
                _length = await _reader.ReadAsync(_buffer.AsMemory(), cancellationToken).ConfigureAwait(false);
                _position = 0;
                if (_length == 0)
                {
                    if (result.Length != 0)
                        throw new InvalidDataException("IPC: соединение закрыто до завершения строкового кадра.");
                    return null;
                }
            }

            var newline = Array.IndexOf(_buffer, '\n', _position, _length - _position);
            var end = newline >= 0 ? newline : _length;
            var count = end - _position;
            if (count > _maximumCharacters - result.Length)
                throw new InvalidDataException($"IPC: размер кадра превышает {_maximumCharacters} символов.");
            result.Append(_buffer, _position, count);
            _position = newline >= 0 ? end + 1 : end;
            if (newline < 0) continue;
            if (result.Length > 0 && result[^1] == '\r') result.Length--;
            return result.ToString();
        }
    }
}
