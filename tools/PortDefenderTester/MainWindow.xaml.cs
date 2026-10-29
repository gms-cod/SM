using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Windows;
using System.Windows.Controls;

namespace PortDefenderTester;

/// <summary>
/// Главное окно сетевого стенда для тестирования Port Defender, порогов атак и правил брандмауэра.
/// </summary>
public partial class MainWindow : Window
{
    private CancellationTokenSource? _cts;
    private bool _isRunning;
    private int _successCount;
    private int _refusedCount;
    private int _timeoutCount;
    private int _sentCount;
    private int _consecutiveDropCount;
    private bool _banDetected;

    /// <summary>
    /// Инициализирует компоненты окна и выполняет первичный расчет времени.
    /// </summary>
    public MainWindow()
    {
        InitializeComponent();
        UpdateCalculatedDuration();
    }

    /// <summary>
    /// Пересчитывает и выводит расчётную длительность тестирования в секундах.
    /// </summary>
    private void UpdateCalculatedDuration()
    {
        if (TxtRate == null || TxtTotal == null || TxtCalculatedDuration == null)
            return;

        if (int.TryParse(TxtRate.Text.Trim(), out var rate) && rate > 0 &&
            int.TryParse(TxtTotal.Text.Trim(), out var total) && total > 0)
        {
            var seconds = (double)total / rate;
            var proto = (CmbProtocol?.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "TCP";
            var modeName = RbModeScan?.IsChecked == true ? "Сканирование портов" :
                           RbModeAlert?.IsChecked == true ? "Проверка алерта" : "Штурм порта";

            TxtCalculatedDuration.Text = $"Расчётное время: {seconds:F1} сек ({total} попыток при {rate} поп/сек, {proto}, режим: {modeName})";
        }
        else
        {
            TxtCalculatedDuration.Text = "Укажите корректные числовые параметры (Rate > 0, Total > 0)";
        }
    }

    /// <summary>
    /// Обработчик переключения режимов испытания (Stress / Sweep / Probe).
    /// </summary>
    private void OnModeChanged(object sender, RoutedEventArgs e)
    {
        if (LblPort == null || TxtPort == null || TxtIp == null || TxtRate == null || TxtTotal == null)
            return;

        if (RbModeScan?.IsChecked == true)
        {
            LblPort.Text = "Порты (список):";
            if (TxtPort.Text == "445" || TxtPort.Text == "80")
            {
                TxtPort.Text = "135,139,445,3389,80,443";
            }
        }
        else if (RbModeAlert?.IsChecked == true)
        {
            LblPort.Text = "Порт (цель):";
            TxtIp.Text = "1.1.1.1";
            TxtPort.Text = "80";
            TxtRate.Text = "1";
            TxtTotal.Text = "3";
        }
        else
        {
            LblPort.Text = "Порт (цель):";
            if (TxtPort.Text.Contains(','))
            {
                TxtPort.Text = "445";
            }
        }

        UpdateCalculatedDuration();
    }

    /// <summary>
    /// Обработчик изменения текстовых полей параметров.
    /// </summary>
    private void OnParametersChanged(object sender, TextChangedEventArgs e)
    {
        UpdateCalculatedDuration();
    }

    /// <summary>
    /// Обработчик смены протокола (TCP / UDP).
    /// </summary>
    private void OnProtocolChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdateCalculatedDuration();
    }

