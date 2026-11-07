using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using StartupMonitor.Shared.Models.Firewall;

namespace StartupMonitor.Service.Services.Firewall;

/// <summary>Временная исходящая политика приложения, удаляемая при завершении службы или срока сеанса.</summary>
public interface ISessionFirewallPolicy : IDisposable
{
    /// <summary>Доступен ли безопасный soft permit при текущем режиме исходящей фильтрации Windows.</summary>
    bool CanAllowSession { get; }

    /// <summary>Атомарно устанавливает исходящую политику IPv4 и IPv6 для точного пути приложения.</summary>
    /// <param name="path">Полный путь исполняемого файла.</param>
    /// <param name="action">Разрешение либо блокировка новых исходящих соединений.</param>
    /// <param name="lifetime">Предельная длительность решения.</param>
    /// <returns>Подтверждение установки либо диагностируемый отказ.</returns>
    FirewallOperationResult Apply(string path, FirewallAction action, TimeSpan lifetime);

    /// <summary>Удаляет принадлежащую службе временную политику одного приложения.</summary>
    /// <param name="path">Полный путь приложения.</param>
    void Remove(string path);

    /// <summary>Проверяет, сохраняется ли принадлежащая службе политика до конца срока действия.</summary>
    /// <param name="path">Полный путь приложения.</param>
    /// <returns>True для действующей политики.</returns>
    bool IsActive(string path);
}

/// <summary>
/// Управляет динамическими WFP-сессиями. ОС удаляет объекты при закрытии handle, включая аварийное
/// завершение процесса. Обычный soft permit не обходит чужие блокирующие правила или IPsec.
/// Каждая сессия содержит только точное приложение, отдельный sublayer и две IP-семьи.
/// </summary>
internal sealed class SessionFirewallPolicy : ISessionFirewallPolicy
{
    private static readonly Serilog.ILogger Log = Serilog.Log.ForContext<SessionFirewallPolicy>();
    private static readonly Guid AppIdCondition = new("d78e1e87-8644-4ea5-9437-d809ecefc971");
    private static readonly Guid ConnectV4 = new("c38d57d1-05a7-4c33-904f-7fbceee60e82");
    private static readonly Guid ConnectV6 = new("4a72393b-319f-44bc-84c3-ba54dcb3b6b4");
    private readonly object _sync = new();
    private readonly WindowsFirewallService _firewall;
    private readonly Dictionary<string, Lease> _leases = new(StringComparer.OrdinalIgnoreCase);
    private readonly Timer _expiryTimer;
    private bool _disposed;

    /// <summary>Создаёт отключённый таймер; WFP не открывается до первого явного решения.</summary>
    /// <param name="firewall">Источник действующего режима для проверки ограничений soft permit.</param>
    public SessionFirewallPolicy(WindowsFirewallService firewall)
    {
        _firewall = firewall ?? throw new ArgumentNullException(nameof(firewall));
        _expiryTimer = new Timer(_ => Expire(), null, Timeout.Infinite, Timeout.Infinite);
    }

    /// <inheritdoc />
    public bool CanAllowSession => _firewall.GetFilteringMode() is FirewallFilteringMode.Normal or FirewallFilteringMode.Disabled;

