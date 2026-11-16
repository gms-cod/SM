using StartupMonitor.Service.Services.Firewall;
using StartupMonitor.Shared.Models.Firewall;

namespace StartupMonitor.Tests.Services;

/// <summary>Verifies test scenario for TestPortDefenderFirewall.cs.</summary>
internal sealed class TestPortDefenderFirewall : IPortDefenderFirewall
{
    /// <summary>
    /// Provides operations for this member.
    /// </summary>
    internal bool BlockSucceeds { get; set; } = true;
    /// <summary>
    /// Provides operations for this member.
    /// </summary>
    internal bool UnblockSucceeds { get; set; } = true;
    /// <summary>
    /// Provides operations for this member.
    /// </summary>
    internal bool ThrowOnBlock { get; set; }
    /// <summary>
    /// Provides operations for this member.
    /// </summary>
    internal bool ThrowOnUnblock { get; set; }
    /// <summary>
    /// Provides operations for this member.
    /// </summary>
    internal int BlockCalls { get; private set; }
    /// <summary>
    /// Provides operations for this member.
    /// </summary>
    internal int UnblockCalls { get; private set; }

    /// <summary>Verifies test scenario for TestPortDefenderFirewall.cs.</summary>
    /// <summary>Verifies test scenario for TestPortDefenderFirewall.cs.</summary>
    /// <summary>Verifies test scenario for TestPortDefenderFirewall.cs.</summary>
    public FirewallOperationResult BlockIp(BlockIpRequest request)
    {
        BlockCalls++;
        if (ThrowOnBlock) throw new InvalidOperationException("Injected firewall block failure.");
        return BlockSucceeds ? FirewallOperationResult.Ok("Blocked", "TestRule") : FirewallOperationResult.Fail("Injected refusal");
    }

    /// <summary>Verifies test scenario for TestPortDefenderFirewall.cs.</summary>
    /// <summary>Verifies test scenario for TestPortDefenderFirewall.cs.</summary>
    /// <summary>Verifies test scenario for TestPortDefenderFirewall.cs.</summary>
    public FirewallOperationResult UnblockIp(UnblockIpRequest request)
    {
        UnblockCalls++;
        if (ThrowOnUnblock) throw new InvalidOperationException("Injected firewall unblock failure.");
        return UnblockSucceeds ? FirewallOperationResult.Ok("Unblocked") : FirewallOperationResult.Fail("Injected refusal");
    }
}
