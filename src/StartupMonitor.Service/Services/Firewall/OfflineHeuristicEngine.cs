namespace StartupMonitor.Service.Services.Firewall;

using System;
using System.Collections.Generic;
using StartupMonitor.Shared.Models.Firewall;

/// <summary>
/// Автономный эвристический движок оценки инцидентов сетевой активности.
/// Работает без внешних зависимостей (ML, облака), используя детерминированные правила.
/// </summary>
public static class OfflineHeuristicEngine
{
    /// <summary>
    /// Множество хорошо известных портов, ассоциируемых с C2-фреймворками и бэкдорами.
    /// </summary>
    private static readonly HashSet<int> KnownC2Ports = new()
    {
        4444,   // Metasploit default
        1337,   // Leet / common backdoor
        31337,  // Back Orifice / elite
        6667,   // IRC (часто используется для C2)
        8088,   // Альтернативный HTTP / частый C2
        8888,   // Альтернативный HTTP / частый C2
        9999    // Частый дефолт малвари
    };

    /// <summary>
    /// Оценивает инцидент телеметрии и возвращает вердикт аномалии на основе эвристик.
    /// </summary>
    /// <param name="incident">Данные инцидента для оценки. Не может быть null.</param>
    /// <returns>Вердикт <see cref="AnomalyAiVerdictDto"/> с уровнем риска, меткой и рекомендуемым действием.</returns>
    /// <exception cref="ArgumentNullException">Выбрасывается, если <paramref name="incident"/> или его обязательные вложенные свойства равны null.</exception>
    public static AnomalyAiVerdictDto Evaluate(IncidentTelemetryDto incident)
    {
        ArgumentNullException.ThrowIfNull(incident);
        ArgumentNullException.ThrowIfNull(incident.Process);
        ArgumentNullException.ThrowIfNull(incident.Traffic);
        ArgumentNullException.ThrowIfNull(incident.Heuristics);

        var verdict = new AnomalyAiVerdictDto
        {
            IncidentId = incident.IncidentId,
            Source = "OfflineHeuristic",
            EvaluatedAt = DateTime.UtcNow
        };

        // 1. Критическая угроза: Известный C2 порт ИЛИ Биконинг + Пользовательский путь + Без подписи/Unsigned
        bool isKnownC2Port = KnownC2Ports.Contains(incident.Traffic.RemotePort);
        bool isUnsignedOrMissing = string.IsNullOrWhiteSpace(incident.Process.Signer) ||
                                   incident.Process.Signer.Equals("Unsigned", StringComparison.OrdinalIgnoreCase);

        if (isKnownC2Port ||
            (incident.Heuristics.IsBeaconing && incident.Heuristics.IsUserWritablePath && isUnsignedOrMissing))
        {
            verdict.RiskLevel = 5;
            verdict.RiskLabel = "Критическая угроза (C2 / Trojan)";
            verdict.RecommendedAction = "BlockAlways";
            verdict.UserExplanation = "Обнаружено соединение с портом, характерным для командных серверов (C2), либо процесс без цифровой подписи из пользовательской папки ведет себя как бэкдор (биконинг). Это признак активного заражения.";
            verdict.TechnicalSummary = $"RemotePort={incident.Traffic.RemotePort} (KnownC2={isKnownC2Port}), Beaconing={incident.Heuristics.IsBeaconing}, UserWritablePath={incident.Heuristics.IsUserWritablePath}, Signer='{incident.Process.Signer}', IsSystem={incident.Process.IsSystem}.";
            return verdict;
        }

        // 2. Высокий риск: Биконинг с низким джиттером (автоматизация) + не системный процесс
        if (incident.Heuristics.IsBeaconing && incident.Traffic.JitterPercent <= 15.0 && !incident.Process.IsSystem)
        {
            verdict.RiskLevel = 4;
            verdict.RiskLabel = "Высокий риск (C2 Beaconing)";
            verdict.RecommendedAction = "BlockAlways";
            verdict.UserExplanation = "Процесс устанавливает регулярные соединения с минимальными вариациями во времени (низкий джиттер), что типично для автоматических имплантов C2. Процесс не является системным.";
            verdict.TechnicalSummary = $"Beaconing=True, JitterPercent={incident.Traffic.JitterPercent:F1}%, IsSystem={incident.Process.IsSystem}, ProcessPath='{incident.Process.Path}'.";
            return verdict;
        }

        // 3. Подозрительно: Нестандартный порт ИЛИ высокий базовый RiskScore трафика
        if (incident.Heuristics.IsNonStandardPort || incident.Traffic.RiskScore >= 60)
        {
            verdict.RiskLevel = 3;
            verdict.RiskLabel = "Подозрительная активность";
            verdict.RecommendedAction = "BlockSession";
            verdict.UserExplanation = "Обнаружено соединение на нестандартный порт или трафик имеет высокий внутренний рейтинг риска (эвристики DPI/репутации). Требуется внимание администратора.";
            verdict.TechnicalSummary = $"IsNonStandardPort={incident.Heuristics.IsNonStandardPort}, TrafficRiskScore={incident.Traffic.RiskScore}, RemotePort={incident.Traffic.RemotePort}.";
            return verdict;
        }

        // 4. Доверенный / Безопасно: Системный процесс ИЛИ Подписанный легитимный процесс без признаков биконинга/польз.пути
        bool isTrustedSigner = !string.IsNullOrWhiteSpace(incident.Process.Signer) &&
                               !incident.Process.Signer.Equals("Unsigned", StringComparison.OrdinalIgnoreCase);

        if (incident.Process.IsSystem || (isTrustedSigner && !incident.Heuristics.IsBeaconing && !incident.Heuristics.IsUserWritablePath))
        {
            verdict.RiskLevel = 1;
            verdict.RiskLabel = "Безопасно (Легитимный процесс)";
            verdict.RecommendedAction = "AllowAlways";
            verdict.UserExplanation = "Процесс является частью операционной системы или имеет валидную цифровую подпись доверенного издателя, не проявляет признаков биконинга и не находится в пользовательских папках.";
            verdict.TechnicalSummary = $"IsSystem={incident.Process.IsSystem}, Signer='{incident.Process.Signer}', Beaconing={incident.Heuristics.IsBeaconing}, UserWritablePath={incident.Heuristics.IsUserWritablePath}.";
            return verdict;
        }

        // 5. По умолчанию: Низкий риск
        verdict.RiskLevel = 2;
        verdict.RiskLabel = "Низкий риск";
        verdict.RecommendedAction = "AllowSession";
        verdict.UserExplanation = "Активность не подпадает под критические или высокорисковые профили, но процесс не имеет явных признаков доверия (подпись отсутствует или процесс в пользовательской папке). Разрешено в рамках текущей сессии.";
        verdict.TechnicalSummary = $"Default fallback. IsSystem={incident.Process.IsSystem}, Signer='{incident.Process.Signer}', Beaconing={incident.Heuristics.IsBeaconing}, UserWritablePath={incident.Heuristics.IsUserWritablePath}, RiskScore={incident.Traffic.RiskScore}.";

        return verdict;
    }
}
