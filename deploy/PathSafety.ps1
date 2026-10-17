Set-StrictMode -Version Latest

function Assert-ActivityMonitorDirectory {
    param([Parameter(Mandatory=$true)][string]$Path, [switch]$PublishOutput)
    if ([string]::IsNullOrWhiteSpace($Path)) { throw 'The target directory is empty.' }
    $full = [IO.Path]::GetFullPath($Path).TrimEnd('\')
    $driveRoot = [IO.Path]::GetPathRoot($full).TrimEnd('\')
    $protected = @($driveRoot, [Environment]::GetFolderPath('Windows'), [Environment]::GetFolderPath('ProgramFiles'),
        [Environment]::GetFolderPath('CommonApplicationData'), [Environment]::GetFolderPath('UserProfile'))
    foreach ($entry in $protected) {
        if ($entry -and $full -eq $entry.TrimEnd('\')) { throw "Refused unsafe target: $full" }
    }
    $windowsRoot = [Environment]::GetFolderPath('Windows').TrimEnd('\')
    if ($full.StartsWith($windowsRoot + '\', [StringComparison]::OrdinalIgnoreCase)) { throw "Refused Windows directory: $full" }
    for ($ancestor = $full; $ancestor; $ancestor = [IO.Path]::GetDirectoryName($ancestor)) {
        if (Test-Path -LiteralPath $ancestor) {
            if ((Get-Item -LiteralPath $ancestor -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) {
                throw "Refused reparse point: $ancestor"
            }
        }
    }
    if (Test-Path -LiteralPath $full) {
        $marker = Join-Path $full '.activitymonitor-directory'
        $expectedDefault = if ($PublishOutput) { [IO.Path]::GetFullPath((Join-Path $PSScriptRoot 'dist')) } else {
            Join-Path ([Environment]::GetFolderPath('ProgramFiles')) 'ActivityMonitor'
        }
        $hasFiles = @(Get-ChildItem -LiteralPath $full -Force | Select-Object -First 1).Count -gt 0
        if ($hasFiles -and !(Test-Path -LiteralPath $marker) -and $full -ne $expectedDefault) {
            throw "Existing nonempty directory is not an ActivityMonitor installation/output: $full"
        }
        if ((Test-Path -LiteralPath $marker) -and (Get-Content -LiteralPath $marker -Raw).Trim() -ne 'ActivityMonitor-v1') {
            throw "Invalid ActivityMonitor directory marker: $full"
        }
    }
    return $full
}

function Stop-ActivityMonitorAgents {
    param([Parameter(Mandatory=$true)][string]$InstallRoot)
    # Use each Agent's own user/session event; never terminate an encoder before finalization.
    $agents = @(Get-Process -Name 'ActivityMonitor.Agent' -ErrorAction SilentlyContinue)
    $signaledAgents = @()
    foreach ($agent in $agents) {
        $processInfo = Get-CimInstance Win32_Process -Filter "ProcessId = $($agent.Id)"
        if (!$processInfo.ExecutablePath) { throw "Cannot inspect Agent $($agent.Id)." }
        if (!$processInfo.ExecutablePath.StartsWith([IO.Path]::GetFullPath($InstallRoot).TrimEnd('\') + '\', [StringComparison]::OrdinalIgnoreCase)) { continue }
        $owner = Invoke-CimMethod -InputObject $processInfo -MethodName GetOwner
        if ($owner.ReturnValue -ne 0) { throw "Cannot identify Agent $($agent.Id)." }
        $eventName = "Global\ActivityMonitor_Agent_Shutdown_$($owner.User)_$($agent.SessionId)"
        $event = $null
        try {
            if (![Threading.EventWaitHandle]::TryOpenExisting($eventName, [ref]$event)) {
                $eventName = "Local\ActivityMonitor_Agent_Shutdown_$($owner.User)_$($agent.SessionId)"
                if (![Threading.EventWaitHandle]::TryOpenExisting($eventName, [ref]$event)) {
                    throw "Cannot signal Agent $($agent.Id). Stop that Agent in its user session before upgrading."
                }
            }
            [void]$event.Set()
            $signaledAgents += $agent
        }
        finally { if ($event) { $event.Dispose() } }
    }
    foreach ($agent in $signaledAgents) {
        if (!$agent.WaitForExit(45000)) { throw "Agent $($agent.Id) did not finalize in time; installation/removal cancelled." }
    }
}
