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

    /// <summary>Обрабатывает нажатие кнопки переоценки AI-рейтинга.</summary>
    private async void OnRefreshRatingClick(object sender, System.Windows.RoutedEventArgs e)
    {
        await _vm.ReRateEntryAsync(_entryId);
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
