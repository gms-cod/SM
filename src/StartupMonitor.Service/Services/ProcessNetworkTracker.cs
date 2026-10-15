using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Runtime.InteropServices;
using StartupMonitor.Shared.Models;
using StartupMonitor.Shared.Diagnostics;

namespace StartupMonitor.Service.Services;

/// <summary>
/// Высокопроизводительный трекер сетевой активности процессов Windows через IP Helper API (GetExtendedTcpTable + GetPerTcpConnectionEStats).
/// Высокопроизводительный трекер сетевой активности процессов Windows через IP Helper API (GetExtendedTcpTable + GetPerTcpConnectionEStats + GetExtendedUdpTable).
/// Не требует сторонних драйверов или тяжелых библиотек трассировки.
/// </summary>
public class ProcessNetworkTracker
{
    private const int AF_INET = 2;
    private const int AF_INET6 = 23;
    private const int TCP_TABLE_OWNER_PID_ALL = 5;
    private const int UDP_TABLE_OWNER_PID = 1;
    private const uint MIB_TCP_STATE_ESTABLISHED = 5;
    private const int TcpConnectionEstatsData = 1;

    // Кэш байт по сокетам: "ip:localPort-remotePort" -> суммарно переданные/принятые байты
    private Dictionary<string, ulong> _prevSocketBytes = new(128);
    private DateTime _lastSampleTime = DateTime.UtcNow;

    // Предвыделенный байт для активации сбора статистики (BOOLEAN EnableCollection = 1)
    private readonly byte[] _rwEnableCollection = [1];
    private readonly int _rodSize = Marshal.SizeOf<TCP_ESTATS_DATA_ROD_v0>();

    /// <summary>
    /// Снимает показания сети и рассчитывает скорость передачи (Байт/с) для каждого процесса, суммарно по системе и количество сокетов.
    /// </summary>
    public (Dictionary<int, long> ProcessNetworkBytesPerSec, long TotalNetworkBytesPerSec, Dictionary<int, int> ProcessSocketCounts) CaptureNetworkBandwidth()
    {
        using var _prof = ExecutionProfiler.Measure("Service.Network.CaptureBandwidth");
        var now = DateTime.UtcNow;
        var elapsedSec = (now - _lastSampleTime).TotalSeconds;
        if (elapsedSec < 0.05) elapsedSec = 0.05;

        var currentSocketBytes = new Dictionary<string, ulong>(Math.Max(64, _prevSocketBytes.Count));
        var accumulatedBytesByPid = new Dictionary<int, ulong>(64);
        var socketCounts = new Dictionary<int, int>(128);

        // 1. IPv4 TCP соединения
        CollectIpv4Sockets(currentSocketBytes, accumulatedBytesByPid, socketCounts);

        // 2. IPv6 TCP соединения
        CollectIpv6Sockets(currentSocketBytes, accumulatedBytesByPid, socketCounts);

        // 3. UDP соединения
        CollectUdpPids(socketCounts);

        // 4. Расчет скорости в Байт/с
        var processSpeeds = new Dictionary<int, long>(accumulatedBytesByPid.Count);
        long totalSpeed = 0;

        foreach (var (pid, bytesDelta) in accumulatedBytesByPid)
        {
            if (bytesDelta > 0)
            {
                long speed = (long)(bytesDelta / elapsedSec);
                processSpeeds[pid] = speed;
                totalSpeed += speed;
            }
        }

        _prevSocketBytes = currentSocketBytes;
        _lastSampleTime = now;

        return (processSpeeds, totalSpeed, socketCounts);
    }

