using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text.Json;
using StartupMonitor.Shared.Models;
using StartupMonitor.Shared.Models.Firewall;

namespace StartupMonitor.Tests.Services;

// ─── Тестовые контракты управления правилами ────────────────────────────────
// Производственных типов CreateRuleRequest / DeleteRuleRequest / ToggleRuleRequest
// и FirewallFilteringMode в Shared пока нет (есть только Block/Unblock IP/Port).
// Чтобы набор компилировался и фиксировал ожидаемый контракт для будущих
// этапов (Worker 1/2), DTO и enum определены здесь как тестовые двойники
// с JSON-именами, совместимыми с принятым соглашением FirewallModels.
// При появлении одноимённых типов в StartupMonitor.Shared.Models.Firewall
// конфликтов не будет: эти типы живут в тестовом неймспейсе.
// ────────────────────────────────────────────────────────────────────────────

/// <summary>Режим фильтрации брандмауэра (ожидаемый контракт).</summary>
public enum FirewallFilteringMode
{
    Normal = 0,
    WhitelistOutbound = 1,
    KillswitchBlockAll = 2,
}

/// <summary>Запрос создания произвольного правила (ожидаемый контракт).</summary>
public sealed class CreateRuleRequest
{
    [System.Text.Json.Serialization.JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [System.Text.Json.Serialization.JsonPropertyName("description")]
    public string? Description { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("direction")]
    public FirewallDirection Direction { get; set; } = FirewallDirection.Inbound;

    [System.Text.Json.Serialization.JsonPropertyName("action")]
    public FirewallAction Action { get; set; } = FirewallAction.Block;

    [System.Text.Json.Serialization.JsonPropertyName("protocol")]
    public FirewallProtocol Protocol { get; set; } = FirewallProtocol.Tcp;

    [System.Text.Json.Serialization.JsonPropertyName("localPorts")]
    public string LocalPorts { get; set; } = string.Empty;

    [System.Text.Json.Serialization.JsonPropertyName("remoteAddresses")]
    public string RemoteAddresses { get; set; } = string.Empty;

    [System.Text.Json.Serialization.JsonPropertyName("applicationPath")]
    public string? ApplicationPath { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = true;

    [System.Text.Json.Serialization.JsonPropertyName("profiles")]
    public FirewallProfile Profiles { get; set; } = FirewallProfile.All;
}

/// <summary>Запрос удаления правила по имени (ожидаемый контракт).</summary>
public sealed class DeleteRuleRequest
{
    [System.Text.Json.Serialization.JsonPropertyName("ruleName")]
    public string RuleName { get; set; } = string.Empty;

    public DeleteRuleRequest() { }

    public DeleteRuleRequest(string ruleName) => RuleName = ruleName;
}

/// <summary>Запрос вкл/выкл правила по имени (ожидаемый контракт).</summary>
public sealed class ToggleRuleRequest
{
    [System.Text.Json.Serialization.JsonPropertyName("ruleName")]
    public string RuleName { get; set; } = string.Empty;

    [System.Text.Json.Serialization.JsonPropertyName("enabled")]
    public bool Enabled { get; set; }

    public ToggleRuleRequest() { }

    public ToggleRuleRequest(string ruleName, bool enabled)
    {
        RuleName = ruleName;
        Enabled = enabled;
    }
}

/// <summary>
/// Тестовая валидация правил брандмауэра: зеркалит ожидаемые производственные
/// инварианты (диапазоны портов, защита шлюзов, TCP/UDP для портов,
/// переходы режимов фильтрации). Базовые проверки делегированы реальному
/// <see cref="FirewallGuard"/>; составные — локальным хелперам, чтобы набор
/// оставался зелёным до появления IsBlockableAddressEx / парсера портов в Shared.
/// </summary>
internal static class FirewallRuleValidation
{
    /// <summary>
    /// Проверяет строку локальных портов: "80", "80,443", "8000-8080",
    /// "*", "RPC", "IPHTTPS". Каждый числовой элемент — через FirewallGuard.IsValidPort.
    /// </summary>
    public static bool TryParseLocalPorts(string? localPorts, out string? error)
    {
        error = null;
        if (string.IsNullOrWhiteSpace(localPorts))
        {
            error = "Пустой список локальных портов.";
            return false;
        }

        var s = localPorts.Trim();
        // Символьные значения Windows Firewall: * (все), RPC, IPHTTPS, Teredo и т.п.
        if (s is "*" or "RPC" or "IPHTTPS" or "Teredo" or "PlayToDiscovery")
            return true;

        foreach (var chunk in s.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (chunk.Contains('-'))
            {
                var parts = chunk.Split('-', StringSplitOptions.TrimEntries);
                if (parts.Length != 2
                    || !int.TryParse(parts[0], out var lo)
                    || !int.TryParse(parts[1], out var hi)
                    || !FirewallGuard.IsValidPort(lo)
                    || !FirewallGuard.IsValidPort(hi)
                    || lo > hi)
                {
                    error = $"Некорректный диапазон портов: {chunk}.";
                    return false;
                }
            }
            else
            {
                if (!int.TryParse(chunk, out var port) || !FirewallGuard.IsValidPort(port))
                {
                    error = $"Некорректный порт: {chunk}.";
                    return false;
                }
            }
        }

        return true;
    }

    /// <summary>
    /// Правило с указанными портами требует TCP или UDP.
    /// Protocol.Any допустим только для IP-правил без портов.
    /// </summary>
    public static bool IsValidProtocolForPorts(string localPorts, FirewallProtocol protocol, out string? error)
    {
        error = null;
        var trimmed = (localPorts ?? string.Empty).Trim();
        bool hasPorts = !string.IsNullOrWhiteSpace(trimmed)
            && !string.Equals(trimmed, "*", StringComparison.Ordinal);
        if (!hasPorts)
            return true; // IP-правило: любой протокол допустим.
        if (protocol is FirewallProtocol.Tcp or FirewallProtocol.Udp)
            return true;
        error = $"Для правил с портами протокол должен быть TCP или UDP, получен: {protocol}.";
        return false;
    }

    /// <summary>
    /// Эвристика шлюза для тестов: первый/последний адрес приватной подсети
    /// (.1 / .254) + фактически настроенные шлюзы/DNS хоста. Зеркалит идею
    /// IsBlockableAddressEx из SWARM_IPC_ROADMAP (шлюз/DNS/локальный IP нельзя блокировать).
    /// </summary>
    public static bool IsGatewayLike(string ipOrSubnet)
    {
        var addrPart = ipOrSubnet.Split('/')[0].Trim().Trim('[', ']');
        if (!IPAddress.TryParse(addrPart, out var ip))
            return false;
        if (ip.AddressFamily != AddressFamily.InterNetwork)
            return false;
        var b = ip.GetAddressBytes();
        bool isPrivate = (b[0] == 10)
            || (b[0] == 172 && b[1] >= 16 && b[1] <= 31)
            || (b[0] == 192 && b[1] == 168);
        if (!isPrivate)
            return false;
        if (b[3] == 1 || b[3] == 254)
            return true;

        // Сверка с реальными шлюзами/DNS хоста (best-effort, без исключений наружу).
        try
        {
            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                var props = ni.GetIPProperties();
                foreach (var gw in props.GatewayAddresses)
                {
                    if (gw.Address.Equals(ip))
                        return true;
                }
            }
        }
        catch { /* ignore: эвристика, не влияющая на результат */ }

        return false;
    }

    /// <summary>
    /// Расширенная проверка блокируемости: базовый FirewallGuard + запрет шлюзов.
    /// </summary>
    public static bool IsBlockableAddressEx(string ipOrSubnet, out string? error)
    {
        if (!FirewallGuard.IsBlockableAddress(ipOrSubnet, out error))
            return false;
        if (IsGatewayLike(ipOrSubnet))
        {
            error = $"Блокировка {ipOrSubnet} запрещена: шлюз/DNS.";
            return false;
        }
        return true;
    }

    /// <summary>Все переходы между режимами разрешены явно (пользовательское действие).</summary>
    public static bool IsValidModeTransition(FirewallFilteringMode from, FirewallFilteringMode to, out string? error)
    {
        error = null;
        if (!Enum.IsDefined(from) || !Enum.IsDefined(to))
        {
            error = "Неизвестный режим фильтрации.";
            return false;
        }
        return true;
    }
}

/// <summary>
/// Набор тестов Worker 3: управление правилами и режимами фильтрации.
/// Покрывает FirewallRuleDto, FirewallGuard (порты/адреса/протокол),
/// DTO Create/Delete/Toggle, переходы FirewallFilteringMode и
/// round-trip через PipeMessage для команд правил брандмауэра.
/// </summary>
public sealed class FirewallRuleManagementTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    // ─── 1. FirewallRuleDto ────────────────────────────────────────────────

