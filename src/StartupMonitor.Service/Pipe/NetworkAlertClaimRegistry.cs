using StartupMonitor.Shared.Models.Firewall;

namespace StartupMonitor.Service.Pipe;

/// <summary>Выдаёт право показа карточки одному авторизованному IPC-клиенту до истечения короткой аренды.</summary>
internal sealed class NetworkAlertClaimRegistry
{
    private readonly object _sync = new();
    private readonly Dictionary<string, Claim> _claims = new(StringComparer.Ordinal);
    private readonly Func<DateTime> _utcNow;
    private static readonly TimeSpan LeaseDuration = TimeSpan.FromMinutes(1);

    /// <summary>Создаёт реестр с тестируемым временем; сам реестр не предоставляет полномочий менять правила.</summary>
    /// <param name="utcNow">Источник UTC; по умолчанию системное время.</param>
    public NetworkAlertClaimRegistry(Func<DateTime>? utcNow = null) => _utcNow = utcNow ?? (() => DateTime.UtcNow);

    /// <summary>Возвращает собственную текущую либо первую свободную карточку; соседние UI не получают тот же запрос.</summary>
    /// <param name="clientId">Идентификатор авторизованного подключения.</param>
    /// <param name="alerts">Актуальные доступные группы детектора.</param>
    /// <returns>Не более одной карточки, арендованной этому клиенту.</returns>
    public List<NetworkConnectionAlertDto> GetNext(string clientId, IReadOnlyList<NetworkConnectionAlertDto> alerts)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(clientId);
        ArgumentNullException.ThrowIfNull(alerts);
        lock (_sync)
        {
            Prune();
            var currentIds = alerts.Select(a => a.AlertId).ToHashSet(StringComparer.Ordinal);
            foreach (var id in _claims.Keys.Where(id => !currentIds.Contains(id)).ToArray()) _claims.Remove(id);
            var selected = alerts.FirstOrDefault(a => _claims.TryGetValue(a.AlertId, out var claim) && claim.ClientId == clientId)
                ?? alerts.FirstOrDefault(a => !_claims.ContainsKey(a.AlertId));
            if (selected == null) return [];
            _claims[selected.AlertId] = new Claim(clientId, _utcNow() + LeaseDuration);
            return [selected];
        }
    }

    /// <summary>Продлевает существующее право либо получает свободный запрос после переподключения.</summary>
    /// <param name="clientId">Идентификатор авторизованного подключения.</param>
    /// <param name="alertId">Авторитетный идентификатор существующей группы.</param>
    /// <returns>False, если карточка уже выдана другому клиенту.</returns>
    public bool TryAcquire(string clientId, string alertId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(clientId);
        ArgumentException.ThrowIfNullOrWhiteSpace(alertId);
        lock (_sync)
        {
            Prune();
            if (_claims.TryGetValue(alertId, out var claim) && claim.ClientId != clientId) return false;
            _claims[alertId] = new Claim(clientId, _utcNow() + LeaseDuration);
            return true;
        }
    }

    /// <summary>Освобождает завершённую карточку только от имени её действующего клиента.</summary>
    /// <param name="clientId">Подключение, завершившее работу с карточкой.</param>
    /// <param name="alertId">Завершённый запрос.</param>
    public void Release(string clientId, string alertId)
    {
        lock (_sync)
            if (_claims.TryGetValue(alertId, out var claim) && claim.ClientId == clientId) _claims.Remove(alertId);
    }

    /// <summary>Возвращает все права отключившегося клиента в очередь без изменения сетевой политики.</summary>
    /// <param name="clientId">Отключённое подключение.</param>
    public void ReleaseClient(string clientId)
    {
        lock (_sync)
            foreach (var id in _claims.Where(p => p.Value.ClientId == clientId).Select(p => p.Key).ToArray()) _claims.Remove(id);
    }

    /// <summary>Удаляет истёкшие аренды под общей блокировкой.</summary>
    private void Prune()
    {
        var now = _utcNow();
        foreach (var id in _claims.Where(p => p.Value.ExpiresAt <= now).Select(p => p.Key).ToArray()) _claims.Remove(id);
    }

    /// <summary>Владелец показа и подтверждённый срок его права.</summary>
    private sealed record Claim(string ClientId, DateTime ExpiresAt);
}
