# Скрипт надёжной публикации Startup Monitor в SMon2 и C:\Program Files\StartupMonitor
param(
    # Конфигурация передаётся из явно запрошенной MSBuild-цели развёртывания.
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',
    # Отсутствие сертификата является явным выбором, а не скрытым успехом подписи.
    [switch]$SkipSign
)
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path

# Сборка выполняется отдельно от работающей копии; сбой компилятора не останавливает защиту.
$publishDir = Join-Path $root "out\publish-$Configuration"
New-Item -ItemType Directory -Path $publishDir -Force | Out-Null

Write-Host "2. Публикация StartupMonitor.Service..." -ForegroundColor Cyan
dotnet publish (Join-Path $root "src\StartupMonitor.Service\StartupMonitor.Service.csproj") `
    -o $publishDir `
    -c $Configuration `
    -r win-x64 `
    --self-contained `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:DebugType=embedded `
    -p:ExcludePublishToSMon=true `
    --nologo
if ($LASTEXITCODE -ne 0) { throw "Публикация службы завершилась с кодом $LASTEXITCODE." }

Write-Host "3. Публикация StartupMonitor.UI..." -ForegroundColor Cyan
dotnet publish (Join-Path $root "src\StartupMonitor.UI\StartupMonitor.UI.csproj") `
    -o $publishDir `
    -c $Configuration `
    -r win-x64 `
    --self-contained `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:DebugType=embedded `
    -p:ExcludePublishToSMon=true `
    --nologo
if ($LASTEXITCODE -ne 0) { throw "Публикация UI завершилась с кодом $LASTEXITCODE." }

if (-not $SkipSign) {
    $executables = @((Join-Path $publishDir 'StartupMonitor.Service.exe'), (Join-Path $publishDir 'StartupMonitor.UI.exe'))
    & (Join-Path $PSScriptRoot "sign-binaries.ps1") -Files $executables
    foreach ($executable in $executables) {
        if ((Get-AuthenticodeSignature -LiteralPath $executable).Status -ne 'Valid') {
            throw "Подпись $executable не подтверждена. Для явной публикации без подписи укажите -SkipSign."
        }
    }
}
else {
    Write-Warning "Публикуются бинарники без Authenticode-подписи (-SkipSign)."
}
Write-Host "4. Копирование иконок в SMon2..." -ForegroundColor Cyan
Copy-Item (Join-Path $root "assets\icon.svg") (Join-Path $publishDir 'icon.svg') -Force
Copy-Item (Join-Path $root "assets\app.ico") (Join-Path $publishDir 'app.ico') -Force

# Заменяются только готовые файлы после успешной сборки обоих модулей.
Write-Host "Остановка службы и замена проверенных бинарников..." -ForegroundColor Cyan
$service = Get-Service -Name 'StartupMonitor' -ErrorAction SilentlyContinue
if ($service -and $service.Status -ne 'Stopped') {
    Stop-Service -Name 'StartupMonitor' -ErrorAction Stop
    $service.WaitForStatus('Stopped', [TimeSpan]::FromSeconds(30))
}
Get-Process -Name 'StartupMonitor.UI' -ErrorAction SilentlyContinue | ForEach-Object {
    $null = $_.CloseMainWindow()
    if (-not $_.WaitForExit(2000)) { Stop-Process -Id $_.Id -Force }
}
New-Item -ItemType Directory -Path (Join-Path $root 'SMon2') -Force | Out-Null
foreach ($name in @('StartupMonitor.Service.exe','StartupMonitor.UI.exe','app.ico','icon.svg')) {
    Copy-Item -LiteralPath (Join-Path $publishDir $name) -Destination (Join-Path $root "SMon2\$name") -Force
}

$pfDir = "C:\Program Files\StartupMonitor"
if (Test-Path $pfDir) {
    Write-Host "5. Обновление файлов в $pfDir..." -ForegroundColor Cyan
    Stop-Service -Name 'StartupMonitor' -Force -ErrorAction SilentlyContinue
    Stop-Process -Name 'StartupMonitor.Service','StartupMonitor.UI' -Force -ErrorAction SilentlyContinue
    Start-Sleep -Milliseconds 500

    Copy-Item (Join-Path $root "SMon2\StartupMonitor*.exe") "$pfDir\" -Force
    Copy-Item (Join-Path $root "assets\icon.svg") "$pfDir\" -Force
    Copy-Item (Join-Path $root "assets\app.ico") "$pfDir\" -Force
    Get-ChildItem $pfDir -File | Unblock-File -ErrorAction SilentlyContinue

    Write-Host "5.1. Синхронизация пути службы в Windows Service Control Manager..." -ForegroundColor Cyan
    # CIM сохраняет кавычки пути с пробелами без неоднозначного экранирования native sc.exe в PowerShell.
    $registeredService = Get-CimInstance Win32_Service -Filter "Name = 'StartupMonitor'" -ErrorAction Stop
    if (-not $registeredService) { throw "Установленная служба StartupMonitor не зарегистрирована." }
    $change = Invoke-CimMethod -InputObject $registeredService -MethodName Change -Arguments @{
        PathName = '"' + (Join-Path $pfDir 'StartupMonitor.Service.exe') + '"'
    } -ErrorAction Stop
    if ($change.ReturnValue -ne 0) { throw "Не удалось обновить путь службы: код $($change.ReturnValue)." }

    Write-Host "5.2. Запуск обновлённой службы StartupMonitor в $pfDir..." -ForegroundColor Cyan
    Start-Service -Name 'StartupMonitor' -ErrorAction Stop
    $svc = Get-Service -Name 'StartupMonitor' -ErrorAction SilentlyContinue
    if ($svc -and $svc.Status -eq 'Running') {
        Write-Host "Служба StartupMonitor успешно запущена (версия обновлена)!" -ForegroundColor Green
    } else {
        throw "Служба StartupMonitor не запущена после обновления; проверьте журнал логов."
    }
}

Write-Host "6. Снятие блокировок безопасности с SMon2..." -ForegroundColor Cyan
Get-ChildItem (Join-Path $root "SMon2") -Recurse -File | Unblock-File -ErrorAction SilentlyContinue

Write-Host "Публикация успешно завершена!" -ForegroundColor Green


