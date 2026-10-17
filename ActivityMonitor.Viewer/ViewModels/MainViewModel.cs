using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using ActivityMonitor.Core.Entities;
using ActivityMonitor.Core.Enums;
using ActivityMonitor.Core.Models;
using ActivityMonitor.Core.Options;
using ActivityMonitor.Infrastructure.Data;
using ActivityMonitor.Infrastructure.Services;
using ActivityMonitor.Viewer.Common;
using ActivityMonitor.Viewer.Services;
using Microsoft.EntityFrameworkCore;

namespace ActivityMonitor.Viewer.ViewModels;

/// <summary>
/// نموذج العرض الرئيسي للتطبيق (MainViewModel).
/// يمثل العصب المركزي لواجهة المستخدم، ويتولى استرجاع بيانات الأنشطة والتسجيلات اليومية، وإدارة تشغيل الفيديو
/// والتنقل بين الأجزاء المتعددة، ورسم الشريط الزمني، ومراقبة حالة خدمة وعميل ويندوز وتصدير التقارير.
/// </summary>
public class MainViewModel : ViewModelBase, IDisposable
{
    // مدير خدمات وعمليات نظام ويندوز
    private readonly WindowsServiceManager _serviceManager = new();

    // خدمة تصدير التقارير اليومية بصيغ متعددة
    private readonly ViewerReportService _reportService = new();

    // مؤقت الاستعلام الدوري عن حالة الخدمة والعميل
    private readonly DispatcherTimer _statusPollTimer = new();
    private bool _isDisposed;
    private AgentHealthSnapshot? _agentHealth;
    private long _minimumFreeBytes = 2L * 1024 * 1024 * 1024;

    // ==========================================
    // الحقول الخلفية للخصائص (Backing Fields)
    // ==========================================
    private DateTime _selectedDate = DateTime.Today;
    private string _searchText = string.Empty;
    private string _selectedActivityFilterKey = "All";
    private ActivityItemViewModel? _selectedActivity;
    private bool _isLoading;

    // حالة خدمة وعميل ويندوز
    private string _serviceStatusText = "جاري الفحص...";
    private bool _isServiceRunning;
    private string _agentStatusText = "جاري الفحص...";
    private bool _isAgentRunning;
    private string _statusMessage = string.Empty;

    // مؤشرات الأداء الرئيسية (KPIs)
    private string _totalActiveTimeDisplay = "00د 00ث";
    private string _totalIdleTimeDisplay = "00د 00ث";
    private int _totalEventsCount;
    private string _topAppName = "لا يوجد";
    private int _recordingsCount;

    // حالة مشغل الفيديو
    private string? _currentVideoPath;
    private TimeSpan _requestedSeekPosition;
    private TimeSpan _currentVideoPosition;
    private TimeSpan _videoDuration;
    private bool _isPlaying;
    private string _nowPlayingTitle = "لا يوجد فيديو قيد التشغيل";
    private string _nowPlayingSub = "حدد نشاطاً من القائمة الجانبية لبدء المشاهدة";
    private double _volume = 0.8;
    private long _lastPlayedActivityId = -1;
    private DateTime _lastPlayRequestTime = DateTime.MinValue;
    private int _loadRequestVersion;
    private ActivityItemViewModel? _playingActivity;
    private int _currentRecordingPartIndex = -1;
    private string _currentRecordingPartDisplay = "لا توجد مقاطع مرتبطة";
    private int _missingRecordingCount;
    private long _freeDiskSpaceBytes;
    private bool _isDiskSpaceKnown;
    private DateTime? _lastDataRefreshUtc;
    private string _systemHealthStatusText = "جاري التحقق...";
    private string _systemHealthDetailText = "لم يكتمل فحص الحالة بعد";
    private bool _isSystemHealthy;
    private bool _isExportingReports;

    /// <summary>
    /// مجموعة كافة الأنشطة المحملة لليوم المحدد.
    /// </summary>
    public ObservableCollection<ActivityItemViewModel> Activities { get; } = new();

    /// <summary>
    /// مجموعة الأنشطة المصفاة المعروضة حالياً في القائمة بعد تطبيق شروط البحث والنوع.
    /// </summary>
    public ObservableCollection<ActivityItemViewModel> FilteredActivities { get; } = new();

    /// <summary>
    /// مجموعة مقاطع الفيديو المسجلة في اليوم المحدد.
    /// </summary>
    public ObservableCollection<RecordingSegmentViewModel> Recordings { get; } = new();

    /// <summary>
    /// كتل المخطط الزمني الأفقي (24 ساعة) لليوم المحدد.
    /// </summary>
    public ObservableCollection<TimelineBlockViewModel> TimelineBlocks { get; } = new();

    /// <summary>
    /// خيارات فلترة الأنشطة المتاحة في القائمة المنسدلة.
    /// </summary>
    public IReadOnlyList<ActivityFilterOption> ActivityFilters { get; } =
    [
        new("All", "كل الأنشطة"),
        new("Active", "نشاط فعلي"),
        new("Idle", "خمول"),
        new("WithVideo", "مع فيديو"),
        new("WithoutVideo", "بدون فيديو")
    ];

    // ==========================================
    // أحداث التحكم في وسائط الفيديو (Media Events)
    // ==========================================

    /// <summary>
    /// حدث موجه لعنصر المشغل في العرض لبدء تشغيل ملف محدد عند إزاحة زمنية معينة.
    /// </summary>
    public event Action<string, TimeSpan>? RequestMediaPlay;

    /// <summary>
    /// حدث إيقاف التشغيل مؤقتاً.
    /// </summary>
    public event Action? RequestMediaPause;

    /// <summary>
    /// حدث استئناف التشغيل.
    /// </summary>
    public event Action? RequestMediaResume;

    /// <summary>
    /// حدث إيقاف التشغيل وإغلاق مصدر الوسائط.
    /// </summary>
    public event Action? RequestMediaStop;

    /// <summary>
    /// حدث القفز إلى موضع زمني محدد داخل الفيديو.
    /// </summary>
    public event Action<TimeSpan>? RequestMediaSeek;

