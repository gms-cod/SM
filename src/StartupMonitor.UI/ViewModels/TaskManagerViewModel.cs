using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StartupMonitor.Shared.Models;
using StartupMonitor.Shared.Diagnostics;
using StartupMonitor.UI.Services;

namespace StartupMonitor.UI.ViewModels;

/// <summary>
/// Модель представления Диспетчера задач: управление процессами, мониторинг системных ресурсов,
/// On-Demand подписка на сбор метрик, фильтрация, сортировка и контекстные действия.
/// </summary>
public partial class TaskManagerViewModel : ObservableObject, IDisposable
{
    private readonly PipeClient _pipeClient;
    private readonly DispatcherTimer _searchDebounceTimer;
    private readonly Dictionary<int, ProcessInfo> _processMap = new();

    private bool _isActive;
    private string _sortColumn = "";
    private bool _sortAscending = false; // По умолчанию сортировка по убыванию нагрузки на ЦП

    /// <summary>Событие запроса перехода на вкладку Автозагрузка с фокусировкой на указанной записи.</summary>
    public event Action<string>? LocateStartupEntryRequested;

    /// <summary>Событие показа всплывающего уведомления.</summary>
    public event Action<string, string>? StatusMessageRequested;

    [ObservableProperty]
    private SystemPerformanceSnapshot? _performanceSnapshot;

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private string _selectedFilter = "all"; // all, apps, background, services, autostart

    [ObservableProperty]
    private string _viewMode = "flat"; // "flat" или "tree"

    public bool IsTreeView => ViewMode == "tree";

    private readonly HashSet<int> _collapsedPids = new();

    [ObservableProperty]
    private ProcessInfo? _selectedProcess;

    [ObservableProperty]
    private bool _isLoading = true;

    public ObservableCollection<ProcessInfo> FilteredProcesses { get; } = new();

    // ──── Системные карточки нагрузки ────

    public string TotalCpuDisplay => PerformanceSnapshot != null ? $"{PerformanceSnapshot.TotalCpuPercent:F0}%" : "0%";
    public double TotalCpuValue => PerformanceSnapshot?.TotalCpuPercent ?? 0;

    public string TotalMemoryDisplay
    {
        get
        {
            if (PerformanceSnapshot == null || PerformanceSnapshot.TotalMemoryBytes == 0) return "0 / 0 ГБ";
            var usedGb = PerformanceSnapshot.UsedMemoryBytes / (1024.0 * 1024 * 1024);
            var totalGb = PerformanceSnapshot.TotalMemoryBytes / (1024.0 * 1024 * 1024);
            return $"{usedGb:F1} / {totalGb:F1} ГБ ({PerformanceSnapshot.TotalMemoryPercent:F0}%)";
        }
    }
    public double TotalMemoryValue => PerformanceSnapshot?.TotalMemoryPercent ?? 0;

    public string TotalDiskDisplay
    {
        get
        {
            if (PerformanceSnapshot == null) return "0 КБ/с";
            var bytes = PerformanceSnapshot.TotalDiskIoBytesPerSec;
            if (bytes >= 1024 * 1024)
                return $"{bytes / (1024.0 * 1024):F1} МБ/с";
            return $"{bytes / 1024.0:F0} КБ/с";
        }
    }

    public string ProcessesCountDisplay => PerformanceSnapshot != null ? $"{PerformanceSnapshot.TotalProcessesCount}" : "0";

    // ──── Индикаторы сортировки ────

    public string SortNameIndicator => _sortColumn == "name" ? (_sortAscending ? "\u25B2" : "\u25BC") : "";
    public string SortPidIndicator => _sortColumn == "pid" ? (_sortAscending ? "\u25B2" : "\u25BC") : "";
    public string SortCpuIndicator => _sortColumn == "cpu" ? (_sortAscending ? "\u25B2" : "\u25BC") : "";
    public string SortRamIndicator => _sortColumn == "ram" ? (_sortAscending ? "\u25B2" : "\u25BC") : "";
    public string SortDiskIndicator => _sortColumn == "disk" ? (_sortAscending ? "\u25B2" : "\u25BC") : "";
    public string SortNetIndicator => _sortColumn == "net" ? (_sortAscending ? "\u25B2" : "\u25BC") : "";
    public string SortPublisherIndicator => _sortColumn == "publisher" ? (_sortAscending ? "\u25B2" : "\u25BC") : "";
    public string SortStartupIndicator => _sortColumn == "startup" ? (_sortAscending ? "\u25B2" : "\u25BC") : "";

