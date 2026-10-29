param(
    [switch]$SkipPublish,
    [switch]$SkipSign
)

$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path

Write-Host "=== Сборка инсталлятора Startup Monitor ===" -ForegroundColor Cyan

# 1. Извлечение версии из Directory.Build.props
$propsFile = Join-Path $repoRoot "Directory.Build.props"
[xml]$propsXml = Get-Content $propsFile
$version = $propsXml.Project.PropertyGroup.Version
if (-not $version) {
    $version = "1.3.1"
}
Write-Host "Версия продукта: $version" -ForegroundColor Green

# 2. Публикация Release бинарников (если не пропущена)
if (-not $SkipPublish) {
    Write-Host "`n--> Выполняется публикация проектов..." -ForegroundColor Yellow
    & (Join-Path $PSScriptRoot "publish.ps1")
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

& $iscc "/DMyAppVersion=$version" $issFile
if ($LASTEXITCODE -ne 0) {
    throw "Ошибка компиляции Inno Setup! Код выхода: $LASTEXITCODE"
}

$installerExe = Join-Path $installerDir "StartupMonitorSetup-$version.exe"
if (-not (Test-Path $installerExe)) {
    throw "Выходной инсталлятор не найден по пути $installerExe!"
}

$fileSizeMb = [math]::Round((Get-Item $installerExe).Length / 1MB, 2)
Write-Host "`nИнсталлятор успешно собран: $installerExe ($fileSizeMb MB)" -ForegroundColor Green

# 5. Цифровая подпись инсталлятора
if (-not $SkipSign) {
    Write-Host "`n--> Выполняется подпись инсталлятора..." -ForegroundColor Yellow
    try {
        & (Join-Path $PSScriptRoot "sign-binaries.ps1") -Files @($installerExe)
    }
    catch {
        Write-Warning "Подпись инсталлятора не удалась: $($_.Exception.Message)"
    }
}

# 6. Вычисление и сохранение SHA-256
Write-Host "`n--> Вычисление контрольной суммы SHA-256..." -ForegroundColor Yellow
$hashResult = Get-FileHash -Path $installerExe -Algorithm SHA256
$sha256 = $hashResult.Hash.ToLowerInvariant()
$sha256File = "$installerExe.sha256"
"$sha256 *StartupMonitorSetup-$version.exe" | Set-Content -Path $sha256File -Encoding utf8

Write-Host "SHA-256: $sha256" -ForegroundColor Cyan
Write-Host "Хэш сохранён в: $sha256File" -ForegroundColor Cyan
Write-Host "`n=== Сборка инсталлятора завершена успешно! ===" -ForegroundColor Green
