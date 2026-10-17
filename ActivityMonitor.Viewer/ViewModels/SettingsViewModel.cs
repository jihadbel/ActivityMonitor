using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using System.Windows.Threading;
using ActivityMonitor.Viewer.Common;
using ActivityMonitor.Viewer.Services;

namespace ActivityMonitor.Viewer.ViewModels;

/// <summary>
/// نموذج عرض نافذة الإعدادات (Settings ViewModel).
/// يدير خيارات تسجيل الشاشة والصوت، ومقياس الصوت اللحظي، وتتبع النشاط، والتخزين، وبدء التشغيل مع ويندوز.
/// </summary>
public class SettingsViewModel : ViewModelBase
{
    // خدمة إدارة وحفظ الإعدادات في ملف JSON المشترك
    private readonly SettingsService _settingsService;

    // مدير خدمات وعمليات ويندوز للتحكم في الإقلاع التلقائي
    private readonly WindowsServiceManager _serviceManager;

    // ==========================================
    // الحقول الخلفية للخصائص (Backing Fields)
    // ==========================================
    private bool _screenRecordingEnabled = true;
    private bool _recordOnlyWhenUserActive = true;
    private bool _recordMicrophone = false;
    private bool _recordSystemAudio = false;
    private bool _isFragmentedMp4Enabled = true;
    private int _segmentMinutes = 10;
    private int _frameRate = 15;
    private int _videoQuality = 60;
    private int _retentionDays = 14;
    private int _activityRetentionDays = 90, _reportRetentionDays = 90, _logRetentionDays = 30;
    public int ActivityRetentionDays { get => _activityRetentionDays; set => SetProperty(ref _activityRetentionDays, value); }
    public int ReportRetentionDays { get => _reportRetentionDays; set => SetProperty(ref _reportRetentionDays, value); }
    public int LogRetentionDays { get => _logRetentionDays; set => SetProperty(ref _logRetentionDays, value); }

    private bool _activityTrackingEnabled = true;
    private int _idleThresholdMinutes = 5;

    private bool _startServiceWithWindows = true;
    private bool _startAgentAtLogon = true;

    private bool _isSaving;
    private string _settingsStatusMessage = string.Empty;

    // قائمة أجهزة التقاط الصوت (الميكروفونات) المتاحة
    private readonly ObservableCollection<AudioDeviceInfo> _microphoneDevices = new();

    /// <summary>
    /// مجموعة أجهزة الميكروفون المكتشفة في النظام لعرضها في القائمة المنسدلة.
    /// </summary>
    public ObservableCollection<AudioDeviceInfo> MicrophoneDevices => _microphoneDevices;

    private AudioDeviceInfo? _selectedMicrophoneDevice;

    /// <summary>
    /// جهاز الميكروفون المختار حالياً للتسجيل والاختبار.
    /// </summary>
    public AudioDeviceInfo? SelectedMicrophoneDevice
    {
        get => _selectedMicrophoneDevice;
        set
        {
            if (SetProperty(ref _selectedMicrophoneDevice, value))
            {
                // إذا كانت المراقبة الصوتية نشطة، يتم إعادة تشغيل الجلسة الصوتية بالجهاز الجديد فوراً
                if (_isAudioMonitoringActive)
                {
                    RestartAudioSession();
                }
            }
        }
    }

    private float _microphoneVolume = 1.0f;

    /// <summary>
    /// معامل تضخيم/ضبط مستوى صوت الميكروفون (من 0.0 إلى 2.0).
    /// </summary>
    public float MicrophoneVolume
    {
        get => _microphoneVolume;
        set
        {
            if (SetProperty(ref _microphoneVolume, value))
            {
                OnPropertyChanged(nameof(MicrophoneVolumePercentDisplay));
            }
        }
    }

    /// <summary>
    /// العرض النصي لنسبة مستوى صوت الميكروفون (مثال: 100%).
    /// </summary>
    public string MicrophoneVolumePercentDisplay => $"{(int)(MicrophoneVolume * 100)}%";

    private double _audioPeakLevel;

    /// <summary>
    /// القيمة اللحظية لقمة مستوى الصوت (Peak Level) كنسبة مئوية من 0 إلى 100 لشريط المقياس في الواجهة.
    /// </summary>
    public double AudioPeakLevel
    {
        get => _audioPeakLevel;
        set => SetProperty(ref _audioPeakLevel, value);
    }

