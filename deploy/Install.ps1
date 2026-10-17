param(
    [string]$InstallDir = (Join-Path ([Environment]::GetFolderPath('ProgramFiles')) 'ActivityMonitor'),
    [switch]$StartImmediately = $false,
    [switch]$CreateShortcuts = $true
)
$ErrorActionPreference = 'Stop'
. "$PSScriptRoot\PathSafety.ps1"
$principal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
if (!$principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) { throw 'Run the installer as Administrator.' }
$target = Assert-ActivityMonitorDirectory -Path $InstallDir
$source = $PSScriptRoot
if (!(Test-Path -LiteralPath (Join-Path $source 'Agent'))) { $source = Join-Path $PSScriptRoot 'dist' }
foreach ($part in @('Agent','Service','Viewer')) {
    if (!(Test-Path -LiteralPath (Join-Path $source "$part\ActivityMonitor.$part.exe"))) { throw "Missing published $part. Run Publish.ps1 first." }
}
# Verify package hashes before stopping or modifying an existing installation.
$manifestPath = Join-Path $source 'manifest.json'
if (!(Test-Path -LiteralPath $manifestPath)) { throw 'The package has no manifest. Re-publish it before installation.' }
$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
if ($manifest.Product -ne 'ActivityMonitor') { throw 'Invalid package product.' }
foreach ($entry in $manifest.Files) {
    $file = [IO.Path]::GetFullPath((Join-Path $source $entry.Path))
    if (!$file.StartsWith([IO.Path]::GetFullPath($source).TrimEnd('\') + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Unsafe package entry.' }
    if (!(Test-Path -LiteralPath $file) -or (Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash -ne $entry.SHA256) { throw "Package verification failed: $($entry.Path)" }
}
$service = Get-Service -Name 'ActivityMonitor.Service' -ErrorAction SilentlyContinue
$wasRunning = $service -and $service.Status -eq 'Running'
if ($wasRunning) { Stop-Service -Name $service.Name; $service.WaitForStatus('Stopped', (New-TimeSpan -Seconds 45)) }
Stop-ActivityMonitorAgents -InstallRoot $target
Get-Process -Name 'ActivityMonitor.Viewer' -ErrorAction SilentlyContinue |
    Where-Object { $_.Path.StartsWith($target.TrimEnd('\') + '\', [StringComparison]::OrdinalIgnoreCase) } | Stop-Process -ErrorAction Stop
$staging = $target + '.install-' + [Guid]::NewGuid().ToString('N')
$backup = $target + '.previous-' + [Guid]::NewGuid().ToString('N')
$swapped = $false
try {
    New-Item -ItemType Directory -Path $staging | Out-Null
    foreach ($part in @('Agent','Service','Viewer')) { Copy-Item -LiteralPath (Join-Path $source $part) -Destination $staging -Recurse }
    Set-Content -LiteralPath (Join-Path $staging '.activitymonitor-directory') -Value 'ActivityMonitor-v1'
    if (Test-Path -LiteralPath $target) { Move-Item -LiteralPath $target -Destination $backup }
    Move-Item -LiteralPath $staging -Destination $target
    $swapped = $true
    $shared = Join-Path ([Environment]::GetFolderPath('CommonApplicationData')) 'ActivityMonitor'
    New-Item -ItemType Directory -Path $shared -Force | Out-Null
    for ($ancestor = $shared; $ancestor; $ancestor = [IO.Path]::GetDirectoryName($ancestor)) {
        if ((Get-Item -LiteralPath $ancestor -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) { throw "Unsafe archive ancestor: $ancestor" }
    }
    $acl = New-Object Security.AccessControl.DirectorySecurity
    $acl.SetAccessRuleProtection($true, $false)
    $archiveOwner = New-Object Security.Principal.SecurityIdentifier('S-1-5-32-544')
    $acl.SetOwner($archiveOwner)
    foreach ($sid in @('S-1-5-18','S-1-5-32-544')) {
        $rule = New-Object Security.AccessControl.FileSystemAccessRule((New-Object Security.Principal.SecurityIdentifier($sid)), 'FullControl', 'ContainerInherit,ObjectInherit', 'None', 'Allow')
        $acl.AddAccessRule($rule)
    }
    # Users can traverse only the root to read administrator defaults; no inheritance into historical data.
    $acl.AddAccessRule((New-Object Security.AccessControl.FileSystemAccessRule((New-Object Security.Principal.SecurityIdentifier('S-1-5-32-545')), 'ReadAndExecute', 'Allow')))
    Set-Acl -LiteralPath $shared -AclObject $acl
    foreach ($item in Get-ChildItem -LiteralPath $shared -Recurse -Force) {
        if ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw "Unsafe archive entry: $($item.FullName)" }
        $childAcl = Get-Acl -LiteralPath $item.FullName
        foreach ($rule in @($childAcl.Access | Where-Object { !$_.IsInherited })) { [void]$childAcl.RemoveAccessRuleSpecific($rule) }
        $childAcl.SetAccessRuleProtection($false, $false)
        $childAcl.SetOwner($archiveOwner)
        Set-Acl -LiteralPath $item.FullName -AclObject $childAcl
    }
    $config = Join-Path $shared 'appsettings.json'
    if (!(Test-Path -LiteralPath $config)) { Copy-Item -LiteralPath (Join-Path $target 'Agent\appsettings.json') -Destination $config }
    $configAcl = Get-Acl -LiteralPath $config
    $configAcl.AddAccessRule((New-Object Security.AccessControl.FileSystemAccessRule((New-Object Security.Principal.SecurityIdentifier('S-1-5-32-545')), 'Read', 'Allow')))
    Set-Acl -LiteralPath $config -AclObject $configAcl
    $exe = Join-Path $target 'Service\ActivityMonitor.Service.exe'
    if (!$service) { New-Service -Name 'ActivityMonitor.Service' -BinaryPathName ('"' + $exe + '"') -DisplayName 'ActivityMonitor Archive Maintenance' -StartupType Automatic | Out-Null }
    & sc.exe config 'ActivityMonitor.Service' 'binPath=' ('"' + $exe + '"') 'start=' 'auto'
    if ($LASTEXITCODE -ne 0) { throw 'Failed to configure service.' }
    # Preserve literal quotes on Windows PowerShell 5.1 as well as PowerShell 7.
    Set-ItemProperty -LiteralPath 'HKLM:\SYSTEM\CurrentControlSet\Services\ActivityMonitor.Service' -Name 'ImagePath' -Value ('"' + $exe + '"')
    & sc.exe failure 'ActivityMonitor.Service' 'reset=' '86400' 'actions=' 'restart/60000/restart/60000/restart/60000'
    if ($LASTEXITCODE -ne 0) { throw 'Failed to configure service recovery.' }
    # Replace legacy scheduled tasks with a single, ordinary interactive Run registration.
    if (Get-ScheduledTask -TaskName 'ActivityMonitorAgent' -ErrorAction SilentlyContinue) { Unregister-ScheduledTask -TaskName 'ActivityMonitorAgent' -Confirm:$false }
    $run = 'HKLM:\Software\Microsoft\Windows\CurrentVersion\Run'
    Set-ItemProperty -LiteralPath $run -Name 'ActivityMonitorAgent' -Value ('"' + (Join-Path $target 'Agent\ActivityMonitor.Agent.exe') + '" --silent --autostart')
    if ($CreateShortcuts) {
        $shell = New-Object -ComObject WScript.Shell
        $programs = Join-Path ([Environment]::GetFolderPath('CommonPrograms')) 'ActivityMonitor'
        New-Item -ItemType Directory -Path $programs -Force | Out-Null
        $link = $shell.CreateShortcut((Join-Path $programs 'ActivityMonitor Viewer.lnk'))
        $link.TargetPath = Join-Path $target 'Viewer\ActivityMonitor.Viewer.exe'
        $link.WorkingDirectory = Join-Path $target 'Viewer'
        $link.Save()
    }
    Start-Service -Name 'ActivityMonitor.Service'
    (Get-Service -Name 'ActivityMonitor.Service').WaitForStatus('Running', (New-TimeSpan -Seconds 30))
    if ($StartImmediately) {
        # Delegate to the interactive Explorer shell instead of inheriting the installer's elevated token.
        $shellApplication = New-Object -ComObject Shell.Application
        $shellApplication.ShellExecute((Join-Path $target 'Agent\ActivityMonitor.Agent.exe'), '--silent', (Join-Path $target 'Agent'), 'open', 0)
    }
    Write-Host "Installed: $target. Agent starts in each user's normal session at logon. Data: %LOCALAPPDATA%\ActivityMonitor" -ForegroundColor Green
    if (Test-Path -LiteralPath $backup) { Remove-Item -LiteralPath $backup -Recurse -Force }
}
catch {
    $failure = $_
    if ($swapped -or (Test-Path -LiteralPath $backup)) {
        Stop-Service -Name 'ActivityMonitor.Service' -ErrorAction SilentlyContinue
        # Both paths were generated from the verified application directory above.
        if (Test-Path -LiteralPath $target) { Remove-Item -LiteralPath $target -Recurse -Force }
        if (Test-Path -LiteralPath $backup) { Move-Item -LiteralPath $backup -Destination $target }
        if ($wasRunning) { Start-Service -Name 'ActivityMonitor.Service' -ErrorAction SilentlyContinue }
    }
    throw $failure
}
finally { if (Test-Path -LiteralPath $staging) { Remove-Item -LiteralPath $staging -Recurse -Force } }
