<#
.SYNOPSIS
Скрипт двухпоточной синхронизации репозиториев Startup Monitor.
Обеспечивает строгое разделение кодовой базы:
1. Публичный репозиторий (origin: gms-cod/SM) — чистая витрина, релизы и открытая документация.
2. Приватный репозиторий (startup-monitor: gms-cod/Startup-Monitor) — 100% полный снимок (включая правила ИИ, скрипты роя и приватные аудиты).
#>
param(
    [switch]$SkipPublic,
    [switch]$SkipPrivate,
    [switch]$NoForcePrivate,
    [switch]$Force,
    # Создаёт новый релиз из уже проверенного установщика; существующие релизы не перезаписываются.
    [switch]$PublishRelease
)

$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
Set-Location $repoRoot

Write-Host "============================================================" -ForegroundColor Cyan
Write-Host "   Двухпоточная синхронизация репозиториев Startup Monitor" -ForegroundColor Cyan
Write-Host "============================================================" -ForegroundColor Cyan

# 1. Проверка текущей ветки
$currentBranch = (git branch --show-current).Trim()
if ($currentBranch -ne 'main') {
    throw "Синхронизация разрешена только из ветки 'main'. Текущая ветка: '$currentBranch'."
}

# 2. Проверка чистоты рабочей директории
$status = (git status --porcelain)
if ($status) {
    Write-Warning "Обнаружены незафиксированные локальные изменения:`n$status"
    if (-not $Force) {
        throw "Рабочая директория содержит незафиксированные изменения. Зафиксируйте их или укажите ключ -Force."
    }
}

# 3. Получение текущей версии продукта
[xml]$propsXml = Get-Content (Join-Path $repoRoot "Directory.Build.props")
$version = $propsXml.Project.PropertyGroup.Version
if (-not $version) { $version = "2.1.2" }
$headCommit = (git rev-parse HEAD).Trim()

Write-Host "Текущий коммит: $headCommit" -ForegroundColor DarkGray
Write-Host "Версия продукта: v$version" -ForegroundColor DarkGray
Write-Host ""

# До первого push проверяются версия, подпись, checksum и отсутствие уже выпущенного тега.
if ($PublishRelease) {
    if ($Force) { throw 'Выпуск релиза требует чистого зафиксированного дерева; -Force недопустим.' }
    $installer = Join-Path $repoRoot "installers\StartupMonitorSetup-$version.exe"
    $releaseNotes = Join-Path $repoRoot "docs\releases\v$version.md"
    foreach ($path in @($installer, "$installer.sha256", $releaseNotes)) {
        if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Не найден артефакт выпуска: $path" }
    }
    if ((Get-AuthenticodeSignature -LiteralPath $installer).Status -ne 'Valid') { throw 'Подпись установщика не подтверждена.' }
    $releaseRepos = @()
    if (-not $SkipPublic) { $releaseRepos += 'gms-cod/SM' }
    if (-not $SkipPrivate) { $releaseRepos += 'gms-cod/Startup-Monitor' }
    if ($releaseRepos.Count -eq 0) { throw 'Не выбран репозиторий для выпуска.' }
    & python (Join-Path $PSScriptRoot 'publish_github_release.py') --version $version --repos @releaseRepos --check-only
    if ($LASTEXITCODE -ne 0) { throw 'Предварительная проверка выпуска не пройдена.' }
}

# ============================================================
# ШАГ 1: Публикация в открытый публичный репозиторий (gms-cod/SM)
# ============================================================
if (-not $SkipPublic) {
    Write-Host "--> [1/2] Отправка в публичный репозиторий: origin (gms-cod/SM)..." -ForegroundColor Yellow
    
    git push origin main
    if ($LASTEXITCODE -ne 0) {
        throw "Сбой отправки ветки main в публичный репозиторий origin."
    }

    Write-Host "[УСПЕХ] Публичный репозиторий origin/main синхронизирован (чистый открытый код)." -ForegroundColor Green
}
else {
    Write-Host "--> [1/2] Пропуск отправки в публичный репозиторий (SkipPublic)." -ForegroundColor DarkGray
}

Write-Host ""

