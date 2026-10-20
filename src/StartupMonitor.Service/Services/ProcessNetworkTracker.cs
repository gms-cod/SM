using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Runtime.InteropServices;
using StartupMonitor.Shared.Models;
using StartupMonitor.Shared.Diagnostics;

namespace StartupMonitor.Service.Services;

/// <summary>
/// Высокопроизводительный трекер сетевой активности процессов Windows через IP Helper API
/// (GetExtendedTcpTable + GetPerTcpConnectionEStats + GetExtendedUdpTable).
/// Не требует сторонних драйверов или тяжелых библиотек трассировки.
/// </summary>
public class ProcessNetworkTracker : IDisposable
{
    private const int AF_INET = 2;
    private const int AF_INET6 = 23;
    private const int TCP_TABLE_OWNER_PID_ALL = 5;
    private const int UDP_TABLE_OWNER_PID = 1;
    private const uint MIB_TCP_STATE_ESTABLISHED = 5;
    private const int TcpConnectionEstatsData = 1;
    private const uint ERROR_INSUFFICIENT_BUFFER = 122;

    #region Компактные безаллокационные ключи сокетов

    /// <summary>
    /// Компактный readonly-ключ IPv4-сокета. Хранится по значению (без боксинга и без строк),
    /// используется как ключ словаря <see cref="Dictionary{TKey,TValue}"/> без выделений в куче.
    /// </summary>
    public readonly struct SocketKey4 : IEquatable<SocketKey4>
    {
        public readonly uint LocalAddr;
        public readonly ushort LocalPort;
        public readonly uint RemoteAddr;
        public readonly ushort RemotePort;

        public SocketKey4(uint localAddr, uint localPortNetworkOrder, uint remoteAddr, uint remotePortNetworkOrder)
        {
            LocalAddr = localAddr;
            LocalPort = NormalizePort(localPortNetworkOrder);
            RemoteAddr = remoteAddr;
            RemotePort = NormalizePort(remotePortNetworkOrder);
        }

        public bool Equals(SocketKey4 other) =>
            LocalAddr == other.LocalAddr &&
            LocalPort == other.LocalPort &&
            RemoteAddr == other.RemoteAddr &&
            RemotePort == other.RemotePort;

        public override bool Equals(object? obj) => obj is SocketKey4 other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                int h = (int)LocalAddr;
                h = (h * 397) ^ LocalPort;
                h = (h * 397) ^ (int)RemoteAddr;
                h = (h * 397) ^ RemotePort;
                return h;
            }
        }
    }

    /// <summary>
    /// Компактный readonly-ключ IPv6-сокета. 16-байтовые адреса упаковываются в два
    /// <see cref="ulong"/> (Hi/Lo) прямо на стеке — без строк и выделений в куче.
    /// </summary>
    public readonly struct SocketKey6 : IEquatable<SocketKey6>
    {
        public readonly ulong LocalAddrHi;
        public readonly ulong LocalAddrLo;
        public readonly uint LocalScopeId;
        public readonly ushort LocalPort;
        public readonly ulong RemoteAddrHi;
        public readonly ulong RemoteAddrLo;
        public readonly uint RemoteScopeId;
        public readonly ushort RemotePort;

        public SocketKey6(
            byte[] localAddr,
            uint localScopeId,
            uint localPortNetworkOrder,
            byte[] remoteAddr,
            uint remoteScopeId,
            uint remotePortNetworkOrder)
        {
            var localSpan = localAddr.AsSpan();
            var remoteSpan = remoteAddr.AsSpan();
            LocalAddrHi = BinaryPrimitives.ReadUInt64LittleEndian(localSpan.Slice(0, 8));
            LocalAddrLo = BinaryPrimitives.ReadUInt64LittleEndian(localSpan.Slice(8, 8));
            LocalScopeId = localScopeId;
            LocalPort = NormalizePort(localPortNetworkOrder);
            RemoteAddrHi = BinaryPrimitives.ReadUInt64LittleEndian(remoteSpan.Slice(0, 8));
            RemoteAddrLo = BinaryPrimitives.ReadUInt64LittleEndian(remoteSpan.Slice(8, 8));
            RemoteScopeId = remoteScopeId;
            RemotePort = NormalizePort(remotePortNetworkOrder);
        }

        public bool Equals(SocketKey6 other) =>
            LocalAddrHi == other.LocalAddrHi &&
            LocalAddrLo == other.LocalAddrLo &&
            LocalScopeId == other.LocalScopeId &&
            LocalPort == other.LocalPort &&
            RemoteAddrHi == other.RemoteAddrHi &&
            RemoteAddrLo == other.RemoteAddrLo &&
            RemoteScopeId == other.RemoteScopeId &&
            RemotePort == other.RemotePort;

        public override bool Equals(object? obj) => obj is SocketKey6 other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                int h = (int)LocalAddrHi;
                h = (h * 397) ^ (int)(LocalAddrHi >> 32);
                h = (h * 397) ^ (int)LocalAddrLo;
                h = (h * 397) ^ (int)(LocalAddrLo >> 32);
                h = (h * 397) ^ (int)LocalScopeId;
                h = (h * 397) ^ LocalPort;
                h = (h * 397) ^ (int)RemoteAddrHi;
                h = (h * 397) ^ (int)(RemoteAddrHi >> 32);
                h = (h * 397) ^ (int)RemoteAddrLo;
                h = (h * 397) ^ (int)(RemoteAddrLo >> 32);
                h = (h * 397) ^ (int)RemoteScopeId;
                h = (h * 397) ^ RemotePort;
                return h;
            }
        }
    }

    #endregion

    // Кэш накопленных байт по сокетам (struct-ключи, без аллокаций строк на каждом тике)
    private Dictionary<SocketKey4, ulong> _prevSocketBytes4 = new(128);
    private Dictionary<SocketKey6, ulong> _prevSocketBytes6 = new(64);
    private DateTime _lastSampleTime = DateTime.UtcNow;

    // Предвыделенный байт для активации сбора статистики (BOOLEAN EnableCollection = 1)
    private readonly byte[] _rwEnableCollection = [1];
    private readonly int _rodSize = Marshal.SizeOf<TCP_ESTATS_DATA_ROD_v0>();

    // Переиспользуемые неуправляемые буферы (AllocHGlobal/ReAllocHGlobal вместо allocate/free на каждый тик)
    private IntPtr _tcp4Buffer;
    private int _tcp4Capacity;
    private IntPtr _tcp6Buffer;
    private int _tcp6Capacity;
    private IntPtr _udp4Buffer;
    private int _udp4Capacity;
    private IntPtr _udp6Buffer;
    private int _udp6Capacity;
    private IntPtr _rodBuffer;
    private int _rodCapacity;

    // Единая блокировка для доступа к общим буферам и кэшу предыдущего среза.
    private readonly object _sync = new();

    /// <summary>
    /// Снимает показания сети и рассчитывает скорость передачи (Байт/с) для каждого процесса, суммарно по системе и количество сокетов.
    /// </summary>
    public (Dictionary<int, long> ProcessNetworkBytesPerSec, long TotalNetworkBytesPerSec, Dictionary<int, int> ProcessSocketCounts) CaptureNetworkBandwidth()
    {
        using var _prof = ExecutionProfiler.Measure("Service.Network.CaptureBandwidth");
        lock (_sync)
        {
            var now = DateTime.UtcNow;
            var elapsedSec = (now - _lastSampleTime).TotalSeconds;
            if (elapsedSec < 0.05) elapsedSec = 0.05;

            var currentSocketBytes4 = new Dictionary<SocketKey4, ulong>(Math.Max(64, _prevSocketBytes4.Count));
            var currentSocketBytes6 = new Dictionary<SocketKey6, ulong>(Math.Max(32, _prevSocketBytes6.Count));
            var accumulatedBytesByPid = new Dictionary<int, ulong>(64);
            var socketCounts = new Dictionary<int, int>(128);

            // 1. IPv4 TCP соединения
            CollectIpv4Sockets(currentSocketBytes4, accumulatedBytesByPid, socketCounts);

            // 2. IPv6 TCP соединения
            CollectIpv6Sockets(currentSocketBytes6, accumulatedBytesByPid, socketCounts);

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

            _prevSocketBytes4 = currentSocketBytes4;
            _prevSocketBytes6 = currentSocketBytes6;
            _lastSampleTime = now;

            return (processSpeeds, totalSpeed, socketCounts);
        }
    }

    /// <summary>CollectIpv4Sockets: Собирает метрики TCP IPv4 и накапливает дельты байт по PID.</summary>
    private void CollectIpv4Sockets(
        Dictionary<SocketKey4, ulong> currentSocketBytes,
        Dictionary<int, ulong> accumulatedBytesByPid,
        Dictionary<int, int> socketCounts)
    {
        int size = 0;
        GetExtendedTcpTable(IntPtr.Zero, ref size, false, AF_INET, TCP_TABLE_OWNER_PID_ALL, 0);
        if (size <= 0) return;

        IntPtr buffer = EnsureBuffer(ref _tcp4Buffer, ref _tcp4Capacity, size);
        uint res = GetExtendedTcpTable(buffer, ref size, false, AF_INET, TCP_TABLE_OWNER_PID_ALL, 0);
        if (res == ERROR_INSUFFICIENT_BUFFER)
        {
            buffer = EnsureBuffer(ref _tcp4Buffer, ref _tcp4Capacity, size);
            res = GetExtendedTcpTable(buffer, ref size, false, AF_INET, TCP_TABLE_OWNER_PID_ALL, 0);
        }
        if (res != 0) return;

        IntPtr rodBuffer = EnsureBuffer(ref _rodBuffer, ref _rodCapacity, _rodSize);

        try
        {
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

                    var key = new SocketKey4(row.dwLocalAddr, row.dwLocalPort, row.dwRemoteAddr, row.dwRemotePort);
                    currentSocketBytes[key] = totalBytes;

                    if (_prevSocketBytes4.TryGetValue(key, out var oldTotal) && totalBytes > oldTotal)
                    {
                        ulong delta = totalBytes - oldTotal;
                        accumulatedBytesByPid[pid] = accumulatedBytesByPid.GetValueOrDefault(pid) + delta;
                    }
                }
            }
        }
        catch { }
    }

    /// <summary>CollectIpv6Sockets: Собирает метрики TCP IPv6 и накапливает дельты байт по PID.</summary>
    private void CollectIpv6Sockets(
        Dictionary<SocketKey6, ulong> currentSocketBytes,
        Dictionary<int, ulong> accumulatedBytesByPid,
        Dictionary<int, int> socketCounts)
    {
        int size = 0;
        GetExtendedTcpTable(IntPtr.Zero, ref size, false, AF_INET6, TCP_TABLE_OWNER_PID_ALL, 0);
        if (size <= 0) return;

        IntPtr buffer = EnsureBuffer(ref _tcp6Buffer, ref _tcp6Capacity, size);
        uint res = GetExtendedTcpTable(buffer, ref size, false, AF_INET6, TCP_TABLE_OWNER_PID_ALL, 0);
        if (res == ERROR_INSUFFICIENT_BUFFER)
        {
            buffer = EnsureBuffer(ref _tcp6Buffer, ref _tcp6Capacity, size);
            res = GetExtendedTcpTable(buffer, ref size, false, AF_INET6, TCP_TABLE_OWNER_PID_ALL, 0);
        }
        if (res != 0) return;

        IntPtr rodBuffer = EnsureBuffer(ref _rodBuffer, ref _rodCapacity, _rodSize);

        try
        {
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

                    var key = new SocketKey6(
                        row.ucLocalAddr, row.dwLocalScopeId, row.dwLocalPort,
                        row.ucRemoteAddr, row.dwRemoteScopeId, row.dwRemotePort);
                    currentSocketBytes[key] = totalBytes;

                    if (_prevSocketBytes6.TryGetValue(key, out var oldTotal) && totalBytes > oldTotal)
                    {
                        ulong delta = totalBytes - oldTotal;
                        accumulatedBytesByPid[pid] = accumulatedBytesByPid.GetValueOrDefault(pid) + delta;
                    }
                }
            }
        }
        catch { }
    }

    /// <summary>CollectUdpPids: Собирает количество UDP сокетов по PID (IPv4 + IPv6).</summary>
    private void CollectUdpPids(Dictionary<int, int> socketCounts)
    {
        // 1. UDP IPv4
        int size4 = 0;
        GetExtendedUdpTable(IntPtr.Zero, ref size4, false, AF_INET, UDP_TABLE_OWNER_PID, 0);
        if (size4 > 0)
        {
            IntPtr buffer = EnsureBuffer(ref _udp4Buffer, ref _udp4Capacity, size4);
            uint res = GetExtendedUdpTable(buffer, ref size4, false, AF_INET, UDP_TABLE_OWNER_PID, 0);
            if (res == ERROR_INSUFFICIENT_BUFFER)
            {
                buffer = EnsureBuffer(ref _udp4Buffer, ref _udp4Capacity, size4);
                res = GetExtendedUdpTable(buffer, ref size4, false, AF_INET, UDP_TABLE_OWNER_PID, 0);
            }
            if (res == 0)
            {
                try
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
                catch { }
            }
        }

        // 2. UDP IPv6
        int size6 = 0;
        GetExtendedUdpTable(IntPtr.Zero, ref size6, false, AF_INET6, UDP_TABLE_OWNER_PID, 0);
        if (size6 > 0)
        {
            IntPtr buffer = EnsureBuffer(ref _udp6Buffer, ref _udp6Capacity, size6);
            uint res = GetExtendedUdpTable(buffer, ref size6, false, AF_INET6, UDP_TABLE_OWNER_PID, 0);
            if (res == ERROR_INSUFFICIENT_BUFFER)
            {
                buffer = EnsureBuffer(ref _udp6Buffer, ref _udp6Capacity, size6);
                res = GetExtendedUdpTable(buffer, ref size6, false, AF_INET6, UDP_TABLE_OWNER_PID, 0);
            }
            if (res == 0)
            {
                try
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
                catch { }
            }
        }
    }

    /// <summary>
    /// Возвращает переиспользуемый неуправляемый буфер, увеличивая его через ReAllocHGlobal только при нехватке ёмкости.
    /// Устраняет пары AllocHGlobal/FreeHGlobal на каждом тике сбора метрик.
    /// </summary>
    private static IntPtr EnsureBuffer(ref IntPtr buffer, ref int capacity, int requiredSize)
    {
        if (requiredSize <= 0) requiredSize = 1;
        if (buffer == IntPtr.Zero)
        {
            buffer = Marshal.AllocHGlobal(requiredSize);
            capacity = requiredSize;
        }
        else if (capacity < requiredSize)
        {
            buffer = Marshal.ReAllocHGlobal(buffer, (IntPtr)requiredSize);
            capacity = requiredSize;
        }

        return buffer;
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
        lock (_sync)
        {
            var sockets = new List<ProcessNetworkSocket>(64);
            var now = DateTime.UtcNow;

            // Периодическая очистка устаревших PID из кэша имён для предотвращения утечек памяти
            if (_procNameCache.Count > 128)
            {
                foreach (var kvp in _procNameCache)
                {
                    if (kvp.Value.ExpiresAt <= now)
                    {
                        _procNameCache.TryRemove(kvp.Key, out _);
                    }
                }
            }

            string GetProcName(int pid)
            {
                if (pid == 0) return "System (Kernel/Idle)";
                if (pid == 4) return "System";
                if (pid < 0) return string.Empty;
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
    }

    /// <summary>CollectTcp4Sockets: Собирает список TCP IPv4 соединений.</summary>
    private void CollectTcp4Sockets(List<ProcessNetworkSocket> sockets, int targetPid, Func<int, string> getProcName)
    {
        int size = 0;
        GetExtendedTcpTable(IntPtr.Zero, ref size, false, AF_INET, TCP_TABLE_OWNER_PID_ALL, 0);
        if (size <= 0) return;

        IntPtr buffer = EnsureBuffer(ref _tcp4Buffer, ref _tcp4Capacity, size);
        uint res = GetExtendedTcpTable(buffer, ref size, false, AF_INET, TCP_TABLE_OWNER_PID_ALL, 0);
        if (res == ERROR_INSUFFICIENT_BUFFER)
        {
            buffer = EnsureBuffer(ref _tcp4Buffer, ref _tcp4Capacity, size);
            res = GetExtendedTcpTable(buffer, ref size, false, AF_INET, TCP_TABLE_OWNER_PID_ALL, 0);
        }
        if (res != 0) return;

        try
        {
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
    }

    /// <summary>CollectTcp6Sockets: Собирает список TCP IPv6 соединений.</summary>
    private void CollectTcp6Sockets(List<ProcessNetworkSocket> sockets, int targetPid, Func<int, string> getProcName)
    {
        int size = 0;
        GetExtendedTcpTable(IntPtr.Zero, ref size, false, AF_INET6, TCP_TABLE_OWNER_PID_ALL, 0);
        if (size <= 0) return;

        IntPtr buffer = EnsureBuffer(ref _tcp6Buffer, ref _tcp6Capacity, size);
        uint res = GetExtendedTcpTable(buffer, ref size, false, AF_INET6, TCP_TABLE_OWNER_PID_ALL, 0);
        if (res == ERROR_INSUFFICIENT_BUFFER)
        {
            buffer = EnsureBuffer(ref _tcp6Buffer, ref _tcp6Capacity, size);
            res = GetExtendedTcpTable(buffer, ref size, false, AF_INET6, TCP_TABLE_OWNER_PID_ALL, 0);
        }
        if (res != 0) return;

        try
        {
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
    }

    /// <summary>CollectUdp4Sockets: Собирает список UDP IPv4 сокетов.</summary>
    private void CollectUdp4Sockets(List<ProcessNetworkSocket> sockets, int targetPid, Func<int, string> getProcName)
    {
        int size = 0;
        GetExtendedUdpTable(IntPtr.Zero, ref size, false, AF_INET, UDP_TABLE_OWNER_PID, 0);
        if (size <= 0) return;

        IntPtr buffer = EnsureBuffer(ref _udp4Buffer, ref _udp4Capacity, size);
        uint res = GetExtendedUdpTable(buffer, ref size, false, AF_INET, UDP_TABLE_OWNER_PID, 0);
        if (res == ERROR_INSUFFICIENT_BUFFER)
        {
            buffer = EnsureBuffer(ref _udp4Buffer, ref _udp4Capacity, size);
            res = GetExtendedUdpTable(buffer, ref size, false, AF_INET, UDP_TABLE_OWNER_PID, 0);
        }
        if (res != 0) return;

        try
        {
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
    }

    /// <summary>CollectUdp6Sockets: Собирает список UDP IPv6 сокетов.</summary>
    private void CollectUdp6Sockets(List<ProcessNetworkSocket> sockets, int targetPid, Func<int, string> getProcName)
    {
        int size = 0;
        GetExtendedUdpTable(IntPtr.Zero, ref size, false, AF_INET6, UDP_TABLE_OWNER_PID, 0);
        if (size <= 0) return;

        IntPtr buffer = EnsureBuffer(ref _udp6Buffer, ref _udp6Capacity, size);
        uint res = GetExtendedUdpTable(buffer, ref size, false, AF_INET6, UDP_TABLE_OWNER_PID, 0);
        if (res == ERROR_INSUFFICIENT_BUFFER)
        {
            buffer = EnsureBuffer(ref _udp6Buffer, ref _udp6Capacity, size);
            res = GetExtendedUdpTable(buffer, ref size, false, AF_INET6, UDP_TABLE_OWNER_PID, 0);
        }
        if (res != 0) return;

        try
        {
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
    }

    /// <summary>Нормализует порт из сетевого порядка байт (big-endian) в хост-порядок.</summary>
    public static ushort NormalizePort(uint portNetworkOrder) =>
        (ushort)(((portNetworkOrder & 0xFF) << 8) | ((portNetworkOrder >> 8) & 0xFF));

    /// <summary>ParsePort: Преобразует порт из сетевого порядка байт в int.</summary>
    public static int ParsePort(uint portNetworkOrder) => NormalizePort(portNetworkOrder);

    /// <summary>ParseIpv4: Преобразует адрес IPv4 (DWORD) в строку.</summary>
    public static string ParseIpv4(uint ipDword) => new IPAddress((long)ipDword).ToString();

    /// <summary>ParseIpv6: Преобразует адрес IPv6 (16 байт + scope) в строку.</summary>
    public static string ParseIpv6(byte[] bytes, uint scopeId) => new IPAddress(bytes, scopeId).ToString();

    /// <summary>GetTcpStateString: Возвращает строковое имя состояния TCP.</summary>
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

    /// <summary>Освобождает переиспользуемые неуправляемые буферы.</summary>
    public void Dispose()
    {
        lock (_sync)
        {
            FreeBuffer(ref _tcp4Buffer, ref _tcp4Capacity);
            FreeBuffer(ref _tcp6Buffer, ref _tcp6Capacity);
            FreeBuffer(ref _udp4Buffer, ref _udp4Capacity);
            FreeBuffer(ref _udp6Buffer, ref _udp6Capacity);
            FreeBuffer(ref _rodBuffer, ref _rodCapacity);
        }
    }

    private static void FreeBuffer(ref IntPtr buffer, ref int capacity)
    {
        if (buffer != IntPtr.Zero)
        {
            Marshal.FreeHGlobal(buffer);
            buffer = IntPtr.Zero;
        }
        capacity = 0;
    }

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
