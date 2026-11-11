using System.Collections.Concurrent;
using StartupMonitor.Shared.Models;

namespace StartupMonitor.Service.Services.Ai;

/// <summary>Потокобезопасный кэш AI-рейтингов (ConcurrentDictionary).</summary>
public class RatingsCache
{
    /// <summary>Ссылка на хранилище конфига для чтения/записи полей AiRating.</summary>
    private readonly JsonDataStore _store;
    /// <summary>Уведомляет владельца после подтверждённого сохранения автоматической оценки.</summary>
    private readonly Action<StartupEntry>? _automaticRatingChanged;
    // Читается из pipe-потока, пишется из фонового prefetch — нужен ConcurrentDictionary
    private readonly ConcurrentDictionary<string, StartupRating> _ratings = new();

    /// <summary>Количество записей в кэше.</summary>
    public int Count => _ratings.Count;

    /// <summary>Принимает JsonDataStore для сохранения рейтингов в конфиг.</summary>
    /// <param name="store">Хранилище записей автозапуска.</param>
    /// <param name="automaticRatingChanged">Уведомление об успешно сохранённой автоматической оценке.</param>
    public RatingsCache(JsonDataStore store, Action<StartupEntry>? automaticRatingChanged = null)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _automaticRatingChanged = automaticRatingChanged;
    }

    /// <summary>Возвращает рейтинг из кэша либо из полей записи (AiRating/AiRecommendation).</summary>
    /// <param name="entry">Проверяемая запись.</param>
    /// <returns>StartupRating или null, если рейтинг ещё не вычислен.</returns>
    public StartupRating? Get(StartupEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        SanitizeForRead(entry);
        var key = BuildKey(entry);

        // Ищем в кэше по ключу
        if (_ratings.TryGetValue(key, out var rating))
            return rating;

        // Если запись уже имеет рейтинг, кэшируем его на лету
        if (entry.AiRating > 0 && !string.IsNullOrEmpty(entry.AiRecommendation))
        {
            var fromEntry = new StartupRating
            {
                Score = entry.AiRating,
                Category = entry.AiCategory,
                Risk = entry.AiRisk,
                Recommendation = entry.AiRecommendation
            };
            _ratings[key] = fromEntry;
            return fromEntry;
        }

        return null;
    }

    /// <summary>Скрывает из снимка устаревший автоматический вердикт, не записывая диск и не изменяя ручную оценку.</summary>
    /// <param name="entry">Независимый снимок для передачи клиенту.</param>
    public static void SanitizeForRead(StartupEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        if (entry.IsUserRating || entry.AiRating <= 0 ||
            string.Equals(entry.AiContextFingerprint, StartupRatingContext.Compute(entry), StringComparison.Ordinal)) return;
        entry.AiRating = 0;
        entry.AiCategory = string.Empty;
        entry.AiRisk = string.Empty;
        entry.AiRecommendation = string.Empty;
        entry.AiContextFingerprint = string.Empty;
    }

    /// <summary>Удаляет рейтинг из кэша и обнуляет поля Ai* в записи, сбрасывая ручной пользовательский флаг.</summary>
    public void Remove(StartupEntry entry)
    {
        var key = BuildKey(entry);
        _ratings.TryRemove(key, out _);
        // Обнуляем AI-поля в записи конфига
        entry.AiRating = 0;
        entry.AiCategory = "";
        entry.AiRisk = "";
        entry.AiRecommendation = "";
        entry.AiContextFingerprint = "";
        entry.IsUserRating = false;
        _store.UpdateEntry(entry);
    }

    /// <summary>Сохраняет рейтинг в кэш и обновляет поля Ai* в записи.</summary>
    public void Set(StartupEntry entry, StartupRating rating)
    {
        if (!entry.IsUserRating) entry.AiContextFingerprint = StartupRatingContext.Compute(entry);
        var key = BuildKey(entry);
        _ratings[key] = rating;

        // Синхронизируем поля с записью конфига
        entry.AiRating = rating.Score;
        entry.AiCategory = rating.Category;
        entry.AiRisk = rating.Risk;
        entry.AiRecommendation = rating.Recommendation;

        _store.UpdateEntry(entry);
    }

    /// <summary>Публикует автоматическую оценку, только если контекст записи и ручная оценка не изменились во время запроса.</summary>
    /// <param name="entry">Снимок записи, отправленный провайдеру.</param>
    /// <param name="rating">Ответ провайдера.</param>
    /// <param name="fingerprint">Отпечаток снимка до начала запроса.</param>
    /// <returns>Признак успешной записи в хранилище.</returns>
    internal bool TrySetAutomatic(StartupEntry entry, StartupRating rating, string fingerprint)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(rating);
        if (!_store.TrySetAutomaticRating(entry.Id, fingerprint, rating)) return false;
        entry.AiRating = rating.Score;
        entry.AiCategory = rating.Category;
        entry.AiRisk = rating.Risk;
        entry.AiRecommendation = rating.Recommendation;
        entry.AiContextFingerprint = fingerprint;
        _ratings[BuildKey(entry)] = rating;
        try { _automaticRatingChanged?.Invoke(entry); }
        catch (Exception ex) { System.Diagnostics.Trace.TraceWarning("Не удалось уведомить об обновлении оценки: {0}", ex); }
        return true;
    }

    /// <summary>Формирует ключ из данных, влияющих на запрос ИИ; ручные оценки и файлы без хеша не разделяются между записями.</summary>
    /// <param name="entry">Запись автозапуска с контекстом оценки.</param>
    /// <returns>Однозначная сериализация полей, передаваемых модели.</returns>
    internal static string BuildKey(StartupEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        var fingerprint = StartupRatingContext.Compute(entry);
        return entry.IsUserRating || string.IsNullOrWhiteSpace(entry.SHA256) ? $"{entry.Id}|{fingerprint}" : fingerprint;
    }
}