# ============================================================
# ШАГ 2: Публикация в закрытый приватный репозиторий (gms-cod/Startup-Monitor)
# ============================================================
if (-not $SkipPrivate) {
    Write-Host "--> [2/2] Формирование полного снимка для приватного репозитория: startup-monitor..." -ForegroundColor Yellow

    $gitDir = (git rev-parse --git-dir).Trim()
    $tempIndexFile = Join-Path $gitDir "temp_private_index"
    if (Test-Path $tempIndexFile) {
        Remove-Item -Force $tempIndexFile
    }

    try {
        $env:GIT_INDEX_FILE = $tempIndexFile

        # Инициализируем временный индекс текущим коммитом HEAD
        git read-tree HEAD
        if ($LASTEXITCODE -ne 0) {
            throw "Ошибка чтения дерева HEAD во временный индекс."
        }

        # Список внутренних файлов и каталогов для включения в приватный репозиторий
        $privateItems = @(
            "AGENTS.md",
            "GEMINI.md",
            "SWARM_RULES.md",
            "Rulez",
            ".agents",
            "opencode.json",
            "mcp_config.json",
            "scripts",
            "docs"
        )

        foreach ($item in $privateItems) {
            $fullPath = Join-Path $repoRoot $item
            if (Test-Path $fullPath) {
                git add -f $item
                if ($LASTEXITCODE -ne 0) {
                    Write-Warning "Предупреждение: не удалось добавить '$item' во временный индекс."
                }
            }
        }

        $treeHash = (git write-tree).Trim()
        if (-not $treeHash -or $LASTEXITCODE -ne 0) {
            throw "Ошибка создания дерева Git (git write-tree)."
        }

        $commitMessage = "v${version}: sync(private): full repository snapshot with internal swarm assets"
        $privateCommit = (git commit-tree $treeHash -p $headCommit -m $commitMessage).Trim()
        if (-not $privateCommit) {
            throw "Ошибка создания коммита полного снимка (git commit-tree)."
        }

        Write-Host "Сформирован коммит полного снимка: $privateCommit" -ForegroundColor DarkCyan

        $pushTarget = "$privateCommit" + ":refs/heads/main"
        $pushArgs = @("push", "startup-monitor", $pushTarget)
        if (-not $NoForcePrivate) {
            $pushArgs += "--force"
        }

        & git $pushArgs
        if ($LASTEXITCODE -ne 0) {
            throw "Сбой отправки полного снимка в приватный репозиторий startup-monitor."
        }

        Write-Host "[УСПЕХ] Приватный репозиторий startup-monitor/main обновлен со всеми внутренними файлами!" -ForegroundColor Green
    }
    finally {
        Remove-Item Env:\GIT_INDEX_FILE -ErrorAction SilentlyContinue
        if (Test-Path $tempIndexFile) {
            Remove-Item -Force $tempIndexFile -ErrorAction SilentlyContinue
        }
    }
}
else {
    Write-Host "--> [2/2] Пропуск отправки в приватный репозиторий (SkipPrivate)." -ForegroundColor DarkGray
}

Write-Host ""
Write-Host "============================================================" -ForegroundColor Cyan

# Выпуск проходит тем же каноническим маршрутом, после синхронизации обеих исходных веток.
if ($PublishRelease) {
    $publisher = Join-Path $PSScriptRoot 'publish_github_release.py'
    $releaseName = "Startup Monitor v$version — Firewall reliability and security"
    if (-not $SkipPublic) {
        & python $publisher --version $version --repos gms-cod/SM --target-commitish $headCommit --body-file $releaseNotes --release-name $releaseName --create-only
        if ($LASTEXITCODE -ne 0) { throw 'Публикация нового публичного релиза не завершена.' }
    }
    if (-not $SkipPrivate) {
        & python $publisher --version $version --repos gms-cod/Startup-Monitor --target-commitish $privateCommit --body-file $releaseNotes --release-name $releaseName --create-only --skip-verify
        if ($LASTEXITCODE -ne 0) { throw 'Публикация нового приватного релиза не завершена.' }
    }
}

Write-Host "         Синхронизация обоих репозиториев завершена!        " -ForegroundColor Green
Write-Host "============================================================" -ForegroundColor Cyan