    /// <summary>
    /// التاريخ المعروض حالياً في التطبيق. يؤدي تغييره إلى تصفير المشغل وإعادة تحميل البيانات.
    /// </summary>
    public DateTime SelectedDate
    {
        get => _selectedDate;
        set
        {
            if (SetProperty(ref _selectedDate, value))
            {
                // تصفير المشغل الحالي لمنع تشغيل فيديو غير متوافق مع اليوم الجديد
                ResetPlayback();

                // تحميل بيانات اليوم الجديد
                _ = LoadDateDataAsync();
            }
        }
    }

    /// <summary>
    /// نص البحث للفلترة الفورية لاسم التطبيق أو عنوان النافذة أو المسار.
    /// </summary>
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

    /// <summary>
    /// مفتاح فلتر الأنشطة المختار (All, Active, Idle, WithVideo, WithoutVideo).
    /// </summary>
    public string SelectedActivityFilterKey
    {
        get => _selectedActivityFilterKey;
        set
        {
            if (SetProperty(ref _selectedActivityFilterKey, value))
            {
                ApplyFilter();
            }
        }
    }

    /// <summary>
    /// النص المعروض لعدد العناصر المصفاة مقارنة بالإجمالي (مثال: 45 من 120).
    /// </summary>
    public string FilteredCountDisplay => $"{FilteredActivities.Count} من {Activities.Count}";

    /// <summary>
    /// هل قائمة الأنشطة المصفاة فارغة تماماً.
    /// </summary>
    public bool IsActivityListEmpty => FilteredActivities.Count == 0;

    /// <summary>
    /// النشاط المختار حالياً من القائمة الجانبية أو الشريط الزمني.
    /// </summary>
    public ActivityItemViewModel? SelectedActivity
    {
        get => _selectedActivity;
        set
        {
            if (SetProperty(ref _selectedActivity, value) && value != null)
            {
                // إذا كان النشاط يمتلك فيديو مرتبطاً، نبدأ تشغيله فور اختياره
                if (value.HasVideo)
                {
                    PlayActivity(value);
                }
            }
        }
    }

    /// <summary>
    /// هل عملية تحميل البيانات جارية حالياً من قاعدة البيانات.
    /// </summary>
    public bool IsLoading
    {
        get => _isLoading;
        set => SetProperty(ref _isLoading, value);
    }

    /// <summary>
    /// نص حالة خدمة ويندوز المعروض في الشريط العلوي.
    /// </summary>
    public string ServiceStatusText
    {
        get => _serviceStatusText;
        set => SetProperty(ref _serviceStatusText, value);
    }

    /// <summary>
    /// هل خدمة ويندوز قيد التشغيل حالياً.
    /// </summary>
    public bool IsServiceRunning
    {
        get => _isServiceRunning;
        set => SetProperty(ref _isServiceRunning, value);
    }

    /// <summary>
    /// نص حالة عميل المراقبة المعروض في الشريط العلوي.
    /// </summary>
    public string AgentStatusText
    {
        get => _agentStatusText;
        set => SetProperty(ref _agentStatusText, value);
    }

    /// <summary>
    /// هل عميل المراقبة قيد التشغيل في الجلسة الحالية.
    /// </summary>
    public bool IsAgentRunning
    {
        get => _isAgentRunning;
        set => SetProperty(ref _isAgentRunning, value);
    }

    /// <summary>
    /// رسالة الحالة العامة والتنبيهات في شريط الحالة السفلي.
    /// </summary>
    public string StatusMessage
    {
        get => _statusMessage;
        set => SetProperty(ref _statusMessage, value);
    }

    /// <summary>
    /// النص المنسق لإجمالي وقت النشاط الفعلي (مثال: 5س 42د).
    /// </summary>
    public string TotalActiveTimeDisplay
    {
        get => _totalActiveTimeDisplay;
        set => SetProperty(ref _totalActiveTimeDisplay, value);
    }

    /// <summary>
    /// النص المنسق لإجمالي وقت خمول المستخدم (مثال: 1س 15د).
    /// </summary>
    public string TotalIdleTimeDisplay
    {
        get => _totalIdleTimeDisplay;
        set => SetProperty(ref _totalIdleTimeDisplay, value);
    }

    /// <summary>
    /// إجمالي عدد أحداث الأنشطة المسجلة في اليوم.
    /// </summary>
    public int TotalEventsCount
    {
        get => _totalEventsCount;
        set => SetProperty(ref _totalEventsCount, value);
    }

    /// <summary>
    /// اسم التطبيق الأكثر استخداماً خلال اليوم المحدد.
    /// </summary>
    public string TopAppName
    {
        get => _topAppName;
        set => SetProperty(ref _topAppName, value);
    }

    /// <summary>
    /// عدد مقاطع الفيديو المسجلة خلال اليوم.
    /// </summary>
    public int RecordingsCount
    {
        get => _recordingsCount;
        set => SetProperty(ref _recordingsCount, value);
    }

    /// <summary>
    /// مسار ملف الفيديو الذي يتم تشغيله حالياً في المشغل.
    /// </summary>
    public string? CurrentVideoPath
    {
        get => _currentVideoPath;
        set => SetProperty(ref _currentVideoPath, value);
    }

    /// <summary>
    /// موضع القفز المطلوب تطبيقه في الفيديو بالثواني.
    /// </summary>
    public TimeSpan RequestedSeekPosition
    {
        get => _requestedSeekPosition;
        set => SetProperty(ref _requestedSeekPosition, value);
    }

    /// <summary>
    /// الموضع الزمني الحالي لتشغيل الفيديو.
    /// </summary>
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

    /// <summary>
    /// المدة الإجمالية لمقطع الفيديو قيد التشغيل.
    /// </summary>
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

    /// <summary>
    /// النص المنسق لموضع التشغيل الحالي مقارنة بإجمالي المدة (مثال: 03:45 / 10:00).
    /// </summary>
    public string VideoPositionDisplay => $"{ActivityTimelineMath.FormatPlaybackTime(CurrentVideoPosition)} / {ActivityTimelineMath.FormatPlaybackTime(VideoDuration)}";

    /// <summary>
    /// هل الفيديو قيد التشغيل النشط حالياً في المشغل.
    /// </summary>
    public bool IsPlaying
    {
        get => _isPlaying;
        set => SetProperty(ref _isPlaying, value);
    }

