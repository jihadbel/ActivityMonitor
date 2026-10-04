using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using ActivityMonitor.Core.Entities;
using ActivityMonitor.Core.Enums;
using ActivityMonitor.Core.Interfaces;
using ActivityMonitor.Core.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ScreenRecorderLib;

namespace ActivityMonitor.Agent.Recording;

public class ScreenRecordingService : IScreenRecordingService, IDisposable
{
    private readonly IRecordingRepository _recordingRepository;
    private readonly IStorageManagementService _storageService;
    private readonly RecordingOptions _options;
    private readonly ILogger<ScreenRecordingService> _logger;

    private readonly SemaphoreSlim _lock = new(1, 1);
    private Recorder? _recorder;
    private RecordingSegment? _currentSegment;
    private System.Threading.Timer? _segmentTimer;
    private TaskCompletionSource<bool>? _stopTcs;
    private bool _isDisposed;

    public bool IsRecording => _recorder != null && _recorder.Status == RecorderStatus.Recording;
    public RecordingSegment? CurrentSegment => _currentSegment;

    public ScreenRecordingService(
        IRecordingRepository recordingRepository,
        IStorageManagementService storageService,
        IOptions<RecordingOptions> options,
        ILogger<ScreenRecordingService> logger)
    {
        _recordingRepository = recordingRepository;
        _storageService = storageService;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<RecordingSegment?> StartRecordingSegmentAsync(CancellationToken ct = default)
    {
        if (!_options.Enabled)
        {
            _logger.LogInformation("Screen recording is disabled in configuration.");
            return null;
        }

        await _lock.WaitAsync(ct);
        try
        {
            if (IsRecording)
            {
                return _currentSegment;
            }

            // Verify disk space safety before starting
            var hasSpace = await _storageService.EnsureDiskSpaceAvailableAsync(ct);
            if (!hasSpace)
            {
                _logger.LogWarning("Insufficient free disk space to start screen recording. Skipping.");
                return null;
            }

            var now = DateTime.UtcNow;
            var machineId = Environment.MachineName;
            var sessionId = GetCurrentSessionId();
            var filePath = ActivityMonitorPaths.GetRecordingFilePath(machineId, now);
            var fileName = Path.GetFileName(filePath);

            var segment = new RecordingSegment
            {
                MachineId = machineId,
                SessionId = sessionId,
                StartedAtUtc = now,
                FilePath = filePath,
                FileName = fileName,
                Status = RecordingStatus.InProgress,
                CreatedAtUtc = now
            };

            segment = await _recordingRepository.AddAsync(segment, ct);
            _currentSegment = segment;

            var recOptions = new RecorderOptions
            {
                VideoEncoderOptions = new VideoEncoderOptions
                {
                    Framerate = Math.Clamp(_options.FrameRate, 5, 60),
                    Bitrate = Math.Clamp(_options.VideoBitrateKbps * 1000, 300_000, 10_000_000),
                    Quality = Math.Clamp(_options.VideoQuality, 10, 100),
                    IsHardwareEncodingEnabled = true,
                    IsLowLatencyEnabled = true,
                    IsMp4FastStartEnabled = true
                },
                AudioOptions = new AudioOptions
                {
                    IsAudioEnabled = false // No audio recording per privacy & resource requirements
                },
                MouseOptions = new MouseOptions
                {
                    IsMouseClicksDetected = false
                }
            };

            _recorder = Recorder.CreateRecorder(recOptions);
            _recorder.OnRecordingComplete += OnRecordingComplete;
            _recorder.OnRecordingFailed += OnRecordingFailed;
            _recorder.OnStatusChanged += OnStatusChanged;

            _recorder.Record(filePath);
            _logger.LogInformation("Started screen recording segment {SegmentId} -> {FilePath}", segment.Id, filePath);

            // Arm segmentation timer
            var segmentInterval = TimeSpan.FromMinutes(Math.Max(1, _options.SegmentMinutes));
            _segmentTimer?.Dispose();
            _segmentTimer = new System.Threading.Timer(
                async _ => await OnSegmentTimerTickAsync(),
                null,
                segmentInterval,
                Timeout.InfiniteTimeSpan);

            return segment;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start screen recording segment");
            if (_currentSegment != null)
            {
                _currentSegment.Status = RecordingStatus.Corrupted;
                await _recordingRepository.UpdateAsync(_currentSegment, ct);
                _currentSegment = null;
            }
            return null;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<RecordingSegment?> StopRecordingSegmentAsync(CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            _segmentTimer?.Change(Timeout.Infinite, Timeout.Infinite);

            if (_recorder == null || _currentSegment == null)
            {
                return null;
            }

            _logger.LogInformation("Stopping screen recording segment {SegmentId}...", _currentSegment.Id);

            _stopTcs = new TaskCompletionSource<bool>();
            _recorder.Stop();

            // Wait for completion callback or timeout
            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);
            try
            {
                await _stopTcs.Task.WaitAsync(linked.Token);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Timeout or error while waiting for recording finalization.");
            }

            _recorder.Dispose();
            _recorder = null;

            var finalized = _currentSegment;
            _currentSegment = null;
            return finalized;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<RecordingSegment?> SwitchToNewSegmentAsync(CancellationToken ct = default)
    {
        _logger.LogInformation("Switching to new recording segment per schedule...");
        await StopRecordingSegmentAsync(ct);
        return await StartRecordingSegmentAsync(ct);
    }

    private async Task OnSegmentTimerTickAsync()
    {
        try
        {
            await SwitchToNewSegmentAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error occurred during scheduled recording segmentation.");
        }
    }

    private void OnRecordingComplete(object? sender, RecordingCompleteEventArgs e)
    {
        _logger.LogInformation("Screen recording successfully completed for file: {FilePath}", e.FilePath);

        _ = Task.Run(async () =>
        {
            if (_currentSegment != null)
            {
                var now = DateTime.UtcNow;
                _currentSegment.EndedAtUtc = now;
                _currentSegment.DurationSeconds = Math.Max(1.0, (now - _currentSegment.StartedAtUtc).TotalSeconds);
                _currentSegment.Status = RecordingStatus.Completed;

                if (File.Exists(e.FilePath))
                {
                    _currentSegment.FileSizeBytes = new FileInfo(e.FilePath).Length;
                }

                await _recordingRepository.UpdateAsync(_currentSegment);
            }

            _stopTcs?.TrySetResult(true);
        });
    }

    private void OnRecordingFailed(object? sender, RecordingFailedEventArgs e)
    {
        _logger.LogError("Screen recording failed! Error: {Error} File: {FilePath}", e.Error, e.FilePath);

        _ = Task.Run(async () =>
        {
            if (_currentSegment != null)
            {
                _currentSegment.EndedAtUtc = DateTime.UtcNow;
                _currentSegment.Status = RecordingStatus.Corrupted;
                await _recordingRepository.UpdateAsync(_currentSegment);
            }

            _stopTcs?.TrySetResult(false);
        });
    }

    private void OnStatusChanged(object? sender, RecordingStatusEventArgs e)
    {
        _logger.LogDebug("Recorder status changed: {Status}", e.Status);
    }

    private static int GetCurrentSessionId()
    {
        try
        {
            return Process.GetCurrentProcess().SessionId;
        }
        catch
        {
            return 1;
        }
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;

        _segmentTimer?.Dispose();
        _recorder?.Dispose();
        _lock.Dispose();
        GC.SuppressFinalize(this);
    }
}
