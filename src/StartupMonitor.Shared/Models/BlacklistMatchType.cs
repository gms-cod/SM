namespace StartupMonitor.Shared.Models;

/// <summary>Способ сопоставления правила чёрного списка с записью.</summary>
public enum BlacklistMatchType
{
    /// <summary>Точное совпадение пути к файлу.</summary>
    Path,
    /// <summary>Совпадение пути по маске (wildcard).</summary>
    PathWildcard,
    /// <summary>Совпадение по издателю (Publisher).</summary>
    Publisher,
    /// <summary>Совпадение по SHA256-хешу.</summary>
    SHA256
}
