**NetworkConnectionAlertService.cs**
```csharp
// -------------------------------------------------------------------------------------------------------------------------------
// Файл: NetworkConnectionAlertService.cs
// Описание:
//   Реализация сервиса алертирования для управления сетевыми подключениями с проверкой на основе белых/черных списков.
//   Все коллекции защищены потокобезопасными структурами (ConcurrentDictionary), что гарантирует отсутствие исключений
//   «коллекция была изменена» при конкурентном чтении/записи. Реализовано жесткое правило приоритета:
//   Черный список имеет преимущество перед белым (Blacklist > Whitelist). При добавлении в черный список запись
//   автоматически удаляется из белого списка, чтобы предотвратить коллизии.
//
//   Авторы:    [Ваша команда безопасности]
//   Версия:    1.0
//   Дата:      2025‑12‑20
// -------------------------------------------------------------------------------------------------------------------------------

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace NetworkAlertService
{
    /// <summary>
    /// Сервис, генерирующий оповещения о сетевых подключениях, основываясь на конфигурации белых/черных списков.
    /// Все операции потокобезопасны, используется ConcurrentDictionary для хранения списков.
    /// </summary>
    public sealed class NetworkConnectionAlertService : IDisposable
    {
        // ---------------------------------------------------------------------------------------------------------------------------
        // Поля данных – потокобезопасные контейнеры.
        // ---------------------------------------------------------------------------------------------------------------------------
        private readonly ConcurrentDictionary<string, byte> _whitelistedApps;   // Идентификаторы доверенных приложений (значение не используется)
        private readonly ConcurrentDictionary<string, byte> _blacklistedApps;   // Идентификаторы запрещенных приложений
        private readonly ConcurrentDictionary<int, byte>   _allowedPorts;      // Допустимые TCP/UDP порты

        // Для выполнения снятия/добавления с регистрации по мере необходимости
        private readonly ReaderWriterLockSlim _lock = new ReaderWriterLockSlim();

        /// <summary>
        /// Инициализирует пустые списки.
        /// </summary>
        public NetworkConnectionAlertService()
        {
            _whitelistedApps = new ConcurrentDictionary<string, byte>();
            _blacklistedApps = new ConcurrentDictionary<string, byte>();
            _allowedPorts     = new ConcurrentDictionary<int, byte>();
        }

        // ---------------------------------------------------------------------------------------------------------------------------
        // Методы управления белым списком
        // ---------------------------------------------------------------------------------------------------------------------------

        /// <summary>
        /// Добавляет приложение в белый список.
        ///   • Если приложение уже находится в черном списке – оно сначала удаляется из черного списка,
        ///     чтобы избежать нарушения правила «черное имеет преимущество».
        ///   • Если приложение уже присутствует в белом списке – операция игнорируется.
        /// </summary>
        /// <param name="appId">Уникальный идентификатор приложения (например, имя файла EXE).</param>
        public void AddToWhitelist(string appId)
        {
            if (string.IsNullOrWhiteSpace(appId))
                throw new ArgumentException("Идентификатор приложения не может быть пустым.", nameof(appId));

            // Атомарно пытаемся удалить из черного списка – если есть, запись возвращается в RemoveResult.
            _blacklistedApps.TryRemove(appId, out _);

            // Добавляем в белый список (если уже присутствует – метод не изменит коллекцию).
            _whitelistedApps.TryAdd(appId, 0);
        }

        /// <summary>
        /// Удаляет приложение из белого списка (если оно там есть).
        /// </summary>
        public void RemoveFromWhitelist(string appId)
        {
            if (string.IsNullOrWhiteSpace(appId))
                return;

            _whitelistedApps.TryRemove(appId, out _);
        }

        // ---------------------------------------------------------------------------------------------------------------------------
        // Методы управления черным списком
        // ---------------------------------------------------------------------------------------------------------------------------

        /// <summary>
        /// Добавляет приложение в черный список.
        ///   • При добавлении в черный список приложение **автоматически** удаляется из белого списка,
        ///     обеспечивая выполнение правила «черное имеет преимущество» без дополнительной логики во время проверки.
        ///   • Повторное добавление игнорируется (TryAdd возвращает false).
        /// </summary>
        public void AddToBlacklist(string appId)
        {
            if (string.IsNullOrWhiteSpace(appId))
                throw new ArgumentException("Идентификатор приложения не может быть пустым.", nameof(appId));

            // Удаляем из белого списка – это бесшумное удаление, даже если запись отсутствует.
            _whitelistedApps.TryRemove(appId, out _);

            // Добавляем в черный список.
            _blacklistedApps.TryAdd(appId, 0);
        }

        /// <summary>
        /// Удаляет приложение из черного списка.
        /// </summary>
        public void RemoveFromBlacklist(string appId)
        {
            if (string.IsNullOrWhiteSpace(appId))
                return;

            _blacklistedApps.TryRemove(appId, out _);
        }

        // ---------------------------------------------------------------------------------------------------------------------------
        // Методы управления портами
        // ---------------------------------------------------------------------------------------------------------------------------

        /// <summary>
        /// Добавляет порт в список разрешенных.
        /// </summary>
        public void AddAllowedPort(int port)
        {
            if (port < 0 || port > ushort.MaxValue)
                throw new ArgumentOutOfRangeException(nameof(port), "Порт должен быть в диапазоне 0–65535.");

            _allowedPorts.TryAdd(port, 0);
        }

        /// <summary>
        /// Удаляет порт из списка разрешенных.
        /// </summary>
        public void RemoveAllowedPort(int port)
        {
            _allowedPorts.TryRemove(port, out _);
        }

        // ---------------------------------------------------------------------------------------------------------------------------
        // Метод проверки – должна ли быть разрешена запись
        // ---------------------------------------------------------------------------------------------------------------------------

        /// <summary>
        /// Определяет, разрешено ли данное приложение выполнять сетевое подключение на указанном порту.
        ///   • Правило приоритета: **черный список имеет преимущество** – если приложение в черном списке,
        ///     оно блокируется немедленно, независимо от присутствия в белом списке.
        ///   • Если порт указан ( > 0 ), он также должен присутствовать в _allowedPorts.
        /// </summary>
        /// <param name="appId">Идентификатор приложения.</param>
        /// <param name="port">Номер порта (0 означает «порт не проверяется»).</param>
        /// <returns>true, если подключение разрешено; false – в противном случае.</returns>
        public bool IsConnectionAllowed(string appId, int port)
        {
            if (string.IsNullOrWhiteSpace(appId))
                return false;   // Неверный идентификатор приложения считается не доверенным.

            // 1) Приоритет черного списка – блокировка немедленная.
            if (_blacklistedApps.ContainsKey(appId))
                return false;

            // 2) Проверка белого списка – должно быть доверенным.
            if (!_whitelistedApps.ContainsKey(appId))
                return false;

            // 3) Проверка порта – если порт != 0, он должен быть в списке разрешенных.
            if (port != 0 && !_allowedPorts.ContainsKey(port))
                return false;

            return true;
        }

        // ---------------------------------------------------------------------------------------------------------------------------
        // Перечислители для внешнего мониторинга (только для чтения)
        // ---------------------------------------------------------------------------------------------------------------------------

        /// <summary>
        /// Возвращает снимок текущего состояния белого списка (только для чтения).
        /// </summary>
        public IReadOnlyCollection<string> GetWhitelistSnapshot()
        {
            _lock.EnterReadLock();
            try
            {
                // Создаем список раз один раз; ConcurrentDictionary.Keys уже является итератором, безопасным для конкурентного чтения.
                return new List<string>(_whitelistedApps.Keys);
            }
            finally
            {
                _lock.ExitReadLock();
            }
        }

        /// <summary>
        /// Возвращает снимок текущего состояния черного списка (только для чтения).
        /// </summary>
        public IReadOnlyCollection<string> GetBlacklistSnapshot()
        {
            _lock.EnterReadLock();
            try
            {
                return new List<string>(_blacklistedApps.Keys);
            }
            finally
            {
                _lock.ExitReadLock();
            }
        }

        /// <summary>
        /// Возвращает снимок списка разрешенных портов (только для чтения).
        /// </summary>
        public IReadOnlyCollection<int> GetAllowedPortsSnapshot()
        {
            _lock.EnterReadLock();
            try
            {
                return new List<int>(_allowedPorts.Keys);
            }
            finally
            {
                _lock.ExitReadLock();
            }
        }

        // ---------------------------------------------------------------------------------------------------------------------------
        // IDisposable – освобождение ресурсов
        // ---------------------------------------------------------------------------------------------------------------------------

        /// <summary>
        /// Освобождает управляемые и неуправляемые ресурсы.
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        private void Dispose(bool disposing)
        {
            if (disposing)
            {
                _lock?.Dispose();
                // ConcurrentDictionary не требует явного Dispose, но мы вызываем его для полноты.
                _whitelistedApps.Clear();
                _blacklistedApps.Clear();
                _allowedPorts.Clear();
            }
        }
    }
}
```