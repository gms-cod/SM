<#
.SYNOPSIS
    Интеграционный сценарий верификации подсистемы ETW Process Lifecycle.
.DESCRIPTION
    Собирает и запускает тестовый инструмент EtwProcessProbe для подтверждения
    100% перехвата событий Start и Stop короткоживущих процессов через SystemTraceProvider.
.PARAMETER Count
    Количество тестовых helper-процессов для запуска (по умолчанию 5).
.PARAMETER Elevate
    Если указан и скрипт запущен без прав администратора, запустить зонд в повышенном процессе.
#>
param (
    [ValidateRange(1, 128)]
    [int]$Count = 5,
    [switch]$Elevate
)

$ErrorActionPreference = "Stop"
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$rootDir = Split-Path -Parent $scriptDir
$probeProject = Join-Path $rootDir "tools\EtwProcessProbe\EtwProcessProbe.csproj"
$probeExe = Join-Path $rootDir "tools\EtwProcessProbe\bin\Release\net8.0-windows10.0.17763\win-x64\EtwProcessProbe.exe"

Write-Host "===============================================================================" -ForegroundColor Cyan
Write-Host "    StartupMonitor: Интеграционная верификация ETW Process Lifecycle" -ForegroundColor Cyan
Write-Host "===============================================================================" -ForegroundColor Cyan

# 1. Сборка зонда
Write-Host "1. Сборка проекта EtwProcessProbe..." -ForegroundColor Gray
& dotnet build $probeProject -c Release --nologo
if ($LASTEXITCODE -ne 0) {
    Write-Error "Сборка EtwProcessProbe завершилась с ошибкой $LASTEXITCODE."
    exit $LASTEXITCODE
}

# 2. Проверка работоспособности helper-режима (non-elevated test)
Write-Host "2. Проверка запуска helper-процесса..." -ForegroundColor Gray
$helperOutput = & $probeExe --helper --duration 100
$joinedOutput = $helperOutput -join "`n"
if ($LASTEXITCODE -ne 0 -or $joinedOutput -notmatch "HELPER_STARTED") {
    Write-Error "Тестовый запуск helper-режима завершился неудачно."
    exit 1
}
Write-Host "   Helper режим функционирует штатно." -ForegroundColor Green

# 3. Проверка прав администратора для запуска реальной ETW сессии
$currentIdentity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = New-Object Security.Principal.WindowsPrincipal($currentIdentity)
$isAdmin = $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)

if (-not $isAdmin) {
    Write-Host "[ИНФО] Текущая сессия PowerShell не обладает повышенными привилегиями Администратора." -ForegroundColor Yellow
    Write-Host "       Для создания реальной native-сессии SystemTraceProvider требуются права Администратора." -ForegroundColor Yellow

    if ($Elevate) {
        Write-Host "Запуск EtwProcessProbe с повышением прав через UAC..." -ForegroundColor Cyan
        # Код повышенного зонда передаётся вызывающему процессу: отказ или ошибка не становятся ложным успехом.
        $probeProcess = Start-Process -FilePath $probeExe -ArgumentList "--count $Count" -Verb RunAs -Wait -PassThru
        exit $probeProcess.ExitCode
    } else {
        Write-Host "Для полного интеграционного теста перезапустите терминал от Администратора или вызовите:" -ForegroundColor Gray
        Write-Host "  .\scripts\test-etw-integration.ps1 -Elevate" -ForegroundColor Gray
        # Отсутствие привилегий означает невыполненную интеграционную проверку.
        exit 2
    }
}

# 4. Запуск зонда под правами Администратора
Write-Host "3. Запуск диагностического зонда ETW (Count: $Count)..." -ForegroundColor Cyan
& $probeExe --count $Count
$probeExitCode = $LASTEXITCODE

if ($probeExitCode -eq 0) {
    Write-Host "`n[УСПЕХ] Интеграционный тест ETW Process Lifecycle успешно пройден!" -ForegroundColor Green
} else {
    Write-Host "`n[ОШИБКА] Интеграционный тест ETW завершился с кодом $probeExitCode." -ForegroundColor Red
}

exit $probeExitCode
