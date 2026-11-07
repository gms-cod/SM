<#
.SYNOPSIS
Проверяет настоящую замену и откат артефактов в изолированном TestDrive без обращения к SCM.+#>
Import-Module (Join-Path $PSScriptRoot '..\scripts\Deployment.psm1') -Force

Describe 'StartupMonitor deployment transaction' {
    BeforeEach {
        $caseDirectory = Join-Path $TestDrive ([Guid]::NewGuid().ToString('N'))
        $source = Join-Path $caseDirectory 'source'
        $first = Join-Path $caseDirectory 'portable'
        $second = Join-Path $caseDirectory 'installed'
        $backup = Join-Path $caseDirectory 'backup'
        $names = @('StartupMonitor.Service.exe', 'StartupMonitor.UI.exe', 'app.ico', 'icon.svg')
        foreach ($directory in @($source, $first, $second)) {
            New-Item -ItemType Directory -Path $directory -Force | Out-Null
            foreach ($name in $names) {
                $content = if ($directory -eq $source) { 'new' } else { 'old' }
                [IO.File]::WriteAllText((Join-Path $directory $name), $content)
            }
            [IO.File]::WriteAllText((Join-Path $directory 'settings.json'), 'user-config')
        }
        $events = [Collections.Generic.List[string]]::new()
        $stop = { $events.Add('stop') }.GetNewClosure()
        $start = { $events.Add('start') }.GetNewClosure()
        $restore = { $events.Add('restore') }.GetNewClosure()
    }

    It 'updates both copies while preserving user configuration' {
        Sync-StartupMonitorFiles $source @($first, $second) $backup $stop $start $restore
        foreach ($directory in @($first, $second)) {
            foreach ($name in $names) { [IO.File]::ReadAllText((Join-Path $directory $name)) | Should Be 'new' }
            [IO.File]::ReadAllText((Join-Path $directory 'settings.json')) | Should Be 'user-config'
        }
        ($events -join ',') | Should Be 'stop,start'
        [IO.File]::ReadAllText((Join-Path $backup '0\StartupMonitor.Service.exe')) | Should Be 'old'
    }

    It 'restores files and prior service state when startup fails' {
        $failureEvents = $events
        $failStart = { $failureEvents.Add('start'); throw 'SCM refused startup' }.GetNewClosure()
        try { Sync-StartupMonitorFiles $source @($first, $second) $backup $stop $failStart $restore; throw 'Expected startup failure' }
        catch { $_.Exception.Message | Should Be 'SCM refused startup' }
        foreach ($directory in @($first, $second)) {
            foreach ($name in $names) { [IO.File]::ReadAllText((Join-Path $directory $name)) | Should Be 'old' }
            [IO.File]::ReadAllText((Join-Path $directory 'settings.json')) | Should Be 'user-config'
        }
        ($events -join ',') | Should Be 'stop,start,stop,restore'
    }

    It 'rolls back an actual locked-file copy failure before restoring service state' {
        $state = @{ Attempts = 0; Lock = $null }
        $lockedPath = Join-Path $second 'StartupMonitor.UI.exe'
        $lockEvents = $events
        $stopWithLock = {
            $lockEvents.Add('stop')
            $state.Attempts++
            if ($state.Attempts -eq 1) {
                $state.Lock = [IO.File]::Open($lockedPath, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::Read)
            }
            elseif ($state.Lock) { $state.Lock.Dispose(); $state.Lock = $null }
        }.GetNewClosure()
        try {
            try { Sync-StartupMonitorFiles $source @($first, $second) $backup $stopWithLock $start $restore; throw 'Expected file copy failure' }
            catch { $_.Exception.Message | Should Not Be 'Expected file copy failure' }
            foreach ($directory in @($first, $second)) {
                foreach ($name in $names) { [IO.File]::ReadAllText((Join-Path $directory $name)) | Should Be 'old' }
            }
            ($events -join ',') | Should Be 'stop,stop,restore'
        }
        finally { if ($state.Lock) { $state.Lock.Dispose() } }
    }

    It 'does not stop the application when source validation fails' {
        # Удаляется один известный артефакт только внутри автоматически изолированного TestDrive.
        Remove-Item -LiteralPath (Join-Path $source 'app.ico')
        { Sync-StartupMonitorFiles $source @($first, $second) $backup $stop $start $restore } | Should Throw
        $events.Count | Should Be 0
        [IO.File]::ReadAllText((Join-Path $first 'StartupMonitor.Service.exe')) | Should Be 'old'
    }

    It 'does not restart a mixed assembly when rollback cannot restore a locked executable' {
        $failureEvents = $events
        $lockState = @{ Stream = $null }
        $servicePath = Join-Path $second 'StartupMonitor.Service.exe'
        $failAndLock = {
            $failureEvents.Add('start')
            $lockState.Stream = [IO.File]::Open($servicePath, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::Read)
            throw 'SCM failed with locked executable'
        }.GetNewClosure()
        try {
            { Sync-StartupMonitorFiles $source @($first, $second) $backup $stop $failAndLock $restore } | Should Throw
            ($events -join ',') | Should Be 'stop,start,stop'
            Test-Path -LiteralPath (Join-Path $backup '1\StartupMonitor.Service.exe') | Should Be $true
        }
        finally { if ($lockState.Stream) { $lockState.Stream.Dispose() } }
    }
}
