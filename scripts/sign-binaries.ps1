param(
    [string[]]$Files = @()
)

$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path

Write-Host "=== Podpis fajlov (Gms CodeWorks Authenticode) ===" -ForegroundColor Cyan

# 1. Poisk sertifikata
$cert = Get-ChildItem Cert:\CurrentUser\My -CodeSigningCert | Where-Object { $_.Subject -like "*Gms CodeWorks*" } | Select-Object -First 1
if (-not $cert) {
    $cert = Get-ChildItem Cert:\LocalMachine\My -CodeSigningCert | Where-Object { $_.Subject -like "*Gms CodeWorks*" } | Select-Object -First 1
}

if (-not $cert) {
    Write-Warning "Sertifikat Gms CodeWorks ne najden!"
    return
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
    if (-not (Test-Path $file)) {
        continue
    }

    Write-Host "Podpis: $file..." -ForegroundColor Cyan
    try {
        $res = Set-AuthenticodeSignature -FilePath $file -Certificate $cert -TimestampServer $timestampServer -HashAlgorithm SHA256
        Write-Host "Status: $($res.Status)" -ForegroundColor Green
    } catch {
        Write-Warning "Oshibka: $($_.Exception.Message)"
    }

    $chk = Get-AuthenticodeSignature -FilePath $file
    Write-Host "Proverka: [$($chk.Status)] $($chk.SignerCertificate.Subject)" -ForegroundColor Cyan
}

Write-Host "Podpis uspeshno zavershena!" -ForegroundColor Green
