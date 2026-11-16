namespace StartupMonitor.Shared.Models;

/// <summary>Blacklist rule: match value, match type, and action upon trigger.</summary>
public class BlacklistRule
{
    /// <summary>Unique rule identifier.</summary>
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    /// <summary>Value to match against (path, hash, publisher).</summary>
    public string Value { get; set; } = string.Empty;
    /// <summary>Method of matching value against entry.</summary>
    public BlacklistMatchType MatchType { get; set; } = BlacklistMatchType.Path;
    /// <summary>Action executed when rule matches.</summary>
    public BlacklistAction Action { get; set; } = BlacklistAction.NotifyAndRemove;
    /// <summary>Creation date and time of the rule.</summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    /// <summary>Optional comment describing rule purpose.</summary>
    public string Comment { get; set; } = string.Empty;
    /// <summary>Creates an independent clone of the rule.</summary>
    public BlacklistRule Clone() => (BlacklistRule)MemberwiseClone();
}