    /// <inheritdoc />
    public FirewallOperationResult Apply(string path, FirewallAction action, TimeSpan lifetime)
    {
        path = path?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path) ||
            action is not (FirewallAction.Allow or FirewallAction.Block) || lifetime <= TimeSpan.Zero || lifetime > TimeSpan.FromHours(8))
            return FirewallOperationResult.Fail("Недопустимый путь, действие или срок временного правила.");

        lock (_sync)
        {
            if (_disposed)
                return FirewallOperationResult.Fail("Менеджер временных правил остановлен.");
            if (action == FirewallAction.Allow && !CanAllowSession)
                return FirewallOperationResult.Fail("В этом режиме временное разрешение не снимает блокировку Windows. Используйте постоянное разрешение или измените режим фильтрации.");
            PruneExpired();
            if (_leases.Count >= NetworkConnectionAlertService.MaxPendingAlerts && !_leases.ContainsKey(path))
                return FirewallOperationResult.Fail("Достигнут предел временных правил приложений.");

            try
            {
                var replacement = CreateLease(path.Trim(), action, DateTime.UtcNow + lifetime);
                if (_leases.Remove(path, out var previous))
                    previous.Handle.Dispose();
                _leases[path] = replacement;
                ScheduleExpiry();
                return FirewallOperationResult.Ok("Временное исходящее правило установлено для IPv4 и IPv6.");
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "NetworkAlerts: WFP-политика {Action} для приложения не установлена", action);
                return FirewallOperationResult.Fail($"Не удалось установить временную политику WFP: {ex.Message}");
            }
        }
    }

    /// <inheritdoc />
    public void Remove(string path)
    {
        path = path?.Trim() ?? string.Empty;
        lock (_sync)
        {
            if (_leases.Remove(path, out var lease))
                lease.Handle.Dispose();
            ScheduleExpiry();
        }
    }

    /// <inheritdoc />
    public bool IsActive(string path)
    {
        path = path?.Trim() ?? string.Empty;
        lock (_sync)
        {
            if (_disposed)
                return false;
            PruneExpired();
            ScheduleExpiry();
            return _leases.ContainsKey(path);
        }
    }

    /// <summary>Закрывает все динамические сессии, не затрагивая постоянную политику Windows.</summary>
    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed)
                return;
            _disposed = true;
            _expiryTimer.Dispose();
            foreach (var lease in _leases.Values)
                lease.Handle.Dispose();
            _leases.Clear();
        }
    }

    /// <summary>Обрабатывает истечение даже при отключённом детекторе алертов.</summary>
    private void Expire()
    {
        lock (_sync)
        {
            if (_disposed)
                return;
            PruneExpired();
            ScheduleExpiry();
        }
    }

    /// <summary>Освобождает истёкшие lease; вызывается только под общей блокировкой.</summary>
    private void PruneExpired()
    {
        foreach (var pair in _leases.Where(pair => pair.Value.ExpiresAt <= DateTime.UtcNow).ToArray())
        {
            pair.Value.Handle.Dispose();
            _leases.Remove(pair.Key);
        }
    }

    /// <summary>Планирует единственное пробуждение к ближайшей экспирации без периодического опроса.</summary>
    private void ScheduleExpiry()
    {
        if (_disposed)
            return;
        if (_leases.Count == 0)
        {
            _expiryTimer.Change(Timeout.Infinite, Timeout.Infinite);
            return;
        }
        var remaining = _leases.Values.Min(lease => lease.ExpiresAt) - DateTime.UtcNow;
        _expiryTimer.Change(remaining > TimeSpan.Zero ? remaining : TimeSpan.FromMilliseconds(1), Timeout.InfiniteTimeSpan);
    }

    /// <summary>Создаёт обе IP-политики одной транзакцией; неуспех закрывает только новую динамическую сессию.</summary>
    /// <param name="path">Точный путь, преобразуемый Windows в ALE app-id.</param>
    /// <param name="action">Действие фильтра без принудительного обхода чужих блокировок.</param>
    /// <param name="expiresAt">Момент завершения lease.</param>
    /// <returns>Подтверждённый владелец WFP-сессии.</returns>
    private static Lease CreateLease(string path, FirewallAction action, DateTime expiresAt)
    {
        var session = new NativeSession
        {
            Display = new NativeDisplay { Name = "StartupMonitor temporary app policy" },
            Flags = 1, // FWPM_SESSION_FLAG_DYNAMIC: BFE удаляет все объекты при утрате клиента.
            TransactionTimeout = 5000
        };
        Check(Native.FwpmEngineOpen0(null, 10, IntPtr.Zero, ref session, out var handle), "FwpmEngineOpen0");
        IntPtr appId = IntPtr.Zero;
        IntPtr conditions = IntPtr.Zero;
        var transaction = false;
        try
        {
            Check(Native.FwpmGetAppIdFromFileName0(path, out appId), "FwpmGetAppIdFromFileName0");
            var condition = new NativeCondition
            {
                FieldKey = AppIdCondition,
                Value = new NativeValue { Type = 12, Data = appId } // FWP_BYTE_BLOB_TYPE.
            };
            conditions = Marshal.AllocHGlobal(Marshal.SizeOf<NativeCondition>());
            Marshal.StructureToPtr(condition, conditions, false);
            Check(Native.FwpmTransactionBegin0(handle, 0), "FwpmTransactionBegin0");
            transaction = true;
            var sublayer = new NativeSublayer
            {
                Key = Guid.NewGuid(),
                Display = new NativeDisplay { Name = "StartupMonitor session app" },
                Weight = 0x100
            };
            Check(Native.FwpmSubLayerAdd0(handle, ref sublayer, IntPtr.Zero), "FwpmSubLayerAdd0");
            foreach (var layer in new[] { ConnectV4, ConnectV6 })
            {
                var filter = new NativeFilter
                {
                    Key = Guid.NewGuid(),
                    Display = new NativeDisplay { Name = "StartupMonitor session outgoing app" },
                    LayerKey = layer,
                    SublayerKey = sublayer.Key,
                    ConditionCount = 1,
                    Conditions = conditions,
                    Action = new NativeAction { Type = action == FirewallAction.Block ? 0x1001u : 0x1002u }
                };
                Check(Native.FwpmFilterAdd0(handle, ref filter, IntPtr.Zero, out _), "FwpmFilterAdd0");
            }
            Check(Native.FwpmTransactionCommit0(handle), "FwpmTransactionCommit0");
            transaction = false;
            return new Lease(handle, expiresAt);
        }
        catch
        {
            if (transaction)
            {
                var error = Native.FwpmTransactionAbort0(handle);
                if (error != 0)
                    Log.Warning("NetworkAlerts: отмена WFP-транзакции завершилась кодом 0x{Error:X8}", error);
            }
            handle.Dispose();
            throw;
        }
        finally
        {
            if (conditions != IntPtr.Zero)
                Marshal.FreeHGlobal(conditions);
            if (appId != IntPtr.Zero)
                Native.FwpmFreeMemory0(ref appId);
        }
    }

    /// <summary>Преобразует native-ошибку в диагностируемое исключение с именем операции.</summary>
    /// <param name="status">Win32/WFP-код результата.</param>
    /// <param name="operation">Имя проверяемой операции.</param>
    private static void Check(uint status, string operation)
    {
        if (status != 0)
            throw new Win32Exception(unchecked((int)status), $"{operation}: 0x{status:X8}");
    }

    /// <summary>Владелец динамической сессии и её предельный срок действия.</summary>
    private sealed record Lease(EngineHandle Handle, DateTime ExpiresAt);

    /// <summary>Дескриптор BFE с детерминированным освобождением и страхующим SafeHandle.</summary>
    private sealed class EngineHandle : SafeHandleZeroOrMinusOneIsInvalid
    {
        /// <summary>Конструктор для native-marshaller.</summary>
        public EngineHandle() : base(true) { }

        /// <summary>Закрывает только принадлежащую этому объекту WFP-сессию.</summary>
        /// <returns>True при подтверждённом освобождении.</returns>
        protected override bool ReleaseHandle()
        {
            var status = Native.FwpmEngineClose0(handle);
            if (status != 0)
                Log.Warning("NetworkAlerts: закрытие WFP-сессии завершилось кодом 0x{Error:X8}", status);
            return status == 0;
        }
    }

    /// <summary>Собственная аннотация native-объекта; строки копируются BFE при вызове.</summary>
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NativeDisplay { [MarshalAs(UnmanagedType.LPWStr)] public string? Name; [MarshalAs(UnmanagedType.LPWStr)] public string? Description; }

    /// <summary>FWPM_SESSION0 из Windows SDK с естественным выравниванием указателей.</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct NativeSession { public Guid Key; public NativeDisplay Display; public uint Flags, TransactionTimeout, ProcessId; public IntPtr Sid, Username; public int KernelMode; }

    /// <summary>FWP_BYTE_BLOB; данные принадлежат вызывающей стороне.</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct NativeBlob { public uint Size; public IntPtr Data; }

    /// <summary>FWPM_SUBLAYER0 без persistent-флагов и provider-контекста.</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct NativeSublayer { public Guid Key; public NativeDisplay Display; public uint Flags; public IntPtr ProviderKey; public NativeBlob ProviderData; public ushort Weight; }

    /// <summary>FWP_VALUE0 / FWP_CONDITION_VALUE0: enum и union, выровненный по границе 8 байт.</summary>
    [StructLayout(LayoutKind.Explicit, Size = 16)]
    private struct NativeValue { [FieldOffset(0)] public uint Type; [FieldOffset(8)] public IntPtr Data; }

    /// <summary>FWPM_FILTER_CONDITION0 для одного точного ALE app-id.</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct NativeCondition { public Guid FieldKey; public uint MatchType; public NativeValue Value; }

    /// <summary>FWPM_ACTION0: terminating action и 16-байтовый union GUID.</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct NativeAction { public uint Type; public Guid FilterType; }

    /// <summary>FWPM_FILTER0; 16-байтовый context union сохраняется даже без provider context.</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct NativeFilter
    {
        public Guid Key; public NativeDisplay Display; public uint Flags; public IntPtr ProviderKey;
        public NativeBlob ProviderData; public Guid LayerKey, SublayerKey; public NativeValue Weight;
        public uint ConditionCount; public IntPtr Conditions; public NativeAction Action;
        public ulong ContextLow, ContextHigh; public IntPtr Reserved; public ulong Id; public NativeValue EffectiveWeight;
    }

    /// <summary>Узкий набор user-mode WFP API; все коды проверяются до подтверждения решения.</summary>
    private static class Native
    {
        [DllImport("fwpuclnt.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
        internal static extern uint FwpmEngineOpen0(string? serverName, uint authenticationService, IntPtr identity, ref NativeSession session, out EngineHandle handle);
        [DllImport("fwpuclnt.dll", ExactSpelling = true)]
        internal static extern uint FwpmEngineClose0(IntPtr handle);
        [DllImport("fwpuclnt.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
        internal static extern uint FwpmGetAppIdFromFileName0(string path, out IntPtr appId);
        [DllImport("fwpuclnt.dll", ExactSpelling = true)]
        internal static extern void FwpmFreeMemory0(ref IntPtr memory);
        [DllImport("fwpuclnt.dll", ExactSpelling = true)]
        internal static extern uint FwpmTransactionBegin0(EngineHandle handle, uint flags);
        [DllImport("fwpuclnt.dll", ExactSpelling = true)]
        internal static extern uint FwpmTransactionCommit0(EngineHandle handle);
        [DllImport("fwpuclnt.dll", ExactSpelling = true)]
        internal static extern uint FwpmTransactionAbort0(EngineHandle handle);
        [DllImport("fwpuclnt.dll", ExactSpelling = true)]
        internal static extern uint FwpmSubLayerAdd0(EngineHandle handle, ref NativeSublayer sublayer, IntPtr securityDescriptor);
        [DllImport("fwpuclnt.dll", ExactSpelling = true)]
        internal static extern uint FwpmFilterAdd0(EngineHandle handle, ref NativeFilter filter, IntPtr securityDescriptor, out ulong id);
    }
}
