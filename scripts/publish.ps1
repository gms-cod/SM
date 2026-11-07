# Скрипт надёжной публикации Startup Monitor в SMon2 и C:\Program Files\StartupMonitor
param(
    # Конфигурация передаётся из явно запрошенной MSBuild-цели развёртывания.
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',
    # Отсутствие сертификата является явным выбором, а не скрытым успехом подписи.
    [switch]$SkipSign,
    # Проверяемый интерфейс запускается в пользовательской сессии после подтверждённой установки.
    [switch]$LaunchUI
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

# Файлы заменяются одной транзакцией: отказ копирования/SCM восстанавливает прежние EXE и службу.
& (Join-Path $PSScriptRoot 'deploy-installed.ps1') -SourceDirectory $publishDir -LaunchUI:$LaunchUI

Write-Host "Публикация успешно завершена!" -ForegroundColor Green
