# Install.ps1 - Automated Installer for ActivityMonitor Windows Service and Agent
# Requires Administrator privileges
param(
    [string]$InstallDir = "C:\Program Files\ActivityMonitor",
    [switch]$StartImmediately = $true
)

$ErrorActionPreference = "Stop"

# Verify Administrator elevation
$isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isAdmin) {
    Write-Error "This installation script must be executed with Administrator privileges. Please run PowerShell as Administrator."
    exit 1
}

Write-Host "==================================================" -ForegroundColor Cyan
Write-Host "      ActivityMonitor Windows Installer           " -ForegroundColor Cyan
Write-Host "==================================================" -ForegroundColor Cyan

# 1. Create Target Directories
Write-Host "[1/7] Creating installation directories..." -ForegroundColor Yellow
$ServiceDir = Join-Path $InstallDir "Service"
$AgentDir = Join-Path $InstallDir "Agent"
$ProgramDataRoot = "C:\ProgramData\ActivityMonitor"
$DataDir = Join-Path $ProgramDataRoot "Data"
$RecordingsDir = Join-Path $ProgramDataRoot "Recordings"
$ReportsDir = Join-Path $ProgramDataRoot "Reports"
$LogsDir = Join-Path $ProgramDataRoot "Logs"

New-Item -ItemType Directory -Force -Path $ServiceDir | Out-Null
New-Item -ItemType Directory -Force -Path $AgentDir | Out-Null
New-Item -ItemType Directory -Force -Path $DataDir | Out-Null
New-Item -ItemType Directory -Force -Path $RecordingsDir | Out-Null
New-Item -ItemType Directory -Force -Path $ReportsDir | Out-Null
New-Item -ItemType Directory -Force -Path $LogsDir | Out-Null

# 2. Configure NTFS Permissions on ProgramData
Write-Host "[2/7] Configuring ACL permissions on ProgramData..." -ForegroundColor Yellow
# Grant BUILTIN\Users full modify permissions to ensure interactive Agent can write DB/Recordings
icacls "$ProgramDataRoot" /grant "Users:(OI)(CI)M" /T /Q | Out-Null
icacls "$ProgramDataRoot" /grant "SYSTEM:(OI)(CI)F" /T /Q | Out-Null
icacls "$ProgramDataRoot" /grant "Administrators:(OI)(CI)F" /T /Q | Out-Null

# 3. Stop existing services/processes if running
Write-Host "[3/7] Stopping any existing instances..." -ForegroundColor Yellow
sc.exe query ActivityMonitor.Service 2>$null | Out-Null
if ($LASTEXITCODE -eq 0) {
    sc.exe stop ActivityMonitor.Service 2>$null | Out-Null
    Start-Sleep -Seconds 2
}
Get-Process -Name "ActivityMonitor.Agent" -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue

# 4. Copy Binaries
Write-Host "[4/7] Copying binaries..." -ForegroundColor Yellow
$SourceService = Join-Path $PSScriptRoot "Service"
$SourceAgent = Join-Path $PSScriptRoot "Agent"

if (-not (Test-Path $SourceService) -or -not (Test-Path $SourceAgent)) {
    # If run from deploy directory before publishing
    $DistService = Join-Path $PSScriptRoot "dist\Service"
    $DistAgent = Join-Path $PSScriptRoot "dist\Agent"
    if (Test-Path $DistService) {
        $SourceService = $DistService
        $SourceAgent = $DistAgent
    }
}

Copy-Item -Path "$SourceService\*" -Destination $ServiceDir -Recurse -Force
Copy-Item -Path "$SourceAgent\*" -Destination $AgentDir -Recurse -Force

# 5. Register and Configure Windows Service
Write-Host "[5/7] Registering Windows Service..." -ForegroundColor Yellow
$ServiceExe = Join-Path $ServiceDir "ActivityMonitor.Service.exe"

sc.exe query ActivityMonitor.Service 2>$null | Out-Null
if ($LASTEXITCODE -ne 0) {
    sc.exe create ActivityMonitor.Service `
        binPath= "`"$ServiceExe`"" `
        start= auto `
        DisplayName= "ActivityMonitor Background Service"
} else {
    sc.exe config ActivityMonitor.Service `
        binPath= "`"$ServiceExe`"" `
        start= auto `
        DisplayName= "ActivityMonitor Background Service"
}

sc.exe description ActivityMonitor.Service "Background maintenance, retention enforcement, and daily reporting for ActivityMonitor."
sc.exe failure ActivityMonitor.Service reset= 86400 actions= restart/60000/restart/60000/restart/60000

# 6. Configure Agent Autostart at User Logon
Write-Host "[6/7] Configuring Agent startup task on user logon..." -ForegroundColor Yellow
$AgentExe = Join-Path $AgentDir "ActivityMonitor.Agent.exe"
$TaskName = "ActivityMonitorAgent"

# Create a Scheduled Task to launch Agent silently with highest privileges at any user logon
schtasks.exe /Create /TN $TaskName /TR "`"$AgentExe`" --silent" /SC ONLOGON /RL HIGHEST /F | Out-Null

# 7. Start Windows Service and Agent
Write-Host "[7/7] Starting services..." -ForegroundColor Yellow
sc.exe start ActivityMonitor.Service | Out-Null

if ($StartImmediately) {
    Write-Host "Launching ActivityMonitor Agent in user session..." -ForegroundColor Cyan
    Start-Process -FilePath $AgentExe -ArgumentList "--silent"
}

Write-Host "==================================================" -ForegroundColor Green
Write-Host "   ActivityMonitor installed and running successfully! " -ForegroundColor Green
Write-Host "   Install Location: $InstallDir" -ForegroundColor Green
Write-Host "   Data Location:    $ProgramDataRoot" -ForegroundColor Green
Write-Host "==================================================" -ForegroundColor Green
