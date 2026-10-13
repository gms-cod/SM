using System.Collections.Concurrent;
using StartupMonitor.Shared.Models.Firewall;

namespace StartupMonitor.Service.Services.Firewall;

/// <summary>
/// Потокобезопасный трекер частоты подключений на основе алгоритма скользящего окна (Sliding Window Rate Limiter).
/// Поддерживает LRU-вытеснение старых IP при превышении емкости (по умолчанию 10 000 записей) для защиты от DDoS OOM.
/// </summary>
public sealed class SlidingWindowTracker
{
    private sealed class TrackerEntry
    {
        public required string Key { get; init; }
        public Queue<DateTime> Timestamps { get; } = new();
        public DateTime LastAccessed { get; set; }
    }

    private readonly object _sync = new();
    private readonly int _defaultThreshold;
    private readonly TimeSpan _defaultWindow;
    private readonly int _maxCapacity;

    /// <summary>
    /// Потокобезопасная коллекция временных меток для каждого RemoteIP: ConcurrentDictionary&lt;string, Queue&lt;DateTime&gt;&gt;.
    /// </summary>
    private readonly ConcurrentDictionary<string, Queue<DateTime>> _ipTimestamps =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly Dictionary<string, LinkedListNode<TrackerEntry>> _entries =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly LinkedList<TrackerEntry> _lruList = new();

    /// <summary>Порог количества подключений в окне по умолчанию.</summary>
    public int Threshold => _defaultThreshold;

    /// <summary>Ширина скользящего временного окна по умолчанию.</summary>
    public TimeSpan Window => _defaultWindow;

    /// <summary>Максимальная емкость трекера (количество отслеживаемых IP).</summary>
    public int MaxCapacity => _maxCapacity;

    /// <summary>Текущее количество отслеживаемых IP-адресов.</summary>
    public int TrackedKeysCount
    {
        get
        {
            lock (_sync)
            {
                return _entries.Count;
            }
        }
    }

    /// <summary>Алиас количества отслеживаемых IP.</summary>
    public int TrackedCount => TrackedKeysCount;

    /// <summary>
    /// Создаёт экземпляр трекера с заданным порогом, окном и емкостью.
    /// </summary>
    public SlidingWindowTracker(int threshold = 10, TimeSpan? window = null, int maxCapacity = 10_000)
    {
        _defaultThreshold = Math.Max(1, threshold);
        _defaultWindow = window ?? TimeSpan.FromMinutes(1);
        _maxCapacity = Math.Max(1, maxCapacity);
    }

    /// <summary>
    /// Создаёт экземпляр трекера с единицей измерения времени TimeWindowUnit.
    /// </summary>
    public SlidingWindowTracker(int threshold, TimeWindowUnit unit, int maxCapacity = 10_000)
        : this(threshold, TimeSpan.FromSeconds((int)unit), maxCapacity)
    {
    }

    /// <summary>
    /// Регистрирует подключение с указанного IP и проверяет, превышен ли порог threshold за окно window.
    /// Вызывается ядром ProcessPortDefenderService.
    /// </summary>
    /// <param name="ip">Удалённый IP-адрес.</param>
    /// <param name="threshold">Порог подключений.</param>
    /// <param name="window">Ширина временного окна.</param>
    /// <param name="currentRate">Количество активных подключений в пределах окна после добавления текущего.</param>
    /// <returns>True, если количество подключений >= threshold; иначе False.</returns>
    public bool RecordConnectionAndCheckThreshold(string ip, int threshold, TimeSpan window, out int currentRate)
    {
        if (string.IsNullOrWhiteSpace(ip))
        {
            currentRate = 0;
            return false;
        }

        var key = ip.Trim();
        var now = DateTime.UtcNow;

        lock (_sync)
        {
            LinkedListNode<TrackerEntry> node;

            if (_entries.TryGetValue(key, out var existingNode))
            {
                node = existingNode;
                _lruList.Remove(node);
                _lruList.AddLast(node);
                node.Value.LastAccessed = now;
            }
            else
            {
                if (_entries.Count >= _maxCapacity)
                {
                    EvictLru();
                }

                var entry = new TrackerEntry { Key = key, LastAccessed = now };
                node = _lruList.AddLast(entry);
                _entries[key] = node;
                _ipTimestamps[key] = entry.Timestamps;
            }

            var queue = node.Value.Timestamps;

            // Удаление устаревших меток времени: now - timestamp > window
            while (queue.Count > 0 && (now - queue.Peek()) > window)
            {
                queue.Dequeue();
            }

            queue.Enqueue(now);
            currentRate = queue.Count;

            return currentRate >= threshold;
        }
    }

