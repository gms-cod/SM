using StartupMonitor.Shared.Models.Firewall;

namespace StartupMonitor.Service.Services.Firewall;

/// <summary>Граница подтверждённых операций брандмауэра, используемых ядром защиты портов.</summary>
public interface IPortDefenderFirewall
{
    /// <summary>Применяет блокировку адреса и сообщает фактический результат изменения правил.</summary>
    /// <param name="request">Параметры блокировки адреса.</param>
    /// <returns>Подтверждение успеха либо описание отказа брандмауэра.</returns>
    FirewallOperationResult BlockIp(BlockIpRequest request);

    /// <summary>Идемпотентно снимает управляемую блокировку адреса.</summary>
    /// <param name="request">Адрес и направления снимаемой блокировки.</param>
    /// <returns>Подтверждение отсутствия управляемой блокировки либо описание отказа.</returns>
    FirewallOperationResult UnblockIp(UnblockIpRequest request);
}
