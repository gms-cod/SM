# Архитектурное решение (ADR): Выбор модели провайдера и управление сессией ETW

* **Дата:** 4 ноября 2026 года  
* **Статус:** Принято (Accepted)  
* **Контекст:** Внедрение мониторинга процессов через Event Tracing for Windows (ETW) в `StartupMonitor.Service` согласно `docs/ETW_IMPLEMENTATION_PLAN.md`.

---

## 1. Контекст и проблема

В документе `docs/ROADMAP.md` в качестве целевого источника событий процессов упоминался манифестный провайдер `Microsoft-Windows-Kernel-Process` (`{22FB2AD8-EC23-422C-B0E3-5A36CE5788C9}`).

При детальном анализе реализации и библиотеки `Microsoft.Diagnostics.Tracing.TraceEvent` (версия 4.0.0) были выявлены следующие архитектурные ограничения:
1. **Несовместимость парсеров:** Парсер ядра `KernelTraceEventParser` (`source.Kernel.ProcessStart`, `source.Kernel.ProcessStop`, `ProcessDCStart`) ориентирован строго на классические события группы ядра, поступающие от сессии `SystemTraceProvider`, а не на манифестные события `Microsoft-Windows-Kernel-Process`. Использование манифестного провайдера потребовало бы создания собственного парсера MOF/манифестов и не гарантировало бы доступность полного набора полей (включая `ParentID`, `UniqueProcessKey`, `ImageFileName` и командную строку) без переопределения ключевых слов.
2. **Конфликты `NT Kernel Logger`:** Исторически классические события ядра передавались только через зарезервированную сессию с именем `NT Kernel Logger`. Попытка захватить эту сессию недопустима для фоновой службы, так как она является глобальным разделяемым ресурсом, может быть занята внешними системными инструментами (WPR, PerfView, Sysinternals Procmon) и приводит к сбоям `ERROR_ALREADY_EXISTS`.
3. **Побочные эффекты `TraceEventSession.Stop()`:** Встроенный класс `TraceEventSession` в версии 4.0.0 при остановке сессии неявно изменяет глобальный интервал семплирования CPU в Windows, что недопустимо для монитора автозагрузки.

---

## 2. Принятое архитектурное решение

Принято решение реализовать **двухуровневую модель сессии**:

### 2.1. Изолированная именованная сессия SystemTraceProvider (Windows 8+)
Начиная с Windows 8 / Windows Server 2012 ядро Windows поддерживает запуск независимых именованных сессий `SystemTraceProvider` с собственным GUID сессии, отличным от `SystemTraceControlGuid`.
* **Имя сессии:** `StartupMonitor.ProcessLifecycle` (строго изолированное фиксированное имя).
* **Флаги логирования:** `EVENT_TRACE_SYSTEM_LOGGER_MODE (0x02000000) | EVENT_TRACE_REAL_TIME_MODE (0x00000100)`.
* **Флаги включения:** `EnableFlags = EVENT_TRACE_FLAG_PROCESS (0x00000001)`.
* **Часы сессии:** `Wnode.ClientContext = 1` (QueryPerformanceCounter, QPC).
* **Управление:** Прямые Win32 P/Invoke вызовы `StartTraceW` и `ControlTraceW` из `advapi32.dll`.

### 2.2. Разделение владения native-сессией и .NET Consumer
* **Native-контроллер (`NativeProcessTraceSession`)**:
  - Отвечает за вызовы `StartTraceW` и `ControlTraceW`.
  - Управляет захватом машинного мьютекса `Global\StartupMonitor.EtwSession.Mutex`.
  - Атомарно фиксирует intent-lease в `%ProgramData%\StartupMonitor\etw_session.lease` с сохранением `SessionGuid`, PID и `CreationTime` процесса владельца.
  - При обнаружении `ERROR_ALREADY_EXISTS` проверяет, является ли сессия «осиротевшей» (orphan) от предыдущего аварийно завершенного экземпляра Startup Monitor, и останавливает её только при строгом совпадении критериев владения. Чужие сессии никогда не останавливаются.
* **.NET Consumer (`EtwProcessReader`)**:
  - Использует `ETWTraceEventSource(session.Name, TraceEventSourceType.Session)`.
  - Подключает события `source.Kernel.ProcessStart`, `ProcessStop` и `ProcessDCStart`.
  - Выполняет только быстрое неблокирующее копирование скаляров в ограниченную очередь `LifecycleIngress`.
  - Завершается по отмене `CancellationToken` с вызовом `session.Stop()`.

---

## 3. Последствия и преимущества

* **Безопасность системы:** Служба не блокирует системные инструменты профилирования (WPR/PerfView) и не трогает `NT Kernel Logger`.
* **Zero-Leak гарантия:** Нативные структуры `EVENT_TRACE_PROPERTIES` инициализируются с точным расчетом смещений `LoggerNameOffset` и `LogFileNameOffset`. Ресурсы освобождаются в блоках `finally`.
* **Плавная деградация (Graceful Degradation):**
  Если создание сессии возвращает ошибку (например, `ERROR_NO_SYSTEM_RESOURCES` из-за исчерпания пула системных трассировок или отсутствие привилегий `SeTraceMultiproviderPrivilege`), служба автоматически переходит в режим `Degraded` и сохраняет полную работоспособность на существующем механизме периодического снимка процессов (`BackgroundCleanupLoop` и `NtQuerySystemInformation`).

