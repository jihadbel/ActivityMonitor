# Uninstall.ps1 - Uninstaller for ActivityMonitor Windows Service and Agent
# Requires Administrator privileges
param(
    [string]$InstallDir = "C:\Program Files\ActivityMonitor",
    [switch]$PurgeData = $false
)

$ErrorActionPreference = "Continue"

$isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isAdmin) {
    Write-Error "This uninstaller must be executed with Administrator privileges. Please run PowerShell as Administrator."
    exit 1
}

Write-Host "==================================================" -ForegroundColor Cyan
Write-Host "     ActivityMonitor Windows Uninstaller          " -ForegroundColor Cyan
Write-Host "==================================================" -ForegroundColor Cyan

# 1. Stop and remove Windows Service
Write-Host "[1/5] Stopping and removing Windows Service..." -ForegroundColor Yellow
sc.exe stop ActivityMonitor.Service 2>$null | Out-Null
Start-Sleep -Seconds 2
sc.exe delete ActivityMonitor.Service 2>$null | Out-Null

# 2. Stop running Agent processes
Write-Host "[2/5] Terminating Agent instances..." -ForegroundColor Yellow
Get-Process -Name "ActivityMonitor.Agent" -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue

# 3. Remove Scheduled Task and Registry Run keys
Write-Host "[3/5] Removing startup registrations..." -ForegroundColor Yellow
schtasks.exe /Delete /TN "ActivityMonitorAgent" /F 2>$null | Out-Null

try {
    $regKey = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Run"
    if (Get-ItemProperty -Path $regKey -Name "ActivityMonitorAgent" -ErrorAction SilentlyContinue) {
        Remove-ItemProperty -Path $regKey -Name "ActivityMonitorAgent" -Force -ErrorAction SilentlyContinue
    }
} catch { }

# 4. Remove Binary Files
Write-Host "[4/5] Removing installed binaries..." -ForegroundColor Yellow
if (Test-Path $InstallDir) {
    Remove-Item -Path $InstallDir -Recurse -Force -ErrorAction SilentlyContinue
}

# 5. Handle ProgramData according to policy
$ProgramDataRoot = "C:\ProgramData\ActivityMonitor"
Write-Host "[5/5] Checking data retention policy..." -ForegroundColor Yellow
if ($PurgeData) {
    Write-Host "PurgeData switch was specified. Deleting all recordings and databases in $ProgramDataRoot..." -ForegroundColor Red
    if (Test-Path $ProgramDataRoot) {
        Remove-Item -Path $ProgramDataRoot -Recurse -Force -ErrorAction SilentlyContinue
    }
    Write-Host "All data removed." -ForegroundColor Green
} else {
    Write-Host "Data preserved safely at: $ProgramDataRoot" -ForegroundColor Cyan
    Write-Host "Note: Database and recordings were NOT deleted. To permanently erase, re-run with -PurgeData flag." -ForegroundColor Cyan
}

Write-Host "==================================================" -ForegroundColor Green
Write-Host "       Uninstall completed successfully!          " -ForegroundColor Green
Write-Host "==================================================" -ForegroundColor Green
