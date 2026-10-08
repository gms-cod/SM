# Скрипт надёжной публикации Startup Monitor в SMon2 и C:\Program Files\StartupMonitor
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path

Write-Host "1. Остановка службы и процессов..." -ForegroundColor Cyan
Stop-Service -Name 'StartupMonitor' -Force -ErrorAction SilentlyContinue
Stop-Process -Name 'StartupMonitor.Service','StartupMonitor.UI' -Force -ErrorAction SilentlyContinue
Start-Sleep -Milliseconds 500

Write-Host "2. Публикация StartupMonitor.Service..." -ForegroundColor Cyan
dotnet publish (Join-Path $root "src\StartupMonitor.Service\StartupMonitor.Service.csproj") `
    -o (Join-Path $root "SMon2") `
    -c Release `
    -r win-x64 `
    --self-contained `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:DebugType=embedded `
    -p:ExcludePublishToSMon=true `
    --nologo

Write-Host "3. Публикация StartupMonitor.UI..." -ForegroundColor Cyan
dotnet publish (Join-Path $root "src\StartupMonitor.UI\StartupMonitor.UI.csproj") `
    -o (Join-Path $root "SMon2") `
    -c Release `
    -r win-x64 `
    --self-contained `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:DebugType=embedded `
    -p:ExcludePublishToSMon=true `
    --nologo

& (Join-Path $PSScriptRoot "sign-binaries.ps1") -Files @((Join-Path $root "SMon2\StartupMonitor.Service.exe"), (Join-Path $root "SMon2\StartupMonitor.UI.exe"))
Write-Host "4. Копирование иконок в SMon2..." -ForegroundColor Cyan
Copy-Item (Join-Path $root "assets\icon.svg") (Join-Path $root "SMon2\icon.svg") -Force -ErrorAction SilentlyContinue
Copy-Item (Join-Path $root "assets\app.ico") (Join-Path $root "SMon2\app.ico") -Force -ErrorAction SilentlyContinue

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
}

Write-Host "6. Снятие блокировок безопасности с SMon2..." -ForegroundColor Cyan
Get-ChildItem (Join-Path $root "SMon2") -Recurse -File | Unblock-File -ErrorAction SilentlyContinue
Add-MpPreference -ExclusionPath (Resolve-Path (Join-Path $root "SMon2")) -ErrorAction SilentlyContinue

Write-Host "Публикация успешно завершена!" -ForegroundColor Green

