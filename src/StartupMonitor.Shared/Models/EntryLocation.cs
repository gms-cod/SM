namespace StartupMonitor.Shared.Models;

/// <summary>Местоположение записи автозапуска в системе.</summary>
public enum EntryLocation
{
    /// <summary>Раздел реестра Run (HKCU/HKLM).</summary>
    RegistryRun,
    /// <summary>Раздел реестра RunOnce.</summary>
    RegistryRunOnce,
    /// <summary>Раздел реестра RunServices.</summary>
    RegistryRunServices,
    /// <summary>Раздел реестра Policies\Explorer\Run.</summary>
    RegistryPolicies,
    /// <summary>Папка автозапуска (%AppData%\Microsoft\Windows\Start Menu\Programs\Startup).</summary>
    StartupFolder,
    /// <summary>Служба Windows.</summary>
    Service,
    /// <summary>Драйвер Windows.</summary>
    Driver,
    /// <summary>Планировщик задач.</summary>
    TaskScheduler,
    /// <summary>Параметр реестра Winlogon (UserInit/Shell).</summary>
    Winlogon,
    /// <summary>Запись BootExecute в реестре.</summary>
    BootExecute,
    /// <summary>Раздел AppInit_DLLs в реестре.</summary>
    AppInitDll,
    /// <summary>Shell-расширение (CLSID).</summary>
    ShellExtension,
    /// <summary>Обработчик контекстного меню.</summary>
    ContextMenuHandler,
    /// <summary>Объект BHO браузера (Browser Helper Object).</summary>
    BrowserHelperObject,
    /// <summary>Параметр отладки образа (Image File Execution Options).</summary>
    ImageFileExecutionOptions,
    /// <summary>Стартовый скрипт групповой политики.</summary>
    GpoStartupScript,
    /// <summary>Скрипт входа групповой политики.</summary>
    GpoLogonScript,
    /// <summary>Подписка на событие WMI.</summary>
    WmiEventSubscription,
    /// <summary>COM-объект (зарегистрированный в системе).</summary>
    ComObject,
    /// <summary>Плагин браузера.</summary>
    BrowserPlugin,
    /// <summary>Сетевой провайдер.</summary>
    NetworkProvider,
    /// <summary>Монитор печати.</summary>
    PrintMonitor,
    /// <summary>Уведомитель LSA.</summary>
    LsaNotification,
    /// <summary>Каталог протоколов (Protocol Handlers).</summary>
    ProtocolCatalog,
    /// <summary>App Paths в реестре.</summary>
    AppPaths,
    /// <summary>Хук клавиатуры.</summary>
    KeyboardHook,
    /// <summary>Автозапуск UWP/AppX-приложения (StartupTask из манифеста пакета).</summary>
    AppxStartupTask,
    /// <summary>Active Setup (HKLM/HKCU\...\Active Setup\Installed Components) — запуск при первом входе.</summary>
    ActiveSetup,
    /// <summary>Кодеки и драйверы мультимедиа (Drivers32).</summary>
    Drivers32
}
