using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using ActivityMonitor.Agent.Interop;
using ActivityMonitor.Core.Entities;
using ActivityMonitor.Core.Enums;
using ActivityMonitor.Core.Interfaces;
using ActivityMonitor.Core.Options;
using ActivityMonitor.Infrastructure.ContextProviders;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Win32;
using ActivityEvent = ActivityMonitor.Core.Entities.ActivityEvent;
using ActivityTrackingOptions = ActivityMonitor.Core.Options.ActivityTrackingOptions;

namespace ActivityMonitor.Agent.Tracking;

public class ActivityTracker : IActivityTracker, IDisposable
{
    private readonly IActivityRepository _activityRepository;
    private readonly IScreenRecordingService _recordingService;
    private readonly ActivityContextProviderComposite _contextProvider;
    private readonly ActivityTrackingOptions _options;
    private readonly ILogger<ActivityTracker> _logger;

    private readonly SemaphoreSlim _syncLock = new(1, 1);
    private CancellationTokenSource? _cts;
    private Thread? _monitorThread;
    private readonly AutoResetEvent _wakeUpEvent = new(false);

    private ActivityEvent? _currentActivity;
    private ActivityEvent? _currentIdleActivity;
    private bool _isIdle;
    private bool _isLocked;
    private DateTime _lastFlushTime = DateTime.UtcNow;

    public event Func<ActivityEvent, Task>? ActivityStarted;
    public event Func<ActivityEvent, Task>? ActivityEnded;

    public ActivityTracker(
        IActivityRepository activityRepository,
        IScreenRecordingService recordingService,
        ActivityContextProviderComposite contextProvider,
        IOptions<ActivityTrackingOptions> options,
        ILogger<ActivityTracker> logger)
    {
        _activityRepository = activityRepository;
        _recordingService = recordingService;
        _contextProvider = contextProvider;
        _options = options.Value;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken ct = default)
    {
        _logger.LogInformation("Starting ActivityTracker...");
        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);

        // Register Windows Session and Power events
        SystemEvents.SessionSwitch += OnSessionSwitch;
        SystemEvents.PowerModeChanged += OnPowerModeChanged;
        SystemEvents.SessionEnding += OnSessionEnding;

        // Open interactive desktop on calling thread
        IntPtr hDesk = NativeMethods.OpenDesktop("default", 0, false, NativeMethods.DESKTOP_ALL_ACCESS);
        if (hDesk == IntPtr.Zero)
        {
            hDesk = NativeMethods.OpenInputDesktop(0, false, NativeMethods.DESKTOP_ALL_ACCESS);
        }

        // Dedicated thread running on the interactive default desktop
        _monitorThread = new Thread(() => RunMonitoringThread(hDesk))
        {
            IsBackground = true,
            Name = "ActivityMonitorThread"
        };
        _monitorThread.Start();

