using System.Collections.Concurrent;
using StartupMonitor.Shared.Models;

namespace StartupMonitor.Service.Services.Ai;

/// <summary>Потокобезопасный кэш AI-рейтингов (ConcurrentDictionary).</summary>
public class RatingsCache
{
    /// <summary>Ссылка на хранилище конфига для чтения/записи полей AiRating.</summary>
    private readonly JsonDataStore _store;
    // Читается из pipe-потока, пишется из фонового prefetch — нужен ConcurrentDictionary
    private readonly ConcurrentDictionary<string, StartupRating> _ratings = new();

    /// <summary>Количество записей в кэше.</summary>
    public int Count => _ratings.Count;

    /// <summary>Принимает JsonDataStore для сохранения рейтингов в конфиг.</summary>
    public RatingsCache(JsonDataStore store)
    {
        _store = store;
    }

    /// <summary>Возвращает рейтинг из кэша либо из полей записи (AiRating/AiRecommendation).</summary>
    /// <param name="entry">Проверяемая запись.</param>
    /// <returns>StartupRating или null, если рейтинг ещё не вычислен.</returns>
    public StartupRating? Get(StartupEntry entry)
    {
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
        entry.IsUserRating = false;
        _store.UpdateEntry(entry);
    }

    /// <summary>Сохраняет рейтинг в кэш и обновляет поля Ai* в записи.</summary>
    public void Set(StartupEntry entry, StartupRating rating)
    {
        var key = BuildKey(entry);
        _ratings[key] = rating;

        // Синхронизируем поля с записью конфига
        entry.AiRating = rating.Score;
        entry.AiCategory = rating.Category;
        entry.AiRisk = rating.Risk;
        entry.AiRecommendation = rating.Recommendation;

        _store.UpdateEntry(entry);
    }

    /// <summary>Строит ключ для словаря из Location, Name, FilePath, SHA256.</summary>
    private static string BuildKey(StartupEntry entry) =>
        $"{entry.Location}|{entry.Name}|{entry.FilePath}|{entry.SHA256}";
}