    /// <summary>
    /// العنوان الرئيسي المعروض في بطاقة المشغل (اسم التطبيق وعنوان النافذة).
    /// </summary>
    public string NowPlayingTitle
    {
        get => _nowPlayingTitle;
        set => SetProperty(ref _nowPlayingTitle, value);
    }

    /// <summary>
    /// العنوان الفرعي المعروض في بطاقة المشغل (نطاق الوقت، الإزاحة، اسم الملف).
    /// </summary>
    public string NowPlayingSub
    {
        get => _nowPlayingSub;
        set => SetProperty(ref _nowPlayingSub, value);
    }

    /// <summary>
    /// مستوى صوت مشغل الفيديو في واجهة العارض (من 0.0 إلى 1.0).
    /// </summary>
    public double Volume
    {
        get => _volume;
        set => SetProperty(ref _volume, value);
    }

    /// <summary>
    /// النص المنسق للجزء الحالي قيد التشغيل (مثال: المقطع 2 من 3 • rec_part2.mp4).
    /// </summary>
    public string CurrentRecordingPartDisplay
    {
        get => _currentRecordingPartDisplay;
        private set => SetProperty(ref _currentRecordingPartDisplay, value);
    }

    /// <summary>
    /// هل يمكن الرجوع لتشغيل جزء فيديو سابق لهذا النشاط.
    /// </summary>
    public bool CanPlayPreviousRecordingPart => FindPreviousExistingPartIndex() >= 0;

    /// <summary>
    /// هل يمكن الانتقال لتشغيل جزء فيديو تالٍ لهذا النشاط.
    /// </summary>
    public bool CanPlayNextRecordingPart => FindNextExistingPartIndex() >= 0;

    /// <summary>
    /// نص حالة سلامة النظام الأساسي (الحالة سليمة / توجد ملاحظات).
    /// </summary>
    public string SystemHealthStatusText
    {
        get => _systemHealthStatusText;
        private set => SetProperty(ref _systemHealthStatusText, value);
    }

    /// <summary>
    /// النص التفصيلي لحالة سلامة النظام ومساحة القرص ووقت آخر تحديث.
    /// </summary>
    public string SystemHealthDetailText
    {
        get => _systemHealthDetailText;
        private set => SetProperty(ref _systemHealthDetailText, value);
    }

    /// <summary>
    /// هل النظام سليم بالكامل وخالٍ من أي تنبيهات أو توقفات.
    /// </summary>
    public bool IsSystemHealthy
    {
        get => _isSystemHealthy;
        private set => SetProperty(ref _isSystemHealthy, value);
    }

    // ==========================================
    // نموذج نافذة الإعدادات (Settings Modal)
    // ==========================================

    /// <summary>
    /// كائن نموذج عرض نافذة الإعدادات.
    /// </summary>
    public SettingsViewModel Settings { get; }

    private bool _isSettingsOpen;

    /// <summary>
    /// هل النافذة المنبثقة للإعدادات مفتوحة حالياً في الواجهة.
    /// </summary>
    public bool IsSettingsOpen
    {
        get => _isSettingsOpen;
        set => SetProperty(ref _isSettingsOpen, value);
    }

    // ==========================================
    // الأوامر (Commands)
    // ==========================================

    /// <summary>
    /// أمر الانتقال لليوم السابق.
    /// </summary>
    public ICommand PreviousDayCommand { get; }

    /// <summary>
    /// أمر الانتقال لليوم التالي.
    /// </summary>
    public ICommand NextDayCommand { get; }

    /// <summary>
    /// أمر الانتقال إلى تاريخ اليوم الحالي.
    /// </summary>
    public ICommand TodayCommand { get; }

    /// <summary>
    /// أمر إعادة تحميل وتحديث بيانات اليوم المختار.
    /// </summary>
    public ICommand RefreshCommand { get; }

    /// <summary>
    /// أمر تشغيل/إيقاف خدمة ويندوز.
    /// </summary>
    public ICommand ToggleServiceCommand { get; }

    /// <summary>
    /// أمر تشغيل/إيقاف عميل المراقبة.
    /// </summary>
    public ICommand ToggleAgentCommand { get; }

    /// <summary>
    /// أمر تشغيل فيديو لنشاط معين عند النقر عليه.
    /// </summary>
    public ICommand PlayActivityCommand { get; }

    /// <summary>
    /// أمر تبديل حالة التشغيل والإيقاف المؤقت للمشغل.
    /// </summary>
    public ICommand PlayPauseCommand { get; }

    /// <summary>
    /// أمر القفز للأمام 5 ثوانٍ في الفيديو.
    /// </summary>
    public ICommand JumpForwardCommand { get; }

    /// <summary>
    /// أمر القفز للخلف 5 ثوانٍ في الفيديو.
    /// </summary>
    public ICommand JumpBackwardCommand { get; }

    /// <summary>
    /// أمر فتح مجلد التقارير في مستكشف ويندوز.
    /// </summary>
    public ICommand OpenReportsFolderCommand { get; }

    /// <summary>
    /// أمر فتح مجلد التسجيلات في مستكشف ويندوز.
    /// </summary>
    public ICommand OpenRecordingsFolderCommand { get; }

    /// <summary>
    /// أمر فتح نافذة الإعدادات وبدء اختبار الصوت.
    /// </summary>
    public ICommand OpenSettingsCommand { get; }

    /// <summary>
    /// أمر إغلاق نافذة الإعدادات وإيقاف اختبار الصوت.
    /// </summary>
    public ICommand CloseSettingsCommand { get; }

    /// <summary>
    /// أمر مسح فلاتر البحث وإعادة تعيين القائمة المنسدلة للكل.
    /// </summary>
    public ICommand ClearFiltersCommand { get; }

    /// <summary>
    /// أمر اختيار نشاط من الشريط الزمني وعرضه.
    /// </summary>
    public ICommand SelectTimelineActivityCommand { get; }

    /// <summary>
    /// أمر الانتقال لمقطع الفيديو السابق للنشاط متعدد الأجزاء.
    /// </summary>
    public ICommand PreviousRecordingPartCommand { get; }

    /// <summary>
    /// أمر الانتقال لمقطع الفيديو التالي للنشاط متعدد الأجزاء.
    /// </summary>
    public ICommand NextRecordingPartCommand { get; }

