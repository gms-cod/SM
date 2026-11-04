using System.Windows.Media;
using StartupMonitor.Shared.Models;
using StartupMonitor.UI.Services;

namespace StartupMonitor.UI.Models;

/// <summary>
/// Строка таблицы активных соединений для DataGrid интерактивного монитора.
/// Оборачивает <see cref="ProcessNetworkSocket"/> и обогащает его GeoIP-данными
/// (<see cref="GeoIpLookupService"/>) и бейджем брандмауэра.
/// </summary>
public sealed class LiveConnectionRow
{
    private static readonly Brush EstablishedBrush = Freeze("#107C41");
    private static readonly Brush ListeningBrush = Freeze("#0078D4");
    private static readonly Brush TimeWaitBrush = Freeze("#CA5010");
    private static readonly Brush CloseWaitBrush = Freeze("#605E5C");
    private static readonly Brush DefaultStateBrush = Freeze("#605E5C");
    private static readonly Brush StateForegroundContrastBrush = Freeze("#FFFFFF");

    /// <summary>Исходный сокет.</summary>
    public ProcessNetworkSocket Socket { get; }

    /// <summary>ID процесса-владельца.</summary>
    public int ProcessId => Socket.ProcessId;

    /// <summary>Алиас PID для XAML-привязок DataGrid (NetworkSecurityHubView).</summary>
    public int Pid => Socket.ProcessId;

    /// <summary>Имя процесса.</summary>
    public string ProcessName => string.IsNullOrEmpty(Socket.ProcessName) ? "(неизвестно)" : Socket.ProcessName;

    /// <summary>Протокол ("TCP", "TCPv6", "UDP", "UDPv6").</summary>
    public string Protocol => Socket.Protocol;

    /// <summary>Локальная конечная точка (IP:Порт).</summary>
    public string LocalEndPoint => Socket.LocalEndPoint;

    /// <summary>Удалённая конечная точка (IP:Порт).</summary>
    public string RemoteEndPoint => Socket.RemoteEndPoint;

    /// <summary>Удалённый IP-адрес.</summary>
    public string RemoteAddress => Socket.RemoteAddress;

    /// <summary>Удалённый порт.</summary>
    public int RemotePort => Socket.RemotePort;

    /// <summary>Состояние соединения.</summary>
    public string State => Socket.State;

    /// <summary>Признак приватного/слушающего адреса (не блокируется).</summary>
    public bool IsPrivate { get; set; }

    /// <summary>Название страны/сети удалённого адреса.</summary>
    public string CountryText { get; set; } = string.Empty;

    /// <summary>Флаг страны/сети (эмодзи).</summary>
    public string CountryFlag { get; set; } = "🌐";

    /// <summary>Бейдж типа сети для колонки DataGrid.</summary>
    public string NetworkTypeBadge => IsPrivate ? "🏠 LAN" : "🌐 WAN";

    /// <summary>Текстовый бейдж статуса брандмауэра для колонки «Брандмауэр».</summary>
    public string FirewallBadge { get; set; } = "🟢 Разрешено";

    /// <summary>Можно ли предложить блокировку IP (только реальные публичные адреса).</summary>
    public bool CanBlockIp =>
        !IsPrivate
        && !string.IsNullOrEmpty(RemoteAddress)
        && RemoteAddress != "*"
        && RemoteAddress != "0.0.0.0";

    /// <summary>Кисть цвета статуса соединения для бейджа колонки «Состояние».</summary>
    public Brush StateBadgeBrush => GetStateBrush(State);

    /// <summary>Контрастный цвет текста для бейджа статуса (белый #FFFFFF для светлой и тёмной тем).</summary>
    public Brush StateForegroundBrush => StateForegroundContrastBrush;

    /// <summary>Создаёт строку с GeoIP-резолвом удалённого адреса.</summary>
    /// <param name="socket">Сокет процесса.</param>
    public LiveConnectionRow(ProcessNetworkSocket socket)
        : this(socket, "🟢 Разрешено")
    {
    }

    /// <summary>Создаёт строку с GeoIP-резолвом и заданным бейджем брандмауэра.</summary>
    /// <param name="socket">Сокет процесса.</param>
    /// <param name="firewallBadge">Бейдж статуса брандмауэра.</param>
    public LiveConnectionRow(ProcessNetworkSocket socket, string firewallBadge)
    {
        Socket = socket ?? throw new ArgumentNullException(nameof(socket));
        FirewallBadge = firewallBadge;
        RefreshGeo();
    }

    /// <summary>Повторно резолвит GeoIP-данные (вызывать при ежесекундном обновлении).</summary>
    public void RefreshGeo()
    {
        var (countryName, countryFlag, isPrivate) = GeoIpLookupService.Resolve(Socket.RemoteAddress);
        CountryText = countryName;
        CountryFlag = countryFlag;
        IsPrivate = isPrivate;
    }

    /// <summary>Возвращает кисть цвета для состояния соединения.</summary>
    /// <param name="state">Состояние (ESTABLISHED, LISTENING, TIME_WAIT, CLOSE_WAIT и др.).</param>
    public static Brush GetStateBrush(string? state)
    {
        var normalized = (state ?? string.Empty).Trim().ToUpperInvariant();
        return normalized switch
        {
            "ESTABLISHED" => EstablishedBrush,
            "LISTENING" or "LISTEN" or "BOUND" => ListeningBrush,
            "TIME_WAIT" => TimeWaitBrush,
            "CLOSE_WAIT" => CloseWaitBrush,
            _ => DefaultStateBrush,
        };
    }

    private static Brush Freeze(string hex)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        brush.Freeze();
        return brush;
    }
}
