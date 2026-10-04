using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using ActivityMonitor.Core.Entities;
using ActivityMonitor.Core.Enums;
using ActivityMonitor.Core.Options;
using ActivityMonitor.Infrastructure.Data;
using ActivityMonitor.Viewer.Common;
using ActivityMonitor.Viewer.Services;
using Microsoft.EntityFrameworkCore;

namespace ActivityMonitor.Viewer.ViewModels;

public class MainViewModel : ViewModelBase
{
    private readonly WindowsServiceManager _serviceManager = new();
    private readonly DispatcherTimer _statusPollTimer = new();

    private DateTime _selectedDate = DateTime.Today;
    private string _searchText = string.Empty;
    private ActivityItemViewModel? _selectedActivity;
    private bool _isLoading;

    // Service & Agent status
    private string _serviceStatusText = "جاري الفحص...";
    private bool _isServiceRunning;
    private string _agentStatusText = "جاري الفحص...";
    private bool _isAgentRunning;
    private string _statusMessage = string.Empty;

    // Summary KPIs
    private string _totalActiveTimeDisplay = "00د 00ث";
    private string _totalIdleTimeDisplay = "00د 00ث";
    private int _totalEventsCount;
    private string _topAppName = "لا يوجد";
    private int _recordingsCount;

    // Video Player state
    private string? _currentVideoPath;
    private TimeSpan _requestedSeekPosition;
    private TimeSpan _currentVideoPosition;
    private TimeSpan _videoDuration;
    private bool _isPlaying;
    private string _nowPlayingTitle = "لا يوجد فيديو قيد التشغيل";
    private string _nowPlayingSub = "حدد نشاطاً من القائمة الجانبية لبدء المشاهدة";
    private double _volume = 0.8;

    public ObservableCollection<ActivityItemViewModel> Activities { get; } = new();
    public ObservableCollection<ActivityItemViewModel> FilteredActivities { get; } = new();
    public ObservableCollection<RecordingSegmentViewModel> Recordings { get; } = new();

    // Event raised for View to perform media seek and play
    public event Action<string, TimeSpan>? RequestMediaPlay;
    public event Action? RequestMediaPause;
    public event Action? RequestMediaResume;
    public event Action<TimeSpan>? RequestMediaSeek;

