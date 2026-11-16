namespace StartupMonitor.Shared.Models;

/// <summary>Matching method for blacklist rules against startup entries.</summary>
public enum BlacklistMatchType
{
    /// <summary>Exact file path match.</summary>
    Path,
    /// <summary>Wildcard file path match.</summary>
    PathWildcard,
    /// <summary>Publisher name match.</summary>
    Publisher,
    /// <summary>SHA-256 hash match.</summary>
    SHA256
}
