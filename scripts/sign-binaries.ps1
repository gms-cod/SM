param(
    [string[]]$Files = @()
)

$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path

# 1. Windows SDK signtool.exe
$signtoolPaths = @(
    "C:\Program Files (x86)\Windows Kits\10\bin\10.0.26100.0\x64\signtool.exe",
    "C:\Program Files (x86)\Windows Kits\10\bin\10.0.22621.0\x64\signtool.exe",
    "C:\Program Files (x86)\Windows Kits\10\bin\10.0.19041.0\x64\signtool.exe"
)

$signtool = $null
foreach ($path in $signtoolPaths) {
    if (Test-Path $path) {
        $signtool = $path
        break
    }
}

if (-not $signtool) {
    Write-Warning "signtool.exe not found in Windows Kits. Skipping signing."
    return
}

# 2. Find Certificate
$cert = Get-ChildItem Cert:\CurrentUser\My -CodeSigningCert | Where-Object { $_.Subject -like "*CN=Startup Monitor*" } | Select-Object -First 1
if (-not $cert) {
    $cert = Get-ChildItem Cert:\LocalMachine\My -CodeSigningCert | Where-Object { $_.Subject -like "*CN=Startup Monitor*" } | Select-Object -First 1
}

if (-not $cert) {
    Write-Warning "Code Signing certificate for Startup Monitor not found. Skipping signing."
    return
}

Write-Host "Found Certificate: $($cert.Subject) [SHA1: $($cert.Thumbprint)]" -ForegroundColor Green

# 3. Default files
if ($Files.Count -eq 0) {
    $Files = @(
        (Join-Path $repoRoot "SMon2\StartupMonitor.Service.exe"),
        (Join-Path $repoRoot "SMon2\StartupMonitor.UI.exe")
    )
    $installer = Get-ChildItem (Join-Path $repoRoot "installers") -Filter "StartupMonitorSetup-*.exe" | Select-Object -First 1
    if ($installer) {
        $Files += $installer.FullName
    }
}

# 4. Sign
$timestampServer = "http://timestamp.digicert.com"

foreach ($file in $Files) {
    if (Test-Path $file) {
        Write-Host "Signing: $file..." -ForegroundColor Cyan
        & $signtool sign /sha1 $($cert.Thumbprint) /fd SHA256 /tr $timestampServer /td SHA256 /d "Startup Monitor" $file
        if ($LASTEXITCODE -ne 0) {
            Write-Warning "Timestamp failed for $file. Retrying without timestamp..."
            & $signtool sign /sha1 $($cert.Thumbprint) /fd SHA256 /d "Startup Monitor" $file
        }
        
        & $signtool verify /pa /v $file | Out-Null
        if ($LASTEXITCODE -eq 0) {
            Write-Host "VERIFIED: $file is signed and trusted!" -ForegroundColor Green
        } else {
            Write-Host "SIGNED: $file (local root trust)" -ForegroundColor Yellow
        }
    } else {
        Write-Warning "File not found: $file"
    }
}