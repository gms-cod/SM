using StartupMonitor.Shared.Models;
using StartupMonitor.UI.ViewModels;
using System.ComponentModel;

namespace StartupMonitor.UI.Views.Dialogs;

/// <summary>Окно детального просмотра и управления записью автозапуска.</summary>
public partial class EntryDetailWindow
{
    /// <summary>Модель представления главного окна.</summary>
    private readonly MainViewModel _vm;
    /// <summary>Идентификатор открытой записи.</summary>
    private readonly string _entryId;
    /// <summary>Объект записи автозапуска.</summary>
    private StartupEntry _entry;
    /// <summary>Получение новой оценки; рабочее окно использует MainViewModel, тесты — управляемый ответ.</summary>
    private readonly Func<string, Task<StartupEntry?>> _ratingRefresher;
    /// <summary>Не позволяет запустить повторную переоценку до завершения первой.</summary>
    private bool _isRefreshingRating;
    /// <summary>Запоздалые события после закрытия больше не меняют visual tree.</summary>
    private bool _isClosed;

    /// <summary>Инициализирует окно деталей с записью и ссылкой на ViewModel.</summary>
    public EntryDetailWindow(StartupEntry entry, MainViewModel viewModel)
        : this(entry, viewModel, null)
    {
    }

    /// <summary>Создаёт настоящее окно с изолируемым источником переоценки без подмены производственной логики привязок.</summary>
    /// <param name="entry">Выбранный объект, который мог устареть после нового снимка.</param>
    /// <param name="viewModel">Владелец актуальных объектов списка.</param>
    /// <param name="ratingRefresher">Необязательный источник результата для регрессионных проверок.</param>
    internal EntryDetailWindow(StartupEntry entry, MainViewModel viewModel, Func<string, Task<StartupEntry?>>? ratingRefresher)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(viewModel);
        InitializeComponent();
        _entry = entry;
        _vm = viewModel;
        _entryId = entry.Id;
        _ratingRefresher = ratingRefresher ?? viewModel.ReRateEntryAsync;
        _entry.PropertyChanged += OnEntryPropertyChanged;
        _vm.PropertyChanged += OnViewModelPropertyChanged;
        Loaded += OnWindowLoaded;
        Closed += OnWindowClosed;
        SynchronizeLiveEntry();
    }

    /// <summary>Перед первым отображением выбирает последний экземпляр объекта вместо сохранённой устаревшей строки.</summary>
    private void OnWindowLoaded(object sender, System.Windows.RoutedEventArgs e) => SynchronizeLiveEntry();

    /// <summary>Разрывает внешние подписки: закрытое окно не удерживается ViewModel и прежней записью.</summary>
    private void OnWindowClosed(object? sender, EventArgs e)
    {
        _isClosed = true;
        _vm.PropertyChanged -= OnViewModelPropertyChanged;
        _entry.PropertyChanged -= OnEntryPropertyChanged;
        Loaded -= OnWindowLoaded;
    }

    /// <summary>Новый снимок или фильтр списка может заменить экземпляр записи, сохранив тот же Id.</summary>
    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MainViewModel.FilteredEntries) or nameof(MainViewModel.Entries) or null or "")
            SynchronizeLiveEntry();
    }

    /// <summary>Изменение происхождения оценки обновляет badge и кнопку сброса без повторного назначения DataContext.</summary>
    private void OnEntryPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(StartupEntry.IsUserRating) or null or "")
        {
            if (Dispatcher.CheckAccess()) UpdateRatingUiState();
            else _ = Dispatcher.InvokeAsync(UpdateRatingUiState);
        }
    }

    /// <summary>Перепривязывает окно к live item даже при исключении записи текущим фильтром основного списка.</summary>
    private void SynchronizeLiveEntry()
    {
        if (_isClosed) return;
        if (!Dispatcher.CheckAccess())
        {
            _ = Dispatcher.InvokeAsync(SynchronizeLiveEntry);
            return;
        }
        var current = _vm.GetLiveEntryById(_entryId);
        if (current != null && !ReferenceEquals(current, _entry))
        {
            _entry.PropertyChanged -= OnEntryPropertyChanged;
            _entry = current;
            _entry.PropertyChanged += OnEntryPropertyChanged;
        }
        if (!ReferenceEquals(DataContext, _entry)) DataContext = _entry;
        UpdateRatingUiState();
    }

    /// <summary>Обновляет визуальные индикаторы принадлежности рейтинга (ручной или AI).</summary>
    private void UpdateRatingUiState()
    {
        if (_isClosed) return;
        if (_entry.IsUserRating)
        {
            TxtRatingOrigin.Text = "Установлено вручную";
            TxtRatingOrigin.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x10, 0xB9, 0x81));
            BtnResetUserRating.Visibility = System.Windows.Visibility.Visible;
        }
        else
        {
            TxtRatingOrigin.Text = "Определено AI";
            TxtRatingOrigin.Foreground = TryFindResource("TextFillColorSecondaryBrush") as System.Windows.Media.Brush ?? System.Windows.Media.Brushes.Gray;
            BtnResetUserRating.Visibility = System.Windows.Visibility.Collapsed;
        }
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

    /// <summary>Обрабатывает нажатие переоценки, сохраняя подтверждённый рейтинг до получения нового результата.</summary>
    private async void OnRefreshRatingClick(object sender, System.Windows.RoutedEventArgs e)
        => await RefreshRatingAsync();

    /// <summary>Обновляет рейтинг и его происхождение; ожидание отражается состоянием кнопки, не фиктивной заменой данных.</summary>
    /// <returns>Завершение подтверждённого обновления либо отображения причины отсутствия результата.</returns>
    internal async Task RefreshRatingAsync()
    {
        if (_isRefreshingRating || _isClosed) return;
        _isRefreshingRating = true;
        BtnRefreshAi.IsEnabled = false;
        var prevContent = BtnRefreshAi.Content;
        BtnRefreshAi.Content = "Оценка AI...";
        TxtRatingRefreshStatus.Visibility = System.Windows.Visibility.Collapsed;

        try
        {
            var updated = await _ratingRefresher(_entryId);
            if (_isClosed) return;
            SynchronizeLiveEntry();
            if (updated != null)
            {
                _entry.AiRating = updated.AiRating;
                _entry.AiCategory = updated.AiCategory;
                _entry.AiRisk = updated.AiRisk;
                _entry.AiRecommendation = updated.AiRecommendation;
                _entry.IsUserRating = updated.IsUserRating;
                UpdateRatingUiState();
            }
            else ShowRatingRefreshFailure();
        }
        catch (System.Exception ex)
        {
            App.Log($"EntryDetailWindow.RefreshRating: {ex.Message}");
            if (!_isClosed) ShowRatingRefreshFailure();
        }
        finally
        {
            _isRefreshingRating = false;
            if (!_isClosed)
            {
                BtnRefreshAi.IsEnabled = true;
                BtnRefreshAi.Content = prevContent;
            }
        }
    }

    /// <summary>Показывает ошибку отдельно от сохранённого результата рейтинга и позволяет повторить запрос.</summary>
    private void ShowRatingRefreshFailure()
    {
        TxtRatingRefreshStatus.Text = "Не удалось получить новую оценку. Сохранён предыдущий рейтинг; попробуйте ещё раз.";
        TxtRatingRefreshStatus.Visibility = System.Windows.Visibility.Visible;
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
