using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using StartupMonitor.Shared.Models;
using StartupMonitor.UI.Services;
using StartupMonitor.UI.ViewModels;
using Xunit;

namespace StartupMonitor.Tests.ViewModels;

/// <summary>
/// Набор модульных тестов для проверки команд интеграции с брандмауэром Windows в <see cref="TaskManagerViewModel"/>.
/// Проверяет добавление процессов в белый и чёрный списки, валидацию путей и защиту критических системных процессов.
/// </summary>
public class TaskManagerFirewallTests
{
    /// <summary>
    /// Проверяет, что при попытке добавить системный процесс (по PID, флагу IsSystemProcess или критическому имени)
    /// в чёрный список брандмауэра пользователю отображается окно с предупреждением, а вызов к службе блокируется.
    /// </summary>
    /// <param name="pid">Идентификатор процесса.</param>
    /// <param name="processName">Имя исполняемого файла процесса.</param>
    /// <param name="isSystemProcess">Флаг системного процесса.</param>
    [Theory]
    [InlineData(4, "System", false)]
    [InlineData(100, "svchost.exe", false)]
    [InlineData(200, "csrss.exe", false)]
    [InlineData(300, "wininit.exe", false)]
    [InlineData(500, "unknown_worker.exe", true)]
    public async Task AddToFirewallBlacklistCommand_WhenTargetIsSystemProcess_ShowsWarningAndDoesNotCallService(
        int pid,
        string processName,
        bool isSystemProcess)
    {
        // Arrange
        var fakeClient = new FakeFirewallPipeClient();
        var vm = new TaskManagerViewModel(fakeClient);

        string? dialogMessage = null;
        string? dialogTitle = null;
        MessageBoxImage? dialogImage = null;

        vm.MessageDialog = (msg, title, btn, img) =>
        {
            dialogMessage = msg;
            dialogTitle = title;
            dialogImage = img;
        };

        var targetProcess = new ProcessInfo
        {
            Pid = pid,
            Name = processName,
            ProcessName = processName,
            IsSystemProcess = isSystemProcess,
            ExecutablePath = @"C:\Windows\System32\" + processName
        };

        // Act
        await vm.AddToFirewallBlacklistCommand.ExecuteAsync(targetProcess);

        // Assert
        Assert.Equal(0, fakeClient.AddToBlacklistCallCount);
        Assert.NotNull(dialogMessage);
        Assert.Equal(LocalizationService.Instance["TmFirewallSystemProtected"], dialogMessage);
        Assert.Equal(MessageBoxImage.Stop, dialogImage);
    }

    /// <summary>
    /// Проверяет, что для пользовательского процесса при подтверждении диалога пользователем
    /// вызывается метод <see cref="PipeClient.AddToFirewallWhitelistAsync"/> с очищенным нормализованным путем к файлу.
    /// </summary>
    [Fact]
    public async Task AddToFirewallWhitelistCommand_WhenConfirmed_CallsPipeClientWhitelist()
    {
        // Arrange
        var fakeClient = new FakeFirewallPipeClient { WhitelistResult = true };
        var vm = new TaskManagerViewModel(fakeClient);

        string? confirmDialogPrompt = null;
        vm.ConfirmDialog = (msg, title, btn, img) =>
        {
            confirmDialogPrompt = msg;
            return MessageBoxResult.Yes;
        };

        string? infoMessage = null;
        vm.MessageDialog = (msg, title, btn, img) =>
        {
            infoMessage = msg;
        };

        var process = new ProcessInfo
        {
            Pid = 4567,
            Name = "MyCustomBrowser.exe",
            ProcessName = "MyCustomBrowser.exe",
            IsSystemProcess = false,
            ExecutablePath = "\"C:\\Program Files\\Browser\\MyCustomBrowser.exe\" --profile=default"
        };

        // Act
        await vm.AddToFirewallWhitelistCommand.ExecuteAsync(process);

        // Assert
        Assert.Equal(1, fakeClient.AddToWhitelistCallCount);
        Assert.Equal(@"C:\Program Files\Browser\MyCustomBrowser.exe", fakeClient.LastWhitelistedPath);
        Assert.NotNull(confirmDialogPrompt);
        Assert.Contains("MyCustomBrowser.exe", confirmDialogPrompt);
        Assert.NotNull(infoMessage);
        Assert.Contains("MyCustomBrowser.exe", infoMessage);
    }

