using System.Globalization;
using StartupMonitor.Shared.Models;
using StartupMonitor.Shared.Models.Firewall;
using StartupMonitor.UI.Services;

namespace StartupMonitor.UI.Services;

/// <summary>
/// Маппинг сетевых статусов (сокеты, баны, брандмауэр) в локализованные UI-строки.
/// Централизует логику, ранее разбросанную по окнам, для покрытия тестами.
/// </summary>
public static class FirewallUiMapper
{
    /// <summary>Возвращает ключ ресурса для статуса брандмауэра.</summary>
    public static string GetBlockStatusKey(FirewallBlockStatus status) => status switch
    {
        FirewallBlockStatus.Allowed => "FwStatusAllowed",
        FirewallBlockStatus.BlockedInbound => "FwStatusBlockedInbound",
        FirewallBlockStatus.BlockedOutbound => "FwStatusBlockedOutbound",
        FirewallBlockStatus.BlockedBoth => "FwStatusBlockedBoth",
        _ => "FwStatusAllowed",
    };

    /// <summary>Возвращает локализованный текст статуса брандмауэра.</summary>
    public static string GetBlockStatusText(FirewallBlockStatus status)
    {
        var loc = LocalizationService.Instance;
        return status switch
        {
            FirewallBlockStatus.Allowed => loc.FwStatusAllowed,
            FirewallBlockStatus.BlockedInbound => loc.FwStatusBlockedInbound,
            FirewallBlockStatus.BlockedOutbound => loc.FwStatusBlockedOutbound,
            FirewallBlockStatus.BlockedBoth => loc.FwStatusBlockedBoth,
            _ => loc.FwStatusAllowed,
        };
    }

    /// <summary>
    /// Возвращает UI-статус сокета: приватный адрес под защитой Defender,
    /// заблокированный IP, активный бан либо разрешённый.
    /// </summary>
    /// <param name="socket">Сокет для оценки.</param>
    /// <param name="blockStatus">Статус IP в брандмауэре.</param>
    /// <param name="isBanned">Находится ли IP в активных банах Port Defender.</param>
    public static string GetSocketStatusText(ProcessNetworkSocket socket, FirewallBlockStatus blockStatus, bool isBanned)
    {
        var loc = LocalizationService.Instance;
        if (isBanned)
            return loc.FwStatusBanned;
        if (blockStatus != FirewallBlockStatus.Allowed)
            return loc.FwStatusBlocked;
        if (socket.IsRemotePrivate || ProcessNetworkSocket.CheckIsPrivateAddress(socket.RemoteAddress))
            return loc.FwStatusProtected;
        return loc.FwStatusAllowed;
    }

    /// <summary>Возвращает текст бана: перманентный либо временный с датой истечения.</summary>
    public static string GetBanStatusText(ActiveBanDto ban)
    {
        var loc = LocalizationService.Instance;
        if (ban.IsPermanent)
            return loc.GetPortDefenderBanPermanent(ban.RepeatCount);
        var expiresText = ban.ExpiresAtUtc?.ToLocalTime().ToString("g", CultureInfo.CurrentUICulture) ?? "?";
        return loc.GetPortDefenderBanStatus(expiresText, ban.RepeatCount);
    }

    /// <summary>Возвращает локализованную подпись длительности бана для меню (15м / 1ч / 24ч / навсегда).</summary>
    /// <param name="banDurationMinutes">Длительность в минутах; 0 или null = навсегда.</param>
    public static string GetBanDurationText(int? banDurationMinutes)
    {
        var loc = LocalizationService.Instance;
        return banDurationMinutes switch
        {
            15 => loc.BanDuration15m,
            60 => loc.BanDuration1h,
            1440 => loc.BanDuration24h,
            0 or null => loc.BanPermanent,
            var m => m < 60 ? loc.BanDuration15m : loc.BanPermanent,
        };
    }

    /// <summary>Формирует строку счётчика сокетов через локализованный формат.</summary>
    public static string FormatSocketCount(int total, int established, int listening) =>
        LocalizationService.Instance.GetFwSocketCount(total, established, listening);

    /// <summary>
    /// Валидация правила Port Defender для UI (без побочных эффектов).
    /// </summary>
    /// <returns>Кортеж (IsValid, Error). Error — человекочитаемая причина.</returns>
    public static (bool IsValid, string? Error) ValidateRule(PortDefenderRuleConfig rule)
    {
        if (rule.LocalPort < 0 || rule.LocalPort > 65535)
            return (false, $"Invalid port: {rule.LocalPort}. Expected 0-65535.");
        if (rule.ThresholdCount < 1)
            return (false, "ThresholdCount must be >= 1.");
        if (rule.BanDurationMinutes < 0)
            return (false, "BanDurationMinutes must be >= 0 (0 = permanent).");
        foreach (var ip in rule.WhitelistIps)
        {
            if (!FirewallGuard.IsBlockableAddress(ip, out _))
            {
                // Белый список может содержать loopback/частные — проверяем только парсинг.
                var addrPart = ip.Split('/')[0].Trim().Trim('[', ']');
                if (!System.Net.IPAddress.TryParse(addrPart, out _))
                    return (false, $"Invalid whitelist entry: {ip}.");
            }
        }
        return (true, null);
    }
}
