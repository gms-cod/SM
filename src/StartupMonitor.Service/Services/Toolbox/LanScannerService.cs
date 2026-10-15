using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using StartupMonitor.Shared.Models.Toolbox;

namespace StartupMonitor.Service.Services.Toolbox;

/// <summary>
/// Быстрый сканер устройств локальной сети (Wi-Fi / Ethernet) через Win32 SendARP API.
/// Обнаруживает все подключенные к роутеру устройства за 1-2 секунды.
/// </summary>
public class LanScannerService
{
    [DllImport("iphlpapi.dll", ExactSpelling = true)]
    private static extern int SendARP(uint destIp, uint srcIp, byte[] pMacAddr, ref uint pdwPhysAddrLen);

    /// <summary>
    /// Сканирует локальную сеть и возвращает список активных устройств.
    /// </summary>
    public Task<List<LanDeviceDto>> ScanLocalNetworkAsync(CancellationToken ct = default) => ScanLanAsync(ct);

    /// <summary>
    /// Сканирует локальную сеть и возвращает список активных устройств.
    /// </summary>
    public async Task<List<LanDeviceDto>> ScanLanAsync(CancellationToken ct = default)
    {
        var devices = new ConcurrentBag<LanDeviceDto>();

        // 1. Находим активный локальный IPv4 адаптер и шлюз
        IPAddress? localIp = null;
        IPAddress? gatewayIp = null;
        IPAddress? subnetMask = null;

        foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (ni.OperationalStatus != OperationalStatus.Up ||
                ni.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                continue;

            var ipProps = ni.GetIPProperties();
            var gw = ipProps.GatewayAddresses.FirstOrDefault(g => g.Address.AddressFamily == AddressFamily.InterNetwork);
            if (gw != null)
            {
                gatewayIp = gw.Address;
                var unicast = ipProps.UnicastAddresses.FirstOrDefault(u => u.Address.AddressFamily == AddressFamily.InterNetwork);
                if (unicast != null)
                {
                    localIp = unicast.Address;
                    subnetMask = unicast.IPv4Mask;
                    break;
                }
            }
        }

        if (localIp == null)
        {
            return [];
        }

        // Если маска не определена — считаем /24
        byte[] ipBytes = localIp.GetAddressBytes();
        byte[] baseIp = [ipBytes[0], ipBytes[1], ipBytes[2], 0];

        // 2. Параллельный опрос всех 254 хостов
        var tasks = new List<Task>(254);
        using var limiter = new SemaphoreSlim(64);

        for (int i = 1; i <= 254; i++)
        {
            int hostId = i;
            tasks.Add(Task.Run(async () =>
            {
                await limiter.WaitAsync(ct);
                try
                {
                    if (ct.IsCancellationRequested) return;

                    byte[] targetIpBytes = [baseIp[0], baseIp[1], baseIp[2], (byte)hostId];
                    var targetIp = new IPAddress(targetIpBytes);
                    uint destIpInt = BitConverter.ToUInt32(targetIpBytes, 0);

                    byte[] mac = new byte[6];
                    uint macLen = 6;

                    bool isCurrent = targetIp.Equals(localIp);
                    bool isGw = gatewayIp != null && targetIp.Equals(gatewayIp);

                    if (isCurrent)
                    {
                        devices.Add(new LanDeviceDto
                        {
                            IpAddress = targetIp.ToString(),
                            MacAddress = GetLocalMacAddress(),
                            HostName = Environment.MachineName,
                            Vendor = "Этот компьютер",
                            IsCurrentDevice = true,
                            IsGateway = false
                        });
                        return;
                    }

                    int res = SendARP(destIpInt, 0, mac, ref macLen);
                    if (res == 0 && macLen == 6)
                    {
                        string macStr = string.Format("{0:X2}:{1:X2}:{2:X2}:{3:X2}:{4:X2}:{5:X2}",
                            mac[0], mac[1], mac[2], mac[3], mac[4], mac[5]);

                        string hostName = string.Empty;
                        try
                        {
                            var entry = await Dns.GetHostEntryAsync(targetIp).WaitAsync(TimeSpan.FromMilliseconds(200), ct);
                            hostName = entry.HostName;
                        }
                        catch { }

                        devices.Add(new LanDeviceDto
                        {
                            IpAddress = targetIp.ToString(),
                            MacAddress = macStr,
                            HostName = hostName,
                            Vendor = isGw ? "Шлюз роутера / Wi-Fi Точка" : ResolveVendor(mac),
                            IsCurrentDevice = false,
                            IsGateway = isGw
                        });
                    }
                }
                catch { }
                finally
                {
                    limiter.Release();
                }
            }, ct));
        }

        await Task.WhenAll(tasks);

        return devices.OrderBy(d => d.IsCurrentDevice ? 0 : d.IsGateway ? 1 : 2)
                      .ThenBy(d => IPAddress.Parse(d.IpAddress).GetAddressBytes()[3])
                      .ToList();
    }

    private static string GetLocalMacAddress()
    {
        try
        {
            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus == OperationalStatus.Up &&
                    ni.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                {
                    var mac = ni.GetPhysicalAddress().GetAddressBytes();
                    if (mac.Length == 6)
                    {
                        return string.Format("{0:X2}:{1:X2}:{2:X2}:{3:X2}:{4:X2}:{5:X2}",
                            mac[0], mac[1], mac[2], mac[3], mac[4], mac[5]);
                    }
                }
            }
        }
        catch { }
        return "00:00:00:00:00:00";
    }

    private static string ResolveVendor(byte[] mac)
    {
        // Простая базовая классификация популярных OUI
        if (mac.Length < 3) return "Сетевое устройство";
        string prefix = $"{mac[0]:X2}:{mac[1]:X2}:{mac[2]:X2}";

        // Примеры популярных вендоров
        if (prefix.StartsWith("BC:D0:74") || prefix.StartsWith("00:1A:11")) return "Google";
        if (prefix.StartsWith("00:50:56") || prefix.StartsWith("00:0C:29")) return "VMware";
        if (prefix.StartsWith("08:00:27")) return "VirtualBox";
        if (prefix.StartsWith("DC:A6:32") || prefix.StartsWith("B8:27:EB")) return "Raspberry Pi";
        if (prefix.StartsWith("F4:34:F0") || prefix.StartsWith("AC:BC:32")) return "Apple";
        if (prefix.StartsWith("70:85:C2") || prefix.StartsWith("48:5D:60")) return "Xiaomi";
        if (prefix.StartsWith("50:C7:BF") || prefix.StartsWith("E8:48:B8")) return "TP-Link";
        if (prefix.StartsWith("D8:07:B6") || prefix.StartsWith("A4:2B:B0")) return "Keenetic / Zyxel";
        if (prefix.StartsWith("00:1E:10") || prefix.StartsWith("60:38:E0")) return "Asus";

        return "Сетевое устройство";
    }
}