    /// <summary>CollectIpv4Sockets: Собирает метрики и информацию Ipv4Sockets.</summary>
    /// <param name="currentSocketBytes">Входные данные: значение параметра currentSocketBytes (Dictionary<string, ulong>).</param>
    /// <param name="accumulatedBytesByPid">Входные данные: значение параметра accumulatedBytesByPid (Dictionary<int, ulong>).</param>
    /// <param name="socketCounts">Входные данные: значение параметра socketCounts (Dictionary<int, int>).</param>
    private void CollectIpv4Sockets(Dictionary<string, ulong> currentSocketBytes, Dictionary<int, ulong> accumulatedBytesByPid, Dictionary<int, int> socketCounts)
    {
        int size = 0;
        GetExtendedTcpTable(IntPtr.Zero, ref size, false, AF_INET, TCP_TABLE_OWNER_PID_ALL, 0);
        if (size <= 0) return;

        IntPtr buffer = Marshal.AllocHGlobal(size);
        IntPtr rodBuffer = Marshal.AllocHGlobal(_rodSize);

        try
        {
            uint res = GetExtendedTcpTable(buffer, ref size, false, AF_INET, TCP_TABLE_OWNER_PID_ALL, 0);
            if (res != 0) return;

            int numEntries = Marshal.ReadInt32(buffer);
            IntPtr rowPtr = IntPtr.Add(buffer, 4);
            int rowSize = Marshal.SizeOf<MIB_TCPROW_OWNER_PID>();

            for (int i = 0; i < numEntries; i++)
            {
                var row = Marshal.PtrToStructure<MIB_TCPROW_OWNER_PID>(IntPtr.Add(rowPtr, i * rowSize));
                int pid = (int)row.dwOwningPid;
                if (pid <= 0) continue;
                socketCounts[pid] = socketCounts.GetValueOrDefault(pid) + 1;

                if (row.dwState != MIB_TCP_STATE_ESTABLISHED) continue;

                var tcpRow = new MIB_TCPROW
                {
                    dwState = row.dwState,
                    dwLocalAddr = row.dwLocalAddr,
                    dwLocalPort = row.dwLocalPort,
                    dwRemoteAddr = row.dwRemoteAddr,
                    dwRemotePort = row.dwRemotePort
                };

                // Включаем сбор статистики (идемпотентно и сверхбыстро в драйвере ядра)
                SetPerTcpConnectionEStats(ref tcpRow, TcpConnectionEstatsData, _rwEnableCollection, 0, _rwEnableCollection.Length, 0);

                // Читаем накопленные байты
                uint getRes = GetPerTcpConnectionEStats(ref tcpRow, TcpConnectionEstatsData, IntPtr.Zero, 0, 0, IntPtr.Zero, 0, 0, rodBuffer, 0, _rodSize);
                if (getRes == 0)
                {
                    var rod = Marshal.PtrToStructure<TCP_ESTATS_DATA_ROD_v0>(rodBuffer);
                    ulong totalBytes = rod.DataBytesIn + rod.DataBytesOut;

                    string key = $"4:{row.dwLocalAddr}:{row.dwLocalPort}-{row.dwRemoteAddr}:{row.dwRemotePort}";
                    currentSocketBytes[key] = totalBytes;

                    if (_prevSocketBytes.TryGetValue(key, out var oldTotal))
                    {
                        if (totalBytes > oldTotal)
                        {
                            ulong delta = totalBytes - oldTotal;
                            accumulatedBytesByPid[pid] = accumulatedBytesByPid.GetValueOrDefault(pid) + delta;
                        }
                    }
                }
            }
        }
        catch { }
        finally
        {
            Marshal.FreeHGlobal(rodBuffer);
            Marshal.FreeHGlobal(buffer);
        }
    }

