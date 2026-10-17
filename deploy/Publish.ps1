param(
    [string]$Configuration = "Release",
    [ValidateSet('win-x64')][string]$Runtime = "win-x64",
    [bool]$SelfContained = $true,
    [string]$OutputDir = "$PSScriptRoot\dist",
    [string]$ArchivePath = (Join-Path $PSScriptRoot '..\deploy.zip')
)
$ErrorActionPreference = 'Stop'
. "$PSScriptRoot\PathSafety.ps1"
$workspace = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$output = Assert-ActivityMonitorDirectory -Path $OutputDir -PublishOutput
$staging = $output + '.staging-' + [Guid]::NewGuid().ToString('N')
$zipTemp = Join-Path $workspace ('deploy-' + [Guid]::NewGuid().ToString('N') + '.zip')
$previous = $output + '.previous-' + [Guid]::NewGuid().ToString('N')
New-Item -ItemType Directory -Path $staging | Out-Null
Set-Content -LiteralPath (Join-Path $staging '.activitymonitor-directory') -Value 'ActivityMonitor-v1'
try {
    foreach ($project in @('Service', 'Agent', 'Viewer')) {
        $projectPath = Join-Path $workspace "ActivityMonitor.$project\ActivityMonitor.$project.csproj"
        & dotnet publish $projectPath -c $Configuration -r $Runtime --self-contained $SelfContained.ToString().ToLowerInvariant() -o (Join-Path $staging $project)
        if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed for $project (exit $LASTEXITCODE). Existing output was preserved." }
        if (!(Test-Path -LiteralPath (Join-Path $staging "$project\ActivityMonitor.$project.exe"))) { throw "Missing $project executable." }
        if (!(Test-Path -LiteralPath (Join-Path $staging "$project\e_sqlite3.dll"))) { throw "Missing SQLite native runtime for $project." }
    }
    if (!(Test-Path -LiteralPath (Join-Path $staging 'Agent\ScreenRecorderLib.dll'))) { throw 'Missing x64 screen recording library.' }
    foreach ($file in @('Install.ps1','Uninstall.ps1','Install.bat','Uninstall.bat','PathSafety.ps1','README_INSTALL.txt')) {
        Copy-Item -LiteralPath (Join-Path $PSScriptRoot $file) -Destination $staging
    }
    $files = @(Get-ChildItem -LiteralPath $staging -Recurse -File | ForEach-Object {
        [pscustomobject]@{ Path=$_.FullName.Substring($staging.Length + 1); SHA256=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash }
    })
    [pscustomobject]@{ Product='ActivityMonitor'; CreatedUtc=[DateTime]::UtcNow.ToString('o'); Runtime=$Runtime; Files=$files } |
        ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $staging 'manifest.json') -Encoding UTF8
    # Include the hidden directory marker as well.
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    [IO.Compression.ZipFile]::CreateFromDirectory($staging, $zipTemp)
    if (Test-Path -LiteralPath $output) { Move-Item -LiteralPath $output -Destination $previous }
    try {
        Move-Item -LiteralPath $staging -Destination $output
        Move-Item -LiteralPath $zipTemp -Destination ([IO.Path]::GetFullPath($ArchivePath)) -Force
    }
    catch {
        if (Test-Path -LiteralPath $previous) {
            if (Test-Path -LiteralPath $output) { Remove-Item -LiteralPath $output -Recurse -Force }
            Move-Item -LiteralPath $previous -Destination $output
        }
        throw
    }
    if (Test-Path -LiteralPath $previous) {
        # This exact path is the verified old output moved above, not a caller-supplied deletion target.
        Remove-Item -LiteralPath $previous -Recurse -Force
    }
    Write-Host "Published and verified: $output" -ForegroundColor Green
}
finally {
    if (Test-Path -LiteralPath $staging) { Remove-Item -LiteralPath $staging -Recurse -Force }
    if (Test-Path -LiteralPath $zipTemp) { Remove-Item -LiteralPath $zipTemp -Force }
}
