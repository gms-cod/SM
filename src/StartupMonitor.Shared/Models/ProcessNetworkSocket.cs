using System;
using System.Net;

namespace StartupMonitor.Shared.Models;

/// <summary>
/// Информация об активном сетевом сокете (соединении) процесса Windows (TCP / UDP).
/// </summary>
public class ProcessNetworkSocket
{
    /// <summary>PID процесса-владельца.</summary>
    public int ProcessId { get; set; }

    /// <summary>Имя процесса (если известно).</summary>
    public string ProcessName { get; set; } = string.Empty;

    /// <summary>Протокол передачи данных ("TCP", "TCPv6", "UDP", "UDPv6").</summary>
    public string Protocol { get; set; } = "TCP";

    /// <summary>Локальный IP-адрес.</summary>
    public string LocalAddress { get; set; } = string.Empty;

    /// <summary>Локальный порт сокета.</summary>
    public int LocalPort { get; set; }

    /// <summary>Удалённый IP-адрес (для прослушиваемых сокетов или UDP может быть "*" или "0.0.0.0").</summary>
    public string RemoteAddress { get; set; } = string.Empty;

    /// <summary>Удалённый порт сокета.</summary>
    public int RemotePort { get; set; }

    /// <summary>
    /// Состояние соединения (ESTABLISHED, LISTENING, TIME_WAIT, CLOSE_WAIT, SYN_SENT, BOUND и др.).
    /// </summary>
    public string State { get; set; } = "ESTABLISHED";

    /// <summary>
    /// Флаг локального / серого IP-адреса (RFC 1918 / Loopback).
    /// </summary>
    public bool IsRemotePrivate { get; set; }

    /// <summary>Локальная конечная точка (IP:Порт).</summary>
    public string LocalEndPoint => $"{LocalAddress}:{LocalPort}";

    /// <summary>Удалённая конечная точка (IP:Порт).</summary>
    public string RemoteEndPoint
    {
        get
        {
            if (string.IsNullOrEmpty(RemoteAddress) || RemoteAddress == "*" || RemoteAddress == "0.0.0.0" || RemoteAddress == "::")
                return "*:*";
            return RemotePort > 0 ? $"{RemoteAddress}:{RemotePort}" : RemoteAddress;
        }
    }

    /// <summary>
    /// Определяет, является ли IP-адрес частным/локальным (Loopback, Link-Local, RFC 1918).
    /// </summary>
    public static bool CheckIsPrivateAddress(string ipStr)
    {
        if (string.IsNullOrWhiteSpace(ipStr) || ipStr == "*" || ipStr == "0.0.0.0" || ipStr == "::")
            return true;

        if (!IPAddress.TryParse(ipStr, out var ip))
            return false;

        if (IPAddress.IsLoopback(ip))
            return true;

        if (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
        {
            byte[] bytes = ip.GetAddressBytes();
            // 10.0.0.0/8
            if (bytes[0] == 10) return true;
            // 172.16.0.0/12
            if (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31) return true;
            // 192.168.0.0/16
            if (bytes[0] == 192 && bytes[1] == 168) return true;
            // 169.254.0.0/16 (APIPA)
            if (bytes[0] == 169 && bytes[1] == 254) return true;
        }
        else if (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6)
        {
            if (ip.IsIPv6LinkLocal || ip.IsIPv6SiteLocal)
                return true;
        }

        return false;
    }
}

