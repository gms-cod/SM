<#
.SYNOPSIS
Сборка, подпись и валидация инсталлятора Startup Monitor через Inno Setup.
#>
param(
    [switch]$SkipPublish,
    [switch]$SkipSign,
    [string]$Suffix
)

$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path

Write-Host "=== Сборка инсталлятора Startup Monitor ===" -ForegroundColor Cyan

# 1. Извлечение версии из Directory.Build.props
$propsFile = Join-Path $repoRoot "Directory.Build.props"
[xml]$propsXml = Get-Content $propsFile
$version = $propsXml.Project.PropertyGroup.Version
if (-not $version) {
    throw "Версия не задана в Directory.Build.props."
}
Write-Host "Версия продукта: $version" -ForegroundColor Green

# Установщик нельзя повторно собирать под уже использованной версией тега.
$versionTags = & git -C $repoRoot tag --list 'v*'
if ($LASTEXITCODE -ne 0) { throw "Не удалось проверить использованные версии Git." }
foreach ($tag in $versionTags) {
    if ($tag -match '^v(\d+\.\d+\.\d+)$' -and [version]$version -le [version]$Matches[1]) {
        throw "Версия $version должна быть строго выше уже существующего тега $tag."
    }
}

# Локальные теги могут не содержать уже опубликованные версии обоих репозиториев.
# Ошибка чтения удалённых тегов останавливает сборку до обновления рабочих EXE.
foreach ($remote in @('origin', 'startup-monitor')) {
    $remoteTags = @(& git -C $repoRoot ls-remote --tags $remote 2>$null)
    if ($LASTEXITCODE -ne 0) { throw "Не удалось проверить удалённые теги репозитория $remote." }
    foreach ($line in $remoteTags) {
        if ($line -match '\srefs/tags/(v(\d+\.\d+\.\d+))$' -and [version]$version -le [version]$Matches[2]) {
            throw "Версия $version должна быть строго выше удалённого тега $($Matches[1]) в $remote."
        }
    }
}

# 2. Публикация Release бинарников (если не пропущена)
if (-not $SkipPublish) {
    Write-Host "`n--> Выполняется публикация проектов..." -ForegroundColor Yellow
    & (Join-Path $PSScriptRoot "publish.ps1") -SkipSign:$SkipSign
}

# Пропуск публикации допустим только для свежих бинарников той же версии.
foreach ($name in @('StartupMonitor.Service.exe', 'StartupMonitor.UI.exe')) {
    $payload = Get-Item -LiteralPath (Join-Path $repoRoot "SMon2\$name") -ErrorAction Stop
    if ([version]$payload.VersionInfo.FileVersion -ne [version]"$version.0") {
        throw "Бинарник $name имеет версию $($payload.VersionInfo.FileVersion), ожидалась $version.0."
    }
}

# 3. Поиск компилятора Inno Setup
$isccPaths = @(
    "$env:LocalAppData\Programs\Inno Setup 7\ISCC.exe",
    "C:\Program Files\Inno Setup 7\ISCC.exe",
    "$env:LocalAppData\Programs\Inno Setup 6\ISCC.exe",
    "C:\Program Files\Inno Setup 6\ISCC.exe",
    "C:\Program Files (x86)\Inno Setup 6\ISCC.exe"
)

$iscc = $null
foreach ($path in $isccPaths) {
    if (Test-Path $path) {
        $iscc = $path
        break
    }
}

if (-not $iscc) {
    $cmd = Get-Command "iscc.exe" -ErrorAction SilentlyContinue
    if ($cmd) {
        $iscc = $cmd.Source
    }
}

if (-not $iscc) {
    throw "Компилятор Inno Setup (ISCC.exe) не найден! Установите Inno Setup 6 или 7."
}

Write-Host "Inno Setup Compiler: $iscc" -ForegroundColor Green

# 4. Компиляция инсталлятора
$issFile = Join-Path $repoRoot "installers\StartupMonitor.iss"
if (-not (Test-Path $issFile)) {
    throw "Файл $issFile не найден!"
}

Write-Host "`n--> Компиляция инсталлятора через Inno Setup..." -ForegroundColor Yellow
$installerDir = Join-Path $repoRoot "installers"
if (-not (Test-Path $installerDir)) {
    New-Item -ItemType Directory -Path $installerDir -Force | Out-Null
}

$outputBase = if ($Suffix) { "StartupMonitorSetup-$version-$Suffix" } else { "StartupMonitorSetup-$version" }

& $iscc "/DMyAppVersion=$version" "/DMyOutputBaseFilename=$outputBase" $issFile
if ($LASTEXITCODE -ne 0) {
    throw "Ошибка компиляции Inno Setup! Код выхода: $LASTEXITCODE"
}

$installerExe = Join-Path $installerDir "$outputBase.exe"
if (-not (Test-Path $installerExe)) {
    throw "Выходной инсталлятор не найден по пути $installerExe!"
}

$fileSizeMb = [math]::Round((Get-Item $installerExe).Length / 1MB, 2)
Write-Host "`nИнсталлятор успешно собран: $installerExe ($fileSizeMb MB)" -ForegroundColor Green

# 5. Цифровая подпись инсталлятора
if (-not $SkipSign) {
    Write-Host "`n--> Выполняется подпись инсталлятора..." -ForegroundColor Yellow
    & (Join-Path $PSScriptRoot "sign-binaries.ps1") -Files @($installerExe)
    if ((Get-AuthenticodeSignature -LiteralPath $installerExe).Status -ne 'Valid') {
        throw "Запрошенная подпись инсталлятора не подтверждена. Для явной сборки без подписи укажите -SkipSign."
    }
}
else {
    Write-Warning "Инсталлятор собран без Authenticode-подписи; целостность подтверждается SHA-256."
}

# 6. Вычисление и сохранение SHA-256
Write-Host "`n--> Вычисление контрольной суммы SHA-256..." -ForegroundColor Yellow
$hashResult = Get-FileHash -Path $installerExe -Algorithm SHA256
$sha256 = $hashResult.Hash.ToLowerInvariant()
$sha256File = "$installerExe.sha256"
"$sha256 *$outputBase.exe" | Set-Content -Path $sha256File -Encoding utf8

Write-Host "SHA-256: $sha256" -ForegroundColor Cyan
Write-Host "Хэш сохранён в: $sha256File" -ForegroundColor Cyan
Write-Host "`n=== Сборка инсталлятора завершена успешно! ===" -ForegroundColor Green
