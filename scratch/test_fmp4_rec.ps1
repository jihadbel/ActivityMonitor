Add-Type -Path "deploy\dist\Agent\ScreenRecorderLib.dll"

$outPath = "C:\Users\Masoud\.gemini\antigravity-ide\brain\83221934-b40d-45cd-b5ba-76bc978c842c\scratch\test_fmp4.mp4"
if (Test-Path $outPath) { Remove-Item $outPath -Force }

$recOpt = New-Object ScreenRecorderLib.RecorderOptions
$recOpt.VideoEncoderOptions = New-Object ScreenRecorderLib.VideoEncoderOptions
$recOpt.VideoEncoderOptions.Framerate = 15
$recOpt.VideoEncoderOptions.Quality = 60
$recOpt.VideoEncoderOptions.IsHardwareEncodingEnabled = $true
$recOpt.VideoEncoderOptions.IsLowLatencyEnabled = $true
$recOpt.VideoEncoderOptions.IsFragmentedMp4Enabled = $true
$recOpt.VideoEncoderOptions.IsMp4FastStartEnabled = $false

$recOpt.SourceOptions = New-Object ScreenRecorderLib.SourceOptions
$disp = [ScreenRecorderLib.DisplayRecordingSource]::MainMonitor
$disp.RecorderApi = [ScreenRecorderLib.RecorderApi]::WindowsGraphicsCapture
$disp.IsCursorCaptureEnabled = $false
$disp.IsBorderRequired = $false
$recOpt.SourceOptions.RecordingSources.Add($disp)

$rec = [ScreenRecorderLib.Recorder]::CreateRecorder($recOpt)
$completed = $false
$rec.add_OnRecordingComplete({
    param($s, $e)
    $global:completed = $true
    Write-Host "Recording completed successfully! File:" $e.FilePath
})
$rec.add_OnRecordingFailed({
    param($s, $e)
    Write-Host "Recording failed: " $e.Error
})

Write-Host "Starting 4-second Fragmented MP4 test recording..."
$rec.Record($outPath)

for ($i = 1; $i -le 4; $i++) {
    Start-Sleep -Seconds 1
    if (Test-Path $outPath) {
        $len = (Get-Item $outPath).Length
        Write-Host "Second ${i}: File size on disk is $len bytes"
    } else {
        Write-Host "Second ${i}: File does not exist yet"
    }
}

Write-Host "Stopping recording..."
$rec.Stop()

$sw = [System.Diagnostics.Stopwatch]::StartNew()
while (-not $global:completed -and $sw.ElapsedMilliseconds -lt 5000) {
    Start-Sleep -Milliseconds 100
}

Write-Host "Final file length:" (Get-Item $outPath).Length "bytes in $($sw.ElapsedMilliseconds) ms"
