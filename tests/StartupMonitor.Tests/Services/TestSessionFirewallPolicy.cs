using StartupMonitor.Service.Services.Firewall;
using StartupMonitor.Shared.Models.Firewall;

namespace StartupMonitor.Tests.Services;

/// <summary>Verifies test scenario for TestSessionFirewallPolicy.cs.</summary>
internal sealed class TestSessionFirewallPolicy : ISessionFirewallPolicy
{
    private readonly Dictionary<string, (FirewallAction Action, TimeSpan Lifetime)> _active = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Verifies test scenario for TestSessionFirewallPolicy.cs.</summary>
    internal bool ApplySucceeds { get; set; } = true;

    /// <summary>Verifies test scenario for TestSessionFirewallPolicy.cs.</summary>
    public bool CanAllowSession { get; set; } = true;

    /// <summary>Verifies test scenario for TestSessionFirewallPolicy.cs.</summary>
    internal int ApplyCalls { get; private set; }

    /// <summary>Verifies test scenario for TestSessionFirewallPolicy.cs.</summary>
    internal string? LastPath { get; private set; }

    /// <summary>Verifies test scenario for TestSessionFirewallPolicy.cs.</summary>
    internal FirewallAction? LastAction { get; private set; }

    /// <summary>Verifies test scenario for TestSessionFirewallPolicy.cs.</summary>
    internal TimeSpan? LastLifetime { get; private set; }

    /// <summary>Verifies test scenario for TestSessionFirewallPolicy.cs.</summary>
    /// <summary>Verifies test scenario for TestSessionFirewallPolicy.cs.</summary>
    /// <summary>Verifies test scenario for TestSessionFirewallPolicy.cs.</summary>
    /// <summary>Verifies test scenario for TestSessionFirewallPolicy.cs.</summary>
    /// <summary>Verifies test scenario for TestSessionFirewallPolicy.cs.</summary>
    public FirewallOperationResult Apply(string path, FirewallAction action, TimeSpan lifetime)
    {
        ApplyCalls++;
        LastPath = path;
        LastAction = action;
        LastLifetime = lifetime;
        if (!ApplySucceeds) return FirewallOperationResult.Fail("Controlled session policy refusal");
        _active[path] = (action, lifetime);
        return FirewallOperationResult.Ok("Controlled session policy applied");
    }

    /// <summary>Verifies test scenario for TestSessionFirewallPolicy.cs.</summary>
    /// <summary>Verifies test scenario for TestSessionFirewallPolicy.cs.</summary>
    public void Remove(string path) => _active.Remove(path);

    /// <summary>Verifies test scenario for TestSessionFirewallPolicy.cs.</summary>
    /// <summary>Verifies test scenario for TestSessionFirewallPolicy.cs.</summary>
    /// <summary>Verifies test scenario for TestSessionFirewallPolicy.cs.</summary>
    public bool IsActive(string path) => _active.ContainsKey(path);

    /// <summary>Verifies test scenario for TestSessionFirewallPolicy.cs.</summary>
    public void Dispose() => _active.Clear();
}
