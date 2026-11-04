Ниже представлен полный, готовый к использованию код для .NET 9, соответствующий всем требованиям: отсутствие локов на горячем пути, изоляция клиентов через `Channels` с таймаутом 2000 мс, строгая безопасность (Anti-Squatting) и русские комментарии.

---

### 1. PipeSecurityFactory.cs
**Настройка DACL: только System, Admins, Interactive. Без права `CreateNewInstance` (защита от Squatting).**

```csharp
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;

namespace HighPerformanceIpc
{
    /// <summary>
    /// Фабрика для создания дескриптора безопасности именованного канала (PipeSecurity).
    /// Реализует принцип минимальных привилегий и защиту от Pipe Squatting.
    /// </summary>
    public static class PipeSecurityFactory
    {
        /// <summary>
        /// Создает экземпляр <see cref="PipeSecurity"/> с настроенными ACL.
        /// </summary>
        /// <returns>Готовый к использованию PipeSecurity.</returns>
        /// <exception cref="InvalidOperationException">Если не удалось разрешить SID известных групп.</exception>
        public static PipeSecurity Create()
        {
            var pipeSecurity = new PipeSecurity();

            // Отключаем наследование ACE от родительского контейнера.
            // Канал создается в корне объектов ядра (\Device\NamedPipe), наследование не нужно и опасно.
            pipeSecurity.SetAccessRuleProtection(true, false);

            // --- Определение SID доверенных принципалов ---

            // 1. Local System (S-1-5-18) - полный контроль для системных служб.
            var systemSid = new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null);

            // 2. Built-in Administrators (S-1-5-32-544) - полный контроль для администрирования.
            var adminsSid = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);

            // 3. Interactive Users (S-1-5-4) - пользователи, вошедшие в интерактивную сессию (RDP, Консоль).
            // ВАЖНО: Это исключает сетевые логионы (S-1-5-2), сервисные аккаунты (S-1-5-6) и анонимные (S-1-5-7).
            var interactiveSid = new SecurityIdentifier(WellKnownSidType.InteractiveSid, null);

            // --- Формирование правил доступа (ACE) ---

            // ПРАВИЛО ДЛЯ СЕРВЕРА (System/Admins): Нужен FullControl для управления экземплярами.
            // CreateNewInstance НУЖЕН серверу, чтобы создавать следующие экземпляры канала (Listen -> Accept -> Create New).
            var serverRights = PipeAccessRights.FullControl;
            var serverRule = new PipeAccessRule(
                new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null), // Правило для System покрывает процесс сервиса
                serverRights,
                AccessControlType.Allow);

            pipeSecurity.AddAccessRule(serverRule);

            // Добавляем Админов явно на FullControl (на случай, если сервис запущен не от System)
            var adminRule = new PipeAccessRule(
                adminsSid,
                serverRights,
                AccessControlType.Allow);
            pipeSecurity.AddAccessRule(adminRule);

            // ПРАВИЛО ДЛЯ КЛИЕНТОВ (Interactive Users):
            // ReadWrite = Connect (0x0001) | ReadData (0x0002) | WriteData (0x0004)
            // КРИТИЧЕСКИ ВАЖНО: НЕ включаем CreateNewInstance (0x0008).
            // Без CreateNewInstance злонамеренный локальный пользователь НЕ МОЖЕТ перехватить имя канала
            // между закрытием экземпляра сервером и созданием нового (Pipe Squatting).
            var clientRights = PipeAccessRights.ReadWrite; // Только Connect + Read + Write

            var clientRule = new PipeAccessRule(
                interactiveSid,
                clientRights,
                AccessControlType.Allow);

            pipeSecurity.AddAccessRule(clientRule);

            // --- Аудит (опционально, но рекомендуется для Production) ---
            // pipeSecurity.AddAuditRule(new PipeAuditRule(...));

            return pipeSecurity;
        }
    }
}
```

---

### 2. PipeServer.cs
**Ядро: Асинхронный прием подключений, управление жизненным циклом сессий через `ConcurrentDictionary` (Lock-Free).**

