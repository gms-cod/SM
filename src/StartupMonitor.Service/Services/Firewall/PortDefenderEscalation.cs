namespace StartupMonitor.Service.Services.Firewall;

/// <summary>
/// Результат вычисления срока бана.
/// </summary>
public readonly record struct BanDurationResult(TimeSpan? Duration, bool IsPermanent)
{
    /// <summary>Временный бан с указанной длительностью.</summary>
    public static BanDurationResult Temporary(TimeSpan duration) => new(duration, false);

    /// <summary>Постоянный бан (Permanent Blacklist).</summary>
    public static BanDurationResult Permanent() => new(null, true);
}

/// <summary>
/// Решение о бане, принятое политикой эскалации.
/// </summary>
public sealed record BanDecision(
    string Ip,
    int RepeatCount,
    TimeSpan? Duration,
    bool IsPermanent,
    DateTime? ExpiresAtUtc
);

/// <summary>
/// Политика адаптивной эскалации срока бана Port Defender при повторных атаках.
/// Реализует схему согласно release_firewall.md:
/// 1-й бан: базовый срок (15 минут).
/// 2-й бан: удвоение (30 минут).
/// 3-й бан: удвоение (60 минут).
/// 4-й бан: 24 часа.
/// 5-й+ бан: постоянный (Permanent).
/// При отсутствии нарушений более 24 часов счётчик рецидивов сбрасывается к 1.
/// </summary>
public sealed class PortDefenderEscalation
{
    private sealed class OffenderHistory
    {
        public int RepeatCount { get; set; }
        public DateTime LastViolationUtc { get; set; }
    }

    private readonly object _sync = new();
    private readonly TimeSpan _baseBanDuration;
    private readonly TimeSpan _resetPeriod;
    private readonly Dictionary<string, OffenderHistory> _history = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Базовый срок первичного бана.</summary>
    public TimeSpan BaseBanDuration => _baseBanDuration;

    /// <summary>Период затишья для сброса счетчика рецидивов (по умолчанию 24 ч).</summary>
    public TimeSpan ResetPeriod => _resetPeriod;

    /// <summary>
    /// Инициализирует политику эскалации с настраиваемым базовым сроком и периодом сброса.
    /// </summary>
    public PortDefenderEscalation(TimeSpan? baseBanDuration = null, TimeSpan? resetPeriod = null)
    {
        _baseBanDuration = baseBanDuration ?? TimeSpan.FromMinutes(15);
        _resetPeriod = resetPeriod ?? TimeSpan.FromHours(24);
    }

    /// <summary>
    /// Чистый расчет длительности бана на основе номера рецидива (1-based) и базовой длительности.
    /// 1 -> 15 мин (базовый)
    /// 2 -> 30 мин (x2)
    /// 3 -> 60 мин (x4)
    /// 4 -> 24 часа
    /// 5+ -> Permanent
    /// </summary>
    public static BanDurationResult CalculateBanDuration(int repeatCount, TimeSpan? baseDuration = null)
    {
        var baseSpan = baseDuration ?? TimeSpan.FromMinutes(15);

        if (repeatCount <= 1)
            return BanDurationResult.Temporary(baseSpan);

        if (repeatCount == 2)
            return BanDurationResult.Temporary(baseSpan * 2);

        if (repeatCount == 3)
            return BanDurationResult.Temporary(baseSpan * 4);

        if (repeatCount == 4)
            return BanDurationResult.Temporary(TimeSpan.FromHours(24));

        return BanDurationResult.Permanent();
    }

    /// <summary>
    /// Вычисляет длительность бана для текущего экземпляра.
    /// </summary>
    public BanDurationResult GetDurationForRepeat(int repeatCount) =>
        CalculateBanDuration(repeatCount, _baseBanDuration);

    /// <summary>
    /// Регистрирует нарушение для IP-адреса.
    /// Если с момента последнего нарушения прошло больше ResetPeriod (24 ч),
    /// счетчик рецидивов сбрасывается к 1.
    /// </summary>
    public BanDecision RecordViolation(string ip, DateTime? timestamp = null)
    {
        if (string.IsNullOrWhiteSpace(ip))
            throw new ArgumentException("IP не может быть пустым.", nameof(ip));

        var key = ip.Trim();
        var now = timestamp ?? DateTime.UtcNow;

        lock (_sync)
        {
            int nextRepeatCount = 1;

            if (_history.TryGetValue(key, out var hist))
            {
                var elapsed = now - hist.LastViolationUtc;
                if (elapsed > _resetPeriod)
                {
                    nextRepeatCount = 1;
                }
                else
                {
                    nextRepeatCount = hist.RepeatCount + 1;
                }

                hist.RepeatCount = nextRepeatCount;
                hist.LastViolationUtc = now;
            }
            else
            {
                _history[key] = new OffenderHistory
                {
                    RepeatCount = 1,
                    LastViolationUtc = now
                };
            }

            var durationResult = CalculateBanDuration(nextRepeatCount, _baseBanDuration);
            DateTime? expiresAt = durationResult.IsPermanent ? null : now + durationResult.Duration;

            return new BanDecision(
                Ip: key,
                RepeatCount: nextRepeatCount,
                Duration: durationResult.Duration,
                IsPermanent: durationResult.IsPermanent,
                ExpiresAtUtc: expiresAt
            );
        }
    }

    /// <summary>
    /// Возвращает текущий счетчик рецидивов для IP. Если срок сброса истек, возвращает 0.
    /// </summary>
    public int GetRepeatCount(string ip, DateTime? timestamp = null)
    {
        if (string.IsNullOrWhiteSpace(ip))
            return 0;

        var now = timestamp ?? DateTime.UtcNow;

        lock (_sync)
        {
            if (!_history.TryGetValue(ip.Trim(), out var hist))
                return 0;

            if ((now - hist.LastViolationUtc) > _resetPeriod)
                return 0;

            return hist.RepeatCount;
        }
    }

    /// <summary>Сбрасывает историю для указанного IP.</summary>
    public bool Reset(string ip)
    {
        if (string.IsNullOrWhiteSpace(ip))
            return false;

        lock (_sync)
        {
            return _history.Remove(ip.Trim());
        }
    }

    /// <summary>Очищает всю историю нарушителей.</summary>
    public void Clear()
    {
        lock (_sync)
        {
            _history.Clear();
        }
    }
}
