using System;

namespace StartupMonitor.Shared.Models;

/// <summary>
/// Информация об открытом системном дескрипторе процесса (Handle).
/// </summary>
public class ProcessHandleInfo
{
    /// <summary>Значение дескриптора (например, 0x000000A4).</summary>
    public string HandleValue { get; set; } = string.Empty;

    /// <summary>Тип системного объекта (File, Key, Mutant, Section, Event, Process, Thread и др.).</summary>
    public string HandleType { get; set; } = string.Empty;

    /// <summary>Имя или путь ресурса (путь к файлу, ветка реестра, имя мьютекса или события).</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Маска доступа (GrantedAccess).</summary>
    public uint GrantedAccess { get; set; }

    /// <summary>Шестнадцатеричная строка доступа.</summary>
    public string AccessString => $"0x{GrantedAccess:X8}";
}