    /// <summary>
    /// Проверяет, что при отмене диалога подтверждения добавления в белый список
    /// обращение к службе через <see cref="PipeClient"/> не происходит.
    /// </summary>
    [Fact]
    public async Task AddToFirewallWhitelistCommand_WhenCancelled_DoesNotCallPipeClient()
    {
        // Arrange
        var fakeClient = new FakeFirewallPipeClient();
        var vm = new TaskManagerViewModel(fakeClient);

        vm.ConfirmDialog = (msg, title, btn, img) => MessageBoxResult.No;

        var process = new ProcessInfo
        {
            Pid = 5678,
            Name = "ThirdPartyUtility.exe",
            ProcessName = "ThirdPartyUtility.exe",
            IsSystemProcess = false,
            ExecutablePath = @"C:\Tools\ThirdPartyUtility.exe"
        };

        // Act
        await vm.AddToFirewallWhitelistCommand.ExecuteAsync(process);

        // Assert
        Assert.Equal(0, fakeClient.AddToWhitelistCallCount);
        Assert.Null(fakeClient.LastWhitelistedPath);
    }

    /// <summary>
    /// Проверяет, что для пользовательского процесса при подтверждении диалога пользователем
    /// вызывается метод <see cref="PipeClient.AddToFirewallBlacklistAsync"/> с очищенным путем и сформированной причиной.
    /// </summary>
    [Fact]
    public async Task AddToFirewallBlacklistCommand_WhenConfirmed_CallsPipeClientBlacklist()
    {
        // Arrange
        var fakeClient = new FakeFirewallPipeClient { BlacklistResult = true };
        var vm = new TaskManagerViewModel(fakeClient);

        vm.ConfirmDialog = (msg, title, btn, img) => MessageBoxResult.Yes;

        string? infoMessage = null;
        vm.MessageDialog = (msg, title, btn, img) =>
        {
            infoMessage = msg;
        };

        var process = new ProcessInfo
        {
            Pid = 8899,
            Name = "UntrustedMiner.exe",
            ProcessName = "UntrustedMiner.exe",
            IsSystemProcess = false,
            ExecutablePath = @"C:\Suspicious\UntrustedMiner.exe"
        };

        // Act
        await vm.AddToFirewallBlacklistCommand.ExecuteAsync(process);

        // Assert
        Assert.Equal(1, fakeClient.AddToBlacklistCallCount);
        Assert.Equal(@"C:\Suspicious\UntrustedMiner.exe", fakeClient.LastBlacklistedPath);
        Assert.NotNull(fakeClient.LastBlacklistReason);
        Assert.Contains("UntrustedMiner.exe", fakeClient.LastBlacklistReason);
        Assert.Contains("8899", fakeClient.LastBlacklistReason);
        Assert.NotNull(infoMessage);
        Assert.Contains("UntrustedMiner.exe", infoMessage);
    }

    /// <summary>
    /// Проверяет, что при отмене диалога подтверждения добавления в чёрный список
    /// обращение к службе через <see cref="PipeClient"/> не выполняется.
    /// </summary>
    [Fact]
    public async Task AddToFirewallBlacklistCommand_WhenCancelled_DoesNotCallPipeClient()
    {
        // Arrange
        var fakeClient = new FakeFirewallPipeClient();
        var vm = new TaskManagerViewModel(fakeClient);

        vm.ConfirmDialog = (msg, title, btn, img) => MessageBoxResult.No;

        var process = new ProcessInfo
        {
            Pid = 9911,
            Name = "SomeApp.exe",
            ProcessName = "SomeApp.exe",
            IsSystemProcess = false,
            ExecutablePath = @"C:\Apps\SomeApp.exe"
        };

        // Act
        await vm.AddToFirewallBlacklistCommand.ExecuteAsync(process);

        // Assert
        Assert.Equal(0, fakeClient.AddToBlacklistCallCount);
        Assert.Null(fakeClient.LastBlacklistedPath);
    }

