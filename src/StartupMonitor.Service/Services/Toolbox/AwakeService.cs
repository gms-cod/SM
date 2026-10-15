using System;
using System.Runtime.InteropServices;
using System.Threading;
using StartupMonitor.Shared.Models.Toolbox;

namespace StartupMonitor.Service.Services.Toolbox;

/// <summary>
/// Сервис управления режимом бодрствования системы и экрана (Keep-Awake / Caffeine).
/// Предотвращает уход в спящий режим и отключение мониторов во время скачивания, рендеринга и работы.
/// </summary>
public class AwakeService : IDisposable
{
    [Flags]
    private enum EXECUTION_STATE : uint
    {
        ES_AWAYMODE_REQUIRED = 0x00000040,
        ES_CONTINUOUS = 0x80000000,
        ES_DISPLAY_REQUIRED = 0x00000002,
        ES_SYSTEM_REQUIRED = 0x00000001
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern EXECUTION_STATE SetThreadExecutionState(EXECUTION_STATE esFlags);

    private readonly object _lock = new();
    private Timer? _timer;
    private bool _isActive;
    private DateTime _expiresAt = DateTime.MinValue;
    private bool _keepDisplayOn = true;
    private int _durationMinutes = 0;

    /// <summary>
    /// Активирует режим бодрствования на указанную длительность (0 = бессрочно).
    /// </summary>
    public AwakeStatusDto Activate(int durationMinutes, bool keepDisplayOn = true)
    {
        lock (_lock)
        {
            _timer?.Dispose();
            _timer = null;

            EXECUTION_STATE flags = EXECUTION_STATE.ES_CONTINUOUS | EXECUTION_STATE.ES_SYSTEM_REQUIRED;
            if (keepDisplayOn)
            {
                flags |= EXECUTION_STATE.ES_DISPLAY_REQUIRED;
            }

            SetThreadExecutionState(flags);
            _isActive = true;
            _keepDisplayOn = keepDisplayOn;
            _durationMinutes = durationMinutes;

            if (durationMinutes > 0)
            {
                _expiresAt = DateTime.UtcNow.AddMinutes(durationMinutes);
                _timer = new Timer(_ => Deactivate(), null, TimeSpan.FromMinutes(durationMinutes), Timeout.InfiniteTimeSpan);
            }
            else
            {
                _expiresAt = DateTime.MaxValue;
            }

            return GetStatusCore();
        }
    }

    /// <summary>
    /// Деактивирует режим бодрствования и восстанавливает стандартные параметры сна Windows.
    /// </summary>
    public AwakeStatusDto Deactivate()
    {
        lock (_lock)
        {
            _timer?.Dispose();
            _timer = null;

            SetThreadExecutionState(EXECUTION_STATE.ES_CONTINUOUS);
            _isActive = false;
            _expiresAt = DateTime.MinValue;
            _durationMinutes = 0;

            return GetStatusCore();
        }
    }

    /// <summary>
    /// Возвращает текущий статус режима бодрствования.
    /// </summary>
    public AwakeStatusDto GetStatus()
    {
        lock (_lock)
        {
            return GetStatusCore();
        }
    }

    private AwakeStatusDto GetStatusCore()
    {
        int remaining = 0;
        string modeText = "Выключен";

        if (_isActive)
        {
            if (_expiresAt == DateTime.MaxValue)
            {
                remaining = -1;
                modeText = "Бессрочно (до выключения)";
            }
            else
            {
                var diff = _expiresAt - DateTime.UtcNow;
                remaining = Math.Max(0, (int)Math.Ceiling(diff.TotalMinutes));
                modeText = $"Осталось {remaining} мин.";
            }
        }

        return new AwakeStatusDto
        {
            IsActive = _isActive,
            RemainingMinutes = remaining,
            KeepDisplayOn = _keepDisplayOn,
            ModeText = modeText
        };
    }

    public void Dispose()
    {
        Deactivate();
    }
}

