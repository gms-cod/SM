param(
    [string]$Version = "",
    [switch]$Bump
)

# Сборка инсталлятора Startup Monitor с синхронизацией версий программы и инсталлятора.
$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$iss = Join-Path $PSScriptRoot 'setup.iss'
$propsPath = Join-Path $repoRoot 'Directory.Build.props'
$utf8NoBom = [System.Text.UTF8Encoding]::new($false)

$text = [System.IO.File]::ReadAllText($iss, $utf8NoBom)
$m = [regex]::Match($text, '(#define MyAppVersion ")(\d+)\.(\d+)\.(\d+)(")')
if (-not $m.Success) { throw 'MyAppVersion в setup.iss не найден' }

$currentVer = "$($m.Groups[2]).$($m.Groups[3]).$($m.Groups[4])"

if ($Version) {
    $targetVer = $Version
} elseif ($Bump) {
    $targetVer = "$($m.Groups[2]).$($m.Groups[3]).$([int]$m.Groups[4].Value + 1)"
} else {
    $targetVer = $currentVer
}

# 1. Синхронизируем версию в setup.iss
if ($targetVer -ne $currentVer) {
    $text = $text.Replace($m.Value, $m.Groups[1].Value + $targetVer + $m.Groups[5].Value)
    [System.IO.File]::WriteAllText($iss, $text, $utf8NoBom)
}

# 2. Синхронизируем Directory.Build.props
$propsXml = @"
<Project>
  <PropertyGroup>
    <Version>$targetVer</Version>
    <AssemblyVersion>$targetVer.0</AssemblyVersion>
    <FileVersion>$targetVer.0</FileVersion>
    <InformationalVersion>$targetVer</InformationalVersion>
  </PropertyGroup>
</Project>
"@
[System.IO.File]::WriteAllText($propsPath, $propsXml, $utf8NoBom)

Write-Host "Версия программы и инсталлятора: $targetVer" -ForegroundColor Green

# 3. Публикуем свежие бинарники с новой версией в SMon2
Write-Host "Публикация бинарников в SMon2..." -ForegroundColor Cyan
& (Join-Path $repoRoot 'scripts\publish.ps1')

# 4. Собираем инсталлятор через Inno Setup
$iscc = "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe"
if (-not (Test-Path $iscc)) { $iscc = 'C:\Program Files (x86)\Inno Setup 6\ISCC.exe' }
if (-not (Test-Path $iscc)) { $iscc = 'C:\Program Files\Inno Setup 6\ISCC.exe' }
if (-not (Test-Path $iscc)) { throw "Компилятор Inno Setup (ISCC.exe) не найден!" }

Write-Host "Сборка инсталлятора через Inno Setup..." -ForegroundColor Cyan
& $iscc $iss

$setupFile = Join-Path $repoRoot "installers\StartupMonitorSetup-$targetVer.exe"
if (Test-Path $setupFile) {
    Write-Host "Установщик успешно собран: $setupFile" -ForegroundColor Green
& (Join-Path $repoRoot "scripts\sign-binaries.ps1") -Files @($setupFile)
} else {
    throw "Файл установщика не найден после компиляции!"
}
