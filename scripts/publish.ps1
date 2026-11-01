# Скрипт надёжной публикации Startup Monitor в SMon2 и C:\Program Files\StartupMonitor
param(
    # Конфигурация передаётся из явно запрошенной MSBuild-цели развёртывания.
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release'
)
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path

Write-Host "1. Остановка службы и процессов..." -ForegroundColor Cyan
Stop-Service -Name 'StartupMonitor' -Force -ErrorAction SilentlyContinue
Stop-Process -Name 'StartupMonitor.Service','StartupMonitor.UI' -Force -ErrorAction SilentlyContinue
Start-Sleep -Milliseconds 500

Write-Host "2. Публикация StartupMonitor.Service..." -ForegroundColor Cyan
dotnet publish (Join-Path $root "src\StartupMonitor.Service\StartupMonitor.Service.csproj") `
    -o (Join-Path $root "SMon2") `
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
    -o (Join-Path $root "SMon2") `
    -c $Configuration `
    -r win-x64 `
    --self-contained `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:DebugType=embedded `
    -p:ExcludePublishToSMon=true `
    --nologo
if ($LASTEXITCODE -ne 0) { throw "Публикация UI завершилась с кодом $LASTEXITCODE." }

& (Join-Path $PSScriptRoot "sign-binaries.ps1") -Files @((Join-Path $root "SMon2\StartupMonitor.Service.exe"), (Join-Path $root "SMon2\StartupMonitor.UI.exe"))
Write-Host "4. Копирование иконок в SMon2..." -ForegroundColor Cyan
Copy-Item (Join-Path $root "assets\icon.svg") (Join-Path $root "SMon2\icon.svg") -Force
Copy-Item (Join-Path $root "assets\app.ico") (Join-Path $root "SMon2\app.ico") -Force

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
    & sc.exe config StartupMonitor binPath= "`"$pfDir\StartupMonitor.Service.exe`"" | Out-Null

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


