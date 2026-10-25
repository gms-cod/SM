using System.Windows;
using StartupMonitor.Shared.Models.Snapshot;
using StartupMonitor.UI.Services;
using StartupMonitor.UI.ViewModels;

namespace StartupMonitor.UI.Views.Dialogs;

/// <summary>
/// Окно создания, управления и дифференциального анализа контрольных снимков автозапуска.
/// </summary>
public partial class SnapshotsWindow
{
    private readonly PipeClient _pipeClient;
    private readonly MainViewModel _vm;
    private SnapshotComparisonResult? _currentDiff;

    /// <summary>SnapshotsWindow: Выполняет операцию SnapshotsWindow.</summary>
    /// <param name="pipeClient">Входные данные: значение параметра pipeClient (PipeClient).</param>
    /// <param name="viewModel">Входные данные: значение параметра viewModel (MainViewModel).</param>
    public SnapshotsWindow(PipeClient pipeClient, MainViewModel viewModel)
    {
        InitializeComponent();
        _pipeClient = pipeClient;
        _vm = viewModel;

        Loaded += async (_, _) => await LoadSnapshotsAsync();
    }

    /// <summary>LoadSnapshotsAsync: Загружает данные SnapshotsAsync.</summary>
    private async Task LoadSnapshotsAsync()
    {
        try
        {
            var snapshots = await _pipeClient.GetSnapshotsAsync();
            GridSnapshots.ItemsSource = snapshots;
            TxtNoSnapshots.Visibility = (snapshots.Count == 0) ? Visibility.Visible : Visibility.Collapsed;
            // Обновляем счётчик сохранённых снимков в бейдже левой навигации главного окна
            _vm.SavedSnapshotsCount = snapshots.Count;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Ошибка загрузки снимков: {ex.Message}", "Startup Monitor", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    /// <summary>OnCreateSnapshotClick: Обрабатывает событие OnCreateSnapshotClick.</summary>
    /// <param name="sender">Входные данные: значение параметра sender (object).</param>
    /// <param name="e">Входные данные: значение параметра e (RoutedEventArgs).</param>
    private async void OnCreateSnapshotClick(object sender, RoutedEventArgs e)
    {
        var name = TxtSnapshotName.Text.Trim();
        var desc = TxtSnapshotDesc.Text.Trim();

        BtnCreateSnapshot.IsEnabled = false;
        try
        {
            var (success, error, snapshot) = await _pipeClient.CreateSnapshotAsync(
                string.IsNullOrEmpty(name) ? null : name,
                string.IsNullOrEmpty(desc) ? null : desc);

            if (success && snapshot != null)
            {
                TxtSnapshotName.Text = string.Empty;
                TxtSnapshotDesc.Text = string.Empty;
                await LoadSnapshotsAsync();

                // Автоматически выполняем сравнение с только что созданным снимком
                await CompareSnapshotInternalAsync(snapshot.Id);
            }
            else
            {
                MessageBox.Show(error ?? "Не удалось создать снимок.", "Startup Monitor", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Ошибка: {ex.Message}", "Startup Monitor", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            BtnCreateSnapshot.IsEnabled = true;
        }
    }

    /// <summary>OnCompareSnapshotClick: Обрабатывает событие OnCompareSnapshotClick.</summary>
    /// <param name="sender">Входные данные: значение параметра sender (object).</param>
    /// <param name="e">Входные данные: значение параметра e (RoutedEventArgs).</param>
    private async void OnCompareSnapshotClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: BaselineSnapshot snapshot })
        {
            await CompareSnapshotInternalAsync(snapshot.Id);
        }
    }

    /// <summary>CompareSnapshotInternalAsync: Выполняет операцию CompareSnapshotInternalAsync.</summary>
    /// <param name="snapshotId">Входные данные: значение параметра snapshotId (string).</param>
    private async Task CompareSnapshotInternalAsync(string snapshotId)
    {
        try
        {
            var (success, error, diff) = await _pipeClient.CompareSnapshotAsync(snapshotId);
            if (success && diff != null)
            {
                _currentDiff = diff;
                DisplayDiffResults(diff);
            }
            else
            {
                MessageBox.Show(error ?? "Ошибка при сравнении со снимком.", "Startup Monitor", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Ошибка: {ex.Message}", "Startup Monitor", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    /// <summary>DisplayDiffResults: Выполняет операцию DisplayDiffResults.</summary>
    /// <param name="diff">Входные данные: значение параметра diff (SnapshotComparisonResult).</param>
    private void DisplayDiffResults(SnapshotComparisonResult diff)
    {
        TxtDiffHeader.Text = $"Сравнение со снимком «{diff.SnapshotName}»:";
        BadgeAdded.Text = $"+{diff.AddedEntries.Count} Новых";
        BadgeModified.Text = $"~{diff.ModifiedEntries.Count} Измененных";
        BadgeRemoved.Text = $"-{diff.RemovedEntries.Count} Удаленных";
        BadgeUnchanged.Text = $"{diff.UnchangedCount} Без изменений";

        TabAdded.Header = $"Новые записи (+{diff.AddedEntries.Count})";
        TabModified.Header = $"Измененные (~{diff.ModifiedEntries.Count})";
        TabRemoved.Header = $"Удаленные (-{diff.RemovedEntries.Count})";

        GridAdded.ItemsSource = diff.AddedEntries;
        GridModified.ItemsSource = diff.ModifiedEntries;
        GridRemoved.ItemsSource = diff.RemovedEntries;

        BtnApplyFilter.IsEnabled = diff.HasDifferences;
    }

    /// <summary>OnDeleteSnapshotClick: Обрабатывает событие OnDeleteSnapshotClick.</summary>
    /// <param name="sender">Входные данные: значение параметра sender (object).</param>
    /// <param name="e">Входные данные: значение параметра e (RoutedEventArgs).</param>
    private async void OnDeleteSnapshotClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: BaselineSnapshot snapshot })
        {
            var res = MessageBox.Show(
                LocalizationService.Instance.SnapshotDeleteConfirm,
                "Startup Monitor",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (res == MessageBoxResult.Yes)
            {
                await _pipeClient.DeleteSnapshotAsync(snapshot.Id);
                await LoadSnapshotsAsync();
            }
        }
    }

    /// <summary>OnApplyFilterToMainClick: Обрабатывает событие OnApplyFilterToMainClick.</summary>
    /// <param name="sender">Входные данные: значение параметра sender (object).</param>
    /// <param name="e">Входные данные: значение параметра e (RoutedEventArgs).</param>
    private void OnApplyFilterToMainClick(object sender, RoutedEventArgs e)
    {
        if (_currentDiff != null)
        {
            _vm.ApplySnapshotComparison(_currentDiff);
            Close();
        }
    }

    /// <summary>OnCloseClick: Обрабатывает событие OnCloseClick.</summary>
    /// <param name="sender">Входные данные: значение параметра sender (object).</param>
    /// <param name="e">Входные данные: значение параметра e (RoutedEventArgs).</param>
    private void OnCloseClick(object sender, RoutedEventArgs e)
    {
        Close();
    }
}

