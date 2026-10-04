# Publish.ps1 - Builds and publishes ActivityMonitor Agent and Service
param(
    [string]$Configuration = "Release",
    [string]$OutputDir = "$PSScriptRoot\dist"
)

$ErrorActionPreference = "Stop"

Write-Host "=========================================" -ForegroundColor Cyan
Write-Host "   ActivityMonitor - Publish Script       " -ForegroundColor Cyan
Write-Host "=========================================" -ForegroundColor Cyan

$WorkspaceRoot = Resolve-Path "$PSScriptRoot\.."

Write-Host "Publishing Service..." -ForegroundColor Yellow
dotnet publish "$WorkspaceRoot\ActivityMonitor.Service\ActivityMonitor.Service.csproj" `
    -c $Configuration `
    -o "$OutputDir\Service"

Write-Host "Publishing Agent..." -ForegroundColor Yellow
dotnet publish "$WorkspaceRoot\ActivityMonitor.Agent\ActivityMonitor.Agent.csproj" `
    -c $Configuration `
    -o "$OutputDir\Agent"

Write-Host "Publishing Viewer..." -ForegroundColor Yellow
dotnet publish "$WorkspaceRoot\ActivityMonitor.Viewer\ActivityMonitor.Viewer.csproj" `
    -c $Configuration `
    -o "$OutputDir\Viewer"

# Copy installer scripts to dist for easy distribution
Copy-Item "$PSScriptRoot\Install.ps1" -Destination "$OutputDir\"
Copy-Item "$PSScriptRoot\Uninstall.ps1" -Destination "$OutputDir\"

Write-Host "Successfully published to: $OutputDir" -ForegroundColor Green
