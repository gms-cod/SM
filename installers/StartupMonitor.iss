; =====================================================================
; Inno Setup Script for Startup Monitor
; =====================================================================

#ifndef MyAppVersion
  #define MyAppVersion "1.9.6"
#endif

#define MyAppName "Startup Monitor"
#define MyAppPublisher "GMS"
#define MyAppURL "https://github.com/gms-cod/SM"
#define MyAppExeName "StartupMonitor.UI.exe"
#define MyServiceExeName "StartupMonitor.Service.exe"
#define MyServiceName "StartupMonitor"

[Setup]
AppId={{5A8D3F91-7C42-4F1E-9C84-B168C8E192D4}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} v{#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}/issues
AppUpdatesURL={#MyAppURL}/releases
DefaultDirName={autopf}\StartupMonitor
DefaultGroupName={#MyAppName}
AllowNoIcons=yes
OutputDir=..\installers
OutputBaseFilename=StartupMonitorSetup-{#MyAppVersion}
SetupIconFile=..\assets\app.ico
UninstallDisplayIcon={app}\app.ico
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=admin
PrivilegesRequiredOverridesAllowed=commandline
; Закрытие запущенных процессов StartupMonitor*.exe перед установкой (через Restart Manager)
CloseApplications=yes
CloseApplicationsFilter=StartupMonitor*.exe
; Попытка автоматического перезапуска приложений, закрытых инсталлятором, после установки.
; Работает только для приложений, зарегистрированных в Restart Manager; для UI служит
; резервный механизм watchdog (см. GitHubUpdateService.InstallUpdate и UpdateAvailableWindow).
RestartApplications=yes
UsedUserAreasWarning=no

[Languages]
Name: "russian"; MessagesFile: "compiler:Languages\Russian.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "autostart"; Description: "Запускать монитор при входе в систему (рекомендуется)"; GroupDescription: "Параметры запуска:"
Name: "desktopicon"; Description: "Создать ярлык на Рабочем столе"; GroupDescription: "Дополнительные значки:"; Flags: unchecked

[Files]
Source: "..\SMon2\StartupMonitor.Service.exe"; DestDir: "{app}"; Flags: ignoreversion restartreplace
Source: "..\SMon2\StartupMonitor.UI.exe"; DestDir: "{app}"; Flags: ignoreversion restartreplace
Source: "..\SMon2\app.ico"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\SMon2\icon.svg"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\app.ico"
Name: "{group}\Удалить {#MyAppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\app.ico"; Tasks: desktopicon

[Registry]
; Автозапуск UI через системный реестр Run (параметр --minimized), без ярлыков в меню Пуск
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "StartupMonitor.UI"; ValueData: """{app}\{#MyAppExeName}"" --minimized"; Tasks: autostart; Flags: uninsdeletevalue

[InstallDelete]
; Гарантированное удаление устаревших ярлыков из папки автозагрузки меню «Пуск»
Type: files; Name: "{autostartup}\{#MyAppName}.lnk"
Type: files; Name: "{userstartup}\{#MyAppName}.lnk"
Type: files; Name: "{commonstartup}\{#MyAppName}.lnk"

[UninstallDelete]
Type: files; Name: "{autostartup}\{#MyAppName}.lnk"
Type: files; Name: "{userstartup}\{#MyAppName}.lnk"
Type: files; Name: "{commonstartup}\{#MyAppName}.lnk"

[Run]
; Создание или обновление службы Windows
Filename: "sc.exe"; Parameters: "create {#MyServiceName} binPath= """"{app}\{#MyServiceExeName}"""" start= auto DisplayName= """"Startup Monitor Service"""""; Flags: runhidden; StatusMsg: "Регистрация службы Startup Monitor..."
Filename: "sc.exe"; Parameters: "config {#MyServiceName} binPath= """"{app}\{#MyServiceExeName}"""" start= auto DisplayName= """"Startup Monitor Service"""""; Flags: runhidden
Filename: "sc.exe"; Parameters: "description {#MyServiceName} """"Мониторинг автозагрузки Windows в реальном времени и защита от нежелательных программ."""""; Flags: runhidden
Filename: "sc.exe"; Parameters: "failure {#MyServiceName} reset= 86400 actions= restart/5000/restart/10000/restart/60000"; Flags: runhidden
; Запуск службы после установки/обновления (важно при тихой установке, т.к. служба могла быть
; остановлена/убита инсталлятором через CloseApplications). Выполняется в контексте установщика (admin).
Filename: "sc.exe"; Parameters: "start {#MyServiceName}"; Flags: runhidden; StatusMsg: "Запуск службы Startup Monitor..."
; Опциональный запуск UI для пользователя при интерактивной установке.
; runasoriginaluser — запуск в контексте пользователя, запустившего установщик (не admin),
; чтобы окно появилось на его рабочем столе, а не в Session 0.
; ВАЖНО: при тихой установке (/SILENT, /VERYSILENT) записи с флагом postinstall НЕ выполняются
; (финальный экран не показывается), поэтому перезапуск UI при автообновлении обеспечивается
; службой через UserSessionLauncher (watchdog в пользовательской сессии).
Filename: "{app}\{#MyAppExeName}"; Description: "Запустить Startup Monitor сейчас"; Flags: nowait postinstall skipifsilent runasoriginaluser

[UninstallRun]
; Остановка и удаление службы при удалении программы
Filename: "sc.exe"; Parameters: "stop {#MyServiceName}"; Flags: runhidden; RunOnceId: "StopService"
Filename: "sc.exe"; Parameters: "delete {#MyServiceName}"; Flags: runhidden; RunOnceId: "DeleteService"
Filename: "taskkill.exe"; Parameters: "/F /IM {#MyAppExeName}"; Flags: runhidden; RunOnceId: "KillUI"

[Code]
// Функция завершения службы и процессов перед установкой или обновлением
function PrepareForInstall(): Boolean;
var
  ResultCode: Integer;
begin
  Result := True;
  // Останавливаем службу, если она есть
  Exec('sc.exe', 'stop {#MyServiceName}', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  // Принудительно завершаем процессы, если они запущены
  Exec('taskkill.exe', '/F /IM {#MyAppExeName}', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Exec('taskkill.exe', '/F /IM {#MyServiceExeName}', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Sleep(500);
end;

function InitializeSetup(): Boolean;
begin
  Result := PrepareForInstall();
end;

function InitializeUninstall(): Boolean;
var
  ResultCode: Integer;
begin
  Exec('sc.exe', 'stop {#MyServiceName}', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Exec('taskkill.exe', '/F /IM {#MyAppExeName}', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Exec('taskkill.exe', '/F /IM {#MyServiceExeName}', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Sleep(500);
  Result := True;
end;
