# Полный каталог функций и подпрограмм Startup Monitor

Документ содержит исчерпывающее структурированное описание всех функций, методов и подпрограмм каждого модуля кодовой базы проекта **Startup Monitor**.

**Формат описания для каждой функции:**
- **Название функции** — имя функции и ее принадлежность к классу/файлу;
- **Что она делает** — назначение, бизнес-логика и выполняемые операции;
- **Какие входные данные** — список аргументов с типами и пояснением назначения;
- **Какие на выходе** — тип возвращаемого значения и результат операции.

---

## Проект: StartupMonitor.Shared

### Файл: `StartupMonitor.Shared\ServiceManager.cs`

#### `ServiceManager.IsProcessRunning` (строка 18)
- **Название функции**: `IsProcessRunning`
- **Что она делает**: Проверяет, запущен ли процесс с указанным именем (без .exe).
- **Какие входные данные**: `processName` (`string`): Имя процесса без расширения.
- **Какие на выходе**: `bool`: true, если хотя бы один процесс найден.

#### `ServiceManager.IsServiceRunning` (строка 27)
- **Название функции**: `IsServiceRunning`
- **Что она делает**: Проверяет, запущен ли процесс службы StartupMonitor.Service.
- **Какие входные данные**: Входные данные отсутствуют (параметры не требуются).
- **Какие на выходе**: `bool`: Логический результат выполнения (`true` в случае успеха или соответствия условию, иначе `false`).

#### `ServiceManager.IsServiceInstalled` (строка 33)
- **Название функции**: `IsServiceInstalled`
- **Что она делает**: Проверяет, установлена ли служба StartupMonitor в SCM.
- **Какие входные данные**: Входные данные отсутствуют (параметры не требуются).
- **Какие на выходе**: `bool`: Логический результат выполнения (`true` в случае успеха или соответствия условию, иначе `false`).

#### `ServiceManager.GetInstalledServicePath` (строка 48)
- **Название функции**: `GetInstalledServicePath`
- **Что она делает**: Путь к exe установленной службы из реестра (ImagePath).
- **Какие входные данные**: Входные данные отсутствуют (параметры не требуются).
- **Какие на выходе**: `string?`: Текстовая строка с результатом операции или сообщением.

#### `ServiceManager.CheckServiceStatus` (строка 79)
- **Название функции**: `CheckServiceStatus`
- **Что она делает**: Выполняет детальную проверку регистрации, автозапуска и активности службы.
- **Какие входные данные**: Входные данные отсутствуют (параметры не требуются).
- **Какие на выходе**: `ServiceRegistrationStatus`: Результирующий объект или экземпляр структуры данных.

#### `ServiceManager.EnsureServiceConfiguredAndRunningAsync` (строка 146)
- **Название функции**: `EnsureServiceConfiguredAndRunningAsync`
- **Что она делает**: Комплексно проверяет службу: если не установлена, не в автозапуске или путь устарел — настраивает её с start=auto, запускает и ожидает готовности, выводя статус на экран.
- **Какие входные данные**: `reportProgress` (`Action<string>?`): Колбэк для вывода текстового статуса на экран.; `timeoutMs` (`int`): Максимальное время ожидания запуска (мс).
- **Какие на выходе**: `Task`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `ServiceManager.EnsureServiceRunningAsync` (строка 258)
- **Название функции**: `EnsureServiceRunningAsync`
- **Что она делает**: Ожидает запуска службы (до timeoutMs), при необходимости запуская процесс напрямую.
- **Какие входные данные**: `timeoutMs` (`int`): Значение параметра `timeoutMs` (int).
- **Какие на выходе**: `Task`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `ServiceManager.TryInstallServiceAsync` (строка 264)
- **Название функции**: `TryInstallServiceAsync`
- **Что она делает**: Устанавливает или перерегистрирует службу StartupMonitor в SCM (требует прав администратора).
- **Какие входные данные**: Входные данные отсутствуют (параметры не требуются).
- **Какие на выходе**: `Task`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `ServiceManager.RunScElevated` (строка 272)
- **Название функции**: `RunScElevated`
- **Что она делает**: Запускает sc.exe с правами администратора (UAC) и ожидает завершения.
- **Какие входные данные**: `arguments` (`string`): Аргументы командной строки для sc.; `checkExitCode` (`bool`): Проверять код возврата (выбрасывать исключение при ненулевом).
- **Какие на выходе**: `Task`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `ServiceManager.StopService` (строка 299)
- **Название функции**: `StopService`
- **Что она делает**: Останавливает службу StartupMonitor (через SCM или убивает процесс).
- **Какие входные данные**: Входные данные отсутствуют (параметры не требуются).
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `ServiceManager.IsUiRunning` (строка 323)
- **Название функции**: `IsUiRunning`
- **Что она делает**: Проверяет, запущен ли процесс UI.
- **Какие входные данные**: Входные данные отсутствуют (параметры не требуются).
- **Какие на выходе**: `bool`: Логический результат выполнения (`true` в случае успеха или соответствия условию, иначе `false`).

---

### Файл: `StartupMonitor.Shared\Audit\AuditReportGenerator.cs`

#### `AuditReportGenerator.GenerateHtml` (строка 25)
- **Название функции**: `GenerateHtml`
- **Что она делает**: GenerateHtml: Выполняет операцию GenerateHtml.
- **Какие входные данные**: `report)` (`AuditReport`): Входные данные: значение параметра report) (AuditReport).
- **Какие на выходе**: `string`: Выходные данные: результирующее значение типа string.

#### `AuditReportGenerator.GenerateCsv` (строка 29)
- **Название функции**: `GenerateCsv`
- **Что она делает**: GenerateCsv: Выполняет операцию GenerateCsv.
- **Какие входные данные**: `report)` (`AuditReport`): Входные данные: значение параметра report) (AuditReport).
- **Какие на выходе**: `string`: Выходные данные: результирующее значение типа string.

#### `AuditReportGenerator.GenerateJson` (строка 33)
- **Название функции**: `GenerateJson`
- **Что она делает**: GenerateJson: Выполняет операцию GenerateJson.
- **Какие входные данные**: `report)` (`AuditReport`): Входные данные: значение параметра report) (AuditReport).
- **Какие на выходе**: `string`: Выходные данные: результирующее значение типа string.

#### `AuditReportGenerator.GenerateHtmlBytes` (строка 38)
- **Название функции**: `GenerateHtmlBytes`
- **Что она делает**: GenerateHtmlBytes: Выполняет операцию GenerateHtmlBytes.
- **Какие входные данные**: `report)` (`AuditReport`): Входные данные: значение параметра report) (AuditReport).
- **Какие на выходе**: `byte[]`: Выходные данные: результирующее значение типа byte[].

#### `AuditReportGenerator.GenerateCsvBytes` (строка 42)
- **Название функции**: `GenerateCsvBytes`
- **Что она делает**: GenerateCsvBytes: Выполняет операцию GenerateCsvBytes.
- **Какие входные данные**: `report)` (`AuditReport`): Входные данные: значение параметра report) (AuditReport).
- **Какие на выходе**: `byte[]`: Выходные данные: результирующее значение типа byte[].

#### `AuditReportGenerator.GenerateJsonBytes` (строка 46)
- **Название функции**: `GenerateJsonBytes`
- **Что она делает**: GenerateJsonBytes: Выполняет операцию GenerateJsonBytes.
- **Какие входные данные**: `report)` (`AuditReport`): Входные данные: значение параметра report) (AuditReport).
- **Какие на выходе**: `byte[]`: Выходные данные: результирующее значение типа byte[].

#### `AuditReportGenerator.GetExporter` (строка 51)
- **Название функции**: `GetExporter`
- **Что она делает**: Получить экспортер по расширению или названию формата (.html, .csv, .json).
- **Какие входные данные**: `formatOrExtension` (`string`): Значение параметра `formatOrExtension` (string).
- **Какие на выходе**: `IAuditReportExporter`: Результирующий объект или экземпляр структуры данных.

#### `AuditReportGenerator.Export` (строка 66)
- **Название функции**: `Export`
- **Что она делает**: Экспортировать отчет в строку по расширению или формату.
- **Какие входные данные**: `report` (`AuditReport`): Значение параметра `report` (AuditReport).; `formatOrExtension` (`string`): Значение параметра `formatOrExtension` (string).
- **Какие на выходе**: `string`: Текстовая строка с результатом операции или сообщением.

#### `AuditReportGenerator.ExportBytes` (строка 74)
- **Название функции**: `ExportBytes`
- **Что она делает**: Экспортировать отчет в массив байтов (с учетом BOM для CSV и кодировок).
- **Какие входные данные**: `report` (`AuditReport`): Значение параметра `report` (AuditReport).; `formatOrExtension` (`string`): Значение параметра `formatOrExtension` (string).
- **Какие на выходе**: `byte[]`: Коллекция или набор результирующих объектов.

---

### Файл: `StartupMonitor.Shared\Audit\Exporters\CsvAuditReportExporter.cs`

#### `CsvAuditReportExporter.ExportToString` (строка 21)
- **Название функции**: `ExportToString`
- **Что она делает**: ExportToString: Экспортирует данные отчета ToString.
- **Какие входные данные**: `report` (`AuditReport`): Входные данные: значение параметра report (AuditReport).
- **Какие на выходе**: `string`: Выходные данные: результирующее значение типа string.

#### `CsvAuditReportExporter.ExportToBytes` (строка 110)
- **Название функции**: `ExportToBytes`
- **Что она делает**: ExportToBytes: Экспортирует данные отчета ToBytes.
- **Какие входные данные**: `report` (`AuditReport`): Входные данные: значение параметра report (AuditReport).
- **Какие на выходе**: `byte[]`: Выходные данные: результирующее значение типа byte[].

#### `CsvAuditReportExporter.EscapeCsv` (строка 126)
- **Название функции**: `EscapeCsv`
- **Что она делает**: EscapeCsv: Выполняет операцию EscapeCsv.
- **Какие входные данные**: `value` (`string?`): Входные данные: значение параметра value (string?).
- **Какие на выходе**: `string`: Выходные данные: результирующее значение типа string.

---

### Файл: `StartupMonitor.Shared\Audit\Exporters\HtmlAuditReportExporter.cs`

#### `HtmlAuditReportExporter.ExportToString` (строка 24)
- **Название функции**: `ExportToString`
- **Что она делает**: ExportToString: Экспортирует данные отчета ToString.
- **Какие входные данные**: `report` (`AuditReport`): Входные данные: значение параметра report (AuditReport).
- **Какие на выходе**: `string`: Выходные данные: результирующее значение типа string.

#### `HtmlAuditReportExporter.ExportToBytes` (строка 79)
- **Название функции**: `ExportToBytes`
- **Что она делает**: ExportToBytes: Экспортирует данные отчета ToBytes.
- **Какие входные данные**: `report` (`AuditReport`): Входные данные: значение параметра report (AuditReport).
- **Какие на выходе**: `byte[]`: Выходные данные: результирующее значение типа byte[].

#### `HtmlAuditReportExporter.AppendHeader` (строка 88)
- **Название функции**: `AppendHeader`
- **Что она делает**: AppendHeader: Добавляет секцию разметки Header.
- **Какие входные данные**: `sb` (`StringBuilder`): Входные данные: значение параметра sb (StringBuilder).; `m` (`AuditMetadata`): Входные данные: значение параметра m (AuditMetadata).; `s` (`AuditSummary`): Входные данные: значение параметра s (AuditSummary).
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `HtmlAuditReportExporter.AppendKpiCards` (строка 114)
- **Название функции**: `AppendKpiCards`
- **Что она делает**: AppendKpiCards: Добавляет секцию разметки KpiCards.
- **Какие входные данные**: `sb` (`StringBuilder`): Входные данные: значение параметра sb (StringBuilder).; `s` (`AuditSummary`): Входные данные: значение параметра s (AuditSummary).
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `HtmlAuditReportExporter.AppendControls` (строка 161)
- **Название функции**: `AppendControls`
- **Что она делает**: AppendControls: Добавляет секцию разметки Controls.
- **Какие входные данные**: `sb` (`StringBuilder`): Входные данные: значение параметра sb (StringBuilder).; `report` (`AuditReport`): Входные данные: значение параметра report (AuditReport).
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `HtmlAuditReportExporter.AppendStartupEntriesSection` (строка 186)
- **Название функции**: `AppendStartupEntriesSection`
- **Что она делает**: AppendStartupEntriesSection: Добавляет секцию разметки StartupEntriesSection.
- **Какие входные данные**: `sb` (`StringBuilder`): Входные данные: значение параметра sb (StringBuilder).; `entries` (`List<StartupEntry>`): Входные данные: значение параметра entries (List<StartupEntry>).
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `HtmlAuditReportExporter.AppendProcessesSection` (строка 303)
- **Название функции**: `AppendProcessesSection`
- **Что она делает**: AppendProcessesSection: Добавляет секцию разметки ProcessesSection.
- **Какие входные данные**: `sb` (`StringBuilder`): Входные данные: значение параметра sb (StringBuilder).; `processes` (`List<ProcessInfo>`): Входные данные: значение параметра processes (List<ProcessInfo>).
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `HtmlAuditReportExporter.AppendFooter` (строка 363)
- **Название функции**: `AppendFooter`
- **Что она делает**: AppendFooter: Добавляет секцию разметки Footer.
- **Какие входные данные**: `sb` (`StringBuilder`): Входные данные: значение параметра sb (StringBuilder).; `m` (`AuditMetadata`): Входные данные: значение параметра m (AuditMetadata).
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `HtmlAuditReportExporter.RenderAiStars` (строка 374)
- **Название функции**: `RenderAiStars`
- **Что она делает**: RenderAiStars: Выполняет операцию RenderAiStars.
- **Какие входные данные**: `avgRating` (`double`): Входные данные: значение параметра avgRating (double).
- **Какие на выходе**: `string`: Выходные данные: результирующее значение типа string.

#### `HtmlAuditReportExporter.RenderEntryAiRating` (строка 404)
- **Название функции**: `RenderEntryAiRating`
- **Что она делает**: RenderEntryAiRating: Выполняет операцию RenderEntryAiRating.
- **Какие входные данные**: `entry` (`StartupEntry`): Входные данные: значение параметра entry (StartupEntry).
- **Какие на выходе**: `string`: Выходные данные: результирующее значение типа string.

#### `HtmlAuditReportExporter.RenderCirclBadge` (строка 437)
- **Название функции**: `RenderCirclBadge`
- **Что она делает**: RenderCirclBadge: Выполняет операцию RenderCirclBadge.
- **Какие входные данные**: `zone` (`string?`): Входные данные: значение параметра zone (string?).; `detection` (`string?`): Входные данные: значение параметра detection (string?).
- **Какие на выходе**: `string`: Выходные данные: результирующее значение типа string.

#### `HtmlAuditReportExporter.RenderThreatBadge` (строка 455)
- **Название функции**: `RenderThreatBadge`
- **Что она делает**: RenderThreatBadge: Выполняет операцию RenderThreatBadge.
- **Какие входные данные**: `info` (`CliThreatInfo?`): Входные данные: значение параметра info (CliThreatInfo?).
- **Какие на выходе**: `string`: Выходные данные: результирующее значение типа string.

#### `HtmlAuditReportExporter.RenderStartupLink` (строка 473)
- **Название функции**: `RenderStartupLink`
- **Что она делает**: RenderStartupLink: Выполняет операцию RenderStartupLink.
- **Какие входные данные**: `proc` (`ProcessInfo`): Входные данные: значение параметра proc (ProcessInfo).
- **Какие на выходе**: `string`: Выходные данные: результирующее значение типа string.

#### `HtmlAuditReportExporter.RenderProcessState` (строка 485)
- **Название функции**: `RenderProcessState`
- **Что она делает**: RenderProcessState: Выполняет операцию RenderProcessState.
- **Какие входные данные**: `proc` (`ProcessInfo`): Входные данные: значение параметра proc (ProcessInfo).
- **Какие на выходе**: `string`: Выходные данные: результирующее значение типа string.

#### `HtmlAuditReportExporter.GetStatusCssClass` (строка 497)
- **Название функции**: `GetStatusCssClass`
- **Что она делает**: GetStatusCssClass: Возвращает StatusCssClass.
- **Какие входные данные**: `status` (`EntryStatus`): Входные данные: значение параметра status (EntryStatus).
- **Какие на выходе**: `string`: Выходные данные: результирующее значение типа string.

#### `HtmlAuditReportExporter.GetStatusDisplayName` (строка 508)
- **Название функции**: `GetStatusDisplayName`
- **Что она делает**: GetStatusDisplayName: Возвращает StatusDisplayName.
- **Какие входные данные**: `status` (`EntryStatus`): Входные данные: значение параметра status (EntryStatus).
- **Какие на выходе**: `string`: Выходные данные: результирующее значение типа string.

#### `HtmlAuditReportExporter.FormatRate` (строка 519)
- **Название функции**: `FormatRate`
- **Что она делает**: FormatRate: Выполняет операцию FormatRate.
- **Какие входные данные**: `kbs` (`double`): Входные данные: значение параметра kbs (double).
- **Какие на выходе**: `string`: Выходные данные: результирующее значение типа string.

#### `HtmlAuditReportExporter.H` (строка 528)
- **Название функции**: `H`
- **Что она делает**: Выполняет операцию H в рамках подсистемы.
- **Какие входные данные**: `value)` (`string?`): Значение параметра `value)` (string?).
- **Какие на выходе**: `string`: Текстовая строка с результатом операции или сообщением.

#### `HtmlAuditReportExporter.AppendStyles` (строка 532)
- **Название функции**: `AppendStyles`
- **Что она делает**: AppendStyles: Добавляет секцию разметки Styles.
- **Какие входные данные**: `sb` (`StringBuilder`): Входные данные: значение параметра sb (StringBuilder).
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `HtmlAuditReportExporter.AppendScripts` (строка 742)
- **Название функции**: `AppendScripts`
- **Что она делает**: AppendScripts: Добавляет секцию разметки Scripts.
- **Какие входные данные**: `sb` (`StringBuilder`): Входные данные: значение параметра sb (StringBuilder).
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

---

### Файл: `StartupMonitor.Shared\Audit\Exporters\JsonAuditReportExporter.cs`

#### `JsonAuditReportExporter.ExportToString` (строка 20)
- **Название функции**: `ExportToString`
- **Что она делает**: ExportToString: Экспортирует данные отчета ToString.
- **Какие входные данные**: `report` (`AuditReport`): Входные данные: значение параметра report (AuditReport).
- **Какие на выходе**: `string`: Выходные данные: результирующее значение типа string.

#### `JsonAuditReportExporter.ExportToBytes` (строка 36)
- **Название функции**: `ExportToBytes`
- **Что она делает**: ExportToBytes: Экспортирует данные отчета ToBytes.
- **Какие входные данные**: `report` (`AuditReport`): Входные данные: значение параметра report (AuditReport).
- **Какие на выходе**: `byte[]`: Выходные данные: результирующее значение типа byte[].

---

### Файл: `StartupMonitor.Shared\Diagnostics\ExecutionProfiler.cs`

#### `ExecutionProfiler.Profile` (строка 51)
- **Название функции**: `Profile`
- **Что она делает**: Начинает замер операции. Нулевые GC-аллокации. Использование: <c>using var _ = ExecutionProfiler.Profile("Service.ScanAll");</c>
- **Какие входные данные**: `operationName` (`string`): Значение параметра `operationName` (string).
- **Какие на выходе**: `Scope`: Результирующий объект или экземпляр структуры данных.

#### `ExecutionProfiler.Measure` (строка 62)
- **Название функции**: `Measure`
- **Что она делает**: Алиас <see cref="Profile"/> для удобства и обратной совместимости. Использование: <c>using var _ = ExecutionProfiler.Measure("Service.ScanAll");</c>
- **Какие входные данные**: `operationName)` (`string`): Значение параметра `operationName)` (string).
- **Какие на выходе**: `Scope`: Результирующий объект или экземпляр структуры данных.

#### `ExecutionProfiler.Record` (строка 65)
- **Название функции**: `Record`
- **Что она делает**: Записывает готовый замер в тиках Stopwatch.
- **Какие входные данные**: `operationName` (`string`): Значение параметра `operationName` (string).; `elapsedTicks` (`long`): Значение параметра `elapsedTicks` (long).
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `ExecutionProfiler.Record` (строка 83)
- **Название функции**: `Record`
- **Что она делает**: Записывает готовый замер из <see cref="TimeSpan"/>.
- **Какие входные данные**: `operationName` (`string`): Значение параметра `operationName` (string).; `elapsed` (`TimeSpan`): Значение параметра `elapsed` (TimeSpan).
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `ExecutionProfiler.GetSnapshotAndReset` (строка 92)
- **Название функции**: `GetSnapshotAndReset`
- **Что она делает**: Возвращает структурированный снимок всех замеров и сбрасывает счётчики (атомарно). Сортировка — по убыванию TotalMs.
- **Какие входные данные**: Входные данные отсутствуют (параметры не требуются).
- **Какие на выходе**: `List<ProfileSnapshot>`: Коллекция или набор результирующих объектов.

#### `ExecutionProfiler.DrainReport` (строка 136)
- **Название функции**: `DrainReport`
- **Что она делает**: Формирует структурированный текстовый отчёт за прошедшее окно (5 минут) и сбрасывает счётчики.
- **Какие входные данные**: `componentName` (`string`): Значение параметра `componentName` (string).
- **Какие на выходе**: `string`: Текстовая строка с результатом операции или сообщением.

#### `ExecutionProfiler.DumpAndReset` (строка 161)
- **Название функции**: `DumpAndReset`
- **Что она делает**: Алиас для <see cref="DrainReport"/>.
- **Какие входные данные**: `componentName` (`string`): Значение параметра `componentName` (string).
- **Какие на выходе**: `string`: Текстовая строка с результатом операции или сообщением.

#### `ExecutionProfiler.Clear` (строка 193)
- **Название функции**: `Clear`
- **Что она делает**: Очищает все счётчики без отчёта (для тестов).
- **Какие входные данные**: `)`: Значение параметра `)`.
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `ExecutionProfiler.Scope` (строка 207)
- **Название функции**: `Scope`
- **Что она делает**: Scope: Выполняет операцию Scope.
- **Какие входные данные**: `operationName` (`string`): Входные данные: значение параметра operationName (string).; `startTimestamp` (`long`): Входные данные: значение параметра startTimestamp (long).
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `ExecutionProfiler.Dispose` (строка 214)
- **Название функции**: `Dispose`
- **Что она делает**: Dispose: Освобождает занятые ресурсы объекта.
- **Какие входные данные**: Входные данные отсутствуют (параметры не требуются).
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

---

### Файл: `StartupMonitor.Shared\Extensions\RegistryExtensions.cs`

#### `RegistryExtensions.readonly` (строка 9)
- **Название функции**: `readonly`
- **Что она делает**: Выполняет операцию readonly в рамках подсистемы.
- **Какие входные данные**: `Key` (`string`): Значение параметра `Key` (string).; `Name` (`string?`): Значение параметра `Name` (string?).; `Location` (`EntryLocation`): Значение параметра `Location` (EntryLocation).; `Label` (`string`): Значение параметра `Label` (string).
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `RegistryExtensions.ScanHklmRunKeys` (строка 20)
- **Название функции**: `ScanHklmRunKeys`
- **Что она делает**: ScanHklmRunKeys: Выполняет операцию ScanHklmRunKeys.
- **Какие входные данные**: Входные данные отсутствуют (параметры не требуются).
- **Какие на выходе**: `List<StartupEntry>`: Выходные данные: результирующее значение типа List<StartupEntry>.

#### `RegistryExtensions.ScanHkcuRunKeys` (строка 51)
- **Название функции**: `ScanHkcuRunKeys`
- **Что она делает**: ScanHkcuRunKeys: Выполняет операцию ScanHkcuRunKeys.
- **Какие входные данные**: Входные данные отсутствуют (параметры не требуются).
- **Какие на выходе**: `List<StartupEntry>`: Выходные данные: результирующее значение типа List<StartupEntry>.

#### `RegistryExtensions.ParsePath` (строка 89)
- **Название функции**: `ParsePath`
- **Что она делает**: ParsePath: Выполняет операцию ParsePath.
- **Какие входные данные**: `commandLine` (`string`): Входные данные: значение параметра commandLine (string).
- **Какие на выходе**: `string`: Выходные данные: результирующее значение типа string.

#### `RegistryExtensions.GetFilePublisher` (строка 107)
- **Название функции**: `GetFilePublisher`
- **Что она делает**: GetFilePublisher: Возвращает FilePublisher.
- **Какие входные данные**: `filePath` (`string`): Входные данные: значение параметра filePath (string).
- **Какие на выходе**: `string`: Выходные данные: результирующее значение типа string.

---

### Файл: `StartupMonitor.Shared\Helpers\AuthenticodeVerifier.cs`

#### `AuthenticodeVerifier.WINTRUST_DATA` (строка 77)
- **Название функции**: `WINTRUST_DATA`
- **Что она делает**: WINTRUST_DATA: Выполняет операцию WINTRUST_DATA.
- **Какие входные данные**: `fileInfo` (`WINTRUST_FILE_INFO`): Входные данные: значение параметра fileInfo (WINTRUST_FILE_INFO).
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `AuthenticodeVerifier.Dispose` (строка 97)
- **Название функции**: `Dispose`
- **Что она делает**: Dispose: Освобождает занятые ресурсы объекта.
- **Какие входные данные**: Входные данные отсутствуют (параметры не требуются).
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `AuthenticodeVerifier.VerifyFile` (строка 118)
- **Название функции**: `VerifyFile`
- **Что она делает**: Проверяет цифровую подпись Authenticode для указанного исполняемого файла с использованием кэша.
- **Какие входные данные**: `filePath` (`string?`): Значение параметра `filePath` (string?).
- **Какие на выходе**: `SignatureVerificationResult`: Результирующий объект или экземпляр структуры данных.

#### `AuthenticodeVerifier.PerformVerification` (строка 158)
- **Название функции**: `PerformVerification`
- **Что она делает**: PerformVerification: Выполняет операцию PerformVerification.
- **Какие входные данные**: `filePath` (`string`): Входные данные: значение параметра filePath (string).
- **Какие на выходе**: `SignatureVerificationResult`: Выходные данные: результирующее значение типа SignatureVerificationResult.

---

### Файл: `StartupMonitor.Shared\Helpers\CliHeuristicAnalyzer.cs`

#### `CliHeuristicAnalyzer.Analyze` (строка 17)
- **Название функции**: `Analyze`
- **Что она делает**: Проводит полный эвристический анализ командной строки и пути файла.
- **Какие входные данные**: `commandLine` (`string?`): Значение параметра `commandLine` (string?).; `filePath` (`string?`): Значение параметра `filePath` (string?).
- **Какие на выходе**: `CliThreatInfo`: Результирующий объект или экземпляр структуры данных.

#### `CliHeuristicAnalyzer.CheckBase64Patterns` (строка 114)
- **Название функции**: `CheckBase64Patterns`
- **Что она делает**: CheckBase64Patterns: Выполняет операцию CheckBase64Patterns.
- **Какие входные данные**: `text` (`string`): Входные данные: значение параметра text (string).; `indicators` (`List<string>`): Входные данные: значение параметра indicators (List<string>).
- **Какие на выходе**: `bool`: Выходные данные: результирующее значение типа bool.

#### `CliHeuristicAnalyzer.CheckDownloaderPatterns` (строка 143)
- **Название функции**: `CheckDownloaderPatterns`
- **Что она делает**: CheckDownloaderPatterns: Выполняет операцию CheckDownloaderPatterns.
- **Какие входные данные**: `text` (`string`): Входные данные: значение параметра text (string).; `indicators` (`List<string>`): Входные данные: значение параметра indicators (List<string>).
- **Какие на выходе**: `bool`: Выходные данные: результирующее значение типа bool.

#### `CliHeuristicAnalyzer.CheckHiddenExecutionPatterns` (строка 182)
- **Название функции**: `CheckHiddenExecutionPatterns`
- **Что она делает**: CheckHiddenExecutionPatterns: Выполняет операцию CheckHiddenExecutionPatterns.
- **Какие входные данные**: `text` (`string`): Входные данные: значение параметра text (string).; `indicators` (`List<string>`): Входные данные: значение параметра indicators (List<string>).
- **Какие на выходе**: `bool`: Выходные данные: результирующее значение типа bool.

#### `CliHeuristicAnalyzer.CheckSuspiciousLocations` (строка 214)
- **Название функции**: `CheckSuspiciousLocations`
- **Что она делает**: CheckSuspiciousLocations: Выполняет операцию CheckSuspiciousLocations.
- **Какие входные данные**: `path` (`string`): Входные данные: значение параметра path (string).; `cmd` (`string`): Входные данные: значение параметра cmd (string).; `indicators` (`List<string>`): Входные данные: значение параметра indicators (List<string>).
- **Какие на выходе**: `bool`: Выходные данные: результирующее значение типа bool.

#### `CliHeuristicAnalyzer.CheckDoubleExtension` (строка 245)
- **Название функции**: `CheckDoubleExtension`
- **Что она делает**: CheckDoubleExtension: Выполняет операцию CheckDoubleExtension.
- **Какие входные данные**: `path` (`string`): Входные данные: значение параметра path (string).; `cmd` (`string`): Входные данные: значение параметра cmd (string).; `indicators` (`List<string>`): Входные данные: значение параметра indicators (List<string>).
- **Какие на выходе**: `bool`: Выходные данные: результирующее значение типа bool.

#### `CliHeuristicAnalyzer.CheckBypassPolicy` (строка 286)
- **Название функции**: `CheckBypassPolicy`
- **Что она делает**: CheckBypassPolicy: Выполняет операцию CheckBypassPolicy.
- **Какие входные данные**: `text` (`string`): Входные данные: значение параметра text (string).; `indicators` (`List<string>`): Входные данные: значение параметра indicators (List<string>).
- **Какие на выходе**: `bool`: Выходные данные: результирующее значение типа bool.

---

### Файл: `StartupMonitor.Shared\Helpers\CpuUsageHelper.cs`

#### `CpuUsageHelper.GetSystemTimes` (строка 20)
- **Название функции**: `GetSystemTimes`
- **Что она делает**: Получает или формирует SystemTimes.
- **Какие входные данные**: `lpIdleTime` (`out long`): Входные данные: значение параметра lpIdleTime (out long).; `lpKernelTime` (`out long`): Входные данные: значение параметра lpKernelTime (out long).; `lpUserTime` (`out long`): Входные данные: значение параметра lpUserTime (out long).
- **Какие на выходе**: `extern bool`: Выходные данные: результирующее значение типа extern bool.

#### `CpuUsageHelper.OpenProcess` (строка 28)
- **Название функции**: `OpenProcess`
- **Что она делает**: Выполняет операцию OpenProcess в рамках подсистемы.
- **Какие входные данные**: `dwDesiredAccess` (`uint`): Входные данные: значение параметра dwDesiredAccess (uint).; `bInheritHandle` (`[MarshalAs(UnmanagedType.Bool)] bool`): Входные данные: значение параметра bInheritHandle ([MarshalAs(UnmanagedType.Bool)] bool).; `dwProcessId` (`int`): Входные данные: значение параметра dwProcessId (int).
- **Какие на выходе**: `extern IntPtr`: Выходные данные: результирующее значение типа extern IntPtr.

#### `CpuUsageHelper.CloseHandle` (строка 44)
- **Название функции**: `CloseHandle`
- **Что она делает**: Выполняет операцию CloseHandle в рамках подсистемы.
- **Какие входные данные**: `hObject` (`IntPtr`): Входные данные: значение параметра hObject (IntPtr).
- **Какие на выходе**: `extern bool`: Выходные данные: результирующее значение типа extern bool.

#### `CpuUsageHelper.CpuUsageHelper` (строка 71)
- **Название функции**: `CpuUsageHelper`
- **Что она делает**: CpuUsageHelper: Выполняет операцию CpuUsageHelper.
- **Какие входные данные**: Входные данные отсутствуют (параметры не требуются).
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `CpuUsageHelper.Initialize` (строка 77)
- **Название функции**: `Initialize`
- **Что она делает**: Инициализирует базовые отсчеты системного и процессорного времени.
- **Какие входные данные**: Входные данные отсутствуют (параметры не требуются).
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `CpuUsageHelper.GetCpuUsage` (строка 97)
- **Название функции**: `GetCpuUsage`
- **Что она делает**: Возвращает текущий процент загрузки процессора.
- **Какие входные данные**: `excludeSelf` (`bool`): Если true — из суммарного времени занятости вычитается процессорное время процессов StartupMonitor (сервиса и UI), возвращая процент нагрузки внешними процессами.
- **Какие на выходе**: `int`: Загрузка процессора в процентах (0..100).

#### `CpuUsageHelper.MeasureUsage` (строка 154)
- **Название функции**: `MeasureUsage`
- **Что она делает**: Выполняет синхронный замер нагрузки за указанный интервал ожидания. Полезно перед принятием решения в фоновом потоке, если сервис давно не опрашивал процессор.
- **Какие входные данные**: `sampleDelayMs` (`int`): Значение параметра `sampleDelayMs` (int).; `excludeSelf` (`bool`): Значение параметра `excludeSelf` (bool).
- **Какие на выходе**: `int`: Числовой результат, количество обработанных элементов или метрика.

#### `CpuUsageHelper.CalculateCpuPercentage` (строка 189)
- **Название функции**: `CalculateCpuPercentage`
- **Что она делает**: Вычисляет процент загрузки процессора по переданным дельтам времени. Используется для тестирования без вызовов API ОС.
- **Какие входные данные**: `deltaTotal` (`long`): Значение параметра `deltaTotal` (long).; `deltaIdle` (`long`): Значение параметра `deltaIdle` (long).; `deltaOur` (`long`): Значение параметра `deltaOur` (long).; `excludeSelf` (`bool`): Значение параметра `excludeSelf` (bool).
- **Какие на выходе**: `int`: Числовой результат, количество обработанных элементов или метрика.

#### `CpuUsageHelper.GetOurProcessesTicks` (строка 204)
- **Название функции**: `GetOurProcessesTicks`
- **Что она делает**: Суммирует процессорное время (в 100-нс тактах) для всех процессов StartupMonitor (сервис, UI).
- **Какие входные данные**: Входные данные отсутствуют (параметры не требуются).
- **Какие на выходе**: `long`: Числовой результат, количество обработанных элементов или метрика.

#### `CpuUsageHelper.GetProcessTicks` (строка 249)
- **Название функции**: `GetProcessTicks`
- **Что она делает**: Возвращает процессорное время (в 100-нс тактах, kernel + user) одного процесса по PID без перечисления всех процессов через <see cref="Process.GetProcesses"/>. Использует P/Invoke GetProcessTimes с fallback на Process.GetProcessById.
- **Какие входные данные**: `pid` (`int`): Идентификатор процесса.
- **Какие на выходе**: `long`: Сумма kernel + user времени в тактах; 0 если процесс недоступен.

#### `CpuUsageHelper.GetProcessesTicks` (строка 290)
- **Название функции**: `GetProcessesTicks`
- **Что она делает**: Суммирует процессорное время (в 100-нс тактах) для заданных PID без вызова тяжёлого <see cref="Process.GetProcesses"/>.
- **Какие входные данные**: `pids` (`IEnumerable<int>`): Идентификаторы процессов (UI + сервис).
- **Какие на выходе**: `long`: Суммарное процессорное время в тактах.

#### `CpuUsageHelper.GetCpuUsageForPids` (строка 308)
- **Название функции**: `GetCpuUsageForPids`
- **Что она делает**: Точечный замер загрузки CPU с вычетом только указанных PID (UI + сервис), без перечисления всех процессов Windows. Предназначен для лёгкого таймера UI.
- **Какие входные данные**: `ownPids` (`IEnumerable<int>`): PID текущего UI-процесса и службы (если известен).; `excludeSelf` (`bool`): true — вычесть время ownPids (внешняя нагрузка); false — общая.
- **Какие на выходе**: `int`: Загрузка процессора в процентах (0..100).

#### `CpuUsageHelper.static` (строка 373)
- **Название функции**: `static`
- **Что она делает**: Возвращает пару (total, external) за один проход системного замера по указанным PID. Избегает двойного вызова GetSystemTimes/GetProcessTimes в тике таймера UI.
- **Какие входные данные**: `Total` (`int`): Значение параметра `Total` (int).; `ownPids` (`int External) GetCpuUsagePairForPids(IEnumerable<int>`): Значение параметра `ownPids` (int External) GetCpuUsagePairForPids(IEnumerable<int>).
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

---

### Файл: `StartupMonitor.Shared\Helpers\SystemProtectionHelper.cs`

#### `SystemProtectionHelper.IsProtectedProcessName` (строка 39)
- **Название функции**: `IsProtectedProcessName`
- **Что она делает**: Проверяет, является ли указанное имя файла или путь защищённым системным процессом Windows.
- **Какие входные данные**: `fileName` (`string?`): Значение параметра `fileName` (string?).
- **Какие на выходе**: `bool`: Логический результат выполнения (`true` в случае успеха или соответствия условию, иначе `false`).

#### `SystemProtectionHelper.IsHostProcess` (строка 49)
- **Название функции**: `IsHostProcess`
- **Что она делает**: Проверяет, является ли процесс хостом служб/скриптов (svchost, rundll32 и др.).
- **Какие входные данные**: `fileName` (`string?`): Значение параметра `fileName` (string?).
- **Какие на выходе**: `bool`: Логический результат выполнения (`true` в случае успеха или соответствия условию, иначе `false`).

#### `SystemProtectionHelper.IsCoreWindowsService` (строка 59)
- **Название функции**: `IsCoreWindowsService`
- **Что она делает**: Проверяет, относится ли имя службы к критически важным службам ядра Windows или Per-User службам.
- **Какие входные данные**: `serviceName` (`string?`): Значение параметра `serviceName` (string?).
- **Какие на выходе**: `bool`: Логический результат выполнения (`true` в случае успеха или соответствия условию, иначе `false`).

#### `SystemProtectionHelper.IsProtectedSystemEntry` (строка 108)
- **Название функции**: `IsProtectedSystemEntry`
- **Что она делает**: Проверяет, является ли запись критически важным компонентом ОС Windows (служба, задача, системный хост), отключение или удаление которого может дестабилизировать систему или вызвать BSoD.
- **Какие входные данные**: `entry` (`StartupEntry?`): Значение параметра `entry` (StartupEntry?).
- **Какие на выходе**: `bool`: Логический результат выполнения (`true` в случае успеха или соответствия условию, иначе `false`).

#### `SystemProtectionHelper.IsProtectedWmiSubscription` (строка 184)
- **Название функции**: `IsProtectedWmiSubscription`
- **Что она делает**: Проверяет, является ли WMI-консьюмер или фильтр защищённым системным компонентом Windows.
- **Какие входные данные**: `name` (`string?`): Значение параметра `name` (string?).
- **Какие на выходе**: `bool`: Логический результат выполнения (`true` в случае успеха или соответствия условию, иначе `false`).

---

### Файл: `StartupMonitor.Shared\Models\JsonDataStore.cs`

#### `JsonDataStore.JsonDataStore` (строка 56)
- **Название функции**: `JsonDataStore`
- **Что она делает**: Инициализирует хранилище с путём к старому единому файлу конфигурации.
- **Какие входные данные**: `filePath` (`string`): Путь к legacy-файлу (startup-monitor.json).
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `JsonDataStore.Load` (строка 84)
- **Название функции**: `Load`
- **Что она делает**: Загружает и возвращает полный конфиг из секционных файлов. Мигрирует legacy при необходимости.
- **Какие входные данные**: Входные данные отсутствуют (параметры не требуются).
- **Какие на выходе**: `AppConfig`: Результирующий объект или экземпляр структуры данных.

#### `JsonDataStore.MigrateLegacyIfNeeded` (строка 115)
- **Название функции**: `MigrateLegacyIfNeeded`
- **Что она делает**: Старый единый файл раскладывается по секционным и переименовывается в .migrated.
- **Какие входные данные**: Входные данные отсутствуют (параметры не требуются).
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `JsonDataStore.ReadHistory` (строка 168)
- **Название функции**: `ReadHistory`
- **Что она делает**: Читает history.jsonl (поддерживая как однострочный JSONL, так и многострочный legacy формат).
- **Какие входные данные**: Входные данные отсутствуют (параметры не требуются).
- **Какие на выходе**: `List<HistoryEntry>`: Коллекция или набор результирующих объектов.

#### `JsonDataStore.ContentHash` (строка 240)
- **Название функции**: `ContentHash`
- **Что она делает**: Вычисляет SHA256-хеш строки содержимого для сравнения изменений.
- **Какие входные данные**: `content` (`string`): Значение параметра `content` (string).
- **Какие на выходе**: `string`: Текстовая строка с результатом операции или сообщением.

#### `JsonDataStore.WriteSectionAtomic` (строка 245)
- **Название функции**: `WriteSectionAtomic`
- **Что она делает**: Атомарная запись секции; пропускается, если содержимое не изменилось.
- **Какие входные данные**: `path` (`string`): Значение параметра `path` (string).; `json` (`string`): Значение параметра `json` (string).
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `JsonDataStore.AppendHistoryLine` (строка 257)
- **Название функции**: `AppendHistoryLine`
- **Что она делает**: Дописывает одну запись истории в конец history.jsonl в одну строку.
- **Какие входные данные**: `entry` (`HistoryEntry`): Значение параметра `entry` (HistoryEntry).
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `JsonDataStore.TrimHistoryFile` (строка 264)
- **Название функции**: `TrimHistoryFile`
- **Что она делает**: Вычищает из history.jsonl события старше HistoryDays (перезаписью при старте).
- **Какие входные данные**: Входные данные отсутствуют (параметры не требуются).
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `JsonDataStore.SaveEntries` (строка 278)
- **Название функции**: `SaveEntries`
- **Что она делает**: Сохраняет startup-entries.json.
- **Какие входные данные**: `)`: Значение параметра `)`.
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `JsonDataStore.SaveBlacklist` (строка 280)
- **Название функции**: `SaveBlacklist`
- **Что она делает**: Сохраняет blacklist.json.
- **Какие входные данные**: `)`: Значение параметра `)`.
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `JsonDataStore.SaveRemoved` (строка 282)
- **Название функции**: `SaveRemoved`
- **Что она делает**: Сохраняет removed-entries.json.
- **Какие входные данные**: `)`: Значение параметра `)`.
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `JsonDataStore.SaveSettings` (строка 285)
- **Название функции**: `SaveSettings`
- **Что она делает**: Сохраняет settings.json (language, theme, AppSettings).
- **Какие входные данные**: Входные данные отсутствуют (параметры не требуются).
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `JsonDataStore.Save` (строка 293)
- **Название функции**: `Save`
- **Что она делает**: Сохраняет весь конфиг — все секции целиком.
- **Какие входные данные**: `config` (`AppConfig`): Значение параметра `config` (AppConfig).
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `JsonDataStore.ClearEntries` (строка 310)
- **Название функции**: `ClearEntries`
- **Что она делает**: Очищает все записи автозапуска и историю.
- **Какие входные данные**: Входные данные отсутствуют (параметры не требуются).
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `JsonDataStore.AddEntry` (строка 323)
- **Название функции**: `AddEntry`
- **Что она делает**: Добавляет запись автозапуска с проверкой на дубликат по Id.
- **Какие входные данные**: `entry` (`StartupEntry`): Добавляемая запись.; `checkDuplicate` (`bool`): Проверять ли дубликат по Id.
- **Какие на выходе**: `bool`: true, если запись добавлена; false, если дубликат найден.

#### `JsonDataStore.FindEntry` (строка 338)
- **Название функции**: `FindEntry`
- **Что она делает**: Находит запись по предикату.
- **Какие входные данные**: `predicate` (`Func<StartupEntry, bool>`): Значение параметра `predicate` (Func<StartupEntry, bool>).
- **Какие на выходе**: `StartupEntry?`: Результирующий объект или экземпляр структуры данных.

#### `JsonDataStore.AddEntryBatch` (строка 350)
- **Название функции**: `AddEntryBatch`
- **Что она делает**: Добавляет пачку записей (опционально с предварительной очисткой).
- **Какие входные данные**: `entries` (`List<StartupEntry>`): Список добавляемых записей.; `clearFirst` (`bool`): Очистить существующие записи и историю перед добавлением.
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `JsonDataStore.NormalizeLocationDetail` (строка 370)
- **Название функции**: `NormalizeLocationDetail`
- **Что она делает**: Нормализует строку местоположения для дедупликации (устраняет расхождение HKCU\user\ vs HKCU\).
- **Какие входные данные**: `detail` (`string?`): Значение параметра `detail` (string?).
- **Какие на выходе**: `string`: Текстовая строка с результатом операции или сообщением.

#### `JsonDataStore.DeduplicateEntries` (строка 390)
- **Название функции**: `DeduplicateEntries`
- **Что она делает**: Удаляет дубликаты записей с одинаковыми Name|Location|NormalizedLocationDetail (регистронезависимо).
- **Какие входные данные**: Входные данные отсутствуют (параметры не требуются).
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `JsonDataStore.RemoveEntry` (строка 447)
- **Название функции**: `RemoveEntry`
- **Что она делает**: Удаляет запись по её Id.
- **Какие входные данные**: `entryId` (`string`): Значение параметра `entryId` (string).
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `JsonDataStore.UpdateEntry` (строка 458)
- **Название функции**: `UpdateEntry`
- **Что она делает**: Обновляет запись (заменяет по Id).
- **Какие входные данные**: `entry` (`StartupEntry`): Значение параметра `entry` (StartupEntry).
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `JsonDataStore.AddHistory` (строка 475)
- **Название функции**: `AddHistory`
- **Что она делает**: Добавляет запись в историю, обрезая элементы старше HistoryDays.
- **Какие входные данные**: `entry` (`HistoryEntry`): Значение параметра `entry` (HistoryEntry).
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `JsonDataStore.GetRemovedEntries` (строка 492)
- **Название функции**: `GetRemovedEntries`
- **Что она делает**: Возвращает копию списка удалённых записей.
- **Какие входные данные**: Входные данные отсутствуют (параметры не требуются).
- **Какие на выходе**: `List<RemovedEntry>`: Коллекция или набор результирующих объектов.

#### `JsonDataStore.IsRemoved` (строка 502)
- **Название функции**: `IsRemoved`
- **Что она делает**: Проверяет, есть ли запись с указанным стабильным Id в списке удалённых.
- **Какие входные данные**: `entryId` (`string`): Значение параметра `entryId` (string).
- **Какие на выходе**: `bool`: Логический результат выполнения (`true` в случае успеха или соответствия условию, иначе `false`).

#### `JsonDataStore.AddRemovedEntry` (строка 512)
- **Название функции**: `AddRemovedEntry`
- **Что она делает**: Добавляет запись в бэкап удалённых (или обновляет существующую по OriginalEntryId).
- **Какие входные данные**: `entry` (`RemovedEntry`): Значение параметра `entry` (RemovedEntry).
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `JsonDataStore.GetRemovedEntryByOriginalId` (строка 525)
- **Название функции**: `GetRemovedEntryByOriginalId`
- **Что она делает**: Возвращает бэкап удаленной записи по её OriginalEntryId или null.
- **Какие входные данные**: `entryId` (`string`): Значение параметра `entryId` (string).
- **Какие на выходе**: `RemovedEntry?`: Результирующий объект или экземпляр структуры данных.

#### `JsonDataStore.RemoveRemovedEntriesByReason` (строка 535)
- **Название функции**: `RemoveRemovedEntriesByReason`
- **Что она делает**: Удаляет все записи из бэкапа с указанной причиной удаления (например LockdownBlocked).
- **Какие входные данные**: `reason` (`string`): Значение параметра `reason` (string).
- **Какие на выходе**: `int`: Числовой результат, количество обработанных элементов или метрика.

#### `JsonDataStore.RemoveRemovedEntryByOriginalId` (строка 550)
- **Название функции**: `RemoveRemovedEntryByOriginalId`
- **Что она делает**: Удаляет запись из бэкапа по OriginalEntryId.
- **Какие входные данные**: `entryId` (`string`): Значение параметра `entryId` (string).
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `JsonDataStore.RemoveRemovedEntry` (строка 564)
- **Название функции**: `RemoveRemovedEntry`
- **Что она делает**: Удаляет запись из бэкапа удалённых по Id.
- **Какие входные данные**: `removedId` (`string`): Значение параметра `removedId` (string).
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `JsonDataStore.GetSnapshots` (строка 577)
- **Название функции**: `GetSnapshots`
- **Что она делает**: Загружает и возвращает список всех сохранённых снимков автозапуска (сортировка от новых к старым).
- **Какие входные данные**: Входные данные отсутствуют (параметры не требуются).
- **Какие на выходе**: `List<BaselineSnapshot>`: Коллекция или набор результирующих объектов.

#### `JsonDataStore.GetSnapshot` (строка 587)
- **Название функции**: `GetSnapshot`
- **Что она делает**: Находит и возвращает снимок по его уникальному Id.
- **Какие входные данные**: `snapshotId` (`string`): Значение параметра `snapshotId` (string).
- **Какие на выходе**: `BaselineSnapshot?`: Результирующий объект или экземпляр структуры данных.

#### `JsonDataStore.SaveSnapshot` (строка 597)
- **Название функции**: `SaveSnapshot`
- **Что она делает**: Сохраняет (добавляет или обновляет) снимок автозапуска в snapshots.json.
- **Какие входные данные**: `snapshot` (`BaselineSnapshot`): Значение параметра `snapshot` (BaselineSnapshot).
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `JsonDataStore.DeleteSnapshot` (строка 610)
- **Название функции**: `DeleteSnapshot`
- **Что она делает**: Удаляет снимок по его Id.
- **Какие входные данные**: `snapshotId` (`string`): Значение параметра `snapshotId` (string).
- **Какие на выходе**: `bool`: Логический результат выполнения (`true` в случае успеха или соответствия условию, иначе `false`).

---

### Файл: `StartupMonitor.Shared\Models\PipeMessage.cs`

#### `PipeMessage.AiConfigPayload` (строка 175)
- **Название функции**: `AiConfigPayload`
- **Что она делает**: Полезная нагрузка для сообщения SetAiConfig: ключ API, URL эндпоинта и имя модели.
- **Какие входные данные**: `ApiKey` (`string`): Значение параметра `ApiKey` (string).; `ApiUrl` (`string?`): Значение параметра `ApiUrl` (string?).; `Model` (`string?`): Значение параметра `Model` (string?).
- **Какие на выходе**: `record`: Результирующий объект или экземпляр структуры данных.

#### `PipeMessage.TestAiResultPayload` (строка 178)
- **Название функции**: `TestAiResultPayload`
- **Что она делает**: Полезная нагрузка для ответа TestAiConnectionResponse: результат проверки связи с AI.
- **Какие входные данные**: `Success` (`bool`): Значение параметра `Success` (bool).; `Message` (`string`): Значение параметра `Message` (string).
- **Какие на выходе**: `record`: Результирующий объект или экземпляр структуры данных.

#### `PipeMessage.CpuThrottlePayload` (строка 181)
- **Название функции**: `CpuThrottlePayload`
- **Что она делает**: Полезная нагрузка для настройки троттлинга процессора.
- **Какие входные данные**: `Enabled` (`bool`): Значение параметра `Enabled` (bool).; `Threshold` (`int`): Значение параметра `Threshold` (int).
- **Какие на выходе**: `record`: Результирующий объект или экземпляр структуры данных.

#### `PipeMessage.StrictModePayload` (строка 184)
- **Название функции**: `StrictModePayload`
- **Что она делает**: Полезная нагрузка для настройки строгого режима.
- **Какие входные данные**: `Enabled` (`bool`): Значение параметра `Enabled` (bool).
- **Какие на выходе**: `record`: Результирующий объект или экземпляр структуры данных.

#### `PipeMessage.ActionNoticePayload` (строка 188)
- **Название функции**: `ActionNoticePayload`
- **Что она делает**: Полезная нагрузка для уведомления ActionNotice: имя записи и код действия ("killed", "disabled", "autoDisabled", "removed", "restored", "lockdownBlocked").
- **Какие входные данные**: `Name` (`string`): Значение параметра `Name` (string).; `Action` (`string`): Значение параметра `Action` (string).
- **Какие на выходе**: `record`: Результирующий объект или экземпляр структуры данных.

#### `PipeMessage.KillProcessPayload` (строка 191)
- **Название функции**: `KillProcessPayload`
- **Что она делает**: Полезная нагрузка для завершения процесса по PID.
- **Какие входные данные**: `ProcessId` (`int`): Значение параметра `ProcessId` (int).; `ProcessName` (`string?`): Значение параметра `ProcessName` (string?).
- **Какие на выходе**: `record`: Результирующий объект или экземпляр структуры данных.

#### `PipeMessage.BlockProcessPayload` (строка 194)
- **Название функции**: `BlockProcessPayload`
- **Что она делает**: Полезная нагрузка для блокировки процесса через Чёрный список.
- **Какие входные данные**: `ProcessId` (`int`): Значение параметра `ProcessId` (int).; `FilePath` (`string`): Значение параметра `FilePath` (string).; `ProcessName` (`string?`): Значение параметра `ProcessName` (string?).
- **Какие на выходе**: `record`: Результирующий объект или экземпляр структуры данных.

#### `PipeMessage.SuspendProcessPayload` (строка 197)
- **Название функции**: `SuspendProcessPayload`
- **Что она делает**: Полезная нагрузка для приостановки процесса по PID.
- **Какие входные данные**: `ProcessId` (`int`): Значение параметра `ProcessId` (int).; `ProcessName` (`string?`): Значение параметра `ProcessName` (string?).
- **Какие на выходе**: `record`: Результирующий объект или экземпляр структуры данных.

#### `PipeMessage.ResumeProcessPayload` (строка 200)
- **Название функции**: `ResumeProcessPayload`
- **Что она делает**: Полезная нагрузка для возобновления процесса по PID.
- **Какие входные данные**: `ProcessId` (`int`): Значение параметра `ProcessId` (int).; `ProcessName` (`string?`): Значение параметра `ProcessName` (string?).
- **Какие на выходе**: `record`: Результирующий объект или экземпляр структуры данных.

#### `PipeMessage.ProcessActionResultPayload` (строка 203)
- **Название функции**: `ProcessActionResultPayload`
- **Что она делает**: Полезная нагрузка ответа на действие над процессом.
- **Какие входные данные**: `Success` (`bool`): Значение параметра `Success` (bool).; `Message` (`string`): Значение параметра `Message` (string).; `ProcessId` (`int`): Значение параметра `ProcessId` (int).
- **Какие на выходе**: `record`: Результирующий объект или экземпляр структуры данных.

#### `PipeMessage.TestRemoteNotificationPayload` (строка 218)
- **Название функции**: `TestRemoteNotificationPayload`
- **Что она делает**: Полезная нагрузка запроса проверки канала удалённых уведомлений ("telegram", "discord", "all").
- **Какие входные данные**: `Channel` (`string`): Значение параметра `Channel` (string).
- **Какие на выходе**: `record`: Результирующий объект или экземпляр структуры данных.

#### `PipeMessage.TestRemoteNotificationResultPayload` (строка 221)
- **Название функции**: `TestRemoteNotificationResultPayload`
- **Что она делает**: Полезная нагрузка ответа на запрос проверки канала удалённых уведомлений.
- **Какие входные данные**: `Success` (`bool`): Значение параметра `Success` (bool).; `Message` (`string`): Значение параметра `Message` (string).
- **Какие на выходе**: `record`: Результирующий объект или экземпляр структуры данных.

#### `PipeMessage.SetDelayedEntryPayload` (строка 224)
- **Название функции**: `SetDelayedEntryPayload`
- **Что она делает**: Полезная нагрузка установки отложенного запуска записи автозапуска.
- **Какие входные данные**: `EntryId` (`string`): Значение параметра `EntryId` (string).; `DelaySeconds` (`int`): Значение параметра `DelaySeconds` (int).
- **Какие на выходе**: `record`: Результирующий объект или экземпляр структуры данных.

#### `PipeMessage.CancelDelayedEntryPayload` (строка 227)
- **Название функции**: `CancelDelayedEntryPayload`
- **Что она делает**: Полезная нагрузка отмены отложенного запуска записи автозапуска.
- **Какие входные данные**: `EntryId` (`string`): Значение параметра `EntryId` (string).
- **Какие на выходе**: `record`: Результирующий объект или экземпляр структуры данных.

#### `PipeMessage.LaunchDelayedEntryPayload` (строка 230)
- **Название функции**: `LaunchDelayedEntryPayload`
- **Что она делает**: Полезная нагрузка принудительного запуска отложенной записи.
- **Какие входные данные**: `EntryId` (`string`): Значение параметра `EntryId` (string).
- **Какие на выходе**: `record`: Результирующий объект или экземпляр структуры данных.

#### `PipeMessage.CreateSnapshotPayload` (строка 238)
- **Название функции**: `CreateSnapshotPayload`
- **Что она делает**: CreateSnapshotPayload: Выполняет операцию CreateSnapshotPayload.
- **Какие входные данные**: Входные данные отсутствуют (параметры не требуются).
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `PipeMessage.CreateSnapshotPayload` (строка 242)
- **Название функции**: `CreateSnapshotPayload`
- **Что она делает**: CreateSnapshotPayload: Выполняет операцию CreateSnapshotPayload.
- **Какие входные данные**: `name` (`string?`): Входные данные: значение параметра name (string?).; `description` (`string?`): Входные данные: значение параметра description (string?).
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `PipeMessage.DeleteSnapshotPayload` (строка 246)
- **Название функции**: `DeleteSnapshotPayload`
- **Что она делает**: Полезная нагрузка удаления контрольного снимка.
- **Какие входные данные**: `SnapshotId` (`string`): Значение параметра `SnapshotId` (string).
- **Какие на выходе**: `record`: Результирующий объект или экземпляр структуры данных.

#### `PipeMessage.CompareSnapshotPayload` (строка 249)
- **Название функции**: `CompareSnapshotPayload`
- **Что она делает**: Полезная нагрузка сравнения текущего состояния с контрольным снимком.
- **Какие входные данные**: `SnapshotId` (`string`): Значение параметра `SnapshotId` (string).
- **Какие на выходе**: `record`: Результирующий объект или экземпляр структуры данных.

#### `PipeMessage.MonitoringStatePayload` (строка 269)
- **Название функции**: `MonitoringStatePayload`
- **Что она делает**: MonitoringStatePayload: Выполняет операцию MonitoringStatePayload.
- **Какие входные данные**: Входные данные отсутствуют (параметры не требуются).
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `PipeMessage.MonitoringStatePayload` (строка 272)
- **Название функции**: `MonitoringStatePayload`
- **Что она делает**: MonitoringStatePayload: Выполняет операцию MonitoringStatePayload.
- **Какие входные данные**: `isPaused` (`bool`): Входные данные: значение параметра isPaused (bool).
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `PipeMessage.Deconstruct` (строка 279)
- **Название функции**: `Deconstruct`
- **Что она делает**: Deconstruct: Выполняет операцию Deconstruct.
- **Какие входные данные**: `isPaused` (`out bool`): Входные данные: значение параметра isPaused (out bool).
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `PipeMessage.EntriesRevisionRequest` (строка 310)
- **Название функции**: `EntriesRevisionRequest`
- **Что она делает**: EntriesRevisionRequest: Выполняет операцию EntriesRevisionRequest.
- **Какие входные данные**: Входные данные отсутствуют (параметры не требуются).
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `PipeMessage.EntriesRevisionRequest` (строка 313)
- **Название функции**: `EntriesRevisionRequest`
- **Что она делает**: EntriesRevisionRequest: Выполняет операцию EntriesRevisionRequest.
- **Какие входные данные**: `clientRevision` (`long`): Входные данные: значение параметра clientRevision (long).
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `PipeMessage.EntriesDeltaResponse` (строка 332)
- **Название функции**: `EntriesDeltaResponse`
- **Что она делает**: EntriesDeltaResponse: Выполняет операцию EntriesDeltaResponse.
- **Какие входные данные**: Входные данные отсутствуют (параметры не требуются).
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `PipeMessage.EntriesDeltaResponse` (строка 337)
- **Название функции**: `EntriesDeltaResponse`
- **Что она делает**: EntriesDeltaResponse: Выполняет операцию EntriesDeltaResponse.
- **Какие входные данные**: `newRevision` (`long`): Входные данные: значение параметра newRevision (long).; `isModified` (`bool`): Входные данные: значение параметра isModified (bool).; `entries` (`List<StartupEntry>?`): Входные данные: значение параметра entries (List<StartupEntry>?).
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

---

### Файл: `StartupMonitor.Shared\Models\ProcessInfo.cs`

#### `ProcessInfo.OnPropertyChanged` (строка 16)
- **Название функции**: `OnPropertyChanged`
- **Что она делает**: OnPropertyChanged: Обрабатывает событие OnPropertyChanged.
- **Какие входные данные**: `name` (`[CallerMemberName] string?`): Входные данные: значение параметра name ([CallerMemberName] string?).
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

---

### Файл: `StartupMonitor.Shared\Models\ProcessNetworkSocket.cs`

#### `ProcessNetworkSocket.CheckIsPrivateAddress` (строка 59)
- **Название функции**: `CheckIsPrivateAddress`
- **Что она делает**: Определяет, является ли IP-адрес частным/локальным (Loopback, Link-Local, RFC 1918).
- **Какие входные данные**: `ipStr` (`string`): Значение параметра `ipStr` (string).
- **Какие на выходе**: `bool`: Логический результат выполнения (`true` в случае успеха или соответствия условию, иначе `false`).

---

### Файл: `StartupMonitor.Shared\Models\StartupEntry.cs`

#### `StartupEntry.OnPropertyChanged` (строка 14)
- **Название функции**: `OnPropertyChanged`
- **Что она делает**: Уведомляет UI об изменении свойства.
- **Какие входные данные**: `name` (`[CallerMemberName] string?`): Значение параметра `name` ([CallerMemberName] string?).
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

---

### Файл: `StartupMonitor.Shared\Models\SystemComputerInfo.cs`

#### `SystemComputerInfo.ToReportString` (строка 62)
- **Название функции**: `ToReportString`
- **Что она делает**: Формирует структурированный текстовый отчёт о конфигурации компьютера.
- **Какие входные данные**: Входные данные отсутствуют (параметры не требуются).
- **Какие на выходе**: `string`: Текстовая строка с результатом операции или сообщением.

#### `SystemComputerInfo.FormatBytes` (строка 82)
- **Название функции**: `FormatBytes`
- **Что она делает**: Преобразует байты в понятный строковый размер.
- **Какие входные данные**: `bytes` (`long`): Значение параметра `bytes` (long).
- **Какие на выходе**: `string`: Текстовая строка с результатом операции или сообщением.

#### `SystemComputerInfo.GlobalMemoryStatusEx` (строка 116)
- **Название функции**: `GlobalMemoryStatusEx`
- **Что она делает**: Выполняет операцию GlobalMemoryStatusEx в рамках подсистемы.
- **Какие входные данные**: `lpBuffer` (`ref MEMORYSTATUSEX`): Входные данные: значение параметра lpBuffer (ref MEMORYSTATUSEX).
- **Какие на выходе**: `extern bool`: Выходные данные: результирующее значение типа extern bool.

#### `SystemComputerInfo.Collect` (строка 124)
- **Название функции**: `Collect`
- **Что она делает**: Мгновенно собирает актуальные аппаратные характеристики компьютера через прямой Win32 P/Invoke и реестр. Время выполнения: ~1-3 мс (нулевая нагрузка на процессор).
- **Какие входные данные**: Входные данные отсутствуют (параметры не требуются).
- **Какие на выходе**: `SystemComputerInfo`: Результирующий объект или экземпляр структуры данных.

---

### Файл: `StartupMonitor.Shared\Models\BootTime\BootSessionInfo.cs`

#### `BootSessionInfo.FormatDuration` (строка 78)
- **Название функции**: `FormatDuration`
- **Что она делает**: Преобразует миллисекунды в человекочитаемую строку длительности.
- **Какие входные данные**: `ms` (`long`): Значение параметра `ms` (long).
- **Какие на выходе**: `string`: Текстовая строка с результатом операции или сообщением.

---

### Файл: `StartupMonitor.Shared\Models\Snapshot\BaselineSnapshot.cs`

#### `BaselineSnapshot.Create` (строка 34)
- **Название функции**: `Create`
- **Что она делает**: Создает новый контрольный снимок автозапуска из списка записей.
- **Какие входные данные**: `name` (`string`): Значение параметра `name` (string).; `description` (`string?`): Значение параметра `description` (string?).; `entries` (`IEnumerable<StartupEntry>`): Значение параметра `entries` (IEnumerable<StartupEntry>).
- **Какие на выходе**: `BaselineSnapshot`: Результирующий объект или экземпляр структуры данных.

---

### Файл: `StartupMonitor.Shared\Models\Snapshot\SnapshotComparator.cs`

#### `SnapshotComparator.Compare` (строка 11)
- **Название функции**: `Compare`
- **Что она делает**: Сравнивает эталонный снимок с текущим набором записей автозапуска.
- **Какие входные данные**: `baseline` (`BaselineSnapshot`): Значение параметра `baseline` (BaselineSnapshot).; `currentEntries` (`IReadOnlyList<StartupEntry>`): Значение параметра `currentEntries` (IReadOnlyList<StartupEntry>).
- **Какие на выходе**: `SnapshotComparisonResult`: Результирующий объект или экземпляр структуры данных.

#### `SnapshotComparator.MakeCompositeKey` (строка 97)
- **Название функции**: `MakeCompositeKey`
- **Что она делает**: MakeCompositeKey: Выполняет операцию MakeCompositeKey.
- **Какие входные данные**: `location` (`EntryLocation`): Входные данные: значение параметра location (EntryLocation).; `detail` (`string?`): Входные данные: значение параметра detail (string?).; `name` (`string?`): Входные данные: значение параметра name (string?).
- **Какие на выходе**: `string`: Выходные данные: результирующее значение типа string.

#### `SnapshotComparator.DetectModifications` (строка 107)
- **Название функции**: `DetectModifications`
- **Что она делает**: DetectModifications: Выполняет операцию DetectModifications.
- **Какие входные данные**: `current` (`StartupEntry`): Входные данные: значение параметра current (StartupEntry).; `baseline` (`StartupEntrySnapshotItem`): Входные данные: значение параметра baseline (StartupEntrySnapshotItem).
- **Какие на выходе**: `SnapshotModifiedEntryItem`: Выходные данные: результирующее значение типа SnapshotModifiedEntryItem.

---

### Файл: `StartupMonitor.Shared\Models\Snapshot\StartupEntrySnapshotItem.cs`

#### `StartupEntrySnapshotItem.FromEntry` (строка 48)
- **Название функции**: `FromEntry`
- **Что она делает**: Создает снимок записи из актуальной модели StartupEntry (алиас для FromStartupEntry).
- **Какие входные данные**: `entry)` (`StartupEntry`): Значение параметра `entry)` (StartupEntry).
- **Какие на выходе**: `StartupEntrySnapshotItem`: Результирующий объект или экземпляр структуры данных.

#### `StartupEntrySnapshotItem.FromStartupEntry` (строка 51)
- **Название функции**: `FromStartupEntry`
- **Что она делает**: Создает снимок записи из актуальной модели StartupEntry.
- **Какие входные данные**: `entry` (`StartupEntry`): Значение параметра `entry` (StartupEntry).
- **Какие на выходе**: `StartupEntrySnapshotItem`: Результирующий объект или экземпляр структуры данных.

---

## Проект: StartupMonitor.Service

### Файл: `StartupMonitor.Service\Pipe\EventPipeServer.cs`

#### `EventPipeServer.ExecuteAsync` (строка 53)
- **Название функции**: `ExecuteAsync`
- **Что она делает**: Основной цикл: подписывается на уведомления, слушает входящие соединения от UI-клиентов на событийном канале. Клиенты не отправляют данные — только получают уведомления (PipeDirection.Out).
- **Какие входные данные**: `stoppingToken` (`CancellationToken`): Токен отмены остановки службы
- **Какие на выходе**: `Task`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `EventPipeServer.StartHeartbeat` (строка 117)
- **Название функции**: `StartHeartbeat`
- **Что она делает**: Периодическая проверка живучести клиентов: запись пустого батча мёртвому клиенту бросает IOException — клиент попадает в dead и освобождает слот канала. Без этого отключившиеся клиенты занимают инстансы навсегда, и после 10 подключений сервер перестаёт принимать новых.
- **Какие входные данные**: `stoppingToken` (`CancellationToken`): Значение параметра `stoppingToken` (CancellationToken).
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `EventPipeServer.RemoveClient` (строка 135)
- **Название функции**: `RemoveClient`
- **Что она делает**: Удаляет клиента из словаря и освобождает pipe.
- **Какие входные данные**: `pipe` (`NamedPipeServerStream`): Экземпляр pipe для освобождения; `id` (`int`): Идентификатор клиента
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `EventPipeServer.OnMessagesBatchReady` (строка 147)
- **Название функции**: `OnMessagesBatchReady`
- **Что она делает**: Обработчик события MessagesBatchReady. Сериализует батч в JSON и отправляет всем подключённым клиентам. Мёртвые клиенты удаляются.
- **Какие входные данные**: `sender` (`object?`): Источник события (NotificationService); `batch` (`List<PipeMessage>`): Список сообщений для рассылки
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

---

### Файл: `StartupMonitor.Service\Pipe\PipeSecurityFactory.cs`

#### `PipeSecurityFactory.CreateDefault` (строка 21)
- **Название функции**: `CreateDefault`
- **Что она делает**: Создаёт PipeSecurity с правилами: — SYSTEM: полный доступ — Администраторы: полный доступ — Интерактивные пользователи: чтение/запись
- **Какие входные данные**: Входные данные отсутствуют (параметры не требуются).
- **Какие на выходе**: `PipeSecurity`: Настроенный PipeSecurity

#### `PipeSecurityFactory.SecurityIdentifier` (строка 30)
- **Название функции**: `SecurityIdentifier`
- **Что она делает**: SecurityIdentifier: Выполняет операцию SecurityIdentifier.
- **Какие входные данные**: `WellKnownSidType.LocalSystemSid`: Входные данные: значение параметра WellKnownSidType.LocalSystemSid ().; `null`: Входные данные: значение параметра null ().
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `PipeSecurityFactory.SecurityIdentifier` (строка 38)
- **Название функции**: `SecurityIdentifier`
- **Что она делает**: SecurityIdentifier: Выполняет операцию SecurityIdentifier.
- **Какие входные данные**: `WellKnownSidType.BuiltinAdministratorsSid`: Входные данные: значение параметра WellKnownSidType.BuiltinAdministratorsSid ().; `null`: Входные данные: значение параметра null ().
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `PipeSecurityFactory.SecurityIdentifier` (строка 46)
- **Название функции**: `SecurityIdentifier`
- **Что она делает**: SecurityIdentifier: Выполняет операцию SecurityIdentifier.
- **Какие входные данные**: `WellKnownSidType.InteractiveSid`: Входные данные: значение параметра WellKnownSidType.InteractiveSid ().; `null`: Входные данные: значение параметра null ().
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `PipeSecurityFactory.SecurityIdentifier` (строка 54)
- **Название функции**: `SecurityIdentifier`
- **Что она делает**: SecurityIdentifier: Выполняет операцию SecurityIdentifier.
- **Какие входные данные**: `WellKnownSidType.AuthenticatedUserSid`: Входные данные: значение параметра WellKnownSidType.AuthenticatedUserSid ().; `null`: Входные данные: значение параметра null ().
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

---

### Файл: `StartupMonitor.Service\Pipe\PipeServer.cs`

#### `PipeServer.NotifyExternalEntriesChanged` (строка 73)
- **Название функции**: `NotifyExternalEntriesChanged`
- **Что она делает**: Увеличивает ревизию записей при внешних мутациях (вотчеры, BlacklistManager).
- **Какие входные данные**: `)`: Значение параметра `)`.
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `PipeServer.ExecuteAsync` (строка 152)
- **Название функции**: `ExecuteAsync`
- **Что она делает**: Основной цикл службы: ожидает подключения UI-клиентов по named pipe. Для каждого подключения запускает асинхронную обработку HandleClientAsync.
- **Какие входные данные**: `stoppingToken` (`CancellationToken`): Токен отмены остановки службы
- **Какие на выходе**: `Task`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `PipeServer.HandleClientAsync` (строка 201)
- **Название функции**: `HandleClientAsync`
- **Что она делает**: Обрабатывает одно подключение клиента: читает JSON-строки, десериализует, выполняет команду через ProcessMessage, сериализует ответ и отправляет обратно.
- **Какие входные данные**: `pipe` (`NamedPipeServerStream`): Экземпляр подключённого pipe; `ct` (`CancellationToken`): Токен отмены
- **Какие на выходе**: `Task`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `PipeServer.ProcessMessage` (строка 252)
- **Название функции**: `ProcessMessage`
- **Что она делает**: Диспетчер команд. По полю Type сообщения выбирает ветку обработки: CRUD записей, управление чёрным списком, AI-рейтинги, очистка устаревших и т.д.
- **Какие входные данные**: `message` (`PipeMessage`): Входящее сообщение от UI
- **Какие на выходе**: `Task<PipeMessage>`: Ответное сообщение с результатом выполнения

#### `PipeServer.ProcessActionResultPayload` (строка 869)
- **Название функции**: `ProcessActionResultPayload`
- **Что она делает**: ProcessActionResultPayload: Выполняет операцию ProcessActionResultPayload.
- **Какие входные данные**: `true`: Входные данные: значение параметра true ().; `msg`: Входные данные: значение параметра msg ().; `suspendPayload.ProcessId`: Входные данные: значение параметра suspendPayload.ProcessId ().
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `PipeServer.ProcessActionResultPayload` (строка 880)
- **Название функции**: `ProcessActionResultPayload`
- **Что она делает**: ProcessActionResultPayload: Выполняет операцию ProcessActionResultPayload.
- **Какие входные данные**: `false`: Входные данные: значение параметра false ().; `msg`: Входные данные: значение параметра msg ().; `suspendPayload.ProcessId`: Входные данные: значение параметра suspendPayload.ProcessId ().
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `PipeServer.ProcessActionResultPayload` (строка 900)
- **Название функции**: `ProcessActionResultPayload`
- **Что она делает**: ProcessActionResultPayload: Выполняет операцию ProcessActionResultPayload.
- **Какие входные данные**: `true`: Входные данные: значение параметра true ().; `msg`: Входные данные: значение параметра msg ().; `resumePayload.ProcessId`: Входные данные: значение параметра resumePayload.ProcessId ().
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `PipeServer.ProcessActionResultPayload` (строка 911)
- **Название функции**: `ProcessActionResultPayload`
- **Что она делает**: ProcessActionResultPayload: Выполняет операцию ProcessActionResultPayload.
- **Какие входные данные**: `false`: Входные данные: значение параметра false ().; `msg`: Входные данные: значение параметра msg ().; `resumePayload.ProcessId`: Входные данные: значение параметра resumePayload.ProcessId ().
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `PipeServer.PrefetchAndLogAsync` (строка 1251)
- **Название функции**: `PrefetchAndLogAsync`
- **Что она делает**: Фоновая загрузка AI-рейтингов для всех записей. Логирует количество загруженных.
- **Какие входные данные**: `entries` (`List<StartupEntry>`): Список записей для оценки
- **Какие на выходе**: `Task`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `PipeServer.TriggerCatchUpScan` (строка 1274)
- **Название функции**: `TriggerCatchUpScan`
- **Что она делает**: Догоняющее сканирование после возобновления мониторинга.
- **Какие входные данные**: Входные данные отсутствуют (параметры не требуются).
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `PipeServer.BackgroundRatingLoop` (строка 1295)
- **Название функции**: `BackgroundRatingLoop`
- **Что она делает**: Фоновый цикл AI-оценки записей: работает независимо от UI, пока есть неоценённые записи и задан API-ключ. Первый запуск через 20с после старта, далее каждые 10 минут.
- **Какие входные данные**: `ct` (`CancellationToken`): Токен отмены
- **Какие на выходе**: `Task`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `PipeServer.BackgroundCleanupLoop` (строка 1334)
- **Название функции**: `BackgroundCleanupLoop`
- **Что она делает**: Фоновый цикл очистки устаревших записей. Запускается через 30с после старта, затем повторяется каждые 60с. Удаляет записи, чьи файлы/ключи реестра/службы больше не существуют в системе.
- **Какие входные данные**: `ct` (`CancellationToken`): Токен отмены
- **Какие на выходе**: `Task`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `PipeServer.BackgroundHashScanLoop` (строка 1398)
- **Название функции**: `BackgroundHashScanLoop`
- **Что она делает**: Фоновый цикл проверки хешей (CIRCL hashlookup). Запускается через 10с после старта, затем повторяется каждые 5 минут. Обновляет KasperskyDetection у записей.
- **Какие входные данные**: `ct` (`CancellationToken`): Токен отмены
- **Какие на выходе**: `Task`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `PipeServer.Dispose` (строка 1434)
- **Название функции**: `Dispose`
- **Что она делает**: Освобождение ресурсов: отмена токена, освобождение AI-сервиса.
- **Какие входные данные**: Входные данные отсутствуют (параметры не требуются).
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `PipeServer.ReapplyBlacklist` (строка 1446)
- **Название функции**: `ReapplyBlacklist`
- **Что она делает**: Применяет правило чёрного списка ко всем существующим записям. Записи, подходящие под правило, блокируются (ApplyBlacklistAction).
- **Какие входные данные**: `rule` (`BlacklistRule`): Правило чёрного списка
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

---

### Файл: `StartupMonitor.Service\Services\AppxStartupTaskHelper.cs`

#### `AppxStartupTaskHelper.ReadState` (строка 19)
- **Название функции**: `ReadState`
- **Что она делает**: Читает состояние StartupTask. Возвращает true, если задача включена. Если ключ отсутствует — слушаем манифест (Enabled="true" означает включено). Считываем состояние текущей сессии (HKCU); для LocalSystem HKCU=SYSTEM, поэтому смотрим ещё и все пользовательские кусты и берём "наиболее включённое" из найденных.
- **Какие входные данные**: `pfn` (`string`): Значение параметра `pfn` (string).; `taskId` (`string`): Значение параметра `taskId` (string).
- **Какие на выходе**: `bool`: Логический результат выполнения (`true` в случае успеха или соответствия условию, иначе `false`).

#### `AppxStartupTaskHelper.WriteState` (строка 47)
- **Название функции**: `WriteState`
- **Что она делает**: Устанавливает состояние StartupTask. enabled=true пишет State=2, enabled=false — State=1 (отключено пользователем). Применяется к текущему пользователю (HKCU) и всем пользовательским кустам.
- **Какие входные данные**: `pfn` (`string`): Значение параметра `pfn` (string).; `taskId` (`string`): Значение параметра `taskId` (string).; `enabled` (`bool`): Значение параметра `enabled` (bool).
- **Какие на выходе**: `bool`: Логический результат выполнения (`true` в случае успеха или соответствия условию, иначе `false`).

---

### Файл: `StartupMonitor.Service\Services\BlacklistManager.cs`

#### `BlacklistManager.BlacklistManager` (строка 16)
- **Название функции**: `BlacklistManager`
- **Что она делает**: BlacklistManager: Выполняет операцию BlacklistManager.
- **Какие входные данные**: `store` (`JsonDataStore`): Входные данные: значение параметра store (JsonDataStore).
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `BlacklistManager.MatchEntry` (строка 22)
- **Название функции**: `MatchEntry`
- **Что она делает**: Ищет первое подходящее правило ЧС для указанной записи.
- **Какие входные данные**: `entry` (`StartupEntry`): Значение параметра `entry` (StartupEntry).
- **Какие на выходе**: `BlacklistRule?`: Результирующий объект или экземпляр структуры данных.

#### `BlacklistManager.ApplyBlacklistAction` (строка 36)
- **Название функции**: `ApplyBlacklistAction`
- **Что она делает**: Применяет действие правила ЧС к записи: блокировка + удаление или только уведомление.
- **Какие входные данные**: `entry` (`StartupEntry`): Значение параметра `entry` (StartupEntry).; `rule` (`BlacklistRule?`): Значение параметра `rule` (BlacklistRule?).; `store` (`JsonDataStore`): Значение параметра `store` (JsonDataStore).; `notifications` (`NotificationService`): Значение параметра `notifications` (NotificationService).
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `BlacklistManager.IsMatchStatic` (строка 90)
- **Название функции**: `IsMatchStatic`
- **Что она делает**: Проверяет, подходит ли запись под правило ЧС по типу совпадения (путь/маска/издатель/SHA256).
- **Какие входные данные**: `rule` (`BlacklistRule`): Значение параметра `rule` (BlacklistRule).; `entry` (`StartupEntry`): Значение параметра `entry` (StartupEntry).
- **Какие на выходе**: `bool`: Логический результат выполнения (`true` в случае успеха или соответствия условию, иначе `false`).

#### `BlacklistManager.MatchesFilePath` (строка 138)
- **Название функции**: `MatchesFilePath`
- **Что она делает**: Проверяет, подходит ли путь файла под правило ЧС (точный путь/маска). Отдельно от IsMatchStatic — для сопоставления процессов с ЧС вне списка записей.
- **Какие входные данные**: `rule` (`BlacklistRule`): Значение параметра `rule` (BlacklistRule).; `filePath` (`string`): Значение параметра `filePath` (string).
- **Какие на выходе**: `bool`: Логический результат выполнения (`true` в случае успеха или соответствия условию, иначе `false`).

#### `BlacklistManager.TryWildcardMatch` (строка 154)
- **Название функции**: `TryWildcardMatch`
- **Что она делает**: Безопасное сопоставление с wildcard-шаблоном с защитой от ReDoS и некорректных масок.
- **Какие входные данные**: `input` (`string`): Значение параметра `input` (string).; `pattern` (`string`): Значение параметра `pattern` (string).
- **Какие на выходе**: `bool`: Логический результат выполнения (`true` в случае успеха или соответствия условию, иначе `false`).

#### `BlacklistManager.WildcardToRegex` (строка 167)
- **Название функции**: `WildcardToRegex`
- **Что она делает**: Преобразует wildcard-шаблон (*, ?) в регулярное выражение.
- **Какие входные данные**: `pattern` (`string`): Значение параметра `pattern` (string).
- **Какие на выходе**: `string`: Текстовая строка с результатом операции или сообщением.

#### `BlacklistManager.ComputeSha256` (строка 176)
- **Название функции**: `ComputeSha256`
- **Что она делает**: Вычисляет SHA256 хеш файла (для правил типа SHA256).
- **Какие входные данные**: `filePath` (`string`): Значение параметра `filePath` (string).
- **Какие на выходе**: `string`: Текстовая строка с результатом операции или сообщением.

---

### Файл: `StartupMonitor.Service\Services\EntryOperations.cs`

#### `EntryOperations.RegistryPayload` (строка 31)
- **Название функции**: `RegistryPayload`
- **Что она делает**: DTO для сохранения/восстановления значения реестра (ключ, имя значения, данные).
- **Какие входные данные**: `Key` (`string`): Значение параметра `Key` (string).; `ValueName` (`string`): Значение параметра `ValueName` (string).; `ValueData` (`string`): Значение параметра `ValueData` (string).
- **Какие на выходе**: `record`: Результирующий объект или экземпляр структуры данных.

#### `EntryOperations.GetRegistryKeyPath` (строка 34)
- **Название функции**: `GetRegistryKeyPath`
- **Что она делает**: Возвращает путь в реестре для указанного типа расположения.
- **Какие входные данные**: `loc` (`EntryLocation`): Значение параметра `loc` (EntryLocation).
- **Какие на выходе**: `string`: Текстовая строка с результатом операции или сообщением.

#### `EntryOperations.GetHive` (строка 54)
- **Название функции**: `GetHive`
- **Что она делает**: Определяет куст реестра (HKCU/HKCR/HKLM) по типу расположения.
- **Какие входные данные**: `loc` (`EntryLocation`): Значение параметра `loc` (EntryLocation).
- **Какие на выходе**: `RegistryHive`: Результирующий объект или экземпляр структуры данных.

#### `EntryOperations.GetHiveName` (строка 67)
- **Название функции**: `GetHiveName`
- **Что она делает**: Возвращает строковое имя куста реестра для отображения.
- **Какие входные данные**: `hive` (`RegistryHive`): Значение параметра `hive` (RegistryHive).
- **Какие на выходе**: `string`: Текстовая строка с результатом операции или сообщением.

#### `EntryOperations.BuildRegistryPayload` (строка 76)
- **Название функции**: `BuildRegistryPayload`
- **Что она делает**: Формирует строку пути в реестре для указанного типа расположения (для DisablePayload).
- **Какие входные данные**: `loc` (`EntryLocation`): Значение параметра `loc` (EntryLocation).
- **Какие на выходе**: `string`: Текстовая строка с результатом операции или сообщением.

#### `EntryOperations.static` (строка 85)
- **Название функции**: `static`
- **Что она делает**: Открывает раздел реестра для указанной записи. Возвращает (успех, ошибка). Для записей пользовательских кустов (LocationDetail вида "HKCU\Имя\...") открывает HKEY_USERS\&lt;SID&gt;, т.к. служба работает как LocalSystem и её HKCU — профиль SYSTEM.
- **Какие входные данные**: `bool`: Значение параметра `bool`.; `entry` (`string?) OpenRegistry(StartupEntry`): Значение параметра `entry` (string?) OpenRegistry(StartupEntry).; `writable` (`bool`): Значение параметра `writable` (bool).; `key` (`out RegistryKey?`): Значение параметра `key` (out RegistryKey?).; `subKey` (`string?`): Значение параметра `subKey` (string?).
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `EntryOperations.BuildPayloadPath` (строка 122)
- **Название функции**: `BuildPayloadPath`
- **Что она делает**: Формирует полный путь ключа для payload (с учётом пользовательского куста).
- **Какие входные данные**: `entry` (`StartupEntry`): Значение параметра `entry` (StartupEntry).; `subKey` (`string?`): Значение параметра `subKey` (string?).
- **Какие на выходе**: `string`: Текстовая строка с результатом операции или сообщением.

#### `EntryOperations.static` (строка 139)
- **Название функции**: `static`
- **Что она делает**: Открывает ключ реестра из сохранённого payload (с поддержкой HKEY_USERS).
- **Какие входные данные**: `bool`: Значение параметра `bool`.; `payloadKey` (`string?) OpenPayloadKey(string`): Значение параметра `payloadKey` (string?) OpenPayloadKey(string).; `writable` (`bool`): Значение параметра `writable` (bool).; `key` (`out RegistryKey?`): Значение параметра `key` (out RegistryKey?).
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `EntryOperations.static` (строка 174)
- **Название функции**: `static`
- **Что она делает**: Разбивает имя записи на путь подключа и имя значения (для subkey-записей: IFEO "app.exe\Debugger", ContextMenuHandlers и т.п.).
- **Какие входные данные**: `subKey` (`string?`): Значение параметра `subKey` (string?).; `name` (`string valueName) SplitSubKeyName(string`): Значение параметра `name` (string valueName) SplitSubKeyName(string).
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `EntryOperations.static` (строка 181)
- **Название функции**: `static`
- **Что она делает**: Отключает запись в реестре: удаляет значение, сохраняет бэкап в entry.DisablePayload
- **Какие входные данные**: `success` (`bool`): Значение параметра `success` (bool).; `entry` (`string? error) DisableRegistry(StartupEntry`): Значение параметра `entry` (string? error) DisableRegistry(StartupEntry).
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `EntryOperations.RegistryPayload` (строка 198)
- **Название функции**: `RegistryPayload`
- **Что она делает**: RegistryPayload: Выполняет операцию RegistryPayload.
- **Какие входные данные**: `fullPath`: Входные данные: значение параметра fullPath ().; `valueName`: Входные данные: значение параметра valueName ().; `existing`: Входные данные: значение параметра existing ().
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `EntryOperations.static` (строка 205)
- **Название функции**: `static`
- **Что она делает**: Включает запись в реестре: восстанавливает значение из DisablePayload
- **Какие входные данные**: `success` (`bool`): Значение параметра `success` (bool).; `entry` (`string? error) EnableRegistry(StartupEntry`): Значение параметра `entry` (string? error) EnableRegistry(StartupEntry).
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `EntryOperations.static` (строка 231)
- **Название функции**: `static`
- **Что она делает**: Удаляет запись из реестра навсегда, возвращает payload для возможного восстановления.
- **Какие входные данные**: `success` (`bool`): Значение параметра `success` (bool).; `entry` (`string? error) RemoveRegistry(StartupEntry`): Значение параметра `entry` (string? error) RemoveRegistry(StartupEntry).; `payload` (`out string`): Значение параметра `payload` (out string).
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `EntryOperations.static` (строка 259)
- **Название функции**: `static`
- **Что она делает**: Восстанавливает удалённую запись реестра из payload.
- **Какие входные данные**: `success` (`bool`): Значение параметра `success` (bool).; `payloadJson` (`string? error) RestoreRegistry(string`): Значение параметра `payloadJson` (string? error) RestoreRegistry(string).
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `EntryOperations.FilePayload` (строка 283)
- **Название функции**: `FilePayload`
- **Что она делает**: DTO для бэкапа файла: путь к бэкапу и оригинальный путь.
- **Какие входные данные**: `BackupPath` (`string`): Значение параметра `BackupPath` (string).; `OriginalPath` (`string`): Значение параметра `OriginalPath` (string).
- **Какие на выходе**: `record`: Результирующий объект или экземпляр структуры данных.

#### `EntryOperations.static` (строка 286)
- **Название функции**: `static`
- **Что она делает**: Отключает файл в папке автозапуска: переименовывает в .disabled
- **Какие входные данные**: `success` (`bool`): Значение параметра `success` (bool).; `entry` (`string? error) DisableStartupFile(StartupEntry`): Значение параметра `entry` (string? error) DisableStartupFile(StartupEntry).
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `EntryOperations.static` (строка 310)
- **Название функции**: `static`
- **Что она делает**: Включает файл в папке автозапуска: переименовывает обратно из .disabled
- **Какие входные данные**: `success` (`bool`): Значение параметра `success` (bool).; `entry` (`string? error) EnableStartupFile(StartupEntry`): Значение параметра `entry` (string? error) EnableStartupFile(StartupEntry).
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `EntryOperations.static` (строка 334)
- **Название функции**: `static`
- **Что она делает**: Удаляет файл из папки автозапуска с бэкапом в BackupDir.
- **Какие входные данные**: `success` (`bool`): Значение параметра `success` (bool).; `entry` (`string? error) RemoveStartupFile(StartupEntry`): Значение параметра `entry` (string? error) RemoveStartupFile(StartupEntry).; `payload` (`out string`): Значение параметра `payload` (out string).
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `EntryOperations.static` (строка 368)
- **Название функции**: `static`
- **Что она делает**: Восстанавливает файл автозапуска из бэкапа.
- **Какие входные данные**: `success` (`bool`): Значение параметра `success` (bool).; `payloadJson` (`string? error) RestoreFile(string`): Значение параметра `payloadJson` (string? error) RestoreFile(string).
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `EntryOperations.ServicePayload` (строка 393)
- **Название функции**: `ServicePayload`
- **Что она делает**: DTO для сохранения/восстановления службы: имя, путь к бинарнику, тип запуска.
- **Какие входные данные**: `ServiceName` (`string`): Значение параметра `ServiceName` (string).; `BinaryPath` (`string`): Значение параметра `BinaryPath` (string).; `StartType` (`string`): Значение параметра `StartType` (string).; `DisplayName` (`string?`): Значение параметра `DisplayName` (string?).
- **Какие на выходе**: `record`: Результирующий объект или экземпляр структуры данных.

#### `EntryOperations.static` (строка 396)
- **Название функции**: `static`
- **Что она делает**: Запускает sc.exe с указанными аргументами и возвращает exit code + вывод.
- **Какие входные данные**: `exitCode` (`int`): Значение параметра `exitCode` (int).; `args` (`string output) RunSc(string`): Значение параметра `args` (string output) RunSc(string).; `timeoutMs` (`int`): Значение параметра `timeoutMs` (int).
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `EntryOperations.IsValidServiceName` (строка 418)
- **Название функции**: `IsValidServiceName`
- **Что она делает**: Имя службы приходит из реестра/WMI — отсекаем инъекцию аргументов sc.
- **Какие входные данные**: `name` (`string`): Значение параметра `name` (string).
- **Какие на выходе**: `bool`: Логический результат выполнения (`true` в случае успеха или соответствия условию, иначе `false`).

#### `EntryOperations.GetServiceStartType` (строка 422)
- **Название функции**: `GetServiceStartType`
- **Что она делает**: Текущий тип запуска службы из `sc qc` (для бэкапа перед удалением).
- **Какие входные данные**: `svcName` (`string`): Значение параметра `svcName` (string).
- **Какие на выходе**: `string`: Текстовая строка с результатом операции или сообщением.

#### `EntryOperations.static` (строка 434)
- **Название функции**: `static`
- **Что она делает**: Отключает службу: sc stop + sc config start=disabled
- **Какие входные данные**: `success` (`bool`): Значение параметра `success` (bool).; `entry` (`string? error) DisableService(StartupEntry`): Значение параметра `entry` (string? error) DisableService(StartupEntry).
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `EntryOperations.static` (строка 458)
- **Название функции**: `static`
- **Что она делает**: Включает службу: sc config start=auto + sc start
- **Какие входные данные**: `success` (`bool`): Значение параметра `success` (bool).; `entry` (`string? error) EnableService(StartupEntry`): Значение параметра `entry` (string? error) EnableService(StartupEntry).
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `EntryOperations.static` (строка 478)
- **Название функции**: `static`
- **Что она делает**: Удаляет службу через sc delete, сохраняя payload для восстановления.
- **Какие входные данные**: `success` (`bool`): Значение параметра `success` (bool).; `entry` (`string? error) RemoveService(StartupEntry`): Значение параметра `entry` (string? error) RemoveService(StartupEntry).; `payload` (`out string`): Значение параметра `payload` (out string).
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `EntryOperations.static` (строка 517)
- **Название функции**: `static`
- **Что она делает**: Восстанавливает удалённую службу через sc create.
- **Какие входные данные**: `success` (`bool`): Значение параметра `success` (bool).; `payloadJson` (`string? error) RestoreService(string`): Значение параметра `payloadJson` (string? error) RestoreService(string).
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `EntryOperations.TaskPayload` (строка 542)
- **Название функции**: `TaskPayload`
- **Что она делает**: DTO для сохранения/восстановления задачи планировщика: XML и путь.
- **Какие входные данные**: `TaskXml` (`string`): Значение параметра `TaskXml` (string).; `TaskPath` (`string`): Значение параметра `TaskPath` (string).
- **Какие на выходе**: `record`: Результирующий объект или экземпляр структуры данных.

#### `EntryOperations.static` (строка 545)
- **Название функции**: `static`
- **Что она делает**: Отключает задачу в планировщике: task.Enabled = false
- **Какие входные данные**: `success` (`bool`): Значение параметра `success` (bool).; `entry` (`string? error) DisableTask(StartupEntry`): Значение параметра `entry` (string? error) DisableTask(StartupEntry).
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `EntryOperations.static` (строка 563)
- **Название функции**: `static`
- **Что она делает**: Включает задачу в планировщике: task.Enabled = true
- **Какие входные данные**: `success` (`bool`): Значение параметра `success` (bool).; `entry` (`string? error) EnableTask(StartupEntry`): Значение параметра `entry` (string? error) EnableTask(StartupEntry).
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `EntryOperations.static` (строка 581)
- **Название функции**: `static`
- **Что она делает**: Удаляет задачу из планировщика с сохранением XML для восстановления.
- **Какие входные данные**: `success` (`bool`): Значение параметра `success` (bool).; `entry` (`string? error) RemoveTask(StartupEntry`): Значение параметра `entry` (string? error) RemoveTask(StartupEntry).; `payload` (`out string`): Значение параметра `payload` (out string).
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `EntryOperations.static` (строка 611)
- **Название функции**: `static`
- **Что она делает**: Восстанавливает задачу в планировщике из сохранённого XML.
- **Какие входные данные**: `success` (`bool`): Значение параметра `success` (bool).; `payloadJson` (`string? error) RestoreTask(string`): Значение параметра `payloadJson` (string? error) RestoreTask(string).
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `EntryOperations.IsProtectedProcessName` (строка 632)
- **Название функции**: `IsProtectedProcessName`
- **Что она делает**: Проверяет, защищено ли имя процесса от завершения (системные и собственные процессы).
- **Какие входные данные**: `fileName` (`string`): Значение параметра `fileName` (string).
- **Какие на выходе**: `bool`: Логический результат выполнения (`true` в случае успеха или соответствия условию, иначе `false`).

#### `EntryOperations.KillProcessByImage` (строка 637)
- **Название функции**: `KillProcessByImage`
- **Что она делает**: Завершает процесс по имени файла. Если передан полный путь — завершаются только процессы, чей образ совпадает с этим путём.
- **Какие входные данные**: `fileName` (`string`): Значение параметра `fileName` (string).
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `EntryOperations.StopService` (строка 669)
- **Название функции**: `StopService`
- **Что она делает**: Завершает службу по имени.
- **Какие входные данные**: `serviceName` (`string`): Значение параметра `serviceName` (string).
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `EntryOperations.KillProcess` (строка 681)
- **Название функции**: `KillProcess`
- **Что она делает**: Завершает процесс/службу по записи автозапуска.
- **Какие входные данные**: `entry` (`StartupEntry`): Значение параметра `entry` (StartupEntry).
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `EntryOperations.TerminateAllBlocked` (строка 708)
- **Название функции**: `TerminateAllBlocked`
- **Что она делает**: При старте службы завершает процессы заблокированных записей (Status == Blocked && FromBlacklist).
- **Какие входные данные**: `entries` (`List<StartupEntry>`): Значение параметра `entries` (List<StartupEntry>).
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `EntryOperations.KillProcessesByRules` (строка 720)
- **Название функции**: `KillProcessesByRules`
- **Что она делает**: Завершает процессы, чей путь к образу совпадает с правилом ЧС (Path/PathWildcard). Срабатывает даже если записи в списке нет — проблема ms-teams, когда правило в ЧС есть, а запись удалена из системы.
- **Какие входные данные**: `rules` (`IEnumerable<BlacklistRule>`): Значение параметра `rules` (IEnumerable<BlacklistRule>).; `logger` (`Microsoft.Extensions.Logging.ILogger?`): Значение параметра `logger` (Microsoft.Extensions.Logging.ILogger?).
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `EntryOperations.BuildEnrichCacheKey` (строка 821)
- **Название функции**: `BuildEnrichCacheKey`
- **Что она делает**: Строит ключ кэша обогащения из идентификатора и исходных путей записи.
- **Какие входные данные**: `entry` (`StartupEntry`): Значение параметра `entry` (StartupEntry).
- **Какие на выходе**: `string`: Текстовая строка с результатом операции или сообщением.

#### `EntryOperations.CloneThreatInfo` (строка 825)
- **Название функции**: `CloneThreatInfo`
- **Что она делает**: Глубоко клонирует <see cref="CliThreatInfo"/> (защита от разделяемого мутабельного списка).
- **Какие входные данные**: `src` (`CliThreatInfo?`): Значение параметра `src` (CliThreatInfo?).
- **Какие на выходе**: `CliThreatInfo?`: Результирующий объект или экземпляр структуры данных.

#### `EntryOperations.AddToEnrichCache` (строка 836)
- **Название функции**: `AddToEnrichCache`
- **Что она делает**: Сохраняет значение в кэш с eviction самых старых записей при переполнении.
- **Какие входные данные**: `key` (`string`): Значение параметра `key` (string).; `value` (`EnrichCacheValue`): Значение параметра `value` (EnrichCacheValue).
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `EntryOperations.ResolveAndEnrichEntry` (строка 857)
- **Название функции**: `ResolveAndEnrichEntry`
- **Что она делает**: Находит исполняемый файл, вычисляет SHA256 и определяет издателя. computeHash=false — для массовых verify-сканов (хеш не нужен для сопоставления).
- **Какие входные данные**: `entry` (`StartupEntry`): Значение параметра `entry` (StartupEntry).; `computeHash` (`bool`): Значение параметра `computeHash` (bool).
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `EntryOperations.ResolveExecutablePath` (строка 1031)
- **Название функции**: `ResolveExecutablePath`
- **Что она делает**: Пытается найти исполняемый путь из FilePath, CommandLine или CLSID.
- **Какие входные данные**: `entry` (`StartupEntry`): Значение параметра `entry` (StartupEntry).
- **Какие на выходе**: `string`: Текстовая строка с результатом операции или сообщением.

#### `EntryOperations.ExpandPath` (строка 1066)
- **Название функции**: `ExpandPath`
- **Что она делает**: Разворачивает путь: убирает префикс \??\ и раскрывает %variables%.
- **Какие входные данные**: `path` (`string`): Значение параметра `path` (string).
- **Какие на выходе**: `string`: Текстовая строка с результатом операции или сообщением.

#### `EntryOperations.ComputeSha256` (строка 1086)
- **Название функции**: `ComputeSha256`
- **Что она делает**: Вычисляет SHA256 хеш файла в нижнем регистре.
- **Какие входные данные**: `filePath` (`string`): Значение параметра `filePath` (string).
- **Какие на выходе**: `string`: Текстовая строка с результатом операции или сообщением.

#### `EntryOperations.IsSelfServiceName` (строка 1205)
- **Название функции**: `IsSelfServiceName`
- **Что она делает**: Проверяет, является ли имя службы нашей собственной службой StartupMonitor.
- **Какие входные данные**: `serviceName` (`string?`): Значение параметра `serviceName` (string?).
- **Какие на выходе**: `bool`: Логический результат выполнения (`true` в случае успеха или соответствия условию, иначе `false`).

#### `EntryOperations.IsSelfEntry` (строка 1214)
- **Название функции**: `IsSelfEntry`
- **Что она делает**: Проверяет, относится ли запись к собственным компонентам StartupMonitor (служба, UI, задачи, ключи реестра).
- **Какие входные данные**: `entry` (`StartupEntry?`): Значение параметра `entry` (StartupEntry?).
- **Какие на выходе**: `bool`: Логический результат выполнения (`true` в случае успеха или соответствия условию, иначе `false`).

#### `EntryOperations.IsProtectedSystemEntry` (строка 1266)
- **Название функции**: `IsProtectedSystemEntry`
- **Что она делает**: Проверяет, является ли запись критически важной системной службой или компонентом ОС Windows, которые недопустимо удалять или глушить в автоматическом режиме Lockdown.
- **Какие входные данные**: `entry` (`StartupEntry?`): Значение параметра `entry` (StartupEntry?).
- **Какие на выходе**: `bool`: Логический результат выполнения (`true` в случае успеха или соответствия условию, иначе `false`).

#### `EntryOperations.IsOwnService` (строка 1270)
- **Название функции**: `IsOwnService`
- **Что она делает**: Проверяет, является ли запись нашим собственным сервисом StartupMonitor.Service.exe (для обратной совместимости).
- **Какие входные данные**: `entry)` (`StartupEntry`): Значение параметра `entry)` (StartupEntry).
- **Какие на выходе**: `bool`: Логический результат выполнения (`true` в случае успеха или соответствия условию, иначе `false`).

#### `EntryOperations.static` (строка 1275)
- **Название функции**: `static`
- **Что она делает**: Удаляет запись из системы (реестр/файл/службу/задачу) в зависимости от Location.
- **Какие входные данные**: `success` (`bool`): Значение параметра `success` (bool).; `error` (`string?`): Значение параметра `error` (string?).; `entry` (`string? payload) RemoveFromSystem(StartupEntry`): Значение параметра `entry` (string? payload) RemoveFromSystem(StartupEntry).
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `EntryOperations.static` (строка 1337)
- **Название функции**: `static`
- **Что она делает**: Восстанавливает удалённую запись в системе по сохранённому payload.
- **Какие входные данные**: `success` (`bool`): Значение параметра `success` (bool).; `entry` (`string? error) RestoreToSystem(RemovedEntry`): Значение параметра `entry` (string? error) RestoreToSystem(RemovedEntry).
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `EntryOperations.static` (строка 1366)
- **Название функции**: `static`
- **Что она делает**: Отключает запись в системе (без удаления, с возможностью включить обратно).
- **Какие входные данные**: `success` (`bool`): Значение параметра `success` (bool).; `entry` (`string? error) DisableFromSystem(StartupEntry`): Значение параметра `entry` (string? error) DisableFromSystem(StartupEntry).
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `EntryOperations.static` (строка 1415)
- **Название функции**: `static`
- **Что она делает**: Включает запись в системе (восстанавливает из отключённого состояния).
- **Какие входные данные**: `success` (`bool`): Значение параметра `success` (bool).; `entry` (`string? error) EnableFromSystem(StartupEntry`): Значение параметра `entry` (string? error) EnableFromSystem(StartupEntry).
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `EntryOperations.static` (строка 1448)
- **Название функции**: `static`
- **Что она делает**: Включает AppX StartupTask (State=2 в SystemAppData).
- **Какие входные данные**: `bool`: Значение параметра `bool`.; `entry` (`string?) EnableAppxTask(StartupEntry`): Значение параметра `entry` (string?) EnableAppxTask(StartupEntry).
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `EntryOperations.ParseAppxPfn` (строка 1460)
- **Название функции**: `ParseAppxPfn`
- **Что она делает**: Извлекает PFN из LocationDetail записи AppX (формат "TaskId (PFN)").
- **Какие входные данные**: `entry` (`StartupEntry`): Значение параметра `entry` (StartupEntry).
- **Какие на выходе**: `string`: Текстовая строка с результатом операции или сообщением.

#### `EntryOperations.ParseAppxTaskId` (строка 1470)
- **Название функции**: `ParseAppxTaskId`
- **Что она делает**: Извлекает TaskId из LocationDetail записи AppX (формат "TaskId (PFN)").
- **Какие входные данные**: `entry` (`StartupEntry`): Значение параметра `entry` (StartupEntry).
- **Какие на выходе**: `string`: Текстовая строка с результатом операции или сообщением.

#### `EntryOperations.NtSuspendProcess` (строка 1485)
- **Название функции**: `NtSuspendProcess`
- **Что она делает**: Выполняет операцию NtSuspendProcess в рамках подсистемы.
- **Какие входные данные**: `processHandle` (`IntPtr`): Входные данные: значение параметра processHandle (IntPtr).
- **Какие на выходе**: `extern int`: Выходные данные: результирующее значение типа extern int.

#### `EntryOperations.NtResumeProcess` (строка 1491)
- **Название функции**: `NtResumeProcess`
- **Что она делает**: Выполняет операцию NtResumeProcess в рамках подсистемы.
- **Какие входные данные**: `processHandle` (`IntPtr`): Входные данные: значение параметра processHandle (IntPtr).
- **Какие на выходе**: `extern int`: Выходные данные: результирующее значение типа extern int.

#### `EntryOperations.OpenProcess` (строка 1499)
- **Название функции**: `OpenProcess`
- **Что она делает**: Выполняет операцию OpenProcess в рамках подсистемы.
- **Какие входные данные**: `processAccess` (`uint`): Входные данные: значение параметра processAccess (uint).; `bInheritHandle` (`bool`): Входные данные: значение параметра bInheritHandle (bool).; `processId` (`int`): Входные данные: значение параметра processId (int).
- **Какие на выходе**: `extern IntPtr`: Выходные данные: результирующее значение типа extern IntPtr.

#### `EntryOperations.CloseHandle` (строка 1506)
- **Название функции**: `CloseHandle`
- **Что она делает**: Выполняет операцию CloseHandle в рамках подсистемы.
- **Какие входные данные**: `hObject` (`IntPtr`): Входные данные: значение параметра hObject (IntPtr).
- **Какие на выходе**: `extern bool`: Выходные данные: результирующее значение типа extern bool.

#### `EntryOperations.IsProcessSuspended` (строка 1511)
- **Название функции**: `IsProcessSuspended`
- **Что она делает**: Проверяет, был ли процесс приостановлен службой.
- **Какие входные данные**: `pid)` (`int`): Значение параметра `pid)` (int).
- **Какие на выходе**: `bool`: Логический результат выполнения (`true` в случае успеха или соответствия условию, иначе `false`).

#### `EntryOperations.GetSuspendedPids` (строка 1514)
- **Название функции**: `GetSuspendedPids`
- **Что она делает**: Возвращает список всех известных приостановленных PID.
- **Какие входные данные**: Входные данные отсутствуют (параметры не требуются).
- **Какие на выходе**: `IEnumerable<int>`: Числовой результат, количество обработанных элементов или метрика.

#### `EntryOperations.TrackSuspendedPid` (строка 1517)
- **Название функции**: `TrackSuspendedPid`
- **Что она делает**: Устанавливает или снимает отметку о приостановке процесса.
- **Какие входные данные**: `pid` (`int`): Значение параметра `pid` (int).; `suspended` (`bool`): Значение параметра `suspended` (bool).
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `EntryOperations.static` (строка 1526)
- **Название функции**: `static`
- **Что она делает**: Приостанавливает выполнение процесса по PID через NtSuspendProcess.
- **Какие входные данные**: `success` (`bool`): Значение параметра `success` (bool).; `processId` (`string message) SuspendProcess(int`): Значение параметра `processId` (string message) SuspendProcess(int).; `processName` (`string?`): Значение параметра `processName` (string?).
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `EntryOperations.static` (строка 1571)
- **Название функции**: `static`
- **Что она делает**: Возобновляет выполнение процесса по PID через NtResumeProcess.
- **Какие входные данные**: `success` (`bool`): Значение параметра `success` (bool).; `processId` (`string message) ResumeProcess(int`): Значение параметра `processId` (string message) ResumeProcess(int).; `processName` (`string?`): Значение параметра `processName` (string?).
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

---

### Файл: `StartupMonitor.Service\Services\InitialScanTracker.cs`

#### `InitialScanTracker.InitialScanTracker` (строка 13)
- **Название функции**: `InitialScanTracker`
- **Что она делает**: Выполняет инициализацию внутренних структур и компонентов.
- **Какие входные данные**: `watchersCount` (`int`): Сколько watcher'ов должны отчитаться через MarkCompleted.
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `InitialScanTracker.MarkCompleted` (строка 25)
- **Название функции**: `MarkCompleted`
- **Что она делает**: Вызывается watcher'ом при завершении его этапа сканирования. Когда все отчитались — сигнализируем ожидающим.
- **Какие входные данные**: Входные данные отсутствуют (параметры не требуются).
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `InitialScanTracker.WaitAsync` (строка 33)
- **Название функции**: `WaitAsync`
- **Что она делает**: Ожидает завершения всех watcher'ов.
- **Какие входные данные**: Входные данные отсутствуют (параметры не требуются).
- **Какие на выходе**: `Task`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

---

### Файл: `StartupMonitor.Service\Services\MonitoringStateCoordinator.cs`

#### `MonitoringStateCoordinator.SetPaused` (строка 42)
- **Название функции**: `SetPaused`
- **Что она делает**: Устанавливает глобальное состояние паузы или возобновления мониторинга.
- **Какие входные данные**: `paused` (`bool`): Значение параметра `paused` (bool).
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

---

### Файл: `StartupMonitor.Service\Services\NotificationService.cs`

#### `NotificationService.NotificationService` (строка 33)
- **Название функции**: `NotificationService`
- **Что она делает**: NotificationService: Выполняет операцию NotificationService.
- **Какие входные данные**: `tracker` (`InitialScanTracker`): Входные данные: значение параметра tracker (InitialScanTracker).; `remoteNotifications` (`RemoteNotificationService?`): Входные данные: значение параметра remoteNotifications (RemoteNotificationService?).
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `NotificationService.Enqueue` (строка 41)
- **Название функции**: `Enqueue`
- **Что она делает**: Добавляет сообщение в очередь. EntryAdded подавляются до завершения скана.
- **Какие входные данные**: `message` (`PipeMessage`): Значение параметра `message` (PipeMessage).
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `NotificationService.DispatchRemoteNotification` (строка 57)
- **Название функции**: `DispatchRemoteNotification`
- **Что она делает**: DispatchRemoteNotification: Выполняет операцию DispatchRemoteNotification.
- **Какие входные данные**: `message` (`PipeMessage`): Входные данные: значение параметра message (PipeMessage).
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `NotificationService.FlushBatch` (строка 96)
- **Название функции**: `FlushBatch`
- **Что она делает**: Сбрасывает накопленные сообщения одним батчем.
- **Какие входные данные**: `state` (`object?`): Значение параметра `state` (object?).
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `NotificationService.Dispose` (строка 116)
- **Название функции**: `Dispose`
- **Что она делает**: Dispose: Освобождает занятые ресурсы объекта.
- **Какие входные данные**: Входные данные отсутствуют (параметры не требуются).
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

---

### Файл: `StartupMonitor.Service\Services\OtherStartupLocationsWatcher.cs`

#### `OtherStartupLocationsWatcher.readonly` (строка 31)
- **Название функции**: `readonly`
- **Что она делает**: Список точек реестра для сканирования и наблюдения.
- **Какие входные данные**: `Path` (`string`): Значение параметра `Path` (string).; `Location` (`EntryLocation`): Значение параметра `Location` (EntryLocation).
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `OtherStartupLocationsWatcher.ExecuteAsync` (строка 61)
- **Название функции**: `ExecuteAsync`
- **Что она делает**: Основной цикл: начальное сканирование → наблюдение.
- **Какие входные данные**: `stoppingToken` (`CancellationToken`): Значение параметра `stoppingToken` (CancellationToken).
- **Какие на выходе**: `Task`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `OtherStartupLocationsWatcher.InitialScan` (строка 72)
- **Название функции**: `InitialScan`
- **Что она делает**: Начальное сканирование всех указанных точек реестра (HKLM + HKCU).
- **Какие входные данные**: `ct` (`CancellationToken`): Значение параметра `ct` (CancellationToken).
- **Какие на выходе**: `Task`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `OtherStartupLocationsWatcher.ScanAppx` (строка 92)
- **Название функции**: `ScanAppx`
- **Что она делает**: Сканирует StartupTask из манифестов AppX-пакетов (UWP autostart). Состояние хранится в HKCU\...\AppModel\SystemAppData — читаем его, чтобы отметить Enabled/Disabled.
- **Какие входные данные**: `isInitial` (`bool`): Значение параметра `isInitial` (bool).
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `OtherStartupLocationsWatcher.ScanHive` (строка 139)
- **Название функции**: `ScanHive`
- **Что она делает**: Сканирует все точки автозапуска в указанном кусте реестра.
- **Какие входные данные**: `hive` (`RegistryHive`): Значение параметра `hive` (RegistryHive).; `isInitial` (`bool`): Значение параметра `isInitial` (bool).
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `OtherStartupLocationsWatcher.ScanKey` (строка 159)
- **Название функции**: `ScanKey`
- **Что она делает**: Сканирует значения и подразделы указанного ключа реестра.
- **Какие входные данные**: `key` (`RegistryKey`): Значение параметра `key` (RegistryKey).; `keyPath` (`string`): Значение параметра `keyPath` (string).; `location` (`EntryLocation`): Значение параметра `location` (EntryLocation).; `hive` (`RegistryHive`): Значение параметра `hive` (RegistryHive).; `isInitial` (`bool`): Значение параметра `isInitial` (bool).
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `OtherStartupLocationsWatcher.ResolveContextMenuClsid` (строка 269)
- **Название функции**: `ResolveContextMenuClsid`
- **Что она делает**: Для ContextMenuHandler разрешает CLSID в читаемое имя и DLL.
- **Какие входные данные**: `entry` (`StartupEntry`): Значение параметра `entry` (StartupEntry).; `rawValue` (`string`): Значение параметра `rawValue` (string).
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `OtherStartupLocationsWatcher.ProcessEntry` (строка 283)
- **Название функции**: `ProcessEntry`
- **Что она делает**: Обрабатывает запись: проверка ЧС, добавление в хранилище, уведомление.
- **Какие входные данные**: `entry` (`StartupEntry`): Значение параметра `entry` (StartupEntry).; `isInitial` (`bool`): Значение параметра `isInitial` (bool).
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `OtherStartupLocationsWatcher.StartWatching` (строка 323)
- **Название функции**: `StartWatching`
- **Что она делает**: Запускает фоновое наблюдение за изменениями ключей реестра.
- **Какие входные данные**: `ct` (`CancellationToken`): Значение параметра `ct` (CancellationToken).
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `OtherStartupLocationsWatcher.IsHkcuKey` (строка 362)
- **Название функции**: `IsHkcuKey`
- **Что она делает**: Проверяет, присутствует ли указанный ключ также в HKCU.
- **Какие входные данные**: `keyPath` (`string`): Значение параметра `keyPath` (string).; `location` (`EntryLocation`): Значение параметра `location` (EntryLocation).
- **Какие на выходе**: `bool`: Логический результат выполнения (`true` в случае успеха или соответствия условию, иначе `false`).

#### `OtherStartupLocationsWatcher.StartRegNotify` (строка 380)
- **Название функции**: `StartRegNotify`
- **Что она делает**: Запускает цикл уведомлений об изменениях ключа реестра через P/Invoke.
- **Какие входные данные**: `regHandle` (`SafeHandle`): Значение параметра `regHandle` (SafeHandle).; `keyPath` (`string`): Значение параметра `keyPath` (string).; `hive` (`RegistryHive`): Значение параметра `hive` (RegistryHive).; `ct` (`CancellationToken`): Значение параметра `ct` (CancellationToken).
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `OtherStartupLocationsWatcher.StopAsync` (строка 424)
- **Название функции**: `StopAsync`
- **Что она делает**: Остановка: закрываем ключи реестра, чтобы разблокировать RegNotifyChangeKeyValue.
- **Какие входные данные**: `cancellationToken` (`CancellationToken`): Значение параметра `cancellationToken` (CancellationToken).
- **Какие на выходе**: `Task`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `OtherStartupLocationsWatcher.ParsePath` (строка 434)
- **Название функции**: `ParsePath`
- **Что она делает**: Извлекает путь к файлу из командной строки (поддерживает кавычки и .exe/.dll/.com/.bat).
- **Какие входные данные**: `commandLine` (`string`): Значение параметра `commandLine` (string).
- **Какие на выходе**: `string`: Текстовая строка с результатом операции или сообщением.

#### `OtherStartupLocationsWatcher.static` (строка 485)
- **Название функции**: `static`
- **Что она делает**: Выполняет операцию static в рамках подсистемы.
- **Какие входные данные**: `name` (`string`): Значение параметра `name` (string).; `clsid` (`string dllPath) Resolve(string`): Значение параметра `clsid` (string dllPath) Resolve(string).
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

---

### Файл: `StartupMonitor.Service\Services\ProcessMetadataEnricher.cs`

#### `ProcessMetadataEnricher.ProcessMetadataEnricher` (строка 42)
- **Название функции**: `ProcessMetadataEnricher`
- **Что она делает**: ProcessMetadataEnricher: Выполняет операцию ProcessMetadataEnricher.
- **Какие входные данные**: Входные данные отсутствуют (параметры не требуются).
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `ProcessMetadataEnricher.EnrichOrQueue` (строка 48)
- **Название функции**: `EnrichOrQueue`
- **Что она делает**: Применяет метаданные из кэша (мгновенно). Если в кэше нет — ставит в Time-Sliced очередь.
- **Какие входные данные**: `proc` (`ProcessInfo`): Значение параметра `proc` (ProcessInfo).; `processHandle` (`IntPtr`): Значение параметра `processHandle` (IntPtr).
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `ProcessMetadataEnricher.ProcessEnrichmentLoopAsync` (строка 128)
- **Название функции**: `ProcessEnrichmentLoopAsync`
- **Что она делает**: Фоновый сглаживающий цикл обработки очереди порциями по 5 процессов за такт с микропаузами.
- **Какие входные данные**: Входные данные отсутствуют (параметры не требуются).
- **Какие на выходе**: `Task`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `ProcessMetadataEnricher.PruneDeadProcesses` (строка 225)
- **Название функции**: `PruneDeadProcesses`
- **Что она делает**: Очищает устаревшие записи из кэша для завершившихся процессов.
- **Какие входные данные**: `activePids` (`HashSet<int>`): Значение параметра `activePids` (HashSet<int>).
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `ProcessMetadataEnricher.TryGetCompanyName` (строка 239)
- **Название функции**: `TryGetCompanyName`
- **Что она делает**: TryGetCompanyName: Выполняет операцию TryGetCompanyName.
- **Какие входные данные**: `filePath` (`string`): Входные данные: значение параметра filePath (string).
- **Какие на выходе**: `string`: Выходные данные: результирующее значение типа string.

#### `ProcessMetadataEnricher.TryGetProcessPath` (строка 253)
- **Название функции**: `TryGetProcessPath`
- **Что она делает**: TryGetProcessPath: Выполняет операцию TryGetProcessPath.
- **Какие входные данные**: `pid` (`int`): Входные данные: значение параметра pid (int).
- **Какие на выходе**: `string`: Выходные данные: результирующее значение типа string.

#### `ProcessMetadataEnricher.GetProcessPathFromHandle` (строка 270)
- **Название функции**: `GetProcessPathFromHandle`
- **Что она делает**: GetProcessPathFromHandle: Возвращает ProcessPathFromHandle.
- **Какие входные данные**: `hProcess` (`IntPtr`): Входные данные: значение параметра hProcess (IntPtr).
- **Какие на выходе**: `string`: Выходные данные: результирующее значение типа string.

#### `ProcessMetadataEnricher.TryGetProcessCommandLine` (строка 301)
- **Название функции**: `TryGetProcessCommandLine`
- **Что она делает**: TryGetProcessCommandLine: Выполняет операцию TryGetProcessCommandLine.
- **Какие входные данные**: `hProcess` (`IntPtr`): Входные данные: значение параметра hProcess (IntPtr).
- **Какие на выходе**: `string`: Выходные данные: результирующее значение типа string.

#### `ProcessMetadataEnricher.OpenProcess` (строка 338)
- **Название функции**: `OpenProcess`
- **Что она делает**: Выполняет операцию OpenProcess в рамках подсистемы.
- **Какие входные данные**: `processAccess` (`int`): Входные данные: значение параметра processAccess (int).; `bInheritHandle` (`bool`): Входные данные: значение параметра bInheritHandle (bool).; `processId` (`int`): Входные данные: значение параметра processId (int).
- **Какие на выходе**: `extern IntPtr`: Выходные данные: результирующее значение типа extern IntPtr.

#### `ProcessMetadataEnricher.CloseHandle` (строка 344)
- **Название функции**: `CloseHandle`
- **Что она делает**: Выполняет операцию CloseHandle в рамках подсистемы.
- **Какие входные данные**: `hObject` (`IntPtr`): Входные данные: значение параметра hObject (IntPtr).
- **Какие на выходе**: `extern bool`: Выходные данные: результирующее значение типа extern bool.

#### `ProcessMetadataEnricher.QueryFullProcessImageName` (строка 353)
- **Название функции**: `QueryFullProcessImageName`
- **Что она делает**: Выполняет операцию QueryFullProcessImageName в рамках подсистемы.
- **Какие входные данные**: `hProcess` (`IntPtr`): Входные данные: значение параметра hProcess (IntPtr).; `flags` (`int`): Входные данные: значение параметра flags (int).; `lpExeName` (`[Out] StringBuilder`): Входные данные: значение параметра lpExeName ([Out] StringBuilder).; `lpdwSize` (`ref int`): Входные данные: значение параметра lpdwSize (ref int).
- **Какие на выходе**: `extern bool`: Выходные данные: результирующее значение типа extern bool.

#### `ProcessMetadataEnricher.Dispose` (строка 357)
- **Название функции**: `Dispose`
- **Что она делает**: Dispose: Освобождает занятые ресурсы объекта.
- **Какие входные данные**: Входные данные отсутствуют (параметры не требуются).
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

---

### Файл: `StartupMonitor.Service\Services\ProcessMetricsTracker.cs`

#### `ProcessMetricsTracker.ProcessMetricsTracker` (строка 54)
- **Название функции**: `ProcessMetricsTracker`
- **Что она делает**: ProcessMetricsTracker: Выполняет операцию ProcessMetricsTracker.
- **Какие входные данные**: `enricher` (`ProcessMetadataEnricher`): Входные данные: значение параметра enricher (ProcessMetadataEnricher).; `linker` (`ProcessStartupLinker`): Входные данные: значение параметра linker (ProcessStartupLinker).
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `ProcessMetricsTracker.Subscribe` (строка 67)
- **Название функции**: `Subscribe`
- **Что она делает**: Подписка UI на поток срезов метрик (активирует периодический опрос раз в 1.5 с).
- **Какие входные данные**: Входные данные отсутствуют (параметры не требуются).
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `ProcessMetricsTracker.Unsubscribe` (строка 77)
- **Название функции**: `Unsubscribe`
- **Что она делает**: Отписка UI (при уходе с вкладки или сворачивании в трей).
- **Какие входные данные**: Входные данные отсутствуют (параметры не требуются).
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `ProcessMetricsTracker.CaptureSnapshot` (строка 88)
- **Название функции**: `CaptureSnapshot`
- **Что она делает**: Разовый замер для немедленного ответа на GetProcesses.
- **Какие входные данные**: Входные данные отсутствуют (параметры не требуются).
- **Какие на выходе**: `SystemPerformanceSnapshot`: Результирующий объект или экземпляр структуры данных.

#### `ProcessMetricsTracker.GetProcessSockets` (строка 94)
- **Название функции**: `GetProcessSockets`
- **Что она делает**: Получает список активных сетевых соединений (сокетов) для процесса или всей системы.
- **Какие входные данные**: `pid` (`int`): Значение параметра `pid` (int).
- **Какие на выходе**: `List<ProcessNetworkSocket>`: Коллекция или набор результирующих объектов.

#### `ProcessMetricsTracker.OnPollTick` (строка 99)
- **Название функции**: `OnPollTick`
- **Что она делает**: OnPollTick: Обрабатывает событие OnPollTick.
- **Какие входные данные**: `state` (`object?`): Входные данные: значение параметра state (object?).
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `ProcessMetricsTracker.CollectSnapshotCore` (строка 118)
- **Название функции**: `CollectSnapshotCore`
- **Что она делает**: CollectSnapshotCore: Собирает метрики и информацию SnapshotCore.
- **Какие входные данные**: Входные данные отсутствуют (параметры не требуются).
- **Какие на выходе**: `SystemPerformanceSnapshot`: Выходные данные: результирующее значение типа SystemPerformanceSnapshot.

#### `ProcessMetricsTracker.CalculateSystemCpuPercent` (строка 361)
- **Название функции**: `CalculateSystemCpuPercent`
- **Что она делает**: CalculateSystemCpuPercent: Выполняет операцию CalculateSystemCpuPercent.
- **Какие входные данные**: Входные данные отсутствуют (параметры не требуются).
- **Какие на выходе**: `double`: Выходные данные: результирующее значение типа double.

#### `ProcessMetricsTracker.FileTimeToLong` (строка 396)
- **Название функции**: `FileTimeToLong`
- **Что она делает**: FileTimeToLong: Выполняет операцию FileTimeToLong.
- **Какие входные данные**: `ft` (`System.Runtime.InteropServices.ComTypes.FILETIME`): Входные данные: значение параметра ft (System.Runtime.InteropServices.ComTypes.FILETIME).
- **Какие на выходе**: `long`: Выходные данные: результирующее значение типа long.

#### `ProcessMetricsTracker.GlobalMemoryStatusEx` (строка 430)
- **Название функции**: `GlobalMemoryStatusEx`
- **Что она делает**: Выполняет операцию GlobalMemoryStatusEx в рамках подсистемы.
- **Какие входные данные**: `lpBuffer` (`ref MEMORYSTATUSEX`): Входные данные: значение параметра lpBuffer (ref MEMORYSTATUSEX).
- **Какие на выходе**: `extern bool`: Выходные данные: результирующее значение типа extern bool.

#### `ProcessMetricsTracker.GetSystemTimes` (строка 439)
- **Название функции**: `GetSystemTimes`
- **Что она делает**: Получает или формирует SystemTimes.
- **Какие входные данные**: `lpIdleTime` (`out System.Runtime.InteropServices.ComTypes.FILETIME`): Входные данные: значение параметра lpIdleTime (out System.Runtime.InteropServices.ComTypes.FILETIME).; `lpKernelTime` (`out System.Runtime.InteropServices.ComTypes.FILETIME`): Входные данные: значение параметра lpKernelTime (out System.Runtime.InteropServices.ComTypes.FILETIME).; `lpUserTime` (`out System.Runtime.InteropServices.ComTypes.FILETIME`): Входные данные: значение параметра lpUserTime (out System.Runtime.InteropServices.ComTypes.FILETIME).
- **Какие на выходе**: `extern bool`: Выходные данные: результирующее значение типа extern bool.

#### `ProcessMetricsTracker.OpenProcess` (строка 447)
- **Название функции**: `OpenProcess`
- **Что она делает**: Выполняет операцию OpenProcess в рамках подсистемы.
- **Какие входные данные**: `processAccess` (`int`): Входные данные: значение параметра processAccess (int).; `bInheritHandle` (`bool`): Входные данные: значение параметра bInheritHandle (bool).; `processId` (`int`): Входные данные: значение параметра processId (int).
- **Какие на выходе**: `extern IntPtr`: Выходные данные: результирующее значение типа extern IntPtr.

#### `ProcessMetricsTracker.CloseHandle` (строка 453)
- **Название функции**: `CloseHandle`
- **Что она делает**: Выполняет операцию CloseHandle в рамках подсистемы.
- **Какие входные данные**: `hObject` (`IntPtr`): Входные данные: значение параметра hObject (IntPtr).
- **Какие на выходе**: `extern bool`: Выходные данные: результирующее значение типа extern bool.

#### `ProcessMetricsTracker.GetProcessIoCounters` (строка 460)
- **Название функции**: `GetProcessIoCounters`
- **Что она делает**: Получает или формирует ProcessIoCounters.
- **Какие входные данные**: `hProcess` (`IntPtr`): Входные данные: значение параметра hProcess (IntPtr).; `lpIoCounters` (`out IO_COUNTERS`): Входные данные: значение параметра lpIoCounters (out IO_COUNTERS).
- **Какие на выходе**: `extern bool`: Выходные данные: результирующее значение типа extern bool.

#### `ProcessMetricsTracker.Dispose` (строка 464)
- **Название функции**: `Dispose`
- **Что она делает**: Dispose: Освобождает занятые ресурсы объекта.
- **Какие входные данные**: Входные данные отсутствуют (параметры не требуются).
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

---

### Файл: `StartupMonitor.Service\Services\ProcessNetworkTracker.cs`

#### `ProcessNetworkTracker.CollectIpv4Sockets` (строка 80)
- **Название функции**: `CollectIpv4Sockets`
- **Что она делает**: CollectIpv4Sockets: Собирает метрики и информацию Ipv4Sockets.
- **Какие входные данные**: `currentSocketBytes` (`Dictionary<string, ulong>`): Входные данные: значение параметра currentSocketBytes (Dictionary<string, ulong>).; `accumulatedBytesByPid` (`Dictionary<int, ulong>`): Входные данные: значение параметра accumulatedBytesByPid (Dictionary<int, ulong>).; `socketCounts` (`Dictionary<int, int>`): Входные данные: значение параметра socketCounts (Dictionary<int, int>).
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `ProcessNetworkTracker.CollectIpv6Sockets` (строка 152)
- **Название функции**: `CollectIpv6Sockets`
- **Что она делает**: CollectIpv6Sockets: Собирает метрики и информацию Ipv6Sockets.
- **Какие входные данные**: `currentSocketBytes` (`Dictionary<string, ulong>`): Входные данные: значение параметра currentSocketBytes (Dictionary<string, ulong>).; `accumulatedBytesByPid` (`Dictionary<int, ulong>`): Входные данные: значение параметра accumulatedBytesByPid (Dictionary<int, ulong>).; `socketCounts` (`Dictionary<int, int>`): Входные данные: значение параметра socketCounts (Dictionary<int, int>).
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `ProcessNetworkTracker.CollectUdpPids` (строка 222)
- **Название функции**: `CollectUdpPids`
- **Что она делает**: CollectUdpPids: Собирает метрики и информацию UdpPids.
- **Какие входные данные**: `socketCounts` (`Dictionary<int, int>`): Входные данные: значение параметра socketCounts (Dictionary<int, int>).
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `ProcessNetworkTracker.GetSockets` (строка 290)
- **Название функции**: `GetSockets`
- **Что она делает**: Возвращает список активных сетевых соединений (сокетов) для конкретного процесса (или для всех, если targetPid <= 0).
- **Какие входные данные**: `targetPid` (`int`): Значение параметра `targetPid` (int).
- **Какие на выходе**: `List<ProcessNetworkSocket>`: Коллекция или набор результирующих объектов.

#### `ProcessNetworkTracker.CollectTcp4Sockets` (строка 331)
- **Название функции**: `CollectTcp4Sockets`
- **Что она делает**: CollectTcp4Sockets: Собирает метрики и информацию Tcp4Sockets.
- **Какие входные данные**: `sockets` (`List<ProcessNetworkSocket>`): Входные данные: значение параметра sockets (List<ProcessNetworkSocket>).; `targetPid` (`int`): Входные данные: значение параметра targetPid (int).; `getProcName` (`Func<int, string>`): Входные данные: значение параметра getProcName (Func<int, string>).
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `ProcessNetworkTracker.CollectTcp6Sockets` (строка 384)
- **Название функции**: `CollectTcp6Sockets`
- **Что она делает**: CollectTcp6Sockets: Собирает метрики и информацию Tcp6Sockets.
- **Какие входные данные**: `sockets` (`List<ProcessNetworkSocket>`): Входные данные: значение параметра sockets (List<ProcessNetworkSocket>).; `targetPid` (`int`): Входные данные: значение параметра targetPid (int).; `getProcName` (`Func<int, string>`): Входные данные: значение параметра getProcName (Func<int, string>).
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `ProcessNetworkTracker.CollectUdp4Sockets` (строка 437)
- **Название функции**: `CollectUdp4Sockets`
- **Что она делает**: CollectUdp4Sockets: Собирает метрики и информацию Udp4Sockets.
- **Какие входные данные**: `sockets` (`List<ProcessNetworkSocket>`): Входные данные: значение параметра sockets (List<ProcessNetworkSocket>).; `targetPid` (`int`): Входные данные: значение параметра targetPid (int).; `getProcName` (`Func<int, string>`): Входные данные: значение параметра getProcName (Func<int, string>).
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `ProcessNetworkTracker.CollectUdp6Sockets` (строка 487)
- **Название функции**: `CollectUdp6Sockets`
- **Что она делает**: CollectUdp6Sockets: Собирает метрики и информацию Udp6Sockets.
- **Какие входные данные**: `sockets` (`List<ProcessNetworkSocket>`): Входные данные: значение параметра sockets (List<ProcessNetworkSocket>).; `targetPid` (`int`): Входные данные: значение параметра targetPid (int).; `getProcName` (`Func<int, string>`): Входные данные: значение параметра getProcName (Func<int, string>).
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `ProcessNetworkTracker.ParsePort` (строка 536)
- **Название функции**: `ParsePort`
- **Что она делает**: ParsePort: Выполняет операцию ParsePort.
- **Какие входные данные**: `portNetworkOrder` (`uint`): Входные данные: значение параметра portNetworkOrder (uint).
- **Какие на выходе**: `int`: Выходные данные: результирующее значение типа int.

#### `ProcessNetworkTracker.ParseIpv4` (строка 542)
- **Название функции**: `ParseIpv4`
- **Что она делает**: ParseIpv4: Выполняет операцию ParseIpv4.
- **Какие входные данные**: `ipDword` (`uint`): Входные данные: значение параметра ipDword (uint).
- **Какие на выходе**: `string`: Выходные данные: результирующее значение типа string.

#### `ProcessNetworkTracker.IPAddress` (строка 545)
- **Название функции**: `IPAddress`
- **Что она делает**: IPAddress: Выполняет операцию IPAddress.
- **Какие входные данные**: `(long)ipDword).ToString(`: Входные данные: значение параметра (long)ipDword).ToString( ().
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `ProcessNetworkTracker.ParseIpv6` (строка 551)
- **Название функции**: `ParseIpv6`
- **Что она делает**: ParseIpv6: Выполняет операцию ParseIpv6.
- **Какие входные данные**: `bytes` (`byte[]`): Входные данные: значение параметра bytes (byte[]).; `scopeId` (`uint`): Входные данные: значение параметра scopeId (uint).
- **Какие на выходе**: `string`: Выходные данные: результирующее значение типа string.

#### `ProcessNetworkTracker.IPAddress` (строка 555)
- **Название функции**: `IPAddress`
- **Что она делает**: IPAddress: Выполняет операцию IPAddress.
- **Какие входные данные**: `bytes`: Входные данные: значение параметра bytes ().; `scopeId).ToString(`: Входные данные: значение параметра scopeId).ToString( ().
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `ProcessNetworkTracker.GetTcpStateString` (строка 560)
- **Название функции**: `GetTcpStateString`
- **Что она делает**: GetTcpStateString: Возвращает TcpStateString.
- **Какие входные данные**: `state` (`uint`): Входные данные: значение параметра state (uint).
- **Какие на выходе**: `string`: Выходные данные: результирующее значение типа string.

---

### Файл: `StartupMonitor.Service\Services\ProcessStartupLinker.cs`

#### `ProcessStartupLinker.UpdateIndex` (строка 21)
- **Название функции**: `UpdateIndex`
- **Что она делает**: Обновляет индекс записей автозапуска из текущего снимка базы.
- **Какие входные данные**: `entries` (`IEnumerable<StartupEntry>`): Значение параметра `entries` (IEnumerable<StartupEntry>).
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `ProcessStartupLinker.LinkProcess` (строка 55)
- **Название функции**: `LinkProcess`
- **Что она делает**: Связывает процесс с записью автозапуска или определяет имя родительского процесса.
- **Какие входные данные**: `proc` (`ProcessInfo`): Значение параметра `proc` (ProcessInfo).; `processNamesByPid` (`IReadOnlyDictionary<int, string>?`): Значение параметра `processNamesByPid` (IReadOnlyDictionary<int, string>?).
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `ProcessStartupLinker.SetStartupInfo` (строка 112)
- **Название функции**: `SetStartupInfo`
- **Что она делает**: SetStartupInfo: Устанавливает StartupInfo.
- **Какие входные данные**: `proc` (`ProcessInfo`): Входные данные: значение параметра proc (ProcessInfo).; `entry` (`StartupEntry`): Входные данные: значение параметра entry (StartupEntry).
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `ProcessStartupLinker.NormalizePath` (строка 128)
- **Название функции**: `NormalizePath`
- **Что она делает**: Нормализует путь к файлу: извлекает путь из кавычек/командной строки, разворачивает переменные окружения.
- **Какие входные данные**: `path` (`string`): Значение параметра `path` (string).
- **Какие на выходе**: `string`: Текстовая строка с результатом операции или сообщением.

#### `ProcessStartupLinker.NtQueryInformationProcess` (строка 167)
- **Название функции**: `NtQueryInformationProcess`
- **Что она делает**: Выполняет операцию NtQueryInformationProcess в рамках подсистемы.
- **Какие входные данные**: `processHandle` (`IntPtr`): Входные данные: значение параметра processHandle (IntPtr).; `processInformationClass` (`int`): Входные данные: значение параметра processInformationClass (int).; `processInformation` (`ref PROCESS_BASIC_INFORMATION`): Входные данные: значение параметра processInformation (ref PROCESS_BASIC_INFORMATION).; `processInformationLength` (`int`): Входные данные: значение параметра processInformationLength (int).; `returnLength` (`out int`): Входные данные: значение параметра returnLength (out int).
- **Какие на выходе**: `extern int`: Выходные данные: результирующее значение типа extern int.

#### `ProcessStartupLinker.GetParentProcessId` (строка 181)
- **Название функции**: `GetParentProcessId`
- **Что она делает**: Быстро считывает Parent PID процесса через NtQueryInformationProcess без использования тяжелого WMI.
- **Какие входные данные**: `processHandle` (`IntPtr`): Значение параметра `processHandle` (IntPtr).
- **Какие на выходе**: `int`: Числовой результат, количество обработанных элементов или метрика.

#### `ProcessStartupLinker.CreateToolhelp32Snapshot` (строка 206)
- **Название функции**: `CreateToolhelp32Snapshot`
- **Что она делает**: Выполняет операцию CreateToolhelp32Snapshot в рамках подсистемы.
- **Какие входные данные**: `dwFlags` (`uint`): Входные данные: значение параметра dwFlags (uint).; `th32ProcessID` (`uint`): Входные данные: значение параметра th32ProcessID (uint).
- **Какие на выходе**: `extern IntPtr`: Выходные данные: результирующее значение типа extern IntPtr.

#### `ProcessStartupLinker.Process32First` (строка 213)
- **Название функции**: `Process32First`
- **Что она делает**: Выполняет операцию Process32First в рамках подсистемы.
- **Какие входные данные**: `hSnapshot` (`IntPtr`): Входные данные: значение параметра hSnapshot (IntPtr).; `lppe` (`ref PROCESSENTRY32`): Входные данные: значение параметра lppe (ref PROCESSENTRY32).
- **Какие на выходе**: `extern bool`: Выходные данные: результирующее значение типа extern bool.

#### `ProcessStartupLinker.Process32Next` (строка 220)
- **Название функции**: `Process32Next`
- **Что она делает**: Выполняет операцию Process32Next в рамках подсистемы.
- **Какие входные данные**: `hSnapshot` (`IntPtr`): Входные данные: значение параметра hSnapshot (IntPtr).; `lppe` (`ref PROCESSENTRY32`): Входные данные: значение параметра lppe (ref PROCESSENTRY32).
- **Какие на выходе**: `extern bool`: Выходные данные: результирующее значение типа extern bool.

#### `ProcessStartupLinker.CloseHandle` (строка 226)
- **Название функции**: `CloseHandle`
- **Что она делает**: Выполняет операцию CloseHandle в рамках подсистемы.
- **Какие входные данные**: `hObject` (`IntPtr`): Входные данные: значение параметра hObject (IntPtr).
- **Какие на выходе**: `extern bool`: Выходные данные: результирующее значение типа extern bool.

#### `ProcessStartupLinker.ProcessSnapshotEntry` (строка 245)
- **Название функции**: `ProcessSnapshotEntry`
- **Что она делает**: Компактная запись снимка процесса: Parent PID и число потоков из Toolhelp32.
- **Какие входные данные**: `ParentPid` (`int`): Значение параметра `ParentPid` (int).; `ThreadCount` (`int`): Значение параметра `ThreadCount` (int).
- **Какие на выходе**: `readonly record struct`: Результирующий объект или экземпляр структуры данных.

#### `ProcessStartupLinker.GetProcessSnapshotMap` (строка 251)
- **Название функции**: `GetProcessSnapshotMap`
- **Что она делает**: Возвращает мгновенный снимок всей таблицы процессов Windows: Child PID -> (Parent PID, ThreadCount). Работает за ~1 мс, не требует открытия дескрипторов (OpenProcess) и покрывает даже защищённые процессы.
- **Какие входные данные**: Входные данные отсутствуют (параметры не требуются).
- **Какие на выходе**: `Dictionary<int, ProcessSnapshotEntry>`: Числовой результат, количество обработанных элементов или метрика.

#### `ProcessStartupLinker.GetParentPidMap` (строка 291)
- **Название функции**: `GetParentPidMap`
- **Что она делает**: Возвращает мгновенный снимок всей таблицы процессов Windows: Child PID -> Parent PID. Работает за ~1 мс, не требует открытия дескрипторов (OpenProcess) и покрывает даже защищённые процессы. Оставлена для совместимости; внутри делегирует <see cref="GetProcessSnapshotMap"/>.
- **Какие входные данные**: Входные данные отсутствуют (параметры не требуются).
- **Какие на выходе**: `Dictionary<int, int>`: Числовой результат, количество обработанных элементов или метрика.

---

### Файл: `StartupMonitor.Service\Services\RegistryWatcher.cs`

#### `RegistryWatcher.Dispose` (строка 40)
- **Название функции**: `Dispose`
- **Что она делает**: Dispose: Освобождает занятые ресурсы объекта.
- **Какие входные данные**: Входные данные отсутствуют (параметры не требуются).
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `RegistryWatcher.ExecuteAsync` (строка 67)
- **Название функции**: `ExecuteAsync`
- **Что она делает**: Основной цикл: начальное сканирование → наблюдение.
- **Какие входные данные**: `stoppingToken` (`CancellationToken`): Значение параметра `stoppingToken` (CancellationToken).
- **Какие на выходе**: `Task`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `RegistryWatcher.InitialScan` (строка 78)
- **Название функции**: `InitialScan`
- **Что она делает**: Начальное сканирование: HKLM + HKCU + все пользовательские кусты.
- **Какие входные данные**: `ct` (`CancellationToken`): Значение параметра `ct` (CancellationToken).
- **Какие на выходе**: `Task`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `RegistryWatcher.ScanHive` (строка 103)
- **Название функции**: `ScanHive`
- **Что она делает**: Сканирует базовый ключ реестра: читает значения, обогащает записи, проверяет ЧС.
- **Какие входные данные**: `baseKey` (`RegistryKey`): Значение параметра `baseKey` (RegistryKey).; `hivePrefix` (`string`): Значение параметра `hivePrefix` (string).; `allowWow64` (`bool`): Значение параметра `allowWow64` (bool).; `isInitial` (`bool`): Значение параметра `isInitial` (bool).
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `RegistryWatcher.StartWatching` (строка 173)
- **Название функции**: `StartWatching`
- **Что она делает**: Запускает фоновое наблюдение за ключами реестра (HKLM + HKCU).
- **Какие входные данные**: `ct` (`CancellationToken`): Значение параметра `ct` (CancellationToken).
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `RegistryWatcher.StartRegNotify` (строка 230)
- **Название функции**: `StartRegNotify`
- **Что она делает**: Запускает цикл уведомлений об изменениях ключа реестра через P/Invoke.
- **Какие входные данные**: `regHandle` (`SafeHandle`): Значение параметра `regHandle` (SafeHandle).; `keyPath` (`string`): Значение параметра `keyPath` (string).; `hive` (`RegistryHive`): Значение параметра `hive` (RegistryHive).; `ct` (`CancellationToken`): Значение параметра `ct` (CancellationToken).
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `RegistryWatcher.StopAsync` (строка 287)
- **Название функции**: `StopAsync`
- **Что она делает**: Остановка: закрываем ключи, чтобы разблокировать RegNotifyChangeKeyValue.
- **Какие входные данные**: `cancellationToken` (`CancellationToken`): Значение параметра `cancellationToken` (CancellationToken).
- **Какие на выходе**: `Task`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

---

### Файл: `StartupMonitor.Service\Services\ServiceProcessHelper.cs`

#### `ServiceProcessHelper.OpenSCManager` (строка 19)
- **Название функции**: `OpenSCManager`
- **Что она делает**: Выполняет операцию OpenSCManager в рамках подсистемы.
- **Какие входные данные**: `lpMachineName` (`string?`): Входные данные: значение параметра lpMachineName (string?).; `lpDatabaseName` (`string?`): Входные данные: значение параметра lpDatabaseName (string?).; `dwDesiredAccess` (`uint`): Входные данные: значение параметра dwDesiredAccess (uint).
- **Какие на выходе**: `extern IntPtr`: Выходные данные: результирующее значение типа extern IntPtr.

#### `ServiceProcessHelper.CloseServiceHandle` (строка 38)
- **Название функции**: `CloseServiceHandle`
- **Что она делает**: Выполняет операцию CloseServiceHandle в рамках подсистемы.
- **Какие входные данные**: `hSCObject` (`IntPtr`): Входные данные: значение параметра hSCObject (IntPtr).
- **Какие на выходе**: `extern bool`: Выходные данные: результирующее значение типа extern bool.

#### `ServiceProcessHelper.GetRunningServicesByPid` (строка 71)
- **Название функции**: `GetRunningServicesByPid`
- **Что она делает**: Возвращает словарь [PID -> список отображаемых имен служб] для всех запущенных служб Windows. Выполняется практически мгновенно на уровне C-вызовов ОС.
- **Какие входные данные**: Входные данные отсутствуют (параметры не требуются).
- **Какие на выходе**: `Dictionary<int, List<string>>`: Текстовая строка с результатом операции или сообщением.

---

### Файл: `StartupMonitor.Service\Services\ServiceWatcher.cs`

#### `ServiceWatcher.ExecuteAsync` (строка 42)
- **Название функции**: `ExecuteAsync`
- **Что она делает**: Основной цикл: начальное сканирование через WMI → подписка на события создания служб.
- **Какие входные данные**: `stoppingToken` (`CancellationToken`): Значение параметра `stoppingToken` (CancellationToken).
- **Какие на выходе**: `Task`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `ServiceWatcher.InitialScan` (строка 81)
- **Название функции**: `InitialScan`
- **Что она делает**: Начальное сканирование: WMI-запрос всех авто-служб.
- **Какие входные данные**: `ct` (`CancellationToken`): Значение параметра `ct` (CancellationToken).
- **Какие на выходе**: `Task`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `ServiceWatcher.OnServiceCreated` (строка 136)
- **Название функции**: `OnServiceCreated`
- **Что она делает**: Обработчик WMI-события создания новой службы.
- **Какие входные данные**: `sender` (`object`): Значение параметра `sender` (object).; `e` (`EventArrivedEventArgs`): Значение параметра `e` (EventArrivedEventArgs).
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

---

### Файл: `StartupMonitor.Service\Services\SnapshotManager.cs`

#### `SnapshotManager.SnapshotManager` (строка 18)
- **Название функции**: `SnapshotManager`
- **Что она делает**: SnapshotManager: Выполняет операцию SnapshotManager.
- **Какие входные данные**: `store` (`JsonDataStore`): Входные данные: значение параметра store (JsonDataStore).; `logger` (`ILogger<SnapshotManager>`): Входные данные: значение параметра logger (ILogger<SnapshotManager>).
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `SnapshotManager.CreateSnapshot` (строка 27)
- **Название функции**: `CreateSnapshot`
- **Что она делает**: Создаёт новый контрольный снимок автозапуска из текущей конфигурации системы.
- **Какие входные данные**: `name` (`string?`): Значение параметра `name` (string?).; `description` (`string?`): Значение параметра `description` (string?).
- **Какие на выходе**: `BaselineSnapshot`: Результирующий объект или экземпляр структуры данных.

#### `SnapshotManager.GetSnapshots` (строка 57)
- **Название функции**: `GetSnapshots`
- **Что она делает**: Возвращает список всех сохранённых снимков автозапуска.
- **Какие входные данные**: Входные данные отсутствуют (параметры не требуются).
- **Какие на выходе**: `List<BaselineSnapshot>`: Коллекция или набор результирующих объектов.

#### `SnapshotManager.DeleteSnapshot` (строка 65)
- **Название функции**: `DeleteSnapshot`
- **Что она делает**: Удаляет снимок по указанному идентификатору.
- **Какие входные данные**: `snapshotId` (`string`): Значение параметра `snapshotId` (string).
- **Какие на выходе**: `bool`: Логический результат выполнения (`true` в случае успеха или соответствия условию, иначе `false`).

---

### Файл: `StartupMonitor.Service\Services\StartupFolderWatcher.cs`

#### `StartupFolderWatcher.GetAllUserStartupFolders` (строка 44)
- **Название функции**: `GetAllUserStartupFolders`
- **Что она делает**: Возвращает пути ко всем папкам автозапуска всех пользователей.
- **Какие входные данные**: Входные данные отсутствуют (параметры не требуются).
- **Какие на выходе**: `string[]`: Текстовая строка с результатом операции или сообщением.

#### `StartupFolderWatcher.ExecuteAsync` (строка 90)
- **Название функции**: `ExecuteAsync`
- **Что она делает**: Основной цикл: начальное сканирование → наблюдение за файловой системой.
- **Какие входные данные**: `stoppingToken` (`CancellationToken`): Значение параметра `stoppingToken` (CancellationToken).
- **Какие на выходе**: `Task`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `StartupFolderWatcher.InitialScan` (строка 113)
- **Название функции**: `InitialScan`
- **Что она делает**: Начальное сканирование: читаем все файлы из папок автозапуска.
- **Какие входные данные**: `ct` (`CancellationToken`): Значение параметра `ct` (CancellationToken).
- **Какие на выходе**: `Task`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `StartupFolderWatcher.StartWatching` (строка 132)
- **Название функции**: `StartWatching`
- **Что она делает**: Запускает FileSystemWatcher для каждой папки автозапуска.
- **Какие входные данные**: `ct` (`CancellationToken`): Значение параметра `ct` (CancellationToken).
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `StartupFolderWatcher.DisposeWatchers` (строка 171)
- **Название функции**: `DisposeWatchers`
- **Что она делает**: Освобождает все FileSystemWatcher.
- **Какие входные данные**: Входные данные отсутствуют (параметры не требуются).
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `StartupFolderWatcher.ProcessFile` (строка 179)
- **Название функции**: `ProcessFile`
- **Что она делает**: Обрабатывает новый файл в папке автозапуска: проверка ЧС, добавление, уведомление.
- **Какие входные данные**: `filePath` (`string`): Значение параметра `filePath` (string).; `isInitial` (`bool`): Значение параметра `isInitial` (bool).
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

---

### Файл: `StartupMonitor.Service\Services\SystemScannerService.cs`

#### `SystemScannerService.readonly` (строка 22)
- **Название функции**: `readonly`
- **Что она делает**: Пути стандартных точек реестра (Run, RunOnce, RunServices, Policies).
- **Какие входные данные**: `Path` (`string`): Значение параметра `Path` (string).; `Location` (`EntryLocation`): Значение параметра `Location` (EntryLocation).
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `SystemScannerService.readonly` (строка 40)
- **Название функции**: `readonly`
- **Что она делает**: Прочие точки реестра (Winlogon, AppInitDll, IFEO и др.).
- **Какие входные данные**: `Path` (`string`): Значение параметра `Path` (string).; `Location` (`EntryLocation`): Значение параметра `Location` (EntryLocation).
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `SystemScannerService.ScanAll` (строка 63)
- **Название функции**: `ScanAll`
- **Что она делает**: Выполняет полное сканирование точек автозапуска. Возвращает снимок системы.
- **Какие входные данные**: `logger` (`Microsoft.Extensions.Logging.ILogger?`): Логгер; `skipRegistry` (`bool`): Пропустить сканирование реестра (при высокой загрузке CPU)
- **Какие на выходе**: `List<StartupEntry>`: Коллекция или набор результирующих объектов.

#### `SystemScannerService.GetHivePrefix` (строка 154)
- **Название функции**: `GetHivePrefix`
- **Что она делает**: Возвращает префикс куста реестра для отображения.
- **Какие входные данные**: `hive` (`RegistryHive`): Значение параметра `hive` (RegistryHive).
- **Какие на выходе**: `string`: Текстовая строка с результатом операции или сообщением.

#### `SystemScannerService.ScanRegistry` (строка 160)
- **Название функции**: `ScanRegistry`
- **Что она делает**: Сканирует стандартные точки реестра (HKLM + HKCU + все пользовательские кусты).
- **Какие входные данные**: `results` (`List<StartupEntry>`): Значение параметра `results` (List<StartupEntry>).
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `SystemScannerService.ScanOtherLocations` (строка 221)
- **Название функции**: `ScanOtherLocations`
- **Что она делает**: Сканирует прочие точки реестра, включая подразделы и CLSID.
- **Какие входные данные**: `results` (`List<StartupEntry>`): Значение параметра `results` (List<StartupEntry>).
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `SystemScannerService.ScanClsidEntries` (строка 414)
- **Название функции**: `ScanClsidEntries`
- **Что она делает**: Сканирует ТОЛЬКО реально зарегистрированные в системе Shell Context Menu Handlers: собирает активные GUID из *\shellex\ContextMenuHandlers и родственных веток HKCR, затем читает путь к DLL/EXE только для этого множества из HKCR\CLSID\{GUID}\InprocServer32|LocalServer32.
- **Какие входные данные**: `results` (`List<StartupEntry>`): Значение параметра `results` (List<StartupEntry>).
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `SystemScannerService.IsHkcuOtherKey` (строка 541)
- **Название функции**: `IsHkcuOtherKey`
- **Что она делает**: Проверяет, доступен ли указанный ключ реестра в HKCU.
- **Какие входные данные**: `keyPath` (`string`): Значение параметра `keyPath` (string).
- **Какие на выходе**: `bool`: Логический результат выполнения (`true` в случае успеха или соответствия условию, иначе `false`).

#### `SystemScannerService.ScanStartupFolders` (строка 553)
- **Название функции**: `ScanStartupFolders`
- **Что она делает**: Сканирует папки автозапуска всех пользователей.
- **Какие входные данные**: `results` (`List<StartupEntry>`): Значение параметра `results` (List<StartupEntry>).
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `SystemScannerService.ScanFolder` (строка 590)
- **Название функции**: `ScanFolder`
- **Что она делает**: Сканирует одну папку автозапуска, добавляя файлы в результаты.
- **Какие входные данные**: `folder` (`string`): Значение параметра `folder` (string).; `results` (`List<StartupEntry>`): Значение параметра `results` (List<StartupEntry>).; `scanned` (`HashSet<string>`): Значение параметра `scanned` (HashSet<string>).
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `SystemScannerService.ScanServices` (строка 619)
- **Название функции**: `ScanServices`
- **Что она делает**: Сканирует автоматические службы через WMI.
- **Какие входные данные**: `results` (`List<StartupEntry>`): Значение параметра `results` (List<StartupEntry>).
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `SystemScannerService.ScanWmiSubscriptions` (строка 663)
- **Название функции**: `ScanWmiSubscriptions`
- **Что она делает**: Сканирует подписки на события WMI (root\subscription) — постоянная точка автозапуска (EventFilter → CommandLineEventConsumer / ActiveScriptEventConsumer).
- **Какие входные данные**: `results` (`List<StartupEntry>`): Значение параметра `results` (List<StartupEntry>).
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `SystemScannerService.ScanTaskScheduler` (строка 685)
- **Название функции**: `ScanTaskScheduler`
- **Что она делает**: Сканирует задания в планировщике задач.
- **Какие входные данные**: `results` (`List<StartupEntry>`): Значение параметра `results` (List<StartupEntry>).
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `SystemScannerService.FormatTaskAction` (строка 720)
- **Название функции**: `FormatTaskAction`
- **Что она делает**: Извлекает команду из действия задачи планировщика.
- **Какие входные данные**: `task` (`Microsoft.Win32.TaskScheduler.Task`): Значение параметра `task` (Microsoft.Win32.TaskScheduler.Task).
- **Какие на выходе**: `string`: Текстовая строка с результатом операции или сообщением.

#### `SystemScannerService.static` (строка 806)
- **Название функции**: `static`
- **Что она делает**: Читает из манифеста AppX-пакета StartupTask: (TaskId, exe файл, отображаемое имя). Возвращает пустые строки, если StartupTask в манифесте нет.
- **Какие входные данные**: `string`: Значение параметра `string`.; `string`: Значение параметра `string`.; `manifestPath` (`string) ReadStartupTaskFromManifest(string`): Значение параметра `manifestPath` (string) ReadStartupTaskFromManifest(string).
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `SystemScannerService.TryMatch` (строка 841)
- **Название функции**: `TryMatch`
- **Что она делает**: Пытается сопоставить запись из БД с записью из свежего скана. Использует несколько стратегий: LocationDetail, Name+FilePath, только FilePath, последний сегмент Name.
- **Какие входные данные**: `dbEntry` (`StartupEntry`): Значение параметра `dbEntry` (StartupEntry).; `scannedEntry` (`StartupEntry`): Значение параметра `scannedEntry` (StartupEntry).
- **Какие на выходе**: `bool`: Логический результат выполнения (`true` в случае успеха или соответствия условию, иначе `false`).

#### `SystemScannerService.VerifyInScan` (строка 884)
- **Название функции**: `VerifyInScan`
- **Что она делает**: Проверяет, находится ли запись в результатах скана. При scanResults &gt; 100 использует HashSet по (Location, LocationDetail) и (Location, Name, FilePath) вместо линейного Any(TryMatch).
- **Какие входные данные**: `entry` (`StartupEntry`): Значение параметра `entry` (StartupEntry).; `scanResults` (`List<StartupEntry>`): Значение параметра `scanResults` (List<StartupEntry>).
- **Какие на выходе**: `bool`: Логический результат выполнения (`true` в случае успеха или соответствия условию, иначе `false`).

---

### Файл: `StartupMonitor.Service\Services\TaskSchedulerWatcher.cs`

#### `TaskSchedulerWatcher.Dispose` (строка 46)
- **Название функции**: `Dispose`
- **Что она делает**: Dispose: Освобождает занятые ресурсы объекта.
- **Какие входные данные**: Входные данные отсутствуют (параметры не требуются).
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `TaskSchedulerWatcher.GetOrCreateTaskService` (строка 54)
- **Название функции**: `GetOrCreateTaskService`
- **Что она делает**: Получает или пересоздаёт подключение к планировщику задач.
- **Какие входные данные**: Входные данные отсутствуют (параметры не требуются).
- **Какие на выходе**: `TaskService`: Результирующий объект или экземпляр структуры данных.

#### `TaskSchedulerWatcher.ExecuteAsync` (строка 62)
- **Название функции**: `ExecuteAsync`
- **Что она делает**: Основной цикл: подключение к планировщику → начальное сканирование → опрос изменений.
- **Какие входные данные**: `stoppingToken` (`CancellationToken`): Значение параметра `stoppingToken` (CancellationToken).
- **Какие на выходе**: `Task`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `TaskSchedulerWatcher.InitialScan` (строка 99)
- **Название функции**: `InitialScan`
- **Что она делает**: Начальное сканирование: итерация по всем задачам, поиск задач автозапуска.
- **Какие входные данные**: `ct` (`CancellationToken`): Значение параметра `ct` (CancellationToken).
- **Какие на выходе**: `Task`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `TaskSchedulerWatcher.PollForChanges` (строка 136)
- **Название функции**: `PollForChanges`
- **Что она делает**: Периодический опрос (каждые 30 с) для обнаружения новых задач.
- **Какие входные данные**: `ct` (`CancellationToken`): Значение параметра `ct` (CancellationToken).
- **Какие на выходе**: `Task`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `TaskSchedulerWatcher.ProcessTask` (строка 203)
- **Название функции**: `ProcessTask`
- **Что она делает**: Обрабатывает задачу: создаёт запись, проверяет ЧС, добавляет в хранилище, уведомляет UI.
- **Какие входные данные**: `task` (`Microsoft.Win32.TaskScheduler.Task`): Значение параметра `task` (Microsoft.Win32.TaskScheduler.Task).
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `TaskSchedulerWatcher.IsStartupTask` (строка 260)
- **Название функции**: `IsStartupTask`
- **Что она делает**: Задача относится к автозапуску: есть ExecAction и триггер Logon/Boot.
- **Какие входные данные**: `task` (`Microsoft.Win32.TaskScheduler.Task`): Значение параметра `task` (Microsoft.Win32.TaskScheduler.Task).
- **Какие на выходе**: `bool`: Логический результат выполнения (`true` в случае успеха или соответствия условию, иначе `false`).

#### `TaskSchedulerWatcher.FindByTaskPath` (строка 272)
- **Название функции**: `FindByTaskPath`
- **Что она делает**: Имена задач не уникальны между папками — сравниваем по полному пути задачи.
- **Какие входные данные**: `taskPath` (`string?`): Значение параметра `taskPath` (string?).
- **Какие на выходе**: `StartupEntry?`: Результирующий объект или экземпляр структуры данных.

#### `TaskSchedulerWatcher.CollectTasks` (строка 308)
- **Название функции**: `CollectTasks`
- **Что она делает**: Рекурсивно собирает все задачи из папки и подпапок.
- **Какие входные данные**: `folder` (`Microsoft.Win32.TaskScheduler.TaskFolder`): Значение параметра `folder` (Microsoft.Win32.TaskScheduler.TaskFolder).; `result` (`List<Microsoft.Win32.TaskScheduler.Task>`): Значение параметра `result` (List<Microsoft.Win32.TaskScheduler.Task>).
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `TaskSchedulerWatcher.FormatTaskAction` (строка 331)
- **Название функции**: `FormatTaskAction`
- **Что она делает**: Извлекает команду из действия задачи планировщика (ExecAction).
- **Какие входные данные**: `task` (`Microsoft.Win32.TaskScheduler.Task`): Значение параметра `task` (Microsoft.Win32.TaskScheduler.Task).
- **Какие на выходе**: `string`: Текстовая строка с результатом операции или сообщением.

---

### Файл: `StartupMonitor.Service\Services\WatcherHelper.cs`

#### `WatcherHelper.ActionNoticePayload` (строка 120)
- **Название функции**: `ActionNoticePayload`
- **Что она делает**: ActionNoticePayload: Выполняет операцию ActionNoticePayload.
- **Какие входные данные**: `""` (`entry.Name ??`): Входные данные: значение параметра "" (entry.Name ??).; `"removedAgain")`: Входные данные: значение параметра "removedAgain") ().
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `WatcherHelper.ActionNoticePayload` (строка 176)
- **Название функции**: `ActionNoticePayload`
- **Что она делает**: ActionNoticePayload: Выполняет операцию ActionNoticePayload.
- **Какие входные данные**: `""` (`entry.Name ??`): Входные данные: значение параметра "" (entry.Name ??).; `"lockdownBlocked")`: Входные данные: значение параметра "lockdownBlocked") ().
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `WatcherHelper.ActionNoticePayload` (строка 303)
- **Название функции**: `ActionNoticePayload`
- **Что она делает**: ActionNoticePayload: Выполняет операцию ActionNoticePayload.
- **Какие входные данные**: `""` (`existing.Name ??`): Входные данные: значение параметра "" (existing.Name ??).; `"autoDisabled")`: Входные данные: значение параметра "autoDisabled") ().
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `WatcherHelper.ParsePath` (строка 318)
- **Название функции**: `ParsePath`
- **Что она делает**: Извлекает путь к исполняемому файлу из командной строки. Поддерживает кавычки, абсолютные пути с диском, %variables%, а также сложные случаи типа "rundll32.exe C:\path\to.dll,EntryPoint".
- **Какие входные данные**: `commandLine` (`string`): Значение параметра `commandLine` (string).
- **Какие на выходе**: `string`: Текстовая строка с результатом операции или сообщением.

#### `WatcherHelper.LooksLikeCommand` (строка 385)
- **Название функции**: `LooksLikeCommand`
- **Что она делает**: Определяет, похоже ли значение реестра на команду автозапуска. Командой считается: первый токен — абсолютный путь (диск/UNC/%env%) либо исполняемый файл (explorer.exe). Значения-метаданные (CLSID, версии, "yes", числа, списки через запятую, имена DLL без пути — кодеки, мониторы, LSA) командой не являются.
- **Какие входные данные**: `value` (`string`): Значение параметра `value` (string).
- **Какие на выходе**: `bool`: Логический результат выполнения (`true` в случае успеха или соответствия условию, иначе `false`).

#### `WatcherHelper.GetActiveSetupStubPath` (строка 425)
- **Название функции**: `GetActiveSetupStubPath`
- **Что она делает**: Возвращает StubPath компонента Active Setup, если компонент активен и его StubPath похож на команду. Деактивированные компоненты (имя ключа или StubPath с префиксом '>') и ключи без StubPath не выполняются при входе — возвращает null.
- **Какие входные данные**: `componentKey` (`RegistryKey`): Значение параметра `componentKey` (RegistryKey).
- **Какие на выходе**: `string?`: Текстовая строка с результатом операции или сообщением.

#### `WatcherHelper.ComputeStableId` (строка 435)
- **Название функции**: `ComputeStableId`
- **Что она делает**: Вычисляет стабильный (детерминированный) ID записи на основе Location, LocationDetail и Name. Использует SHA256 первых 12 символов hex.
- **Какие входные данные**: `entry` (`StartupEntry`): Значение параметра `entry` (StartupEntry).
- **Какие на выходе**: `string`: Текстовая строка с результатом операции или сообщением.

#### `WatcherHelper.IsExecutableExtension` (строка 444)
- **Название функции**: `IsExecutableExtension`
- **Что она делает**: Проверяет, является ли расширение файла исполняемым (.exe, .dll, .com, .scr, .bat, .cmd).
- **Какие входные данные**: `ext` (`string`): Значение параметра `ext` (string).
- **Какие на выходе**: `bool`: Логический результат выполнения (`true` в случае успеха или соответствия условию, иначе `false`).

#### `WatcherHelper.ParseServicePath` (строка 456)
- **Название функции**: `ParseServicePath`
- **Что она делает**: Парсит путь к исполняемому файлу из строки команды службы (PathName). Учитывает кавычки.
- **Какие входные данные**: `path` (`string?`): Значение параметра `path` (string?).
- **Какие на выходе**: `string`: Текстовая строка с результатом операции или сообщением.

#### `WatcherHelper.GetLocationFromKeyPath` (строка 470)
- **Название функции**: `GetLocationFromKeyPath`
- **Что она делает**: Определяет тип расположения (EntryLocation) по пути ключа реестра.
- **Какие входные данные**: `keyPath` (`string`): Значение параметра `keyPath` (string).
- **Какие на выходе**: `EntryLocation`: Результирующий объект или экземпляр структуры данных.

---

### Файл: `StartupMonitor.Service\Services\WmiSubscriptionWatcher.cs`

#### `WmiSubscriptionWatcher.ExecuteAsync` (строка 48)
- **Название функции**: `ExecuteAsync`
- **Что она делает**: ExecuteAsync: Выполняет операцию ExecuteAsync.
- **Какие входные данные**: `stoppingToken` (`CancellationToken`): Входные данные: значение параметра stoppingToken (CancellationToken).
- **Какие на выходе**: `Task`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `WmiSubscriptionWatcher.ScheduleRescan` (строка 124)
- **Название функции**: `ScheduleRescan`
- **Что она делает**: Планирует повторное сканирование с дебаунсом в 1 секунду (позволяет скриптам завершить последовательное создание Filter -> Consumer -> Binding).
- **Какие входные данные**: `reason` (`string`): Значение параметра `reason` (string).
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `WmiSubscriptionWatcher.RescanSubscriptionsAsync` (строка 158)
- **Название функции**: `RescanSubscriptionsAsync`
- **Что она делает**: Выполняет дифференциальное сканирование WMI-подписок с оповещением и синхронизацией хранилища.
- **Какие входные данные**: `reason` (`string`): Значение параметра `reason` (string).; `isInitialScan` (`bool`): Значение параметра `isInitialScan` (bool).; `ct` (`CancellationToken`): Значение параметра `ct` (CancellationToken).
- **Какие на выходе**: `Task`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

---

### Файл: `StartupMonitor.Service\Services\Ai\AiRatingService.cs`

#### `AiRatingService.UpdateUiLang` (строка 44)
- **Название функции**: `UpdateUiLang`
- **Что она делает**: UpdateUiLang: Обновляет состояние UiLang.
- **Какие входные данные**: `uiLang` (`string`): Входные данные: значение параметра uiLang (string).
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `AiRatingService.UpdateApiKey` (строка 52)
- **Название функции**: `UpdateApiKey`
- **Что она делает**: UpdateApiKey: Обновляет состояние ApiKey.
- **Какие входные данные**: `apiKey` (`string`): Входные данные: значение параметра apiKey (string).
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `AiRatingService.UpdateConfig` (строка 58)
- **Название функции**: `UpdateConfig`
- **Что она делает**: UpdateConfig: Обновляет состояние Config.
- **Какие входные данные**: `apiKey` (`string`): Входные данные: значение параметра apiKey (string).; `apiUrl` (`string?`): Входные данные: значение параметра apiUrl (string?).; `model` (`string?`): Входные данные: значение параметра model (string?).
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `AiRatingService.IsLocalUrl` (строка 68)
- **Название функции**: `IsLocalUrl`
- **Что она делает**: IsLocalUrl: Проверяет состояние IsLocalUrl.
- **Какие входные данные**: `url` (`string?`): Входные данные: значение параметра url (string?).
- **Какие на выходе**: `bool`: Выходные данные: результирующее значение типа bool.

#### `AiRatingService.GetCached` (строка 79)
- **Название функции**: `GetCached`
- **Что она делает**: GetCached: Возвращает Cached.
- **Какие входные данные**: `entry)` (`StartupEntry`): Входные данные: значение параметра entry) (StartupEntry).
- **Какие на выходе**: `StartupRating?`: Выходные данные: результирующее значение типа StartupRating?.

#### `AiRatingService.PrefetchAsync` (строка 83)
- **Название функции**: `PrefetchAsync`
- **Что она делает**: PrefetchAsync: Выполняет операцию PrefetchAsync.
- **Какие входные данные**: `entry` (`StartupEntry`): Входные данные: значение параметра entry (StartupEntry).
- **Какие на выходе**: `Task`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `AiRatingService.PrefetchAllAsync` (строка 157)
- **Название функции**: `PrefetchAllAsync`
- **Что она делает**: PrefetchAllAsync: Выполняет операцию PrefetchAllAsync.
- **Какие входные данные**: `entries` (`List<StartupEntry>`): Входные данные: значение параметра entries (List<StartupEntry>).
- **Какие на выходе**: `Task`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `AiRatingService.FetchRatingAsync` (строка 240)
- **Название функции**: `FetchRatingAsync`
- **Что она делает**: FetchRatingAsync: Выполняет операцию FetchRatingAsync.
- **Какие входные данные**: `entry` (`StartupEntry`): Входные данные: значение параметра entry (StartupEntry).
- **Какие на выходе**: `Task<StartupRating?>`: Выходные данные: результирующее значение типа Task<StartupRating?>.

#### `AiRatingService.BuildPrompt` (строка 351)
- **Название функции**: `BuildPrompt`
- **Что она делает**: BuildPrompt: Выполняет операцию BuildPrompt.
- **Какие входные данные**: `entry` (`StartupEntry`): Входные данные: значение параметра entry (StartupEntry).
- **Какие на выходе**: `string`: Выходные данные: результирующее значение типа string.

#### `AiRatingService.ParseResponse` (строка 599)
- **Название функции**: `ParseResponse`
- **Что она делает**: ParseResponse: Выполняет операцию ParseResponse.
- **Какие входные данные**: `json` (`string`): Входные данные: значение параметра json (string).; `entryName` (`string`): Входные данные: значение параметра entryName (string).
- **Какие на выходе**: `StartupRating?`: Выходные данные: результирующее значение типа StartupRating?.

#### `AiRatingService.Dispose` (строка 642)
- **Название функции**: `Dispose`
- **Что она делает**: Dispose: Освобождает занятые ресурсы объекта.
- **Какие входные данные**: `)`: Входные данные: значение параметра ) ().
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

---

### Файл: `StartupMonitor.Service\Services\Ai\ProgramLangDetector.cs`

#### `ProgramLangDetector.GetFileVersionInfoSizeW` (строка 14)
- **Название функции**: `GetFileVersionInfoSizeW`
- **Что она делает**: Получает или формирует FileVersionInfoSizeW.
- **Какие входные данные**: `filename` (`string`): Входные данные: значение параметра filename (string).; `handle` (`out int`): Входные данные: значение параметра handle (out int).
- **Какие на выходе**: `extern int`: Выходные данные: результирующее значение типа extern int.

#### `ProgramLangDetector.GetFileVersionInfoW` (строка 23)
- **Название функции**: `GetFileVersionInfoW`
- **Что она делает**: Получает или формирует FileVersionInfoW.
- **Какие входные данные**: `filename` (`string`): Входные данные: значение параметра filename (string).; `handle` (`int`): Входные данные: значение параметра handle (int).; `size` (`int`): Входные данные: значение параметра size (int).; `data` (`byte[]`): Входные данные: значение параметра data (byte[]).
- **Какие на выходе**: `extern bool`: Выходные данные: результирующее значение типа extern bool.

#### `ProgramLangDetector.VerQueryValueW` (строка 32)
- **Название функции**: `VerQueryValueW`
- **Что она делает**: Выполняет операцию VerQueryValueW в рамках подсистемы.
- **Какие входные данные**: `block` (`byte[]`): Входные данные: значение параметра block (byte[]).; `subblock` (`string`): Входные данные: значение параметра subblock (string).; `buffer` (`out IntPtr`): Входные данные: значение параметра buffer (out IntPtr).; `len` (`out int`): Входные данные: значение параметра len (out int).
- **Какие на выходе**: `extern bool`: Выходные данные: результирующее значение типа extern bool.

#### `ProgramLangDetector.Detect` (строка 37)
- **Название функции**: `Detect`
- **Что она делает**: Detect: Выполняет операцию Detect.
- **Какие входные данные**: `filePath` (`string`): Входные данные: значение параметра filePath (string).
- **Какие на выходе**: `string`: Выходные данные: результирующее значение типа string.

---

### Файл: `StartupMonitor.Service\Services\Ai\RatingsCache.cs`

#### `RatingsCache.RatingsCache` (строка 18)
- **Название функции**: `RatingsCache`
- **Что она делает**: Принимает JsonDataStore для сохранения рейтингов в конфиг.
- **Какие входные данные**: `store` (`JsonDataStore`): Значение параметра `store` (JsonDataStore).
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `RatingsCache.Get` (строка 26)
- **Название функции**: `Get`
- **Что она делает**: Возвращает рейтинг из кэша либо из полей записи (AiRating/AiRecommendation).
- **Какие входные данные**: `entry` (`StartupEntry`): Проверяемая запись.
- **Какие на выходе**: `StartupRating?`: StartupRating или null, если рейтинг ещё не вычислен.

#### `RatingsCache.Remove` (строка 52)
- **Название функции**: `Remove`
- **Что она делает**: Удаляет рейтинг из кэша и обнуляет поля Ai* в записи.
- **Какие входные данные**: `entry` (`StartupEntry`): Значение параметра `entry` (StartupEntry).
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `RatingsCache.Set` (строка 65)
- **Название функции**: `Set`
- **Что она делает**: Сохраняет рейтинг в кэш и обновляет поля Ai* в записи.
- **Какие входные данные**: `entry` (`StartupEntry`): Значение параметра `entry` (StartupEntry).; `rating` (`StartupRating`): Значение параметра `rating` (StartupRating).
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `RatingsCache.BuildKey` (строка 80)
- **Название функции**: `BuildKey`
- **Что она делает**: Строит ключ для словаря из Location, Name, FilePath, SHA256.
- **Какие входные данные**: `entry` (`StartupEntry`): Значение параметра `entry` (StartupEntry).
- **Какие на выходе**: `string`: Текстовая строка с результатом операции или сообщением.

---

### Файл: `StartupMonitor.Service\Services\BootTime\BootTimeService.cs`

#### `BootTimeService.CleanPath` (строка 14)
- **Название функции**: `CleanPath`
- **Что она делает**: Нормализует путь: раскрывает переменные среды, убирает лишние кавычки и аргументы.
- **Какие входные данные**: `rawPath` (`string?`): Значение параметра `rawPath` (string?).
- **Какие на выходе**: `string`: Текстовая строка с результатом операции или сообщением.

#### `BootTimeService.BootTimeService` (строка 51)
- **Название функции**: `BootTimeService`
- **Что она делает**: BootTimeService: Выполняет операцию BootTimeService.
- **Какие входные данные**: `store` (`JsonDataStore`): Входные данные: значение параметра store (JsonDataStore).; `logger` (`ILogger<BootTimeService>`): Входные данные: значение параметра logger (ILogger<BootTimeService>).
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `BootTimeService.GetReport` (строка 61)
- **Название функции**: `GetReport`
- **Что она делает**: Возвращает актуальный отчет анализа времени загрузки Windows (с кешированием на 60 секунд).
- **Какие входные данные**: `forceRefresh` (`bool`): Значение параметра `forceRefresh` (bool).
- **Какие на выходе**: `BootTimeReport`: Результирующий объект или экземпляр структуры данных.

#### `BootTimeService.EnrichEntriesWithBootImpact` (строка 128)
- **Название функции**: `EnrichEntriesWithBootImpact`
- **Что она делает**: Обогащает элементы автозапуска метриками замедления старта системы (BootDelayBadge).
- **Какие входные данные**: `entries` (`IEnumerable<StartupEntry>`): Значение параметра `entries` (IEnumerable<StartupEntry>).
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `BootTimeService.LinkOffendersToEntries` (строка 203)
- **Название функции**: `LinkOffendersToEntries`
- **Что она делает**: LinkOffendersToEntries: Выполняет операцию LinkOffendersToEntries.
- **Какие входные данные**: `offenders` (`List<BootDegradationEvent>`): Входные данные: значение параметра offenders (List<BootDegradationEvent>).; `entries` (`List<StartupEntry>`): Входные данные: значение параметра entries (List<StartupEntry>).
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

---

### Файл: `StartupMonitor.Service\Services\BootTime\WindowsDiagnosticsLogReader.cs`

#### `WindowsDiagnosticsLogReader.WindowsDiagnosticsLogReader` (строка 20)
- **Название функции**: `WindowsDiagnosticsLogReader`
- **Что она делает**: WindowsDiagnosticsLogReader: Выполняет операцию WindowsDiagnosticsLogReader.
- **Какие входные данные**: `logger` (`ILogger`): Входные данные: значение параметра logger (ILogger).
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `WindowsDiagnosticsLogReader.ReadBootSessions` (строка 29)
- **Название функции**: `ReadBootSessions`
- **Что она делает**: Считывает историю сессий запуска Windows (Event ID 100).
- **Какие входные данные**: `maxSessions` (`int`): Максимальное количество возвращаемых сессий.
- **Какие на выходе**: `List<BootSessionInfo>`: Коллекция или набор результирующих объектов.

#### `WindowsDiagnosticsLogReader.ReadDegradationEvents` (строка 83)
- **Название функции**: `ReadDegradationEvents`
- **Что она делает**: Считывает события замедления загрузки приложениями, службами и драйверами (Event ID 101–110).
- **Какие входные данные**: `maxEvents` (`int`): Максимальное количество событий.
- **Какие на выходе**: `List<BootDegradationEvent>`: Коллекция или набор результирующих объектов.

#### `WindowsDiagnosticsLogReader.ParseEvent100` (строка 129)
- **Название функции**: `ParseEvent100`
- **Что она делает**: ParseEvent100: Выполняет операцию ParseEvent100.
- **Какие входные данные**: `record` (`EventRecord`): Входные данные: значение параметра record (EventRecord).
- **Какие на выходе**: `BootSessionInfo?`: Выходные данные: результирующее значение типа BootSessionInfo?.

#### `WindowsDiagnosticsLogReader.ParseDegradationEvent` (строка 168)
- **Название функции**: `ParseDegradationEvent`
- **Что она делает**: ParseDegradationEvent: Выполняет операцию ParseDegradationEvent.
- **Какие входные данные**: `record` (`EventRecord`): Входные данные: значение параметра record (EventRecord).
- **Какие на выходе**: `BootDegradationEvent?`: Выходные данные: результирующее значение типа BootDegradationEvent?.

#### `WindowsDiagnosticsLogReader.ParseInt` (строка 220)
- **Название функции**: `ParseInt`
- **Что она делает**: ParseInt: Выполняет операцию ParseInt.
- **Какие входные данные**: `dict` (`Dictionary<string, string>`): Входные данные: значение параметра dict (Dictionary<string, string>).; `key` (`string`): Входные данные: значение параметра key (string).
- **Какие на выходе**: `int`: Выходные данные: результирующее значение типа int.

#### `WindowsDiagnosticsLogReader.ParseLong` (строка 233)
- **Название функции**: `ParseLong`
- **Что она делает**: ParseLong: Выполняет операцию ParseLong.
- **Какие входные данные**: `dict` (`Dictionary<string, string>`): Входные данные: значение параметра dict (Dictionary<string, string>).; `key` (`string`): Входные данные: значение параметра key (string).
- **Какие на выходе**: `long`: Выходные данные: результирующее значение типа long.

---

### Файл: `StartupMonitor.Service\Services\Delayed\DelayedStartupManager.cs`

#### `DelayedStartupManager.StartAsync` (строка 37)
- **Название функции**: `StartAsync`
- **Что она делает**: StartAsync: Выполняет операцию StartAsync.
- **Какие входные данные**: `cancellationToken` (`CancellationToken`): Входные данные: значение параметра cancellationToken (CancellationToken).
- **Какие на выходе**: `Task`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `DelayedStartupManager.StopAsync` (строка 46)
- **Название функции**: `StopAsync`
- **Что она делает**: StopAsync: Выполняет операцию StopAsync.
- **Какие входные данные**: `cancellationToken` (`CancellationToken`): Входные данные: значение параметра cancellationToken (CancellationToken).
- **Какие на выходе**: `Task`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `DelayedStartupManager.ScheduleAllDelayedEntries` (строка 206)
- **Название функции**: `ScheduleAllDelayedEntries`
- **Что она делает**: Инициализирует и запускает таймеры для всех отложенных записей при старте службы.
- **Какие входные данные**: Входные данные отсутствуют (параметры не требуются).
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `DelayedStartupManager.ScheduleEntry` (строка 226)
- **Название функции**: `ScheduleEntry`
- **Что она делает**: ScheduleEntry: Выполняет операцию ScheduleEntry.
- **Какие входные данные**: `entry` (`StartupEntry`): Входные данные: значение параметра entry (StartupEntry).
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `DelayedStartupManager.CancelScheduled` (строка 296)
- **Название функции**: `CancelScheduled`
- **Что она делает**: CancelScheduled: Выполняет операцию CancelScheduled.
- **Какие входные данные**: `entryId` (`string`): Входные данные: значение параметра entryId (string).
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `DelayedStartupManager.CancelAllScheduled` (строка 310)
- **Название функции**: `CancelAllScheduled`
- **Что она делает**: CancelAllScheduled: Выполняет операцию CancelAllScheduled.
- **Какие входные данные**: Входные данные отсутствуют (параметры не требуются).
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

---

### Файл: `StartupMonitor.Service\Services\Delayed\UserSessionLauncher.cs`

#### `UserSessionLauncher.WTSGetActiveConsoleSessionId` (строка 61)
- **Название функции**: `WTSGetActiveConsoleSessionId`
- **Что она делает**: Выполняет операцию WTSGetActiveConsoleSessionId в рамках подсистемы.
- **Какие входные данные**: Входные данные отсутствуют (параметры не требуются).
- **Какие на выходе**: `extern uint`: Выходные данные: результирующее значение типа extern uint.

#### `UserSessionLauncher.WTSQueryUserToken` (строка 68)
- **Название функции**: `WTSQueryUserToken`
- **Что она делает**: Выполняет операцию WTSQueryUserToken в рамках подсистемы.
- **Какие входные данные**: `sessionId` (`uint`): Входные данные: значение параметра sessionId (uint).; `phToken` (`out IntPtr`): Входные данные: значение параметра phToken (out IntPtr).
- **Какие на выходе**: `extern bool`: Выходные данные: результирующее значение типа extern bool.

#### `UserSessionLauncher.CreateEnvironmentBlock` (строка 85)
- **Название функции**: `CreateEnvironmentBlock`
- **Что она делает**: Выполняет операцию CreateEnvironmentBlock в рамках подсистемы.
- **Какие входные данные**: `lpEnvironment` (`out IntPtr`): Входные данные: значение параметра lpEnvironment (out IntPtr).; `hToken` (`IntPtr`): Входные данные: значение параметра hToken (IntPtr).; `bInherit` (`bool`): Входные данные: значение параметра bInherit (bool).
- **Какие на выходе**: `extern bool`: Выходные данные: результирующее значение типа extern bool.

#### `UserSessionLauncher.DestroyEnvironmentBlock` (строка 91)
- **Название функции**: `DestroyEnvironmentBlock`
- **Что она делает**: Выполняет операцию DestroyEnvironmentBlock в рамках подсистемы.
- **Какие входные данные**: `lpEnvironment` (`IntPtr`): Входные данные: значение параметра lpEnvironment (IntPtr).
- **Какие на выходе**: `extern bool`: Выходные данные: результирующее значение типа extern bool.

#### `UserSessionLauncher.CloseHandle` (строка 111)
- **Название функции**: `CloseHandle`
- **Что она делает**: Выполняет операцию CloseHandle в рамках подсистемы.
- **Какие входные данные**: `hObject` (`IntPtr`): Входные данные: значение параметра hObject (IntPtr).
- **Какие на выходе**: `extern bool`: Выходные данные: результирующее значение типа extern bool.

#### `UserSessionLauncher.static` (строка 116)
- **Название функции**: `static`
- **Что она делает**: Разбирает и подготавливает исполняемый путь и командную строку.
- **Какие входные данные**: `ExePath` (`string`): Значение параметра `ExePath` (string).; `FullCommandLine` (`string`): Значение параметра `FullCommandLine` (string).; `filePath` (`string? WorkingDir) PrepareExecutionTarget(string?`): Значение параметра `filePath` (string? WorkingDir) PrepareExecutionTarget(string?).; `commandLine` (`string?`): Значение параметра `commandLine` (string?).
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `UserSessionLauncher.static` (строка 162)
- **Название функции**: `static`
- **Что она делает**: Запускает программу в активной сессии интерактивного пользователя.
- **Какие входные данные**: `Success` (`bool`): Значение параметра `Success` (bool).; `Message` (`string`): Значение параметра `Message` (string).; `filePath` (`int ProcessId) Launch(string?`): Значение параметра `filePath` (int ProcessId) Launch(string?).; `commandLine` (`string?`): Значение параметра `commandLine` (string?).; `logger` (`ILogger?`): Значение параметра `logger` (ILogger?).
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `UserSessionLauncher.static` (строка 229)
- **Название функции**: `static`
- **Что она делает**: Выполняет операцию static в рамках подсистемы.
- **Какие входные данные**: `Success` (`bool`): Значение параметра `Success` (bool).; `Message` (`string`): Значение параметра `Message` (string).; `ProcessId` (`int`): Значение параметра `ProcessId` (int).
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

---

### Файл: `StartupMonitor.Service\Services\Guards\AutoGuardEngine.cs`

#### `AutoGuardEngine.Evaluate` (строка 42)
- **Название функции**: `Evaluate`
- **Что она делает**: Оценивает запись автозапуска на соответствие активным политикам защиты. Возвращает PolicyMatchResult при первом совпадении с активной политикой, либо null.
- **Какие входные данные**: `entry` (`StartupEntry`): Значение параметра `entry` (StartupEntry).; `settings` (`AppSettings`): Значение параметра `settings` (AppSettings).
- **Какие на выходе**: `PolicyMatchResult?`: Результирующий объект или экземпляр структуры данных.

#### `AutoGuardEngine.IsWhitelisted` (строка 127)
- **Название функции**: `IsWhitelisted`
- **Что она делает**: Проверяет, является ли запись безусловно доверенной (системной или собственной).
- **Какие входные данные**: `entry` (`StartupEntry`): Значение параметра `entry` (StartupEntry).
- **Какие на выходе**: `bool`: Логический результат выполнения (`true` в случае успеха или соответствия условию, иначе `false`).

#### `AutoGuardEngine.IsTempOrPublicLocation` (строка 165)
- **Название функции**: `IsTempOrPublicLocation`
- **Что она делает**: Проверяет, указывает ли путь на временный или общедоступный каталог.
- **Какие входные данные**: `path` (`string?`): Значение параметра `path` (string?).
- **Какие на выходе**: `bool`: Логический результат выполнения (`true` в случае успеха или соответствия условию, иначе `false`).

#### `AutoGuardEngine.IsScriptAutostart` (строка 195)
- **Название функции**: `IsScriptAutostart`
- **Что она делает**: Проверяет, представляет ли запись запуск скрипта или интерпретатора.
- **Какие входные данные**: `entry` (`StartupEntry`): Значение параметра `entry` (StartupEntry).
- **Какие на выходе**: `bool`: Логический результат выполнения (`true` в случае успеха или соответствия условию, иначе `false`).

#### `AutoGuardEngine.IsUnsignedOrUntrusted` (строка 241)
- **Название функции**: `IsUnsignedOrUntrusted`
- **Что она делает**: Проверяет, является ли бинарный файл автозапуска неподписанным или с недействительной подписью.
- **Какие входные данные**: `entry` (`StartupEntry`): Значение параметра `entry` (StartupEntry).
- **Какие на выходе**: `bool`: Логический результат выполнения (`true` в случае успеха или соответствия условию, иначе `false`).

---

### Файл: `StartupMonitor.Service\Services\ProcessInspector\ProcessInspectorService.cs`

#### `ProcessInspectorService.InspectProcess` (строка 26)
- **Название функции**: `InspectProcess`
- **Что она делает**: Выполняет детальную инспекцию процесса: список загруженных DLL с верификацией подписей и список открытых дескрипторов.
- **Какие входные данные**: `processId` (`int`): Значение параметра `processId` (int).; `includeHandles` (`bool`): Значение параметра `includeHandles` (bool).
- **Какие на выходе**: `ProcessModulesResponse`: Результирующий объект или экземпляр структуры данных.

#### `ProcessInspectorService.CollectModules` (строка 83)
- **Название функции**: `CollectModules`
- **Что она делает**: CollectModules: Собирает метрики и информацию Modules.
- **Какие входные данные**: `processId` (`int`): Входные данные: значение параметра processId (int).
- **Какие на выходе**: `List<ProcessModuleInfo>`: Выходные данные: результирующее значение типа List<ProcessModuleInfo>.

#### `ProcessInspectorService.CreateModuleInfo` (строка 151)
- **Название функции**: `CreateModuleInfo`
- **Что она делает**: CreateModuleInfo: Выполняет операцию CreateModuleInfo.
- **Какие входные данные**: `moduleName` (`string?`): Входные данные: значение параметра moduleName (string?).; `exePath` (`string?`): Входные данные: значение параметра exePath (string?).; `baseAddress` (`IntPtr`): Входные данные: значение параметра baseAddress (IntPtr).; `moduleSize` (`uint`): Входные данные: значение параметра moduleSize (uint).
- **Какие на выходе**: `ProcessModuleInfo`: Выходные данные: результирующее значение типа ProcessModuleInfo.

#### `ProcessInspectorService.IsPathInTemp` (строка 203)
- **Название функции**: `IsPathInTemp`
- **Что она делает**: IsPathInTemp: Проверяет состояние IsPathInTemp.
- **Какие входные данные**: `path` (`string`): Входные данные: значение параметра path (string).
- **Какие на выходе**: `bool`: Выходные данные: результирующее значение типа bool.

#### `ProcessInspectorService.IsSystemPath` (строка 220)
- **Название функции**: `IsSystemPath`
- **Что она делает**: IsSystemPath: Проверяет состояние IsSystemPath.
- **Какие входные данные**: `path` (`string`): Входные данные: значение параметра path (string).
- **Какие на выходе**: `bool`: Выходные данные: результирующее значение типа bool.

#### `ProcessInspectorService.CollectHandles` (строка 247)
- **Название функции**: `CollectHandles`
- **Что она делает**: CollectHandles: Собирает метрики и информацию Handles.
- **Какие входные данные**: `processId` (`int`): Входные данные: значение параметра processId (int).
- **Какие на выходе**: `List<ProcessHandleInfo>`: Выходные данные: результирующее значение типа List<ProcessHandleInfo>.

#### `ProcessInspectorService.QueryHandleDetails` (строка 337)
- **Название функции**: `QueryHandleDetails`
- **Что она делает**: QueryHandleDetails: Выполняет операцию QueryHandleDetails.
- **Какие входные данные**: `hDup` (`IntPtr`): Входные данные: значение параметра hDup (IntPtr).; `originalHandle` (`IntPtr`): Входные данные: значение параметра originalHandle (IntPtr).; `grantedAccess` (`uint`): Входные данные: значение параметра grantedAccess (uint).
- **Какие на выходе**: `ProcessHandleInfo?`: Выходные данные: результирующее значение типа ProcessHandleInfo?.

#### `ProcessInspectorService.GetObjectTypeName` (строка 400)
- **Название функции**: `GetObjectTypeName`
- **Что она делает**: GetObjectTypeName: Возвращает ObjectTypeName.
- **Какие входные данные**: `handle` (`IntPtr`): Входные данные: значение параметра handle (IntPtr).
- **Какие на выходе**: `string`: Выходные данные: результирующее значение типа string.

#### `ProcessInspectorService.GetObjectName` (строка 429)
- **Название функции**: `GetObjectName`
- **Что она делает**: GetObjectName: Возвращает ObjectName.
- **Какие входные данные**: `handle` (`IntPtr`): Входные данные: значение параметра handle (IntPtr).
- **Какие на выходе**: `string`: Выходные данные: результирующее значение типа string.

#### `ProcessInspectorService.CreateToolhelp32Snapshot` (строка 466)
- **Название функции**: `CreateToolhelp32Snapshot`
- **Что она делает**: Выполняет операцию CreateToolhelp32Snapshot в рамках подсистемы.
- **Какие входные данные**: `dwFlags` (`uint`): Входные данные: значение параметра dwFlags (uint).; `th32ProcessID` (`uint`): Входные данные: значение параметра th32ProcessID (uint).
- **Какие на выходе**: `extern IntPtr`: Выходные данные: результирующее значение типа extern IntPtr.

#### `ProcessInspectorService.Module32First` (строка 473)
- **Название функции**: `Module32First`
- **Что она делает**: Выполняет операцию Module32First в рамках подсистемы.
- **Какие входные данные**: `hSnapshot` (`IntPtr`): Входные данные: значение параметра hSnapshot (IntPtr).; `lpme` (`ref MODULEENTRY32`): Входные данные: значение параметра lpme (ref MODULEENTRY32).
- **Какие на выходе**: `extern bool`: Выходные данные: результирующее значение типа extern bool.

#### `ProcessInspectorService.Module32Next` (строка 480)
- **Название функции**: `Module32Next`
- **Что она делает**: Выполняет операцию Module32Next в рамках подсистемы.
- **Какие входные данные**: `hSnapshot` (`IntPtr`): Входные данные: значение параметра hSnapshot (IntPtr).; `lpme` (`ref MODULEENTRY32`): Входные данные: значение параметра lpme (ref MODULEENTRY32).
- **Какие на выходе**: `extern bool`: Выходные данные: результирующее значение типа extern bool.

#### `ProcessInspectorService.OpenProcess` (строка 488)
- **Название функции**: `OpenProcess`
- **Что она делает**: Выполняет операцию OpenProcess в рамках подсистемы.
- **Какие входные данные**: `dwDesiredAccess` (`uint`): Входные данные: значение параметра dwDesiredAccess (uint).; `bInheritHandle` (`bool`): Входные данные: значение параметра bInheritHandle (bool).; `dwProcessId` (`uint`): Входные данные: значение параметра dwProcessId (uint).
- **Какие на выходе**: `extern IntPtr`: Выходные данные: результирующее значение типа extern IntPtr.

#### `ProcessInspectorService.GetCurrentProcess` (строка 493)
- **Название функции**: `GetCurrentProcess`
- **Что она делает**: Получает или формирует CurrentProcess.
- **Какие входные данные**: Входные данные отсутствуют (параметры не требуются).
- **Какие на выходе**: `extern IntPtr`: Выходные данные: результирующее значение типа extern IntPtr.

#### `ProcessInspectorService.CloseHandle` (строка 511)
- **Название функции**: `CloseHandle`
- **Что она делает**: Выполняет операцию CloseHandle в рамках подсистемы.
- **Какие входные данные**: `hObject` (`IntPtr`): Входные данные: значение параметра hObject (IntPtr).
- **Какие на выходе**: `extern bool`: Выходные данные: результирующее значение типа extern bool.

#### `ProcessInspectorService.GetFileType` (строка 517)
- **Название функции**: `GetFileType`
- **Что она делает**: Получает или формирует FileType.
- **Какие входные данные**: `hFile` (`IntPtr`): Входные данные: значение параметра hFile (IntPtr).
- **Какие на выходе**: `extern uint`: Выходные данные: результирующее значение типа extern uint.

#### `ProcessInspectorService.GetFinalPathNameByHandle` (строка 526)
- **Название функции**: `GetFinalPathNameByHandle`
- **Что она делает**: Получает или формирует FinalPathNameByHandle.
- **Какие входные данные**: `hFile` (`IntPtr`): Входные данные: значение параметра hFile (IntPtr).; `lpszFilePath` (`[Out] StringBuilder`): Входные данные: значение параметра lpszFilePath ([Out] StringBuilder).; `cchFilePath` (`uint`): Входные данные: значение параметра cchFilePath (uint).; `dwFlags` (`uint`): Входные данные: значение параметра dwFlags (uint).
- **Какие на выходе**: `extern uint`: Выходные данные: результирующее значение типа extern uint.

#### `ProcessInspectorService.NtQuerySystemInformation` (строка 535)
- **Название функции**: `NtQuerySystemInformation`
- **Что она делает**: Выполняет операцию NtQuerySystemInformation в рамках подсистемы.
- **Какие входные данные**: `query` (`int`): Входные данные: значение параметра query (int).; `dataPtr` (`IntPtr`): Входные данные: значение параметра dataPtr (IntPtr).; `size` (`uint`): Входные данные: значение параметра size (uint).; `returnedSize` (`out uint`): Входные данные: значение параметра returnedSize (out uint).
- **Какие на выходе**: `extern uint`: Выходные данные: результирующее значение типа extern uint.

#### `ProcessInspectorService.NtQueryObject` (строка 545)
- **Название функции**: `NtQueryObject`
- **Что она делает**: Выполняет операцию NtQueryObject в рамках подсистемы.
- **Какие входные данные**: `handle` (`IntPtr`): Входные данные: значение параметра handle (IntPtr).; `query` (`int`): Входные данные: значение параметра query (int).; `dataPtr` (`IntPtr`): Входные данные: значение параметра dataPtr (IntPtr).; `size` (`uint`): Входные данные: значение параметра size (uint).; `returnedSize` (`out uint`): Входные данные: значение параметра returnedSize (out uint).
- **Какие на выходе**: `extern uint`: Выходные данные: результирующее значение типа extern uint.

---

### Файл: `StartupMonitor.Service\Services\Remote\RemoteNotificationService.cs`

#### `RemoteNotificationService.NotifyNewEntryAsync` (строка 50)
- **Название функции**: `NotifyNewEntryAsync`
- **Что она делает**: Уведомляет о добавлении новой записи автозапуска.
- **Какие входные данные**: `entry` (`StartupEntry`): Значение параметра `entry` (StartupEntry).
- **Какие на выходе**: `Task`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `RemoteNotificationService.NotifyThreatAsync` (строка 119)
- **Название функции**: `NotifyThreatAsync`
- **Что она делает**: Уведомляет об обнаружении подозрительной команды (CLI Threat Heuristics).
- **Какие входные данные**: `entry` (`StartupEntry`): Значение параметра `entry` (StartupEntry).; `threat` (`CliThreatInfo`): Значение параметра `threat` (CliThreatInfo).
- **Какие на выходе**: `Task`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `RemoteNotificationService.NotifyBlockedAsync` (строка 183)
- **Название функции**: `NotifyBlockedAsync`
- **Что она делает**: Уведомляет о блокировке записи Чёрным списком или режимом изоляции Lockdown.
- **Какие входные данные**: `entry` (`StartupEntry`): Значение параметра `entry` (StartupEntry).; `reason` (`string`): Значение параметра `reason` (string).
- **Какие на выходе**: `Task`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `RemoteNotificationService.SendTelegramAsync` (строка 363)
- **Название функции**: `SendTelegramAsync`
- **Что она делает**: SendTelegramAsync: Выполняет операцию SendTelegramAsync.
- **Какие входные данные**: `token` (`string`): Входные данные: значение параметра token (string).; `chatId` (`string`): Входные данные: значение параметра chatId (string).; `html` (`string`): Входные данные: значение параметра html (string).
- **Какие на выходе**: `Task`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `RemoteNotificationService.SendDiscordAsync` (строка 392)
- **Название функции**: `SendDiscordAsync`
- **Что она делает**: SendDiscordAsync: Выполняет операцию SendDiscordAsync.
- **Какие входные данные**: `webhookUrl` (`string`): Входные данные: значение параметра webhookUrl (string).; `payload` (`DiscordPayload`): Входные данные: значение параметра payload (DiscordPayload).
- **Какие на выходе**: `Task`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `RemoteNotificationService.EscapeHtml` (строка 413)
- **Название функции**: `EscapeHtml`
- **Что она делает**: EscapeHtml: Выполняет операцию EscapeHtml.
- **Какие входные данные**: `text` (`string?`): Входные данные: значение параметра text (string?).
- **Какие на выходе**: `string`: Выходные данные: результирующее значение типа string.

#### `RemoteNotificationService.Dispose` (строка 423)
- **Название функции**: `Dispose`
- **Что она делает**: Dispose: Освобождает занятые ресурсы объекта.
- **Какие входные данные**: Входные данные отсутствуют (параметры не требуются).
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

---

### Файл: `StartupMonitor.Service\Services\VirusScan\HashLookupService.cs`

#### `HashLookupService.HashLookupService` (строка 29)
- **Название функции**: `HashLookupService`
- **Что она делает**: Принимает хранилище конфига и логгер.
- **Какие входные данные**: `store` (`JsonDataStore`): Значение параметра `store` (JsonDataStore).; `logger` (`ILogger<HashLookupService>`): Значение параметра `logger` (ILogger<HashLookupService>).
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `HashLookupService.ScanEntryAsync` (строка 41)
- **Название функции**: `ScanEntryAsync`
- **Что она делает**: Проверяет одну запись через CIRCL hashlookup. Если SHA256 нет — пытается вычислить через ResolveAndEnrichEntry.
- **Какие входные данные**: `entry` (`StartupEntry`): Проверяемая запись.; `ct` (`CancellationToken`): Токен отмены.
- **Какие на выходе**: `Task<bool>`: true — запрос к CIRCL выполнен; false — нет хеша (помечен Grey/NoHash).

#### `HashLookupService.ScanAllAsync` (строка 81)
- **Название функции**: `ScanAllAsync`
- **Что она делает**: Проверяет все записи без дублей в сессии (через _sessionScanned).
- **Какие входные данные**: `entries` (`List<StartupEntry>`): Значение параметра `entries` (List<StartupEntry>).; `ct` (`CancellationToken`): Значение параметра `ct` (CancellationToken).
- **Какие на выходе**: `Task`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `HashLookupService.static` (строка 169)
- **Название функции**: `static`
- **Что она делает**: Парсит ответ CIRCL: извлекает Source, db, FileName.
- **Какие входные данные**: `zone` (`string`): Значение параметра `zone` (string).; `detection` (`string`): Значение параметра `detection` (string).; `json` (`string status) ParseResponse(string`): Значение параметра `json` (string status) ParseResponse(string).
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

---

### Файл: `StartupMonitor.Service\Services\VirusScan\KasperskyScanService.cs`

#### `KasperskyScanService.KasperskyScanService` (строка 28)
- **Название функции**: `KasperskyScanService`
- **Что она делает**: KasperskyScanService: Выполняет операцию KasperskyScanService.
- **Какие входные данные**: `store` (`JsonDataStore`): Входные данные: значение параметра store (JsonDataStore).; `logger` (`ILogger<KasperskyScanService>`): Входные данные: значение параметра logger (ILogger<KasperskyScanService>).; `httpClient` (`HttpClient?`): Входные данные: значение параметра httpClient (HttpClient?).
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `KasperskyScanService.TestConnectionAsync` (строка 38)
- **Название функции**: `TestConnectionAsync`
- **Что она делает**: Проверяет подключение к API Kaspersky OpenTIP с переданным токеном.
- **Какие входные данные**: `apiKey` (`string`): Значение параметра `apiKey` (string).; `apiUrl` (`string?`): Значение параметра `apiUrl` (string?).; `ct` (`CancellationToken`): Значение параметра `ct` (CancellationToken).
- **Какие на выходе**: `Task<KasperskyTestResult>`: Результирующий объект или экземпляр структуры данных.

#### `KasperskyScanService.ScanEntryAsync` (строка 114)
- **Название функции**: `ScanEntryAsync`
- **Что она делает**: Проверяет отдельную запись автозапуска через Kaspersky OpenTIP.
- **Какие входные данные**: `entry` (`StartupEntry`): Значение параметра `entry` (StartupEntry).; `ct` (`CancellationToken`): Значение параметра `ct` (CancellationToken).
- **Какие на выходе**: `Task<bool>`: Логический результат выполнения (`true` в случае успеха или соответствия условию, иначе `false`).

#### `KasperskyScanService.ScanAllAsync` (строка 186)
- **Название функции**: `ScanAllAsync`
- **Что она делает**: Проверяет список записей автозапуска с соблюдением интервалов и предотвращением повторных запросов.
- **Какие входные данные**: `entries` (`List<StartupEntry>`): Значение параметра `entries` (List<StartupEntry>).; `ct` (`CancellationToken`): Значение параметра `ct` (CancellationToken).
- **Какие на выходе**: `Task`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `KasperskyScanService.QueryKasperskyHashAsync` (строка 263)
- **Название функции**: `QueryKasperskyHashAsync`
- **Что она делает**: Выполняет GET-запрос к Kaspersky OpenTIP API по SHA256.
- **Какие входные данные**: `sha256` (`string`): Значение параметра `sha256` (string).; `apiKey` (`string`): Значение параметра `apiKey` (string).; `apiUrl` (`string`): Значение параметра `apiUrl` (string).; `ct` (`CancellationToken`): Значение параметра `ct` (CancellationToken).
- **Какие на выходе**: `Task<KasperskyResult?>`: Результирующий объект или экземпляр структуры данных.

#### `KasperskyScanService.ParseKasperskyResponse` (строка 304)
- **Название функции**: `ParseKasperskyResponse`
- **Что она делает**: Парсит JSON-ответ Kaspersky OpenTIP.
- **Какие входные данные**: `json` (`string`): Значение параметра `json` (string).
- **Какие на выходе**: `KasperskyResult`: Результирующий объект или экземпляр структуры данных.

---

### Файл: `StartupMonitor.Service\Services\Wmi\WmiSubscriptionHelper.cs`

#### `WmiSubscriptionHelper.WmiDisablePayload` (строка 64)
- **Название функции**: `WmiDisablePayload`
- **Что она делает**: DTO для отключения подписки WMI (сохранение пути привязки в StartupEntry.DisablePayload).
- **Какие входные данные**: `FilterPath` (`string`): Значение параметра `FilterPath` (string).; `ConsumerPath` (`string`): Значение параметра `ConsumerPath` (string).
- **Какие на выходе**: `record`: Результирующий объект или экземпляр структуры данных.

#### `WmiSubscriptionHelper.ScanSubscriptions` (строка 83)
- **Название функции**: `ScanSubscriptions`
- **Что она делает**: Сканирует пространство имён root\subscription и возвращает список всех обнаруженных подписок на события.
- **Какие входные данные**: Входные данные отсутствуют (параметры не требуются).
- **Какие на выходе**: `List<StartupEntry>`: Коллекция или набор результирующих объектов.

#### `WmiSubscriptionHelper.static` (строка 296)
- **Название функции**: `static`
- **Что она делает**: Удаляет подписку WMI из системы (привязку, консьюмер и фильтр) с созданием полного снимка для восстановления.
- **Какие входные данные**: `success` (`bool`): Значение параметра `success` (bool).; `error` (`string?`): Значение параметра `error` (string?).; `entry` (`string? payload) RemoveSubscription(StartupEntry`): Значение параметра `entry` (string? payload) RemoveSubscription(StartupEntry).
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `WmiSubscriptionHelper.static` (строка 402)
- **Название функции**: `static`
- **Что она делает**: Восстанавливает ранее удалённую WMI-подписку из JSON-пейлоада.
- **Какие входные данные**: `success` (`bool`): Значение параметра `success` (bool).; `payloadJson` (`string? error) RestoreSubscription(string?`): Значение параметра `payloadJson` (string? error) RestoreSubscription(string?).
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `WmiSubscriptionHelper.static` (строка 490)
- **Название функции**: `static`
- **Что она делает**: Отключает WMI-подписку (деактивирует __FilterToConsumerBinding без удаления кода/консьюмера).
- **Какие входные данные**: `success` (`bool`): Значение параметра `success` (bool).; `entry` (`string? error) DisableSubscription(StartupEntry`): Значение параметра `entry` (string? error) DisableSubscription(StartupEntry).
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `WmiSubscriptionHelper.static` (строка 547)
- **Название функции**: `static`
- **Что она делает**: Включает ранее отключённую WMI-подписку (восстанавливает __FilterToConsumerBinding).
- **Какие входные данные**: `success` (`bool`): Значение параметра `success` (bool).; `entry` (`string? error) EnableSubscription(StartupEntry`): Значение параметра `entry` (string? error) EnableSubscription(StartupEntry).
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `WmiSubscriptionHelper.ExtractNameFromWmiRef` (строка 602)
- **Название функции**: `ExtractNameFromWmiRef`
- **Что она делает**: Извлекает имя объекта из ссылки WMI (например `__EventFilter.Name="MyFilter"` -> `MyFilter`).
- **Какие входные данные**: `wmiRef` (`string`): Значение параметра `wmiRef` (string).
- **Какие на выходе**: `string`: Текстовая строка с результатом операции или сообщением.

---

## Проект: StartupMonitor.UI

### Файл: `StartupMonitor.UI\App.xaml.cs`

#### `App.xaml.Log` (строка 37)
- **Название функции**: `Log`
- **Что она делает**: Записывает сообщение в лог-файл с автоматической ротацией при превышении 1 МБ.
- **Какие входные данные**: `msg` (`string`): Значение параметра `msg` (string).
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `App.xaml.OnStartup` (строка 56)
- **Название функции**: `OnStartup`
- **Что она делает**: Обрабатывает запуск приложения: проверяет единственный экземпляр, настраивает тему, создаёт иконку трея.
- **Какие входные данные**: `e` (`StartupEventArgs`): Значение параметра `e` (StartupEventArgs).
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `App.xaml.FindWindow` (строка 193)
- **Название функции**: `FindWindow`
- **Что она делает**: Выполняет поиск соответствующего элемента по переданным критериям.
- **Какие входные данные**: `lpClassName` (`string?`): Входные данные: значение параметра lpClassName (string?).; `lpWindowName` (`string?`): Входные данные: значение параметра lpWindowName (string?).
- **Какие на выходе**: `extern IntPtr`: Выходные данные: результирующее значение типа extern IntPtr.

#### `App.xaml.SetForegroundWindow` (строка 199)
- **Название функции**: `SetForegroundWindow`
- **Что она делает**: Устанавливает значение для ForegroundWindow.
- **Какие входные данные**: `hWnd` (`IntPtr`): Входные данные: значение параметра hWnd (IntPtr).
- **Какие на выходе**: `extern bool`: Выходные данные: результирующее значение типа extern bool.

#### `App.xaml.ShowWindow` (строка 206)
- **Название функции**: `ShowWindow`
- **Что она делает**: Выполняет операцию ShowWindow в рамках подсистемы.
- **Какие входные данные**: `hWnd` (`IntPtr`): Входные данные: значение параметра hWnd (IntPtr).; `nCmdShow` (`int`): Входные данные: значение параметра nCmdShow (int).
- **Какие на выходе**: `extern bool`: Выходные данные: результирующее значение типа extern bool.

#### `App.xaml.UpdateTrayMonitoringState` (строка 290)
- **Название функции**: `UpdateTrayMonitoringState`
- **Что она делает**: Обновляет состояние иконки трея (цветная или серая при паузе), тултип и пункт меню.
- **Какие входные данные**: `isPaused` (`bool`): Значение параметра `isPaused` (bool).
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `App.xaml.ToggleWindow` (строка 308)
- **Название функции**: `ToggleWindow`
- **Что она делает**: Переключает видимость главного окна (показать/скрыть).
- **Какие входные данные**: Входные данные отсутствуют (параметры не требуются).
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `App.xaml.RestoreMainWindow` (строка 322)
- **Название функции**: `RestoreMainWindow`
- **Что она делает**: Восстанавливает главное окно из трея (с сохранением состояния Maximized/Normal).
- **Какие входные данные**: Входные данные отсутствуют (параметры не требуются).
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `App.xaml.ActivateOtherInstance` (строка 339)
- **Название функции**: `ActivateOtherInstance`
- **Что она делает**: Активирует окно другого экземпляра приложения.
- **Какие входные данные**: Входные данные отсутствуют (параметры не требуются).
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `App.xaml.ShowTrayBalloon` (строка 350)
- **Название функции**: `ShowTrayBalloon`
- **Что она делает**: Показывает всплывающее уведомление в трее через агрегатор с группировкой однородных событий.
- **Какие входные данные**: `title` (`string`): Значение параметра `title` (string).; `text` (`string`): Значение параметра `text` (string).; `entryId` (`string?`): Значение параметра `entryId` (string?).; `kind` (`TrayNotificationKind`): Значение параметра `kind` (TrayNotificationKind).
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `App.xaml.OnExit` (строка 365)
- **Название функции**: `OnExit`
- **Что она делает**: Очищает ресурсы при завершении приложения.
- **Какие входные данные**: `e` (`ExitEventArgs`): Значение параметра `e` (ExitEventArgs).
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `App.xaml.IsWindowsThemeLight` (строка 380)
- **Название функции**: `IsWindowsThemeLight`
- **Что она делает**: Определяет, используется ли светлая тема в Windows.
- **Какие входные данные**: `)`: Значение параметра `)`.
- **Какие на выходе**: `bool`: Логический результат выполнения (`true` в случае успеха или соответствия условию, иначе `false`).

#### `App.xaml.IsSystemThemeLight` (строка 383)
- **Название функции**: `IsSystemThemeLight`
- **Что она делает**: IsSystemThemeLight: Проверяет состояние IsSystemThemeLight.
- **Какие входные данные**: Входные данные отсутствуют (параметры не требуются).
- **Какие на выходе**: `bool`: Выходные данные: результирующее значение типа bool.

#### `App.xaml.LoadSvgIcon` (строка 397)
- **Название функции**: `LoadSvgIcon`
- **Что она делает**: Загружает векторную иконку щита с галочкой (логотип программы) для системного трея.
- **Какие входные данные**: `isPaused` (`bool`): Если true, формирует спокойную монохромную серую иконку.
- **Какие на выходе**: `ImageSource`: Результирующий объект или экземпляр структуры данных.

#### `App.xaml.Point` (строка 413)
- **Название функции**: `Point`
- **Что она делает**: Point: Выполняет операцию Point.
- **Какие входные данные**: `0`: Входные данные: значение параметра 0 ().; `0`: Входные данные: значение параметра 0 ().
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `App.xaml.Point` (строка 417)
- **Название функции**: `Point`
- **Что она делает**: Point: Выполняет операцию Point.
- **Какие входные данные**: `1`: Входные данные: значение параметра 1 ().; `1)`: Входные данные: значение параметра 1) ().
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `App.xaml.Point` (строка 428)
- **Название функции**: `Point`
- **Что она делает**: Point: Выполняет операцию Point.
- **Какие входные данные**: `0`: Входные данные: значение параметра 0 ().; `0`: Входные данные: значение параметра 0 ().
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `App.xaml.Point` (строка 432)
- **Название функции**: `Point`
- **Что она делает**: Point: Выполняет операцию Point.
- **Какие входные данные**: `1`: Входные данные: значение параметра 1 ().; `1)`: Входные данные: значение параметра 1) ().
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

---

### Файл: `StartupMonitor.UI\Converters\DesignHelperConverters.cs`

#### `DesignHelperConverters.Convert` (строка 19)
- **Название функции**: `Convert`
- **Что она делает**: Convert: Выполняет преобразование данных .
- **Какие входные данные**: `value` (`object?`): Входные данные: значение параметра value (object?).; `targetType` (`Type`): Входные данные: значение параметра targetType (Type).; `parameter` (`object?`): Входные данные: значение параметра parameter (object?).; `culture` (`CultureInfo`): Входные данные: значение параметра culture (CultureInfo).
- **Какие на выходе**: `object`: Выходные данные: результирующее значение типа object.

#### `DesignHelperConverters.ConvertBack` (строка 35)
- **Название функции**: `ConvertBack`
- **Что она делает**: ConvertBack: Выполняет преобразование данных Back.
- **Какие входные данные**: `value` (`object?`): Входные данные: значение параметра value (object?).; `targetType` (`Type`): Входные данные: значение параметра targetType (Type).; `parameter` (`object?`): Входные данные: значение параметра parameter (object?).; `culture` (`CultureInfo`): Входные данные: значение параметра culture (CultureInfo).
- **Какие на выходе**: `object`: Выходные данные: результирующее значение типа object.

#### `DesignHelperConverters.Convert` (строка 48)
- **Название функции**: `Convert`
- **Что она делает**: Convert: Выполняет преобразование данных .
- **Какие входные данные**: `value` (`object?`): Входные данные: значение параметра value (object?).; `targetType` (`Type`): Входные данные: значение параметра targetType (Type).; `parameter` (`object?`): Входные данные: значение параметра parameter (object?).; `culture` (`CultureInfo`): Входные данные: значение параметра culture (CultureInfo).
- **Какие на выходе**: `object`: Выходные данные: результирующее значение типа object.

#### `DesignHelperConverters.ConvertBack` (строка 70)
- **Название функции**: `ConvertBack`
- **Что она делает**: ConvertBack: Выполняет преобразование данных Back.
- **Какие входные данные**: `value` (`object?`): Входные данные: значение параметра value (object?).; `targetType` (`Type`): Входные данные: значение параметра targetType (Type).; `parameter` (`object?`): Входные данные: значение параметра parameter (object?).; `culture` (`CultureInfo`): Входные данные: значение параметра culture (CultureInfo).
- **Какие на выходе**: `object`: Выходные данные: результирующее значение типа object.

#### `DesignHelperConverters.Convert` (строка 83)
- **Название функции**: `Convert`
- **Что она делает**: Convert: Выполняет преобразование данных .
- **Какие входные данные**: `value` (`object?`): Входные данные: значение параметра value (object?).; `targetType` (`Type`): Входные данные: значение параметра targetType (Type).; `parameter` (`object?`): Входные данные: значение параметра parameter (object?).; `culture` (`CultureInfo`): Входные данные: значение параметра culture (CultureInfo).
- **Какие на выходе**: `object`: Выходные данные: результирующее значение типа object.

#### `DesignHelperConverters.ConvertBack` (строка 108)
- **Название функции**: `ConvertBack`
- **Что она делает**: ConvertBack: Выполняет преобразование данных Back.
- **Какие входные данные**: `value` (`object?`): Входные данные: значение параметра value (object?).; `targetType` (`Type`): Входные данные: значение параметра targetType (Type).; `parameter` (`object?`): Входные данные: значение параметра parameter (object?).; `culture` (`CultureInfo`): Входные данные: значение параметра culture (CultureInfo).
- **Какие на выходе**: `object`: Выходные данные: результирующее значение типа object.

#### `DesignHelperConverters.Convert` (строка 121)
- **Название функции**: `Convert`
- **Что она делает**: Convert: Выполняет преобразование данных .
- **Какие входные данные**: `value` (`object?`): Входные данные: значение параметра value (object?).; `targetType` (`Type`): Входные данные: значение параметра targetType (Type).; `parameter` (`object?`): Входные данные: значение параметра parameter (object?).; `culture` (`CultureInfo`): Входные данные: значение параметра culture (CultureInfo).
- **Какие на выходе**: `object`: Выходные данные: результирующее значение типа object.

#### `DesignHelperConverters.ConvertBack` (строка 146)
- **Название функции**: `ConvertBack`
- **Что она делает**: ConvertBack: Выполняет преобразование данных Back.
- **Какие входные данные**: `value` (`object?`): Входные данные: значение параметра value (object?).; `targetType` (`Type`): Входные данные: значение параметра targetType (Type).; `parameter` (`object?`): Входные данные: значение параметра parameter (object?).; `culture` (`CultureInfo`): Входные данные: значение параметра culture (CultureInfo).
- **Какие на выходе**: `object`: Выходные данные: результирующее значение типа object.

#### `DesignHelperConverters.Convert` (строка 159)
- **Название функции**: `Convert`
- **Что она делает**: Convert: Выполняет преобразование данных .
- **Какие входные данные**: `value` (`object?`): Входные данные: значение параметра value (object?).; `targetType` (`Type`): Входные данные: значение параметра targetType (Type).; `parameter` (`object?`): Входные данные: значение параметра parameter (object?).; `culture` (`CultureInfo`): Входные данные: значение параметра culture (CultureInfo).
- **Какие на выходе**: `object`: Выходные данные: результирующее значение типа object.

#### `DesignHelperConverters.ConvertBack` (строка 184)
- **Название функции**: `ConvertBack`
- **Что она делает**: ConvertBack: Выполняет преобразование данных Back.
- **Какие входные данные**: `value` (`object?`): Входные данные: значение параметра value (object?).; `targetType` (`Type`): Входные данные: значение параметра targetType (Type).; `parameter` (`object?`): Входные данные: значение параметра parameter (object?).; `culture` (`CultureInfo`): Входные данные: значение параметра culture (CultureInfo).
- **Какие на выходе**: `object`: Выходные данные: результирующее значение типа object.

#### `DesignHelperConverters.GetOrCreate` (строка 203)
- **Название функции**: `GetOrCreate`
- **Что она делает**: GetOrCreate: Возвращает OrCreate.
- **Какие входные данные**: `key` (`string`): Входные данные: значение параметра key (string).; `colorFactory` (`Func<Color>`): Входные данные: значение параметра colorFactory (Func<Color>).
- **Какие на выходе**: `SolidColorBrush`: Выходные данные: результирующее значение типа SolidColorBrush.

#### `DesignHelperConverters.Convert` (строка 229)
- **Название функции**: `Convert`
- **Что она делает**: Convert: Выполняет преобразование данных .
- **Какие входные данные**: `value` (`object?`): Входные данные: значение параметра value (object?).; `targetType` (`Type`): Входные данные: значение параметра targetType (Type).; `parameter` (`object?`): Входные данные: значение параметра parameter (object?).; `culture` (`CultureInfo`): Входные данные: значение параметра culture (CultureInfo).
- **Какие на выходе**: `object`: Выходные данные: результирующее значение типа object.

#### `DesignHelperConverters.ConvertBack` (строка 238)
- **Название функции**: `ConvertBack`
- **Что она делает**: ConvertBack: Выполняет преобразование данных Back.
- **Какие входные данные**: `value` (`object?`): Входные данные: значение параметра value (object?).; `targetType` (`Type`): Входные данные: значение параметра targetType (Type).; `parameter` (`object?`): Входные данные: значение параметра parameter (object?).; `culture` (`CultureInfo`): Входные данные: значение параметра culture (CultureInfo).
- **Какие на выходе**: `object`: Выходные данные: результирующее значение типа object.

#### `DesignHelperConverters.Convert` (строка 251)
- **Название функции**: `Convert`
- **Что она делает**: Convert: Выполняет преобразование данных .
- **Какие входные данные**: `value` (`object?`): Входные данные: значение параметра value (object?).; `targetType` (`Type`): Входные данные: значение параметра targetType (Type).; `parameter` (`object?`): Входные данные: значение параметра parameter (object?).; `culture` (`CultureInfo`): Входные данные: значение параметра culture (CultureInfo).
- **Какие на выходе**: `object`: Выходные данные: результирующее значение типа object.

#### `DesignHelperConverters.ConvertCore` (строка 258)
- **Название функции**: `ConvertCore`
- **Что она делает**: ConvertCore: Выполняет преобразование данных Core.
- **Какие входные данные**: `hex` (`string?`): Входные данные: значение параметра hex (string?).; `parameter` (`object?`): Входные данные: значение параметра parameter (object?).
- **Какие на выходе**: `object`: Выходные данные: результирующее значение типа object.

#### `DesignHelperConverters.ConvertBack` (строка 301)
- **Название функции**: `ConvertBack`
- **Что она делает**: ConvertBack: Выполняет преобразование данных Back.
- **Какие входные данные**: `value` (`object?`): Входные данные: значение параметра value (object?).; `targetType` (`Type`): Входные данные: значение параметра targetType (Type).; `parameter` (`object?`): Входные данные: значение параметра parameter (object?).; `culture` (`CultureInfo`): Входные данные: значение параметра culture (CultureInfo).
- **Какие на выходе**: `object`: Выходные данные: результирующее значение типа object.

---

### Файл: `StartupMonitor.UI\Converters\FilePathToIconConverter.cs`

#### `FilePathToIconConverter.Convert` (строка 19)
- **Название функции**: `Convert`
- **Что она делает**: Convert: Выполняет преобразование данных .
- **Какие входные данные**: `value` (`object?`): Входные данные: значение параметра value (object?).; `targetType` (`Type`): Входные данные: значение параметра targetType (Type).; `parameter` (`object?`): Входные данные: значение параметра parameter (object?).; `culture` (`CultureInfo`): Входные данные: значение параметра culture (CultureInfo).
- **Какие на выходе**: `object?`: Выходные данные: результирующее значение типа object?.

#### `FilePathToIconConverter.ConvertBack` (строка 34)
- **Название функции**: `ConvertBack`
- **Что она делает**: ConvertBack: Выполняет преобразование данных Back.
- **Какие входные данные**: `value` (`object?`): Входные данные: значение параметра value (object?).; `targetType` (`Type`): Входные данные: значение параметра targetType (Type).; `parameter` (`object?`): Входные данные: значение параметра parameter (object?).; `culture` (`CultureInfo`): Входные данные: значение параметра culture (CultureInfo).
- **Какие на выходе**: `object`: Выходные данные: результирующее значение типа object.

---

### Файл: `StartupMonitor.UI\Converters\FilterToAppearanceConverter.cs`

#### `FilterToAppearanceConverter.Convert` (строка 19)
- **Название функции**: `Convert`
- **Что она делает**: Convert: Выполняет преобразование данных .
- **Какие входные данные**: `value` (`object`): Входные данные: значение параметра value (object).; `targetType` (`Type`): Входные данные: значение параметра targetType (Type).; `parameter` (`object`): Входные данные: значение параметра parameter (object).; `culture` (`CultureInfo`): Входные данные: значение параметра culture (CultureInfo).
- **Какие на выходе**: `object`: Выходные данные: результирующее значение типа object.

#### `FilterToAppearanceConverter.ConvertBack` (строка 33)
- **Название функции**: `ConvertBack`
- **Что она делает**: ConvertBack: Выполняет преобразование данных Back.
- **Какие входные данные**: `value` (`object`): Входные данные: значение параметра value (object).; `targetType` (`Type`): Входные данные: значение параметра targetType (Type).; `parameter` (`object`): Входные данные: значение параметра parameter (object).; `culture` (`CultureInfo`): Входные данные: значение параметра culture (CultureInfo).
- **Какие на выходе**: `object`: Выходные данные: результирующее значение типа object.

---

### Файл: `StartupMonitor.UI\Converters\InverseBoolConverter.cs`

#### `InverseBoolConverter.Convert` (строка 18)
- **Название функции**: `Convert`
- **Что она делает**: Convert: Выполняет преобразование данных .
- **Какие входные данные**: `value` (`object?`): Входные данные: значение параметра value (object?).; `targetType` (`Type`): Входные данные: значение параметра targetType (Type).; `parameter` (`object?`): Входные данные: значение параметра parameter (object?).; `culture` (`CultureInfo`): Входные данные: значение параметра culture (CultureInfo).
- **Какие на выходе**: `object`: Выходные данные: результирующее значение типа object.

#### `InverseBoolConverter.ConvertBack` (строка 31)
- **Название функции**: `ConvertBack`
- **Что она делает**: ConvertBack: Выполняет преобразование данных Back.
- **Какие входные данные**: `value` (`object?`): Входные данные: значение параметра value (object?).; `targetType` (`Type`): Входные данные: значение параметра targetType (Type).; `parameter` (`object?`): Входные данные: значение параметра parameter (object?).; `culture` (`CultureInfo`): Входные данные: значение параметра culture (CultureInfo).
- **Какие на выходе**: `object`: Выходные данные: результирующее значение типа object.

---

### Файл: `StartupMonitor.UI\Converters\KasperskyIndicatorConverter.cs`

#### `KasperskyIndicatorConverter.Convert` (строка 20)
- **Название функции**: `Convert`
- **Что она делает**: Convert: Выполняет преобразование данных .
- **Какие входные данные**: `value` (`object?`): Входные данные: значение параметра value (object?).; `targetType` (`Type`): Входные данные: значение параметра targetType (Type).; `parameter` (`object?`): Входные данные: значение параметра parameter (object?).; `culture` (`CultureInfo`): Входные данные: значение параметра culture (CultureInfo).
- **Какие на выходе**: `object`: Выходные данные: результирующее значение типа object.

#### `KasperskyIndicatorConverter.ConvertBack` (строка 40)
- **Название функции**: `ConvertBack`
- **Что она делает**: ConvertBack: Выполняет преобразование данных Back.
- **Какие входные данные**: `value` (`object?`): Входные данные: значение параметра value (object?).; `targetType` (`Type`): Входные данные: значение параметра targetType (Type).; `parameter` (`object?`): Входные данные: значение параметра parameter (object?).; `culture` (`CultureInfo`): Входные данные: значение параметра culture (CultureInfo).
- **Какие на выходе**: `object`: Выходные данные: результирующее значение типа object.

#### `KasperskyIndicatorConverter.CreateFrozen` (строка 63)
- **Название функции**: `CreateFrozen`
- **Что она делает**: CreateFrozen: Выполняет операцию CreateFrozen.
- **Какие входные данные**: `r` (`byte`): Входные данные: значение параметра r (byte).; `g` (`byte`): Входные данные: значение параметра g (byte).; `b` (`byte`): Входные данные: значение параметра b (byte).
- **Какие на выходе**: `SolidColorBrush`: Выходные данные: результирующее значение типа SolidColorBrush.

#### `KasperskyIndicatorConverter.Convert` (строка 77)
- **Название функции**: `Convert`
- **Что она делает**: Convert: Выполняет преобразование данных .
- **Какие входные данные**: `value` (`object?`): Входные данные: значение параметра value (object?).; `targetType` (`Type`): Входные данные: значение параметра targetType (Type).; `parameter` (`object?`): Входные данные: значение параметра parameter (object?).; `culture` (`CultureInfo`): Входные данные: значение параметра culture (CultureInfo).
- **Какие на выходе**: `object`: Выходные данные: результирующее значение типа object.

#### `KasperskyIndicatorConverter.ConvertBack` (строка 97)
- **Название функции**: `ConvertBack`
- **Что она делает**: ConvertBack: Выполняет преобразование данных Back.
- **Какие входные данные**: `value` (`object?`): Входные данные: значение параметра value (object?).; `targetType` (`Type`): Входные данные: значение параметра targetType (Type).; `parameter` (`object?`): Входные данные: значение параметра parameter (object?).; `culture` (`CultureInfo`): Входные данные: значение параметра culture (CultureInfo).
- **Какие на выходе**: `object`: Выходные данные: результирующее значение типа object.

---

### Файл: `StartupMonitor.UI\Converters\LocationToDisplayConverter.cs`

#### `LocationToDisplayConverter.Convert` (строка 21)
- **Название функции**: `Convert`
- **Что она делает**: Convert: Выполняет преобразование данных .
- **Какие входные данные**: `value` (`object?`): Входные данные: значение параметра value (object?).; `targetType` (`Type`): Входные данные: значение параметра targetType (Type).; `parameter` (`object?`): Входные данные: значение параметра parameter (object?).; `culture` (`CultureInfo`): Входные данные: значение параметра culture (CultureInfo).
- **Какие на выходе**: `object`: Выходные данные: результирующее значение типа object.

#### `LocationToDisplayConverter.ConvertBack` (строка 69)
- **Название функции**: `ConvertBack`
- **Что она делает**: ConvertBack: Выполняет преобразование данных Back.
- **Какие входные данные**: `value` (`object`): Входные данные: значение параметра value (object).; `targetType` (`Type`): Входные данные: значение параметра targetType (Type).; `parameter` (`object`): Входные данные: значение параметра parameter (object).; `culture` (`CultureInfo`): Входные данные: значение параметра culture (CultureInfo).
- **Какие на выходе**: `object`: Выходные данные: результирующее значение типа object.

---

### Файл: `StartupMonitor.UI\Converters\LocationToFilterLabelConverter.cs`

#### `LocationToFilterLabelConverter.Convert` (строка 17)
- **Название функции**: `Convert`
- **Что она делает**: Convert: Выполняет преобразование данных .
- **Какие входные данные**: `value` (`object`): Входные данные: значение параметра value (object).; `targetType` (`Type`): Входные данные: значение параметра targetType (Type).; `parameter` (`object`): Входные данные: значение параметра parameter (object).; `culture` (`CultureInfo`): Входные данные: значение параметра culture (CultureInfo).
- **Какие на выходе**: `object`: Выходные данные: результирующее значение типа object.

#### `LocationToFilterLabelConverter.ConvertBack` (строка 42)
- **Название функции**: `ConvertBack`
- **Что она делает**: ConvertBack: Выполняет преобразование данных Back.
- **Какие входные данные**: `value` (`object`): Входные данные: значение параметра value (object).; `targetType` (`Type`): Входные данные: значение параметра targetType (Type).; `parameter` (`object`): Входные данные: значение параметра parameter (object).; `culture` (`CultureInfo`): Входные данные: значение параметра culture (CultureInfo).
- **Какие на выходе**: `object`: Выходные данные: результирующее значение типа object.

---

### Файл: `StartupMonitor.UI\Converters\LocationToIconConverter.cs`

#### `LocationToIconConverter.Convert` (строка 21)
- **Название функции**: `Convert`
- **Что она делает**: Convert: Выполняет преобразование данных .
- **Какие входные данные**: `value` (`object`): Входные данные: значение параметра value (object).; `targetType` (`Type`): Входные данные: значение параметра targetType (Type).; `parameter` (`object`): Входные данные: значение параметра parameter (object).; `culture` (`CultureInfo`): Входные данные: значение параметра culture (CultureInfo).
- **Какие на выходе**: `object`: Выходные данные: результирующее значение типа object.

#### `LocationToIconConverter.ConvertBack` (строка 62)
- **Название функции**: `ConvertBack`
- **Что она делает**: ConvertBack: Выполняет преобразование данных Back.
- **Какие входные данные**: `value` (`object`): Входные данные: значение параметра value (object).; `targetType` (`Type`): Входные данные: значение параметра targetType (Type).; `parameter` (`object`): Входные данные: значение параметра parameter (object).; `culture` (`CultureInfo`): Входные данные: значение параметра culture (CultureInfo).
- **Какие на выходе**: `object`: Выходные данные: результирующее значение типа object.

---

### Файл: `StartupMonitor.UI\Converters\LocationToIconStringConverter.cs`

#### `LocationToIconStringConverter.Convert` (строка 20)
- **Название функции**: `Convert`
- **Что она делает**: Convert: Выполняет преобразование данных .
- **Какие входные данные**: `value` (`object`): Входные данные: значение параметра value (object).; `targetType` (`Type`): Входные данные: значение параметра targetType (Type).; `parameter` (`object`): Входные данные: значение параметра parameter (object).; `culture` (`CultureInfo`): Входные данные: значение параметра culture (CultureInfo).
- **Какие на выходе**: `object`: Выходные данные: результирующее значение типа object.

#### `LocationToIconStringConverter.ConvertBack` (строка 60)
- **Название функции**: `ConvertBack`
- **Что она делает**: ConvertBack: Выполняет преобразование данных Back.
- **Какие входные данные**: `value` (`object`): Входные данные: значение параметра value (object).; `targetType` (`Type`): Входные данные: значение параметра targetType (Type).; `parameter` (`object`): Входные данные: значение параметра parameter (object).; `culture` (`CultureInfo`): Входные данные: значение параметра culture (CultureInfo).
- **Какие на выходе**: `object`: Выходные данные: результирующее значение типа object.

---

### Файл: `StartupMonitor.UI\Converters\ProcessConverters.cs`

#### `ProcessConverters.CpuUsageToColorConverter` (строка 17)
- **Название функции**: `CpuUsageToColorConverter`
- **Что она делает**: CpuUsageToColorConverter: Выполняет операцию CpuUsageToColorConverter.
- **Какие входные данные**: Входные данные отсутствуют (параметры не требуются).
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `ProcessConverters.Convert` (строка 31)
- **Название функции**: `Convert`
- **Что она делает**: Convert: Выполняет преобразование данных .
- **Какие входные данные**: `value` (`object`): Входные данные: значение параметра value (object).; `targetType` (`Type`): Входные данные: значение параметра targetType (Type).; `parameter` (`object`): Входные данные: значение параметра parameter (object).; `culture` (`CultureInfo`): Входные данные: значение параметра culture (CultureInfo).
- **Какие на выходе**: `object`: Выходные данные: результирующее значение типа object.

#### `ProcessConverters.ConvertBack` (строка 48)
- **Название функции**: `ConvertBack`
- **Что она делает**: ConvertBack: Выполняет преобразование данных Back.
- **Какие входные данные**: `value` (`object`): Входные данные: значение параметра value (object).; `targetType` (`Type`): Входные данные: значение параметра targetType (Type).; `parameter` (`object`): Входные данные: значение параметра parameter (object).; `culture)` (`CultureInfo`): Входные данные: значение параметра culture) (CultureInfo).
- **Какие на выходе**: `object`: Выходные данные: результирующее значение типа object.

#### `ProcessConverters.freshNotSupported` (строка 49)
- **Название функции**: `freshNotSupported`
- **Что она делает**: Выполняет операцию freshNotSupported в рамках подсистемы.
- **Какие входные данные**: `)`: Значение параметра `)`.
- **Какие на выходе**: `NotSupportedException`: Результирующий объект или экземпляр структуры данных.

#### `ProcessConverters.StartupStatusBackgroundConverter` (строка 59)
- **Название функции**: `StartupStatusBackgroundConverter`
- **Что она делает**: StartupStatusBackgroundConverter: Выполняет операцию StartupStatusBackgroundConverter.
- **Какие входные данные**: Входные данные отсутствуют (параметры не требуются).
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `ProcessConverters.Convert` (строка 71)
- **Название функции**: `Convert`
- **Что она делает**: Convert: Выполняет преобразование данных .
- **Какие входные данные**: `value` (`object`): Входные данные: значение параметра value (object).; `targetType` (`Type`): Входные данные: значение параметра targetType (Type).; `parameter` (`object`): Входные данные: значение параметра parameter (object).; `culture` (`CultureInfo`): Входные данные: значение параметра culture (CultureInfo).
- **Какие на выходе**: `object`: Выходные данные: результирующее значение типа object.

#### `ProcessConverters.ConvertBack` (строка 82)
- **Название функции**: `ConvertBack`
- **Что она делает**: ConvertBack: Выполняет преобразование данных Back.
- **Какие входные данные**: `value` (`object`): Входные данные: значение параметра value (object).; `targetType` (`Type`): Входные данные: значение параметра targetType (Type).; `parameter` (`object`): Входные данные: значение параметра parameter (object).; `culture)` (`CultureInfo`): Входные данные: значение параметра culture) (CultureInfo).
- **Какие на выходе**: `object`: Выходные данные: результирующее значение типа object.

#### `ProcessConverters.StartupStatusBorderConverter` (строка 92)
- **Название функции**: `StartupStatusBorderConverter`
- **Что она делает**: StartupStatusBorderConverter: Выполняет операцию StartupStatusBorderConverter.
- **Какие входные данные**: Входные данные отсутствуют (параметры не требуются).
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `ProcessConverters.Convert` (строка 104)
- **Название функции**: `Convert`
- **Что она делает**: Convert: Выполняет преобразование данных .
- **Какие входные данные**: `value` (`object`): Входные данные: значение параметра value (object).; `targetType` (`Type`): Входные данные: значение параметра targetType (Type).; `parameter` (`object`): Входные данные: значение параметра parameter (object).; `culture` (`CultureInfo`): Входные данные: значение параметра culture (CultureInfo).
- **Какие на выходе**: `object`: Выходные данные: результирующее значение типа object.

#### `ProcessConverters.ConvertBack` (строка 115)
- **Название функции**: `ConvertBack`
- **Что она делает**: ConvertBack: Выполняет преобразование данных Back.
- **Какие входные данные**: `value` (`object`): Входные данные: значение параметра value (object).; `targetType` (`Type`): Входные данные: значение параметра targetType (Type).; `parameter` (`object`): Входные данные: значение параметра parameter (object).; `culture)` (`CultureInfo`): Входные данные: значение параметра culture) (CultureInfo).
- **Какие на выходе**: `object`: Выходные данные: результирующее значение типа object.

#### `ProcessConverters.StartupStatusForegroundConverter` (строка 125)
- **Название функции**: `StartupStatusForegroundConverter`
- **Что она делает**: StartupStatusForegroundConverter: Выполняет операцию StartupStatusForegroundConverter.
- **Какие входные данные**: Входные данные отсутствуют (параметры не требуются).
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `ProcessConverters.Convert` (строка 137)
- **Название функции**: `Convert`
- **Что она делает**: Convert: Выполняет преобразование данных .
- **Какие входные данные**: `value` (`object`): Входные данные: значение параметра value (object).; `targetType` (`Type`): Входные данные: значение параметра targetType (Type).; `parameter` (`object`): Входные данные: значение параметра parameter (object).; `culture` (`CultureInfo`): Входные данные: значение параметра culture (CultureInfo).
- **Какие на выходе**: `object`: Выходные данные: результирующее значение типа object.

#### `ProcessConverters.ConvertBack` (строка 148)
- **Название функции**: `ConvertBack`
- **Что она делает**: ConvertBack: Выполняет преобразование данных Back.
- **Какие входные данные**: `value` (`object`): Входные данные: значение параметра value (object).; `targetType` (`Type`): Входные данные: значение параметра targetType (Type).; `parameter` (`object`): Входные данные: значение параметра parameter (object).; `culture)` (`CultureInfo`): Входные данные: значение параметра culture) (CultureInfo).
- **Какие на выходе**: `object`: Выходные данные: результирующее значение типа object.

#### `ProcessConverters.Convert` (строка 160)
- **Название функции**: `Convert`
- **Что она делает**: Convert: Выполняет преобразование данных .
- **Какие входные данные**: `value` (`object`): Входные данные: значение параметра value (object).; `targetType` (`Type`): Входные данные: значение параметра targetType (Type).; `parameter` (`object`): Входные данные: значение параметра parameter (object).; `culture` (`CultureInfo`): Входные данные: значение параметра culture (CultureInfo).
- **Какие на выходе**: `object`: Выходные данные: результирующее значение типа object.

#### `ProcessConverters.ConvertBack` (строка 171)
- **Название функции**: `ConvertBack`
- **Что она делает**: ConvertBack: Выполняет преобразование данных Back.
- **Какие входные данные**: `value` (`object`): Входные данные: значение параметра value (object).; `targetType` (`Type`): Входные данные: значение параметра targetType (Type).; `parameter` (`object`): Входные данные: значение параметра parameter (object).; `culture)` (`CultureInfo`): Входные данные: значение параметра culture) (CultureInfo).
- **Какие на выходе**: `object`: Выходные данные: результирующее значение типа object.

#### `ProcessConverters.ThreatLevelToBackgroundConverter` (строка 182)
- **Название функции**: `ThreatLevelToBackgroundConverter`
- **Что она делает**: ThreatLevelToBackgroundConverter: Выполняет операцию ThreatLevelToBackgroundConverter.
- **Какие входные данные**: Входные данные отсутствуют (параметры не требуются).
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `ProcessConverters.Convert` (строка 195)
- **Название функции**: `Convert`
- **Что она делает**: Convert: Выполняет преобразование данных .
- **Какие входные данные**: `value` (`object?`): Входные данные: значение параметра value (object?).; `targetType` (`Type`): Входные данные: значение параметра targetType (Type).; `parameter` (`object?`): Входные данные: значение параметра parameter (object?).; `culture` (`CultureInfo`): Входные данные: значение параметра culture (CultureInfo).
- **Какие на выходе**: `object`: Выходные данные: результирующее значение типа object.

#### `ProcessConverters.ConvertBack` (строка 216)
- **Название функции**: `ConvertBack`
- **Что она делает**: ConvertBack: Выполняет преобразование данных Back.
- **Какие входные данные**: `value` (`object?`): Входные данные: значение параметра value (object?).; `targetType` (`Type`): Входные данные: значение параметра targetType (Type).; `parameter` (`object?`): Входные данные: значение параметра parameter (object?).; `culture)` (`CultureInfo`): Входные данные: значение параметра culture) (CultureInfo).
- **Какие на выходе**: `object`: Выходные данные: результирующее значение типа object.

#### `ProcessConverters.ThreatLevelToBorderConverter` (строка 227)
- **Название функции**: `ThreatLevelToBorderConverter`
- **Что она делает**: ThreatLevelToBorderConverter: Выполняет операцию ThreatLevelToBorderConverter.
- **Какие входные данные**: Входные данные отсутствуют (параметры не требуются).
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `ProcessConverters.Convert` (строка 240)
- **Название функции**: `Convert`
- **Что она делает**: Convert: Выполняет преобразование данных .
- **Какие входные данные**: `value` (`object?`): Входные данные: значение параметра value (object?).; `targetType` (`Type`): Входные данные: значение параметра targetType (Type).; `parameter` (`object?`): Входные данные: значение параметра parameter (object?).; `culture` (`CultureInfo`): Входные данные: значение параметра culture (CultureInfo).
- **Какие на выходе**: `object`: Выходные данные: результирующее значение типа object.

#### `ProcessConverters.ConvertBack` (строка 261)
- **Название функции**: `ConvertBack`
- **Что она делает**: ConvertBack: Выполняет преобразование данных Back.
- **Какие входные данные**: `value` (`object?`): Входные данные: значение параметра value (object?).; `targetType` (`Type`): Входные данные: значение параметра targetType (Type).; `parameter` (`object?`): Входные данные: значение параметра parameter (object?).; `culture)` (`CultureInfo`): Входные данные: значение параметра culture) (CultureInfo).
- **Какие на выходе**: `object`: Выходные данные: результирующее значение типа object.

#### `ProcessConverters.ThreatLevelToForegroundConverter` (строка 273)
- **Название функции**: `ThreatLevelToForegroundConverter`
- **Что она делает**: ThreatLevelToForegroundConverter: Выполняет операцию ThreatLevelToForegroundConverter.
- **Какие входные данные**: Входные данные отсутствуют (параметры не требуются).
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `ProcessConverters.Convert` (строка 287)
- **Название функции**: `Convert`
- **Что она делает**: Convert: Выполняет преобразование данных .
- **Какие входные данные**: `value` (`object?`): Входные данные: значение параметра value (object?).; `targetType` (`Type`): Входные данные: значение параметра targetType (Type).; `parameter` (`object?`): Входные данные: значение параметра parameter (object?).; `culture` (`CultureInfo`): Входные данные: значение параметра culture (CultureInfo).
- **Какие на выходе**: `object`: Выходные данные: результирующее значение типа object.

#### `ProcessConverters.ConvertBack` (строка 308)
- **Название функции**: `ConvertBack`
- **Что она делает**: ConvertBack: Выполняет преобразование данных Back.
- **Какие входные данные**: `value` (`object?`): Входные данные: значение параметра value (object?).; `targetType` (`Type`): Входные данные: значение параметра targetType (Type).; `parameter` (`object?`): Входные данные: значение параметра parameter (object?).; `culture)` (`CultureInfo`): Входные данные: значение параметра culture) (CultureInfo).
- **Какие на выходе**: `object`: Выходные данные: результирующее значение типа object.

#### `ProcessConverters.Convert` (строка 320)
- **Название функции**: `Convert`
- **Что она делает**: Convert: Выполняет преобразование данных .
- **Какие входные данные**: `value` (`object?`): Входные данные: значение параметра value (object?).; `targetType` (`Type`): Входные данные: значение параметра targetType (Type).; `parameter` (`object?`): Входные данные: значение параметра parameter (object?).; `culture` (`CultureInfo`): Входные данные: значение параметра culture (CultureInfo).
- **Какие на выходе**: `object`: Выходные данные: результирующее значение типа object.

#### `ProcessConverters.ConvertBack` (строка 361)
- **Название функции**: `ConvertBack`
- **Что она делает**: ConvertBack: Выполняет преобразование данных Back.
- **Какие входные данные**: `value` (`object?`): Входные данные: значение параметра value (object?).; `targetType` (`Type`): Входные данные: значение параметра targetType (Type).; `parameter` (`object?`): Входные данные: значение параметра parameter (object?).; `culture)` (`CultureInfo`): Входные данные: значение параметра culture) (CultureInfo).
- **Какие на выходе**: `object`: Выходные данные: результирующее значение типа object.

#### `ProcessConverters.Convert` (строка 373)
- **Название функции**: `Convert`
- **Что она делает**: Convert: Выполняет преобразование данных .
- **Какие входные данные**: `value` (`object?`): Входные данные: значение параметра value (object?).; `targetType` (`Type`): Входные данные: значение параметра targetType (Type).; `parameter` (`object?`): Входные данные: значение параметра parameter (object?).; `culture` (`CultureInfo`): Входные данные: значение параметра culture (CultureInfo).
- **Какие на выходе**: `object`: Выходные данные: результирующее значение типа object.

#### `ProcessConverters.ConvertBack` (строка 387)
- **Название функции**: `ConvertBack`
- **Что она делает**: ConvertBack: Выполняет преобразование данных Back.
- **Какие входные данные**: `value` (`object?`): Входные данные: значение параметра value (object?).; `targetType` (`Type`): Входные данные: значение параметра targetType (Type).; `parameter` (`object?`): Входные данные: значение параметра parameter (object?).; `culture)` (`CultureInfo`): Входные данные: значение параметра culture) (CultureInfo).
- **Какие на выходе**: `object`: Выходные данные: результирующее значение типа object.

---

### Файл: `StartupMonitor.UI\Converters\RatingToStarsConverter.cs`

#### `RatingToStarsConverter.Convert` (строка 19)
- **Название функции**: `Convert`
- **Что она делает**: Convert: Выполняет преобразование данных .
- **Какие входные данные**: `value` (`object`): Входные данные: значение параметра value (object).; `targetType` (`Type`): Входные данные: значение параметра targetType (Type).; `parameter` (`object`): Входные данные: значение параметра parameter (object).; `culture` (`CultureInfo`): Входные данные: значение параметра culture (CultureInfo).
- **Какие на выходе**: `object`: Выходные данные: результирующее значение типа object.

#### `RatingToStarsConverter.ConvertBack` (строка 36)
- **Название функции**: `ConvertBack`
- **Что она делает**: ConvertBack: Выполняет преобразование данных Back.
- **Какие входные данные**: `value` (`object`): Входные данные: значение параметра value (object).; `targetType` (`Type`): Входные данные: значение параметра targetType (Type).; `parameter` (`object`): Входные данные: значение параметра parameter (object).; `culture` (`CultureInfo`): Входные данные: значение параметра culture (CultureInfo).
- **Какие на выходе**: `object`: Выходные данные: результирующее значение типа object.

#### `RatingToStarsConverter.ScoreToStars` (строка 43)
- **Название функции**: `ScoreToStars`
- **Что она делает**: ScoreToStars: Выполняет операцию ScoreToStars.
- **Какие входные данные**: `score` (`int`): Входные данные: значение параметра score (int).
- **Какие на выходе**: `int`: Выходные данные: результирующее значение типа int.

#### `RatingToStarsConverter.Convert` (строка 86)
- **Название функции**: `Convert`
- **Что она делает**: Convert: Выполняет преобразование данных .
- **Какие входные данные**: `value` (`object`): Входные данные: значение параметра value (object).; `targetType` (`Type`): Входные данные: значение параметра targetType (Type).; `parameter` (`object`): Входные данные: значение параметра parameter (object).; `culture` (`CultureInfo`): Входные данные: значение параметра culture (CultureInfo).
- **Какие на выходе**: `object`: Выходные данные: результирующее значение типа object.

#### `RatingToStarsConverter.ConvertBack` (строка 112)
- **Название функции**: `ConvertBack`
- **Что она делает**: ConvertBack: Выполняет преобразование данных Back.
- **Какие входные данные**: `value` (`object`): Входные данные: значение параметра value (object).; `targetType` (`Type`): Входные данные: значение параметра targetType (Type).; `parameter` (`object`): Входные данные: значение параметра parameter (object).; `culture` (`CultureInfo`): Входные данные: значение параметра culture (CultureInfo).
- **Какие на выходе**: `object`: Выходные данные: результирующее значение типа object.

#### `RatingToStarsConverter.Convert` (строка 129)
- **Название функции**: `Convert`
- **Что она делает**: Convert: Выполняет преобразование данных .
- **Какие входные данные**: `value` (`object`): Входные данные: значение параметра value (object).; `targetType` (`Type`): Входные данные: значение параметра targetType (Type).; `parameter` (`object`): Входные данные: значение параметра parameter (object).; `culture` (`CultureInfo`): Входные данные: значение параметра culture (CultureInfo).
- **Какие на выходе**: `object`: Выходные данные: результирующее значение типа object.

#### `RatingToStarsConverter.ConvertBack` (строка 144)
- **Название функции**: `ConvertBack`
- **Что она делает**: ConvertBack: Выполняет преобразование данных Back.
- **Какие входные данные**: `value` (`object`): Входные данные: значение параметра value (object).; `targetType` (`Type`): Входные данные: значение параметра targetType (Type).; `parameter` (`object`): Входные данные: значение параметра parameter (object).; `culture` (`CultureInfo`): Входные данные: значение параметра culture (CultureInfo).
- **Какие на выходе**: `object`: Выходные данные: результирующее значение типа object.

---

### Файл: `StartupMonitor.UI\Converters\SignatureConverters.cs`

#### `SignatureConverters.Convert` (строка 22)
- **Название функции**: `Convert`
- **Что она делает**: Convert: Выполняет преобразование данных .
- **Какие входные данные**: `value` (`object`): Входные данные: значение параметра value (object).; `targetType` (`Type`): Входные данные: значение параметра targetType (Type).; `parameter` (`object`): Входные данные: значение параметра parameter (object).; `culture` (`CultureInfo`): Входные данные: значение параметра culture (CultureInfo).
- **Какие на выходе**: `object`: Выходные данные: результирующее значение типа object.

#### `SignatureConverters.ConvertBack` (строка 42)
- **Название функции**: `ConvertBack`
- **Что она делает**: ConvertBack: Выполняет преобразование данных Back.
- **Какие входные данные**: `value` (`object`): Входные данные: значение параметра value (object).; `targetType` (`Type`): Входные данные: значение параметра targetType (Type).; `parameter` (`object`): Входные данные: значение параметра parameter (object).; `culture)` (`CultureInfo`): Входные данные: значение параметра culture) (CultureInfo).
- **Какие на выходе**: `object`: Выходные данные: результирующее значение типа object.

#### `SignatureConverters.SignatureStatusToColorConverter` (строка 56)
- **Название функции**: `SignatureStatusToColorConverter`
- **Что она делает**: SignatureStatusToColorConverter: Выполняет операцию SignatureStatusToColorConverter.
- **Какие входные данные**: Входные данные отсутствуют (параметры не требуются).
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `SignatureConverters.Convert` (строка 70)
- **Название функции**: `Convert`
- **Что она делает**: Convert: Выполняет преобразование данных .
- **Какие входные данные**: `value` (`object`): Входные данные: значение параметра value (object).; `targetType` (`Type`): Входные данные: значение параметра targetType (Type).; `parameter` (`object`): Входные данные: значение параметра parameter (object).; `culture` (`CultureInfo`): Входные данные: значение параметра culture (CultureInfo).
- **Какие на выходе**: `object`: Выходные данные: результирующее значение типа object.

#### `SignatureConverters.ConvertBack` (строка 90)
- **Название функции**: `ConvertBack`
- **Что она делает**: ConvertBack: Выполняет преобразование данных Back.
- **Какие входные данные**: `value` (`object`): Входные данные: значение параметра value (object).; `targetType` (`Type`): Входные данные: значение параметра targetType (Type).; `parameter` (`object`): Входные данные: значение параметра parameter (object).; `culture)` (`CultureInfo`): Входные данные: значение параметра culture) (CultureInfo).
- **Какие на выходе**: `object`: Выходные данные: результирующее значение типа object.

#### `SignatureConverters.SignatureStatusToBackgroundConverter` (строка 104)
- **Название функции**: `SignatureStatusToBackgroundConverter`
- **Что она делает**: SignatureStatusToBackgroundConverter: Выполняет операцию SignatureStatusToBackgroundConverter.
- **Какие входные данные**: Входные данные отсутствуют (параметры не требуются).
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `SignatureConverters.Convert` (строка 118)
- **Название функции**: `Convert`
- **Что она делает**: Convert: Выполняет преобразование данных .
- **Какие входные данные**: `value` (`object`): Входные данные: значение параметра value (object).; `targetType` (`Type`): Входные данные: значение параметра targetType (Type).; `parameter` (`object`): Входные данные: значение параметра parameter (object).; `culture` (`CultureInfo`): Входные данные: значение параметра culture (CultureInfo).
- **Какие на выходе**: `object`: Выходные данные: результирующее значение типа object.

#### `SignatureConverters.ConvertBack` (строка 138)
- **Название функции**: `ConvertBack`
- **Что она делает**: ConvertBack: Выполняет преобразование данных Back.
- **Какие входные данные**: `value` (`object`): Входные данные: значение параметра value (object).; `targetType` (`Type`): Входные данные: значение параметра targetType (Type).; `parameter` (`object`): Входные данные: значение параметра parameter (object).; `culture)` (`CultureInfo`): Входные данные: значение параметра culture) (CultureInfo).
- **Какие на выходе**: `object`: Выходные данные: результирующее значение типа object.

#### `SignatureConverters.SignatureStatusToBorderConverter` (строка 152)
- **Название функции**: `SignatureStatusToBorderConverter`
- **Что она делает**: SignatureStatusToBorderConverter: Выполняет операцию SignatureStatusToBorderConverter.
- **Какие входные данные**: Входные данные отсутствуют (параметры не требуются).
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `SignatureConverters.Convert` (строка 166)
- **Название функции**: `Convert`
- **Что она делает**: Convert: Выполняет преобразование данных .
- **Какие входные данные**: `value` (`object`): Входные данные: значение параметра value (object).; `targetType` (`Type`): Входные данные: значение параметра targetType (Type).; `parameter` (`object`): Входные данные: значение параметра parameter (object).; `culture` (`CultureInfo`): Входные данные: значение параметра culture (CultureInfo).
- **Какие на выходе**: `object`: Выходные данные: результирующее значение типа object.

#### `SignatureConverters.ConvertBack` (строка 186)
- **Название функции**: `ConvertBack`
- **Что она делает**: ConvertBack: Выполняет преобразование данных Back.
- **Какие входные данные**: `value` (`object`): Входные данные: значение параметра value (object).; `targetType` (`Type`): Входные данные: значение параметра targetType (Type).; `parameter` (`object`): Входные данные: значение параметра parameter (object).; `culture)` (`CultureInfo`): Входные данные: значение параметра culture) (CultureInfo).
- **Какие на выходе**: `object`: Выходные данные: результирующее значение типа object.

#### `SignatureConverters.Convert` (строка 200)
- **Название функции**: `Convert`
- **Что она делает**: Convert: Выполняет преобразование данных .
- **Какие входные данные**: `value` (`object`): Входные данные: значение параметра value (object).; `targetType` (`Type`): Входные данные: значение параметра targetType (Type).; `parameter` (`object`): Входные данные: значение параметра parameter (object).; `culture` (`CultureInfo`): Входные данные: значение параметра culture (CultureInfo).
- **Какие на выходе**: `object`: Выходные данные: результирующее значение типа object.

#### `SignatureConverters.FormatTooltip` (строка 223)
- **Название функции**: `FormatTooltip`
- **Что она делает**: FormatTooltip: Выполняет операцию FormatTooltip.
- **Какие входные данные**: `status` (`DigitalSignatureStatus`): Входные данные: значение параметра status (DigitalSignatureStatus).; `signer` (`string?`): Входные данные: значение параметра signer (string?).; `issuer` (`string?`): Входные данные: значение параметра issuer (string?).; `fallback` (`string?`): Входные данные: значение параметра fallback (string?).
- **Какие на выходе**: `string`: Выходные данные: результирующее значение типа string.

#### `SignatureConverters.ConvertBack` (строка 265)
- **Название функции**: `ConvertBack`
- **Что она делает**: ConvertBack: Выполняет преобразование данных Back.
- **Какие входные данные**: `value` (`object`): Входные данные: значение параметра value (object).; `targetType` (`Type`): Входные данные: значение параметра targetType (Type).; `parameter` (`object`): Входные данные: значение параметра parameter (object).; `culture)` (`CultureInfo`): Входные данные: значение параметра culture) (CultureInfo).
- **Какие на выходе**: `object`: Выходные данные: результирующее значение типа object.

---

### Файл: `StartupMonitor.UI\Converters\StatusToBoolConverter.cs`

#### `StatusToBoolConverter.Convert` (строка 20)
- **Название функции**: `Convert`
- **Что она делает**: Convert: Выполняет преобразование данных .
- **Какие входные данные**: `value` (`object`): Входные данные: значение параметра value (object).; `targetType` (`Type`): Входные данные: значение параметра targetType (Type).; `parameter` (`object`): Входные данные: значение параметра parameter (object).; `culture` (`CultureInfo`): Входные данные: значение параметра culture (CultureInfo).
- **Какие на выходе**: `object`: Выходные данные: результирующее значение типа object.

#### `StatusToBoolConverter.ConvertBack` (строка 34)
- **Название функции**: `ConvertBack`
- **Что она делает**: ConvertBack: Выполняет преобразование данных Back.
- **Какие входные данные**: `value` (`object`): Входные данные: значение параметра value (object).; `targetType` (`Type`): Входные данные: значение параметра targetType (Type).; `parameter` (`object`): Входные данные: значение параметра parameter (object).; `culture` (`CultureInfo`): Входные данные: значение параметра culture (CultureInfo).
- **Какие на выходе**: `object`: Выходные данные: результирующее значение типа object.

---

### Файл: `StartupMonitor.UI\Converters\StatusToColorConverter.cs`

#### `StatusToColorConverter.Convert` (строка 21)
- **Название функции**: `Convert`
- **Что она делает**: Convert: Выполняет преобразование данных .
- **Какие входные данные**: `value` (`object`): Входные данные: значение параметра value (object).; `targetType` (`Type`): Входные данные: значение параметра targetType (Type).; `parameter` (`object`): Входные данные: значение параметра parameter (object).; `culture` (`CultureInfo`): Входные данные: значение параметра culture (CultureInfo).
- **Какие на выходе**: `object`: Выходные данные: результирующее значение типа object.

#### `StatusToColorConverter.ConvertBack` (строка 49)
- **Название функции**: `ConvertBack`
- **Что она делает**: ConvertBack: Выполняет преобразование данных Back.
- **Какие входные данные**: `value` (`object`): Входные данные: значение параметра value (object).; `targetType` (`Type`): Входные данные: значение параметра targetType (Type).; `parameter` (`object`): Входные данные: значение параметра parameter (object).; `culture` (`CultureInfo`): Входные данные: значение параметра culture (CultureInfo).
- **Какие на выходе**: `object`: Выходные данные: результирующее значение типа object.

---

### Файл: `StartupMonitor.UI\Converters\StatusToDisplayConverter.cs`

#### `StatusToDisplayConverter.Convert` (строка 21)
- **Название функции**: `Convert`
- **Что она делает**: Convert: Выполняет преобразование данных .
- **Какие входные данные**: `value` (`object`): Входные данные: значение параметра value (object).; `targetType` (`Type`): Входные данные: значение параметра targetType (Type).; `parameter` (`object`): Входные данные: значение параметра parameter (object).; `culture` (`CultureInfo`): Входные данные: значение параметра culture (CultureInfo).
- **Какие на выходе**: `object`: Выходные данные: результирующее значение типа object.

#### `StatusToDisplayConverter.ConvertBack` (строка 42)
- **Название функции**: `ConvertBack`
- **Что она делает**: ConvertBack: Выполняет преобразование данных Back.
- **Какие входные данные**: `value` (`object`): Входные данные: значение параметра value (object).; `targetType` (`Type`): Входные данные: значение параметра targetType (Type).; `parameter` (`object`): Входные данные: значение параметра parameter (object).; `culture` (`CultureInfo`): Входные данные: значение параметра culture (CultureInfo).
- **Какие на выходе**: `object`: Выходные данные: результирующее значение типа object.

---

### Файл: `StartupMonitor.UI\Converters\StatusToToggleTextConverter.cs`

#### `StatusToToggleTextConverter.Convert` (строка 21)
- **Название функции**: `Convert`
- **Что она делает**: Convert: Выполняет преобразование данных .
- **Какие входные данные**: `value` (`object`): Входные данные: значение параметра value (object).; `targetType` (`Type`): Входные данные: значение параметра targetType (Type).; `parameter` (`object`): Входные данные: значение параметра parameter (object).; `culture` (`CultureInfo`): Входные данные: значение параметра culture (CultureInfo).
- **Какие на выходе**: `object`: Выходные данные: результирующее значение типа object.

#### `StatusToToggleTextConverter.ConvertBack` (строка 41)
- **Название функции**: `ConvertBack`
- **Что она делает**: ConvertBack: Выполняет преобразование данных Back.
- **Какие входные данные**: `value` (`object`): Входные данные: значение параметра value (object).; `targetType` (`Type`): Входные данные: значение параметра targetType (Type).; `parameter` (`object`): Входные данные: значение параметра parameter (object).; `culture` (`CultureInfo`): Входные данные: значение параметра culture (CultureInfo).
- **Какие на выходе**: `object`: Выходные данные: результирующее значение типа object.

---

### Файл: `StartupMonitor.UI\Converters\StatusToVisibilityConverter.cs`

#### `StatusToVisibilityConverter.Convert` (строка 21)
- **Название функции**: `Convert`
- **Что она делает**: Convert: Выполняет преобразование данных .
- **Какие входные данные**: `value` (`object`): Входные данные: значение параметра value (object).; `targetType` (`Type`): Входные данные: значение параметра targetType (Type).; `parameter` (`object`): Входные данные: значение параметра parameter (object).; `culture` (`CultureInfo`): Входные данные: значение параметра culture (CultureInfo).
- **Какие на выходе**: `object`: Выходные данные: результирующее значение типа object.

#### `StatusToVisibilityConverter.ConvertBack` (строка 43)
- **Название функции**: `ConvertBack`
- **Что она делает**: ConvertBack: Выполняет преобразование данных Back.
- **Какие входные данные**: `value` (`object`): Входные данные: значение параметра value (object).; `targetType` (`Type`): Входные данные: значение параметра targetType (Type).; `parameter` (`object`): Входные данные: значение параметра parameter (object).; `culture` (`CultureInfo`): Входные данные: значение параметра culture (CultureInfo).
- **Какие на выходе**: `object`: Выходные данные: результирующее значение типа object.

---

### Файл: `StartupMonitor.UI\Converters\StringNotEmptyToVisibilityConverter.cs`

#### `StringNotEmptyToVisibilityConverter.Convert` (строка 17)
- **Название функции**: `Convert`
- **Что она делает**: Convert: Выполняет преобразование данных .
- **Какие входные данные**: `value` (`object`): Входные данные: значение параметра value (object).; `targetType` (`Type`): Входные данные: значение параметра targetType (Type).; `parameter` (`object`): Входные данные: значение параметра parameter (object).; `culture` (`CultureInfo`): Входные данные: значение параметра culture (CultureInfo).
- **Какие на выходе**: `object`: Выходные данные: результирующее значение типа object.

#### `StringNotEmptyToVisibilityConverter.ConvertBack` (строка 26)
- **Название функции**: `ConvertBack`
- **Что она делает**: ConvertBack: Выполняет преобразование данных Back.
- **Какие входные данные**: `value` (`object`): Входные данные: значение параметра value (object).; `targetType` (`Type`): Входные данные: значение параметра targetType (Type).; `parameter` (`object`): Входные данные: значение параметра parameter (object).; `culture` (`CultureInfo`): Входные данные: значение параметра culture (CultureInfo).
- **Какие на выходе**: `object`: Выходные данные: результирующее значение типа object.

---

### Файл: `StartupMonitor.UI\Converters\TruncateConverter.cs`

#### `TruncateConverter.Convert` (строка 19)
- **Название функции**: `Convert`
- **Что она делает**: Convert: Выполняет преобразование данных .
- **Какие входные данные**: `value` (`object`): Входные данные: значение параметра value (object).; `targetType` (`Type`): Входные данные: значение параметра targetType (Type).; `parameter` (`object`): Входные данные: значение параметра parameter (object).; `culture` (`CultureInfo`): Входные данные: значение параметра culture (CultureInfo).
- **Какие на выходе**: `object`: Выходные данные: результирующее значение типа object.

#### `TruncateConverter.ConvertBack` (строка 38)
- **Название функции**: `ConvertBack`
- **Что она делает**: ConvertBack: Выполняет преобразование данных Back.
- **Какие входные данные**: `value` (`object`): Входные данные: значение параметра value (object).; `targetType` (`Type`): Входные данные: значение параметра targetType (Type).; `parameter` (`object`): Входные данные: значение параметра parameter (object).; `culture` (`CultureInfo`): Входные данные: значение параметра culture (CultureInfo).
- **Какие на выходе**: `object`: Выходные данные: результирующее значение типа object.

---

### Файл: `StartupMonitor.UI\Services\AppIconService.cs`

#### `AppIconService.SHGetFileInfo` (строка 42)
- **Название функции**: `SHGetFileInfo`
- **Что она делает**: Выполняет операцию SHGetFileInfo в рамках подсистемы.
- **Какие входные данные**: `pszPath` (`string`): Входные данные: значение параметра pszPath (string).; `dwFileAttributes` (`uint`): Входные данные: значение параметра dwFileAttributes (uint).; `psfi` (`ref SHFILEINFO`): Входные данные: значение параметра psfi (ref SHFILEINFO).; `cbFileInfo` (`uint`): Входные данные: значение параметра cbFileInfo (uint).; `uFlags` (`uint`): Входные данные: значение параметра uFlags (uint).
- **Какие на выходе**: `extern IntPtr`: Выходные данные: результирующее значение типа extern IntPtr.

#### `AppIconService.DestroyIcon` (строка 49)
- **Название функции**: `DestroyIcon`
- **Что она делает**: Выполняет операцию DestroyIcon в рамках подсистемы.
- **Какие входные данные**: `hIcon` (`IntPtr`): Входные данные: значение параметра hIcon (IntPtr).
- **Какие на выходе**: `extern bool`: Выходные данные: результирующее значение типа extern bool.

#### `AppIconService.GetIcon` (строка 54)
- **Название функции**: `GetIcon`
- **Что она делает**: Возвращает иконку приложения по пути к файлу с кэшированием.
- **Какие входные данные**: `rawPath` (`string?`): Значение параметра `rawPath` (string?).
- **Какие на выходе**: `ImageSource?`: Результирующий объект или экземпляр структуры данных.

#### `AppIconService.ExtractIconCore` (строка 69)
- **Название функции**: `ExtractIconCore`
- **Что она делает**: ExtractIconCore: Выполняет операцию ExtractIconCore.
- **Какие входные данные**: `path` (`string`): Входные данные: значение параметра path (string).
- **Какие на выходе**: `ImageSource?`: Выходные данные: результирующее значение типа ImageSource?.

#### `AppIconService.CleanPath` (строка 107)
- **Название функции**: `CleanPath`
- **Что она делает**: CleanPath: Выполняет операцию CleanPath.
- **Какие входные данные**: `input` (`string`): Входные данные: значение параметра input (string).
- **Какие на выходе**: `string`: Выходные данные: результирующее значение типа string.

---

### Файл: `StartupMonitor.UI\Services\AutostartService.cs`

#### `AutostartService.SetEnabled` (строка 17)
- **Название функции**: `SetEnabled`
- **Что она делает**: Включает или отключает автозапуск UI при входе пользователя.
- **Какие входные данные**: `enable` (`bool`): true — добавить/обновить запись, false — удалить.; `valueName` (`string?`): Необязательное имя значения в реестре (по умолчанию ValueName).
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `AutostartService.EnsureSingleAutostartEntry` (строка 49)
- **Название функции**: `EnsureSingleAutostartEntry`
- **Что она делает**: Проверяет реестр на наличие дубликатов или устаревших записей автозапуска StartupMonitor.UI и гарантирует наличие ровно одной актуальной записи (если включено) или полное удаление (если выключено).
- **Какие входные данные**: `enable` (`bool`): true — включить автозапуск ровно в одном месте; false — удалить все записи.; `targetExePath` (`string?`): Значение параметра `targetExePath` (string?).
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `AutostartService.IsStartupMonitorUiEntry` (строка 146)
- **Название функции**: `IsStartupMonitorUiEntry`
- **Что она делает**: Проверяет, относится ли запись в реестре к StartupMonitor.UI (по имени значения или пути команды).
- **Какие входные данные**: `name` (`string`): Значение параметра `name` (string).; `commandValue` (`string`): Значение параметра `commandValue` (string).
- **Какие на выходе**: `bool`: Логический результат выполнения (`true` в случае успеха или соответствия условию, иначе `false`).

#### `AutostartService.CleanHklmDuplicates` (строка 167)
- **Название функции**: `CleanHklmDuplicates`
- **Что она делает**: Очищает дубликаты в разделах HKLM Run, если они существуют (например, если программа была прописана общесистемно).
- **Какие входные данные**: Входные данные отсутствуют (параметры не требуются).
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `AutostartService.IsEnabled` (строка 198)
- **Название функции**: `IsEnabled`
- **Что она делает**: Проверяет, зарегистрирован ли UI в автозагрузке.
- **Какие входные данные**: `valueName` (`string?`): Необязательное имя значения (по умолчанию ValueName).
- **Какие на выходе**: `bool`: true, если запись существует в HKCU Run.

---

### Файл: `StartupMonitor.UI\Services\LocalizationService.cs`

#### `LocalizationService.GetStatusLoaded` (строка 481)
- **Название функции**: `GetStatusLoaded`
- **Что она делает**: Возвращает локализованную строку с количеством загруженных записей.
- **Какие входные данные**: `count` (`int`): Значение параметра `count` (int).
- **Какие на выходе**: `string`: Текстовая строка с результатом операции или сообщением.

#### `LocalizationService.GetStatusError` (строка 485)
- **Название функции**: `GetStatusError`
- **Что она делает**: Возвращает локализованную строку ошибки с указанным сообщением.
- **Какие входные данные**: `message` (`string`): Значение параметра `message` (string).
- **Какие на выходе**: `string`: Текстовая строка с результатом операции или сообщением.

#### `LocalizationService.GetServiceNotFound` (строка 489)
- **Название функции**: `GetServiceNotFound`
- **Что она делает**: Возвращает локализованную строку «Служба не найдена» с указанным путём.
- **Какие входные данные**: `path` (`string`): Значение параметра `path` (string).
- **Какие на выходе**: `string`: Текстовая строка с результатом операции или сообщением.

#### `LocalizationService.GetNotifText` (строка 493)
- **Название функции**: `GetNotifText`
- **Что она делает**: Возвращает локализованный текст уведомления с именем и расположением записи.
- **Какие входные данные**: `name` (`string`): Значение параметра `name` (string).; `location` (`string`): Значение параметра `location` (string).
- **Какие на выходе**: `string`: Текстовая строка с результатом операции или сообщением.

#### `LocalizationService.GetBlacklistNotifText` (строка 500)
- **Название функции**: `GetBlacklistNotifText`
- **Что она делает**: Текст предупреждения об авто-блокировке записи из чёрного списка.
- **Какие входные данные**: `name` (`string`): Значение параметра `name` (string).
- **Какие на выходе**: `string`: Текстовая строка с результатом операции или сообщением.

#### `LocalizationService.GetActionNotifText` (строка 507)
- **Название функции**: `GetActionNotifText`
- **Что она делает**: Возвращает локализованный текст уведомления о действии по коду действия.
- **Какие входные данные**: `action` (`string`): Значение параметра `action` (string).; `name` (`string`): Значение параметра `name` (string).
- **Какие на выходе**: `string`: Текстовая строка с результатом операции или сообщением.

#### `LocalizationService.GetBootTimeFaster` (строка 712)
- **Название функции**: `GetBootTimeFaster`
- **Что она делает**: Формирует строку «Быстрее на X» при сравнении с предыдущей загрузкой.
- **Какие входные данные**: `delta` (`string`): Значение параметра `delta` (string).
- **Какие на выходе**: `string`: Текстовая строка с результатом операции или сообщением.

#### `LocalizationService.GetBootTimeSlower` (строка 716)
- **Название функции**: `GetBootTimeSlower`
- **Что она делает**: Формирует строку «Медленнее на X» при сравнении с предыдущей загрузкой.
- **Какие входные данные**: `delta` (`string`): Значение параметра `delta` (string).
- **Какие на выходе**: `string`: Текстовая строка с результатом операции или сообщением.

#### `LocalizationService.GetSnapshotDiffTitle` (строка 721)
- **Название функции**: `GetSnapshotDiffTitle`
- **Что она делает**: Формирует заголовок окна диффа со снимком.
- **Какие входные данные**: `snapshotName` (`string`): Значение параметра `snapshotName` (string).
- **Какие на выходе**: `string`: Текстовая строка с результатом операции или сообщением.

#### `LocalizationService.GetSnapshotActiveComparison` (строка 725)
- **Название функции**: `GetSnapshotActiveComparison`
- **Что она делает**: Формирует строку активного сравнения со снимком.
- **Какие входные данные**: `name` (`string`): Значение параметра `name` (string).; `added` (`int`): Значение параметра `added` (int).; `modified` (`int`): Значение параметра `modified` (int).
- **Какие на выходе**: `string`: Текстовая строка с результатом операции или сообщением.

#### `LocalizationService.GetStatusWaitingServiceReady` (строка 729)
- **Название функции**: `GetStatusWaitingServiceReady`
- **Что она делает**: Формирует строку ожидания готовности службы.
- **Какие входные данные**: `seconds` (`int`): Значение параметра `seconds` (int).
- **Какие на выходе**: `string`: Текстовая строка с результатом операции или сообщением.

#### `LocalizationService.GetStatusConnectingAttempt` (строка 733)
- **Название функции**: `GetStatusConnectingAttempt`
- **Что она делает**: Формирует строку попытки подключения к службе.
- **Какие входные данные**: `attempt` (`int`): Значение параметра `attempt` (int).; `total` (`int`): Значение параметра `total` (int).
- **Какие на выходе**: `string`: Текстовая строка с результатом операции или сообщением.

#### `LocalizationService.ChangeLanguage` (строка 865)
- **Название функции**: `ChangeLanguage`
- **Что она делает**: Переключает язык интерфейса и оповещает подписчиков через INotifyPropertyChanged.
- **Какие входные данные**: `cultureCode` (`string`): Код культуры (например, "ru", "en").
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `LocalizationService.RaiseAllChanged` (строка 875)
- **Название функции**: `RaiseAllChanged`
- **Что она делает**: Принудительно оповещает всех подписчиков об изменении всех строк (для обновления привязок).
- **Какие входные данные**: Входные данные отсутствуют (параметры не требуются).
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

---

### Файл: `StartupMonitor.UI\Services\LogReaderService.cs`

#### `LogReaderService.ReadAllLogs` (строка 31)
- **Название функции**: `ReadAllLogs`
- **Что она делает**: Читает все логи (службы + UI), сортирует по времени (свежие сверху) и обрезает до MaxLines.
- **Какие входные данные**: `serviceDir` (`string`): Корневая папка, в которой искать подпапку logs/.
- **Какие на выходе**: `List<LogEntry>`: Список записей лога, отсортированный от новых к старым.

#### `LogReaderService.FindLatestServiceLog` (строка 62)
- **Название функции**: `FindLatestServiceLog`
- **Что она делает**: Находит самый свежий файл лога службы по маске service-*.log.
- **Какие входные данные**: `serviceDir` (`string`): Корневая папка с подпапкой logs/.
- **Какие на выходе**: `string?`: Полный путь к файлу или null, если логов нет.

#### `LogReaderService.FindLatestServiceLogs` (строка 71)
- **Название функции**: `FindLatestServiceLogs`
- **Что она делает**: Находит последние файлы логов службы по маске service-*.log (от новых к старым).
- **Какие входные данные**: `serviceDir` (`string`): Корневая папка с подпапкой logs/.; `maxFiles` (`int`): Максимальное количество файлов для чтения.
- **Какие на выходе**: `List<string>`: Список путей к файлам логов.

#### `LogReaderService.ReadServiceLog` (строка 85)
- **Название функции**: `ReadServiceLog`
- **Что она делает**: Разбирает файл лога службы в коллекцию LogEntry с использованием ServiceLogRegex.
- **Какие входные данные**: `filePath` (`string`): Полный путь к файлу лога службы.
- **Какие на выходе**: `List<LogEntry>`: Список распарсенных записей лога.

#### `LogReaderService.ReadUiLog` (строка 135)
- **Название функции**: `ReadUiLog`
- **Что она делает**: Разбирает файл лога UI в коллекцию LogEntry с использованием UiLogWithDateRegex или UiLogRegex.
- **Какие входные данные**: `filePath` (`string`): Полный путь к ui-debug.log.; `today` (`DateTime`): Дата сегодня (для конструирования временной метки из устаревших строк без даты).
- **Какие на выходе**: `List<LogEntry>`: Список распарсенных записей лога.

#### `LogReaderService.Filter` (строка 214)
- **Название функции**: `Filter`
- **Что она делает**: Фильтрует записи лога по текстовому запросу (поиск в FullLine, Message, Level, Source).
- **Какие входные данные**: `entries` (`List<LogEntry>`): Исходный список записей.; `searchText` (`string`): Текст для поиска (регистронезависимый).
- **Какие на выходе**: `List<LogEntry>`: Отфильтрованный список.

#### `LogReaderService.ReadTail` (строка 231)
- **Название функции**: `ReadTail`
- **Что она делает**: Читает хвост файла (последние maxLines строк), используя общий доступ на чтение/запись.
- **Какие входные данные**: `filePath` (`string`): Полный путь к файлу.; `maxLines` (`int`): Максимальное количество возвращаемых строк.
- **Какие на выходе**: `IEnumerable<string>`: Последние строки файла (с конца).

---

### Файл: `StartupMonitor.UI\Services\PipeClient.cs`

#### `PipeClient.ConnectAsync` (строка 64)
- **Название функции**: `ConnectAsync`
- **Что она делает**: Подключается к службе по именованному каналу. В случае успеха запускает event-pipe.
- **Какие входные данные**: Входные данные отсутствуют (параметры не требуются).
- **Какие на выходе**: `Task`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `PipeClient.StartEventPipe` (строка 87)
- **Название функции**: `StartEventPipe`
- **Что она делает**: Запускает фоновое чтение event-канала для получения асинхронных уведомлений от службы.
- **Какие входные данные**: Входные данные отсутствуют (параметры не требуются).
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `PipeClient.ConnectAndReadEventsAsync` (строка 98)
- **Название функции**: `ConnectAndReadEventsAsync`
- **Что она делает**: Фоновый цикл: подключается к event-каналу и читает сообщения до отмены или разрыва.
- **Какие входные данные**: `cts` (`CancellationTokenSource`): Токен отмены для остановки цикла.
- **Какие на выходе**: `Task`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `PipeClient.SendAsync` (строка 155)
- **Название функции**: `SendAsync`
- **Что она делает**: Отправляет запрос службе и ожидает ответ с таймаутом.
- **Какие входные данные**: `request` (`PipeMessage`): Сообщение-запрос.
- **Какие на выходе**: `Task<PipeMessage?>`: Ответ службы или null при таймауте/разрыве.

#### `PipeClient.GetAllEntriesAsync` (строка 208)
- **Название функции**: `GetAllEntriesAsync`
- **Что она делает**: Запрашивает у службы полный список записей автозапуска.
- **Какие входные данные**: Входные данные отсутствуют (параметры не требуются).
- **Какие на выходе**: `Task<List<StartupEntry>>`: Коллекция или набор результирующих объектов.

#### `PipeClient.GetBlacklistAsync` (строка 286)
- **Название функции**: `GetBlacklistAsync`
- **Что она делает**: Запрашивает текущий чёрный список.
- **Какие входные данные**: Входные данные отсутствуют (параметры не требуются).
- **Какие на выходе**: `Task<List<BlacklistRule>>`: Коллекция или набор результирующих объектов.

#### `PipeClient.DisableEntryAsync` (строка 302)
- **Название функции**: `DisableEntryAsync`
- **Что она делает**: Отключает (помечает Disabled) запись автозапуска.
- **Какие входные данные**: `entryId` (`string`): Значение параметра `entryId` (string).
- **Какие на выходе**: `Task`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `PipeClient.EnableEntryAsync` (строка 312)
- **Название функции**: `EnableEntryAsync`
- **Что она делает**: Включает (помечает Enabled) запись автозапуска.
- **Какие входные данные**: `entryId` (`string`): Значение параметра `entryId` (string).
- **Какие на выходе**: `Task`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `PipeClient.AddToBlacklistAsync` (строка 322)
- **Название функции**: `AddToBlacklistAsync`
- **Что она делает**: Добавляет правило в чёрный список на стороне службы.
- **Какие входные данные**: `rule` (`BlacklistRule`): Значение параметра `rule` (BlacklistRule).
- **Какие на выходе**: `Task`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `PipeClient.RemoveFromBlacklistAsync` (строка 332)
- **Название функции**: `RemoveFromBlacklistAsync`
- **Что она делает**: Удаляет правило из чёрного списка по его идентификатору.
- **Какие входные данные**: `ruleId` (`string`): Значение параметра `ruleId` (string).
- **Какие на выходе**: `Task`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `PipeClient.GetStatusAsync` (строка 342)
- **Название функции**: `GetStatusAsync`
- **Что она делает**: Запрашивает текстовый статус службы.
- **Какие входные данные**: Входные данные отсутствуют (параметры не требуются).
- **Какие на выходе**: `Task<string?>`: Текстовая строка с результатом операции или сообщением.

#### `PipeClient.RemoveFromSystemAsync` (строка 353)
- **Название функции**: `RemoveFromSystemAsync`
- **Что она делает**: Удаляет запись из системы (реестр/файл) через службу.
- **Какие входные данные**: `entryId` (`string`): Значение параметра `entryId` (string).
- **Какие на выходе**: `Task<bool>`: Логический результат выполнения (`true` в случае успеха или соответствия условию, иначе `false`).

#### `PipeClient.GetRemovedEntriesAsync` (строка 364)
- **Название функции**: `GetRemovedEntriesAsync`
- **Что она делает**: Запрашивает список удалённых записей.
- **Какие входные данные**: Входные данные отсутствуют (параметры не требуются).
- **Какие на выходе**: `Task<List<RemovedEntry>>`: Коллекция или набор результирующих объектов.

#### `PipeClient.GetHistoryAsync` (строка 375)
- **Название функции**: `GetHistoryAsync`
- **Что она делает**: Запрашивает историю изменений.
- **Какие входные данные**: Входные данные отсутствуют (параметры не требуются).
- **Какие на выходе**: `Task<List<HistoryEntry>>`: Коллекция или набор результирующих объектов.

#### `PipeClient.GetRatingsAsync` (строка 386)
- **Название функции**: `GetRatingsAsync`
- **Что она делает**: Запрашивает словарь AI-рейтингов (Id → StartupRating).
- **Какие входные данные**: Входные данные отсутствуют (параметры не требуются).
- **Какие на выходе**: `Task<Dictionary<string, StartupRating>>`: Текстовая строка с результатом операции или сообщением.

#### `PipeClient.CleanStaleAsync` (строка 397)
- **Название функции**: `CleanStaleAsync`
- **Что она делает**: Запускает очистку устаревших записей на стороне службы.
- **Какие входные данные**: Входные данные отсутствуют (параметры не требуются).
- **Какие на выходе**: `Task`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `PipeClient.SetAiConfigAsync` (строка 403)
- **Название функции**: `SetAiConfigAsync`
- **Что она делает**: Отправляет службе настройки для AI-рейтинга (ключ API, URL, модель).
- **Какие входные данные**: `apiKey` (`string`): Значение параметра `apiKey` (string).; `apiUrl` (`string?`): Значение параметра `apiUrl` (string?).; `model` (`string?`): Значение параметра `model` (string?).
- **Какие на выходе**: `Task`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `PipeClient.SetKasperskyConfigAsync` (строка 490)
- **Название функции**: `SetKasperskyConfigAsync`
- **Что она делает**: Отправляет конфигурацию Kaspersky OpenTIP службе.
- **Какие входные данные**: `enabled` (`bool`): Значение параметра `enabled` (bool).; `apiKey` (`string`): Значение параметра `apiKey` (string).; `apiUrl` (`string`): Значение параметра `apiUrl` (string).
- **Какие на выходе**: `Task`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `PipeClient.SetAutoGuardConfigAsync` (строка 507)
- **Название функции**: `SetAutoGuardConfigAsync`
- **Что она делает**: Отправляет конфигурацию автоматических политик защиты Auto-Guards службе.
- **Какие входные данные**: `config` (`AutoGuardConfigPayload`): Значение параметра `config` (AutoGuardConfigPayload).
- **Какие на выходе**: `Task`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `PipeClient.TestKasperskyConnectionAsync` (строка 518)
- **Название функции**: `TestKasperskyConnectionAsync`
- **Что она делает**: Проверяет подключение к API Kaspersky OpenTIP через службу.
- **Какие входные данные**: `apiKey` (`string`): Значение параметра `apiKey` (string).; `apiUrl` (`string`): Значение параметра `apiUrl` (string).
- **Какие на выходе**: `Task<KasperskyTestResult>`: Результирующий объект или экземпляр структуры данных.

#### `PipeClient.ScanKasperskyEntryAsync` (строка 551)
- **Название функции**: `ScanKasperskyEntryAsync`
- **Что она делает**: Запрашивает сканирование отдельной записи через Kaspersky OpenTIP.
- **Какие входные данные**: `entryId` (`string`): Значение параметра `entryId` (string).
- **Какие на выходе**: `Task<StartupEntry?>`: Результирующий объект или экземпляр структуры данных.

#### `PipeClient.SetKillOnDisableAsync` (строка 572)
- **Название функции**: `SetKillOnDisableAsync`
- **Что она делает**: Устанавливает настройку "убивать процесс при отключении".
- **Какие входные данные**: `value` (`bool`): Значение параметра `value` (bool).
- **Какие на выходе**: `Task`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `PipeClient.SetCpuThrottleAsync` (строка 582)
- **Название функции**: `SetCpuThrottleAsync`
- **Что она делает**: Отправляет настройки троттлинга сканирования реестра по загрузке CPU.
- **Какие входные данные**: `enabled` (`bool`): Значение параметра `enabled` (bool).; `threshold` (`int`): Значение параметра `threshold` (int).
- **Какие на выходе**: `Task`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `PipeClient.SetStrictModeAsync` (строка 592)
- **Название функции**: `SetStrictModeAsync`
- **Что она делает**: Отправляет настройку строгого режима (блокировка новых записей).
- **Какие входные данные**: `enabled` (`bool`): Значение параметра `enabled` (bool).
- **Какие на выходе**: `Task`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `PipeClient.SetLanguageAsync` (строка 602)
- **Название функции**: `SetLanguageAsync`
- **Что она делает**: Синхронизирует язык интерфейса ("ru", "en") со службой.
- **Какие входные данные**: `lang` (`string`): Значение параметра `lang` (string).
- **Какие на выходе**: `Task`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `PipeClient.ReRateEntryAsync` (строка 612)
- **Название функции**: `ReRateEntryAsync`
- **Что она делает**: Запрашивает переоценку AI-рейтинга для указанной записи.
- **Какие входные данные**: `entryId` (`string`): Значение параметра `entryId` (string).
- **Какие на выходе**: `Task`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `PipeClient.RestoreEntryAsync` (строка 622)
- **Название функции**: `RestoreEntryAsync`
- **Что она делает**: Восстанавливает удалённую запись в системе.
- **Какие входные данные**: `removedId` (`string`): Значение параметра `removedId` (string).
- **Какие на выходе**: `Task<bool>`: Логический результат выполнения (`true` в случае успеха или соответствия условию, иначе `false`).

#### `PipeClient.SubscribeProcessMetricsAsync` (строка 633)
- **Название функции**: `SubscribeProcessMetricsAsync`
- **Что она делает**: Подписывается на регулярный поток метрик процессов (для вкладки Диспетчера задач).
- **Какие входные данные**: Входные данные отсутствуют (параметры не требуются).
- **Какие на выходе**: `Task`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `PipeClient.UnsubscribeProcessMetricsAsync` (строка 642)
- **Название функции**: `UnsubscribeProcessMetricsAsync`
- **Что она делает**: Отписывается от потока метрик процессов (при уходе с вкладки, снижая CPU службы до 0%).
- **Какие входные данные**: Входные данные отсутствуют (параметры не требуются).
- **Какие на выходе**: `Task`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `PipeClient.GetProcessesAsync` (строка 651)
- **Название функции**: `GetProcessesAsync`
- **Что она делает**: Запрашивает актуальный снимок процессов и системной нагрузки.
- **Какие входные данные**: Входные данные отсутствуют (параметры не требуются).
- **Какие на выходе**: `Task<SystemPerformanceSnapshot?>`: Результирующий объект или экземпляр структуры данных.

#### `PipeClient.KillProcessAsync` (строка 667)
- **Название функции**: `KillProcessAsync`
- **Что она делает**: Завершает процесс по идентификатору (PID).
- **Какие входные данные**: `pid` (`int`): Значение параметра `pid` (int).; `processName` (`string?`): Значение параметра `processName` (string?).
- **Какие на выходе**: `Task<ProcessActionResultPayload?>`: Результирующий объект или экземпляр структуры данных.

#### `PipeClient.BlockProcessAsync` (строка 688)
- **Название функции**: `BlockProcessAsync`
- **Что она делает**: Блокирует процесс в Чёрном списке и завершает его.
- **Какие входные данные**: `pid` (`int`): Значение параметра `pid` (int).; `filePath` (`string`): Значение параметра `filePath` (string).; `processName` (`string?`): Значение параметра `processName` (string?).
- **Какие на выходе**: `Task<ProcessActionResultPayload?>`: Результирующий объект или экземпляр структуры данных.

#### `PipeClient.SuspendProcessAsync` (строка 709)
- **Название функции**: `SuspendProcessAsync`
- **Что она делает**: Приостанавливает выполнение процесса по PID.
- **Какие входные данные**: `pid` (`int`): Значение параметра `pid` (int).; `processName` (`string?`): Значение параметра `processName` (string?).
- **Какие на выходе**: `Task<ProcessActionResultPayload?>`: Результирующий объект или экземпляр структуры данных.

#### `PipeClient.ResumeProcessAsync` (строка 730)
- **Название функции**: `ResumeProcessAsync`
- **Что она делает**: Возобновляет выполнение ранее приостановленного процесса по PID.
- **Какие входные данные**: `pid` (`int`): Значение параметра `pid` (int).; `processName` (`string?`): Значение параметра `processName` (string?).
- **Какие на выходе**: `Task<ProcessActionResultPayload?>`: Результирующий объект или экземпляр структуры данных.

#### `PipeClient.GetSnapshotsAsync` (строка 821)
- **Название функции**: `GetSnapshotsAsync`
- **Что она делает**: Запрашивает список всех сохранённых контрольных снимков.
- **Какие входные данные**: Входные данные отсутствуют (параметры не требуются).
- **Какие на выходе**: `Task<List<BaselineSnapshot>>`: Коллекция или набор результирующих объектов.

#### `PipeClient.PauseMonitoringAsync` (строка 907)
- **Название функции**: `PauseMonitoringAsync`
- **Что она делает**: Приостанавливает глобальный мониторинг автозапуска в службе.
- **Какие входные данные**: Входные данные отсутствуют (параметры не требуются).
- **Какие на выходе**: `Task<bool>`: Логический результат выполнения (`true` в случае успеха или соответствия условию, иначе `false`).

#### `PipeClient.ResumeMonitoringAsync` (строка 918)
- **Название функции**: `ResumeMonitoringAsync`
- **Что она делает**: Возобновляет глобальный мониторинг автозапуска в службе.
- **Какие входные данные**: Входные данные отсутствуют (параметры не требуются).
- **Какие на выходе**: `Task<bool>`: Логический результат выполнения (`true` в случае успеха или соответствия условию, иначе `false`).

#### `PipeClient.GetMonitoringStateAsync` (строка 929)
- **Название функции**: `GetMonitoringStateAsync`
- **Что она делает**: Запрашивает текущее состояние мониторинга (активен / приостановлен).
- **Какие входные данные**: Входные данные отсутствуют (параметры не требуются).
- **Какие на выходе**: `Task<MonitoringStatePayload?>`: Результирующий объект или экземпляр структуры данных.

#### `PipeClient.GetServiceInfoAsync` (строка 952)
- **Название функции**: `GetServiceInfoAsync`
- **Что она делает**: Запрашивает расширенную информацию о службе (PID, версия, аптайм).
- **Какие входные данные**: Входные данные отсутствуют (параметры не требуются).
- **Какие на выходе**: `Task<ServiceInfoPayload?>`: Результирующий объект или экземпляр структуры данных.

#### `PipeClient.GetProcessNetworkConnectionsAsync` (строка 975)
- **Название функции**: `GetProcessNetworkConnectionsAsync`
- **Что она делает**: Запрашивает список активных сетевых соединений (сокетов) для процесса (или всех процессов).
- **Какие входные данные**: `pid` (`int`): Значение параметра `pid` (int).
- **Какие на выходе**: `Task<List<ProcessNetworkSocket>>`: Коллекция или набор результирующих объектов.

#### `PipeClient.GetProcessModulesAsync` (строка 998)
- **Название функции**: `GetProcessModulesAsync`
- **Что она делает**: Запрашивает список загруженных модулей (DLL) и дескрипторов (Handles) для процесса.
- **Какие входные данные**: `pid` (`int`): Значение параметра `pid` (int).; `includeHandles` (`bool`): Значение параметра `includeHandles` (bool).
- **Какие на выходе**: `Task<ProcessModulesResponse?>`: Результирующий объект или экземпляр структуры данных.

#### `PipeClient.Dispose` (строка 1038)
- **Название функции**: `Dispose`
- **Что она делает**: Освобождает все ресурсы: каналы, читатели, писатели, токены отмены.
- **Какие входные данные**: Входные данные отсутствуют (параметры не требуются).
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

---

### Файл: `StartupMonitor.UI\Services\ThemeService.cs`

#### `ThemeService.ApplyTheme` (строка 23)
- **Название функции**: `ApplyTheme`
- **Что она делает**: Применяет выбранную тему (Light или Dark) и обновляет все кисти в ресурсах приложения.
- **Какие входные данные**: `theme` (`ApplicationTheme`): Значение параметра `theme` (ApplicationTheme).; `force` (`bool`): Значение параметра `force` (bool).
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `ThemeService.ApplyDarkTheme` (строка 46)
- **Название функции**: `ApplyDarkTheme`
- **Что она делает**: Палитра для тёмной темы (сохраняет эталонное оформление, одобренное пользователем).
- **Какие входные данные**: `res` (`ResourceDictionary`): Значение параметра `res` (ResourceDictionary).
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `ThemeService.ApplyLightTheme` (строка 113)
- **Название функции**: `ApplyLightTheme`
- **Что она делает**: Палитра для светлой темы (чистота, контрастность, читаемость текста и карточек).
- **Какие входные данные**: `res` (`ResourceDictionary`): Значение параметра `res` (ResourceDictionary).
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `ThemeService.CreateSolidBrush` (строка 193)
- **Название функции**: `CreateSolidBrush`
- **Что она делает**: CreateSolidBrush: Выполняет операцию CreateSolidBrush.
- **Какие входные данные**: `a` (`byte`): Входные данные: значение параметра a (byte).; `r` (`byte`): Входные данные: значение параметра r (byte).; `g` (`byte`): Входные данные: значение параметра g (byte).; `b` (`byte`): Входные данные: значение параметра b (byte).
- **Какие на выходе**: `SolidColorBrush`: Выходные данные: результирующее значение типа SolidColorBrush.

#### `ThemeService.CreateLinearBrush` (строка 204)
- **Название функции**: `CreateLinearBrush`
- **Что она делает**: CreateLinearBrush: Выполняет операцию CreateLinearBrush.
- **Какие входные данные**: `c1` (`Color`): Входные данные: значение параметра c1 (Color).; `c2` (`Color`): Входные данные: значение параметра c2 (Color).
- **Какие на выходе**: `LinearGradientBrush`: Выходные данные: результирующее значение типа LinearGradientBrush.

#### `ThemeService.CreateRadialBrush` (строка 218)
- **Название функции**: `CreateRadialBrush`
- **Что она делает**: CreateRadialBrush: Выполняет операцию CreateRadialBrush.
- **Какие входные данные**: `cx` (`double`): Входные данные: значение параметра cx (double).; `cy` (`double`): Входные данные: значение параметра cy (double).; `c1` (`Color`): Входные данные: значение параметра c1 (Color).; `c2` (`Color`): Входные данные: значение параметра c2 (Color).; `c3` (`Color`): Входные данные: значение параметра c3 (Color).
- **Какие на выходе**: `RadialGradientBrush`: Выходные данные: результирующее значение типа RadialGradientBrush.

#### `ThemeService.GradientStop` (строка 230)
- **Название функции**: `GradientStop`
- **Что она делает**: GradientStop: Выполняет операцию GradientStop.
- **Какие входные данные**: `c1`: Входные данные: значение параметра c1 ().; `0.0`: Входные данные: значение параметра 0.0 ().
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `ThemeService.GradientStop` (строка 234)
- **Название функции**: `GradientStop`
- **Что она делает**: GradientStop: Выполняет операцию GradientStop.
- **Какие входные данные**: `c2`: Входные данные: значение параметра c2 ().; `0.45`: Входные данные: значение параметра 0.45 ().
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `ThemeService.GradientStop` (строка 238)
- **Название функции**: `GradientStop`
- **Что она делает**: GradientStop: Выполняет операцию GradientStop.
- **Какие входные данные**: `c3`: Входные данные: значение параметра c3 ().; `0.75`: Входные данные: значение параметра 0.75 ().
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `ThemeService.GradientStop` (строка 242)
- **Название функции**: `GradientStop`
- **Что она делает**: GradientStop: Выполняет операцию GradientStop.
- **Какие входные данные**: `0)` (`Color.FromArgb(0, 0, 0,`): Входные данные: значение параметра 0) (Color.FromArgb(0, 0, 0,).; `1.0`: Входные данные: значение параметра 1.0 ().
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

---

### Файл: `StartupMonitor.UI\Services\TrayNotificationAggregator.cs`

#### `TrayNotificationAggregator.Enqueue` (строка 46)
- **Название функции**: `Enqueue`
- **Что она делает**: Добавляет уведомление в очередь агрегации. Если уведомление критическое (SystemDanger), оно отображается немедленно вне очереди.
- **Какие входные данные**: `title` (`string`): Значение параметра `title` (string).; `text` (`string`): Значение параметра `text` (string).; `entryId` (`string?`): Значение параметра `entryId` (string?).; `kind` (`TrayNotificationKind`): Значение параметра `kind` (TrayNotificationKind).
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `TrayNotificationAggregator.Flush` (строка 85)
- **Название функции**: `Flush`
- **Что она делает**: Немедленно сбрасывает накопленный буфер и отображает объединенное уведомление.
- **Какие входные данные**: Входные данные отсутствуют (параметры не требуются).
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `TrayNotificationAggregator.static` (строка 104)
- **Название функции**: `static`
- **Что она делает**: Интеллектуальное объединение пачки уведомлений по смысловым категориям.
- **Какие входные данные**: `Title` (`string`): Значение параметра `Title` (string).; `Text` (`string`): Значение параметра `Text` (string).; `EntryIds` (`IReadOnlyList<string>`): Значение параметра `EntryIds` (IReadOnlyList<string>).
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `TrayNotificationAggregator.Classify` (строка 180)
- **Название функции**: `Classify`
- **Что она делает**: Автоматическая классификация категории по заголовку (для обратной совместимости).
- **Какие входные данные**: `title` (`string`): Значение параметра `title` (string).
- **Какие на выходе**: `TrayNotificationKind`: Результирующий объект или экземпляр структуры данных.

#### `TrayNotificationAggregator.FormatCategoryTitle` (строка 215)
- **Название функции**: `FormatCategoryTitle`
- **Что она делает**: FormatCategoryTitle: Выполняет операцию FormatCategoryTitle.
- **Какие входные данные**: `kind` (`TrayNotificationKind`): Входные данные: значение параметра kind (TrayNotificationKind).; `count` (`int`): Входные данные: значение параметра count (int).; `loc` (`LocalizationService`): Входные данные: значение параметра loc (LocalizationService).
- **Какие на выходе**: `string`: Выходные данные: результирующее значение типа string.

#### `TrayNotificationAggregator.Truncate` (строка 235)
- **Название функции**: `Truncate`
- **Что она делает**: Truncate: Выполняет операцию Truncate.
- **Какие входные данные**: `s` (`string`): Входные данные: значение параметра s (string).; `max` (`int`): Входные данные: значение параметра max (int).
- **Какие на выходе**: `string`: Выходные данные: результирующее значение типа string.

#### `TrayNotificationAggregator.Dispose` (строка 242)
- **Название функции**: `Dispose`
- **Что она делает**: Dispose: Освобождает занятые ресурсы объекта.
- **Какие входные данные**: Входные данные отсутствуют (параметры не требуются).
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

---

### Файл: `StartupMonitor.UI\Services\WindowHelper.cs`

#### `WindowHelper.EnumWindowsProc` (строка 16)
- **Название функции**: `EnumWindowsProc`
- **Что она делает**: EnumWindowsProc: Выполняет операцию EnumWindowsProc.
- **Какие входные данные**: `hWnd` (`IntPtr`): Входные данные: значение параметра hWnd (IntPtr).; `lParam` (`IntPtr`): Входные данные: значение параметра lParam (IntPtr).
- **Какие на выходе**: `delegate bool`: Выходные данные: результирующее значение типа delegate bool.

#### `WindowHelper.EnumWindows` (строка 23)
- **Название функции**: `EnumWindows`
- **Что она делает**: Выполняет операцию EnumWindows в рамках подсистемы.
- **Какие входные данные**: `lpEnumFunc` (`EnumWindowsProc`): Входные данные: значение параметра lpEnumFunc (EnumWindowsProc).; `lParam` (`IntPtr`): Входные данные: значение параметра lParam (IntPtr).
- **Какие на выходе**: `extern bool`: Выходные данные: результирующее значение типа extern bool.

#### `WindowHelper.GetWindowThreadProcessId` (строка 30)
- **Название функции**: `GetWindowThreadProcessId`
- **Что она делает**: Получает или формирует WindowThreadProcessId.
- **Какие входные данные**: `hWnd` (`IntPtr`): Входные данные: значение параметра hWnd (IntPtr).; `lpdwProcessId` (`out uint`): Входные данные: значение параметра lpdwProcessId (out uint).
- **Какие на выходе**: `extern uint`: Выходные данные: результирующее значение типа extern uint.

#### `WindowHelper.IsWindowVisible` (строка 37)
- **Название функции**: `IsWindowVisible`
- **Что она делает**: Проверяет состояние или признак WindowVisible (возвращает логический флаг).
- **Какие входные данные**: `hWnd` (`IntPtr`): Входные данные: значение параметра hWnd (IntPtr).
- **Какие на выходе**: `extern bool`: Выходные данные: результирующее значение типа extern bool.

#### `WindowHelper.GetWindowTextLength` (строка 43)
- **Название функции**: `GetWindowTextLength`
- **Что она делает**: Получает или формирует WindowTextLength.
- **Какие входные данные**: `hWnd` (`IntPtr`): Входные данные: значение параметра hWnd (IntPtr).
- **Какие на выходе**: `extern int`: Выходные данные: результирующее значение типа extern int.

#### `WindowHelper.GetShellWindow` (строка 48)
- **Название функции**: `GetShellWindow`
- **Что она делает**: Получает или формирует ShellWindow.
- **Какие входные данные**: Входные данные отсутствуют (параметры не требуются).
- **Какие на выходе**: `extern IntPtr`: Выходные данные: результирующее значение типа extern IntPtr.

#### `WindowHelper.GetWindowLong` (строка 55)
- **Название функции**: `GetWindowLong`
- **Что она делает**: Получает или формирует WindowLong.
- **Какие входные данные**: `hWnd` (`IntPtr`): Входные данные: значение параметра hWnd (IntPtr).; `nIndex` (`int`): Входные данные: значение параметра nIndex (int).
- **Какие на выходе**: `extern int`: Выходные данные: результирующее значение типа extern int.

#### `WindowHelper.GetProcessIdsWithWindows` (строка 64)
- **Название функции**: `GetProcessIdsWithWindows`
- **Что она делает**: Возвращает набор идентификаторов процессов (PID), имеющих видимые окна с заголовком на рабочем столе. Выполняется практически мгновенно (&lt; 1 мс).
- **Какие входные данные**: Входные данные отсутствуют (параметры не требуются).
- **Какие на выходе**: `HashSet<int>`: Числовой результат, количество обработанных элементов или метрика.

---

### Файл: `StartupMonitor.UI\ViewModels\MainViewModel.cs`

#### `MainViewModel.OnIsConnectedChanged` (строка 86)
- **Название функции**: `OnIsConnectedChanged`
- **Что она делает**: Обрабатывает событие жизненного цикла или действие пользователя (OnIsConnectedChanged).
- **Какие входные данные**: `value` (`bool`): Значение параметра `value` (bool).
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `MainViewModel.OnIsMonitoringPausedChanged` (строка 107)
- **Название функции**: `OnIsMonitoringPausedChanged`
- **Что она делает**: Обрабатывает событие жизненного цикла или действие пользователя (OnIsMonitoringPausedChanged).
- **Какие входные данные**: `value` (`bool`): Значение параметра `value` (bool).
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `MainViewModel.SelectOption` (строка 171)
- **Название функции**: `SelectOption`
- **Что она делает**: Запись для отображения выпадающего списка (Display/Code).
- **Какие входные данные**: `Display` (`string`): Значение параметра `Display` (string).; `Code` (`string`): Значение параметра `Code` (string).
- **Какие на выходе**: `record`: Результирующий объект или экземпляр структуры данных.

#### `MainViewModel.OnSearchTextChanged` (строка 221)
- **Название функции**: `OnSearchTextChanged`
- **Что она делает**: Вызывается при изменении текста поиска — применяет фильтр с задержкой 250 мс.
- **Какие входные данные**: `value` (`string`): Значение параметра `value` (string).
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `MainViewModel.ClearSearch` (строка 232)
- **Название функции**: `ClearSearch`
- **Что она делает**: Очищает строку поиска (кнопка-крестик в поле поиска).
- **Какие входные данные**: Входные данные отсутствуют (параметры не требуются).
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `MainViewModel.SortBy` (строка 242)
- **Название функции**: `SortBy`
- **Что она делает**: Сортирует записи по указанной колонке; повторный вызов по той же колонке переключает направление.
- **Какие входные данные**: `column` (`string`): Имя колонки: "name", "date", "status", "rating", "kaspersky".
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `MainViewModel.ApplySort` (строка 264)
- **Название функции**: `ApplySort`
- **Что она делает**: Применяет текущий порядок сортировки к _allEntries (по колонке и направлению).
- **Какие входные данные**: Входные данные отсутствуют (параметры не требуются).
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `MainViewModel.InitOptions` (строка 295)
- **Название функции**: `InitOptions`
- **Что она делает**: Инициализирует выпадающие списки языка и темы переведёнными названиями.
- **Какие входные данные**: Входные данные отсутствуют (параметры не требуются).
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `MainViewModel.OnSelectedThemeCodeChanged` (строка 309)
- **Название функции**: `OnSelectedThemeCodeChanged`
- **Что она делает**: Применяет тему при её изменении пользователем; для system включает watcher системной темы.
- **Какие входные данные**: `value` (`string`): Значение параметра `value` (string).
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `MainViewModel.OnSelectedLanguageCodeChanged` (строка 330)
- **Название функции**: `OnSelectedLanguageCodeChanged`
- **Что она делает**: Меняет язык интерфейса и обновляет локализованные тексты в выпадающих списках.
- **Какие входные данные**: `value` (`string`): Значение параметра `value` (string).
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `MainViewModel.RefreshLocalizedOptions` (строка 339)
- **Название функции**: `RefreshLocalizedOptions`
- **Что она делает**: Перестраивает выпадающие списки с новыми локализованными названиями, сохраняя выбранные значения.
- **Какие входные данные**: Входные данные отсутствуют (параметры не требуются).
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `MainViewModel.IsWindowsThemeLight` (строка 376)
- **Название функции**: `IsWindowsThemeLight`
- **Что она делает**: Определяет, включена ли светлая тема в Windows (по реестру).
- **Какие входные данные**: Входные данные отсутствуют (параметры не требуются).
- **Какие на выходе**: `bool`: true, если Windows использует светлую тему.

#### `MainViewModel.ApplySystemTheme` (строка 389)
- **Название функции**: `ApplySystemTheme`
- **Что она делает**: Применяет тему Windows (светлую/тёмную) к приложению.
- **Какие входные данные**: Входные данные отсутствуют (параметры не требуются).
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `MainViewModel.StartSystemThemeWatcher` (строка 396)
- **Название функции**: `StartSystemThemeWatcher`
- **Что она делает**: Запускает таймер опроса системной темы (каждые 2 с) для автоматического переключения.
- **Какие входные данные**: Входные данные отсутствуют (параметры не требуются).
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `MainViewModel.StopSystemThemeWatcher` (строка 406)
- **Название функции**: `StopSystemThemeWatcher`
- **Что она делает**: Останавливает таймер опроса системной темы.
- **Какие входные данные**: Входные данные отсутствуют (параметры не требуются).
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `MainViewModel.Dispose` (строка 413)
- **Название функции**: `Dispose`
- **Что она делает**: Освобождает ресурсы: таймеры, токены, pipe-клиент.
- **Какие входные данные**: Входные данные отсутствуют (параметры не требуются).
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `MainViewModel.SuspendCpuTimer` (строка 430)
- **Название функции**: `SuspendCpuTimer`
- **Что она делает**: Приостанавливает замер CPU (глубокий покой UI в трее, п. 2.1): 0% CPU от таймера.
- **Какие входные данные**: Входные данные отсутствуют (параметры не требуются).
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `MainViewModel.ResumeCpuTimer` (строка 437)
- **Название функции**: `ResumeCpuTimer`
- **Что она делает**: Возобновляет замер CPU при восстановлении окна из трея.
- **Какие входные данные**: Входные данные отсутствуют (параметры не требуются).
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `MainViewModel.OnNotifyOnNewEntryChanged` (строка 451)
- **Название функции**: `OnNotifyOnNewEntryChanged`
- **Что она делает**: Сохраняет настройку уведомлений при изменении.
- **Какие входные данные**: `value` (`bool`): Значение параметра `value` (bool).
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `MainViewModel.OnMinimizeToTrayChanged` (строка 461)
- **Название функции**: `OnMinimizeToTrayChanged`
- **Что она делает**: Сохраняет настройку сворачивания в трей.
- **Какие входные данные**: `value` (`bool`): Значение параметра `value` (bool).
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `MainViewModel.OnStartWithWindowsChanged` (строка 471)
- **Название функции**: `OnStartWithWindowsChanged`
- **Что она делает**: Обновляет автозапуск UI в реестре и сохраняет настройку.
- **Какие входные данные**: `value` (`bool`): Значение параметра `value` (bool).
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `MainViewModel.OnStrictModeChanged` (строка 482)
- **Название функции**: `OnStrictModeChanged`
- **Что она делает**: Сохраняет настройку строгого режима и отправляет её службе.
- **Какие входные данные**: `value` (`bool`): Значение параметра `value` (bool).
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `MainViewModel.OnCpuThrottleEnabledChanged` (строка 493)
- **Название функции**: `OnCpuThrottleEnabledChanged`
- **Что она делает**: Сохраняет настройку троттлинга и отправляет службе.
- **Какие входные данные**: `value` (`bool`): Значение параметра `value` (bool).
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `MainViewModel.OnCpuThrottleThresholdChanged` (строка 504)
- **Название функции**: `OnCpuThrottleThresholdChanged`
- **Что она делает**: Сохраняет порог загрузки CPU и отправляет службе.
- **Какие входные данные**: `value` (`int`): Значение параметра `value` (int).
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `MainViewModel.OnAutoBlockOnRemoveChanged` (строка 527)
- **Название функции**: `OnAutoBlockOnRemoveChanged`
- **Что она делает**: Сохраняет настройку автоблокировки при удалении.
- **Какие входные данные**: `value` (`bool`): Значение параметра `value` (bool).
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `MainViewModel.OnKillOnDisableChanged` (строка 537)
- **Название функции**: `OnKillOnDisableChanged`
- **Что она делает**: Сохраняет настройку и отправляет её службе.
- **Какие входные данные**: `value` (`bool`): Значение параметра `value` (bool).
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `MainViewModel.OnSilentModeChanged` (строка 548)
- **Название функции**: `OnSilentModeChanged`
- **Что она делает**: Сохраняет настройку тихого режима.
- **Какие входные данные**: `value` (`bool`): Значение параметра `value` (bool).
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `MainViewModel.OnAiEnabledChanged` (строка 558)
- **Название функции**: `OnAiEnabledChanged`
- **Что она делает**: Сохраняет настройку AI.
- **Какие входные данные**: `value` (`bool`): Значение параметра `value` (bool).
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `MainViewModel.OnAiApiKeyChanged` (строка 568)
- **Название функции**: `OnAiApiKeyChanged`
- **Что она делает**: Сохраняет ключ и отправляет его службе через pipe.
- **Какие входные данные**: `value` (`string`): Значение параметра `value` (string).
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `MainViewModel.OnAiApiUrlChanged` (строка 585)
- **Название функции**: `OnAiApiUrlChanged`
- **Что она делает**: Обрабатывает событие жизненного цикла или действие пользователя (OnAiApiUrlChanged).
- **Какие входные данные**: `value` (`string`): Значение параметра `value` (string).
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `MainViewModel.OnAiModelChanged` (строка 595)
- **Название функции**: `OnAiModelChanged`
- **Что она делает**: Обрабатывает событие жизненного цикла или действие пользователя (OnAiModelChanged).
- **Какие входные данные**: `value` (`string`): Значение параметра `value` (string).
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `MainViewModel.TestAiConnectionAsync` (строка 615)
- **Название функции**: `TestAiConnectionAsync`
- **Что она делает**: Тестирует соединение с настроенным AI эндпоинтом.
- **Какие входные данные**: Входные данные отсутствуют (параметры не требуются).
- **Какие на выходе**: `Task`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `MainViewModel.OnKasperskyEnabledChanged` (строка 642)
- **Название функции**: `OnKasperskyEnabledChanged`
- **Что она делает**: Обрабатывает событие жизненного цикла или действие пользователя (OnKasperskyEnabledChanged).
- **Какие входные данные**: `value` (`bool`): Значение параметра `value` (bool).
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `MainViewModel.OnKasperskyApiKeyChanged` (строка 652)
- **Название функции**: `OnKasperskyApiKeyChanged`
- **Что она делает**: Обрабатывает событие жизненного цикла или действие пользователя (OnKasperskyApiKeyChanged).
- **Какие входные данные**: `value` (`string`): Значение параметра `value` (string).
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `MainViewModel.OnKasperskyApiUrlChanged` (строка 662)
- **Название функции**: `OnKasperskyApiUrlChanged`
- **Что она делает**: Обрабатывает событие жизненного цикла или действие пользователя (OnKasperskyApiUrlChanged).
- **Какие входные данные**: `value` (`string`): Значение параметра `value` (string).
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `MainViewModel.TestKasperskyConnectionAsync` (строка 682)
- **Название функции**: `TestKasperskyConnectionAsync`
- **Что она делает**: Тестирует соединение с настроенным Kaspersky OpenTIP API.
- **Какие входные данные**: Входные данные отсутствуют (параметры не требуются).
- **Какие на выходе**: `Task`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `MainViewModel.OnGuardBlockDangerousCliChanged` (строка 734)
- **Название функции**: `OnGuardBlockDangerousCliChanged`
- **Что она делает**: Обрабатывает событие жизненного цикла или действие пользователя (OnGuardBlockDangerousCliChanged).
- **Какие входные данные**: `value` (`bool`): Значение параметра `value` (bool).
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `MainViewModel.OnGuardBlockTempPathChanged` (строка 744)
- **Название функции**: `OnGuardBlockTempPathChanged`
- **Что она делает**: Обрабатывает событие жизненного цикла или действие пользователя (OnGuardBlockTempPathChanged).
- **Какие входные данные**: `value` (`bool`): Значение параметра `value` (bool).
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `MainViewModel.OnGuardBlockScriptsChanged` (строка 754)
- **Название функции**: `OnGuardBlockScriptsChanged`
- **Что она делает**: Обрабатывает событие жизненного цикла или действие пользователя (OnGuardBlockScriptsChanged).
- **Какие входные данные**: `value` (`bool`): Значение параметра `value` (bool).
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `MainViewModel.OnGuardBlockUnsignedChanged` (строка 764)
- **Название функции**: `OnGuardBlockUnsignedChanged`
- **Что она делает**: Обрабатывает событие жизненного цикла или действие пользователя (OnGuardBlockUnsignedChanged).
- **Какие входные данные**: `value` (`bool`): Значение параметра `value` (bool).
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `MainViewModel.OnGuardBlockNewServicesChanged` (строка 774)
- **Название функции**: `OnGuardBlockNewServicesChanged`
- **Что она делает**: Обрабатывает событие жизненного цикла или действие пользователя (OnGuardBlockNewServicesChanged).
- **Какие входные данные**: `value` (`bool`): Значение параметра `value` (bool).
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `MainViewModel.SyncAutoGuardConfig` (строка 781)
- **Название функции**: `SyncAutoGuardConfig`
- **Что она делает**: SyncAutoGuardConfig: Выполняет операцию SyncAutoGuardConfig.
- **Какие входные данные**: Входные данные отсутствуют (параметры не требуются).
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `MainViewModel.ScanKasperskyEntryAsync` (строка 795)
- **Название функции**: `ScanKasperskyEntryAsync`
- **Что она делает**: Принудительно сканирует выбранную запись автозапуска в Kaspersky OpenTIP.
- **Какие входные данные**: `param` (`object?`): Значение параметра `param` (object?).
- **Какие на выходе**: `Task`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `MainViewModel.OnTelegramEnabledChanged` (строка 827)
- **Название функции**: `OnTelegramEnabledChanged`
- **Что она делает**: Обрабатывает событие жизненного цикла или действие пользователя (OnTelegramEnabledChanged).
- **Какие входные данные**: `value` (`bool`): Значение параметра `value` (bool).
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `MainViewModel.OnTelegramBotTokenChanged` (строка 837)
- **Название функции**: `OnTelegramBotTokenChanged`
- **Что она делает**: Обрабатывает событие жизненного цикла или действие пользователя (OnTelegramBotTokenChanged).
- **Какие входные данные**: `value` (`string`): Значение параметра `value` (string).
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `MainViewModel.OnTelegramChatIdChanged` (строка 847)
- **Название функции**: `OnTelegramChatIdChanged`
- **Что она делает**: Обрабатывает событие жизненного цикла или действие пользователя (OnTelegramChatIdChanged).
- **Какие входные данные**: `value` (`string`): Значение параметра `value` (string).
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `MainViewModel.OnDiscordWebhookEnabledChanged` (строка 857)
- **Название функции**: `OnDiscordWebhookEnabledChanged`
- **Что она делает**: Обрабатывает событие жизненного цикла или действие пользователя (OnDiscordWebhookEnabledChanged).
- **Какие входные данные**: `value` (`bool`): Значение параметра `value` (bool).
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `MainViewModel.OnDiscordWebhookUrlChanged` (строка 867)
- **Название функции**: `OnDiscordWebhookUrlChanged`
- **Что она делает**: Обрабатывает событие жизненного цикла или действие пользователя (OnDiscordWebhookUrlChanged).
- **Какие входные данные**: `value` (`string`): Значение параметра `value` (string).
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `MainViewModel.OnRemoteNotifyOnNewEntryChanged` (строка 877)
- **Название функции**: `OnRemoteNotifyOnNewEntryChanged`
- **Что она делает**: Обрабатывает событие жизненного цикла или действие пользователя (OnRemoteNotifyOnNewEntryChanged).
- **Какие входные данные**: `value` (`bool`): Значение параметра `value` (bool).
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `MainViewModel.OnRemoteNotifyOnThreatChanged` (строка 887)
- **Название функции**: `OnRemoteNotifyOnThreatChanged`
- **Что она делает**: Обрабатывает событие жизненного цикла или действие пользователя (OnRemoteNotifyOnThreatChanged).
- **Какие входные данные**: `value` (`bool`): Значение параметра `value` (bool).
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `MainViewModel.OnRemoteNotifyOnBlockedChanged` (строка 897)
- **Название функции**: `OnRemoteNotifyOnBlockedChanged`
- **Что она делает**: Обрабатывает событие жизненного цикла или действие пользователя (OnRemoteNotifyOnBlockedChanged).
- **Какие входные данные**: `value` (`bool`): Значение параметра `value` (bool).
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `MainViewModel.SyncRemoteNotifications` (строка 928)
- **Название функции**: `SyncRemoteNotifications`
- **Что она делает**: Синхронизирует параметры удалённых уведомлений со службой Windows.
- **Какие входные данные**: Входные данные отсутствуют (параметры не требуются).
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `MainViewModel.TestTelegramAsync` (строка 943)
- **Название функции**: `TestTelegramAsync`
- **Что она делает**: Отправляет тестовое уведомление в Telegram.
- **Какие входные данные**: Входные данные отсутствуют (параметры не требуются).
- **Какие на выходе**: `Task`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `MainViewModel.TestDiscordAsync` (строка 969)
- **Название функции**: `TestDiscordAsync`
- **Что она делает**: Отправляет тестовое уведомление в Discord Webhook.
- **Какие входные данные**: Входные данные отсутствуют (параметры не требуются).
- **Какие на выходе**: `Task`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `MainViewModel.OnCurrentFilterChanged` (строка 997)
- **Название функции**: `OnCurrentFilterChanged`
- **Что она делает**: При смене фильтра переприменяет фильтрацию списка.
- **Какие входные данные**: `value` (`string`): Значение параметра `value` (string).
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `MainViewModel.OnLogSearchTextChanged` (строка 1023)
- **Название функции**: `OnLogSearchTextChanged`
- **Что она делает**: При изменении текста поиска по логам применяет фильтр.
- **Какие входные данные**: `value` (`string`): Значение параметра `value` (string).
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `MainViewModel.ApplyLogFilter` (строка 1032)
- **Название функции**: `ApplyLogFilter`
- **Что она делает**: Фильтрует _allLogEntries по _logSearchText и обновляет LogEntries.
- **Какие входные данные**: Входные данные отсутствуют (параметры не требуются).
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `MainViewModel.LoadThemeFromFile` (строка 1088)
- **Название функции**: `LoadThemeFromFile`
- **Что она делает**: Загружает тему из ui-settings.json. Возвращает "dark" по умолчанию.
- **Какие входные данные**: Входные данные отсутствуют (параметры не требуются).
- **Какие на выходе**: `string`: Текстовая строка с результатом операции или сообщением.

#### `MainViewModel.LoadLanguageFromFile` (строка 1106)
- **Название функции**: `LoadLanguageFromFile`
- **Что она делает**: Загружает язык из ui-settings.json. Возвращает "ru" по умолчанию.
- **Какие входные данные**: Входные данные отсутствуют (параметры не требуются).
- **Какие на выходе**: `string`: Текстовая строка с результатом операции или сообщением.

#### `MainViewModel.LoadStartWithWindowsFromFile` (строка 1124)
- **Название функции**: `LoadStartWithWindowsFromFile`
- **Что она делает**: Загружает настройку автозапуска UI из файла конфигурации ui-settings.json (по умолчанию true).
- **Какие входные данные**: Входные данные отсутствуют (параметры не требуются).
- **Какие на выходе**: `bool`: Логический результат выполнения (`true` в случае успеха или соответствия условию, иначе `false`).

#### `MainViewModel.MainViewModel` (строка 1130)
- **Название функции**: `MainViewModel`
- **Что она делает**: Создаёт MainViewModel, инициализирует опции и подписывается на события PipeClient.
- **Какие входные данные**: `pipeClient` (`PipeClient`): Значение параметра `pipeClient` (PipeClient).
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `MainViewModel.OnPipeClientConnected` (строка 1217)
- **Название функции**: `OnPipeClientConnected`
- **Что она делает**: OnPipeClientConnected: Обрабатывает событие OnPipeClientConnected.
- **Какие входные данные**: `sender` (`object?`): Входные данные: значение параметра sender (object?).; `args` (`EventArgs`): Входные данные: значение параметра args (EventArgs).
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `MainViewModel.OnPipeClientEventsReceived` (строка 1251)
- **Название функции**: `OnPipeClientEventsReceived`
- **Что она делает**: OnPipeClientEventsReceived: Обрабатывает событие OnPipeClientEventsReceived.
- **Какие входные данные**: `sender` (`object?`): Входные данные: значение параметра sender (object?).; `batch` (`List<PipeMessage>`): Входные данные: значение параметра batch (List<PipeMessage>).
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `MainViewModel.OnPipeClientDisconnected` (строка 1325)
- **Название функции**: `OnPipeClientDisconnected`
- **Что она делает**: OnPipeClientDisconnected: Обрабатывает событие OnPipeClientDisconnected.
- **Какие входные данные**: `sender` (`object?`): Входные данные: значение параметра sender (object?).; `args` (`EventArgs`): Входные данные: значение параметра args (EventArgs).
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `MainViewModel.UpdateServiceStatusText` (строка 1338)
- **Название функции**: `UpdateServiceStatusText`
- **Что она делает**: Обновляет отображаемую текстовую строку статуса службы.
- **Какие входные данные**: Входные данные отсутствуют (параметры не требуются).
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `MainViewModel.UpdateServiceInfoAsync` (строка 1358)
- **Название функции**: `UpdateServiceInfoAsync`
- **Что она делает**: Запрашивает актуальную информацию о процессе службы (PID, версия, статус).
- **Какие входные данные**: Входные данные отсутствуют (параметры не требуются).
- **Какие на выходе**: `Task`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `MainViewModel.TogglePauseMonitoringAsync` (строка 1382)
- **Название функции**: `TogglePauseMonitoringAsync`
- **Что она делает**: Переключает состояние глобальной паузы мониторинга автозапуска.
- **Какие входные данные**: Входные данные отсутствуют (параметры не требуются).
- **Какие на выходе**: `Task`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `MainViewModel.SetInitialData` (строка 1434)
- **Название функции**: `SetInitialData`
- **Что она делает**: Устанавливает начальные данные (записи + чёрный список) при первой загрузке.
- **Какие входные данные**: `entries` (`List<StartupEntry>`): Значение параметра `entries` (List<StartupEntry>).; `rules` (`List<BlacklistRule>`): Значение параметра `rules` (List<BlacklistRule>).
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `MainViewModel.LoadDataIfChanged` (строка 1449)
- **Название функции**: `LoadDataIfChanged`
- **Что она делает**: Проверяет, изменились ли данные, и при необходимости обновляет список и уведомляет о новых записях.
- **Какие входные данные**: Входные данные отсутствуют (параметры не требуются).
- **Какие на выходе**: `Task`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `MainViewModel.ApplyBlacklistToEntries` (строка 1530)
- **Название функции**: `ApplyBlacklistToEntries`
- **Что она делает**: Проставляет статус Blocked записям, совпадающим с правилами чёрного списка по пути.
- **Какие входные данные**: `rules` (`List<BlacklistRule>`): Значение параметра `rules` (List<BlacklistRule>).
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `MainViewModel.HasChanges` (строка 1547)
- **Название функции**: `HasChanges`
- **Что она делает**: Сравнивает текущие данные с полученными — есть ли различия.
- **Какие входные данные**: `entries` (`List<StartupEntry>`): Значение параметра `entries` (List<StartupEntry>).; `rules` (`List<BlacklistRule>`): Значение параметра `rules` (List<BlacklistRule>).
- **Какие на выходе**: `bool`: true, если количество или содержимое записей/правил изменилось.

#### `MainViewModel.RefreshStats` (строка 1576)
- **Название функции**: `RefreshStats`
- **Что она делает**: Обновляет статистику (Total, Enabled, Blocked) на основе _allEntries.
- **Какие входные данные**: Входные данные отсутствуют (параметры не требуются).
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `MainViewModel.OpenSystemInfo` (строка 1611)
- **Название функции**: `OpenSystemInfo`
- **Что она делает**: Открывает диалоговое окно полной системной информации о компьютере.
- **Какие входные данные**: Входные данные отсутствуют (параметры не требуются).
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `MainViewModel.OpenSnapshots` (строка 1623)
- **Название функции**: `OpenSnapshots`
- **Что она делает**: Открывает окно управления и сравнения контрольных снимков системы.
- **Какие входные данные**: Входные данные отсутствуют (параметры не требуются).
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `MainViewModel.ResetSnapshotComparison` (строка 1630)
- **Название функции**: `ResetSnapshotComparison`
- **Что она делает**: Сбрасывает режим сравнения со снимком, очищая бейджи и восстанавливая стандартную фильтрацию.
- **Какие входные данные**: Входные данные отсутствуют (параметры не требуются).
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `MainViewModel.ApplySnapshotComparison` (строка 1649)
- **Название функции**: `ApplySnapshotComparison`
- **Что она делает**: Применяет дифференциальные результаты сравнения со снимком («Было — Стало»).
- **Какие входные данные**: `diff` (`SnapshotComparisonResult`): Значение параметра `diff` (SnapshotComparisonResult).
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `MainViewModel.ApplyFilter` (строка 1697)
- **Название функции**: `ApplyFilter`
- **Что она делает**: Фильтрует Entries по текущему фильтру (CurrentFilter) и поисковому запросу (SearchText).
- **Какие входные данные**: Входные данные отсутствуют (параметры не требуются).
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `MainViewModel.ConnectAsync` (строка 1757)
- **Название функции**: `ConnectAsync`
- **Что она делает**: Подключается к службе: запускает службу при необходимости и открывает pipe-соединение.
- **Какие входные данные**: Входные данные отсутствуют (параметры не требуются).
- **Какие на выходе**: `Task`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `MainViewModel.LoadDataAsync` (строка 1772)
- **Название функции**: `LoadDataAsync`
- **Что она делает**: Загружает данные (записи + чёрный список) и запускает фоновые циклы рейтингов и хешей.
- **Какие входные данные**: Входные данные отсутствуют (параметры не требуются).
- **Какие на выходе**: `Task`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `MainViewModel.LoadRatingsAsync` (строка 1813)
- **Название функции**: `LoadRatingsAsync`
- **Что она делает**: Фоновый опрос AI-рейтингов (каждые 5 с, макс 3 мин) до полной загрузки.
- **Какие входные данные**: `entries` (`List<StartupEntry>`): Значение параметра `entries` (List<StartupEntry>).; `ct` (`CancellationToken`): Значение параметра `ct` (CancellationToken).
- **Какие на выходе**: `Task`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `MainViewModel.LoadHashDataAsync` (строка 1858)
- **Название функции**: `LoadHashDataAsync`
- **Что она делает**: Фоновый опрос хеш-данных (CIRCL/Kaspersky) до полной загрузки (макс 60 с).
- **Какие входные данные**: `entries` (`List<StartupEntry>`): Значение параметра `entries` (List<StartupEntry>).; `ct` (`CancellationToken`): Значение параметра `ct` (CancellationToken).
- **Какие на выходе**: `Task`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `MainViewModel.SortByName` (строка 1902)
- **Название функции**: `SortByName`
- **Что она делает**: Сортирует по имени (RelayCommand для UI).
- **Какие входные данные**: `)`: Значение параметра `)`.
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `MainViewModel.SortByDate` (строка 1906)
- **Название функции**: `SortByDate`
- **Что она делает**: Сортирует по дате обнаружения.
- **Какие входные данные**: `)`: Значение параметра `)`.
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `MainViewModel.SortByPublisher` (строка 1910)
- **Название функции**: `SortByPublisher`
- **Что она делает**: Сортирует по издателю.
- **Какие входные данные**: `)`: Значение параметра `)`.
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `MainViewModel.SortByImpact` (строка 1914)
- **Название функции**: `SortByImpact`
- **Что она делает**: Сортирует по влиянию на запуск.
- **Какие входные данные**: `)`: Значение параметра `)`.
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `MainViewModel.SortByStatus` (строка 1918)
- **Название функции**: `SortByStatus`
- **Что она делает**: Сортирует по статусу.
- **Какие входные данные**: `)`: Значение параметра `)`.
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `MainViewModel.SortByRating` (строка 1922)
- **Название функции**: `SortByRating`
- **Что она делает**: Сортирует по AI-рейтингу.
- **Какие входные данные**: `)`: Значение параметра `)`.
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `MainViewModel.SortByKaspersky` (строка 1926)
- **Название функции**: `SortByKaspersky`
- **Что она делает**: Сортирует по зоне CIRCL.
- **Какие входные данные**: `)`: Значение параметра `)`.
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `MainViewModel.SetFilter` (строка 1929)
- **Название функции**: `SetFilter`
- **Что она делает**: Устанавливает фильтр и сохраняет его в настройки.
- **Какие входные данные**: `filter` (`string`): Значение параметра `filter` (string).
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `MainViewModel.ImpactSeverity` (строка 1936)
- **Название функции**: `ImpactSeverity`
- **Что она делает**: Возвращает числовой вес влияния на запуск для сортировки: Высокое (3) → Среднее (2) → Низкое (1).
- **Какие входные данные**: `loc` (`EntryLocation`): Значение параметра `loc` (EntryLocation).
- **Какие на выходе**: `int`: Числовой результат, количество обработанных элементов или метрика.

#### `MainViewModel.CirclSeverity` (строка 1946)
- **Название функции**: `CirclSeverity`
- **Что она делает**: Возвращает числовой вес зоны CIRCL для сортировки: Red (3) → Green (0).
- **Какие входные данные**: `zone` (`string?`): Значение параметра `zone` (string?).
- **Какие на выходе**: `int`: Числовой результат, количество обработанных элементов или метрика.

#### `MainViewModel.FilterAll` (строка 1957)
- **Название функции**: `FilterAll`
- **Что она делает**: Фильтр: показать все записи.
- **Какие входные данные**: `)`: Значение параметра `)`.
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `MainViewModel.FilterRegistry` (строка 1961)
- **Название функции**: `FilterRegistry`
- **Что она делает**: Фильтр: только записи из реестра.
- **Какие входные данные**: `)`: Значение параметра `)`.
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `MainViewModel.FilterFolder` (строка 1965)
- **Название функции**: `FilterFolder`
- **Что она делает**: Фильтр: только записи из папки автозапуска.
- **Какие входные данные**: `)`: Значение параметра `)`.
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `MainViewModel.FilterServices` (строка 1969)
- **Название функции**: `FilterServices`
- **Что она делает**: Фильтр: только службы.
- **Какие входные данные**: `)`: Значение параметра `)`.
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `MainViewModel.FilterTasks` (строка 1973)
- **Название функции**: `FilterTasks`
- **Что она делает**: Фильтр: только задачи планировщика.
- **Какие входные данные**: `)`: Значение параметра `)`.
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `MainViewModel.FilterRecent` (строка 1977)
- **Название функции**: `FilterRecent`
- **Что она делает**: Фильтр: записи, добавленные за последние 24 часа.
- **Какие входные данные**: `)`: Значение параметра `)`.
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `MainViewModel.FilterOther` (строка 1981)
- **Название функции**: `FilterOther`
- **Что она делает**: Фильтр: прочие расположения (не реестр/папка/служба/планировщик).
- **Какие входные данные**: `)`: Значение параметра `)`.
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `MainViewModel.LoadFilter` (строка 1984)
- **Название функции**: `LoadFilter`
- **Что она делает**: Загружает сохранённый фильтр из настроек.
- **Какие входные данные**: Входные данные отсутствуют (параметры не требуются).
- **Какие на выходе**: `string`: Текстовая строка с результатом операции или сообщением.

#### `MainViewModel.SaveFilter` (строка 1990)
- **Название функции**: `SaveFilter`
- **Что она делает**: Сохраняет выбранный фильтр в настройки.
- **Какие входные данные**: `filter` (`string`): Значение параметра `filter` (string).
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `MainViewModel.LoadSort` (строка 1996)
- **Название функции**: `LoadSort`
- **Что она делает**: Загружает настройки сортировки (колонка + направление) из файла.
- **Какие входные данные**: Входные данные отсутствуют (параметры не требуются).
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `MainViewModel.SaveSort` (строка 2003)
- **Название функции**: `SaveSort`
- **Что она делает**: Сохраняет текущие настройки сортировки в JSON-файл.
- **Какие входные данные**: Входные данные отсутствуют (параметры не требуются).
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `MainViewModel.SaveSetting` (строка 2079)
- **Название функции**: `SaveSetting`
- **Что она делает**: Сохраняет настройку в ui-settings.json (read-modify-write под блокировкой, атомарная запись).
- **Какие входные данные**: `key` (`string`): Значение параметра `key` (string).; `value` (`object`): Значение параметра `value` (object).
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `MainViewModel.DisableEntryAsync` (строка 2102)
- **Название функции**: `DisableEntryAsync`
- **Что она делает**: Отключает запись (EntryStatus.Disabled) через службу.
- **Какие входные данные**: `entryId` (`string`): Значение параметра `entryId` (string).
- **Какие на выходе**: `Task`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `MainViewModel.EnableEntryAsync` (строка 2113)
- **Название функции**: `EnableEntryAsync`
- **Что она делает**: Включает запись (EntryStatus.Enabled) через службу.
- **Какие входные данные**: `entryId` (`string`): Значение параметра `entryId` (string).
- **Какие на выходе**: `Task`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `MainViewModel.ToggleEntryAsync` (строка 2124)
- **Название функции**: `ToggleEntryAsync`
- **Что она делает**: Переключает состояние записи: Blocked → Enable, Delayed → Enable, Enabled → Disable, Disabled → Enable.
- **Какие входные данные**: `entryId` (`string`): Значение параметра `entryId` (string).
- **Какие на выходе**: `Task`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `MainViewModel.SetDelayedEntryAsync` (строка 2147)
- **Название функции**: `SetDelayedEntryAsync`
- **Что она делает**: Переводит запись в режим отложенного запуска с указанным интервалом задержки.
- **Какие входные данные**: `entryIdWithDelay` (`string`): Значение параметра `entryIdWithDelay` (string).
- **Какие на выходе**: `Task`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `MainViewModel.CancelDelayedEntryAsync` (строка 2179)
- **Название функции**: `CancelDelayedEntryAsync`
- **Что она делает**: Отменяет отложенный запуск записи и возвращает обычный автозапуск.
- **Какие входные данные**: `entryId` (`string`): Значение параметра `entryId` (string).
- **Какие на выходе**: `Task`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `MainViewModel.LaunchDelayedEntryAsync` (строка 2200)
- **Название функции**: `LaunchDelayedEntryAsync`
- **Что она делает**: Выполняет ручной тестовый запуск отложенной программы.
- **Какие входные данные**: `entryId` (`string`): Значение параметра `entryId` (string).
- **Какие на выходе**: `Task`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `MainViewModel.ExportReportAsync` (строка 2208)
- **Название функции**: `ExportReportAsync`
- **Что она делает**: Экспортирует полный отчет аудита безопасности в выбранном пользователем формате (HTML, CSV, JSON).
- **Какие входные данные**: Входные данные отсутствуют (параметры не требуются).
- **Какие на выходе**: `Task`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `MainViewModel.OpenFileLocation` (строка 2273)
- **Название функции**: `OpenFileLocation`
- **Что она делает**: Открывает в проводнике папку с файлом записи и выделяет его.
- **Какие входные данные**: `entryId` (`string`): Значение параметра `entryId` (string).
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `MainViewModel.CopyFilePath` (строка 2284)
- **Название функции**: `CopyFilePath`
- **Что она делает**: Копирует полный путь к файлу записи в буфер обмена.
- **Какие входные данные**: `entryId` (`string`): Значение параметра `entryId` (string).
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `MainViewModel.BlockEntryByIdAsync` (строка 2293)
- **Название функции**: `BlockEntryByIdAsync`
- **Что она делает**: Блокирует запись по её идентификатору.
- **Какие входные данные**: `entryId` (`string`): Значение параметра `entryId` (string).
- **Какие на выходе**: `Task`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `MainViewModel.BlockEntryAsync` (строка 2302)
- **Название функции**: `BlockEntryAsync`
- **Что она делает**: Добавляет запись в чёрный список (блокирует) и помечает как Blocked.
- **Какие входные данные**: `entry` (`StartupEntry`): Значение параметра `entry` (StartupEntry).
- **Какие на выходе**: `Task`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `MainViewModel.RemoveFromSystemAsync` (строка 2342)
- **Название функции**: `RemoveFromSystemAsync`
- **Что она делает**: Удаляет запись из системы (реестр/файл) через службу. При AutoBlockOnRemove предварительно блокирует.
- **Какие входные данные**: `entryId` (`string`): Значение параметра `entryId` (string).
- **Какие на выходе**: `Task`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `MainViewModel.LoadRemovedEntriesAsync` (строка 2388)
- **Название функции**: `LoadRemovedEntriesAsync`
- **Что она делает**: Загружает список удалённых записей из службы.
- **Какие входные данные**: Входные данные отсутствуют (параметры не требуются).
- **Какие на выходе**: `Task`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `MainViewModel.LoadHistoryAsync` (строка 2395)
- **Название функции**: `LoadHistoryAsync`
- **Что она делает**: Загружает историю изменений из службы (с сортировкой от новых к старым).
- **Какие входные данные**: Входные данные отсутствуют (параметры не требуются).
- **Какие на выходе**: `Task`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `MainViewModel.LoadLogsAsync` (строка 2403)
- **Название функции**: `LoadLogsAsync`
- **Что она делает**: Загружает логи (служба + UI) и отображает их на странице логов.
- **Какие входные данные**: Входные данные отсутствуют (параметры не требуются).
- **Какие на выходе**: `Task`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `MainViewModel.ReRateEntryAsync` (строка 2412)
- **Название функции**: `ReRateEntryAsync`
- **Что она делает**: Запрашивает переоценку AI-рейтинга для указанной записи.
- **Какие входные данные**: `entryId` (`string`): Значение параметра `entryId` (string).
- **Какие на выходе**: `Task`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `MainViewModel.StartLogWatch` (строка 2423)
- **Название функции**: `StartLogWatch`
- **Что она делает**: Включает автообновление логов (страница «Логи» открыта).
- **Какие входные данные**: Входные данные отсутствуют (параметры не требуются).
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `MainViewModel.StopLogWatch` (строка 2432)
- **Название функции**: `StopLogWatch`
- **Что она делает**: Выключает автообновление (ушли со страницы «Логи»).
- **Какие входные данные**: Входные данные отсутствуют (параметры не требуются).
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `MainViewModel.RefreshLogsIfChanged` (строка 2439)
- **Название функции**: `RefreshLogsIfChanged`
- **Что она делает**: Проверяет, изменились ли файлы логов, и при необходимости обновляет коллекцию.
- **Какие входные данные**: Входные данные отсутствуют (параметры не требуются).
- **Какие на выходе**: `Task`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `MainViewModel.RestoreEntryAsync` (строка 2469)
- **Название функции**: `RestoreEntryAsync`
- **Что она делает**: Восстанавливает удалённую запись в системе.
- **Какие входные данные**: `removedId` (`string`): Значение параметра `removedId` (string).
- **Какие на выходе**: `Task`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `MainViewModel.CleanStaleConfigAsync` (строка 2478)
- **Название функции**: `CleanStaleConfigAsync`
- **Что она делает**: Запускает очистку устаревших записей на стороне службы.
- **Какие входные данные**: Входные данные отсутствуют (параметры не требуются).
- **Какие на выходе**: `Task`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `MainViewModel.UnblockEntryAsync` (строка 2486)
- **Название функции**: `UnblockEntryAsync`
- **Что она делает**: Снимает блокировку с записи: удаляет из чёрного списка и включает обратно.
- **Какие входные данные**: `entry` (`StartupEntry`): Значение параметра `entry` (StartupEntry).
- **Какие на выходе**: `Task`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `MainViewModel.UnblockEntryByIdAsync` (строка 2506)
- **Название функции**: `UnblockEntryByIdAsync`
- **Что она делает**: Снимает блокировку с записи по идентификатору.
- **Какие входные данные**: `entryId` (`string`): Значение параметра `entryId` (string).
- **Какие на выходе**: `Task`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `MainViewModel.AddToBlacklistAsync` (строка 2515)
- **Название функции**: `AddToBlacklistAsync`
- **Что она делает**: Добавляет правило в чёрный список через службу.
- **Какие входные данные**: `rule` (`BlacklistRule`): Значение параметра `rule` (BlacklistRule).
- **Какие на выходе**: `Task`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `MainViewModel.RemoveFromBlacklistAsync` (строка 2523)
- **Название функции**: `RemoveFromBlacklistAsync`
- **Что она делает**: Удаляет правило из чёрного списка по ID.
- **Какие входные данные**: `ruleId` (`string`): Значение параметра `ruleId` (string).
- **Какие на выходе**: `Task`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `MainViewModel.AddRuleAsync` (строка 2532)
- **Название функции**: `AddRuleAsync`
- **Что она делает**: Добавляет новое правило через UI-форму (с маппингом типов и действий).
- **Какие входные данные**: Входные данные отсутствуют (параметры не требуются).
- **Какие на выходе**: `Task`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `MainViewModel.LoadBootTimeAsync` (строка 2593)
- **Название функции**: `LoadBootTimeAsync`
- **Что она делает**: Загружает/обновляет отчёт Boot Time Tracer из службы.
- **Какие входные данные**: Входные данные отсутствуют (параметры не требуются).
- **Какие на выходе**: `Task`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

---

### Файл: `StartupMonitor.UI\ViewModels\TaskManagerViewModel.cs`

#### `TaskManagerViewModel.TaskManagerViewModel` (строка 109)
- **Название функции**: `TaskManagerViewModel`
- **Что она делает**: TaskManagerViewModel: Выполняет операцию TaskManagerViewModel.
- **Какие входные данные**: `pipeClient` (`PipeClient`): Входные данные: значение параметра pipeClient (PipeClient).
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `TaskManagerViewModel.OnPipeClientConnected` (строка 129)
- **Название функции**: `OnPipeClientConnected`
- **Что она делает**: OnPipeClientConnected: Обрабатывает событие OnPipeClientConnected.
- **Какие входные данные**: `sender` (`object?`): Входные данные: значение параметра sender (object?).; `e` (`EventArgs`): Входные данные: значение параметра e (EventArgs).
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `TaskManagerViewModel.OnViewModeChanged` (строка 137)
- **Название функции**: `OnViewModeChanged`
- **Что она делает**: Обрабатывает событие жизненного цикла или действие пользователя (OnViewModeChanged).
- **Какие входные данные**: `value` (`string`): Значение параметра `value` (string).
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `TaskManagerViewModel.OnSearchTextChanged` (строка 144)
- **Название функции**: `OnSearchTextChanged`
- **Что она делает**: Обрабатывает событие жизненного цикла или действие пользователя (OnSearchTextChanged).
- **Какие входные данные**: `value` (`string`): Значение параметра `value` (string).
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `TaskManagerViewModel.OnSelectedFilterChanged` (строка 150)
- **Название функции**: `OnSelectedFilterChanged`
- **Что она делает**: Обрабатывает событие жизненного цикла или действие пользователя (OnSelectedFilterChanged).
- **Какие входные данные**: `value` (`string`): Значение параметра `value` (string).
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `TaskManagerViewModel.ActivateAsync` (строка 156)
- **Название функции**: `ActivateAsync`
- **Что она делает**: Активирует вкладку Диспетчера задач: подписывается на поток метрик службы.
- **Какие входные данные**: Входные данные отсутствуют (параметры не требуются).
- **Какие на выходе**: `Task`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `TaskManagerViewModel.DeactivateAsync` (строка 179)
- **Название функции**: `DeactivateAsync`
- **Что она делает**: Деактивирует вкладку Диспетчера задач: отписывается от службы (снижает CPU службы до 0%).
- **Какие входные данные**: Входные данные отсутствуют (параметры не требуются).
- **Какие на выходе**: `Task`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `TaskManagerViewModel.OnProcessSnapshotReceived` (строка 194)
- **Название функции**: `OnProcessSnapshotReceived`
- **Что она делает**: OnProcessSnapshotReceived: Обрабатывает событие OnProcessSnapshotReceived.
- **Какие входные данные**: `sender` (`object?`): Входные данные: значение параметра sender (object?).; `snapshot` (`SystemPerformanceSnapshot`): Входные данные: значение параметра snapshot (SystemPerformanceSnapshot).
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `TaskManagerViewModel.UpdateSnapshot` (строка 200)
- **Название функции**: `UpdateSnapshot`
- **Что она делает**: Обновляет снимок и обновляет коллекцию процессов без мерцания.
- **Какие входные данные**: `snapshot` (`SystemPerformanceSnapshot`): Значение параметра `snapshot` (SystemPerformanceSnapshot).
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `TaskManagerViewModel.IsAppProcess` (строка 309)
- **Название функции**: `IsAppProcess`
- **Что она делает**: Проверяет, относится ли процесс к категории пользовательских приложений (Apps).
- **Какие входные данные**: `p` (`ProcessInfo`): Значение параметра `p` (ProcessInfo).
- **Какие на выходе**: `bool`: Логический результат выполнения (`true` в случае успеха или соответствия условию, иначе `false`).

#### `TaskManagerViewModel.IsServiceProcess` (строка 330)
- **Название функции**: `IsServiceProcess`
- **Что она делает**: Проверяет, относится ли процесс к категории служб Windows (Services).
- **Какие входные данные**: `p` (`ProcessInfo`): Значение параметра `p` (ProcessInfo).
- **Какие на выходе**: `bool`: Логический результат выполнения (`true` в случае успеха или соответствия условию, иначе `false`).

#### `TaskManagerViewModel.SortProcesses` (строка 344)
- **Название функции**: `SortProcesses`
- **Что она делает**: Вспомогательный метод сортировки коллекции процессов в соответствии с текущей выбранной колонкой.
- **Какие входные данные**: `list` (`IEnumerable<ProcessInfo>`): Значение параметра `list` (IEnumerable<ProcessInfo>).
- **Какие на выходе**: `List<ProcessInfo>`: Коллекция или набор результирующих объектов.

#### `TaskManagerViewModel.ApplyFilterAndSort` (строка 380)
- **Название функции**: `ApplyFilterAndSort`
- **Что она делает**: Применяет текстовый фильтр, категорию, режим отображения (Список / Дерево) и порядок сортировки.
- **Какие входные данные**: Входные данные отсутствуют (параметры не требуются).
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `TaskManagerViewModel.SyncFilteredProcesses` (строка 549)
- **Название функции**: `SyncFilteredProcesses`
- **Что она делает**: In-place синхронизация отображаемой коллекции с новым отсортированным снимком. 1. Сохраняет выбранный PID. 2. Удаляет только завершившиеся/отфильтрованные PID. 3. Добавляет только новые процессы. 4. Перемещает изменившие порядок. Метрики (CpuUsage, Memory, Disk, Net) уже обновлены на существующих объектах в <see cref="UpdateSnapshot"/>, поэтому контейнеры DataGrid переиспользуются.
- **Какие входные данные**: `sortedList` (`List<ProcessInfo>`): Значение параметра `sortedList` (List<ProcessInfo>).
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `TaskManagerViewModel.SetViewMode` (строка 618)
- **Название функции**: `SetViewMode`
- **Что она делает**: Переключает режим отображения процессов («flat» или «tree»).
- **Какие входные данные**: `mode` (`string`): Значение параметра `mode` (string).
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `TaskManagerViewModel.ToggleExpand` (строка 625)
- **Название функции**: `ToggleExpand`
- **Что она делает**: Сворачивает или разворачивает ветвь дерева дочерних процессов.
- **Какие входные данные**: `process` (`ProcessInfo?`): Значение параметра `process` (ProcessInfo?).
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `TaskManagerViewModel.OpenProcessConnections` (строка 645)
- **Название функции**: `OpenProcessConnections`
- **Что она делает**: Открывает окно просмотра активных сетевых соединений (сокетов) для выбранного процесса.
- **Какие входные данные**: `process` (`ProcessInfo?`): Значение параметра `process` (ProcessInfo?).
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `TaskManagerViewModel.SortBy` (строка 659)
- **Название функции**: `SortBy`
- **Что она делает**: Сортирует по указанной колонке (при повторном клике меняет направление).
- **Какие входные данные**: `column` (`string`): Значение параметра `column` (string).
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `TaskManagerViewModel.UpdateSortIndicators` (строка 675)
- **Название функции**: `UpdateSortIndicators`
- **Что она делает**: UpdateSortIndicators: Обновляет состояние SortIndicators.
- **Какие входные данные**: Входные данные отсутствуют (параметры не требуются).
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `TaskManagerViewModel.SetFilter` (строка 692)
- **Название функции**: `SetFilter`
- **Что она делает**: Устанавливает значение для Filter.
- **Какие входные данные**: `filter` (`string`): Входные данные: значение параметра filter (string).
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `TaskManagerViewModel.KillProcessAsync` (строка 698)
- **Название функции**: `KillProcessAsync`
- **Что она делает**: Завершает выбранный процесс (Снять задачу).
- **Какие входные данные**: `process` (`ProcessInfo?`): Значение параметра `process` (ProcessInfo?).
- **Какие на выходе**: `Task`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `TaskManagerViewModel.BlockProcessAsync` (строка 723)
- **Название функции**: `BlockProcessAsync`
- **Что она делает**: Блокирует процесс в Чёрном списке, удаляет из автозапуска и завершает процесс.
- **Какие входные данные**: `process` (`ProcessInfo?`): Значение параметра `process` (ProcessInfo?).
- **Какие на выходе**: `Task`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `TaskManagerViewModel.SuspendProcessAsync` (строка 749)
- **Название функции**: `SuspendProcessAsync`
- **Что она делает**: Приостанавливает выполнение выбранного процесса.
- **Какие входные данные**: `process` (`ProcessInfo?`): Значение параметра `process` (ProcessInfo?).
- **Какие на выходе**: `Task`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `TaskManagerViewModel.ResumeProcessAsync` (строка 770)
- **Название функции**: `ResumeProcessAsync`
- **Что она делает**: Возобновляет выполнение ранее приостановленного процесса.
- **Какие входные данные**: `process` (`ProcessInfo?`): Значение параметра `process` (ProcessInfo?).
- **Какие на выходе**: `Task`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `TaskManagerViewModel.LocateInStartup` (строка 791)
- **Название функции**: `LocateInStartup`
- **Что она делает**: Переходит к связанной записи автозапуска в таблице Автозагрузка.
- **Какие входные данные**: `process` (`ProcessInfo?`): Значение параметра `process` (ProcessInfo?).
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `TaskManagerViewModel.OpenFileLocation` (строка 806)
- **Название функции**: `OpenFileLocation`
- **Что она делает**: Открывает проводник с выделением исполняемого файла процесса.
- **Какие входные данные**: `process` (`ProcessInfo?`): Значение параметра `process` (ProcessInfo?).
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `TaskManagerViewModel.CleanExecutablePath` (строка 880)
- **Название функции**: `CleanExecutablePath`
- **Что она делает**: Очищает путь к исполняемому файлу: убирает кавычки, аргументы, NT-префиксы и переменные среды.
- **Какие входные данные**: `rawPath` (`string?`): Значение параметра `rawPath` (string?).
- **Какие на выходе**: `string`: Текстовая строка с результатом операции или сообщением.

#### `TaskManagerViewModel.TryGetProcessPath` (строка 922)
- **Название функции**: `TryGetProcessPath`
- **Что она делает**: Пытается получить путь к исполняемому файлу процесса по PID напрямую из UI.
- **Какие входные данные**: `pid` (`int`): Значение параметра `pid` (int).
- **Какие на выходе**: `string?`: Текстовая строка с результатом операции или сообщением.

#### `TaskManagerViewModel.OpenProcess` (строка 960)
- **Название функции**: `OpenProcess`
- **Что она делает**: Выполняет операцию OpenProcess в рамках подсистемы.
- **Какие входные данные**: `processAccess` (`int`): Входные данные: значение параметра processAccess (int).; `bInheritHandle` (`bool`): Входные данные: значение параметра bInheritHandle (bool).; `processId` (`int`): Входные данные: значение параметра processId (int).
- **Какие на выходе**: `extern IntPtr`: Выходные данные: результирующее значение типа extern IntPtr.

#### `TaskManagerViewModel.CloseHandle` (строка 966)
- **Название функции**: `CloseHandle`
- **Что она делает**: Выполняет операцию CloseHandle в рамках подсистемы.
- **Какие входные данные**: `hObject` (`IntPtr`): Входные данные: значение параметра hObject (IntPtr).
- **Какие на выходе**: `extern bool`: Выходные данные: результирующее значение типа extern bool.

#### `TaskManagerViewModel.QueryFullProcessImageName` (строка 975)
- **Название функции**: `QueryFullProcessImageName`
- **Что она делает**: Выполняет операцию QueryFullProcessImageName в рамках подсистемы.
- **Какие входные данные**: `hProcess` (`IntPtr`): Входные данные: значение параметра hProcess (IntPtr).; `flags` (`int`): Входные данные: значение параметра flags (int).; `lpExeName` (`[Out] StringBuilder`): Входные данные: значение параметра lpExeName ([Out] StringBuilder).; `lpdwSize` (`ref int`): Входные данные: значение параметра lpdwSize (ref int).
- **Какие на выходе**: `extern bool`: Выходные данные: результирующее значение типа extern bool.

#### `TaskManagerViewModel.Dispose` (строка 979)
- **Название функции**: `Dispose`
- **Что она делает**: Dispose: Освобождает занятые ресурсы объекта.
- **Какие входные данные**: Входные данные отсутствуют (параметры не требуются).
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

---

### Файл: `StartupMonitor.UI\Views\MainWindow.xaml.cs`

#### `MainWindow.xaml.MainWindow` (строка 36)
- **Название функции**: `MainWindow`
- **Что она делает**: Инициализирует главное окно, настраивает Model-View-ViewModel и систему уведомлений.
- **Какие входные данные**: Входные данные отсутствуют (параметры не требуются).
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `MainWindow.xaml.OnEntryPreviewMouseDown` (строка 244)
- **Название функции**: `OnEntryPreviewMouseDown`
- **Что она делает**: Обрабатывает предварительный клик левой кнопкой мыши для детекции двойного клика по записи.
- **Какие входные данные**: `sender` (`object`): Значение параметра `sender` (object).; `e` (`MouseButtonEventArgs`): Значение параметра `e` (MouseButtonEventArgs).
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `MainWindow.xaml.Action` (строка 266)
- **Название функции**: `Action`
- **Что она делает**: Action: Выполняет операцию Action.
- **Какие входные данные**: `(`: Входные данные: значение параметра ( ().
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `MainWindow.xaml.OnMoreClick` (строка 279)
- **Название функции**: `OnMoreClick`
- **Что она делает**: Обрабатывает клик по кнопке «Подробнее» (More) в строке списка.
- **Какие входные данные**: `sender` (`object`): Значение параметра `sender` (object).; `e` (`RoutedEventArgs`): Значение параметра `e` (RoutedEventArgs).
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `MainWindow.xaml.OnToggleEntryClick` (строка 292)
- **Название функции**: `OnToggleEntryClick`
- **Что она делает**: Обрабатывает переключение тумблера включения/отключения автозагрузки в строке списка.
- **Какие входные данные**: `sender` (`object`): Значение параметра `sender` (object).; `e` (`RoutedEventArgs`): Значение параметра `e` (RoutedEventArgs).
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `MainWindow.xaml.StartupSequence` (строка 313)
- **Название функции**: `StartupSequence`
- **Что она делает**: Последовательность запуска: установка службы, подключение, загрузка данных.
- **Какие входные данные**: Входные данные отсутствуют (параметры не требуются).
- **Какие на выходе**: `Task`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `MainWindow.xaml.ShowStartupError` (строка 401)
- **Название функции**: `ShowStartupError`
- **Что она делает**: Отображает ошибку на оверлее с кнопками повтора и выхода.
- **Какие входные данные**: `errorMessage` (`string`): Значение параметра `errorMessage` (string).
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `MainWindow.xaml.OnOverlayRetryClick` (строка 412)
- **Название функции**: `OnOverlayRetryClick`
- **Что она делает**: Повторяет запуск и подключение к службе по нажатию кнопки.
- **Какие входные данные**: `sender` (`object`): Значение параметра `sender` (object).; `e` (`RoutedEventArgs`): Значение параметра `e` (RoutedEventArgs).
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `MainWindow.xaml.OnOverlayExitClick` (строка 418)
- **Название функции**: `OnOverlayExitClick`
- **Что она делает**: Завершает приложение по нажатию кнопки выхода на оверлее.
- **Какие входные данные**: `sender` (`object`): Значение параметра `sender` (object).; `e` (`RoutedEventArgs`): Значение параметра `e` (RoutedEventArgs).
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `MainWindow.xaml.OnContextMenuOpening` (строка 424)
- **Название функции**: `OnContextMenuOpening`
- **Что она делает**: Формирует контекстное меню для выбранной записи в списке.
- **Какие входные данные**: `sender` (`object`): Значение параметра `sender` (object).; `e` (`ContextMenuEventArgs`): Значение параметра `e` (ContextMenuEventArgs).
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `MainWindow.xaml.FocusEntry` (строка 581)
- **Название функции**: `FocusEntry`
- **Что она делает**: Фокусирует определённую запись в списке по её идентификатору.
- **Какие входные данные**: `entryId` (`string`): Значение параметра `entryId` (string).
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `MainWindow.xaml.OnNavClick` (строка 593)
- **Название функции**: `OnNavClick`
- **Что она делает**: Обрабатывает клик по элементу навигации для переключения страниц.
- **Какие входные данные**: `sender` (`object`): Значение параметра `sender` (object).; `e` (`System.Windows.Input.MouseButtonEventArgs`): Значение параметра `e` (System.Windows.Input.MouseButtonEventArgs).
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `MainWindow.xaml.OnExitClick` (строка 600)
- **Название функции**: `OnExitClick`
- **Что она делает**: Обрабатывает клик по кнопке выхода из приложения.
- **Какие входные данные**: `sender` (`object`): Значение параметра `sender` (object).; `e` (`System.Windows.Input.MouseButtonEventArgs`): Значение параметра `e` (System.Windows.Input.MouseButtonEventArgs).
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `MainWindow.xaml.NavigateToPage` (строка 606)
- **Название функции**: `NavigateToPage`
- **Что она делает**: Переключает видимость страниц и обновляет состояние выбранного пункта навигации.
- **Какие входные данные**: `idx` (`int`): Значение параметра `idx` (int).
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `MainWindow.xaml.RestoreFromTray` (строка 664)
- **Название функции**: `RestoreFromTray`
- **Что она делает**: Восстанавливает главное окно из системного трея в его исходное состояние (Normal или Maximized).
- **Какие входные данные**: Входные данные отсутствуют (параметры не требуются).
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `MainWindow.xaml.IsWindowPlacementVisible` (строка 678)
- **Название функции**: `IsWindowPlacementVisible`
- **Что она делает**: Проверяет, попадает ли прямоугольник окна хотя бы частично на видимую область подключенных мониторов.
- **Какие входные данные**: `bounds` (`Rect`): Значение параметра `bounds` (Rect).
- **Какие на выходе**: `bool`: Логический результат выполнения (`true` в случае успеха или соответствия условию, иначе `false`).

---

### Файл: `StartupMonitor.UI\Views\Dialogs\EntryDetailWindow.xaml.cs`

#### `EntryDetailWindow.xaml.EntryDetailWindow` (строка 17)
- **Название функции**: `EntryDetailWindow`
- **Что она делает**: Инициализирует окно деталей с записью и ссылкой на ViewModel.
- **Какие входные данные**: `entry` (`StartupEntry`): Значение параметра `entry` (StartupEntry).; `viewModel` (`MainViewModel`): Значение параметра `viewModel` (MainViewModel).
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `EntryDetailWindow.xaml.OnBlockClick` (строка 27)
- **Название функции**: `OnBlockClick`
- **Что она делает**: Обрабатывает нажатие кнопки «Заблокировать».
- **Какие входные данные**: `sender` (`object`): Значение параметра `sender` (object).; `e` (`System.Windows.RoutedEventArgs`): Значение параметра `e` (System.Windows.RoutedEventArgs).
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `EntryDetailWindow.xaml.OnUnblockClick` (строка 33)
- **Название функции**: `OnUnblockClick`
- **Что она делает**: Обрабатывает нажатие кнопки «Разблокировать».
- **Какие входные данные**: `sender` (`object`): Значение параметра `sender` (object).; `e` (`System.Windows.RoutedEventArgs`): Значение параметра `e` (System.Windows.RoutedEventArgs).
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `EntryDetailWindow.xaml.OnDisableClick` (строка 39)
- **Название функции**: `OnDisableClick`
- **Что она делает**: Обрабатывает нажатие кнопки «Отключить».
- **Какие входные данные**: `sender` (`object`): Значение параметра `sender` (object).; `e` (`System.Windows.RoutedEventArgs`): Значение параметра `e` (System.Windows.RoutedEventArgs).
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `EntryDetailWindow.xaml.OnEnableClick` (строка 45)
- **Название функции**: `OnEnableClick`
- **Что она делает**: Обрабатывает нажатие кнопки «Включить».
- **Какие входные данные**: `sender` (`object`): Значение параметра `sender` (object).; `e` (`System.Windows.RoutedEventArgs`): Значение параметра `e` (System.Windows.RoutedEventArgs).
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `EntryDetailWindow.xaml.OnSetDelayClick` (строка 51)
- **Название функции**: `OnSetDelayClick`
- **Что она делает**: Обрабатывает нажатие кнопки «Отложить запуск».
- **Какие входные данные**: `sender` (`object`): Значение параметра `sender` (object).; `e` (`System.Windows.RoutedEventArgs`): Значение параметра `e` (System.Windows.RoutedEventArgs).
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `EntryDetailWindow.xaml.OnCancelDelayClick` (строка 57)
- **Название функции**: `OnCancelDelayClick`
- **Что она делает**: Обрабатывает нажатие кнопки «Отменить задержку».
- **Какие входные данные**: `sender` (`object`): Значение параметра `sender` (object).; `e` (`System.Windows.RoutedEventArgs`): Значение параметра `e` (System.Windows.RoutedEventArgs).
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `EntryDetailWindow.xaml.OnLaunchNowClick` (строка 63)
- **Название функции**: `OnLaunchNowClick`
- **Что она делает**: Обрабатывает нажатие кнопки «Запустить сейчас».
- **Какие входные данные**: `sender` (`object`): Значение параметра `sender` (object).; `e` (`System.Windows.RoutedEventArgs`): Значение параметра `e` (System.Windows.RoutedEventArgs).
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `EntryDetailWindow.xaml.OnRemoveClick` (строка 69)
- **Название функции**: `OnRemoveClick`
- **Что она делает**: Обрабатывает нажатие кнопки «Удалить из системы» с подтверждением.
- **Какие входные данные**: `sender` (`object`): Значение параметра `sender` (object).; `e` (`System.Windows.RoutedEventArgs`): Значение параметра `e` (System.Windows.RoutedEventArgs).
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `EntryDetailWindow.xaml.OnRefreshRatingClick` (строка 83)
- **Название функции**: `OnRefreshRatingClick`
- **Что она делает**: Обрабатывает нажатие кнопки переоценки AI-рейтинга.
- **Какие входные данные**: `sender` (`object`): Значение параметра `sender` (object).; `e` (`System.Windows.RoutedEventArgs`): Значение параметра `e` (System.Windows.RoutedEventArgs).
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `EntryDetailWindow.xaml.OnScanKasperskyClick` (строка 89)
- **Название функции**: `OnScanKasperskyClick`
- **Что она делает**: Обрабатывает нажатие кнопки проверки в Kaspersky OpenTIP.
- **Какие входные данные**: `sender` (`object`): Значение параметра `sender` (object).; `e` (`System.Windows.RoutedEventArgs`): Значение параметра `e` (System.Windows.RoutedEventArgs).
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `EntryDetailWindow.xaml.OnOpenLocationClick` (строка 95)
- **Название функции**: `OnOpenLocationClick`
- **Что она делает**: Обрабатывает нажатие кнопки «Открыть расположение файла».
- **Какие входные данные**: `sender` (`object`): Значение параметра `sender` (object).; `e` (`System.Windows.RoutedEventArgs`): Значение параметра `e` (System.Windows.RoutedEventArgs).
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `EntryDetailWindow.xaml.OnCopyPathClick` (строка 101)
- **Название функции**: `OnCopyPathClick`
- **Что она делает**: Обрабатывает нажатие кнопки «Копировать путь».
- **Какие входные данные**: `sender` (`object`): Значение параметра `sender` (object).; `e` (`System.Windows.RoutedEventArgs`): Значение параметра `e` (System.Windows.RoutedEventArgs).
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `EntryDetailWindow.xaml.OnCloseClick` (строка 107)
- **Название функции**: `OnCloseClick`
- **Что она делает**: Закрывает окно деталей.
- **Какие входные данные**: `sender` (`object`): Значение параметра `sender` (object).; `e` (`System.Windows.RoutedEventArgs`): Значение параметра `e` (System.Windows.RoutedEventArgs).
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

---

### Файл: `StartupMonitor.UI\Views\Dialogs\ProcessConnectionsWindow.xaml.cs`

#### `ProcessConnectionsWindow.xaml.ProcessConnectionsWindow` (строка 32)
- **Название функции**: `ProcessConnectionsWindow`
- **Что она делает**: ProcessConnectionsWindow: Выполняет операцию ProcessConnectionsWindow.
- **Какие входные данные**: `targetProcess` (`ProcessInfo`): Входные данные: значение параметра targetProcess (ProcessInfo).; `pipeClient` (`PipeClient`): Входные данные: значение параметра pipeClient (PipeClient).
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `ProcessConnectionsWindow.xaml.LoadConnectionsAsync` (строка 61)
- **Название функции**: `LoadConnectionsAsync`
- **Что она делает**: LoadConnectionsAsync: Загружает данные ConnectionsAsync.
- **Какие входные данные**: Входные данные отсутствуют (параметры не требуются).
- **Какие на выходе**: `Task`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `ProcessConnectionsWindow.xaml.ApplyFilter` (строка 91)
- **Название функции**: `ApplyFilter`
- **Что она делает**: ApplyFilter: Выполняет операцию ApplyFilter.
- **Какие входные данные**: Входные данные отсутствуют (параметры не требуются).
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `ProcessConnectionsWindow.xaml.OnSearchTextChanged` (строка 116)
- **Название функции**: `OnSearchTextChanged`
- **Что она делает**: OnSearchTextChanged: Обрабатывает событие OnSearchTextChanged.
- **Какие входные данные**: `sender` (`object`): Входные данные: значение параметра sender (object).; `e` (`TextChangedEventArgs`): Входные данные: значение параметра e (TextChangedEventArgs).
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `ProcessConnectionsWindow.xaml.OnRefreshClick` (строка 124)
- **Название функции**: `OnRefreshClick`
- **Что она делает**: OnRefreshClick: Обрабатывает событие OnRefreshClick.
- **Какие входные данные**: `sender` (`object`): Входные данные: значение параметра sender (object).; `e` (`RoutedEventArgs`): Входные данные: значение параметра e (RoutedEventArgs).
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `ProcessConnectionsWindow.xaml.OnAutoRefreshChanged` (строка 132)
- **Название функции**: `OnAutoRefreshChanged`
- **Что она делает**: OnAutoRefreshChanged: Обрабатывает событие OnAutoRefreshChanged.
- **Какие входные данные**: `sender` (`object`): Входные данные: значение параметра sender (object).; `e` (`RoutedEventArgs`): Входные данные: значение параметра e (RoutedEventArgs).
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `ProcessConnectionsWindow.xaml.OnCopyRemoteIpClick` (строка 148)
- **Название функции**: `OnCopyRemoteIpClick`
- **Что она делает**: OnCopyRemoteIpClick: Обрабатывает событие OnCopyRemoteIpClick.
- **Какие входные данные**: `sender` (`object`): Входные данные: значение параметра sender (object).; `e` (`RoutedEventArgs`): Входные данные: значение параметра e (RoutedEventArgs).
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `ProcessConnectionsWindow.xaml.OnCopyRemoteEndpointClick` (строка 160)
- **Название функции**: `OnCopyRemoteEndpointClick`
- **Что она делает**: OnCopyRemoteEndpointClick: Обрабатывает событие OnCopyRemoteEndpointClick.
- **Какие входные данные**: `sender` (`object`): Входные данные: значение параметра sender (object).; `e` (`RoutedEventArgs`): Входные данные: значение параметра e (RoutedEventArgs).
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `ProcessConnectionsWindow.xaml.OnOpenWhoisClick` (строка 172)
- **Название функции**: `OnOpenWhoisClick`
- **Что она делает**: OnOpenWhoisClick: Обрабатывает событие OnOpenWhoisClick.
- **Какие входные данные**: `sender` (`object`): Входные данные: значение параметра sender (object).; `e` (`RoutedEventArgs`): Входные данные: значение параметра e (RoutedEventArgs).
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `ProcessConnectionsWindow.xaml.OnOpenVirusTotalClick` (строка 188)
- **Название функции**: `OnOpenVirusTotalClick`
- **Что она делает**: OnOpenVirusTotalClick: Обрабатывает событие OnOpenVirusTotalClick.
- **Какие входные данные**: `sender` (`object`): Входные данные: значение параметра sender (object).; `e` (`RoutedEventArgs`): Входные данные: значение параметра e (RoutedEventArgs).
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `ProcessConnectionsWindow.xaml.OnCloseClick` (строка 204)
- **Название функции**: `OnCloseClick`
- **Что она делает**: OnCloseClick: Обрабатывает событие OnCloseClick.
- **Какие входные данные**: `sender` (`object`): Входные данные: значение параметра sender (object).; `e` (`RoutedEventArgs`): Входные данные: значение параметра e (RoutedEventArgs).
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

---

### Файл: `StartupMonitor.UI\Views\Dialogs\ProcessModulesWindow.xaml.cs`

#### `ProcessModulesWindow.xaml.ProcessModulesWindow` (строка 32)
- **Название функции**: `ProcessModulesWindow`
- **Что она делает**: ProcessModulesWindow: Выполняет операцию ProcessModulesWindow.
- **Какие входные данные**: `targetProcess` (`ProcessInfo`): Входные данные: значение параметра targetProcess (ProcessInfo).; `pipeClient` (`PipeClient`): Входные данные: значение параметра pipeClient (PipeClient).
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `ProcessModulesWindow.xaml.LoadDataAsync` (строка 46)
- **Название функции**: `LoadDataAsync`
- **Что она делает**: LoadDataAsync: Загружает данные DataAsync.
- **Какие входные данные**: Входные данные отсутствуют (параметры не требуются).
- **Какие на выходе**: `Task`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `ProcessModulesWindow.xaml.ApplyFilter` (строка 86)
- **Название функции**: `ApplyFilter`
- **Что она делает**: ApplyFilter: Выполняет операцию ApplyFilter.
- **Какие входные данные**: Входные данные отсутствуют (параметры не требуются).
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `ProcessModulesWindow.xaml.OnSearchTextChanged` (строка 125)
- **Название функции**: `OnSearchTextChanged`
- **Что она делает**: OnSearchTextChanged: Обрабатывает событие OnSearchTextChanged.
- **Какие входные данные**: `sender` (`object`): Входные данные: значение параметра sender (object).; `e` (`TextChangedEventArgs`): Входные данные: значение параметра e (TextChangedEventArgs).
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `ProcessModulesWindow.xaml.OnRefreshClick` (строка 133)
- **Название функции**: `OnRefreshClick`
- **Что она делает**: OnRefreshClick: Обрабатывает событие OnRefreshClick.
- **Какие входные данные**: `sender` (`object`): Входные данные: значение параметра sender (object).; `e` (`RoutedEventArgs`): Входные данные: значение параметра e (RoutedEventArgs).
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `ProcessModulesWindow.xaml.OnOpenLocationClick` (строка 141)
- **Название функции**: `OnOpenLocationClick`
- **Что она делает**: OnOpenLocationClick: Обрабатывает событие OnOpenLocationClick.
- **Какие входные данные**: `sender` (`object`): Входные данные: значение параметра sender (object).; `e` (`RoutedEventArgs`): Входные данные: значение параметра e (RoutedEventArgs).
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `ProcessModulesWindow.xaml.OnCopyPathClick` (строка 159)
- **Название функции**: `OnCopyPathClick`
- **Что она делает**: OnCopyPathClick: Обрабатывает событие OnCopyPathClick.
- **Какие входные данные**: `sender` (`object`): Входные данные: значение параметра sender (object).; `e` (`RoutedEventArgs`): Входные данные: значение параметра e (RoutedEventArgs).
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `ProcessModulesWindow.xaml.OnCopyNameClick` (строка 171)
- **Название функции**: `OnCopyNameClick`
- **Что она делает**: OnCopyNameClick: Обрабатывает событие OnCopyNameClick.
- **Какие входные данные**: `sender` (`object`): Входные данные: значение параметра sender (object).; `e` (`RoutedEventArgs`): Входные данные: значение параметра e (RoutedEventArgs).
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `ProcessModulesWindow.xaml.OnCheckKasperskyClick` (строка 183)
- **Название функции**: `OnCheckKasperskyClick`
- **Что она делает**: OnCheckKasperskyClick: Обрабатывает событие OnCheckKasperskyClick.
- **Какие входные данные**: `sender` (`object`): Входные данные: значение параметра sender (object).; `e` (`RoutedEventArgs`): Входные данные: значение параметра e (RoutedEventArgs).
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `ProcessModulesWindow.xaml.OnCheckVirusTotalClick` (строка 202)
- **Название функции**: `OnCheckVirusTotalClick`
- **Что она делает**: OnCheckVirusTotalClick: Обрабатывает событие OnCheckVirusTotalClick.
- **Какие входные данные**: `sender` (`object`): Входные данные: значение параметра sender (object).; `e` (`RoutedEventArgs`): Входные данные: значение параметра e (RoutedEventArgs).
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `ProcessModulesWindow.xaml.OnCopyHandleNameClick` (строка 221)
- **Название функции**: `OnCopyHandleNameClick`
- **Что она делает**: OnCopyHandleNameClick: Обрабатывает событие OnCopyHandleNameClick.
- **Какие входные данные**: `sender` (`object`): Входные данные: значение параметра sender (object).; `e` (`RoutedEventArgs`): Входные данные: значение параметра e (RoutedEventArgs).
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `ProcessModulesWindow.xaml.OnCloseClick` (строка 233)
- **Название функции**: `OnCloseClick`
- **Что она делает**: OnCloseClick: Обрабатывает событие OnCloseClick.
- **Какие входные данные**: `sender` (`object`): Входные данные: значение параметра sender (object).; `e` (`RoutedEventArgs`): Входные данные: значение параметра e (RoutedEventArgs).
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `ProcessModulesWindow.xaml.GetFileSha256` (строка 241)
- **Название функции**: `GetFileSha256`
- **Что она делает**: GetFileSha256: Возвращает FileSha256.
- **Какие входные данные**: `path` (`string`): Входные данные: значение параметра path (string).
- **Какие на выходе**: `string?`: Выходные данные: результирующее значение типа string?.

---

### Файл: `StartupMonitor.UI\Views\Dialogs\SnapshotsWindow.xaml.cs`

#### `SnapshotsWindow.xaml.SnapshotsWindow` (строка 20)
- **Название функции**: `SnapshotsWindow`
- **Что она делает**: SnapshotsWindow: Выполняет операцию SnapshotsWindow.
- **Какие входные данные**: `pipeClient` (`PipeClient`): Входные данные: значение параметра pipeClient (PipeClient).; `viewModel` (`MainViewModel`): Входные данные: значение параметра viewModel (MainViewModel).
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `SnapshotsWindow.xaml.LoadSnapshotsAsync` (строка 30)
- **Название функции**: `LoadSnapshotsAsync`
- **Что она делает**: LoadSnapshotsAsync: Загружает данные SnapshotsAsync.
- **Какие входные данные**: Входные данные отсутствуют (параметры не требуются).
- **Какие на выходе**: `Task`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `SnapshotsWindow.xaml.OnCreateSnapshotClick` (строка 47)
- **Название функции**: `OnCreateSnapshotClick`
- **Что она делает**: OnCreateSnapshotClick: Обрабатывает событие OnCreateSnapshotClick.
- **Какие входные данные**: `sender` (`object`): Входные данные: значение параметра sender (object).; `e` (`RoutedEventArgs`): Входные данные: значение параметра e (RoutedEventArgs).
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `SnapshotsWindow.xaml.OnCompareSnapshotClick` (строка 86)
- **Название функции**: `OnCompareSnapshotClick`
- **Что она делает**: OnCompareSnapshotClick: Обрабатывает событие OnCompareSnapshotClick.
- **Какие входные данные**: `sender` (`object`): Входные данные: значение параметра sender (object).; `e` (`RoutedEventArgs`): Входные данные: значение параметра e (RoutedEventArgs).
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `SnapshotsWindow.xaml.CompareSnapshotInternalAsync` (строка 96)
- **Название функции**: `CompareSnapshotInternalAsync`
- **Что она делает**: CompareSnapshotInternalAsync: Выполняет операцию CompareSnapshotInternalAsync.
- **Какие входные данные**: `snapshotId` (`string`): Входные данные: значение параметра snapshotId (string).
- **Какие на выходе**: `Task`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `SnapshotsWindow.xaml.DisplayDiffResults` (строка 119)
- **Название функции**: `DisplayDiffResults`
- **Что она делает**: DisplayDiffResults: Выполняет операцию DisplayDiffResults.
- **Какие входные данные**: `diff` (`SnapshotComparisonResult`): Входные данные: значение параметра diff (SnapshotComparisonResult).
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `SnapshotsWindow.xaml.OnDeleteSnapshotClick` (строка 141)
- **Название функции**: `OnDeleteSnapshotClick`
- **Что она делает**: OnDeleteSnapshotClick: Обрабатывает событие OnDeleteSnapshotClick.
- **Какие входные данные**: `sender` (`object`): Входные данные: значение параметра sender (object).; `e` (`RoutedEventArgs`): Входные данные: значение параметра e (RoutedEventArgs).
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `SnapshotsWindow.xaml.OnApplyFilterToMainClick` (строка 162)
- **Название функции**: `OnApplyFilterToMainClick`
- **Что она делает**: OnApplyFilterToMainClick: Обрабатывает событие OnApplyFilterToMainClick.
- **Какие входные данные**: `sender` (`object`): Входные данные: значение параметра sender (object).; `e` (`RoutedEventArgs`): Входные данные: значение параметра e (RoutedEventArgs).
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `SnapshotsWindow.xaml.OnCloseClick` (строка 174)
- **Название функции**: `OnCloseClick`
- **Что она делает**: OnCloseClick: Обрабатывает событие OnCloseClick.
- **Какие входные данные**: `sender` (`object`): Входные данные: значение параметра sender (object).; `e` (`RoutedEventArgs`): Входные данные: значение параметра e (RoutedEventArgs).
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

---

### Файл: `StartupMonitor.UI\Views\Dialogs\SystemInfoWindow.xaml.cs`

#### `SystemInfoWindow.xaml.PropertyMetadata` (строка 18)
- **Название функции**: `PropertyMetadata`
- **Что она делает**: PropertyMetadata: Выполняет операцию PropertyMetadata.
- **Какие входные данные**: `null)`: Входные данные: значение параметра null) ().
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `SystemInfoWindow.xaml.SystemInfoWindow` (строка 28)
- **Название функции**: `SystemInfoWindow`
- **Что она делает**: SystemInfoWindow: Выполняет операцию SystemInfoWindow.
- **Какие входные данные**: `initialInfo` (`SystemComputerInfo?`): Входные данные: значение параметра initialInfo (SystemComputerInfo?).
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `SystemInfoWindow.xaml.OnRefreshClick` (строка 37)
- **Название функции**: `OnRefreshClick`
- **Что она делает**: OnRefreshClick: Обрабатывает событие OnRefreshClick.
- **Какие входные данные**: `sender` (`object`): Входные данные: значение параметра sender (object).; `e` (`RoutedEventArgs`): Входные данные: значение параметра e (RoutedEventArgs).
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `SystemInfoWindow.xaml.OnCopyClick` (строка 46)
- **Название функции**: `OnCopyClick`
- **Что она делает**: OnCopyClick: Обрабатывает событие OnCopyClick.
- **Какие входные данные**: `sender` (`object`): Входные данные: значение параметра sender (object).; `e` (`RoutedEventArgs`): Входные данные: значение параметра e (RoutedEventArgs).
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

#### `SystemInfoWindow.xaml.OnCloseClick` (строка 62)
- **Название функции**: `OnCloseClick`
- **Что она делает**: OnCloseClick: Обрабатывает событие OnCloseClick.
- **Какие входные данные**: `sender` (`object`): Входные данные: значение параметра sender (object).; `e` (`RoutedEventArgs`): Входные данные: значение параметра e (RoutedEventArgs).
- **Какие на выходе**: `void`: Функция не возвращает значение (выполняет действие или асинхронную операцию).

---