    /// <summary>CollectIpv6Sockets: Собирает метрики и информацию Ipv6Sockets.</summary>
    /// <param name="currentSocketBytes">Входные данные: значение параметра currentSocketBytes (Dictionary<string, ulong>).</param>
    /// <param name="accumulatedBytesByPid">Входные данные: значение параметра accumulatedBytesByPid (Dictionary<int, ulong>).</param>
    /// <param name="socketCounts">Входные данные: значение параметра socketCounts (Dictionary<int, int>).</param>
    private void CollectIpv6Sockets(Dictionary<string, ulong> currentSocketBytes, Dictionary<int, ulong> accumulatedBytesByPid, Dictionary<int, int> socketCounts)
    {
        int size = 0;
        GetExtendedTcpTable(IntPtr.Zero, ref size, false, AF_INET6, TCP_TABLE_OWNER_PID_ALL, 0);
        if (size <= 0) return;

        IntPtr buffer = Marshal.AllocHGlobal(size);
        IntPtr rodBuffer = Marshal.AllocHGlobal(_rodSize);

        try
        {
            uint res = GetExtendedTcpTable(buffer, ref size, false, AF_INET6, TCP_TABLE_OWNER_PID_ALL, 0);
            if (res != 0) return;

            int numEntries = Marshal.ReadInt32(buffer);
            IntPtr rowPtr = IntPtr.Add(buffer, 4);
            int rowSize = Marshal.SizeOf<MIB_TCP6ROW_OWNER_PID>();

            for (int i = 0; i < numEntries; i++)
            {
                var row = Marshal.PtrToStructure<MIB_TCP6ROW_OWNER_PID>(IntPtr.Add(rowPtr, i * rowSize));
                int pid = (int)row.dwOwningPid;
                if (pid <= 0) continue;
                socketCounts[pid] = socketCounts.GetValueOrDefault(pid) + 1;

                if (row.dwState != MIB_TCP_STATE_ESTABLISHED) continue;

                var tcpRow = new MIB_TCP6ROW
                {
                    State = row.dwState,
                    LocalAddr = row.ucLocalAddr,
                    dwLocalScopeId = row.dwLocalScopeId,
                    dwLocalPort = row.dwLocalPort,
                    RemoteAddr = row.ucRemoteAddr,
                    dwRemoteScopeId = row.dwRemoteScopeId,
                    dwRemotePort = row.dwRemotePort
                };

                SetPerTcp6ConnectionEStats(ref tcpRow, TcpConnectionEstatsData, _rwEnableCollection, 0, _rwEnableCollection.Length, 0);

                uint getRes = GetPerTcp6ConnectionEStats(ref tcpRow, TcpConnectionEstatsData, IntPtr.Zero, 0, 0, IntPtr.Zero, 0, 0, rodBuffer, 0, _rodSize);
                if (getRes == 0)
                {
                    var rod = Marshal.PtrToStructure<TCP_ESTATS_DATA_ROD_v0>(rodBuffer);
                    ulong totalBytes = rod.DataBytesIn + rod.DataBytesOut;

                    string key = $"6:{row.dwLocalPort}-{row.dwRemotePort}";
                    currentSocketBytes[key] = totalBytes;

                    if (_prevSocketBytes.TryGetValue(key, out var oldTotal))
                    {
                        if (totalBytes > oldTotal)
                        {
                            ulong delta = totalBytes - oldTotal;
                            accumulatedBytesByPid[pid] = accumulatedBytesByPid.GetValueOrDefault(pid) + delta;
                        }
                    }
                }
            }
        }
        catch { }
        finally
        {
            Marshal.FreeHGlobal(rodBuffer);
            Marshal.FreeHGlobal(buffer);
        }
    }

