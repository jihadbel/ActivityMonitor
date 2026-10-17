param(
    [string]$InstallDir = (Join-Path ([Environment]::GetFolderPath('ProgramFiles')) 'ActivityMonitor'),
    [switch]$PurgeData = $false
)
$ErrorActionPreference = 'Stop'
. "$PSScriptRoot\PathSafety.ps1"
$principal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
if (!$principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) { throw 'Run the uninstaller as Administrator.' }
$target = Assert-ActivityMonitorDirectory -Path $InstallDir
$service = Get-Service -Name 'ActivityMonitor.Service' -ErrorAction SilentlyContinue
if ($service -and $service.Status -ne 'Stopped') { Stop-Service -Name $service.Name; $service.WaitForStatus('Stopped', (New-TimeSpan -Seconds 45)) }
Stop-ActivityMonitorAgents -InstallRoot $target
Get-Process -Name 'ActivityMonitor.Viewer' -ErrorAction SilentlyContinue |
    Where-Object { $_.Path.StartsWith($target.TrimEnd('\') + '\', [StringComparison]::OrdinalIgnoreCase) } | Stop-Process -ErrorAction Stop
if ($service) { & sc.exe delete 'ActivityMonitor.Service'; if ($LASTEXITCODE -ne 0) { throw 'Failed to remove service.' } }
if (Get-ScheduledTask -TaskName 'ActivityMonitorAgent' -ErrorAction SilentlyContinue) { Unregister-ScheduledTask -TaskName 'ActivityMonitorAgent' -Confirm:$false }
foreach ($run in @('HKLM:\Software\Microsoft\Windows\CurrentVersion\Run','HKCU:\Software\Microsoft\Windows\CurrentVersion\Run')) {
    if (Get-ItemProperty -LiteralPath $run -Name 'ActivityMonitorAgent' -ErrorAction SilentlyContinue) { Remove-ItemProperty -LiteralPath $run -Name 'ActivityMonitorAgent' }
}
if (Test-Path -LiteralPath $target) { Remove-Item -LiteralPath $target -Recurse -Force }
$shortcut = Join-Path ([Environment]::GetFolderPath('CommonPrograms')) 'ActivityMonitor\ActivityMonitor Viewer.lnk'
if (Test-Path -LiteralPath $shortcut) { Remove-Item -LiteralPath $shortcut -Force }
if ($PurgeData) {
    $shared = Join-Path ([Environment]::GetFolderPath('CommonApplicationData')) 'ActivityMonitor'
    $private = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'ActivityMonitor'
    foreach ($data in @($shared,$private)) {
        $full = [IO.Path]::GetFullPath($data)
        if ([IO.Path]::GetFileName($full) -ne 'ActivityMonitor') { throw "Unsafe data target: $full" }
        for ($ancestor=$full; $ancestor; $ancestor=[IO.Path]::GetDirectoryName($ancestor)) {
            if ((Test-Path -LiteralPath $ancestor) -and ((Get-Item -LiteralPath $ancestor -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw "Unsafe data ancestor: $ancestor" }
        }
        if (Test-Path -LiteralPath $full) { Remove-Item -LiteralPath $full -Recurse -Force }
    }
}
Write-Host 'Uninstalled. Data is preserved unless -PurgeData was specified. Other users private stores are always preserved.' -ForegroundColor Green
