using StartupMonitor.Shared.Models;
using StartupMonitor.UI.ViewModels;

namespace StartupMonitor.UI.Views.Dialogs;

/// <summary>Окно детального просмотра и управления записью автозапуска.</summary>
public partial class EntryDetailWindow
{
    /// <summary>Модель представления главного окна.</summary>
    private readonly MainViewModel _vm;
    /// <summary>Идентификатор открытой записи.</summary>
    private readonly string _entryId;
    /// <summary>Объект записи автозапуска.</summary>
    private readonly StartupEntry _entry;

    /// <summary>Инициализирует окно деталей с записью и ссылкой на ViewModel.</summary>
    public EntryDetailWindow(StartupEntry entry, MainViewModel viewModel)
    {
        InitializeComponent();
        DataContext = entry;
        _entry = entry;
        _vm = viewModel;
        _entryId = entry.Id;
        Loaded += (_, _) => UpdateRatingUiState();
    }

    /// <summary>Обновляет визуальные индикаторы принадлежности рейтинга (ручной или AI).</summary>
    private void UpdateRatingUiState()
    {
        try
        {
            if (_entry.IsUserRating)
            {
                TxtRatingOrigin.Text = "Установлено вручную";
                TxtRatingOrigin.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x10, 0xB9, 0x81));
                BtnResetUserRating.Visibility = System.Windows.Visibility.Visible;
            }
            else
            {
                TxtRatingOrigin.Text = "Определено AI";
                TxtRatingOrigin.Foreground = (System.Windows.Media.Brush)FindResource("TextFillColorSecondaryBrush");
                BtnResetUserRating.Visibility = System.Windows.Visibility.Collapsed;
            }

