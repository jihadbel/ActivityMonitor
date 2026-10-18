$psi = New-Object System.Diagnostics.ProcessStartInfo
$psi.FileName = (Resolve-Path "deploy\dist\Viewer\ActivityMonitor.Viewer.exe").Path
$psi.RedirectStandardError = $true
$psi.RedirectStandardOutput = $true
$psi.UseShellExecute = $false
$p = [System.Diagnostics.Process]::Start($psi)
$exited = $p.WaitForExit(15000)
Write-Host "Exited within 15s: $exited"
if ($exited) {
    Write-Host "ExitCode: $($p.ExitCode)"
    Write-Host "StdErr: $($p.StandardError.ReadToEnd())"
    Write-Host "StdOut: $($p.StandardOutput.ReadToEnd())"
} else {
    Write-Host "Still running! PID: $($p.Id)"
}
