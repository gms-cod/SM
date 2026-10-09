using System.Collections.Generic;

namespace StartupMonitor.Shared.Models;

/// <summary>
/// Запрос на инспекцию загруженных модулей и дескрипторов процесса.
/// </summary>
public class GetProcessModulesRequest
{
    /// <summary>Идентификатор целевого процесса (PID).</summary>
    public int ProcessId { get; set; }

    /// <summary>Флаг включения сбора открытых дескрипторов (Handles).</summary>
    public bool IncludeHandles { get; set; } = true;
}

/// <summary>
/// Результат инспекции загруженных модулей и дескрипторов процесса.
/// </summary>
public class ProcessModulesResponse
{
    /// <summary>Идентификатор процесса (PID).</summary>
    public int ProcessId { get; set; }

    /// <summary>Имя исполняемого файла процесса.</summary>
    public string ProcessName { get; set; } = string.Empty;

    /// <summary>Список загруженных динамических библиотек (DLL).</summary>
    public List<ProcessModuleInfo> Modules { get; set; } = new();

    /// <summary>Список открытых системных дескрипторов (Handles).</summary>
    public List<ProcessHandleInfo> Handles { get; set; } = new();

    /// <summary>Признак успешности выполнения операции.</summary>
    public bool Success { get; set; } = true;

    /// <summary>Сообщение об ошибке, если операция не удалась.</summary>
    public string? ErrorMessage { get; set; }
}