    /// <summary>CollectUdpPids: Собирает метрики и информацию UdpPids.</summary>
    /// <param name="socketCounts">Входные данные: значение параметра socketCounts (Dictionary<int, int>).</param>
    private void CollectUdpPids(Dictionary<int, int> socketCounts)
    {
        // 1. UDP IPv4
        int size4 = 0;
        GetExtendedUdpTable(IntPtr.Zero, ref size4, false, AF_INET, UDP_TABLE_OWNER_PID, 0);
        if (size4 > 0)
        {
            IntPtr buffer = Marshal.AllocHGlobal(size4);
            try
            {
                if (GetExtendedUdpTable(buffer, ref size4, false, AF_INET, UDP_TABLE_OWNER_PID, 0) == 0)
                {
                    int numEntries = Marshal.ReadInt32(buffer);
                    IntPtr rowPtr = IntPtr.Add(buffer, 4);
                    int rowSize = Marshal.SizeOf<MIB_UDPROW_OWNER_PID>();
                    for (int i = 0; i < numEntries; i++)
                    {
                        var row = Marshal.PtrToStructure<MIB_UDPROW_OWNER_PID>(IntPtr.Add(rowPtr, i * rowSize));
                        int pid = (int)row.dwOwningPid;
                        if (pid > 0)
                        {
                            socketCounts[pid] = socketCounts.GetValueOrDefault(pid) + 1;
                        }
                    }
                }
            }
            catch { }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }

        // 2. UDP IPv6
        int size6 = 0;
        GetExtendedUdpTable(IntPtr.Zero, ref size6, false, AF_INET6, UDP_TABLE_OWNER_PID, 0);
        if (size6 > 0)
        {
            IntPtr buffer = Marshal.AllocHGlobal(size6);
            try
            {
                if (GetExtendedUdpTable(buffer, ref size6, false, AF_INET6, UDP_TABLE_OWNER_PID, 0) == 0)
                {
                    int numEntries = Marshal.ReadInt32(buffer);
                    IntPtr rowPtr = IntPtr.Add(buffer, 4);
                    int rowSize = Marshal.SizeOf<MIB_UDP6ROW_OWNER_PID>();
                    for (int i = 0; i < numEntries; i++)
                    {
                        var row = Marshal.PtrToStructure<MIB_UDP6ROW_OWNER_PID>(IntPtr.Add(rowPtr, i * rowSize));
                        int pid = (int)row.dwOwningPid;
                        if (pid > 0)
                        {
                            socketCounts[pid] = socketCounts.GetValueOrDefault(pid) + 1;
                        }
                    }
                }
            }
            catch { }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }
    }

    // Потокобезопасный кэш PID -> (Имя процесса, время истечения TTL) для исключения постоянного выделения Process объектов
    private readonly System.Collections.Concurrent.ConcurrentDictionary<int, (string Name, DateTime ExpiresAt)> _procNameCache = new();

    /// <summary>
    /// Возвращает список активных сетевых соединений (сокетов) для конкретного процесса (или для всех, если targetPid <= 0).
    /// </summary>
    public List<ProcessNetworkSocket> GetSockets(int targetPid = -1)
    {
        using var _profNetScan = ExecutionProfiler.Profile("Service.NetworkConnectionsScan");
        using var _prof = ExecutionProfiler.Measure("Service.Network.GetSockets");
        var sockets = new List<ProcessNetworkSocket>(64);
        var now = DateTime.UtcNow;

        string GetProcName(int pid)
        {
            if (pid <= 0) return string.Empty;
            if (_procNameCache.TryGetValue(pid, out var cached) && cached.ExpiresAt > now)
                return cached.Name;

            string name = string.Empty;
            try
            {
                // Быстрый путь через QueryFullProcessImageName без создания объекта Process
                var path = ProcessMetadataEnricher.TryGetProcessPath(pid);
                if (!string.IsNullOrEmpty(path))
                {
                    name = System.IO.Path.GetFileName(path);
                }
            }
            catch { }

            if (string.IsNullOrEmpty(name))
            {
                try
                {
                    using var p = Process.GetProcessById(pid);
                    name = p.ProcessName + ".exe";
                }
                catch
                {
                    name = string.Empty;
                }
            }

            _procNameCache[pid] = (name, now.AddSeconds(15));
            return name;
        }

        // 1. TCP IPv4
        CollectTcp4Sockets(sockets, targetPid, GetProcName);
        // 2. TCP IPv6
        CollectTcp6Sockets(sockets, targetPid, GetProcName);
        // 3. UDP IPv4
        CollectUdp4Sockets(sockets, targetPid, GetProcName);
        // 4. UDP IPv6
        CollectUdp6Sockets(sockets, targetPid, GetProcName);

        return sockets;
    }