    private string _audioStatusText = "⚪ الميكروفون في انتظار الاختبار";

    /// <summary>
    /// النص التوضيحي لحالة اتصال الميكروفون واستقبال الصوت.
    /// </summary>
    public string AudioStatusText
    {
        get => _audioStatusText;
        set => SetProperty(ref _audioStatusText, value);
    }

    private bool _isMicrophoneReceivingAudio;

    /// <summary>
    /// هل الميكروفون يستقبل صوتاً فعالاً في اللحظة الحالية (تجاوز عتبة الضجيج المنخفضة).
    /// </summary>
    public bool IsMicrophoneReceivingAudio
    {
        get => _isMicrophoneReceivingAudio;
        set => SetProperty(ref _isMicrophoneReceivingAudio, value);
    }

    // مؤقت التحديث الدوري لمقياس الصوت
    private DispatcherTimer? _audioTimer;

    // الجلسة الصوتية الحية عبر WASAPI
    private AudioMeterService.LiveAudioSession? _audioSession;

    // هل وضع اختبار الصوت نشط حالياً (نافذة الإعدادات مفتوحة)
    private bool _isAudioMonitoringActive;

    // المعرف المخزن مسبقاً للميكروفون للمطابقة عند الفتح
    private string? _savedSelectedMicId;

    /// <summary>
    /// تفعيل أو تعطيل تسجيل الشاشة بالفيديو.
    /// </summary>
    public bool ScreenRecordingEnabled
    {
        get => _screenRecordingEnabled;
        set => SetProperty(ref _screenRecordingEnabled, value);
    }

    /// <summary>
    /// حصر التسجيل أثناء تفاعل المستخدم فقط وتجاوز فترات الخمول.
    /// </summary>
    public bool RecordOnlyWhenUserActive
    {
        get => _recordOnlyWhenUserActive;
        set => SetProperty(ref _recordOnlyWhenUserActive, value);
    }

    /// <summary>
    /// تفعيل أو تعطيل تسجيل صوت الميكروفون.
    /// </summary>
    public bool RecordMicrophone
    {
        get => _recordMicrophone;
        set
        {
            if (!SetProperty(ref _recordMicrophone, value) || !_isAudioMonitoringActive)
            {
                return;
            }

            // بدء أو إيقاف جلسة اختبار الصوت بالتزامن مع حالة التبديل
            if (value)
            {
                RestartAudioSession();
                _audioTimer?.Start();
            }
            else
            {
                StopAudioSession();
            }
        }
    }

    /// <summary>
    /// تفعيل أو تعطيل تسجيل أصوات النظام الداخلية (Loopback Audio).
    /// </summary>
    public bool RecordSystemAudio
    {
        get => _recordSystemAudio;
        set => SetProperty(ref _recordSystemAudio, value);
    }

    /// <summary>
    /// تفعيل تسجيل ملفات الفيديو بنمط Fragmented MP4 لضمان سلامتها عند الإغلاق المفاجئ.
    /// </summary>
    public bool IsFragmentedMp4Enabled
    {
        get => _isFragmentedMp4Enabled;
        set => SetProperty(ref _isFragmentedMp4Enabled, value);
    }

    /// <summary>
    /// مدة كل مقطع فيديو بالدقائق.
    /// </summary>
    public int SegmentMinutes
    {
        get => _segmentMinutes;
        set
        {
            if (SetProperty(ref _segmentMinutes, value))
            {
                OnPropertyChanged(nameof(SelectedSegmentMinutesIndex));
            }
        }
    }

    /// <summary>
    /// معدل إطارات تسجيل الفيديو في الثانية (FPS).
    /// </summary>
    public int FrameRate
    {
        get => _frameRate;
        set
        {
            if (SetProperty(ref _frameRate, value))
            {
                OnPropertyChanged(nameof(SelectedFrameRateIndex));
            }
        }
    }

    /// <summary>
    /// مقياس جودة ضغط الفيديو.
    /// </summary>
    public int VideoQuality
    {
        get => _videoQuality;
        set
        {
            if (SetProperty(ref _videoQuality, value))
            {
                OnPropertyChanged(nameof(SelectedVideoQualityIndex));
            }
        }
    }

