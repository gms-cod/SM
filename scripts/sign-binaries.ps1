<#
.SYNOPSIS
Подписывает проверяемые файлы доступным сертификатом издателя с закрытым ключом.
.PARAMETER Files
Точные пути подписываемых EXE; отсутствие файла или действительной подписи прерывает выпуск.
#>
param(
    [string[]]$Files = @()
)

$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path

Write-Host "=== Authenticode signing ===" -ForegroundColor Cyan

# Сохраняем совместимость с прежним издателем и текущим локальным сертификатом Startup Monitor.
# Выбирается только действующий сертификат Code Signing с доступным закрытым ключом.
$cert = Get-ChildItem Cert:\CurrentUser\My, Cert:\LocalMachine\My -CodeSigningCert |
    Where-Object { ($_.Subject -like '*Gms CodeWorks*' -or $_.Subject -eq 'CN=Startup Monitor, O=Startup Monitor, C=RU') -and
        $_.HasPrivateKey -and $_.NotBefore -le (Get-Date) -and $_.NotAfter -gt (Get-Date) } |
    Sort-Object @{ Expression = { $_.Subject -like '*Gms CodeWorks*' }; Descending = $true }, NotAfter -Descending |
    Select-Object -First 1

if (-not $cert) {
    throw 'Не найден действующий сертификат подписи издателя с закрытым ключом.'
}

Write-Host "Najden sertifikat: $($cert.Subject)" -ForegroundColor Green
Write-Host "Thumbprint: $($cert.Thumbprint)" -ForegroundColor Green

# 2. Fajly dlja podpisi
if ($Files.Count -eq 0) {
    [xml]$propsXml = Get-Content (Join-Path $repoRoot "Directory.Build.props")
    $ver = $propsXml.Project.PropertyGroup.Version
    if (-not $ver) { $ver = "1.9.2" }
    $Files = @(
        (Join-Path $repoRoot "SMon2\StartupMonitor.Service.exe"),
        (Join-Path $repoRoot "SMon2\StartupMonitor.UI.exe"),
        (Join-Path $repoRoot "installers\StartupMonitorSetup-$ver.exe")
    )
}

$timestampServer = "http://timestamp.digicert.com"

foreach ($file in $Files) {
    if (-not (Test-Path -LiteralPath $file -PathType Leaf)) {
        throw "Подписываемый файл не найден: $file"
    }

    Write-Host "Podpis: $file..." -ForegroundColor Cyan
    try {
        $res = Set-AuthenticodeSignature -FilePath $file -Certificate $cert -TimestampServer $timestampServer -HashAlgorithm SHA256
        Write-Host "Status: $($res.Status)" -ForegroundColor Green
    } catch {
        throw "Не удалось подписать ${file}: $($_.Exception.Message)"
    }

    $chk = Get-AuthenticodeSignature -FilePath $file
    # Успех объявляется только после проверки реальной подписи, включая цепочку доверия этой машины.
    if ($chk.Status -ne 'Valid') { throw "Подпись $file не подтверждена: $($chk.Status)." }
    Write-Host "Proverka: [$($chk.Status)] $($chk.SignerCertificate.Subject)" -ForegroundColor Cyan
}

Write-Host "Podpis uspeshno zavershena!" -ForegroundColor Green
