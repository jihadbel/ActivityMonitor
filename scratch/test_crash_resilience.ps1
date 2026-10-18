$killPath = "C:\Users\Masoud\.gemini\antigravity-ide\brain\83221934-b40d-45cd-b5ba-76bc978c842c\scratch\test_killed.mp4"
if (Test-Path $killPath) { Remove-Item $killPath -Force }

# Spawn a separate powershell process that records for 10 seconds, but we kill it after 3 seconds
$script = @"
Add-Type -Path 'deploy\dist\Agent\ScreenRecorderLib.dll'
`$recOpt = New-Object ScreenRecorderLib.RecorderOptions
`$recOpt.VideoEncoderOptions = New-Object ScreenRecorderLib.VideoEncoderOptions
`$recOpt.VideoEncoderOptions.Framerate = 15
`$recOpt.VideoEncoderOptions.Quality = 60
`$recOpt.VideoEncoderOptions.IsHardwareEncodingEnabled = `$true
`$recOpt.VideoEncoderOptions.IsLowLatencyEnabled = `$true
`$recOpt.VideoEncoderOptions.IsFragmentedMp4Enabled = `$true
`$recOpt.VideoEncoderOptions.IsMp4FastStartEnabled = `$false

`$recOpt.SourceOptions = New-Object ScreenRecorderLib.SourceOptions
`$disp = [ScreenRecorderLib.DisplayRecordingSource]::MainMonitor
`$disp.RecorderApi = [ScreenRecorderLib.RecorderApi]::WindowsGraphicsCapture
`$disp.IsBorderRequired = `$false
`$recOpt.SourceOptions.RecordingSources.Add(`$disp)

`$rec = [ScreenRecorderLib.Recorder]::CreateRecorder(`$recOpt)
`$rec.Record('$killPath')
Start-Sleep -Seconds 10
"@

$psi = New-Object System.Diagnostics.ProcessStartInfo
$psi.FileName = "powershell.exe"
$psi.Arguments = "-Command `"$script`""
$p = [System.Diagnostics.Process]::Start($psi)

Start-Sleep -Seconds 3
Write-Host "Simulating sudden power outage / crash: Killing recorder process PID $($p.Id)..."
$p.Kill()
$p.WaitForExit()

Write-Host "Process terminated abruptly."
$file = Get-Item $killPath
Write-Host "File size on disk after crash: $($file.Length) bytes"

# Verify media can be opened by Windows Shell / Media Foundation
$shell = New-Object -ComObject Shell.Application
$folder = $shell.Namespace((Split-Path $killPath))
$item = $folder.ParseName((Split-Path $killPath -Leaf))
$duration = $folder.GetDetailsOf($item, 27) # 27 is Duration in Windows Shell
Write-Host "Windows Shell Duration metadata: $duration"