    /// <summary>
    /// Проверяет, что если путь к исполняемому файлу процесса не может быть определён (пустой или пробельный),
    /// пользователю выводится предупреждение, а запрос к службе не отправляется.
    /// </summary>
    [Fact]
    public async Task AddToFirewallCommands_WhenExecutablePathMissing_ShowsWarningAndDoesNotCallService()
    {
        // Arrange
        var fakeClient = new FakeFirewallPipeClient();
        var vm = new TaskManagerViewModel(fakeClient);

        int warningCount = 0;
        vm.MessageDialog = (msg, title, btn, img) =>
        {
            if (msg == LocalizationService.Instance["TmFirewallPathNotFound"])
            {
                warningCount++;
            }
        };

        var processWithoutPath = new ProcessInfo
        {
            Pid = 99999, // Несуществующий PID, чтобы TryGetProcessPath вернул null
            Name = "GhostProcess.exe",
            ProcessName = "GhostProcess.exe",
            IsSystemProcess = false,
            ExecutablePath = string.Empty
        };

        // Act
        await vm.AddToFirewallWhitelistCommand.ExecuteAsync(processWithoutPath);
        await vm.AddToFirewallBlacklistCommand.ExecuteAsync(processWithoutPath);

        // Assert
        Assert.Equal(2, warningCount);
        Assert.Equal(0, fakeClient.AddToWhitelistCallCount);
        Assert.Equal(0, fakeClient.AddToBlacklistCallCount);
    }
}

/// <summary>
/// Тестовая реализация клиента IPC-канала для изоляции проверок команд управления брандмауэром.
/// Перехватывает вызовы <see cref="PipeClient.AddToFirewallWhitelistAsync"/> и <see cref="PipeClient.AddToFirewallBlacklistAsync"/>.
/// </summary>
public class FakeFirewallPipeClient : PipeClient
{
    /// <summary>Результат, возвращаемый методом добавления в белый список.</summary>
    public bool WhitelistResult { get; set; } = true;

    /// <summary>Результат, возвращаемый методом добавления в чёрный список.</summary>
    public bool BlacklistResult { get; set; } = true;

    /// <summary>Количество вызовов добавления в белый список.</summary>
    public int AddToWhitelistCallCount { get; private set; }

    /// <summary>Последний путь, переданный в белый список.</summary>
    public string? LastWhitelistedPath { get; private set; }

    /// <summary>Количество вызовов добавления в чёрный список.</summary>
    public int AddToBlacklistCallCount { get; private set; }

    /// <summary>Последний путь, переданный в чёрный список.</summary>
    public string? LastBlacklistedPath { get; private set; }

    /// <summary>Последняя причина блокировки, переданная в чёрный список.</summary>
    public string? LastBlacklistReason { get; private set; }

    /// <summary>
    /// Переопределяет метод добавления в белый список брандмауэра для тестовой верификации.
    /// </summary>
    /// <param name="path">Путь к исполняемому файлу.</param>
    /// <param name="ct">Токен отмены операции.</param>
    /// <returns>Значение свойства <see cref="WhitelistResult"/>.</returns>
    public override Task<bool> AddToFirewallWhitelistAsync(string path, CancellationToken ct = default)
    {
        AddToWhitelistCallCount++;
        LastWhitelistedPath = path;
        return Task.FromResult(WhitelistResult);
    }

    /// <summary>
    /// Переопределяет метод добавления в чёрный список брандмауэра для тестовой верификации.
    /// </summary>
    /// <param name="path">Путь к исполняемому файлу.</param>
    /// <param name="reason">Причина добавления в чёрный список.</param>
    /// <param name="ct">Токен отмены операции.</param>
    /// <returns>Значение свойства <see cref="BlacklistResult"/>.</returns>
    public override Task<bool> AddToFirewallBlacklistAsync(string path, string? reason = null, CancellationToken ct = default)
    {
        AddToBlacklistCallCount++;
        LastBlacklistedPath = path;
        LastBlacklistReason = reason;
        return Task.FromResult(BlacklistResult);
    }
}