    /// <summary>
    /// أمر تصدير تقارير اليوم المحدد بصيغ Excel و CSV وملخص نصي.
    /// </summary>
    public ICommand ExportSelectedDayCommand { get; }

    /// <summary>
    /// تهيئة نموذج العرض الرئيسي وربط الأوامر وبدء المؤقتات الدورية.
    /// </summary>
    public MainViewModel()
    {
        // تهيئة نموذج الإعدادات
        Settings = new SettingsViewModel(_serviceManager);

        // ربط أوامر التنقل بين الأيام
        PreviousDayCommand = new RelayCommand(() => SelectedDate = SelectedDate.AddDays(-1));
        NextDayCommand = new RelayCommand(() => SelectedDate = SelectedDate.AddDays(1));
        TodayCommand = new RelayCommand(() => SelectedDate = DateTime.Today);
        RefreshCommand = new RelayCommand(async () => await LoadDateDataAsync());

        // ربط أوامر تبديل الخدمات
        ToggleServiceCommand = new RelayCommand(async () => await ToggleServiceAsync(), () => !App.IsReviewOnlyMode);
        ToggleAgentCommand = new RelayCommand(async () => await ToggleAgentAsync(), () => !App.IsReviewOnlyMode);

        // ربط أوامر الإعدادات
        OpenSettingsCommand = new RelayCommand(async () =>
        {
            await Settings.LoadSettingsAsync();
            if (_isDisposed) return;
            IsSettingsOpen = true;
            Settings.StartAudioMonitoring();
        }, () => !App.IsReviewOnlyMode);
        CloseSettingsCommand = new RelayCommand(() =>
        {
            Settings.StopAudioMonitoring();
            IsSettingsOpen = false;
        });

        // ربط أوامر مشغل الفيديو
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

        // ربط أوامر فتح المجلدات
        OpenReportsFolderCommand = new RelayCommand(OpenReportsFolder);
        OpenRecordingsFolderCommand = new RelayCommand(OpenRecordingsFolder);

        // أوامر الفلترة والتنقل بين الأجزاء
        ClearFiltersCommand = new RelayCommand(() =>
        {
            SearchText = string.Empty;
            SelectedActivityFilterKey = "All";
        });
        SelectTimelineActivityCommand = new RelayCommand(parameter =>
        {
            if (parameter is ActivityItemViewModel activity)
            {
                SelectedActivity = activity;
            }
        });
        PreviousRecordingPartCommand = new RelayCommand(PlayPreviousRecordingPart);
        NextRecordingPartCommand = new RelayCommand(() => TryPlayNextRecordingPart());
        ExportSelectedDayCommand = new RelayCommand(async () => await ExportSelectedDayAsync());

        // تهيئة مؤقت الاستعلام الدوري عن حالة الخدمة والعميل كل 3 ثوانٍ
        _statusPollTimer.Interval = TimeSpan.FromSeconds(3);
        _statusPollTimer.Tick += (_, _) => CheckStatuses();
        _statusPollTimer.Start();

        // فحص الحالة وتحميل بيانات اليوم الافتراضي عند البدء
        CheckStatuses();
        _ = LoadDateDataAsync();
    }

    /// <summary>
    /// الاستعلام عن الحالة الراهنة لخدمة وعميل ويندوز وتحديث مؤشرات واجهة المستخدم وسلامة النظام.
    /// </summary>
    public void CheckStatuses()
    {
        if (_isDisposed) return;
        // 1. فحص خدمة ويندوز
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

        // 2. فحص عميل المراقبة في جلسة المستخدم الحالية
        var aRunning = _serviceManager.IsAgentRunning();
        IsAgentRunning = aRunning;
        AgentStatusText = aRunning ? "العميل: نشط" : "العميل: متوقف";

        // تحديث ملخص صحة وسلامة النظام
        try
        {
            var session = Process.GetCurrentProcess().SessionId;
            var path = Path.Combine(ActivityMonitorPaths.RootDirectory, $"agent-health-{session}.json");
            _agentHealth = File.Exists(path) ? JsonSerializer.Deserialize<AgentHealthSnapshot>(File.ReadAllText(path)) : null;
            var (known, free) = TryGetFreeDiskSpace();
            _isDiskSpaceKnown = known; _freeDiskSpaceBytes = free;
            var configPath = File.Exists(ActivityMonitorPaths.SettingsPath) ? ActivityMonitorPaths.SettingsPath : Path.Combine(ActivityMonitorPaths.SharedRootDirectory, "appsettings.json");
            if (File.Exists(configPath))
                _minimumFreeBytes = (JsonNode.Parse(File.ReadAllText(configPath))?["Storage"]?["MinimumFreeDiskSpaceGB"]?.GetValue<long>() ?? 2) * 1024 * 1024 * 1024;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException) { _agentHealth = null; }
        UpdateSystemHealthStatus();
        if (SelectedDate.Date == DateTime.Today && !IsLoading &&
            (!_lastDataRefreshUtc.HasValue || DateTime.UtcNow - _lastDataRefreshUtc.Value > TimeSpan.FromSeconds(15))) _ = LoadDateDataAsync();
    }

    /// <summary>
    /// إرسال أمر تبديل حالة خدمة ويندوز غير المتزامن.
    /// </summary>
    private async Task ToggleServiceAsync()
    {
        StatusMessage = "جاري إرسال أمر تبديل حالة الخدمة...";
        var result = await _serviceManager.ToggleServiceAsync();
        StatusMessage = result;
        CheckStatuses();
    }

    /// <summary>
    /// إرسال أمر تبديل حالة عميل المراقبة غير المتزامن.
    /// </summary>
    private async Task ToggleAgentAsync()
    {
        StatusMessage = "جاري إرسال أمر تبديل حالة العميل...";
        var result = await _serviceManager.ToggleAgentAsync();
        StatusMessage = result;
        CheckStatuses();
    }

    /// <summary>
    /// تصدير التقارير اليومية لليوم المختار وعرض مسار الحفظ للمستخدم.
    /// </summary>
    private async Task ExportSelectedDayAsync()
    {
        if (_isExportingReports) return;

        _isExportingReports = true;
        var dateSnapshot = SelectedDate;
        StatusMessage = $"جاري تصدير تقارير {dateSnapshot:yyyy-MM-dd}...";
        try
        {
            var files = await _reportService.ExportSelectedDayAsync(dateSnapshot);
            StatusMessage = $"تم إنشاء {files.Count} تقارير لليوم المحدد داخل مجلد التقارير.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"تعذر تصدير التقارير: {ex.Message}";
        }
        finally
        {
            _isExportingReports = false;
        }
    }