            DataContext = null;
            DataContext = _entry;
        }
        catch { }
    }

    /// <summary>Обрабатывает ручной выбор количества звёзд (1–5) пользователем.</summary>
    private async void OnStarClick(object sender, System.Windows.RoutedEventArgs e)
    {
        if (sender is System.Windows.FrameworkElement elem && int.TryParse(elem.Tag?.ToString(), out var stars))
        {
            try
            {
                var updated = await _vm.SetUserRatingAsync(_entryId, stars, resetToAi: false);
                if (updated != null)
                {
                    _entry.IsUserRating = true;
                    _entry.AiRating = updated.AiRating;
                    _entry.AiCategory = updated.AiCategory;
                    _entry.AiRisk = updated.AiRisk;
                    _entry.AiRecommendation = updated.AiRecommendation;
                }
                UpdateRatingUiState();
            }
            catch (System.Exception ex)
            {
                System.Windows.MessageBox.Show($"Ошибка установки рейтинга: {ex.Message}", "Startup Monitor",
                    System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
            }
        }
    }

    /// <summary>Сбрасывает ручной рейтинг и возвращает автоматическую AI-оценку.</summary>
    private async void OnResetUserRatingClick(object sender, System.Windows.RoutedEventArgs e)
    {
        try
        {
            var updated = await _vm.SetUserRatingAsync(_entryId, 0, resetToAi: true);
            if (updated != null)
            {
                _entry.IsUserRating = false;
                _entry.AiRating = updated.AiRating;
                _entry.AiCategory = updated.AiCategory;
                _entry.AiRisk = updated.AiRisk;
                _entry.AiRecommendation = updated.AiRecommendation;
            }
            UpdateRatingUiState();
        }
        catch (System.Exception ex)
        {
            System.Windows.MessageBox.Show($"Ошибка сброса рейтинга: {ex.Message}", "Startup Monitor",
                System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
        }
    }

    /// <summary>Обрабатывает нажатие кнопки «Заблокировать».</summary>
    private async void OnBlockClick(object sender, System.Windows.RoutedEventArgs e)
    {
        await _vm.BlockEntryByIdCommand.ExecuteAsync(_entryId);
    }

    /// <summary>Обрабатывает нажатие кнопки «Разблокировать».</summary>
    private async void OnUnblockClick(object sender, System.Windows.RoutedEventArgs e)
    {
        await _vm.UnblockEntryCommand.ExecuteAsync(_entry);
    }

    /// <summary>Обрабатывает нажатие кнопки «Отключить».</summary>
    private async void OnDisableClick(object sender, System.Windows.RoutedEventArgs e)
    {
        await _vm.DisableEntryCommand.ExecuteAsync(_entryId);
    }

    /// <summary>Обрабатывает нажатие кнопки «Включить».</summary>
    private async void OnEnableClick(object sender, System.Windows.RoutedEventArgs e)
    {
        await _vm.EnableEntryCommand.ExecuteAsync(_entryId);
    }

    /// <summary>Обрабатывает нажатие кнопки «Отложить запуск».</summary>
    private async void OnSetDelayClick(object sender, System.Windows.RoutedEventArgs e)
    {
        await _vm.SetDelayedEntryCommand.ExecuteAsync($"{_entryId}|30");
    }

    /// <summary>Обрабатывает нажатие кнопки «Отменить задержку».</summary>
    private async void OnCancelDelayClick(object sender, System.Windows.RoutedEventArgs e)
    {
        await _vm.CancelDelayedEntryCommand.ExecuteAsync(_entryId);
    }

    /// <summary>Обрабатывает нажатие кнопки «Запустить сейчас».</summary>
    private async void OnLaunchNowClick(object sender, System.Windows.RoutedEventArgs e)
    {
        await _vm.LaunchDelayedEntryCommand.ExecuteAsync(_entryId);
    }

    /// <summary>Обрабатывает нажатие кнопки «Удалить из системы» с подтверждением.</summary>
    private async void OnRemoveClick(object sender, System.Windows.RoutedEventArgs e)
    {
        var result = System.Windows.MessageBox.Show(
            $"Удалить «{_entry.Name}» из автозапуска системы?",
            "Подтверждение удаления",
            System.Windows.MessageBoxButton.YesNo,
            System.Windows.MessageBoxImage.Warning);
        if (result != System.Windows.MessageBoxResult.Yes) return;

        await _vm.RemoveFromSystemCommand.ExecuteAsync(_entryId);
        Close();
    }

    /// <summary>Обрабатывает нажатие кнопки переоценки AI-рейтинга с мгновенным сбросом в окне и моментальным запросом к AI.</summary>
    private async void OnRefreshRatingClick(object sender, System.Windows.RoutedEventArgs e)
    {
        BtnRefreshAi.IsEnabled = false;
        var prevContent = BtnRefreshAi.Content;
        BtnRefreshAi.Content = "Оценка AI...";

        // Мгновенный сброс текущего рейтинга прямо в окне для моментального визуального отклика
        _entry.AiRating = 0;
        _entry.AiCategory = "Подключение к AI...";
        _entry.AiRisk = "Отправлен прямой запрос нейросети. Ожидание вердикта...";
        _entry.AiRecommendation = "Идёт расчет рейтинга безопасности...";

        // Принудительное обновление интерфейса окна
        DataContext = null;
        DataContext = _entry;

        try
        {
            var updated = await _vm.ReRateEntryAsync(_entryId);
            if (updated != null)
            {
                _entry.AiRating = updated.AiRating;
                _entry.AiCategory = updated.AiCategory;
                _entry.AiRisk = updated.AiRisk;
                _entry.AiRecommendation = updated.AiRecommendation;
            }
        }
        catch (System.Exception ex)
        {
            _entry.AiRisk = $"Ошибка запроса к AI: {ex.Message}";
        }
        finally
        {
            DataContext = null;
            DataContext = _entry;
            BtnRefreshAi.IsEnabled = true;
            BtnRefreshAi.Content = prevContent;
        }
    }

    /// <summary>Обрабатывает нажатие кнопки проверки в Kaspersky OpenTIP.</summary>
    private async void OnScanKasperskyClick(object sender, System.Windows.RoutedEventArgs e)
    {
        await _vm.ScanKasperskyEntryCommand.ExecuteAsync(_entry);
    }

    /// <summary>Обрабатывает нажатие кнопки «Открыть расположение файла».</summary>
    private void OnOpenLocationClick(object sender, System.Windows.RoutedEventArgs e)
    {
        _vm.OpenFileLocationCommand.Execute(_entryId);
    }

    /// <summary>Обрабатывает нажатие кнопки «Копировать путь».</summary>
    private void OnCopyPathClick(object sender, System.Windows.RoutedEventArgs e)
    {
        _vm.CopyFilePathCommand.Execute(_entryId);
    }

    /// <summary>Закрывает окно деталей.</summary>
    private void OnCloseClick(object sender, System.Windows.RoutedEventArgs e)
    {
        Close();
    }
}
