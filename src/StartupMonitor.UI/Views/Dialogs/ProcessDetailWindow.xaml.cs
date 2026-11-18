using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows;
using StartupMonitor.Shared.Models;
using StartupMonitor.UI.ViewModels;

namespace StartupMonitor.UI.Views.Dialogs;

/// <summary>
/// Окно детальных свойств процесса, включая комплексный рейтинг безопасности (Kaspersky OpenTIP и ИИ).
/// </summary>
public partial class ProcessDetailWindow
{
    private readonly ProcessInfo _process;
    private readonly MainViewModel _mainVm;

    /// <summary>Информация о просматриваемом процессе.</summary>
    public ProcessInfo Process => _process;

    /// <summary>Сводный текстовый вердикт безопасности.</summary>
    public string SafetyVerdictSummary
    {
        get
        {
            if (_process.HasKasperskyThreat || (_process.AiRiskScore.HasValue && _process.AiRiskScore.Value >= 75) ||
                (_process.ThreatInfo != null && _process.ThreatInfo.ThreatLevel == CliThreatLevel.High))
            {
                return "КРИТИЧЕСКАЯ УГРОЗА";
            }

            if (string.Equals(_process.KasperskyZone, "Yellow", StringComparison.OrdinalIgnoreCase) ||
                (_process.AiRiskScore.HasValue && _process.AiRiskScore.Value >= 45) ||
                (_process.ThreatInfo != null && _process.ThreatInfo.ThreatLevel == CliThreatLevel.Medium))
            {
                return "ПОДОЗРИТЕЛЬНЫЙ";
            }

            if ((_process.AiRiskScore.HasValue && _process.AiRiskScore.Value >= 20) ||
                (_process.ThreatInfo != null && _process.ThreatInfo.ThreatLevel == CliThreatLevel.Low))
            {
                return "НИЗКИЙ РИСК";
            }

            if (string.Equals(_process.KasperskyZone, "Green", StringComparison.OrdinalIgnoreCase) ||
                (_process.AiRiskScore.HasValue && _process.AiRiskScore.Value < 20) ||
                _process.IsSystemProcess)
            {
                return "БЕЗОПАСЕН (ПРОВЕРЕНО)";
            }

            return "АНАЛИЗ НА ЛЕТУ...";
        }
    }

    /// <summary>Отображаемая сигнатура Kaspersky.</summary>
    public string DisplayKasperskyDetection => !string.IsNullOrWhiteSpace(_process.KasperskyDetection)
        ? _process.KasperskyDetection
        : (string.Equals(_process.KasperskyZone, "Green", StringComparison.OrdinalIgnoreCase) ? "Угроз не обнаружено (Чисто)" : "Сигнатуры отсутствуют");

    /// <summary>Отображаемое объяснение ИИ.</summary>
    public string DisplayAiExplanation => !string.IsNullOrWhiteSpace(_process.AiExplanation)
        ? _process.AiExplanation
        : "Поведенческий анализ выполняется непрерывно службой в фоновом режиме.";

    /// <summary>
    /// Создает экземпляр окна детальных свойств процесса.
    /// </summary>
    /// <param name="process">Модель процесса.</param>
    /// <param name="mainVm">Главная модель представления.</param>
    public ProcessDetailWindow(ProcessInfo process, MainViewModel mainVm)
    {
        _process = process ?? throw new ArgumentNullException(nameof(process));
        _mainVm = mainVm ?? throw new ArgumentNullException(nameof(mainVm));
        DataContext = this;
        InitializeComponent();
    }

    private void OnKillProcessClick(object sender, RoutedEventArgs e)
    {
        var result = MessageBox.Show(
            $"Вы действительно хотите принудительно завершить процесс {_process.ProcessName} (PID: {_process.ProcessId})?",
            "Подтверждение завершения",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (result == MessageBoxResult.Yes)
        {
            _mainVm.TaskManager?.KillProcessCommand.Execute(_process);
            Close();
        }
    }

    private void OnCopyAllClick(object sender, RoutedEventArgs e)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"=== Startup Monitor: Свойства процесса ===");
        sb.AppendLine($"Имя процесса: {_process.ProcessName}");
        sb.AppendLine($"PID: {_process.ProcessId}");
        sb.AppendLine($"Родительский PID: {_process.ParentProcessId}");
        sb.AppendLine($"Путь к файлу: {_process.FilePath}");
        sb.AppendLine($"SHA-256: {_process.Sha256Hash}");
        sb.AppendLine($"Командная строка: {_process.CommandLine}");
        sb.AppendLine($"Издатель / Подпись: {_process.Publisher}");
        sb.AppendLine($"Системный процесс: {_process.IsSystemProcess}");
        sb.AppendLine();
        sb.AppendLine($"--- Рейтинги безопасности ---");
        sb.AppendLine($"Сводный вердикт: {SafetyVerdictSummary}");
        sb.AppendLine($"Kaspersky OpenTIP: {_process.KasperskyZoneText} (Зона: {_process.KasperskyZone}, Детект: {DisplayKasperskyDetection})");
        sb.AppendLine($"ИИ-поведение: {_process.DisplayAiVerdict}");
        sb.AppendLine($"ИИ-обоснование: {DisplayAiExplanation}");
        if (_process.ThreatInfo != null && _process.ThreatInfo.HasThreat)
        {
            sb.AppendLine($"CLI-эвристика: {_process.ThreatInfo.BadgeText} ({_process.ThreatInfo.Description})");
        }
        sb.AppendLine();
        sb.AppendLine($"--- Использование ресурсов ---");
        sb.AppendLine($"ЦП: {_process.DisplayCpu}");
        sb.AppendLine($"Память: {_process.DisplayRam}");
        sb.AppendLine($"Диск: {_process.DisplayDisk}");
        sb.AppendLine($"Сеть: {_process.DisplayNetwork}");

        try
        {
            Clipboard.SetText(sb.ToString());
            MessageBox.Show("Полная сводка о процессе скопирована в буфер обмена.", "Скопировано", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch { }
    }

    private void OnCopyPathClick(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(_process.FilePath))
        {
            try { Clipboard.SetText(_process.FilePath); } catch { }
        }
    }

    private void OnOpenFolderClick(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(_process.FilePath) && File.Exists(_process.FilePath))
        {
            try
            {
                System.Diagnostics.Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{_process.FilePath}\"") { UseShellExecute = true });
            }
            catch { }
        }
    }

    private void OnCopySha256Click(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(_process.Sha256Hash))
        {
            try { Clipboard.SetText(_process.Sha256Hash); } catch { }
        }
    }

    private void OnCopyCommandLineClick(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(_process.CommandLine))
        {
            try { Clipboard.SetText(_process.CommandLine); } catch { }
        }
    }

    private void OnOpenConnectionsClick(object sender, RoutedEventArgs e)
    {
        _mainVm.TaskManager?.OpenProcessConnectionsCommand.Execute(_process);
    }

    private void OnOpenModulesClick(object sender, RoutedEventArgs e)
    {
        _mainVm.TaskManager?.OpenProcessModulesCommand.Execute(_process);
    }

    private void OnCloseClick(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