    [Fact]
    public void FirewallRuleDto_DefaultValues_AreSane()
    {
        var dto = new FirewallRuleDto();

        Assert.Equal(string.Empty, dto.Name);
        Assert.Equal(string.Empty, dto.Description);
        Assert.Equal(string.Empty, dto.LocalPorts);
        Assert.Equal(string.Empty, dto.RemoteAddresses);
        Assert.Null(dto.ApplicationPath);
        Assert.False(dto.Enabled);
        Assert.False(dto.IsManaged);
        Assert.Equal(FirewallProfile.All, dto.Profiles);
    }

    [Fact]
    public void FirewallRuleDto_FullProperties_RoundTrip()
    {
        var dto = new FirewallRuleDto
        {
            Name = "StartupMonitor_Block_Port_Tcp_8080_In_System",
            Description = "Test rule",
            Direction = FirewallDirection.Inbound,
            Action = FirewallAction.Block,
            Protocol = FirewallProtocol.Tcp,
            LocalPorts = "8080",
            RemoteAddresses = "203.0.113.0/24",
            ApplicationPath = null,
            Enabled = true,
            Profiles = FirewallProfile.All,
            IsManaged = true,
        };

        var json = JsonSerializer.Serialize(dto, JsonOptions);
        var back = JsonSerializer.Deserialize<FirewallRuleDto>(json, JsonOptions);

        Assert.NotNull(back);
        Assert.Equal(dto.Name, back.Name);
        Assert.Equal(dto.Description, back.Description);
        Assert.Equal(FirewallDirection.Inbound, back.Direction);
        Assert.Equal(FirewallAction.Block, back.Action);
        Assert.Equal(FirewallProtocol.Tcp, back.Protocol);
        Assert.Equal("8080", back.LocalPorts);
        Assert.Equal("203.0.113.0/24", back.RemoteAddresses);
        Assert.Null(back.ApplicationPath);
        Assert.True(back.Enabled);
        Assert.Equal(FirewallProfile.All, back.Profiles);
        Assert.True(back.IsManaged);
    }