    /// <summary>
    /// Устанавливает стандартный порт из кнопок быстрых пресетов.
    /// </summary>
    private void OnPresetPortClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string portStr)
        {
            TxtPort.Text = portStr;
            if (CmbProtocol != null) CmbProtocol.SelectedIndex = 0; // TCP
            if (RbModeStress != null) RbModeStress.IsChecked = true;
            UpdateCalculatedDuration();
        }
    }

    /// <summary>
    /// Пресет для проверки DNS (порт 53 UDP).
    /// </summary>
    private void OnPresetDnsUdpClick(object sender, RoutedEventArgs e)
    {
        TxtPort.Text = "53";
        if (CmbProtocol != null) CmbProtocol.SelectedIndex = 1; // UDP
        if (RbModeStress != null) RbModeStress.IsChecked = true;
        UpdateCalculatedDuration();
    }

    /// <summary>
    /// Пресет для проверки алерта фаервола при обращении к внешнему адресу (Cloudflare WAN 1.1.1.1).
    /// </summary>
    private void OnPresetWanAlertClick(object sender, RoutedEventArgs e)
    {
        TxtIp.Text = "1.1.1.1";
        TxtPort.Text = "80";
        TxtRate.Text = "1";
        TxtTotal.Text = "3";
        if (CmbProtocol != null) CmbProtocol.SelectedIndex = 0; // TCP
        if (RbModeAlert != null) RbModeAlert.IsChecked = true;
        UpdateCalculatedDuration();
    }

    /// <summary>
    /// Очищает журнал сокетов.
    /// </summary>
    private void OnClearLogClick(object sender, RoutedEventArgs e)
    {
        TxtLog.Clear();
    }

    /// <summary>
    /// Экспортирует итоговый протокол испытаний в текстовый файл.
    /// </summary>
    private void OnExportReportClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var sfd = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "Текстовый отчёт (*.txt)|*.txt|Все файлы (*.*)|*.*",
                FileName = $"PortDefenderTest_{DateTime.Now:yyyyMMdd_HHmmss}.txt",
                Title = "Сохранение отчёта испытаний"
            };

            if (sfd.ShowDialog() == true)
            {
                var sb = new StringBuilder();
                sb.AppendLine("======================================================================");
                sb.AppendLine("ОТЧЁТ ТЕСТИРОВАНИЯ УСТОЙЧИВОСТИ PORT DEFENDER & FIREWALL");
                sb.AppendLine($"Дата и время: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                sb.AppendLine($"Целевой IP: {TxtIp.Text.Trim()}");
                sb.AppendLine($"Порты / Спецификация: {TxtPort.Text.Trim()}");
                sb.AppendLine($"Протокол: {(CmbProtocol.SelectedItem as ComboBoxItem)?.Content ?? "TCP"}");
                sb.AppendLine($"Всего попыток: {_sentCount}");
                sb.AppendLine($"Успешных подключений: {_successCount}");
                sb.AppendLine($"Отказов сокета (Refused): {_refusedCount}");
                sb.AppendLine($"Таймаутов / Сбросов (Drop): {_timeoutCount}");
                sb.AppendLine($"Блокировка Port Defender: {(_banDetected ? "ОБНАРУЖЕНА (IP заблокирован брандмауэром)" : "Не зафиксирована")}");
                sb.AppendLine("======================================================================");
                sb.AppendLine();
                sb.AppendLine("ПОЛНЫЙ ЖУРНАЛ СОКЕТОВ:");
                sb.AppendLine(TxtLog.Text);

                File.WriteAllText(sfd.FileName, sb.ToString(), Encoding.UTF8);
                MessageBox.Show($"Отчёт успешно сохранён:\n{sfd.FileName}", "Экспорт отчёта", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Ошибка при сохранении отчёта: {ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    /// <summary>
    /// Разбирает строку портов (одиночный порт, список через запятую или диапазон).
    /// </summary>
    private List<int> ParseTargetPorts(string portText)
    {
        var result = new List<int>();
        var parts = portText.Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries);

        foreach (var part in parts)
        {
            if (part.Contains('-'))
            {
                var range = part.Split('-');
                if (range.Length == 2 && int.TryParse(range[0], out var start) && int.TryParse(range[1], out var end))
                {
                    var min = Math.Min(start, end);
                    var max = Math.Max(start, end);
                    for (var p = min; p <= max && result.Count < 500; p++)
                    {
                        if (p >= 1 && p <= 65535 && !result.Contains(p))
                            result.Add(p);
                    }
                }
            }
            else if (int.TryParse(part.Trim(), out var p) && p >= 1 && p <= 65535)
            {
                if (!result.Contains(p))
                    result.Add(p);
            }
        }

        if (result.Count == 0)
            result.Add(445);

        return result;
    }

    /// <summary>
    /// Запускает цикл генерации подключений.
    /// </summary>
    private async void OnStartClick(object sender, RoutedEventArgs e)
    {
        if (_isRunning) return;

        // Валидация IP
        var ipStr = TxtIp.Text.Trim();
        if (!IPAddress.TryParse(ipStr, out var targetIp))
        {
            MessageBox.Show("Некорректный IP-адрес цели!", "Ошибка валидации", MessageBoxButton.OK, MessageBoxImage.Warning);
            TxtIp.Focus();
            return;
        }

        // Парсинг целевых портов
        var targetPorts = ParseTargetPorts(TxtPort.Text.Trim());
        if (targetPorts.Count == 0)
        {
            MessageBox.Show("Укажите хотя бы один корректный порт (1–65535)!", "Ошибка валидации", MessageBoxButton.OK, MessageBoxImage.Warning);
            TxtPort.Focus();
            return;
        }

        // Валидация частоты
        if (!int.TryParse(TxtRate.Text.Trim(), out var rate) || rate <= 0 || rate > 2000)
        {
            MessageBox.Show("Частота должна быть от 1 до 2000 попыток в секунду!", "Ошибка валидации", MessageBoxButton.OK, MessageBoxImage.Warning);
            TxtRate.Focus();
            return;
        }

        // Валидация общего количества
        if (!int.TryParse(TxtTotal.Text.Trim(), out var totalAttempts) || totalAttempts <= 0)
        {
            MessageBox.Show("Общее количество попыток должно быть больше 0!", "Ошибка валидации", MessageBoxButton.OK, MessageBoxImage.Warning);
            TxtTotal.Focus();
            return;
        }

        var isUdp = (CmbProtocol.SelectedItem as ComboBoxItem)?.Content?.ToString() == "UDP";
        var protocolName = isUdp ? "UDP" : "TCP";
        var isSweep = RbModeScan?.IsChecked == true;
        var isAlertProbe = RbModeAlert?.IsChecked == true;

        _isRunning = true;
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;

        BtnStart.IsEnabled = false;
        BtnStop.IsEnabled = true;
        SetInputsEnabled(false);

        _sentCount = 0;
        _successCount = 0;
        _refusedCount = 0;
        _timeoutCount = 0;
        _consecutiveDropCount = 0;
        _banDetected = false;
        BadgeBanDetected.Visibility = Visibility.Collapsed;

        PrgProgress.Maximum = totalAttempts;
        PrgProgress.Value = 0;
        TxtProgressCount.Text = $"0 / {totalAttempts}";
        TxtSuccessCount.Text = "0";
        TxtRefusedCount.Text = "0";
        TxtTimeoutCount.Text = "0";

        var modeLabel = isSweep ? "Сканирование пула портов" :
                        isAlertProbe ? "Зондирование алерта брандмауэра" : "Штурм порта";
        TxtStatus.Text = $"{modeLabel} ({protocolName})...";

        var portDesc = targetPorts.Count == 1 ? targetPorts[0].ToString() : string.Join(",", targetPorts);
        AppendLog($"=== Старт теста: {targetIp}:[{portDesc}] ({protocolName}) | Режим: {modeLabel} | Скорость: {rate}/сек | Всего: {totalAttempts} ===");

        var stopwatch = Stopwatch.StartNew();
        var expectedIntervalMs = 1000.0 / rate;

        try
        {
            for (var i = 1; i <= totalAttempts; i++)
            {
                if (ct.IsCancellationRequested)
                    break;

                var attemptIndex = i;
                // Выбираем порт: циклически по списку при сканировании, либо фиксированный при штурме
                var port = targetPorts[(attemptIndex - 1) % targetPorts.Count];
                var attemptStartTicks = Stopwatch.GetTimestamp();

                // Запускаем асинхронную попытку подключения
                _ = Task.Run(async () =>
                {
                    if (isUdp)
                    {
                        await ProbeUdpAsync(targetIp, port, attemptIndex, ct);
                    }
                    else
                    {
                        await ProbeTcpAsync(targetIp, port, attemptIndex, ct);
                    }
                }, ct);

                _sentCount++;

                // Обновляем прогресс регулярно
                if (_sentCount % Math.Max(1, rate / 10) == 0 || _sentCount == totalAttempts)
                {
                    UpdateUiProgress(totalAttempts, stopwatch.Elapsed.TotalSeconds, rate);
                }

                // Расчет интервала до следующей отправки для соблюдения заданной частоты
                var elapsedMs = Stopwatch.GetElapsedTime(attemptStartTicks).TotalMilliseconds;
                var sleepMs = expectedIntervalMs - elapsedMs;
                if (sleepMs > 1)
                {
                    await Task.Delay((int)sleepMs, ct);
                }
            }

            // Ожидание завершения последних пакетов
            await Task.Delay(400, CancellationToken.None);
        }
        catch (OperationCanceledException)
        {
            AppendLog("⏹ Тестирование остановлено пользователем.");
        }
        catch (Exception ex)
        {
            AppendLog($"❌ Ошибка генератора: {ex.Message}");
        }
        finally
        {
            stopwatch.Stop();
            _isRunning = false;
            BtnStart.IsEnabled = true;
            BtnStop.IsEnabled = false;
            SetInputsEnabled(true);

            UpdateUiProgress(totalAttempts, stopwatch.Elapsed.TotalSeconds, rate);
            TxtStatus.Text = "Тестирование завершено";
            TxtTimeRemaining.Text = "0.0 сек";

            var totalBlocked = _refusedCount + _timeoutCount;
            AppendLog($"=== Завершено за {stopwatch.Elapsed.TotalSeconds:F2} сек | Отправлено: {_sentCount} | Подключено: {_successCount} | Отказов/Сбросов: {totalBlocked} ===");

            if (_banDetected)
            {
                AppendLog("🛡️ ВЕРДИКТ: Port Defender успешно распознал аномальную активность и активировал блокировку брандмауэра!");
            }
        }
    }

    /// <summary>
    /// Выполняет попытку TCP подключения к сокету с таймаутом.
    /// </summary>
    private async Task ProbeTcpAsync(IPAddress ip, int port, int index, CancellationToken ct)
    {
        using var client = new TcpClient();
        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromMilliseconds(350));
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);

        try
        {
            await client.ConnectAsync(ip, port, linkedCts.Token);
            Interlocked.Increment(ref _successCount);
            _consecutiveDropCount = 0;

            if (index <= 10 || index % 20 == 0)
                AppendLog($"[#{index:D4}] TCP {ip}:{port} -> ОТКРЫТ (Connected)");
        }
        catch (SocketException sex)
        {
            Interlocked.Increment(ref _refusedCount);

            if (index <= 10 || index % 20 == 0)
                AppendLog($"[#{index:D4}] TCP {ip}:{port} -> Отказ: {sex.SocketErrorCode}");

            // Если сетевой стек сообщает о блокировке маршрута или хоста
            if (sex.SocketErrorCode == SocketError.HostUnreachable || sex.SocketErrorCode == SocketError.NetworkUnreachable)
            {
                OnDropOrTimeoutDetected(index);
            }
        }
        catch (OperationCanceledException)
        {
            if (ct.IsCancellationRequested) return;

            Interlocked.Increment(ref _timeoutCount);
            OnDropOrTimeoutDetected(index);

            if (index <= 10 || index % 20 == 0)
                AppendLog($"[#{index:D4}] TCP {ip}:{port} -> Таймаут (Drop/Фильтрация брандмауэра)");
        }
        catch (Exception ex)
        {
            Interlocked.Increment(ref _refusedCount);
            if (index <= 10 || index % 20 == 0)
                AppendLog($"[#{index:D4}] TCP {ip}:{port} -> Ошибка: {ex.Message}");
        }
    }

    /// <summary>
    /// Выполняет отправку UDP тестовой дейтаграммы.
    /// </summary>
    private async Task ProbeUdpAsync(IPAddress ip, int port, int index, CancellationToken ct)
    {
        using var udp = new UdpClient();
        try
        {
            var payload = Encoding.UTF8.GetBytes($"SMON_PROBE_{index}_{DateTime.UtcNow.Ticks}");
            await udp.SendAsync(payload, payload.Length, new IPEndPoint(ip, port));
            Interlocked.Increment(ref _successCount);

            if (index <= 10 || index % 20 == 0)
                AppendLog($"[#{index:D4}] UDP {ip}:{port} -> Отправлен датаграмм ({payload.Length} байт)");
        }
        catch (Exception ex)
        {
            Interlocked.Increment(ref _refusedCount);
            if (index <= 10 || index % 20 == 0)
                AppendLog($"[#{index:D4}] UDP {ip}:{port} -> Сбой отправки UDP: {ex.Message}");
        }
    }

    /// <summary>
    /// Регистрирует событие отбрасывания пакетов брандмауэром и активирует индикатор бана при фиксации серии сбросов.
    /// </summary>
    private void OnDropOrTimeoutDetected(int index)
    {
        var consecutive = Interlocked.Increment(ref _consecutiveDropCount);
        if (consecutive >= 3 && !_banDetected)
        {
            _banDetected = true;
            Dispatcher.Invoke(() =>
            {
                BadgeBanDetected.Visibility = Visibility.Visible;
                AppendLog($"🚨 [#{index:D4}] ОБНАРУЖЕН БАН: Зафиксирована серия отбрасывания пакетов (Drop/Timeout)! Port Defender заблокировал соединения.");
            });
        }
    }

    /// <summary>
    /// Обновляет элементы прогресса и счетчиков в UI-потоке.
    /// </summary>
    private void UpdateUiProgress(int total, double elapsedSeconds, int rate)
    {
        Dispatcher.Invoke(() =>
        {
            PrgProgress.Value = _sentCount;
            TxtProgressCount.Text = $"{_sentCount} / {total}";
            TxtSuccessCount.Text = _successCount.ToString();
            TxtRefusedCount.Text = _refusedCount.ToString();
            TxtTimeoutCount.Text = _timeoutCount.ToString();

            var remainingCount = Math.Max(0, total - _sentCount);
            var remainingSec = (double)remainingCount / rate;
            TxtTimeRemaining.Text = $"{remainingSec:F1} сек";
        });
    }

    /// <summary>
    /// Останавливает запущенное тестирование.
    /// </summary>
    private void OnStopClick(object sender, RoutedEventArgs e)
    {
        _cts?.Cancel();
        BtnStop.IsEnabled = false;
        TxtStatus.Text = "Остановка...";
    }

    /// <summary>
    /// Включает или блокирует элементы ввода во время работы теста.
    /// </summary>
    private void SetInputsEnabled(bool enabled)
    {
        CmbProtocol.IsEnabled = enabled;
        TxtIp.IsEnabled = enabled;
        TxtPort.IsEnabled = enabled;
        TxtRate.IsEnabled = enabled;
        TxtTotal.IsEnabled = enabled;
        RbModeStress.IsEnabled = enabled;
        RbModeScan.IsEnabled = enabled;
        RbModeAlert.IsEnabled = enabled;
    }

    /// <summary>
    /// Добавляет запись в журнал лога с отметкой времени.
    /// </summary>
    private void AppendLog(string message)
    {
        Dispatcher.Invoke(() =>
        {
            var time = DateTime.Now.ToString("HH:mm:ss.fff");
            TxtLog.AppendText($"[{time}] {message}\n");
            TxtLog.ScrollToEnd();
        });
    }
}