```csharp
using System;
using System.Collections.Concurrent;
using System.IO.Pipes;
using System.Threading;
using System.Threading.Tasks;

namespace HighPerformanceIpc
{
    /// <summary>
    /// Транспортный сервер Named Pipes.
    /// Отвечает ТОЛЬКО за: создание каналов, прием подключений, раздачу сессий в ConcurrentDictionary.
    /// НЕ содержит бизнес-логики. Полностью Lock-Free на горячем пути.
    /// </summary>
    public sealed class PipeServer : IDisposable
    {
        private readonly string _pipeName;
        private readonly PipeSecurity _pipeSecurity;
        private readonly int _maxInstances;
        private readonly CancellationTokenSource _shutdownCts = new();
        private readonly ConcurrentDictionary<Guid, ClientSession> _sessions = new();
        private Task? _listenerTask;

        // События для верхнего слоя (EventPipeServer)
        public event Func<ClientSession, ValueTask>? ClientConnectedAsync;
        public event Func<ClientSession, ReadOnlyMemory<byte>, ValueTask>? MessageReceivedAsync;
        public event Func<ClientSession, Exception?, ValueTask>? ClientDisconnectedAsync;

        /// <summary>
        /// Инициализирует сервер.
        /// </summary>
        /// <param name="pipeName">Имя канала (без префикса \\.\pipe\).</param>
        /// <param name="pipeSecurity">Дескриптор безопасности из PipeSecurityFactory.</param>
        /// <param name="maxInstances">Макс. экземпляров ( 보통 254 или -1 для неограниченно).</param>
        public PipeServer(string pipeName, PipeSecurity pipeSecurity, int maxInstances = 254)
        {
            _pipeName = pipeName ?? throw new ArgumentNullException(nameof(pipeName));
            _pipeSecurity = pipeSecurity ?? throw new ArgumentNullException(nameof(pipeSecurity));
            _maxInstances = maxInstances;
        }

        /// <summary>
        /// Запускает цикл прослушивания в фоне.
        /// </summary>
        public void Start()
        {
            if (_listenerTask != null) return;
            _listenerTask = Task.Run(ListenLoopAsync, _shutdownCts.Token);
        }

        /// <summary>
        /// Останавливает сервер и отключает всех клиентов.
        /// </summary>
        public async ValueTask StopAsync()
        {
            _shutdownCts.Cancel();

            // 1. Ожидаем завершение слушателя
            if (_listenerTask != null)
            {
                try { await _listenerTask.ConfigureAwait(false); }
                catch (OperationCanceledException) { /* Ожидаемо */ }
            }

            // 2. Параллельно отключаем все активные сессии (Lock-Free)
            var disconnectTasks = _sessions.Values.Select(s => s.DisposeAsync().AsTask());
            await Task.WhenAll(disconnectTasks).ConfigureAwait(false);
            _sessions.Clear();
        }

        /// <summary>
        /// Основной цикл Accept. Создает экземпляр канала -> Ждет подключения -> Спавнит ClientSession.
        /// </summary>
        private async Task ListenLoopAsync()
        {
            try
            {
                while (!_shutdownCts.IsCancellationRequested)
                {
                    // Создаем НОВЫЙ экземпляр канала для КАЖДОГО клиента.
                    // Использование using здесь НЕЛЬЗЯ: владелец стрима - ClientSession.
                    var pipeStream = new NamedPipeServerStream(
                        _pipeName,
                        PipeDirection.InOut,
                        _maxInstances,
                        PipeTransmissionMode.Byte, // Потоковый режим, фрейминг делаем сами (Length-Prefixed)
                        PipeOptions.Asynchronous | PipeOptions.WriteThrough, // WriteThrough критично для низкой латентности
                        0, // InBufferSize (0 = дефолт 4096)
                        0, // OutBufferSize
                        _pipeSecurity);

                    try
                    {
                        // Асинхронное ожидание подключения клиента
                        await pipeStream.WaitForConnectionAsync(_shutdownCts.Token).ConfigureAwait(false);

                        // Подключение состоялось. Создаем сессию.
                        var session = new ClientSession(pipeStream, _shutdownCts.Token, OnSessionTerminated);

                        // Регистрируем в словаре (Lock-Free)
                        if (_sessions.TryAdd(session.Id, session))
                        {
                            // Запускаем циклы чтения/записи сессии (Fire-and-Forget, ошибки обрабатываются внутри)
                            _ = session.RunAsync();

                            // Уведомляем верхний слой
                            if (ClientConnectedAsync != null)
                                await ClientConnectedAsync(session).ConfigureAwait(false);
                        }
                        else
                        {
                            // Мало вероятно при Guid, но на случай коллизии - закрываем
                            await pipeStream.DisposeAsync().ConfigureAwait(false);
                        }
                    }
                    catch (OperationCanceledException) when (_shutdownCts.IsCancellationRequested)
                    {
                        // Нормальное завершение при остановке сервера
                        await pipeStream.DisposeAsync().ConfigureAwait(false);
                        break;
                    }
                    catch (Exception ex)
                    {
                        // Ошибка конкретного экземпляра канала (например, клиент отключился до Accept)
                        // Логируем и продолжаем слушать
                        // Logger.LogError(ex, "Ошибка в цикле Accept");
                        await pipeStream.DisposeAsync().ConfigureAwait(false);
                        // Небольшая пауза чтобы не спамить CPU при системных ошибках
                        await Task.Delay(50, _shutdownCts.Token).ConfigureAwait(false);
                    }
                }
            }
            catch (OperationCanceledException) { /* Игнор */ }
        }

        /// <summary>
        /// Callback из ClientSession при завер