    /// <summary>
    /// Регистрирует подключение с использованием дефолтного порога и окна.
    /// </summary>
    public bool RecordConnection(string ip, DateTime? timestamp = null)
    {
        if (string.IsNullOrWhiteSpace(ip))
            return false;

        var key = ip.Trim();
        var now = timestamp ?? DateTime.UtcNow;

        lock (_sync)
        {
            LinkedListNode<TrackerEntry> node;

            if (_entries.TryGetValue(key, out var existingNode))
            {
                node = existingNode;
                _lruList.Remove(node);
                _lruList.AddLast(node);
                node.Value.LastAccessed = now;
            }
            else
            {
                if (_entries.Count >= _maxCapacity)
                {
                    EvictLru();
                }

                var entry = new TrackerEntry { Key = key, LastAccessed = now };
                node = _lruList.AddLast(entry);
                _entries[key] = node;
                _ipTimestamps[key] = entry.Timestamps;
            }

            var queue = node.Value.Timestamps;

            while (queue.Count > 0 && (now - queue.Peek()) > _defaultWindow)
            {
                queue.Dequeue();
            }

            queue.Enqueue(now);
            return queue.Count >= _defaultThreshold;
        }
    }

    /// <summary>
    /// Возвращает текущее количество подключений в пределах окна без добавления нового.
    /// </summary>
    public int GetConnectionCount(string ip, DateTime? timestamp = null)
    {
        if (string.IsNullOrWhiteSpace(ip)) return 0;
        var key = ip.Trim();
        var now = timestamp ?? DateTime.UtcNow;

        lock (_sync)
        {
            if (!_entries.TryGetValue(key, out var node))
                return 0;

            var queue = node.Value.Timestamps;
            while (queue.Count > 0 && (now - queue.Peek()) > _defaultWindow)
            {
                queue.Dequeue();
            }

            return queue.Count;
        }
    }

    /// <summary>
    /// Возвращает количество подключений для заданного окна времени.
    /// </summary>
    public int GetCurrentRate(string ip, TimeSpan window)
    {
        if (string.IsNullOrWhiteSpace(ip)) return 0;
        var key = ip.Trim();
        var now = DateTime.UtcNow;

        lock (_sync)
        {
            if (!_entries.TryGetValue(key, out var node))
                return 0;

            var queue = node.Value.Timestamps;
            while (queue.Count > 0 && (now - queue.Peek()) > window)
            {
                queue.Dequeue();
            }

            return queue.Count;
        }
    }

    /// <summary>Проверяет, отслеживается ли данный IP в памяти.</summary>
    public bool IsTracked(string ip)
    {
        if (string.IsNullOrWhiteSpace(ip)) return false;
        lock (_sync)
        {
            return _entries.ContainsKey(ip.Trim());
        }
    }

    /// <summary>Удаляет запись об IP из памяти.</summary>
    public bool Remove(string ip)
    {
        if (string.IsNullOrWhiteSpace(ip)) return false;
        var key = ip.Trim();

        lock (_sync)
        {
            if (_entries.TryGetValue(key, out var node))
            {
                _lruList.Remove(node);
                _entries.Remove(key);
                _ipTimestamps.TryRemove(key, out _);
                return true;
            }
            return false;
        }
    }

    /// <summary>Сбрасывает историю для IP (алиас Remove).</summary>
    public bool Reset(string ip) => Remove(ip);

    /// <summary>Полная очистка всей истории.</summary>
    public void Clear()
    {
        lock (_sync)
        {
            _entries.Clear();
            _lruList.Clear();
            _ipTimestamps.Clear();
        }
    }

    private void EvictLru()
    {
        var oldest = _lruList.First;
        if (oldest != null)
        {
            _lruList.RemoveFirst();
            _entries.Remove(oldest.Value.Key);
            _ipTimestamps.TryRemove(oldest.Value.Key, out _);
        }
    }
}
