using System.Net;
using StartupMonitor.Shared.Models;
using StartupMonitor.Shared.Models.Firewall;

namespace StartupMonitor.Service.Services.Firewall;

/// <summary>Выявляет регулярное появление отдельных исходящих TCP-соединений в снимках (C2 Beaconing). Это эвристика, а не доказательство угрозы.</summary>
internal sealed class TrafficBehavioralScorer
{
    private const int MaxGroups = 1024;
    private const int RequiredObservations = 5;
    private static readonly TimeSpan Retention = TimeSpan.FromHours(1);
    private static readonly TimeSpan Cooldown = TimeSpan.FromMinutes(30);

    private readonly object _sync = new();
    private readonly Dictionary<string, Group> _groups = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Доверенные имена процессов, для которых фоновые регулярные веб-соединения являются штатными.</summary>
    private static readonly HashSet<string> AllowlistedProcessNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "chrome.exe",
        "msedge.exe",
        "firefox.exe",
        "svchost.exe",
        "w32time.exe",
        "w32time",
        "StartupMonitor.Service.exe",
        "StartupMonitor.UI.exe"
    };

    /// <summary>Известные порты C2 / бэкдоров и утилит удалённого управления.</summary>
    private static readonly HashSet<int> SuspiciousPorts = new()
    {
        4444, 1337, 31337, 6667, 8888, 9999
    };

    private sealed class Group
    {
        public readonly Queue<DateTime> Times = new();
        public readonly Dictionary<string, DateTime> Sockets = new(StringComparer.OrdinalIgnoreCase);
        public DateTime LastSeen;
        public DateTime LastAlert = DateTime.MinValue;
    }

    /// <summary>Проверяет, входит ли сокет в белый список легитимных системных или браузерных соединений.</summary>
    private static bool IsAllowlisted(string processName, int remotePort)
    {
        // Протокол синхронизации времени NTP
        if (remotePort == 123)
            return true;

        // Штатные браузерные веб-порты для доверенных браузеров
        if ((remotePort == 80 || remotePort == 443) && AllowlistedProcessNames.Contains(processName))
            return true;

        return false;
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
            IncomingTcpObserver.IsInternalAddress(remote))
            return null;

        var processName = socket.ProcessName ?? string.Empty;
        if (IsAllowlisted(processName, socket.RemotePort))
            return null;

        lock (_sync)
        {
            foreach (var stale in _groups.Where(p => now - p.Value.LastSeen > Retention || now < p.Value.LastSeen).Select(p => p.Key).ToArray())
                _groups.Remove(stale);

            var key = $"{socket.ProcessId}|{processStart}|{remote}|{socket.RemotePort}";
            if (!_groups.TryGetValue(key, out var group))
            {
                if (_groups.Count >= MaxGroups)
                    return null;
                _groups[key] = group = new Group();
            }

            group.LastSeen = now;
            foreach (var old in group.Sockets.Where(p => now - p.Value > Retention).Select(p => p.Key).ToArray())
                group.Sockets.Remove(old);

            var socketKey = $"{socket.LocalAddress}|{socket.LocalPort}";
            if (group.Sockets.ContainsKey(socketKey) || group.Sockets.Count >= 256)
                return null;

            group.Sockets[socketKey] = now;
            if (group.Times.Count > 0 && now <= group.Times.Last())
                return null;

            group.Times.Enqueue(now);
            while (group.Times.Count > RequiredObservations)
                group.Times.Dequeue();

            if (group.Times.Count < RequiredObservations || now - group.LastAlert < Cooldown)
                return null;

            var times = group.Times.ToArray();
            var intervals = times.Zip(times.Skip(1), (a, b) => (b - a).TotalSeconds).ToArray();
            var sortedIntervals = intervals.OrderBy(x => x).ToArray();
            double median = GetMedian(sortedIntervals);
            if (median < 10.0 || median > 600.0)
                return null;

            var deviations = sortedIntervals.Select(x => Math.Abs(x - median)).OrderBy(x => x).ToArray();
            double mad = GetMedian(deviations);
            double jitterPercent = median > 0 ? (mad / median) * 100.0 : 0.0;
            if (jitterPercent > 25.0)
                return null;

            group.LastAlert = now;
            double average = intervals.Average();
            bool isSuspiciousPort = SuspiciousPorts.Contains(socket.RemotePort);
            int riskScore = isSuspiciousPort ? 90 : (int)Math.Clamp(Math.Round(95.0 - (jitterPercent / 25.0) * 15.0), 80, 95);
            string anomalyType = isSuspiciousPort ? "SuspiciousPort" : "Beaconing";

            return new TrafficAnomalyObservation(
                ProcessId: socket.ProcessId,
                ProcessName: processName,
                RemoteAddress: remote.ToString(),
                RemotePort: socket.RemotePort,
                ObservationCount: RequiredObservations,
                AverageIntervalSeconds: average,
                JitterPercent: Math.Round(jitterPercent, 1),
                AnomalyType: anomalyType,
                RiskScore: riskScore,
                Protocol: "TCP",
                DetectedAt: now);
        }
    }

    /// <summary>Вычисляет медиану для отсортированного массива значений.</summary>
    private static double GetMedian(double[] sorted)
    {
        if (sorted.Length == 0)
            return 0.0;
        int mid = sorted.Length / 2;
        if (sorted.Length % 2 == 0)
            return (sorted[mid - 1] + sorted[mid]) / 2.0;
        return sorted[mid];
    }

    /// <summary>Удаляет накопленные наблюдения после отключения сканирования.</summary>
    public void Clear()
    {
        lock (_sync)
        {
            _groups.Clear();
        }
    }
}