    /// <summary>
    /// تحميل أنشطة وتسجيلات اليوم المختار من قاعدة بيانات SQLite في خيط خلفي وحساب الإحصائيات ورسم المخطط الزمني.
    /// </summary>
    public async Task LoadDateDataAsync()
    {
        if (_isDisposed) return;
        var selectedDateSnapshot = SelectedDate;
        // زيادة رقم إصدار الطلب لضمان تجاهل نتائج الطلبات القديمة المتأخرة
        var requestVersion = Interlocked.Increment(ref _loadRequestVersion);
        IsLoading = true;
        StatusMessage = $"جاري تحميل بيانات {selectedDateSnapshot:yyyy-MM-dd}...";

        try
        {
            await Task.Run(async () =>
            {
                // تحويل تاريخ اليوم المحلي إلى نطاق UTC كامل
                var (startOfDayUtc, endOfDayUtc) = ActivityTimelineMath.GetUtcDayRange(selectedDateSnapshot);
                await ActivityMonitorDbContext.InitializeDatabaseAsync();

                using var db = new ActivityMonitorDbContext();

                // 1. جلب أحداث الأنشطة المتداخلة مع هذا اليوم
                var events = await db.ActivityEvents
                    .AsNoTracking()
                    .Where(a => a.StartedAtUtc < endOfDayUtc &&
                                (a.EndedAtUtc == null || a.EndedAtUtc > startOfDayUtc))
                    .OrderBy(a => a.StartedAtUtc)
                    .ToListAsync();

                // 2. جلب مقاطع الفيديو المسجلة المتداخلة مع هذا اليوم
                var segments = await db.RecordingSegments
                    .AsNoTracking()
                    .Where(r => r.StartedAtUtc < endOfDayUtc &&
                                (r.EndedAtUtc == null || r.EndedAtUtc > startOfDayUtc))
                    .OrderBy(r => r.StartedAtUtc)
                    .ToListAsync();

                var items = new List<ActivityItemViewModel>();
                double totalActiveSeconds = 0;
                double totalIdleSeconds = 0;
                var appDurations = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);

                // معالجة كل حدث نشاط وبناء الأجزاء المرئية وحساب الإحصاءات
                foreach (var ev in events)
                {
                    var durationWithinDay = ActivityTimelineMath.CalculateOverlapSeconds(
                        ev.StartedAtUtc, ev.EndedAtUtc, ev.DurationSeconds, startOfDayUtc, endOfDayUtc);
                    var displayStartUtc = ev.StartedAtUtc < startOfDayUtc ? startOfDayUtc : ev.StartedAtUtc;
                    var resolvedEventEndUtc = ev.EndedAtUtc ??
                                              ev.StartedAtUtc.AddSeconds(Math.Max(1, ev.DurationSeconds));
                    var displayEndUtc = resolvedEventEndUtc > endOfDayUtc ? endOfDayUtc : resolvedEventEndUtc;

                    // ربط الحدث بمقاطع الفيديو المتداخلة معه زمنياً
                    var recordingParts = BuildRecordingParts(ev, segments, startOfDayUtc, endOfDayUtc);
                    var primaryRecording = recordingParts.FirstOrDefault(part => part.ExistsOnDisk) ??
                                           recordingParts.FirstOrDefault();

                    // حساب إجمالي وقت النشاط والخمول
                    if (ev.EventType == ActivityEventType.ActiveWindow)
                    {
                        totalActiveSeconds += durationWithinDay;
                        var app = ev.ProcessName;
                        appDurations[app] = appDurations.GetValueOrDefault(app) + durationWithinDay;
                    }
                    else if (ev.EventType == ActivityEventType.IdleStart)
                    {
                        totalIdleSeconds += ActivityTimelineMath.CalculateOverlapSeconds(
                            ev.StartedAtUtc, ev.EndedAtUtc, ev.DurationSeconds, startOfDayUtc, endOfDayUtc);
                    }

                    // إضافة نموذج عرض النشاط
                    items.Add(new ActivityItemViewModel
                    {
                        Id = ev.Id,
                        StartedAtUtc = displayStartUtc,
                        EndedAtUtc = displayEndUtc,
                        ProcessName = ev.ProcessName,
                        WindowTitle = ev.WindowTitle ?? string.Empty,
                        ContextPath = ev.ContextPath,
                        ProcessPath = ev.ProcessPath,
                        EventType = ev.EventType.ToString(),
                        DurationSeconds = durationWithinDay,
                        RecordingSegmentId = primaryRecording?.SegmentId,
                        RecordingFileName = primaryRecording?.FileName,
                        RecordingFilePath = primaryRecording?.FilePath,
                        VideoOffset = primaryRecording?.Offset ?? TimeSpan.Zero,
                        RecordingParts = recordingParts
                    });
                }

                // تجهيز نماذج عرض مقاطع التسجيلات
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

                // حساب التطبيق الأكثر استخداماً
                var topApp = appDurations.OrderByDescending(kv => kv.Value).FirstOrDefault().Key ?? "N/A";

                // بناء كتل الشريط الزمني الأفقي
                var timelineBlocks = BuildTimelineBlocks(items, selectedDateSnapshot);

                // فحص الملفات المفقودة من القرص
                var missingRecordingCount = segViewModels.Count(segment =>
                    !segment.ExistsOnDisk &&
                    segment.Status is not nameof(RecordingStatus.Deleted) and not nameof(RecordingStatus.DiskSpaceReclaimed));

                var (diskSpaceKnown, freeDiskSpaceBytes) = TryGetFreeDiskSpace();

                // تحديث الواجهة على خيط واجهة المستخدم الرئيسي (UI Dispatcher)
                Application.Current.Dispatcher.Invoke(() =>
                {
                    // التحقق من مطابقة الإصدار والتاريخ قبل تحديث الواجهة
                    if (_isDisposed || requestVersion != Volatile.Read(ref _loadRequestVersion) ||
                        SelectedDate != selectedDateSnapshot)
                    {
                        return;
                    }

                    Activities.Clear();
                    foreach (var it in items) Activities.Add(it);

                    Recordings.Clear();
                    foreach (var s in segViewModels) Recordings.Add(s);

                    TimelineBlocks.Clear();
                    foreach (var block in timelineBlocks) TimelineBlocks.Add(block);

                    TotalEventsCount = items.Count;
                    RecordingsCount = segViewModels.Count;
                    TopAppName = topApp;
                    _missingRecordingCount = missingRecordingCount;
                    _isDiskSpaceKnown = diskSpaceKnown;
                    _freeDiskSpaceBytes = freeDiskSpaceBytes;
                    _lastDataRefreshUtc = DateTime.UtcNow;

                    // تنسيق أوقات النشاط والخمول
                    var activeTs = TimeSpan.FromSeconds(totalActiveSeconds);
                    TotalActiveTimeDisplay = activeTs.TotalHours >= 1
                        ? $"{(int)activeTs.TotalHours}س {activeTs.Minutes}د"
                        : $"{activeTs.Minutes}د {activeTs.Seconds}ث";

                    var idleTs = TimeSpan.FromSeconds(totalIdleSeconds);
                    TotalIdleTimeDisplay = idleTs.TotalHours >= 1
                        ? $"{(int)idleTs.TotalHours}س {idleTs.Minutes}د"
                        : $"{idleTs.Minutes}د {idleTs.Seconds}ث";

                    // تطبيق شروط الفلترة الحالية
                    ApplyFilter();

                    // تحديث مؤشرات السلامة
                    UpdateSystemHealthStatus();

                    StatusMessage = $"تم تحميل {items.Count} نشاطاً و {segViewModels.Count} مقطع فيديو.";
                });
            });
        }
        catch (Exception ex)
        {
            if (requestVersion == Volatile.Read(ref _loadRequestVersion))
            {
                StatusMessage = $"خطأ أثناء تحميل البيانات: {ex.Message}";
            }
        }
        finally
        {
            if (requestVersion == Volatile.Read(ref _loadRequestVersion))
            {
                IsLoading = false;
            }
        }
    }

    /// <summary>
    /// تطبيق شروط البحث والفلترة على قائمة الأنشطة وتحديث القائمة المعروضة FilteredActivities.
    /// </summary>
    private void ApplyFilter()
    {
        FilteredActivities.Clear();
        var q = Activities.AsEnumerable();

        // تصفية نص البحث
        if (!string.IsNullOrWhiteSpace(SearchText))
        {
            var term = SearchText.Trim();
            q = q.Where(a =>
                a.ProcessName.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                a.WindowTitle.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                (a.ContextPath != null && a.ContextPath.Contains(term, StringComparison.OrdinalIgnoreCase)));
        }

        // تصفية بنوع النشاط
        q = SelectedActivityFilterKey switch
        {
            "Active" => q.Where(activity => activity.EventType == nameof(ActivityEventType.ActiveWindow)),
            "Idle" => q.Where(activity => activity.EventType == nameof(ActivityEventType.IdleStart)),
            "WithVideo" => q.Where(activity => activity.HasVideo),
            "WithoutVideo" => q.Where(activity => !activity.HasVideo),
            _ => q
        };

        // إضافة النتائج المصفاة
        foreach (var item in q)
        {
            FilteredActivities.Add(item);
        }

        OnPropertyChanged(nameof(FilteredCountDisplay));
        OnPropertyChanged(nameof(IsActivityListEmpty));
    }

    /// <summary>
    /// تشغيل الفيديو المقترن بنشاط محدد وإرسال أمر التشغيل لعنصر MediaElement.
    /// </summary>
    /// <param name="activity">عنصر النشاط المطلوب تشغيله.</param>
    public void PlayActivity(ActivityItemViewModel activity)
    {
        if (activity == null) return;

        // منع التكرار السريع في حال النقر المتتالي خلال 300 ميلي ثانية
        var now = DateTime.UtcNow;
        if (activity.Id == _lastPlayedActivityId && (now - _lastPlayRequestTime).TotalMilliseconds < 300)
        {
            return;
        }
        _lastPlayedActivityId = activity.Id;
        _lastPlayRequestTime = now;

        _playingActivity = activity;

        // البحث عن أول جزء فيديو موجود فعلياً على القرص
        _currentRecordingPartIndex = activity.RecordingParts
            .Select((part, index) => (part, index))
            .Where(item => item.part.ExistsOnDisk)
            .Select(item => item.index)
            .DefaultIfEmpty(-1)
            .First();

        // إذا وُجد جزء مسجل في قائمة الأجزاء المتعددة، نشغله فوراً
        if (_currentRecordingPartIndex >= 0)
        {
            PlayRecordingPart(activity, _currentRecordingPartIndex);
            return;
        }

        // في حال عدم وجود أجزاء متعددة، فحص المسار الفردي القديم
        if (string.IsNullOrWhiteSpace(activity.RecordingFilePath) ||
            !StoragePathGuard.TryResolvePathWithinRoot(
                activity.RecordingFilePath,
                ActivityMonitorPaths.RecordingsDirectory,
                out var safeLegacyPath,
                out _) ||
            !File.Exists(safeLegacyPath))
        {
            StatusMessage = $"ملف الفيديو الخاص بـ '{activity.ProcessName}' غير موجود على القرص.";
            CurrentRecordingPartDisplay = "لا يوجد مقطع متاح على القرص";
            NotifyRecordingNavigationChanged();
            return;
        }

        CurrentVideoPath = safeLegacyPath;
        RequestedSeekPosition = activity.VideoOffset;
        NowPlayingTitle = $"{activity.ProcessName} - {activity.WindowTitle}";
        NowPlayingSub = $"{activity.TimeRangeDisplay} | الإزاحة: {activity.OffsetDisplay} | الملف: {activity.RecordingFileName}";
        CurrentRecordingPartDisplay = "مقطع واحد";

        StatusMessage = $"جاري القفز إلى {activity.OffsetDisplay} في مقطع {activity.RecordingFileName}...";
        RequestMediaPlay?.Invoke(safeLegacyPath, activity.VideoOffset);
        IsPlaying = true;
        NotifyRecordingNavigationChanged();
    }

    /// <summary>
    /// محاولة الانتقال وتشغيل المقطع التالي التابع للنشاط الحالي إن وُجد.
    /// </summary>
    /// <returns>صحيح إذا تم الانتقال بنجاح؛ وإلا خطأ.</returns>
    public bool TryPlayNextRecordingPart()
    {
        var nextIndex = FindNextExistingPartIndex();
        if (_playingActivity == null || nextIndex < 0)
        {
            return false;
        }

        PlayRecordingPart(_playingActivity, nextIndex);
        return true;
    }

    /// <summary>
    /// تشغيل المقطع السابق التابع للنشاط الحالي إن وُجد.
    /// </summary>
    private void PlayPreviousRecordingPart()
    {
        var previousIndex = FindPreviousExistingPartIndex();
        if (_playingActivity != null && previousIndex >= 0)
        {
            PlayRecordingPart(_playingActivity, previousIndex);
        }
    }

    /// <summary>
    /// تشغيل جزء محدد بالرقم الترتيبي من قائمة أجزاء النشاط.
    /// </summary>
    /// <param name="activity">عنصر النشاط.</param>
    /// <param name="partIndex">فهرس الجزء في القائمة.</param>
    private void PlayRecordingPart(ActivityItemViewModel activity, int partIndex)
    {
        if (partIndex < 0 || partIndex >= activity.RecordingParts.Count)
        {
            return;
        }

        var part = activity.RecordingParts[partIndex];
        if (!part.ExistsOnDisk)
        {
            StatusMessage = $"المقطع '{part.FileName}' غير موجود على القرص.";
            return;
        }

        _playingActivity = activity;
        _currentRecordingPartIndex = partIndex;
        CurrentVideoPath = part.FilePath;
        RequestedSeekPosition = part.Offset;
        CurrentRecordingPartDisplay = part.Display;
        NowPlayingTitle = $"{activity.ProcessName} - {activity.WindowTitle}";
        NowPlayingSub = $"{activity.TimeRangeDisplay} | الإزاحة: {part.Offset:mm\\:ss} | مدة الجزء: {FormatCompactDuration(part.OverlapDuration)}";
        StatusMessage = $"جاري تشغيل {part.Display}.";
        RequestMediaPlay?.Invoke(part.FilePath, part.Offset);
        IsPlaying = true;
        NotifyRecordingNavigationChanged();
    }

    /// <summary>
    /// البحث عن فهرس الجزء السابق المتوفر على القرص.
    /// </summary>
    private int FindPreviousExistingPartIndex()
    {
        if (_playingActivity == null) return -1;
        for (var index = _currentRecordingPartIndex - 1; index >= 0; index--)
        {
            if (_playingActivity.RecordingParts[index].ExistsOnDisk) return index;
        }
        return -1;
    }

    /// <summary>
    /// البحث عن فهرس الجزء التالي المتوفر على القرص.
    /// </summary>
    private int FindNextExistingPartIndex()
    {
        if (_playingActivity == null) return -1;
        for (var index = _currentRecordingPartIndex + 1; index < _playingActivity.RecordingParts.Count; index++)
        {
            if (_playingActivity.RecordingParts[index].ExistsOnDisk) return index;
        }
        return -1;
    }

    /// <summary>
    /// إشعار واجهة المستخدم بتحديث إمكانية التنقل بين أجزاء التسجيل (السابق / التالي).
    /// </summary>
    private void NotifyRecordingNavigationChanged()
    {
        OnPropertyChanged(nameof(CanPlayPreviousRecordingPart));
        OnPropertyChanged(nameof(CanPlayNextRecordingPart));
    }

    /// <summary>
    /// بناء قائمة أجزاء الفيديو المتداخلة مع نشاط معين وتأمين مساراتها على القرص.
    /// </summary>
    /// <param name="activity">كيان النشاط.</param>
    /// <param name="segments">قائمة مقاطع الفيديو المتاحة.</param>
    /// <returns>قائمة بأجزاء التسجيل المنسقة للعرض.</returns>
    private static IReadOnlyList<ActivityRecordingPartViewModel> BuildRecordingParts(
        ActivityMonitor.Core.Entities.ActivityEvent activity, IReadOnlyList<RecordingSegment> segments,
        DateTime? rangeStartUtc = null, DateTime? rangeEndUtc = null)
    {
        var locations = RecordingTimeline.Resolve(activity, segments, DateTime.UtcNow, rangeStartUtc, rangeEndUtc);
        return locations.Select((part, index) => new ActivityRecordingPartViewModel {
            SegmentId = part.RecordingSegmentId, PartNumber = index + 1, TotalParts = locations.Count,
            FileName = part.FileName,
            FilePath = StoragePathGuard.TryResolvePathWithinRoot(part.FilePath, ActivityMonitorPaths.RecordingsDirectory,
                out var safePath, out _) ? safePath : string.Empty,
            Offset = part.Offset, OverlapDuration = part.Duration
        }).ToList();
    }

    /// <summary>
    /// حساب مواضع وأبعاد كتل الأنشطة على المخطط الزمني الأفقي (Timeline) بعرض ثابت 960 بكسل.
    /// </summary>
    /// <param name="activities">قائمة الأنشطة.</param>
    /// <param name="selectedDate">التاريخ المحدد.</param>
    /// <returns>قائمة كتل المخطط الزمني جاهزة للعرض.</returns>
    private static List<TimelineBlockViewModel> BuildTimelineBlocks(
        IReadOnlyList<ActivityItemViewModel> activities,
        DateTime selectedDate)
    {
        const double timelineWidth = 960;
        var localDayStart = selectedDate.Date;
        var blocks = new List<TimelineBlockViewModel>(activities.Count);

        foreach (var activity in activities)
        {
            var localStart = activity.StartedAtUtc.ToLocalTime();
            var localEnd = (activity.EndedAtUtc ?? activity.StartedAtUtc).ToLocalTime();

            // حساب موقع الدقيقة خلال اليوم من 0 إلى 1440
            var startMinutes = Math.Clamp((localStart - localDayStart).TotalMinutes, 0, 1_440);
            var endMinutes = Math.Clamp((localEnd - localDayStart).TotalMinutes, 0, 1_440);
            var durationMinutes = Math.Max(0, endMinutes - startMinutes);

            blocks.Add(new TimelineBlockViewModel
            {
                Activity = activity,
                Left = startMinutes / 1_440d * timelineWidth,
                Width = Math.Max(3, durationMinutes / 1_440d * timelineWidth),
                Color = activity.AppBadgeColor,
                ToolTip = $"{activity.TimeRangeDisplay} • {activity.ProcessName} • {activity.DurationDisplay}"
            });
        }

        return blocks;
    }

    /// <summary>
    /// محاولة استعلام مساحة القرص الفارغة لوحدة التخزين التي تستضيف مجلد البرنامج.
    /// </summary>
    /// <returns>زوج من القيم يوضح نجاح القراءة والمساحة الفارغة بالبايت.</returns>
    private static (bool IsKnown, long FreeBytes) TryGetFreeDiskSpace()
    {
        try
        {
            var root = Path.GetPathRoot(ActivityMonitorPaths.RootDirectory);
            if (string.IsNullOrWhiteSpace(root)) return (false, 0);
            return (true, new DriveInfo(root).AvailableFreeSpace);
        }
        catch
        {
            return (false, 0);
        }
    }

    /// <summary>
    /// تحديث نصوص ومؤشرات سلامة النظام بناءً على حالة الخدمات ومساحة القرص والملفات المفقودة.
    /// </summary>
    private void UpdateSystemHealthStatus()
    {
        if (App.IsReviewOnlyMode)
        {
            IsSystemHealthy = false;
            SystemHealthStatusText = "وضع مراجعة الأرشيف";
            SystemHealthDetailText = ActivityMonitorPaths.RootDirectory;
            return;
        }
        var issues = new List<string>();
        // User maintenance runs inside the Agent; the legacy archive service is independent.
        if (!IsAgentRunning) issues.Add("العميل متوقف");
        else if (_agentHealth == null || DateTime.UtcNow - _agentHealth.UpdatedAtUtc > TimeSpan.FromSeconds(20)) issues.Add("نبضة العميل قديمة أو غير متاحة");
        else if (_agentHealth.RecordingRequested && !_agentHealth.IsRecording) issues.Add("التسجيل مطلوب لكنه لا يعمل");
        else if (!string.IsNullOrWhiteSpace(_agentHealth.Error) && _agentHealth.RecordingRequested) issues.Add("توجد مشكلة في التسجيل");
        if (_missingRecordingCount > 0) issues.Add($"{_missingRecordingCount} ملف مفقود");
        if (!_isDiskSpaceKnown) issues.Add("مساحة القرص غير معروفة");
        else if (_freeDiskSpaceBytes < _minimumFreeBytes) issues.Add("مساحة القرص منخفضة");

        IsSystemHealthy = issues.Count == 0;
        SystemHealthStatusText = IsSystemHealthy ? "الحالة سليمة" : "توجد ملاحظات";

        var diskText = _isDiskSpaceKnown
            ? $"متاح {_freeDiskSpaceBytes / (1024d * 1024d * 1024d):F1} GB"
            : "تعذر قراءة القرص";
        var refreshText = _lastDataRefreshUtc.HasValue
            ? $"آخر تحديث {_lastDataRefreshUtc.Value.ToLocalTime():HH:mm:ss}"
            : "لم تُحمّل البيانات بعد";
        SystemHealthDetailText = issues.Count == 0
            ? $"{diskText} • {refreshText}"
            : $"{string.Join("، ", issues)} • {diskText}";
    }

    /// <summary>
    /// تنسيق مدة زمنية قصيرة بصيغة مختصرة (مثال: 1س 20د أو 05د 30ث).
    /// </summary>
    private static string FormatCompactDuration(TimeSpan duration) =>
        duration.TotalHours >= 1
            ? $"{(int)duration.TotalHours}س {duration.Minutes}د"
            : $"{duration.Minutes}د {duration.Seconds}ث";

    /// <summary>
    /// تبديل حالة مشغل الفيديو بين التشغيل والإيقاف المؤقت.
    /// </summary>
    private void TogglePlayPause()
    {
        if (string.IsNullOrWhiteSpace(CurrentVideoPath) || !File.Exists(CurrentVideoPath))
        {
            IsPlaying = false;
            StatusMessage = "اختر نشاطاً يحتوي على تسجيل فيديو أولاً.";
            return;
        }

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

    /// <summary>
    /// تصفير حالة مشغل الفيديو بالكامل وإيقاف التشغيل ومسح المسارات عند تغيير اليوم.
    /// </summary>
    private void ResetPlayback()
    {
        RequestMediaStop?.Invoke();
        IsPlaying = false;
        CurrentVideoPath = null;
        RequestedSeekPosition = TimeSpan.Zero;
        CurrentVideoPosition = TimeSpan.Zero;
        VideoDuration = TimeSpan.Zero;
        SelectedActivity = null;
        _playingActivity = null;
        _currentRecordingPartIndex = -1;
        CurrentRecordingPartDisplay = "لا توجد مقاطع مرتبطة";
        NowPlayingTitle = "لا يوجد فيديو قيد التشغيل";
        NowPlayingSub = "حدد نشاطاً من القائمة الجانبية لبدء المشاهدة";
        NotifyRecordingNavigationChanged();
    }

    /// <summary>
    /// القفز في الفيديو بقيمة نسبية (للأمام أو الخلف).
    /// </summary>
    /// <param name="offset">مقدار القفز الزمني (موجب للأمام، سالب للخلف).</param>
    private void JumpRelative(TimeSpan offset)
    {
        var target = CurrentVideoPosition + offset;
        if (target < TimeSpan.Zero) target = TimeSpan.Zero;
        if (target > VideoDuration && VideoDuration > TimeSpan.Zero) target = VideoDuration;
        RequestMediaSeek?.Invoke(target);
    }

    /// <summary>
    /// فتح مجلد التقارير في مستكشف الملفات.
    /// </summary>
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

    /// <summary>
    /// فتح مجلد التسجيلات في مستكشف الملفات.
    /// </summary>
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

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;
        Interlocked.Increment(ref _loadRequestVersion);
        _statusPollTimer.Stop();
        Settings.StopAudioMonitoring();
        IsLoading = false;
    }
}
