using StartupMonitor.Service.Services.Firewall;
using StartupMonitor.Shared.Models.Firewall;

namespace StartupMonitor.Tests.Services;

/// <summary>Управляемый результат firewall-операций для unit-тестов без обращения к COM или сетевым правилам машины.</summary>
internal sealed class TestPortDefenderFirewall : IPortDefenderFirewall
{
    internal bool BlockSucceeds { get; set; } = true;
    internal bool UnblockSucceeds { get; set; } = true;
    internal bool ThrowOnBlock { get; set; }
    internal bool ThrowOnUnblock { get; set; }
    internal int BlockCalls { get; private set; }
    internal int UnblockCalls { get; private set; }

    /// <summary>Возвращает управляемый результат блокировки и учитывает число попыток.</summary>
    /// <param name="request">Запрошенная блокировка.</param>
    /// <returns>Успех или отказ, выбранный сценарием теста.</returns>
    public FirewallOperationResult BlockIp(BlockIpRequest request)
    {
        BlockCalls++;
        if (ThrowOnBlock) throw new InvalidOperationException("Injected firewall block failure.");
        return BlockSucceeds ? FirewallOperationResult.Ok("Blocked", "TestRule") : FirewallOperationResult.Fail("Injected refusal");
    }

    /// <summary>Возвращает управляемый результат разбана, не меняя настройки операционной системы.</summary>
    /// <param name="request">Запрошенный разбан.</param>
    /// <returns>Успех или отказ, выбранный сценарием теста.</returns>
    public FirewallOperationResult UnblockIp(UnblockIpRequest request)
    {
        UnblockCalls++;
        if (ThrowOnUnblock) throw new InvalidOperationException("Injected firewall unblock failure.");
        return UnblockSucceeds ? FirewallOperationResult.Ok("Unblocked") : FirewallOperationResult.Fail("Injected refusal");
    }
}