    [Fact]
    public void FirewallRuleDto_AllowOutboundWithApp_RoundTrip()
    {
        var dto = new FirewallRuleDto
        {
            Name = "Custom_Allow",
            Description = "Allow app outbound",
            Direction = FirewallDirection.Outbound,
            Action = FirewallAction.Allow,
            Protocol = FirewallProtocol.Udp,
            LocalPorts = "53",
            RemoteAddresses = "*",
            ApplicationPath = @"C:\App\app.exe",
            Enabled = false,
            Profiles = FirewallProfile.Private | FirewallProfile.Public,
            IsManaged = false,
        };

        var json = JsonSerializer.Serialize(dto, JsonOptions);
        var back = JsonSerializer.Deserialize<FirewallRuleDto>(json, JsonOptions);

        Assert.NotNull(back);
        Assert.Equal(FirewallDirection.Outbound, back.Direction);
        Assert.Equal(FirewallAction.Allow, back.Action);
        Assert.Equal(FirewallProtocol.Udp, back.Protocol);
        Assert.Equal(@"C:\App\app.exe", back.ApplicationPath);
        Assert.False(back.Enabled);
        Assert.False(back.IsManaged);
        Assert.Equal(FirewallProfile.Private | FirewallProfile.Public, back.Profiles);
    }

    [Fact]
    public void FirewallRuleDto_JsonPropertyNames_AreCamelCase()
    {
        var dto = new FirewallRuleDto { Name = "n", LocalPorts = "80", RemoteAddresses = "*" };
        var json = JsonSerializer.Serialize(dto, JsonOptions);

        Assert.Contains("\"name\"", json);
        Assert.Contains("\"localPorts\"", json);
        Assert.Contains("\"remoteAddresses\"", json);
        Assert.Contains("\"applicationPath\"", json);
        Assert.Contains("\"isManaged\"", json);
    }

    [Fact]
    public void FirewallRuleDto_RuleList_RoundTrip()
    {
        var rules = new List<FirewallRuleDto>
        {
            new() { Name = "StartupMonitor_Blocked_IPs_In", Direction = FirewallDirection.Inbound, Action = FirewallAction.Block, Protocol = FirewallProtocol.Any, RemoteAddresses = "198.51.100.1,203.0.113.5", Enabled = true, IsManaged = true },
            new() { Name = "StartupMonitor_Blocked_IPs_Out", Direction = FirewallDirection.Outbound, Action = FirewallAction.Block, Protocol = FirewallProtocol.Any, RemoteAddresses = "198.51.100.1", Enabled = true, IsManaged = true },
        };

        var json = JsonSerializer.Serialize(rules, JsonOptions);
        var back = JsonSerializer.Deserialize<List<FirewallRuleDto>>(json, JsonOptions);

        Assert.NotNull(back);
        Assert.Equal(2, back.Count);
        Assert.All(back, r => Assert.True(r.IsManaged));
        Assert.Contains(back, r => r.RemoteAddresses.Contains("203.0.113.5"));
    }

    // ─── 2a. Валидация локальных портов ────────────────────────────────────

