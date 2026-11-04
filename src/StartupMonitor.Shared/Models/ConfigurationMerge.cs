using System.Text.Json;
using System.Text.Json.Nodes;

namespace StartupMonitor.Shared.Models;

/// <summary>Согласует изменения устаревшего снимка с актуальными данными без отката независимых полей.</summary>
internal static class ConfigurationMerge
{
    /// <summary>Выполняет трёхстороннее объединение только на пути сохранения изменённого снимка.</summary>
    /// <typeparam name="T">Тип сохраняемого контракта.</typeparam>
    /// <param name="baseline">Неизменяемое состояние, из которого получен снимок.</param>
    /// <param name="requested">Снимок с изменениями вызывающего кода.</param>
    /// <param name="current">Актуальное состояние хранилища.</param>
    /// <param name="options">Единые настройки сериализации контракта.</param>
    /// <returns>Объединённый независимый объект.</returns>
    public static T Merge<T>(T baseline, T requested, T current, JsonSerializerOptions options) where T : class
    {
        var merged = MergeNode(JsonSerializer.SerializeToNode(baseline, options),
            JsonSerializer.SerializeToNode(requested, options), JsonSerializer.SerializeToNode(current, options), "$ ");
        return merged?.Deserialize<T>(options) ?? throw new JsonException("Не удалось объединить снимок конфигурации.");
    }

    /// <summary>Объединяет объекты по полям, списки записей по Id и сохраняет конкурентные удаления.</summary>
    /// <param name="baseline">Исходное значение.</param>
    /// <param name="requested">Запрошенное значение.</param>
    /// <param name="current">Актуальное значение.</param>
    /// <param name="path">Путь поля для диагностики конфликтов без вывода значений.</param>
    /// <returns>Независимый JSON-узел объединённых данных.</returns>
    private static JsonNode? MergeNode(JsonNode? baseline, JsonNode? requested, JsonNode? current, string path)
    {
        if (JsonNode.DeepEquals(baseline, requested)) return current?.DeepClone();
        if (JsonNode.DeepEquals(baseline, current) || JsonNode.DeepEquals(requested, current)) return requested?.DeepClone();
        if (baseline is JsonObject original && requested is JsonObject changed && current is JsonObject latest)
        {
            var result = (JsonObject)latest.DeepClone();
            foreach (var name in original.Select(item => item.Key).Union(changed.Select(item => item.Key)))
            {
                result[name] = MergeNode(original[name], changed[name], latest[name], path + "." + name);
            }
            return result;
        }
        if (baseline is JsonArray oldItems && requested is JsonArray newItems && current is JsonArray currentItems &&
            CanIndex(oldItems) && CanIndex(newItems) && CanIndex(currentItems))
        {
            var oldById = oldItems.ToDictionary(GetId, StringComparer.Ordinal);
            var newById = newItems.ToDictionary(GetId, StringComparer.Ordinal);
            var result = new JsonArray();
            var existing = new HashSet<string>(StringComparer.Ordinal);
            foreach (var item in currentItems)
            {
                var id = GetId(item);
                existing.Add(id);
                if (oldById.TryGetValue(id, out var oldItem))
                {
                    if (newById.TryGetValue(id, out var newItem))
                        result.Add(MergeNode(oldItem, newItem, item, path + "[" + id + "]"));
                }
                else
                {
                    result.Add(item?.DeepClone());
                }
            }
            foreach (var item in newItems)
            {
                var id = GetId(item);
                if (!oldById.ContainsKey(id) && existing.Add(id)) result.Add(item?.DeepClone());
            }
            return result;
        }
        throw new InvalidOperationException($"Конфликт параллельного изменения поля {path.Trim()}; загрузите актуальные данные и повторите операцию.");
    }

    /// <summary>Проверяет наличие уникальных стабильных идентификаторов элементов массива.</summary>
    /// <param name="items">Проверяемый массив.</param>
    /// <returns>Признак возможности независимого объединения элементов.</returns>
    private static bool CanIndex(JsonArray items)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        return items.All(item => item is JsonObject obj && obj["id"] is JsonValue id &&
            id.TryGetValue<string>(out var text) && !string.IsNullOrWhiteSpace(text) && ids.Add(text));
    }

    /// <summary>Извлекает предварительно проверенный идентификатор элемента.</summary>
    /// <param name="item">Элемент, проверенный методом CanIndex.</param>
    /// <returns>Строковый идентификатор.</returns>
    private static string GetId(JsonNode? item) => item?["id"]?.GetValue<string>()
        ?? throw new JsonException("В объединяемой записи отсутствует Id.");
}