    /// <summary>CollectTcp4Sockets: Собирает метрики и информацию Tcp4Sockets.</summary>
    /// <param name="sockets">Входные данные: значение параметра sockets (List<ProcessNetworkSocket>).</param>
    /// <param name="targetPid">Входные данные: значение параметра targetPid (int).</param>
    /// <param name="getProcName">Входные данные: значение параметра getProcName (Func<int, string>).</param>
    private void CollectTcp4Sockets(List<ProcessNetworkSocket> sockets, int targetPid, Func<int, string> getProcName)
    {
        int size = 0;
        GetExtendedTcpTable(IntPtr.Zero, ref size, false, AF_INET, TCP_TABLE_OWNER_PID_ALL, 0);
        if (size <= 0) return;

        IntPtr buffer = Marshal.AllocHGlobal(size);
        try
        {
            uint res = GetExtendedTcpTable(buffer, ref size, false, AF_INET, TCP_TABLE_OWNER_PID_ALL, 0);
            if (res != 0) return;

            int numEntries = Marshal.ReadInt32(buffer);
            IntPtr rowPtr = IntPtr.Add(buffer, 4);
            int rowSize = Marshal.SizeOf<MIB_TCPROW_OWNER_PID>();

            for (int i = 0; i < numEntries; i++)
            {
                var row = Marshal.PtrToStructure<MIB_TCPROW_OWNER_PID>(IntPtr.Add(rowPtr, i * rowSize));
                int pid = (int)row.dwOwningPid;
                if (targetPid > 0 && pid != targetPid) continue;

                var localIp = ParseIpv4(row.dwLocalAddr);
                var localPort = ParsePort(row.dwLocalPort);
                var remoteIp = ParseIpv4(row.dwRemoteAddr);
                var remotePort = ParsePort(row.dwRemotePort);
                var state = GetTcpStateString(row.dwState);

                sockets.Add(new ProcessNetworkSocket
                {
                    ProcessId = pid,
                    ProcessName = getProcName(pid),
                    Protocol = "TCP",
                    LocalAddress = localIp,
                    LocalPort = localPort,
                    RemoteAddress = remoteIp,
                    RemotePort = remotePort,
                    State = state,
                    IsRemotePrivate = ProcessNetworkSocket.CheckIsPrivateAddress(remoteIp)
                });
            }
        }
        catch { }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    /// <summary>CollectTcp6Sockets: Собирает метрики и информацию Tcp6Sockets.</summary>
    /// <param name="sockets">Входные данные: значение параметра sockets (List<ProcessNetworkSocket>).</param>
    /// <param name="targetPid">Входные данные: значение параметра targetPid (int).</param>
    /// <param name="getProcName">Входные данные: значение параметра getProcName (Func<int, string>).</param>
    private void CollectTcp6Sockets(List<ProcessNetworkSocket> sockets, int targetPid, Func<int, string> getProcName)
    {
        int size = 0;
        GetExtendedTcpTable(IntPtr.Zero, ref size, false, AF_INET6, TCP_TABLE_OWNER_PID_ALL, 0);
        if (size <= 0) return;

        IntPtr buffer = Marshal.AllocHGlobal(size);
        try
        {
            uint res = GetExtendedTcpTable(buffer, ref size, false, AF_INET6, TCP_TABLE_OWNER_PID_ALL, 0);
            if (res != 0) return;

            int numEntries = Marshal.ReadInt32(buffer);
            IntPtr rowPtr = IntPtr.Add(buffer, 4);
            int rowSize = Marshal.SizeOf<MIB_TCP6ROW_OWNER_PID>();

            for (int i = 0; i < numEntries; i++)
            {
                var row = Marshal.PtrToStructure<MIB_TCP6ROW_OWNER_PID>(IntPtr.Add(rowPtr, i * rowSize));
                int pid = (int)row.dwOwningPid;
                if (targetPid > 0 && pid != targetPid) continue;

                var localIp = ParseIpv6(row.ucLocalAddr, row.dwLocalScopeId);
                var localPort = ParsePort(row.dwLocalPort);
                var remoteIp = ParseIpv6(row.ucRemoteAddr, row.dwRemoteScopeId);
                var remotePort = ParsePort(row.dwRemotePort);
                var state = GetTcpStateString(row.dwState);

                sockets.Add(new ProcessNetworkSocket
                {
                    ProcessId = pid,
                    ProcessName = getProcName(pid),
                    Protocol = "TCPv6",
                    LocalAddress = localIp,
                    LocalPort = localPort,
                    RemoteAddress = remoteIp,
                    RemotePort = remotePort,
                    State = state,
                    IsRemotePrivate = ProcessNetworkSocket.CheckIsPrivateAddress(remoteIp)
                });
            }
        }
        catch { }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    /// <summary>CollectUdp4Sockets: Собирает метрики и информацию Udp4Sockets.</summary>
    /// <param name="sockets">Входные данные: значение параметра sockets (List<ProcessNetworkSocket>).</param>
    /// <param name="targetPid">Входные данные: значение параметра targetPid (int).</param>
    /// <param name="getProcName">Входные данные: значение параметра getProcName (Func<int, string>).</param>
    private void CollectUdp4Sockets(List<ProcessNetworkSocket> sockets, int targetPid, Func<int, string> getProcName)
    {
        int size = 0;
        GetExtendedUdpTable(IntPtr.Zero, ref size, false, AF_INET, UDP_TABLE_OWNER_PID, 0);
        if (size <= 0) return;

        IntPtr buffer = Marshal.AllocHGlobal(size);
        try
        {
            uint res = GetExtendedUdpTable(buffer, ref size, false, AF_INET, UDP_TABLE_OWNER_PID, 0);
            if (res != 0) return;

            int numEntries = Marshal.ReadInt32(buffer);
            IntPtr rowPtr = IntPtr.Add(buffer, 4);
            int rowSize = Marshal.SizeOf<MIB_UDPROW_OWNER_PID>();

            for (int i = 0; i < numEntries; i++)
            {
                var row = Marshal.PtrToStructure<MIB_UDPROW_OWNER_PID>(IntPtr.Add(rowPtr, i * rowSize));
                int pid = (int)row.dwOwningPid;
                if (targetPid > 0 && pid != targetPid) continue;

                var localIp = ParseIpv4(row.dwLocalAddr);
                var localPort = ParsePort(row.dwLocalPort);

                sockets.Add(new ProcessNetworkSocket
                {
                    ProcessId = pid,
                    ProcessName = getProcName(pid),
                    Protocol = "UDP",
                    LocalAddress = localIp,
                    LocalPort = localPort,
                    RemoteAddress = "*",
                    RemotePort = 0,
                    State = "LISTENING",
                    IsRemotePrivate = true
                });
            }
        }
        catch { }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    /// <summary>CollectUdp6Sockets: Собирает метрики и информацию Udp6Sockets.</summary>
    /// <param name="sockets">Входные данные: значение параметра sockets (List<ProcessNetworkSocket>).</param>
    /// <param name="targetPid">Входные данные: значение параметра targetPid (int).</param>
    /// <param name="getProcName">Входные данные: значение параметра getProcName (Func<int, string>).</param>
    private void CollectUdp6Sockets(List<ProcessNetworkSocket> sockets, int targetPid, Func<int, string> getProcName)
    {
        int size = 0;
        GetExtendedUdpTable(IntPtr.Zero, ref size, false, AF_INET6, UDP_TABLE_OWNER_PID, 0);
        if (size <= 0) return;

        IntPtr buffer = Marshal.AllocHGlobal(size);
        try
        {
            uint res = GetExtendedUdpTable(buffer, ref size, false, AF_INET6, UDP_TABLE_OWNER_PID, 0);
            if (res != 0) return;

            int numEntries = Marshal.ReadInt32(buffer);
            IntPtr rowPtr = IntPtr.Add(buffer, 4);
            int rowSize = Marshal.SizeOf<MIB_UDP6ROW_OWNER_PID>();

            for (int i = 0; i < numEntries; i++)
            {
                var row = Marshal.PtrToStructure<MIB_UDP6ROW_OWNER_PID>(IntPtr.Add(rowPtr, i * rowSize));
                int pid = (int)row.dwOwningPid;
                if (targetPid > 0 && pid != targetPid) continue;

                var localIp = ParseIpv6(row.ucLocalAddr, row.dwLocalScopeId);
                var localPort = ParsePort(row.dwLocalPort);

                sockets.Add(new ProcessNetworkSocket
                {
                    ProcessId = pid,
                    ProcessName = getProcName(pid),
                    Protocol = "UDPv6",
                    LocalAddress = localIp,
                    LocalPort = localPort,
                    RemoteAddress = "*",
                    RemotePort = 0,
                    State = "LISTENING",
                    IsRemotePrivate = true
                });
            }
        }
        catch { }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    /// <summary>ParsePort: Выполняет операцию ParsePort.</summary>
    /// <param name="portNetworkOrder">Входные данные: значение параметра portNetworkOrder (uint).</param>
    /// <returns>Выходные данные: результирующее значение типа int.</returns>
    public static int ParsePort(uint portNetworkOrder) =>
        (ushort)(((portNetworkOrder & 0xFF) << 8) | ((portNetworkOrder >> 8) & 0xFF));

    /// <summary>ParseIpv4: Выполняет операцию ParseIpv4.</summary>
    /// <param name="ipDword">Входные данные: значение параметра ipDword (uint).</param>
    /// <returns>Выходные данные: результирующее значение типа string.</returns>
    public static string ParseIpv4(uint ipDword) =>
        /// <summary>IPAddress: Выполняет операцию IPAddress.</summary>
        /// <param name="(long)ipDword).ToString(">Входные данные: значение параметра (long)ipDword).ToString( ().</param>
        new IPAddress((long)ipDword).ToString();

    /// <summary>ParseIpv6: Выполняет операцию ParseIpv6.</summary>
    /// <param name="bytes">Входные данные: значение параметра bytes (byte[]).</param>
    /// <param name="scopeId">Входные данные: значение параметра scopeId (uint).</param>
    /// <returns>Выходные данные: результирующее значение типа string.</returns>
    public static string ParseIpv6(byte[] bytes, uint scopeId) =>
        /// <summary>IPAddress: Выполняет операцию IPAddress.</summary>
        /// <param name="bytes">Входные данные: значение параметра bytes ().</param>
        /// <param name="scopeId).ToString(">Входные данные: значение параметра scopeId).ToString( ().</param>
        new IPAddress(bytes, scopeId).ToString();

    /// <summary>GetTcpStateString: Возвращает TcpStateString.</summary>
    /// <param name="state">Входные данные: значение параметра state (uint).</param>
    /// <returns>Выходные данные: результирующее значение типа string.</returns>
    public static string GetTcpStateString(uint state) => state switch
    {
        1 => "CLOSED",
        2 => "LISTENING",
        3 => "SYN_SENT",
        4 => "SYN_RCVD",
        5 => "ESTABLISHED",
        6 => "FIN_WAIT1",
        7 => "FIN_WAIT2",
        8 => "CLOSE_WAIT",
        9 => "CLOSING",
        10 => "LAST_ACK",
        11 => "TIME_WAIT",
        12 => "DELETE_TCB",
        _ => $"UNKNOWN({state})"
    };

    #region P/Invoke Native Definitions

    [DllImport("iphlpapi.dll", SetLastError = true)]
    private static extern uint GetExtendedTcpTable(
        IntPtr pTcpTable,
        ref int pdwSize,
        bool bOrder,
        int ulAf,
        int TableClass,
        uint reserved);

    [DllImport("iphlpapi.dll", SetLastError = true)]
    private static extern uint GetPerTcpConnectionEStats(
        ref MIB_TCPROW row,
        int EstatsType,
        IntPtr rw,
        int rwVersion,
        int rwSize,
        IntPtr ros,
        int rosVersion,
        int rosSize,
        IntPtr rod,
        int rodVersion,
        int rodSize);

    [DllImport("iphlpapi.dll", SetLastError = true)]
    private static extern uint SetPerTcpConnectionEStats(
        ref MIB_TCPROW row,
        int EstatsType,
        [In] byte[] rw,
        int rwVersion,
        int rwSize,
        int Offset);

    [DllImport("iphlpapi.dll", SetLastError = true)]
    private static extern uint GetPerTcp6ConnectionEStats(
        ref MIB_TCP6ROW row,
        int EstatsType,
        IntPtr rw,
        int rwVersion,
        int rwSize,
        IntPtr ros,
        int rosVersion,
        int rosSize,
        IntPtr rod,
        int rodVersion,
        int rodSize);

    [DllImport("iphlpapi.dll", SetLastError = true)]
    private static extern uint SetPerTcp6ConnectionEStats(
        ref MIB_TCP6ROW row,
        int EstatsType,
        [In] byte[] rw,
        int rwVersion,
        int rwSize,
        int Offset);

    [StructLayout(LayoutKind.Sequential)]
    private struct MIB_TCPROW
    {
        public uint dwState;
        public uint dwLocalAddr;
        public uint dwLocalPort;
        public uint dwRemoteAddr;
        public uint dwRemotePort;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MIB_TCPROW_OWNER_PID
    {
        public uint dwState;
        public uint dwLocalAddr;
        public uint dwLocalPort;
        public uint dwRemoteAddr;
        public uint dwRemotePort;
        public uint dwOwningPid;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MIB_TCP6ROW
    {
        public uint State;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)]
        public byte[] LocalAddr;
        public uint dwLocalScopeId;
        public uint dwLocalPort;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)]
        public byte[] RemoteAddr;
        public uint dwRemoteScopeId;
        public uint dwRemotePort;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MIB_TCP6ROW_OWNER_PID
    {
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)]
        public byte[] ucLocalAddr;
        public uint dwLocalScopeId;
        public uint dwLocalPort;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)]
        public byte[] ucRemoteAddr;
        public uint dwRemoteScopeId;
        public uint dwRemotePort;
        public uint dwState;
        public uint dwOwningPid;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct TCP_ESTATS_DATA_ROD_v0
    {
        public ulong DataBytesOut;
        public ulong DataSegsOut;
        public ulong DataBytesIn;
        public ulong DataSegsIn;
        public ulong SegsOut;
        public ulong SegsIn;
        public uint SoftErrors;
        public uint SoftErrorReason;
        public uint SndUna;
        public uint SndNxt;
        public uint SndMax;
        public ulong ThruBytesAcked;
        public uint RcvNxt;
        public ulong ThruBytesReceived;
    }

    [DllImport("iphlpapi.dll", SetLastError = true)]
    private static extern uint GetExtendedUdpTable(
        IntPtr pUdpTable,
        ref int pdwSize,
        bool bOrder,
        int ulAf,
        int TableClass,
        uint reserved);

    [StructLayout(LayoutKind.Sequential)]
    private struct MIB_UDPROW_OWNER_PID
    {
        public uint dwLocalAddr;
        public uint dwLocalPort;
        public uint dwOwningPid;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MIB_UDP6ROW_OWNER_PID
    {
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)]
        public byte[] ucLocalAddr;
        public uint dwLocalScopeId;
        public uint dwLocalPort;
        public uint dwOwningPid;
    }

    #endregion
}