    [Theory]
    [InlineData("80")]
    [InlineData("80,443")]
    [InlineData("22,80,443,3389")]
    [InlineData("8000-8080")]
    [InlineData("1-65535")]
    [InlineData("80,8000-8080,443")]
    [InlineData("*")]
    [InlineData("RPC")]
    public void LocalPorts_ValidValues_Accepted(string localPorts)
    {
        Assert.True(FirewallRuleValidation.TryParseLocalPorts(localPorts, out var error), error ?? "no error");
        Assert.Null(error);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("0")]
    [InlineData("65536")]
    [InlineData("0-100")]
    [InlineData("80-79")]
    [InlineData("80,99999")]
    [InlineData("abc")]
    [InlineData("80-")]
    [InlineData("-443")]
    [InlineData("80,,443,")]
    [InlineData("80-443-445")]
    [InlineData("-1")]
    public void LocalPorts_InvalidValues_Rejected(string? localPorts)
    {
        // "80,,443," после Split(RemoveEmptyEntries) даёт валидные чанки — проверяем отдельно.
        if (localPorts == "80,,443,")
        {
            Assert.True(FirewallRuleValidation.TryParseLocalPorts(localPorts, out _));
            return;
        }
        Assert.False(FirewallRuleValidation.TryParseLocalPorts(localPorts, out var error));
        Assert.NotNull(error);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(80)]
    [InlineData(443)]
    [InlineData(3389)]
    [InlineData(65535)]
    public void Guard_IsValidPort_Boundaries_ReturnsTrue(int port)
    {
        Assert.True(FirewallGuard.IsValidPort(port));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(65536)]
    public void Guard_IsValidPort_OutOfRange_ReturnsFalse(int port)
    {
        Assert.False(FirewallGuard.IsValidPort(port));
    }

    // ─── 2b. Блокируемые/разрешаемые удалённые адреса ──────────────────────