        _logger.LogInformation("ActivityTracker started successfully.");
        return Task.CompletedTask;
    }

    private readonly struct WindowSnapshot
    {
        public IntPtr HWnd { get; init; }
        public int ProcessId { get; init; }
        public string WindowTitle { get; init; }
        public TimeSpan IdleTime { get; init; }

        public WindowSnapshot(IntPtr hWnd, int processId, string windowTitle, TimeSpan idleTime)
        {
            HWnd = hWnd;
            ProcessId = processId;
            WindowTitle = windowTitle;
            IdleTime = idleTime;
        }
    }

    private void RunMonitoringThread(IntPtr hDesk)
    {
        try
        {
            if (hDesk != IntPtr.Zero)
            {
                var bound = NativeMethods.SetThreadDesktop(hDesk);
                int err = Marshal.GetLastWin32Error();
                _logger.LogInformation("Attached ActivityTracker thread to desktop: {Bound} (LastError={Err}, TargetHandle={Handle})", bound, err, hDesk);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not attach thread to desktop 'default'");
        }

        var interval = Math.Max(250, _options.PollingIntervalMs);

        while (_cts != null && !_cts.IsCancellationRequested)
        {
            try
            {
                var snapshot = CaptureWindowSnapshotOnThread();
                ProcessSnapshotAsync(snapshot).GetAwaiter().GetResult();

                _wakeUpEvent.WaitOne(interval);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in ActivityTracker loop");
            }
        }

        if (hDesk != IntPtr.Zero)
        {
            NativeMethods.CloseDesktop(hDesk);
        }
    }

    private WindowSnapshot CaptureWindowSnapshotOnThread()
    {
        var idleTime = NativeMethods.GetIdleTime();
        var hWnd = NativeMethods.GetForegroundWindow();
        int processId = 0;
        string windowTitle = string.Empty;

        if (hWnd != IntPtr.Zero)
        {
            NativeMethods.GetWindowThreadProcessId(hWnd, out uint pid);
            processId = (int)pid;
            windowTitle = NativeMethods.GetWindowTitle(hWnd);
        }

        return new WindowSnapshot(hWnd, processId, windowTitle, idleTime);
    }


    public async Task StopAsync(CancellationToken ct = default)
    {
        _logger.LogInformation("Stopping ActivityTracker...");

        SystemEvents.SessionSwitch -= OnSessionSwitch;
        SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        SystemEvents.SessionEnding -= OnSessionEnding;

        _cts?.Cancel();
        _wakeUpEvent.Set();

        if (_monitorThread != null)
        {
            try
            {
                _monitorThread.Join(2000);
            }
            catch { }
        }

        await _syncLock.WaitAsync(ct);
        try
        {
            if (_currentActivity != null)
            {
                await FinalizeCurrentActivityAsync();
            }
            if (_currentIdleActivity != null)
            {
                await FinalizeCurrentIdleActivityAsync();
            }
        }
        finally
        {
            _syncLock.Release();
        }

        _logger.LogInformation("ActivityTracker stopped.");
    }

    private async Task ProcessSnapshotAsync(WindowSnapshot snapshot)
    {
        if (_isLocked) return;

        _logger.LogInformation("Sample: hWnd={HWnd}, PID={Pid}, Title='{Title}', Idle={Idle:mm\\:ss}",
            snapshot.HWnd, snapshot.ProcessId, snapshot.WindowTitle, snapshot.IdleTime);

        await _syncLock.WaitAsync();
        try
        {
            var idleThreshold = TimeSpan.FromMinutes(_options.IdleThresholdMinutes);

            if (snapshot.IdleTime >= idleThreshold)
            {
                await HandleIdleStateAsync(snapshot.IdleTime);
                return;
            }

            if (_isIdle)
            {
                await HandleResumeFromIdleAsync();
            }

            await HandleActiveWindowStateAsync(snapshot);
        }
        finally
        {
            _syncLock.Release();
        }
    }

    private async Task HandleIdleStateAsync(TimeSpan idleTime)
    {
        if (!_isIdle)
        {
            _logger.LogInformation("User entered IDLE state (Idle duration: {Duration})", idleTime);

            if (_currentActivity != null)
            {
                await FinalizeCurrentActivityAsync();
            }

            var idleStartUtc = DateTime.UtcNow - idleTime;
            _currentIdleActivity = new ActivityEvent
            {
                MachineId = Environment.MachineName,
                SessionId = GetCurrentSessionId(),
                UserIdentifier = Environment.UserName,
                ProcessName = "Idle",
                ProcessId = 0,
                WindowTitle = "User Idle",
                EventType = ActivityEventType.IdleStart,
                StartedAtUtc = idleStartUtc,
                EndedAtUtc = DateTime.UtcNow,
                DurationSeconds = idleTime.TotalSeconds,
                RecordingSegmentId = _recordingService.CurrentSegment?.Id,
                CreatedAtUtc = DateTime.UtcNow
            };

            await _activityRepository.AddAsync(_currentIdleActivity);
            _isIdle = true;
            _lastFlushTime = DateTime.UtcNow;
        }
        else if (_currentIdleActivity != null)
        {
            var now = DateTime.UtcNow;
            _currentIdleActivity.EndedAtUtc = now;
            _currentIdleActivity.DurationSeconds = (now - _currentIdleActivity.StartedAtUtc).TotalSeconds;

            if ((now - _lastFlushTime).TotalSeconds >= 30)
            {
                await _activityRepository.UpdateAsync(_currentIdleActivity);
                _lastFlushTime = now;
            }
        }
    }

    private async Task HandleResumeFromIdleAsync()
    {
        _logger.LogInformation("User returned from IDLE state.");
        if (_currentIdleActivity != null)
        {
            await FinalizeCurrentIdleActivityAsync();
        }
        _isIdle = false;
    }

    private async Task HandleActiveWindowStateAsync(WindowSnapshot snapshot)
    {
        string processName = "explorer";
        int processId = snapshot.ProcessId;
        string windowTitle = snapshot.WindowTitle;
        string? processPath = null;
        string? contextPath = null;

        if (snapshot.HWnd != IntPtr.Zero && processId > 0)
        {
            try
            {
                using var proc = Process.GetProcessById(processId);
                processName = proc.ProcessName;
            }
            catch { }

            if (string.IsNullOrWhiteSpace(windowTitle))
            {
                windowTitle = processName;
            }

            processPath = NativeMethods.GetProcessExecutablePath(processId);
            contextPath = _contextProvider.ResolveContext(processName, snapshot.HWnd, windowTitle);
        }
        else
        {
            windowTitle = "Desktop";
            contextPath = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
        }


        var now = DateTime.UtcNow;

        // Check if user is still on the same application & context
        if (_currentActivity != null &&
            _currentActivity.ProcessId == processId &&
            string.Equals(_currentActivity.ProcessName, processName, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(_currentActivity.WindowTitle, windowTitle, StringComparison.Ordinal) &&
            string.Equals(_currentActivity.ContextPath, contextPath, StringComparison.Ordinal))
        {
            // Merge duration into current activity
            _currentActivity.EndedAtUtc = now;
            _currentActivity.DurationSeconds = Math.Max(1.0, (now - _currentActivity.StartedAtUtc).TotalSeconds);

            if (_currentActivity.RecordingSegmentId == null && _recordingService.CurrentSegment != null)
            {
                _currentActivity.RecordingSegmentId = _recordingService.CurrentSegment.Id;
            }

            // Periodic flush every 30 seconds
            if ((now - _lastFlushTime).TotalSeconds >= 30)
            {
                await _activityRepository.UpdateAsync(_currentActivity);
                _lastFlushTime = now;
            }

            return;
        }

        // Window or context has changed: close previous activity
        if (_currentActivity != null)
        {
            await FinalizeCurrentActivityAsync();
        }

        // Create new ActivityEvent
        _currentActivity = new ActivityEvent
        {
            MachineId = Environment.MachineName,
            SessionId = GetCurrentSessionId(),
            UserIdentifier = Environment.UserName,
            ProcessName = processName,
            ProcessId = processId,
            ProcessPath = processPath,
            WindowTitle = windowTitle,
            ContextPath = contextPath,
            EventType = ActivityEventType.ActiveWindow,
            StartedAtUtc = now,
            EndedAtUtc = now,
            DurationSeconds = 1.0,
            RecordingSegmentId = _recordingService.CurrentSegment?.Id,
            CreatedAtUtc = now
        };

        await _activityRepository.AddAsync(_currentActivity);
        _lastFlushTime = now;

        _logger.LogInformation("Active Window changed: [{Process}] '{Title}' (Context: {Context})",
            processName, windowTitle, contextPath ?? "N/A");

        if (ActivityStarted != null)
        {
            await ActivityStarted.Invoke(_currentActivity);
        }
    }

    private async Task FinalizeCurrentActivityAsync()
    {
        if (_currentActivity == null) return;

        var now = DateTime.UtcNow;
        _currentActivity.EndedAtUtc = now;
        _currentActivity.DurationSeconds = Math.Max(1.0, (now - _currentActivity.StartedAtUtc).TotalSeconds);

        if (_currentActivity.RecordingSegmentId == null && _recordingService.CurrentSegment != null)
        {
            _currentActivity.RecordingSegmentId = _recordingService.CurrentSegment.Id;
        }

        await _activityRepository.UpdateAsync(_currentActivity);

        if (ActivityEnded != null)
        {
            await ActivityEnded.Invoke(_currentActivity);
        }

        _currentActivity = null;
    }

    private async Task FinalizeCurrentIdleActivityAsync()
    {
        if (_currentIdleActivity == null) return;

        var now = DateTime.UtcNow;
        _currentIdleActivity.EndedAtUtc = now;
        _currentIdleActivity.DurationSeconds = Math.Max(1.0, (now - _currentIdleActivity.StartedAtUtc).TotalSeconds);
        await _activityRepository.UpdateAsync(_currentIdleActivity);
        _currentIdleActivity = null;
    }

    private void OnSessionSwitch(object sender, SessionSwitchEventArgs e)
    {
        _logger.LogInformation("Windows Session Switch event received: {Reason}", e.Reason);

        _ = Task.Run(async () =>
        {
            await _syncLock.WaitAsync();
            try
            {
                if (_currentActivity != null) await FinalizeCurrentActivityAsync();

                ActivityEventType eventType = e.Reason switch
                {
                    SessionSwitchReason.SessionLock => ActivityEventType.SessionLock,
                    SessionSwitchReason.SessionUnlock => ActivityEventType.SessionUnlock,
                    SessionSwitchReason.SessionLogon => ActivityEventType.SessionLogon,
                    SessionSwitchReason.SessionLogoff => ActivityEventType.SessionLogoff,
                    _ => ActivityEventType.ActiveWindow
                };

                _isLocked = e.Reason == SessionSwitchReason.SessionLock;

                var sessionEvent = new ActivityEvent
                {
                    MachineId = Environment.MachineName,
                    SessionId = GetCurrentSessionId(),
                    UserIdentifier = Environment.UserName,
                    ProcessName = "System",
                    ProcessId = 0,
                    WindowTitle = e.Reason.ToString(),
                    EventType = eventType,
                    StartedAtUtc = DateTime.UtcNow,
                    EndedAtUtc = DateTime.UtcNow,
                    DurationSeconds = 0,
                    RecordingSegmentId = _recordingService.CurrentSegment?.Id,
                    CreatedAtUtc = DateTime.UtcNow
                };

                await _activityRepository.AddAsync(sessionEvent);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to record session switch event {Reason}", e.Reason);
            }
            finally
            {
                _syncLock.Release();
            }
        });
    }

    private void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
    {
        _logger.LogInformation("PowerModeChanged event: {Mode}", e.Mode);

        _ = Task.Run(async () =>
        {
            await _syncLock.WaitAsync();
            try
            {
                if (_currentActivity != null) await FinalizeCurrentActivityAsync();

                var eventType = e.Mode == PowerModes.Suspend ? ActivityEventType.SystemSleep : ActivityEventType.SystemResume;
                var powerEvent = new ActivityEvent
                {
                    MachineId = Environment.MachineName,
                    SessionId = GetCurrentSessionId(),
                    UserIdentifier = Environment.UserName,
                    ProcessName = "System",
                    ProcessId = 0,
                    WindowTitle = e.Mode.ToString(),
                    EventType = eventType,
                    StartedAtUtc = DateTime.UtcNow,
                    EndedAtUtc = DateTime.UtcNow,
                    DurationSeconds = 0,
                    RecordingSegmentId = _recordingService.CurrentSegment?.Id,
                    CreatedAtUtc = DateTime.UtcNow
                };

                await _activityRepository.AddAsync(powerEvent);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to record power mode event {Mode}", e.Mode);
            }
            finally
            {
                _syncLock.Release();
            }
        });
    }

    private void OnSessionEnding(object sender, SessionEndingEventArgs e)
    {
        _logger.LogInformation("SessionEnding event: {Reason}", e.Reason);

        try
        {
            _syncLock.Wait(1000);
            if (_currentActivity != null)
            {
                _currentActivity.EndedAtUtc = DateTime.UtcNow;
                _currentActivity.DurationSeconds = (DateTime.UtcNow - _currentActivity.StartedAtUtc).TotalSeconds;
                _activityRepository.UpdateAsync(_currentActivity).GetAwaiter().GetResult();
            }

            var shutdownEvent = new ActivityEvent
            {
                MachineId = Environment.MachineName,
                SessionId = GetCurrentSessionId(),
                UserIdentifier = Environment.UserName,
                ProcessName = "System",
                ProcessId = 0,
                WindowTitle = "System Shutdown",
                EventType = ActivityEventType.SystemShutdown,
                StartedAtUtc = DateTime.UtcNow,
                EndedAtUtc = DateTime.UtcNow,
                DurationSeconds = 0,
                RecordingSegmentId = _recordingService.CurrentSegment?.Id,
                CreatedAtUtc = DateTime.UtcNow
            };

            _activityRepository.AddAsync(shutdownEvent).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to record session ending event");
        }
        finally
        {
            _syncLock.Release();
        }
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
        _cts?.Dispose();
        _wakeUpEvent.Dispose();
        _syncLock.Dispose();
        GC.SuppressFinalize(this);
    }
}
