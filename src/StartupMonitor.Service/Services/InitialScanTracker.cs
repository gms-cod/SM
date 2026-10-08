namespace StartupMonitor.Service.Services;

/// <summary>Отслеживает завершение начального сканирования всеми watcher'ами.
/// Пока скан не завершён — уведомления о новых записях подавляются (батч-режим).</summary>
public class InitialScanTracker
{
    /// <summary>TaskCompletionSource для сигнализации о завершении всех watcher'ов.</summary>
    private readonly TaskCompletionSource _completion = new();
    /// <summary>Сколько watcher'ов ещё не отчитались о завершении скана.</summary>
    private int _remaining;

    /// <param name="watchersCount">Сколько watcher'ов должны отчитаться через MarkCompleted.</param>
    public InitialScanTracker(int watchersCount = 5)
    {
        _remaining = watchersCount;
    }

    /// <summary>True, если все watcher'ы завершили сканирование.</summary>
    public bool IsCompleted => _remaining <= 0 || _completion.Task.IsCompleted;
    /// <summary>Текущая стадия сканирования (отображается в UI).</summary>
    public string CurrentStage { get; set; } = "Ожидание запуска сканирования...";

    /// <summary>Вызывается watcher'ом при завершении его этапа сканирования.
    /// Когда все отчитались — сигнализируем ожидающим.</summary>
    public void MarkCompleted()
    {
        CurrentStage = "Завершено";
        if (Interlocked.Decrement(ref _remaining) == 0)
            _completion.TrySetResult();
    }

    /// <summary>Ожидает завершения всех watcher'ов.</summary>
    public Task WaitAsync() => _completion.Task;
}