    /// <summary>
    /// عدد أيام الاحتفاظ بالبيانات والتسجيلات قبل الحذف الدوري التلقائي.
    /// </summary>
    public int RetentionDays
    {
        get => _retentionDays;
        set
        {
            if (SetProperty(ref _retentionDays, value))
            {
                OnPropertyChanged(nameof(SelectedRetentionDaysIndex));
            }
        }
    }

    /// <summary>
    /// تفعيل أو تعطيل تتبع الأنشطة والنوافذ النشطة.
    /// </summary>
    public bool ActivityTrackingEnabled
    {
        get => _activityTrackingEnabled;
        set => SetProperty(ref _activityTrackingEnabled, value);
    }

    /// <summary>
    /// عتبة الخمول بالدقائق التي بعدها يتم احتساب فترة خمول المستخدم.
    /// </summary>
    public int IdleThresholdMinutes
    {
        get => _idleThresholdMinutes;
        set
        {
            if (SetProperty(ref _idleThresholdMinutes, value))
            {
                OnPropertyChanged(nameof(SelectedIdleMinutesIndex));
            }
        }
    }

    /// <summary>
    /// الفهرس المحدد في القائمة المنسدلة لمدة المقطع بالدقائق.
    /// </summary>
    public int SelectedSegmentMinutesIndex
    {
        get => SegmentMinutes switch
        {
            1 => 0,
            2 => 1,
            5 => 2,
            10 => 3,
            15 => 4,
            _ => 3
        };
        set
        {
            SegmentMinutes = value switch
            {
                0 => 1,
                1 => 2,
                2 => 5,
                3 => 10,
                4 => 15,
                _ => 10
            };
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// الفهرس المحدد في القائمة المنسدلة لمعدل الإطارات في الثانية.
    /// </summary>
    public int SelectedFrameRateIndex
    {
        get => FrameRate switch
        {
            10 => 0,
            15 => 1,
            30 => 2,
            _ => 1
        };
        set
        {
            FrameRate = value switch
            {
                0 => 10,
                1 => 15,
                2 => 30,
                _ => 15
            };
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// الفهرس المحدد في القائمة المنسدلة لجودة الفيديو.
    /// </summary>
    public int SelectedVideoQualityIndex
    {
        get => VideoQuality switch
        {
            50 => 0,
            60 => 1,
            75 => 2,
            _ => 1
        };
        set
        {
            VideoQuality = value switch
            {
                0 => 50,
                1 => 60,
                2 => 75,
                _ => 60
            };
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// الفهرس المحدد في القائمة المنسدلة لعتبة دقائق الخمول.
    /// </summary>
    public int SelectedIdleMinutesIndex
    {
        get => IdleThresholdMinutes switch
        {
            1 => 0,
            2 => 1,
            3 => 2,
            5 => 3,
            10 => 4,
            15 => 5,
            30 => 6,
            _ => 3
        };
        set
        {
            IdleThresholdMinutes = value switch
            {
                0 => 1,
                1 => 2,
                2 => 3,
                3 => 5,
                4 => 10,
                5 => 15,
                6 => 30,
                _ => 5
            };
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// الفهرس المحدد في القائمة المنسدلة لأيام الاحتفاظ بالتسجيلات.
    /// </summary>
    public int SelectedRetentionDaysIndex
    {
        get => RetentionDays switch
        {
            7 => 0,
            14 => 1,
            30 => 2,
            60 => 3,
            _ => 1
        };
        set
        {
            RetentionDays = value switch
            {
                0 => 7,
                1 => 14,
                2 => 30,
                3 => 60,
                _ => 14
            };
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// بدء خدمة ويندوز تلقائياً مع تشغيل الجهاز.
    /// </summary>
    public bool StartServiceWithWindows
    {
        get => _startServiceWithWindows;
        set => SetProperty(ref _startServiceWithWindows, value);
    }

    /// <summary>
    /// بدء عميل المراقبة تلقائياً عند تسجيل دخول المستخدم.
    /// </summary>
    public bool StartAgentAtLogon
    {
        get => _startAgentAtLogon;
        set => SetProperty(ref _startAgentAtLogon, value);
    }

    /// <summary>
    /// حالة تنفيذ عملية الحفظ (تُستخدم لإظهار مؤشر التحميل وتعطيل الأزرار).
    /// </summary>
    public bool IsSaving
    {
        get => _isSaving;
        set => SetProperty(ref _isSaving, value);
    }

    /// <summary>
    /// رسالة التنبيه أو الحالة المعروضة في أسفل نافذة الإعدادات.
    /// </summary>
    public string SettingsStatusMessage
    {
        get => _settingsStatusMessage;
        set => SetProperty(ref _settingsStatusMessage, value);
    }

    // ==========================================
    // الأوامر (Commands)
    // ==========================================

    /// <summary>
    /// أمر حفظ وتطبيق الإعدادات المعدلة.
    /// </summary>
    public ICommand SaveSettingsCommand { get; }

    /// <summary>
    /// أمر فتح مجلد بيانات البرنامج العام في مستكشف الملفات.
    /// </summary>
    public ICommand OpenProgramDataFolderCommand { get; }

    /// <summary>
    /// أمر فتح مجلد التسجيلات المرئية في مستكشف الملفات.
    /// </summary>
    public ICommand OpenRecordingsFolderCommand { get; }

    /// <summary>
    /// أمر فتح مجلد التقارير المصدرة في مستكشف الملفات.
    /// </summary>
    public ICommand OpenReportsFolderCommand { get; }

    /// <summary>
    /// أمر فتح مجلد السجلات في مستكشف الملفات.
    /// </summary>
    public ICommand OpenLogsFolderCommand { get; }

    /// <summary>
    /// تهيئة نموذج عرض الإعدادات وربط الأوامر بالمسارات والإجراءات التنفيذية.
    /// </summary>
    /// <param name="serviceManager">مدير خدمات ويندوز المشترك.</param>
    public SettingsViewModel(WindowsServiceManager serviceManager)
    {
        _serviceManager = serviceManager;
        _settingsService = new SettingsService(serviceManager);

        // ربط أوامر الحفظ وفتح المجلدات
        SaveSettingsCommand = new RelayCommand(async () => await SaveSettingsAsync());
        OpenProgramDataFolderCommand = new RelayCommand(() => OpenFolder(ActivityMonitor.Core.Options.ActivityMonitorPaths.RootDirectory));
        OpenRecordingsFolderCommand = new RelayCommand(() => OpenFolder(ActivityMonitor.Core.Options.ActivityMonitorPaths.RecordingsDirectory));
        OpenReportsFolderCommand = new RelayCommand(() => OpenFolder(ActivityMonitor.Core.Options.ActivityMonitorPaths.ReportsDirectory));
        OpenLogsFolderCommand = new RelayCommand(() => OpenFolder(ActivityMonitor.Core.Options.ActivityMonitorPaths.LogsDirectory));
    }

    /// <summary>
    /// تحميل الإعدادات الحالية من ملف التكوين وقراءة حالة خدمات ويندوز.
    /// </summary>
    public async Task LoadSettingsAsync()
    {
        SettingsStatusMessage = "جاري قراءة الإعدادات...";
        var model = await _settingsService.LoadSettingsAsync();

        // تطبيق خيارات التسجيل
        ScreenRecordingEnabled = model.ScreenRecordingEnabled;
        RecordOnlyWhenUserActive = model.RecordOnlyWhenUserActive;
        RecordMicrophone = model.RecordMicrophone;
        RecordSystemAudio = model.RecordSystemAudio;
        IsFragmentedMp4Enabled = model.IsFragmentedMp4Enabled;
        SegmentMinutes = model.SegmentMinutes;
        FrameRate = model.FrameRate;
        VideoQuality = model.VideoQuality;
        RetentionDays = model.RetentionDays;
        ActivityRetentionDays = model.ActivityRetentionDays;
        ReportRetentionDays = model.ReportRetentionDays;
        LogRetentionDays = model.LogRetentionDays;

        // تطبيق خيارات التتبع
        ActivityTrackingEnabled = model.ActivityTrackingEnabled;
        IdleThresholdMinutes = model.IdleThresholdMinutes;

        // تطبيق خيارات البدء التلقائي
        StartServiceWithWindows = model.StartServiceWithWindows;
        StartAgentAtLogon = model.StartAgentAtLogon;

        // حفظ معرّف الميكروفون المختار لربطه بالقائمة
        _savedSelectedMicId = model.SelectedMicrophoneDeviceName;
        MicrophoneVolume = model.MicrophoneVolume > 0 ? model.MicrophoneVolume : 1.0f;

        // إشعار الواجهة بتحديث فهارس القوائم المنسدلة
        OnPropertyChanged(nameof(SelectedSegmentMinutesIndex));
        OnPropertyChanged(nameof(SelectedFrameRateIndex));
        OnPropertyChanged(nameof(SelectedVideoQualityIndex));
        OnPropertyChanged(nameof(SelectedIdleMinutesIndex));
        OnPropertyChanged(nameof(SelectedRetentionDaysIndex));

        SettingsStatusMessage = string.Empty;
    }

    /// <summary>
    /// حفظ وتطبيق الإعدادات المعدلة مع إعادة تشغيل العميل إن كان مطلوباً لتطبيق التغييرات.
    /// </summary>
    public async Task SaveSettingsAsync()
    {
        if (IsSaving) return;
        IsSaving = true;
        SettingsStatusMessage = "جاري حفظ وتطبيق الإعدادات...";

        // تجهيز نموذج الإعدادات بالبيانات الحالية للواجهة
        var model = new AppSettingsModel
        {
            ScreenRecordingEnabled = ScreenRecordingEnabled,
            RecordOnlyWhenUserActive = RecordOnlyWhenUserActive,
            RecordMicrophone = RecordMicrophone,
            RecordSystemAudio = RecordSystemAudio,
            SelectedMicrophoneDeviceName = SelectedMicrophoneDevice?.Id,
            SelectedMicrophoneFriendlyName = SelectedMicrophoneDevice?.DisplayName,
            MicrophoneVolume = MicrophoneVolume,
            IsFragmentedMp4Enabled = IsFragmentedMp4Enabled,
            SegmentMinutes = SegmentMinutes,
            FrameRate = FrameRate,
            VideoQuality = VideoQuality,
            RetentionDays = RetentionDays,
            ActivityRetentionDays = ActivityRetentionDays,
            ReportRetentionDays = ReportRetentionDays,
            LogRetentionDays = LogRetentionDays,
            ActivityTrackingEnabled = ActivityTrackingEnabled,
            IdleThresholdMinutes = IdleThresholdMinutes,
            StartServiceWithWindows = StartServiceWithWindows,
            StartAgentAtLogon = StartAgentAtLogon
        };

        // تنفيذ الحفظ وتطبيق الإعدادات
        var result = await _settingsService.SaveSettingsAsync(model, restartAgentIfNeeded: true);
        SettingsStatusMessage = result;
        IsSaving = false;
    }

    /// <summary>
    /// بدء مراقبة واختبار الميكروفون عند فتح نافذة الإعدادات.
    /// </summary>
    public void StartAudioMonitoring()
    {
        _isAudioMonitoringActive = true;
        RefreshMicrophoneDevices();

        // تهيئة مؤقت نبضات المقياس الصوتي بمعدل 50 ميلي ثانية (20 تحديثاً في الثانية)
        if (_audioTimer == null)
        {
            _audioTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(50)
            };
            _audioTimer.Tick += OnAudioMeterTick;
        }

        // إذا كان تسجيل الميكروفون مفعلاً، نبدأ جلسة المراقبة فوراً
        if (RecordMicrophone)
        {
            RestartAudioSession();
            _audioTimer.Start();
        }
        else
        {
            StopAudioSession();
            AudioStatusText = "⚪ فعّل تسجيل الميكروفون لاختبار مستوى الصوت";
        }
    }

    /// <summary>
    /// إيقاف مراقبة الصوت وتحرير جلسة WASAPI عند إغلاق نافذة الإعدادات لتوفير موارد المعالج.
    /// </summary>
    public void StopAudioMonitoring()
    {
        _isAudioMonitoringActive = false;
        StopAudioSession();
        AudioStatusText = "⚪ متوقف";
    }

    /// <summary>
    /// إنهاء الجلسة الصوتية الحية وإيقاف المؤقت وتصفير مؤشرات المقياس.
    /// </summary>
    private void StopAudioSession()
    {
        _audioTimer?.Stop();
        _audioSession?.Dispose();
        _audioSession = null;
        AudioPeakLevel = 0;
        IsMicrophoneReceivingAudio = false;
    }

    /// <summary>
    /// تحديث قائمة أجهزة الميكروفون المكتشفة في النظام ومطابقة الجهاز المحفوظ مسبقاً.
    /// </summary>
    private void RefreshMicrophoneDevices()
    {
        var devices = AudioMeterService.GetCaptureDevices();
        MicrophoneDevices.Clear();
        foreach (var d in devices) MicrophoneDevices.Add(d);

        // مطابقة الاختيار السابق بالمعرف الفريد أو اعتماد الميكروفون الافتراضي الأول
        var matched = MicrophoneDevices.FirstOrDefault(d =>
            !string.IsNullOrEmpty(_savedSelectedMicId) &&
            string.Equals(d.Id, _savedSelectedMicId, StringComparison.OrdinalIgnoreCase))
            ?? MicrophoneDevices.FirstOrDefault();

        _selectedMicrophoneDevice = matched;
        OnPropertyChanged(nameof(SelectedMicrophoneDevice));
    }

    /// <summary>
    /// إعادة إنشاء الجلسة الصوتية الحية للجهاز المختار حالياً.
    /// </summary>
    private void RestartAudioSession()
    {
        // التخلص من الجلسة السابقة
        _audioSession?.Dispose();
        _audioSession = null;

        try
        {
            // بناء جلسة صوتية جديدة
            _audioSession = AudioMeterService.CreateLiveSession(SelectedMicrophoneDevice?.Id);
            if (_audioSession != null)
            {
                AudioStatusText = "🟢 الميكروفون متصل وجاهز - تحدث للاختبار";
            }
            else
            {
                AudioStatusText = "⚠️ تعذر الاتصال بجهاز الميكروفون المحدد";
            }
        }
        catch (Exception ex)
        {
            AudioStatusText = $"خطأ: {ex.Message}";
        }
    }

    /// <summary>
    /// معالج حدث نبضة مؤقت فحص مستوى الصوت وتحديث شريط القياس بسلاسة مع انحدار طبيعي (Smooth decay).
    /// </summary>
    private void OnAudioMeterTick(object? sender, EventArgs e)
    {
        if (_audioSession == null)
        {
            AudioPeakLevel = 0;
            IsMicrophoneReceivingAudio = false;
            return;
        }

        // قراءة ذروة الصوت الخام ومضاعفتها بمعامل الصوت
        float rawPeak = _audioSession.GetPeakLevel();
        float scaled = Math.Clamp(rawPeak * MicrophoneVolume, 0f, 1f);
        double targetPercent = scaled * 100.0;

        // تطبيق تأثير التلاشي السلس (Decaying bounce) لراحة العين بدلاً من القفزات الحادة
        if (targetPercent < AudioPeakLevel)
        {
            AudioPeakLevel = Math.Max(0, AudioPeakLevel * 0.85);
        }
        else
        {
            AudioPeakLevel = targetPercent;
        }

        // تحديد ما إذا كان الصوت المستلم أعلى من عتبة السكون (2%)
        if (AudioPeakLevel > 2.0)
        {
            IsMicrophoneReceivingAudio = true;
            AudioStatusText = $"🟢 نشط - يستقبل الصوت المحيط ({AudioPeakLevel:F0}%)";
        }
        else
        {
            IsMicrophoneReceivingAudio = false;
            AudioStatusText = "⚪ متصل - في انتظار صوت...";
        }
    }

    /// <summary>
    /// فتح مجلد محدد على القرص في مستكشف ويندوز (Windows Explorer).
    /// </summary>
    /// <param name="path">المسار المطلوب فتحه.</param>
    private static void OpenFolder(string path)
    {
        try
        {
            // إنشاء المجلد إذا لم يكن موجوداً
            if (!Directory.Exists(path)) Directory.CreateDirectory(path);

            // تشغيل مستكشف الملفات
            Process.Start(new ProcessStartInfo("explorer.exe", path) { UseShellExecute = true });
        }
        catch { }
    }
}