    public TaskManagerViewModel(PipeClient pipeClient)
    {
        _pipeClient = pipeClient;
        _pipeClient.Connected += OnPipeClientConnected;
        _viewMode = MainViewModel.LoadSetting("taskManagerViewMode", "flat");

        _searchDebounceTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(250)
        };
        _searchDebounceTimer.Tick += (_, _) =>
        {
            _searchDebounceTimer.Stop();
            ApplyFilterAndSort();
        };
    }

    private async void OnPipeClientConnected(object? sender, EventArgs e)
    {
        if (_isActive)
        {
            await ActivateAsync();
        }
    }

    partial void OnViewModeChanged(string value)
    {
        OnPropertyChanged(nameof(IsTreeView));
        MainViewModel.SaveSetting("taskManagerViewMode", value);
        ApplyFilterAndSort();
    }

    partial void OnSearchTextChanged(string value)
    {
        _searchDebounceTimer.Stop();
        _searchDebounceTimer.Start();
    }

    partial void OnSelectedFilterChanged(string value)
    {
        ApplyFilterAndSort();
    }

    /// <summary>Активирует вкладку Диспетчера задач: подписывается на поток метрик службы.</summary>
    public async Task ActivateAsync()
    {
        _isActive = true;
        App.Log($"TaskManager: ActivateAsync called (IsConnected={_pipeClient.IsConnected})");

        _pipeClient.ProcessSnapshotReceived -= OnProcessSnapshotReceived;
        _pipeClient.ProcessSnapshotReceived += OnProcessSnapshotReceived;

        if (_pipeClient.IsConnected)
        {
            await _pipeClient.SubscribeProcessMetricsAsync();

            // Запрашиваем первый снимок немедленно, не дожидаясь тика таймера
            var firstSnapshot = await _pipeClient.GetProcessesAsync();
            if (firstSnapshot != null)
            {
                App.Log($"TaskManager: firstSnapshot received ({firstSnapshot.Processes.Count} processes, CPU={firstSnapshot.TotalCpuPercent:F1}%)");
                UpdateSnapshot(firstSnapshot);
            }
        }
    }

    /// <summary>Деактивирует вкладку Диспетчера задач: отписывается от службы (снижает CPU службы до 0%).</summary>
    public async Task DeactivateAsync()
    {
        if (!_isActive) return;
        _isActive = false;

        _pipeClient.ProcessSnapshotReceived -= OnProcessSnapshotReceived;
        if (_pipeClient.IsConnected)
        {
            await _pipeClient.UnsubscribeProcessMetricsAsync();
        }
    }

    private void OnProcessSnapshotReceived(object? sender, SystemPerformanceSnapshot snapshot)
    {
        UpdateSnapshot(snapshot);
    }

    /// <summary>Обновляет снимок и обновляет коллекцию процессов без мерцания.</summary>
    public void UpdateSnapshot(SystemPerformanceSnapshot snapshot)
    {
        using var _prof = ExecutionProfiler.Measure("UI.TaskManager.UpdateSnapshot");
        if (Application.Current?.Dispatcher != null && !Application.Current.Dispatcher.CheckAccess())
        {
            Application.Current.Dispatcher.InvokeAsync(() => UpdateSnapshot(snapshot));
            return;
        }

        PerformanceSnapshot = snapshot;
        IsLoading = false;

        OnPropertyChanged(nameof(TotalCpuDisplay));
        OnPropertyChanged(nameof(TotalCpuValue));
        OnPropertyChanged(nameof(TotalMemoryDisplay));
        OnPropertyChanged(nameof(TotalMemoryValue));
        OnPropertyChanged(nameof(TotalDiskDisplay));
        OnPropertyChanged(nameof(ProcessesCountDisplay));

        HashSet<int>? windowPids = null;
        try
        {
            windowPids = WindowHelper.GetProcessIdsWithWindows();
        }
        catch { }

        var incomingPids = new HashSet<int>();

        foreach (var incoming in snapshot.Processes)
        {
            incomingPids.Add(incoming.Pid);

            if (windowPids != null && windowPids.Contains(incoming.Pid))
            {
                incoming.HasWindow = true;
            }

            if (_processMap.TryGetValue(incoming.Pid, out var existing))
            {
                // Обновляем метрики существующего процесса на лету
                existing.CpuPercent = incoming.CpuPercent;
                existing.WorkingSetBytes = incoming.WorkingSetBytes;
                existing.DiskIoBytesPerSec = incoming.DiskIoBytesPerSec;
                existing.NetworkBytesPerSec = incoming.NetworkBytesPerSec;
                existing.ActiveSocketsCount = incoming.ActiveSocketsCount;
                existing.HasNetworkActivity = incoming.HasNetworkActivity;

                if (!string.IsNullOrEmpty(incoming.Publisher) && string.IsNullOrEmpty(existing.Publisher))
                    existing.Publisher = incoming.Publisher;

                if (!string.IsNullOrEmpty(incoming.Description))
                    existing.Description = incoming.Description;

                if (incoming.IsService)
                    existing.IsService = true;

                if (incoming.HasWindow)
                    existing.HasWindow = true;
                else if (windowPids != null && windowPids.Contains(existing.Pid))
                    existing.HasWindow = true;

                if (incoming.ParentProcessId > 0)
                    existing.ParentProcessId = incoming.ParentProcessId;

                if (!string.IsNullOrEmpty(incoming.ParentProcessName))
                    existing.ParentProcessName = incoming.ParentProcessName;

                if (incoming.SessionId > 0)
                    existing.SessionId = incoming.SessionId;

                if (incoming.IsStartupEntry)
                {
                    existing.IsStartupEntry = true;
                    existing.StartupEntryId = incoming.StartupEntryId;
                    existing.StartupLocation = incoming.StartupLocation;
                }

                if (incoming.ThreatInfo != null)
                {
                    existing.ThreatInfo = incoming.ThreatInfo;
                }

                if (!string.IsNullOrEmpty(incoming.CommandLine))
                    existing.CommandLine = incoming.CommandLine;

                if (!string.IsNullOrEmpty(incoming.FilePath))
                    existing.FilePath = incoming.FilePath;

                existing.IsSuspended = incoming.IsSuspended;
            }
            else
            {
                // Новый процесс
                _processMap[incoming.Pid] = incoming;
            }
        }

        // Удаляем завершившиеся процессы
        var deadPids = _processMap.Keys.Where(pid => !incomingPids.Contains(pid)).ToList();
        foreach (var deadPid in deadPids)
        {
            _processMap.Remove(deadPid);
            _collapsedPids.Remove(deadPid);
        }

        ApplyFilterAndSort();
    }

    /// <summary>Проверяет, относится ли процесс к категории пользовательских приложений (Apps).</summary>
    public static bool IsAppProcess(ProcessInfo p)
    {
        if (IsServiceProcess(p)) return false;

        // Если процесс имеет видимое окно на рабочем столе
        if (p.HasWindow) return true;

        // Если процесс запущен из Проводника Windows (вручную пользователем)
        if (string.Equals(p.ParentProcessName, "explorer", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(p.ParentProcessName, "explorer.exe", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // UWP/AppX приложения пользователя
        if (p.StartupLocation == EntryLocation.AppxStartupTask) return true;

        return false;
    }

    /// <summary>Проверяет, относится ли процесс к категории служб Windows (Services).</summary>
    public static bool IsServiceProcess(ProcessInfo p)
    {
        if (p.IsService) return true;
        if (p.StartupLocation == EntryLocation.Service) return true;
        if (string.Equals(p.ProcessName, "svchost", StringComparison.OrdinalIgnoreCase)) return true;
        if (string.Equals(p.ParentProcessName, "services", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(p.ParentProcessName, "services.exe", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }
        return false;
    }

    /// <summary>Вспомогательный метод сортировки коллекции процессов в соответствии с текущей выбранной колонкой.</summary>
    private List<ProcessInfo> SortProcesses(IEnumerable<ProcessInfo> list)
    {
        using var _prof = ExecutionProfiler.Measure("UI.TaskManager.SortProcesses");
        return _sortColumn switch
        {
            "name" => _sortAscending
                ? list.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase).ToList()
                : list.OrderByDescending(p => p.Name, StringComparer.OrdinalIgnoreCase).ToList(),
            "pid" => _sortAscending
                ? list.OrderBy(p => p.Pid).ToList()
                : list.OrderByDescending(p => p.Pid).ToList(),
            "cpu" => _sortAscending
                ? list.OrderBy(p => p.CpuPercent).ThenByDescending(p => p.WorkingSetBytes).ToList()
                : list.OrderByDescending(p => p.CpuPercent).ThenByDescending(p => p.WorkingSetBytes).ToList(),
            "ram" => _sortAscending
                ? list.OrderBy(p => p.WorkingSetBytes).ToList()
                : list.OrderByDescending(p => p.WorkingSetBytes).ToList(),
            "disk" => _sortAscending
                ? list.OrderBy(p => p.DiskIoBytesPerSec).ToList()
                : list.OrderByDescending(p => p.DiskIoBytesPerSec).ToList(),
            "net" => _sortAscending
                ? list.OrderBy(p => p.NetworkBytesPerSec).ToList()
                : list.OrderByDescending(p => p.NetworkBytesPerSec).ToList(),
            "publisher" => _sortAscending
                ? list.OrderBy(p => string.IsNullOrWhiteSpace(p.Publisher)).ThenBy(p => p.Publisher, StringComparer.OrdinalIgnoreCase).ToList()
                : list.OrderBy(p => string.IsNullOrWhiteSpace(p.Publisher)).ThenByDescending(p => p.Publisher, StringComparer.OrdinalIgnoreCase).ToList(),
            "startup" => _sortAscending
                ? list.OrderBy(p => p.IsStartupEntry ? 0 : 1).ThenBy(p => p.DisplayStartupStatus).ToList()
                : list.OrderByDescending(p => p.IsStartupEntry ? 0 : 1).ThenBy(p => p.DisplayStartupStatus).ToList(),
            _ => _sortAscending
                ? list.OrderBy(p => p.CpuPercent).ThenBy(p => p.WorkingSetBytes).ToList()
                : list.OrderByDescending(p => p.CpuPercent).ThenByDescending(p => p.WorkingSetBytes).ToList()
        };
    }

    /// <summary>Применяет текстовый фильтр, категорию, режим отображения (Список / Дерево) и порядок сортировки.</summary>
    public void ApplyFilterAndSort()
    {
        using var _prof = ExecutionProfiler.Measure("UI.TaskManager.ApplyFilterAndSort");
        IEnumerable<ProcessInfo> items = _processMap.Values;

        // Фильтрация по категории
        items = SelectedFilter switch
        {
            "apps" => items.Where(IsAppProcess),
            "services" => items.Where(IsServiceProcess),
            "background" => items.Where(p => !IsAppProcess(p) && !IsServiceProcess(p) && !p.IsStartupEntry),
            "autostart" => items.Where(p => p.IsStartupEntry),
            _ => items
        };

        List<ProcessInfo> sortedList;

        if (!IsTreeView)
        {
            // Плоский режим: сбрасываем свойства дерева
            foreach (var p in _processMap.Values)
            {
                p.TreeLevel = 0;
                p.HasChildren = false;
                p.ChildrenCount = 0;
            }

            // Поиск в плоском режиме
            if (!string.IsNullOrWhiteSpace(SearchText))
            {
                var q = SearchText.Trim();
                items = items.Where(p =>
                    (p.Name != null && p.Name.Contains(q, StringComparison.OrdinalIgnoreCase)) ||
                    (p.Description != null && p.Description.Contains(q, StringComparison.OrdinalIgnoreCase)) ||
                    (p.Publisher != null && p.Publisher.Contains(q, StringComparison.OrdinalIgnoreCase)) ||
                    (p.ExecutablePath != null && p.ExecutablePath.Contains(q, StringComparison.OrdinalIgnoreCase)) ||
                    p.Pid.ToString().Contains(q));
            }

            sortedList = SortProcesses(items);
        }
        else
        {
            // Режим иерархического дерева процессов
            var candidateList = items.ToList();
            var candidateMap = candidateList.ToDictionary(p => p.Pid);

            // Поиск в дереве: если задан запрос, разворачиваем предков для найденных элементов
            HashSet<int>? visiblePids = null;
            if (!string.IsNullOrWhiteSpace(SearchText))
            {
                var q = SearchText.Trim();
                visiblePids = new HashSet<int>();

                foreach (var p in candidateList)
                {
                    bool matches = (p.Name != null && p.Name.Contains(q, StringComparison.OrdinalIgnoreCase)) ||
                                   (p.Description != null && p.Description.Contains(q, StringComparison.OrdinalIgnoreCase)) ||
                                   (p.Publisher != null && p.Publisher.Contains(q, StringComparison.OrdinalIgnoreCase)) ||
                                   (p.ExecutablePath != null && p.ExecutablePath.Contains(q, StringComparison.OrdinalIgnoreCase)) ||
                                   p.Pid.ToString().Contains(q);

                    if (matches)
                    {
                        visiblePids.Add(p.Pid);
                        // Разворачиваем предков
                        int curParent = p.ParentProcessId;
                        var visitedAncestors = new HashSet<int>();
                        while (curParent > 0 && candidateMap.TryGetValue(curParent, out var ancestor) && visitedAncestors.Add(curParent))
                        {
                            visiblePids.Add(ancestor.Pid);
                            _collapsedPids.Remove(ancestor.Pid);
                            ancestor.IsExpanded = true;
                            curParent = ancestor.ParentProcessId;
                        }
                    }
                }
            }

            var activeCandidates = visiblePids != null
                ? candidateList.Where(p => visiblePids.Contains(p.Pid)).ToList()
                : candidateList;
            var activeMap = activeCandidates.ToDictionary(p => p.Pid);

            // Инициализация свойств дерева
            foreach (var p in activeCandidates)
            {
                p.TreeLevel = 0;
                p.HasChildren = false;
                p.ChildrenCount = 0;
                p.IsExpanded = !_collapsedPids.Contains(p.Pid);
            }

            // Построение графа: корни и дети
            var childrenMap = new Dictionary<int, List<ProcessInfo>>();
            var roots = new List<ProcessInfo>();

            foreach (var p in activeCandidates)
            {
                if (p.ParentProcessId > 0 && activeMap.TryGetValue(p.ParentProcessId, out var parent) && parent.Pid != p.Pid)
                {
                    if (!childrenMap.TryGetValue(parent.Pid, out var list))
                    {
                        list = new List<ProcessInfo>();
                        childrenMap[parent.Pid] = list;
                    }
                    list.Add(p);
                }
                else
                {
                    roots.Add(p);
                }
            }

            // Заполняем флаги наличия детей и счетчики
            foreach (var (parentPid, childrenList) in childrenMap)
            {
                if (activeMap.TryGetValue(parentPid, out var parent))
                {
                    parent.HasChildren = childrenList.Count > 0;
                    parent.ChildrenCount = childrenList.Count;
                }
            }

            // Сортировка корней и обход DFS
            var sortedRoots = SortProcesses(roots);
            sortedList = new List<ProcessInfo>(activeCandidates.Count);
            var visited = new HashSet<int>();

            void AddNodeAndChildren(ProcessInfo node, int level)
            {
                if (!visited.Add(node.Pid)) return;

                node.TreeLevel = level;
                sortedList.Add(node);

                if (childrenMap.TryGetValue(node.Pid, out var children) && children.Count > 0)
                {
                    var sortedChildren = SortProcesses(children);
                    if (node.IsExpanded)
                    {
                        foreach (var child in sortedChildren)
                        {
                            AddNodeAndChildren(child, level + 1);
                        }
                    }
                }
            }

            foreach (var root in sortedRoots)
            {
                AddNodeAndChildren(root, 0);
            }
        }

        // In-place синхронизация FilteredProcesses (Diff update):
        // - без Clear() -> сохраняется виртуализация, выделение и позиция скролла;
        // - метрики существующих процессов уже обновлены в UpdateSnapshot через INotifyPropertyChanged,
        //   здесь пересоздания UI-элементов не происходит, только Remove/Insert/Move.
        SyncFilteredProcesses(sortedList);
    }

    /// <summary>
    /// In-place синхронизация отображаемой коллекции с новым отсортированным снимком.
    /// 1. Сохраняет выбранный PID. 2. Удаляет только завершившиеся/отфильтрованные PID.
    /// 3. Добавляет только новые процессы. 4. Перемещает изменившие порядок.
    /// Метрики (CpuUsage, Memory, Disk, Net) уже обновлены на существующих объектах
    /// в <see cref="UpdateSnapshot"/>, поэтому контейнеры DataGrid переиспользуются.
    /// </summary>
    private void SyncFilteredProcesses(List<ProcessInfo> sortedList)
    {
        using var _prof = ExecutionProfiler.Measure("UI.TaskManager.SyncFilteredProcesses");

        var prevSelectedPid = SelectedProcess?.Pid;

        // 1. Быстрый выход: порядок и состав идентичны — ничего не трогаем (0 уведомлений UI).
        bool orderSame = FilteredProcesses.Count == sortedList.Count;
        if (orderSame)
        {
            for (int k = 0; k < sortedList.Count; k++)
            {
                if (!ReferenceEquals(FilteredProcesses[k], sortedList[k]))
                {
                    orderSame = false;
                    break;
                }
            }
            if (orderSame) return;
        }

        // 2. Удаляем элементы, которых больше нет в новом снимке (с конца — O(n)).
        var newSet = new HashSet<ProcessInfo>(sortedList);
        for (int i = FilteredProcesses.Count - 1; i >= 0; i--)
        {
            if (!newSet.Contains(FilteredProcesses[i]))
                FilteredProcesses.RemoveAt(i);
        }

        // 3. Вставляем новые и перемещаем изменившие позицию.
        // Инвариант: после обработки позиции i -> FilteredProcesses[i] == sortedList[i].
        for (int i = 0; i < sortedList.Count; i++)
        {
            var target = sortedList[i];
            if (i < FilteredProcesses.Count && ReferenceEquals(FilteredProcesses[i], target))
                continue;

            int oldIndex = FilteredProcesses.IndexOf(target);
            if (oldIndex < 0)
            {
                // Новый процесс — только Insert, без пересоздания остальных строк.
                FilteredProcesses.Insert(i, target);
            }
            else
            {
                // Существующий процесс сменил позицию из-за сортировки — Move сохраняет контейнер.
                FilteredProcesses.Move(oldIndex, i);
            }
        }

        // 4. Восстанавливаем выбор по PID без сброса, если элемент всё ещё видим.
        if (prevSelectedPid.HasValue)
        {
            if (SelectedProcess == null || !FilteredProcesses.Contains(SelectedProcess))
            {
                ProcessInfo? reselected = null;
                foreach (var p in FilteredProcesses)
                {
                    if (p.Pid == prevSelectedPid.Value) { reselected = p; break; }
                }
                // Если процесс завершён или отфильтрован — снимаем выделение, скролл не дёргаем.
                if (!ReferenceEquals(SelectedProcess, reselected))
                    SelectedProcess = reselected;
            }
        }
    }

    /// <summary>Переключает режим отображения процессов («flat» или «tree»).</summary>
    [RelayCommand]
    public void SetViewMode(string mode)
    {
        ViewMode = string.Equals(mode, "tree", StringComparison.OrdinalIgnoreCase) ? "tree" : "flat";
    }

    /// <summary>Сворачивает или разворачивает ветвь дерева дочерних процессов.</summary>
    [RelayCommand]
    public void ToggleExpand(ProcessInfo? process)
    {
        if (process == null) return;

        if (_collapsedPids.Contains(process.Pid))
        {
            _collapsedPids.Remove(process.Pid);
            process.IsExpanded = true;
        }
        else
        {
            _collapsedPids.Add(process.Pid);
            process.IsExpanded = false;
        }

        ApplyFilterAndSort();
    }

    /// <summary>Открывает окно просмотра активных сетевых соединений (сокетов) для выбранного процесса.</summary>
    [RelayCommand]
    public void OpenProcessConnections(ProcessInfo? process)
    {
        var target = process ?? SelectedProcess;
        if (target == null || !target.HasNetworkActivity) return;

        var win = new Views.Dialogs.ProcessConnectionsWindow(target, _pipeClient)
        {
            Owner = Application.Current.MainWindow
        };
        win.Show();
    }

    /// <summary>Сортирует по указанной колонке (при повторном клике меняет направление).</summary>
    [RelayCommand]
    public void SortBy(string column)
    {
        if (_sortColumn == column)
            _sortAscending = !_sortAscending;
        else
        {
            _sortColumn = column;
            // Для ЦП, ОЗУ, Диска и Сети по умолчанию сортируем по убыванию (максимальные сверху)
            _sortAscending = column is "name" or "publisher" or "startup" or "pid";
        }

        UpdateSortIndicators();
        ApplyFilterAndSort();
    }

    private void UpdateSortIndicators()
    {
        OnPropertyChanged(nameof(SortNameIndicator));
        OnPropertyChanged(nameof(SortPidIndicator));
        OnPropertyChanged(nameof(SortCpuIndicator));
        OnPropertyChanged(nameof(SortRamIndicator));
        OnPropertyChanged(nameof(SortDiskIndicator));
        OnPropertyChanged(nameof(SortNetIndicator));
        OnPropertyChanged(nameof(SortPublisherIndicator));
        OnPropertyChanged(nameof(SortStartupIndicator));
    }

    // ──── Команды фильтрации категорий ────

    [RelayCommand]
    private void SetFilter(string filter) => SelectedFilter = filter;

    // ──── Команды действий над процессами ────

    /// <summary>Завершает выбранный процесс (Снять задачу).</summary>
    [RelayCommand]
    public async Task KillProcessAsync(ProcessInfo? process)
    {
        var target = process ?? SelectedProcess;
        if (target == null) return;

        var loc = LocalizationService.Instance;
        var msg = string.Format(loc["TmKillConfirmMsg"], target.Name, target.Pid);
        var result = MessageBox.Show(msg, loc["TmKillConfirmTitle"], MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (result != MessageBoxResult.Yes) return;

        var res = await _pipeClient.KillProcessAsync(target.Pid, target.Name);
        if (res?.Success == true)
        {
            StatusMessageRequested?.Invoke(loc["TmProcessKilled"], res.Message);
            _processMap.Remove(target.Pid);
            ApplyFilterAndSort();
        }
        else
        {
            MessageBox.Show(res?.Message ?? loc["TmKillFailed"], loc["TmError"], MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    /// <summary>Блокирует процесс в Чёрном списке, удаляет из автозапуска и завершает процесс.</summary>
    [RelayCommand]
    public async Task BlockProcessAsync(ProcessInfo? process)
    {
        var target = process ?? SelectedProcess;
        if (target == null) return;

        var loc = LocalizationService.Instance;
        var path = !string.IsNullOrEmpty(target.ExecutablePath) ? target.ExecutablePath : $"{target.Name}.exe";
        var msg = string.Format(loc["TmBlockConfirmMsg"], target.Name, path);
        var result = MessageBox.Show(msg, loc["TmBlockConfirmTitle"], MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (result != MessageBoxResult.Yes) return;

        var res = await _pipeClient.BlockProcessAsync(target.Pid, path, target.Name);
        if (res?.Success == true)
        {
            StatusMessageRequested?.Invoke(loc["TmProcessBlocked"], res.Message);
            _processMap.Remove(target.Pid);
            ApplyFilterAndSort();
        }
        else
        {
            MessageBox.Show(res?.Message ?? loc["TmBlockFailed"], loc["TmError"], MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    /// <summary>Приостанавливает выполнение выбранного процесса.</summary>
    [RelayCommand]
    public async Task SuspendProcessAsync(ProcessInfo? process)
    {
        var target = process ?? SelectedProcess;
        if (target == null) return;

        var loc = LocalizationService.Instance;
        var res = await _pipeClient.SuspendProcessAsync(target.Pid, target.Name);
        if (res?.Success == true)
        {
            target.IsSuspended = true;
            StatusMessageRequested?.Invoke(loc["TmProcessSuspended"], res.Message);
            ApplyFilterAndSort();
        }
        else
        {
            MessageBox.Show(res?.Message ?? loc["TmSuspendFailed"], loc["TmError"], MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    /// <summary>Возобновляет выполнение ранее приостановленного процесса.</summary>
    [RelayCommand]
    public async Task ResumeProcessAsync(ProcessInfo? process)
    {
        var target = process ?? SelectedProcess;
        if (target == null) return;

        var loc = LocalizationService.Instance;
        var res = await _pipeClient.ResumeProcessAsync(target.Pid, target.Name);
        if (res?.Success == true)
        {
            target.IsSuspended = false;
            StatusMessageRequested?.Invoke(loc["TmProcessResumed"], res.Message);
            ApplyFilterAndSort();
        }
        else
        {
            MessageBox.Show(res?.Message ?? loc["TmResumeFailed"], loc["TmError"], MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    /// <summary>Переходит к связанной записи автозапуска в таблице Автозагрузка.</summary>
    [RelayCommand]
    public void LocateInStartup(ProcessInfo? process)
    {
        var target = process ?? SelectedProcess;
        var loc = LocalizationService.Instance;
        if (target == null || string.IsNullOrEmpty(target.StartupEntryId))
        {
            MessageBox.Show(loc["TmNoStartupMatch"], loc["TmInfo"], MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        LocateStartupEntryRequested?.Invoke(target.StartupEntryId);
    }

    /// <summary>Открывает проводник с выделением исполняемого файла процесса.</summary>
    [RelayCommand]
    public void OpenFileLocation(ProcessInfo? process)
    {
        var target = process ?? SelectedProcess;
        var loc = LocalizationService.Instance;
        if (target == null)
        {
            MessageBox.Show(loc["TmNoProcessSelected"], loc["TmInfo"], MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        // 1. Пытаемся взять путь из метаданных процесса
        string? candidatePath = target.ExecutablePath;

        // 2. Если путь пустой — пытаемся определить его локально в UI процессе
        if (string.IsNullOrWhiteSpace(candidatePath))
        {
            candidatePath = TryGetProcessPath(target.ProcessId);
        }

        // 3. Если путь всё ещё пустой, но процесс привязан к автозапуску — проверяем детали автозапуска
        if (string.IsNullOrWhiteSpace(candidatePath) && !string.IsNullOrWhiteSpace(target.StartupLocationDetail))
        {
            candidatePath = target.StartupLocationDetail;
        }

        // 4. Очищаем путь от кавычек, параметров и спецсимволов
        string cleanPath = CleanExecutablePath(candidatePath);

        if (string.IsNullOrWhiteSpace(cleanPath))
        {
            MessageBox.Show(
                string.Format(loc["TmCantFindExePath"], target.ProcessName, target.ProcessId),
                loc["TmInfo"],
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        try
        {
            if (File.Exists(cleanPath))
            {
                // Запускаем проводник с выделением файла
                Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{cleanPath}\"")
                {
                    UseShellExecute = true
                });
                return;
            }

            var dir = Path.GetDirectoryName(cleanPath);
            if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
            {
                // Если файл недоступен напрямую, открываем каталог
                Process.Start(new ProcessStartInfo("explorer.exe", $"\"{dir}\"")
                {
                    UseShellExecute = true
                });
                return;
            }

            MessageBox.Show(
                string.Format(loc["TmExeNotFound"], cleanPath),
                loc["TmInfo"],
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(string.Format(loc["TmOpenLocationFailed"], ex.Message), loc["TmError"], MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    /// <summary>Очищает путь к исполняемому файлу: убирает кавычки, аргументы, NT-префиксы и переменные среды.</summary>
    public static string CleanExecutablePath(string? rawPath)
    {
        if (string.IsNullOrWhiteSpace(rawPath)) return string.Empty;
        var p = rawPath.Trim();

        // 1. Если путь обрамлен кавычками: "C:\path\app.exe"
        if (p.StartsWith('"'))
        {
            int close = p.IndexOf('"', 1);
            p = close > 1 ? p[1..close] : p.Trim('"');
        }
        else
        {
            // 2. Если путь без кавычек, но за ним идут параметры: C:\path\app.exe -arg
            int exeIdx = p.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
            if (exeIdx > 0 && exeIdx + 4 < p.Length && char.IsWhiteSpace(p[exeIdx + 4]))
            {
                p = p[..(exeIdx + 4)];
            }
        }

        // 3. Убираем NT-префикс пути: \??\C:\... -> C:\...
        if (p.StartsWith(@"\??\")) p = p[4..];

        // 4. Заменяем системный префикс: \SystemRoot\ -> C:\Windows\
        if (p.StartsWith(@"\SystemRoot\", StringComparison.OrdinalIgnoreCase))
        {
            var winDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            p = Path.Combine(winDir, p[@"\SystemRoot\".Length..]);
        }

        // 5. Разворачиваем системные переменные окружения
        try
        {
            p = Environment.ExpandEnvironmentVariables(p.Trim());
        }
        catch { }

        return p.Trim();
    }

    /// <summary>Пытается получить путь к исполняемому файлу процесса по PID напрямую из UI.</summary>
    private static string? TryGetProcessPath(int pid)
    {
        if (pid <= 4) return null;

        try
        {
            using var p = Process.GetProcessById(pid);
            var fn = p.MainModule?.FileName;
            if (!string.IsNullOrEmpty(fn)) return fn;
        }
        catch { }

        IntPtr hProc = OpenProcess(0x1000 /* PROCESS_QUERY_LIMITED_INFORMATION */, false, pid);
        if (hProc != IntPtr.Zero)
        {
            try
            {
                var sb = new StringBuilder(1024);
                int size = sb.Capacity;
                if (QueryFullProcessImageName(hProc, 0, sb, ref size))
                    return sb.ToString();
            }
            finally
            {
                CloseHandle(hProc);
            }
        }

        return null;
    }

    #region P/Invoke для получения путей процессов из UI
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(int processAccess, bool bInheritHandle, int processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool QueryFullProcessImageName(IntPtr hProcess, int flags, [Out] StringBuilder lpExeName, ref int lpdwSize);
    #endregion

    public void Dispose()
    {
        _searchDebounceTimer.Stop();
        _pipeClient.Connected -= OnPipeClientConnected;
        if (_isActive)
        {
            _pipeClient.ProcessSnapshotReceived -= OnProcessSnapshotReceived;
            if (_pipeClient.IsConnected)
            {
                _ = _pipeClient.UnsubscribeProcessMetricsAsync();
            }
        }
    }
}