    public DateTime SelectedDate
    {
        get => _selectedDate;
        set
        {
            if (SetProperty(ref _selectedDate, value))
            {
                _ = LoadDateDataAsync();
            }
        }
    }

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetProperty(ref _searchText, value))
            {
                ApplyFilter();
            }
        }
    }

    public ActivityItemViewModel? SelectedActivity
    {
        get => _selectedActivity;
        set
        {
            if (SetProperty(ref _selectedActivity, value) && value != null)
            {
                if (value.HasVideo)
                {
                    PlayActivity(value);
                }
            }
        }
    }

    public bool IsLoading
    {
        get => _isLoading;
        set => SetProperty(ref _isLoading, value);
    }

    public string ServiceStatusText
    {
        get => _serviceStatusText;
        set => SetProperty(ref _serviceStatusText, value);
    }

    public bool IsServiceRunning
    {
        get => _isServiceRunning;
        set => SetProperty(ref _isServiceRunning, value);
    }

    public string AgentStatusText
    {
        get => _agentStatusText;
        set => SetProperty(ref _agentStatusText, value);
    }

    public bool IsAgentRunning
    {
        get => _isAgentRunning;
        set => SetProperty(ref _isAgentRunning, value);
    }

    public string StatusMessage
    {
        get => _statusMessage;
        set => SetProperty(ref _statusMessage, value);
    }

    public string TotalActiveTimeDisplay
    {
        get => _totalActiveTimeDisplay;
        set => SetProperty(ref _totalActiveTimeDisplay, value);
    }

    public string TotalIdleTimeDisplay
    {
        get => _totalIdleTimeDisplay;
        set => SetProperty(ref _totalIdleTimeDisplay, value);
    }

    public int TotalEventsCount
    {
        get => _totalEventsCount;
        set => SetProperty(ref _totalEventsCount, value);
    }

    public string TopAppName
    {
        get => _topAppName;
        set => SetProperty(ref _topAppName, value);
    }

    public int RecordingsCount
    {
        get => _recordingsCount;
        set => SetProperty(ref _recordingsCount, value);
    }

    public string? CurrentVideoPath
    {
        get => _currentVideoPath;
        set => SetProperty(ref _currentVideoPath, value);
    }

    public TimeSpan RequestedSeekPosition
    {
        get => _requestedSeekPosition;
        set => SetProperty(ref _requestedSeekPosition, value);
    }

    public TimeSpan CurrentVideoPosition
    {
        get => _currentVideoPosition;
        set
        {
            if (SetProperty(ref _currentVideoPosition, value))
            {
                OnPropertyChanged(nameof(VideoPositionDisplay));
            }
        }
    }

    public TimeSpan VideoDuration
    {
        get => _videoDuration;
        set
        {
            if (SetProperty(ref _videoDuration, value))
            {
                OnPropertyChanged(nameof(VideoPositionDisplay));
            }
        }
    }

    public string VideoPositionDisplay => $"{CurrentVideoPosition:mm\\:ss} / {VideoDuration:mm\\:ss}";

    public bool IsPlaying
    {
        get => _isPlaying;
        set => SetProperty(ref _isPlaying, value);
    }

    public string NowPlayingTitle
    {
        get => _nowPlayingTitle;
        set => SetProperty(ref _nowPlayingTitle, value);
    }

    public string NowPlayingSub
    {
        get => _nowPlayingSub;
        set => SetProperty(ref _nowPlayingSub, value);
    }

    public double Volume
    {
        get => _volume;
        set => SetProperty(ref _volume, value);
    }

    // Commands
    public ICommand PreviousDayCommand { get; }
    public ICommand NextDayCommand { get; }
    public ICommand TodayCommand { get; }
    public ICommand RefreshCommand { get; }
    public ICommand ToggleServiceCommand { get; }
    public ICommand ToggleAgentCommand { get; }
    public ICommand PlayActivityCommand { get; }
    public ICommand PlayPauseCommand { get; }
    public ICommand JumpForwardCommand { get; }
    public ICommand JumpBackwardCommand { get; }
    public ICommand OpenReportsFolderCommand { get; }
    public ICommand OpenRecordingsFolderCommand { get; }

    public MainViewModel()
    {
        PreviousDayCommand = new RelayCommand(() => SelectedDate = SelectedDate.AddDays(-1));
        NextDayCommand = new RelayCommand(() => SelectedDate = SelectedDate.AddDays(1));
        TodayCommand = new RelayCommand(() => SelectedDate = DateTime.Today);
        RefreshCommand = new RelayCommand(async () => await LoadDateDataAsync());
        ToggleServiceCommand = new RelayCommand(async () => await ToggleServiceAsync());
        ToggleAgentCommand = new RelayCommand(async () => await ToggleAgentAsync());
        PlayActivityCommand = new RelayCommand(param =>
        {
            if (param is ActivityItemViewModel item)
            {
                PlayActivity(item);
            }
        });
        PlayPauseCommand = new RelayCommand(TogglePlayPause);
        JumpForwardCommand = new RelayCommand(() => JumpRelative(TimeSpan.FromSeconds(5)));
        JumpBackwardCommand = new RelayCommand(() => JumpRelative(TimeSpan.FromSeconds(-5)));
        OpenReportsFolderCommand = new RelayCommand(OpenReportsFolder);
        OpenRecordingsFolderCommand = new RelayCommand(OpenRecordingsFolder);

        // Start polling service & agent status every 3 seconds
        _statusPollTimer.Interval = TimeSpan.FromSeconds(3);
        _statusPollTimer.Tick += (_, _) => CheckStatuses();
        _statusPollTimer.Start();

        CheckStatuses();
        _ = LoadDateDataAsync();
    }

    public void CheckStatuses()
    {
        var sState = _serviceManager.GetServiceState();
        IsServiceRunning = sState == ServiceState.Running;
        ServiceStatusText = sState switch
        {
            ServiceState.Running => "الخدمة: قيد التشغيل",
            ServiceState.Stopped => "الخدمة: متوقفة",
            ServiceState.Transitioning => "الخدمة: جاري المعالجة...",
            ServiceState.NotInstalled => "الخدمة: غير مثبتة",
            _ => "الخدمة: غير معروفة"
        };

        var aRunning = _serviceManager.IsAgentRunning();
        IsAgentRunning = aRunning;
        AgentStatusText = aRunning ? "العميل: نشط" : "العميل: متوقف";
    }

    private async Task ToggleServiceAsync()
    {
        StatusMessage = "جاري إرسال أمر تبديل حالة الخدمة...";
        var result = await _serviceManager.ToggleServiceAsync();
        StatusMessage = result;
        CheckStatuses();
    }

    private async Task ToggleAgentAsync()
    {
        StatusMessage = "جاري إرسال أمر تبديل حالة العميل...";
        var result = await _serviceManager.ToggleAgentAsync();
        StatusMessage = result;
        CheckStatuses();
    }

    public async Task LoadDateDataAsync()
    {
        IsLoading = true;
        StatusMessage = $"جاري تحميل بيانات {SelectedDate:yyyy-MM-dd}...";

        try
        {
            await Task.Run(async () =>
            {
                var startOfDayUtc = SelectedDate.Date.ToUniversalTime();
                var endOfDayUtc = SelectedDate.Date.AddDays(1).ToUniversalTime();

                using var db = new ActivityMonitorDbContext();

                // Fetch activities
                var events = await db.ActivityEvents
                    .AsNoTracking()
                    .Where(a => a.StartedAtUtc >= startOfDayUtc && a.StartedAtUtc < endOfDayUtc)
                    .OrderBy(a => a.StartedAtUtc)
                    .ToListAsync();

                // Fetch recording segments
                var segments = await db.RecordingSegments
                    .AsNoTracking()
                    .Where(r => r.StartedAtUtc >= startOfDayUtc && r.StartedAtUtc < endOfDayUtc)
                    .OrderBy(r => r.StartedAtUtc)
                    .ToListAsync();

                // Build lookup of segments
                var segmentDict = segments.ToDictionary(s => s.Id);

                var items = new List<ActivityItemViewModel>();
                double totalActiveSeconds = 0;
                double totalIdleSeconds = 0;
                var appDurations = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);

                foreach (var ev in events)
                {
                    RecordingSegment? seg = null;
                    if (ev.RecordingSegmentId.HasValue && segmentDict.TryGetValue(ev.RecordingSegmentId.Value, out var s))
                    {
                        seg = s;
                    }
                    else
                    {
                        // Fallback: match by time range
                        seg = segments.FirstOrDefault(s => ev.StartedAtUtc >= s.StartedAtUtc &&
                            (s.EndedAtUtc == null || ev.StartedAtUtc <= s.EndedAtUtc.Value));
                    }

                    var offset = TimeSpan.Zero;
                    if (seg != null && ev.StartedAtUtc >= seg.StartedAtUtc)
                    {
                        offset = ev.StartedAtUtc - seg.StartedAtUtc;
                    }

                    if (ev.EventType == ActivityEventType.ActiveWindow)
                    {
                        totalActiveSeconds += ev.DurationSeconds;
                        var app = ev.ProcessName;
                        appDurations[app] = appDurations.GetValueOrDefault(app) + ev.DurationSeconds;
                    }
                    else if (ev.EventType == ActivityEventType.IdleStart)
                    {
                        totalIdleSeconds += ev.DurationSeconds;
                    }

                    items.Add(new ActivityItemViewModel
                    {
                        Id = ev.Id,
                        StartedAtUtc = ev.StartedAtUtc,
                        EndedAtUtc = ev.EndedAtUtc,
                        ProcessName = ev.ProcessName,
                        WindowTitle = ev.WindowTitle ?? string.Empty,
                        ContextPath = ev.ContextPath,
                        ProcessPath = ev.ProcessPath,
                        EventType = ev.EventType.ToString(),
                        DurationSeconds = ev.DurationSeconds,
                        RecordingSegmentId = seg?.Id,
                        RecordingFileName = seg?.FileName,
                        RecordingFilePath = seg?.FilePath,
                        VideoOffset = offset
                    });
                }

                var segViewModels = segments.Select(s => new RecordingSegmentViewModel
                {
                    Id = s.Id,
                    StartedAtUtc = s.StartedAtUtc,
                    EndedAtUtc = s.EndedAtUtc,
                    FileName = s.FileName,
                    FilePath = s.FilePath,
                    DurationSeconds = s.DurationSeconds,
                    FileSizeBytes = s.FileSizeBytes,
                    Status = s.Status.ToString()
                }).ToList();

                var topApp = appDurations.OrderByDescending(kv => kv.Value).FirstOrDefault().Key ?? "N/A";

                // Update UI on main thread
                Application.Current.Dispatcher.Invoke(() =>
                {
                    Activities.Clear();
                    foreach (var it in items) Activities.Add(it);

                    Recordings.Clear();
                    foreach (var s in segViewModels) Recordings.Add(s);

                    TotalEventsCount = items.Count;
                    RecordingsCount = segViewModels.Count;
                    TopAppName = topApp;

                    var activeTs = TimeSpan.FromSeconds(totalActiveSeconds);
                    TotalActiveTimeDisplay = activeTs.TotalHours >= 1
                        ? $"{(int)activeTs.TotalHours}س {activeTs.Minutes}د"
                        : $"{activeTs.Minutes}د {activeTs.Seconds}ث";

                    var idleTs = TimeSpan.FromSeconds(totalIdleSeconds);
                    TotalIdleTimeDisplay = idleTs.TotalHours >= 1
                        ? $"{(int)idleTs.TotalHours}س {idleTs.Minutes}د"
                        : $"{idleTs.Minutes}د {idleTs.Seconds}ث";

                    ApplyFilter();
                    StatusMessage = $"تم تحميل {items.Count} نشاطاً و {segViewModels.Count} مقطع فيديو.";
                });
            });
        }
        catch (Exception ex)
        {
            StatusMessage = $"خطأ أثناء تحميل البيانات: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    private void ApplyFilter()
    {
        FilteredActivities.Clear();
        var q = Activities.AsEnumerable();

        if (!string.IsNullOrWhiteSpace(SearchText))
        {
            var term = SearchText.Trim();
            q = q.Where(a =>
                a.ProcessName.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                a.WindowTitle.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                (a.ContextPath != null && a.ContextPath.Contains(term, StringComparison.OrdinalIgnoreCase)));
        }

        foreach (var item in q)
        {
            FilteredActivities.Add(item);
        }
    }

    public void PlayActivity(ActivityItemViewModel activity)
    {
        if (string.IsNullOrWhiteSpace(activity.RecordingFilePath) || !File.Exists(activity.RecordingFilePath))
        {
            StatusMessage = $"ملف الفيديو الخاص بـ '{activity.ProcessName}' غير موجود على القرص.";
            return;
        }

        CurrentVideoPath = activity.RecordingFilePath;
        RequestedSeekPosition = activity.VideoOffset;
        NowPlayingTitle = $"{activity.ProcessName} - {activity.WindowTitle}";
        NowPlayingSub = $"{activity.TimeRangeDisplay} | الإزاحة: {activity.OffsetDisplay} | الملف: {activity.RecordingFileName}";

        StatusMessage = $"جاري القفز إلى {activity.OffsetDisplay} في مقطع {activity.RecordingFileName}...";
        RequestMediaPlay?.Invoke(activity.RecordingFilePath, activity.VideoOffset);
        IsPlaying = true;
    }

    private void TogglePlayPause()
    {
        if (IsPlaying)
        {
            RequestMediaPause?.Invoke();
            IsPlaying = false;
        }
        else
        {
            RequestMediaResume?.Invoke();
            IsPlaying = true;
        }
    }

    private void JumpRelative(TimeSpan offset)
    {
        var target = CurrentVideoPosition + offset;
        if (target < TimeSpan.Zero) target = TimeSpan.Zero;
        if (target > VideoDuration && VideoDuration > TimeSpan.Zero) target = VideoDuration;
        RequestMediaSeek?.Invoke(target);
    }

    private void OpenReportsFolder()
    {
        try
        {
            var folder = ActivityMonitorPaths.ReportsDirectory;
            if (Directory.Exists(folder))
            {
                Process.Start("explorer.exe", folder);
            }
        }
        catch { }
    }

    private void OpenRecordingsFolder()
    {
        try
        {
            var folder = ActivityMonitorPaths.RecordingsDirectory;
            if (Directory.Exists(folder))
            {
                Process.Start("explorer.exe", folder);
            }
        }
        catch { }
    }
}