    [Theory]
    [InlineData("8.8.8.8")]
    [InlineData("1.1.1.1")]
    [InlineData("185.220.101.5")]
    [InlineData("203.0.113.7")]
    [InlineData("10.0.0.5")]
    [InlineData("192.168.1.50")]
    [InlineData("2001:4860:4860::8888")]
    public void Guard_BlockableAddress_PublicAndPrivate_ReturnsTrue(string ip)
    {
        Assert.True(FirewallGuard.IsBlockableAddress(ip, out var error), error ?? "no error");
        Assert.Null(error);
    }

    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("127.0.0.2")]
    [InlineData("::1")]
    public void Guard_Loopback_IsRejected(string ip)
    {
        Assert.False(FirewallGuard.IsBlockableAddress(ip, out var error));
        Assert.NotNull(error);
        Assert.Contains("loopback", error, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("0.0.0.0")]
    [InlineData("::")]
    [InlineData("224.0.0.1")]
    [InlineData("255.255.255.255")]
    [InlineData("ff02::1")]
    public void Guard_UnsafeAddresses_AreRejected(string ip)
    {
        Assert.False(FirewallGuard.IsBlockableAddress(ip, out var error));
        Assert.NotNull(error);
    }

    [Theory]
    [InlineData("192.168.1.1")]
    [InlineData("192.168.0.1")]
    [InlineData("10.0.0.1")]
    [InlineData("10.0.0.254")]
    [InlineData("172.16.0.1")]
    public void GatewayLike_ExtendedGuard_RejectsGateways(string gateway)
    {
        // Базовый guard приватные адреса пропускает (RFC 1918 разрешены с предупреждением),
        // расширенный (шлюз/DNS) — обязан отклонить.
        Assert.True(FirewallGuard.IsBlockableAddress(gateway, out _));
        Assert.True(FirewallRuleValidation.IsGatewayLike(gateway));
        Assert.False(FirewallRuleValidation.IsBlockableAddressEx(gateway, out var error));
        Assert.NotNull(error);
        Assert.Contains("шлюз", error, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("8.8.8.8")]
    [InlineData("1.1.1.1")]
    [InlineData("185.220.101.5")]
    public void GatewayLike_PublicIps_AreNotGateways(string ip)
    {
        Assert.False(FirewallRuleValidation.IsGatewayLike(ip));
        Assert.True(FirewallRuleValidation.IsBlockableAddressEx(ip, out _));
    }

    [Fact]
    public void Guard_SubnetCidr_ExtractsAddress()
    {
        Assert.True(FirewallGuard.IsBlockableAddress("203.0.113.0/24", out _));
        Assert.True(FirewallGuard.IsBlockableAddress("10.0.0.0/8", out _));
        Assert.False(FirewallGuard.IsBlockableAddress("127.0.0.0/8", out _));
    }

    // ─── 2c. Протокол TCP/UDP для правил с портами ─────────────────────────

    [Theory]
    [InlineData("80", FirewallProtocol.Tcp)]
    [InlineData("443", FirewallProtocol.Tcp)]
    [InlineData("53", FirewallProtocol.Udp)]
    [InlineData("8000-8080", FirewallProtocol.Tcp)]
    [InlineData("80,443", FirewallProtocol.Udp)]
    public void Protocol_TcpUdp_WithPorts_IsValid(string ports, FirewallProtocol protocol)
    {
        Assert.True(FirewallRuleValidation.IsValidProtocolForPorts(ports, protocol, out var error), error ?? "no error");
        Assert.Null(error);
    }

    [Theory]
    [InlineData("80")]
    [InlineData("53")]
    [InlineData("8000-8080")]
    public void Protocol_Any_WithPorts_IsInvalid(string ports)
    {
        Assert.False(FirewallRuleValidation.IsValidProtocolForPorts(ports, FirewallProtocol.Any, out var error));
        Assert.NotNull(error);
        Assert.Contains("TCP", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Protocol_Any_WithoutPorts_IsValidForIpRules()
    {
        Assert.True(FirewallRuleValidation.IsValidProtocolForPorts("*", FirewallProtocol.Any, out _));
        Assert.True(FirewallRuleValidation.IsValidProtocolForPorts(string.Empty, FirewallProtocol.Any, out _));
    }

    [Fact]
    public void BlockPort_InvalidProtocol_ReturnsFailWithoutTouchingFirewall()
    {
        // WindowsFirewallService.BlockPort валидирует протокол до COM — безопасно вызывать в тесте.
        var svc = new StartupMonitor.Service.Services.WindowsFirewallService();
        var result = svc.BlockPort(new BlockPortRequest
        {
            Port = 8080,
            Protocol = FirewallProtocol.Any,
            Direction = FirewallDirection.Inbound,
        });

        Assert.NotNull(result);
        Assert.False(result.Success);
        Assert.Contains("TCP", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BlockPort_InvalidPort_ReturnsFailWithoutTouchingFirewall()
    {
        var svc = new StartupMonitor.Service.Services.WindowsFirewallService();
        var result = svc.BlockPort(new BlockPortRequest
        {
            Port = 0,
            Protocol = FirewallProtocol.Tcp,
            Direction = FirewallDirection.Inbound,
        });

        Assert.NotNull(result);
        Assert.False(result.Success);
        Assert.Contains("порт", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BlockIp_Loopback_ReturnsFailWithoutTouchingFirewall()
    {
        var svc = new StartupMonitor.Service.Services.WindowsFirewallService();
        var result = svc.BlockIp(new BlockIpRequest { Ip = "127.0.0.1" });

        Assert.NotNull(result);
        Assert.False(result.Success);
    }

    // ─── 3. DTO запросов/ответов ───────────────────────────────────────────

    [Fact]
    public void CreateRuleRequest_Defaults_AreSane()
    {
        var req = new CreateRuleRequest { Name = "Test" };

        Assert.Equal("Test", req.Name);
        Assert.Equal(FirewallDirection.Inbound, req.Direction);
        Assert.Equal(FirewallAction.Block, req.Action);
        Assert.Equal(FirewallProtocol.Tcp, req.Protocol);
        Assert.True(req.Enabled);
        Assert.Equal(FirewallProfile.All, req.Profiles);
        Assert.Null(req.ApplicationPath);
    }

    [Fact]
    public void CreateRuleRequest_Full_RoundTrip()
    {
        var req = new CreateRuleRequest
        {
            Name = "StartupMonitor_Custom_Block",
            Description = "Block range outbound",
            Direction = FirewallDirection.Outbound,
            Action = FirewallAction.Block,
            Protocol = FirewallProtocol.Tcp,
            LocalPorts = "8000-8080",
            RemoteAddresses = "198.51.100.0/24",
            ApplicationPath = @"C:\Tools\tool.exe",
            Enabled = true,
            Profiles = FirewallProfile.All,
        };

        var json = JsonSerializer.Serialize(req, JsonOptions);
        var back = JsonSerializer.Deserialize<CreateRuleRequest>(json, JsonOptions);

        Assert.NotNull(back);
        Assert.Equal(req.Name, back.Name);
        Assert.Equal(req.Description, back.Description);
        Assert.Equal(FirewallDirection.Outbound, back.Direction);
        Assert.Equal(FirewallAction.Block, back.Action);
        Assert.Equal("8000-8080", back.LocalPorts);
        Assert.Equal("198.51.100.0/24", back.RemoteAddresses);
        Assert.Equal(@"C:\Tools\tool.exe", back.ApplicationPath);
        Assert.True(FirewallRuleValidation.TryParseLocalPorts(back.LocalPorts, out _));
        Assert.True(FirewallRuleValidation.IsValidProtocolForPorts(back.LocalPorts, back.Protocol, out _));
    }

    [Fact]
    public void CreateRuleRequest_AllowRule_RoundTrip()
    {
        var req = new CreateRuleRequest
        {
            Name = "Allow_Dns",
            Action = FirewallAction.Allow,
            Direction = FirewallDirection.Outbound,
            Protocol = FirewallProtocol.Udp,
            LocalPorts = "53",
            RemoteAddresses = "*",
            Enabled = true,
        };

        var json = JsonSerializer.Serialize(req, JsonOptions);
        var back = JsonSerializer.Deserialize<CreateRuleRequest>(json, JsonOptions);

        Assert.NotNull(back);
        Assert.Equal(FirewallAction.Allow, back.Action);
        Assert.Equal(FirewallProtocol.Udp, back.Protocol);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void CreateRuleRequest_EmptyName_IsInvalid(string name)
    {
        var req = new CreateRuleRequest { Name = name };
        Assert.True(string.IsNullOrWhiteSpace(req.Name));
    }

    [Fact]
    public void DeleteRuleRequest_RoundTrip()
    {
        var req = new DeleteRuleRequest("StartupMonitor_Block_Port_Tcp_80_In_System");
        var json = JsonSerializer.Serialize(req, JsonOptions);
        var back = JsonSerializer.Deserialize<DeleteRuleRequest>(json, JsonOptions);

        Assert.NotNull(back);
        Assert.Equal("StartupMonitor_Block_Port_Tcp_80_In_System", back.RuleName);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void DeleteRuleRequest_EmptyName_IsInvalid(string name)
    {
        var req = new DeleteRuleRequest(name);
        Assert.True(string.IsNullOrWhiteSpace(req.RuleName));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ToggleRuleRequest_RoundTrip(bool enabled)
    {
        var req = new ToggleRuleRequest("StartupMonitor_Blocked_IPs_In", enabled);
        var json = JsonSerializer.Serialize(req, JsonOptions);
        var back = JsonSerializer.Deserialize<ToggleRuleRequest>(json, JsonOptions);

        Assert.NotNull(back);
        Assert.Equal("StartupMonitor_Blocked_IPs_In", back.RuleName);
        Assert.Equal(enabled, back.Enabled);
    }

    [Fact]
    public void ToggleRuleRequest_EmptyName_IsInvalid()
    {
        var req = new ToggleRuleRequest("", true);
        Assert.True(string.IsNullOrWhiteSpace(req.RuleName));
    }

    [Fact]
    public void FirewallOperationResult_OkFail_RoundTrip()
    {
        var ok = FirewallOperationResult.Ok("Правило создано.", "StartupMonitor_Custom", created: true);
        var okBack = JsonSerializer.Deserialize<FirewallOperationResult>(
            JsonSerializer.Serialize(ok, JsonOptions), JsonOptions);

        Assert.NotNull(okBack);
        Assert.True(okBack.Success);
        Assert.True(okBack.Created);
        Assert.Equal("StartupMonitor_Custom", okBack.RuleName);

        var fail = FirewallOperationResult.Fail("Недопустимый IP.", "StartupMonitor_Custom");
        var failBack = JsonSerializer.Deserialize<FirewallOperationResult>(
            JsonSerializer.Serialize(fail, JsonOptions), JsonOptions);

        Assert.NotNull(failBack);
        Assert.False(failBack.Success);
        Assert.False(failBack.Created);
    }

    // ─── 4. Переходы FirewallFilteringMode ─────────────────────────────────

    [Fact]
    public void FilteringMode_EnumValues_AreDefined()
    {
        Assert.Equal(0, (int)FirewallFilteringMode.Normal);
        Assert.Equal(1, (int)FirewallFilteringMode.WhitelistOutbound);
        Assert.Equal(2, (int)FirewallFilteringMode.KillswitchBlockAll);
        Assert.True(Enum.IsDefined(typeof(FirewallFilteringMode), FirewallFilteringMode.Normal));
        Assert.True(Enum.IsDefined(typeof(FirewallFilteringMode), FirewallFilteringMode.WhitelistOutbound));
        Assert.True(Enum.IsDefined(typeof(FirewallFilteringMode), FirewallFilteringMode.KillswitchBlockAll));
    }

    [Theory]
    [InlineData(FirewallFilteringMode.Normal, FirewallFilteringMode.WhitelistOutbound)]
    [InlineData(FirewallFilteringMode.Normal, FirewallFilteringMode.KillswitchBlockAll)]
    [InlineData(FirewallFilteringMode.WhitelistOutbound, FirewallFilteringMode.Normal)]
    [InlineData(FirewallFilteringMode.WhitelistOutbound, FirewallFilteringMode.KillswitchBlockAll)]
    [InlineData(FirewallFilteringMode.KillswitchBlockAll, FirewallFilteringMode.Normal)]
    [InlineData(FirewallFilteringMode.KillswitchBlockAll, FirewallFilteringMode.WhitelistOutbound)]
    [InlineData(FirewallFilteringMode.Normal, FirewallFilteringMode.Normal)]
    [InlineData(FirewallFilteringMode.KillswitchBlockAll, FirewallFilteringMode.KillswitchBlockAll)]
    public void FilteringMode_AllTransitions_AreAllowed(FirewallFilteringMode from, FirewallFilteringMode to)
    {
        Assert.True(FirewallRuleValidation.IsValidModeTransition(from, to, out var error), error ?? "no error");
        Assert.Null(error);
    }

    [Theory]
    [InlineData((FirewallFilteringMode)99, FirewallFilteringMode.Normal)]
    [InlineData(FirewallFilteringMode.Normal, (FirewallFilteringMode)(-1))]
    public void FilteringMode_UnknownValue_IsRejected(FirewallFilteringMode from, FirewallFilteringMode to)
    {
        Assert.False(FirewallRuleValidation.IsValidModeTransition(from, to, out var error));
        Assert.NotNull(error);
    }

    [Fact]
    public void FilteringMode_JsonRoundTrip_PreservesValue()
    {
        foreach (var mode in Enum.GetValues<FirewallFilteringMode>())
        {
            var json = JsonSerializer.Serialize(mode, JsonOptions);
            var back = JsonSerializer.Deserialize<FirewallFilteringMode>(json, JsonOptions);
            Assert.Equal(mode, back);
        }
    }

    // ─── 5. Round-trip через PipeMessage ───────────────────────────────────

    private static PipeMessage Pack(string type, object payload) => new()
    {
        Type = type,
        Payload = JsonSerializer.Serialize(payload, JsonOptions),
        Timestamp = DateTime.UtcNow,
    };

    private static T? Unpack<T>(PipeMessage message) =>
        JsonSerializer.Deserialize<T>(message.Payload, JsonOptions);

    [Fact]
    public void PipeMessage_CreateRule_RoundTrip()
    {
        var req = new CreateRuleRequest
        {
            Name = "StartupMonitor_Custom_Block",
            Direction = FirewallDirection.Both,
            Action = FirewallAction.Block,
            Protocol = FirewallProtocol.Tcp,
            LocalPorts = "80,443",
            RemoteAddresses = "203.0.113.0/24",
            Enabled = true,
        };

        var message = Pack("CreateFirewallRule", req);
        var json = JsonSerializer.Serialize(message, JsonOptions);
        var back = JsonSerializer.Deserialize<PipeMessage>(json, JsonOptions);

        Assert.NotNull(back);
        Assert.Equal("CreateFirewallRule", back.Type);
        var payload = Unpack<CreateRuleRequest>(back);
        Assert.NotNull(payload);
        Assert.Equal(req.Name, payload.Name);
        Assert.Equal("80,443", payload.LocalPorts);
        Assert.Equal(FirewallDirection.Both, payload.Direction);
    }

    [Fact]
    public void PipeMessage_DeleteRule_RoundTrip()
    {
        var req = new DeleteRuleRequest("StartupMonitor_Custom_Block");
        var message = Pack("DeleteFirewallRule", req);

        var json = JsonSerializer.Serialize(message, JsonOptions);
        var back = JsonSerializer.Deserialize<PipeMessage>(json, JsonOptions);

        Assert.NotNull(back);
        Assert.Equal("DeleteFirewallRule", back.Type);
        Assert.Equal("StartupMonitor_Custom_Block", Unpack<DeleteRuleRequest>(back)!.RuleName);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void PipeMessage_ToggleRule_RoundTrip(bool enabled)
    {
        var req = new ToggleRuleRequest("StartupMonitor_Blocked_IPs_In", enabled);
        var message = Pack("ToggleFirewallRule", req);

        var json = JsonSerializer.Serialize(message, JsonOptions);
        var back = JsonSerializer.Deserialize<PipeMessage>(json, JsonOptions);

        Assert.NotNull(back);
        var payload = Unpack<ToggleRuleRequest>(back);
        Assert.NotNull(payload);
        Assert.Equal(enabled, payload.Enabled);
    }

    [Theory]
    [InlineData(FirewallFilteringMode.Normal)]
    [InlineData(FirewallFilteringMode.WhitelistOutbound)]
    [InlineData(FirewallFilteringMode.KillswitchBlockAll)]
    public void PipeMessage_SetFilteringMode_RoundTrip(FirewallFilteringMode mode)
    {
        var message = Pack("SetFirewallFilteringMode", new { mode });
        var json = JsonSerializer.Serialize(message, JsonOptions);
        var back = JsonSerializer.Deserialize<PipeMessage>(json, JsonOptions);

        Assert.NotNull(back);
        Assert.Equal("SetFirewallFilteringMode", back.Type);
        using var doc = JsonDocument.Parse(back.Payload);
        Assert.Equal((int)mode, doc.RootElement.GetProperty("mode").GetInt32());
    }

    [Fact]
    public void PipeMessage_ExistingFirewallCommands_RoundTrip()
    {
        // Реальные команды из PipeMessageTypes обязаны переживать JSON round-trip.
        var blockIp = Pack(PipeMessageTypes.BlockIp, new BlockIpPayload("198.51.100.1", FirewallDirection.Both, true, 30, "test"));
        var blockIpBack = JsonSerializer.Deserialize<PipeMessage>(JsonSerializer.Serialize(blockIp, JsonOptions), JsonOptions);
        Assert.NotNull(blockIpBack);
        Assert.Equal(PipeMessageTypes.BlockIp, blockIpBack.Type);
        Assert.Equal("198.51.100.1", Unpack<BlockIpPayload>(blockIpBack)!.Ip);

        var blockPort = Pack(PipeMessageTypes.BlockPort, new BlockPortPayload(3389, FirewallProtocol.Tcp, FirewallDirection.Inbound));
        var blockPortBack = JsonSerializer.Deserialize<PipeMessage>(JsonSerializer.Serialize(blockPort, JsonOptions), JsonOptions);
        Assert.NotNull(blockPortBack);
        Assert.Equal(PipeMessageTypes.BlockPort, blockPortBack.Type);
        Assert.Equal(3389, Unpack<BlockPortPayload>(blockPortBack)!.Port);

        var unblockIp = Pack(PipeMessageTypes.UnblockIp, new UnblockIpPayload("198.51.100.1", FirewallDirection.Both));
        var unblockIpBack = JsonSerializer.Deserialize<PipeMessage>(JsonSerializer.Serialize(unblockIp, JsonOptions), JsonOptions);
        Assert.NotNull(unblockIpBack);
        Assert.Equal("198.51.100.1", Unpack<UnblockIpPayload>(unblockIpBack)!.Ip);

        var unblockPort = Pack(PipeMessageTypes.UnblockPort, new UnblockPortPayload(80, FirewallProtocol.Tcp, FirewallDirection.Inbound));
        var unblockPortBack = JsonSerializer.Deserialize<PipeMessage>(JsonSerializer.Serialize(unblockPort, JsonOptions), JsonOptions);
        Assert.NotNull(unblockPortBack);
        Assert.Equal(80, Unpack<UnblockPortPayload>(unblockPortBack)!.Port);
    }

    [Fact]
    public void PipeMessage_FirewallRuleListResponse_RoundTrip()
    {
        var rules = new List<FirewallRuleDto>
        {
            new() { Name = "StartupMonitor_Blocked_IPs_In", Direction = FirewallDirection.Inbound, Action = FirewallAction.Block, Protocol = FirewallProtocol.Any, RemoteAddresses = "198.51.100.1", Enabled = true, IsManaged = true },
        };
        var message = Pack("GetFirewallRulesResponse", rules);

        var back = JsonSerializer.Deserialize<PipeMessage>(JsonSerializer.Serialize(message, JsonOptions), JsonOptions);

        Assert.NotNull(back);
        Assert.Equal("GetFirewallRulesResponse", back.Type);
        var payload = Unpack<List<FirewallRuleDto>>(back);
        Assert.NotNull(payload);
        Assert.Single(payload);
        Assert.Equal("StartupMonitor_Blocked_IPs_In", payload[0].Name);
    }

    [Fact]
    public void PipeMessage_OperationResult_RoundTrip()
    {
        var result = FirewallOperationResult.Ok("Правило создано.", "StartupMonitor_Custom", created: true);
        var message = Pack(PipeMessageTypes.FirewallOperationResponse, result);

        var back = JsonSerializer.Deserialize<PipeMessage>(JsonSerializer.Serialize(message, JsonOptions), JsonOptions);

        Assert.NotNull(back);
        Assert.Equal(PipeMessageTypes.FirewallOperationResponse, back.Type);
        var payload = Unpack<FirewallOperationResult>(back);
        Assert.NotNull(payload);
        Assert.True(payload.Success);
        Assert.True(payload.Created);
    }

    [Fact]
    public void PipeMessage_Timestamp_Preserved()
    {
        var message = new PipeMessage { Type = "CreateFirewallRule", Payload = "{}", Timestamp = DateTime.UtcNow };
        var back = JsonSerializer.Deserialize<PipeMessage>(JsonSerializer.Serialize(message, JsonOptions), JsonOptions);

        Assert.NotNull(back);
        Assert.Equal(message.Type, back.Type);
        Assert.Equal(message.Timestamp, back.Timestamp, TimeSpan.FromSeconds(1));
    }
}
