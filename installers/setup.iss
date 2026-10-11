; Startup Monitor — инсталлятор Inno Setup 6
;
; ============ ОБНОВЛЕНИЯ ============
; Один и тот же setup можно запускать повторно «поверх»:
;  - перед установкой останавливаются служба StartupMonitor и процессы UI/Service;
;  - конфиги (*.json) и логи (logs/) НЕ входят в пакет и НЕ трогаются при обновлении;
;  - exe и иконки перезаписываются;
;  - после установки служба (если уже установлена) перезапускается, UI открывается.
;
; ВАЖНО: при каждом выпуске увеличивайте MyAppVersion (например 1.0.2) —
; иначе Inno Setup предложит «обслуживание» вместо переустановки.
;
; Сборка:  "C:\Program Files (x86)\Inno Setup 6\ISCC.exe" setup.iss
; Результат: ..\installers\StartupMonitorSetup-<версия>.exe

#define MyAppName "Startup Monitor"
#define MyAppVersion "1.2.4"
#define MyAppPublisher "Startup Monitor"
#define MyAppExeName "StartupMonitor.UI.exe"
#define MyServiceExeName "StartupMonitor.Service.exe"

[Setup]
AppId={{F2B8E0A4-9C51-4F6B-8E2C-3D5571B2E9A0}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\StartupMonitor
DefaultGroupName={#MyAppName}
OutputDir=.
OutputBaseFilename=StartupMonitorSetup-{#MyAppVersion}
SetupIconFile=..\SMon2\app.ico
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
DisableProgramGroupPage=yes
AllowNoIcons=yes
CloseApplications=force
CloseApplicationsFilter=*.exe,*.dll
RestartApplications=no
VersionInfoVersion={#MyAppVersion}.0
VersionInfoProductVersion={#MyAppVersion}
VersionInfoTextVersion={#MyAppVersion}

[Languages]
Name: "russian"; MessagesFile: "compiler:Languages\Russian.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
; Только бинарники и иконки. Конфиги и логи НЕ включаем — они создаются службой/UI
; и сохраняются при обновлениях.
Source: "..\SMon2\StartupMonitor.Service.exe"; DestDir: "{app}"; Flags: ignoreversion restartreplace uninsrestartdelete
Source: "..\SMon2\StartupMonitor.UI.exe"; DestDir: "{app}"; Flags: ignoreversion restartreplace uninsrestartdelete
Source: "..\SMon2\app.ico"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\SMon2\icon.svg"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\app.ico"
Name: "{group}\{cm:UninstallProgram,{#MyAppName}}"; Filename: "{uninstallexe}"; IconFilename: "{app}\app.ico"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\app.ico"; Tasks: desktopicon

[Run]
; Последний шаг — открыть UI. Он сам зарегистрирует службу (sc create) и запустит её,
; если установка чистая; при обновлении служба уже существует.
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName,'&','&&')}}"; Flags: nowait postinstall skipifsilent

[Code]
// Проверяет, установлена ли служба StartupMonitor в SCM.
function IsServiceInstalled(): Boolean;
begin
  Result := RegKeyExists(HKLM, 'SYSTEM\CurrentControlSet\Services\StartupMonitor');
end;

// Принудительно останавливает службу и закрывает процессы UI/Service для обновления.
procedure StopAppProcesses();
var
  ResultCode: Integer;
  I: Integer;
begin
  // 1. Остановка службы через net stop (дожидается остановки) и sc stop
  Exec(ExpandConstant('{sys}\net.exe'), 'stop StartupMonitor /y', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Exec(ExpandConstant('{sys}\sc.exe'), 'stop StartupMonitor', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);

  // 2. Принудительное мгновенное завершение всех процессов UI и Service через taskkill
  Exec(ExpandConstant('{sys}\taskkill.exe'), '/F /T /IM StartupMonitor.UI.exe', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Exec(ExpandConstant('{sys}\taskkill.exe'), '/F /T /IM StartupMonitor.Service.exe', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);

  // 3. Дополнительное завершение через PowerShell на случай разных сессий или повышенных прав
  Exec(ExpandConstant('{sys}\WindowsPowerShell\v1.0\powershell.exe'),
    '-NoProfile -ExecutionPolicy Bypass -Command "Stop-Process -Name StartupMonitor.UI,StartupMonitor.Service -Force -ErrorAction SilentlyContinue; Stop-Service -Name StartupMonitor -Force -ErrorAction SilentlyContinue"',
    '', SW_HIDE, ewWaitUntilTerminated, ResultCode);

  // 4. Пауза и повторный контрольный выстрел
  for I := 1 to 2 do
  begin
    Sleep(500);
    Exec(ExpandConstant('{sys}\taskkill.exe'), '/F /T /IM StartupMonitor.UI.exe', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
    Exec(ExpandConstant('{sys}\taskkill.exe'), '/F /T /IM StartupMonitor.Service.exe', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  end;
end;

// Закрываем программу сразу при старте установщика обновления
function InitializeSetup(): Boolean;
begin
  StopAppProcesses();
  Result := True;
end;

// Проверяет, остановлена ли служба StartupMonitor (состояние STOPPED или служба отсутствует).
function IsServiceStopped(): Boolean;
var
  ResultCode: Integer;
  TmpFile: String;
  OutText: AnsiString;
begin
  Result := True;
  TmpFile := ExpandConstant('{tmp}\sm_svcquery.txt');
  try
    if Exec(ExpandConstant('{sys}\cmd.exe'),
      '/c sc query StartupMonitor > "' + TmpFile + '" 2>&1',
      '', SW_HIDE, ewWaitUntilTerminated, ResultCode) then
    begin
      if FileExists(TmpFile) and LoadStringFromFile(TmpFile, OutText) then
      begin
        // 1060 / "не существует" — службы нет, считать остановленной
        if (Pos('STOPPED', String(OutText)) > 0) or (Pos('1060', String(OutText)) > 0) or
           (Pos('does not exist', String(OutText)) > 0) or
           (Pos('не существует', String(OutText)) > 0) then
        begin
          Result := True;
          Exit;
        end;
        // RUNNING / STOP_PENDING — ещё не остановлена
        Result := False;
      end;
    end;
  except
  end;
end;

// Проверяет, завершены ли процессы UI и Service.
function AreAppProcessesGone(): Boolean;
var
  ResultCode: Integer;
  TmpFile: String;
  OutText: AnsiString;
begin
  Result := True;
  TmpFile := ExpandConstant('{tmp}\sm_tasklist.txt');
  try
    if Exec(ExpandConstant('{sys}\cmd.exe'),
      '/c tasklist /FI "IMAGENAME eq StartupMonitor.UI.exe" /FI "IMAGENAME eq StartupMonitor.Service.exe" /FO CSV /NH > "' + TmpFile + '" 2>&1',
      '', SW_HIDE, ewWaitUntilTerminated, ResultCode) then
    begin
      if FileExists(TmpFile) and LoadStringFromFile(TmpFile, OutText) then
      begin
        if (Pos('StartupMonitor.UI.exe', String(OutText)) > 0) or
           (Pos('StartupMonitor.Service.exe', String(OutText)) > 0) then
          Result := False;
      end;
    end;
  except
  end;
end;

// Ожидает фактической остановки службы и завершения процессов (до ~20 с),
// повторяя taskkill каждую секунду. Гарантирует освобождение exe перед копированием.
procedure WaitForStop();
var
  I: Integer;
  ResultCode: Integer;
begin
  for I := 1 to 20 do
  begin
    if IsServiceStopped() and AreAppProcessesGone() then
    begin
      Log(Format('WaitForStop: service stopped and processes exited after %d sec', [I - 1]));
      Exit;
    end;
    Sleep(1000);
    // Повторный контрольный выстрел каждую итерацию
    Exec(ExpandConstant('{sys}\taskkill.exe'), '/F /T /IM StartupMonitor.UI.exe', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
    Exec(ExpandConstant('{sys}\taskkill.exe'), '/F /T /IM StartupMonitor.Service.exe', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  end;
  Log('WaitForStop: timeout reached, proceeding with installation anyway');
end;

// Повторно гарантируем закрытие перед копированием новых файлов
function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  StopAppProcesses();
  WaitForStop();
  Result := '';
end;

// Безопасно определяет путь к каталогу установки программы без раннего обращения к неинициализированной константе {app}.
function GetAppDir(): String;
var
  PrevPath: String;
begin
  try
    Result := ExpandConstant('{app}');
    if (Result <> '') and (Pos('{app}', Result) = 0) then Exit;
  except
  end;

  if RegQueryStringValue(HKLM, 'Software\Microsoft\Windows\CurrentVersion\Uninstall\{F2B8E0A4-9C51-4F6B-8E2C-3D5571B2E9A0}_is1', 'InstallLocation', PrevPath) and (PrevPath <> '') then
  begin
    if (Length(PrevPath) > 0) and (PrevPath[Length(PrevPath)] = '\') then
      SetLength(PrevPath, Length(PrevPath) - 1);
    Result := PrevPath;
    Exit;
  end;

  Result := ExpandConstant('{autopf}\StartupMonitor');
end;

// Проверяет настройку startWithWindows из сохраненного файла ui-settings.json.
// Возвращает True, если автозапуск разрешен (по умолчанию True для чистой установки или если опция включена).
function ShouldStartUiWithWindows(): Boolean;
var
  SettingsFile: String;
  JsonContent: AnsiString;
begin
  SettingsFile := GetAppDir() + '\ui-settings.json';
  if not FileExists(SettingsFile) then
  begin
    Log('ShouldStartUiWithWindows: ui-settings.json not found, default to True');
    Result := True;
    Exit;
  end;

  // Файл существует — программа уже была установлена и обновляется.
  // Проверяем, отключил ли пользователь автозапуск в настройках.
  if LoadStringFromFile(SettingsFile, JsonContent) then
  begin
    if (Pos('"startWithWindows":false', String(JsonContent)) > 0) or
       (Pos('"startWithWindows": false', String(JsonContent)) > 0) then
    begin
      Log('ShouldStartUiWithWindows: Found startWithWindows: false in settings');
      Result := False;
      Exit;
    end;
  end;

  Log('ShouldStartUiWithWindows: Autostart enabled (True)');
  Result := True;
end;

// Настраивает и запускает службу Windows StartupMonitor
procedure ConfigureAndStartService();
var
  ResultCode: Integer;
  AppDir: String;
  SvcExe: String;
  Sddl: String;
  Cmd: String;
begin
  AppDir := GetAppDir();
  SvcExe := AppDir + '\StartupMonitor.Service.exe';
  if not FileExists(SvcExe) then
  begin
    Log('ConfigureAndStartService: Service binary not found: ' + SvcExe);
    Exit;
  end;

  if not IsServiceInstalled() then
  begin
    // Чистая установка: регистрируем службу с автозапуском start=auto
    Log('ConfigureAndStartService: Service not installed. Running sc create...');
    Cmd := 'create StartupMonitor binPath= "\"' + SvcExe + '\" --windows-service" start= auto';
    Exec(ExpandConstant('{sys}\sc.exe'), Cmd, '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
    Log('sc create exit code=' + IntToStr(ResultCode));

    // Настройка автоматического восстановления при сбоях
    Cmd := 'failure StartupMonitor reset= 86400 actions= restart/5000/restart/10000/restart/30000';
    Exec(ExpandConstant('{sys}\sc.exe'), Cmd, '', SW_HIDE, ewWaitUntilTerminated, ResultCode);

    // Разрешаем интерактивным пользователям управлять службой
    Sddl := 'D:(A;;CCLCSWRPWPDTLOCRRC;;;SY)(A;;CCDCLCSWRPWPDTLOCRSDRCWDWO;;;BA)(A;;CCLCSWRPWPRC;;;IU)(A;;CCLCSWLOCRRC;;;SU)S:(AU;FA;CCDCLCSWRPWPDTLOCRSDRCWDWO;;;WD)';
    Exec(ExpandConstant('{sys}\sc.exe'), 'sdset StartupMonitor "' + Sddl + '"', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  end
  else
  begin
    // Обновление программы: актуализируем путь к службе и режим автозапуска
    Log('ConfigureAndStartService: Service already installed. Running sc config...');
    Cmd := 'config StartupMonitor binPath= "\"' + SvcExe + '\" --windows-service" start= auto';
    Exec(ExpandConstant('{sys}\sc.exe'), Cmd, '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
    Log('sc config exit code=' + IntToStr(ResultCode));
  end;

  // Запуск службы
  Log('ConfigureAndStartService: Starting service...');
  Exec(ExpandConstant('{sys}\sc.exe'), 'start StartupMonitor', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Log('sc start exit code=' + IntToStr(ResultCode));
end;

// Настраивает автозапуск UI в реестре Windows в соответствии с опциями
procedure ConfigureUiAutostart();
var
  AppDir: String;
  UiCmd: String;
  Ok: Boolean;
begin
  AppDir := GetAppDir();
  UiCmd := '"' + AppDir + '\StartupMonitor.UI.exe" --minimized';

  if ShouldStartUiWithWindows() then
  begin
    Log('ConfigureUiAutostart: Registering UI autostart in HKCU Run: ' + UiCmd);
    Ok := RegWriteStringValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run', 'StartupMonitor.UI', UiCmd);
    Log('ConfigureUiAutostart: RegWriteStringValue result=' + IntToStr(Ord(Ok)));
  end
  else
  begin
    Log('ConfigureUiAutostart: UI autostart is disabled in settings, removing HKCU Run key');
    RegDeleteValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run', 'StartupMonitor.UI');
  end;

  // Очищаем возможные устаревшие или дублирующие записи в HKLM и HKCU
  RegDeleteValue(HKLM, 'Software\Microsoft\Windows\CurrentVersion\Run', 'StartupMonitor.UI');
  RegDeleteValue(HKLM, 'Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Run', 'StartupMonitor.UI');
  RegDeleteValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run', 'StartupMonitorUI');
end;

// Запускает пользовательский интерфейс программы после завершения установки
procedure LaunchUiProcess();
var
  ResultCode: Integer;
  AppDir: String;
begin
  AppDir := GetAppDir();
  Log('LaunchUiProcess: launching ' + AppDir + '\StartupMonitor.UI.exe');
  Exec(AppDir + '\StartupMonitor.UI.exe', '', '', SW_SHOWNORMAL, ewNoWait, ResultCode);
end;

// Удаляет остаточные *.tmp файлы в каталоге установки (обрывки прошлых обновлений/загрузок).
procedure CleanupTmpFiles();
var
  FindRec: TFindRec;
  AppDir: String;
begin
  AppDir := GetAppDir();
  if FindFirst(AppDir + '\*.tmp', FindRec) then
  try
    repeat
      if (FindRec.Attributes and FILE_ATTRIBUTE_DIRECTORY) = 0 then
      begin
        Log('CleanupTmpFiles: deleting ' + AppDir + '\' + FindRec.Name);
        DeleteFile(AppDir + '\' + FindRec.Name);
      end;
    until not FindNext(FindRec);
  finally
    FindClose(FindRec);
  end;
end;

// Действия после копирования файлов (настройка автозапуска службы и UI)
procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssPostInstall then
  begin
    Log('CurStepChanged: ssPostInstall - configuring service and UI autostart');
    CleanupTmpFiles();
    ConfigureAndStartService();
    ConfigureUiAutostart();
    LaunchUiProcess();
  end;
end;

// Действия при удалении программы
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  ResultCode: Integer;
begin
  if CurUninstallStep = usUninstall then
  begin
    StopAppProcesses();
    // Удаляем службу Windows
    Exec(ExpandConstant('{sys}\sc.exe'), 'delete StartupMonitor', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
    // Удаляем автозапуск UI из реестра
    RegDeleteValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run', 'StartupMonitor.UI');
    RegDeleteValue(HKLM, 'Software\Microsoft\Windows\CurrentVersion\Run', 'StartupMonitor.UI');
    RegDeleteValue(HKLM, 'Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Run', 'StartupMonitor.UI');
  end;
end;