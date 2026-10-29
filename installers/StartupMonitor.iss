; =====================================================================
; Inno Setup Script for Startup Monitor
; =====================================================================

#ifndef MyAppVersion
  #define MyAppVersion "2.0.0"
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
ShowLanguageDialog=auto
; Закрытие запущенных процессов StartupMonitor*.exe перед установкой (через Restart Manager)
CloseApplications=yes
CloseApplicationsFilter=StartupMonitor*.exe
; Попытка автоматического перезапуска приложений, закрытых инсталлятором, после установки.
RestartApplications=yes
UsedUserAreasWarning=no

[Languages]
Name: "russian"; MessagesFile: "compiler:Languages\Russian.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[CustomMessages]
russian.AutoStartTask=Запускать Startup Monitor при входе в систему (рекомендуется)
english.AutoStartTask=Launch Startup Monitor at Windows startup (recommended)
russian.AutoStartGroup=Параметры запуска:
english.AutoStartGroup=Startup options:
russian.CreateDesktopIcon=Создать ярлык на Рабочем столе
english.CreateDesktopIcon=Create a desktop shortcut
russian.AdditionalIcons=Дополнительные значки:
english.AdditionalIcons=Additional shortcuts:
russian.UninstallProgram=Удалить {#MyAppName}
english.UninstallProgram=Uninstall {#MyAppName}
russian.RegisterService=Регистрация службы Startup Monitor...
english.RegisterService=Registering Startup Monitor Service...
russian.ServiceDescription=Мониторинг автозагрузки Windows в реальном времени и защита от нежелательных программ.
english.ServiceDescription=Real-time Windows startup monitoring and security protection.
russian.StartService=Запуск службы Startup Monitor...
english.StartService=Starting Startup Monitor Service...
russian.LaunchApp=Запустить Startup Monitor сейчас
english.LaunchApp=Launch Startup Monitor now

[Tasks]
Name: "autostart"; Description: "{cm:AutoStartTask}"; GroupDescription: "{cm:AutoStartGroup}"
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "..\SMon2\StartupMonitor.Service.exe"; DestDir: "{app}"; Flags: ignoreversion restartreplace
Source: "..\SMon2\StartupMonitor.UI.exe"; DestDir: "{app}"; Flags: ignoreversion restartreplace
Source: "..\SMon2\app.ico"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\SMon2\icon.svg"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\app.ico"
Name: "{group}\{cm:UninstallProgram}"; Filename: "{uninstallexe}"
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
Filename: "sc.exe"; Parameters: "create {#MyServiceName} binPath= """"{app}\{#MyServiceExeName}"""" start= auto DisplayName= """"Startup Monitor Service"""""; Flags: runhidden; StatusMsg: "{cm:RegisterService}"
Filename: "sc.exe"; Parameters: "config {#MyServiceName} binPath= """"{app}\{#MyServiceExeName}"""" start= auto DisplayName= """"Startup Monitor Service"""""; Flags: runhidden
Filename: "sc.exe"; Parameters: "description {#MyServiceName} """"{cm:ServiceDescription}"""""; Flags: runhidden
Filename: "sc.exe"; Parameters: "failure {#MyServiceName} reset= 86400 actions= restart/5000/restart/10000/restart/60000"; Flags: runhidden
; Запуск службы после установки/обновления
Filename: "sc.exe"; Parameters: "start {#MyServiceName}"; Flags: runhidden; StatusMsg: "{cm:StartService}"
; Опциональный запуск UI для пользователя при интерактивной установке
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchApp}"; Flags: nowait postinstall skipifsilent runasoriginaluser

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
