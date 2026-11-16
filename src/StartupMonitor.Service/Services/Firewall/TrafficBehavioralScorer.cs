using System.Net;
using StartupMonitor.Shared.Models;
using StartupMonitor.Shared.Models.Firewall;

namespace StartupMonitor.Service.Services.Firewall;

/// <summary>Выявляет регулярное появление отдельных исходящих TCP-соединений в снимках. Это эвристика, а не доказательство угрозы.</summary>
internal sealed class TrafficBehavioralScorer
{
    private const int MaxGroups = 1024;
    private const int RequiredObservations = 5;
    private static readonly TimeSpan Retention = TimeSpan.FromHours(1);
    private static readonly TimeSpan Cooldown = TimeSpan.FromMinutes(30);
    private readonly Dictionary<string, Group> _groups = new(StringComparer.OrdinalIgnoreCase);

    private sealed class Group
    {
        public readonly Queue<DateTime> Times = new();
        public readonly Dictionary<string, DateTime> Sockets = new(StringComparer.OrdinalIgnoreCase);
        public DateTime LastSeen;
        public DateTime LastAlert = DateTime.MinValue;
    }

    /// <summary>Оценивает новое наблюдение, не изменяя сетевые правила и не передавая данные наружу.</summary>
    /// <param name="socket">Снимок TCP-сокета с подтверждённым исходящим направлением.</param>
    /// <param name="processStart">Время запуска процесса в формате FILETIME для защиты от повторного использования PID.</param>
    /// <param name="now">Время снимка в UTC.</param>
    /// <returns>Описание регулярного паттерна либо null при недостатке данных.</returns>
    public TrafficAnomalyObservation? Observe(ProcessNetworkSocket socket, long? processStart, DateTime now)
    {
        ArgumentNullException.ThrowIfNull(socket);
        if (socket.ProcessId <= 0 || processStart is null || processStart <= 0 ||
            !string.Equals(socket.Direction, "Outbound", StringComparison.OrdinalIgnoreCase) ||
            !socket.Protocol.StartsWith("TCP", StringComparison.OrdinalIgnoreCase) ||
            socket.LocalPort is < 1 or > 65535 || socket.RemotePort is < 1 or > 65535 ||
            !IPAddress.TryParse(socket.RemoteAddress, out var remote) ||
            IncomingTcpObserver.IsInternalAddress(remote)) return null;

        foreach (var stale in _groups.Where(p => now - p.Value.LastSeen > Retention || now < p.Value.LastSeen).Select(p => p.Key).ToArray())
            _groups.Remove(stale);
        var key = $"{socket.ProcessId}|{processStart}|{remote}|{socket.RemotePort}";
        if (!_groups.TryGetValue(key, out var group))
        {
            if (_groups.Count >= MaxGroups) return null;
            _groups[key] = group = new Group();
        }
        group.LastSeen = now;
        foreach (var old in group.Sockets.Where(p => now - p.Value > Retention).Select(p => p.Key).ToArray())
            group.Sockets.Remove(old);
        var socketKey = $"{socket.LocalAddress}|{socket.LocalPort}";
        if (group.Sockets.ContainsKey(socketKey) || group.Sockets.Count >= 256) return null;
        group.Sockets[socketKey] = now;
        if (group.Times.Count > 0 && now <= group.Times.Last()) return null;
        group.Times.Enqueue(now);
        while (group.Times.Count > RequiredObservations) group.Times.Dequeue();
        if (group.Times.Count < RequiredObservations || now - group.LastAlert < Cooldown) return null;
        var times = group.Times.ToArray();
        var intervals = times.Zip(times.Skip(1), (a, b) => (b - a).TotalSeconds).ToArray();
        var average = intervals.Average();
        if (average < 10 || average > 600 || intervals.Any(x => Math.Abs(x - average) > average * 0.25)) return null;
        group.LastAlert = now;
        return new TrafficAnomalyObservation(socket.ProcessId, socket.ProcessName ?? string.Empty, remote.ToString(), socket.RemotePort, RequiredObservations, average);
    }

    /// <summary>Удаляет накопленные наблюдения после отключения сканирования.</summary>
    public void Clear() => _groups.Clear();
}
