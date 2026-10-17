using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using ActivityMonitor.Core.Entities;
using ActivityMonitor.Core.Enums;
using ActivityMonitor.Core.Interfaces;
using ActivityMonitor.Core.Options;
using ActivityMonitor.Infrastructure.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ScreenRecorderLib;

namespace ActivityMonitor.Agent.Recording;

/// <summary>
/// فئة مسؤولة عن إدارة خدمة تسجيل الشاشة بالفيديو، والتقاط الصوت،
/// وتجزئة الفيديو إلى مقاطع دورية، واسترداد التسجيلات التالفة أو المعلقة.
/// </summary>
public class ScreenRecordingService : IScreenRecordingService, IDisposable
{
    /// <summary>
    /// الحالات التشغيلية لدورة حياة مسجل الشاشة.
    /// </summary>
    private enum RecordingLifecycleState
    {
        /// <summary>
        /// متوقف تماماً عن التسجيل.
        /// </summary>
        Stopped = 0,

        /// <summary>
        /// جاري بدء عملية التسجيل وإعداد الترميز.
        /// </summary>
        Starting = 1,

        /// <summary>
        /// قيد التسجيل الفعلي النشط حالياً.
        /// </summary>
        Recording = 2,

        /// <summary>
        /// جاري إنهاء وحفظ مقطع التسجيل.
        /// </summary>
        Stopping = 3,

        /// <summary>
        /// حالة عطل أو فشل مؤقت في التسجيل بانتظار إعادة المحاولة.
        /// </summary>
        Faulted = 4
    }

    /// <summary>
    /// مستودع حفظ وتحديث بيانات مقاطع التسجيل.
    /// </summary>
    private readonly IRecordingRepository _recordingRepository;

    /// <summary>
    /// خدمة إدارة التخزين لفحص المساحة المتاحة قبل التسجيل.
    /// </summary>
    private readonly IStorageManagementService _storageService;

    /// <summary>
    /// مراقب خيارات التسجيل لدعم التحديث المباشر للإعدادات.
    /// </summary>
    private readonly IOptionsMonitor<RecordingOptions> _optionsMonitor;

    /// <summary>
    /// مسجل السجلات التشخيصية للخدمة.
    /// </summary>
    private readonly ILogger<ScreenRecordingService> _logger;

    /// <summary>
    /// قراءة الإعدادات الحالية لخيارات التسجيل.
    /// </summary>
    private RecordingOptions Options => _optionsMonitor.CurrentValue;

    /// <summary>
    /// قفل ثنائي خفيف لتأمين بدء وإيقاف التسجيل ومنع تداخل الخيوط.
    /// </summary>
    private readonly SemaphoreSlim _lock = new(1, 1);

    /// <summary>
    /// كائن مسجل الشاشة من مكتبة ScreenRecorderLib.
    /// </summary>
    private Recorder? _recorder;
    private readonly string _instanceId = Guid.NewGuid().ToString("N");
    private readonly Stopwatch _segmentClock = new();
    private readonly IOptionsMonitor<StorageOptions>? _storageOptions;
    private readonly CancellationTokenSource _lifetime = new();
    private Timer? _healthTimer;
    private int _desiredRecording;
    private long _requestVersion;
    private int _healthBusy;
    private DateTime? _requestedEndUtc;
    private long _captureStartedTicks;

    /// <summary>
    /// سجل مقطع الفيديو الحالي الجاري تسجيله.
    /// </summary>
    private RecordingSegment? _currentSegment;

    /// <summary>
    /// مؤقت تجزئة الفيديو لإنهاء المقطع الحالي والبدء في مقطع جديد دورياً.
    /// </summary>
    private System.Threading.Timer? _segmentTimer;

    /// <summary>
    /// مصدر إكمال المهمة لانتظار إشعار اكتمال الإيقاف غير المتزامن من المكتبة.
    /// </summary>
    private TaskCompletionSource<bool>? _stopTcs;

    /// <summary>
    /// مرجع لمسجل الشاشة الجاري إيقافه حالياً لتجنب التعارض أثناء التبديل.
    /// </summary>
    private Recorder? _recorderBeingStopped;

    /// <summary>
    /// علامة تدل على ما إذا كان الكائن قد تم التخلص من موارده.
    /// </summary>
    private bool _isDisposed;

    /// <summary>
    /// مؤشر للتحول لترميز البرمجيات (Software Encoding) كبديل عند فشل بطاقة الرسوميات.
    /// </summary>
    private bool _useSoftwareEncodingFallback;

    /// <summary>
    /// القيمة الرقمية للحالة الحالية لدورة حياة التسجيل (للقراءة والكتابة المتطايرة).
    /// </summary>
    private int _lifecycleState = (int)RecordingLifecycleState.Stopped;

    /// <summary>
    /// عدد محاولات البدء الفاشلة المتتالية لحساب فترة التأجيل التصاعدي (Backoff).
    /// </summary>
    private int _consecutiveStartFailures;

    /// <summary>
    /// التوقيت الزمني القادم المسموح فيه بمحاولة إعادة تشغيل التسجيل.
    /// </summary>
    private DateTime _nextStartAttemptUtc = DateTime.MinValue;

    /// <summary>
    /// خاصية قراءة وتعيين حالة دورة حياة التسجيل بشكل آمن عبر الخيوط.
    /// </summary>
    private RecordingLifecycleState LifecycleState
    {
        get => (RecordingLifecycleState)Volatile.Read(ref _lifecycleState);
        set => Volatile.Write(ref _lifecycleState, (int)value);
    }

    /// <summary>
    /// مؤشر يحدد ما إذا كانت الخدمة تسجل حالياً أو في طور البدء.
    /// </summary>
    public bool IsRecording => LifecycleState == RecordingLifecycleState.Recording;

    /// <summary>
    /// كائن مقطع التسجيل الحالي.
    /// </summary>
    public RecordingSegment? CurrentSegment => _currentSegment;
    public string? LastError { get; private set; }

    /// <summary>
    /// منشئ فئة خدمة تسجيل الشاشة.
    /// </summary>
    /// <param name="recordingRepository">مستودع مقاطع التسجيل.</param>
    /// <param name="storageService">خدمة إدارة التخزين.</param>
    /// <param name="optionsMonitor">مراقب خيارات التسجيل.</param>
    /// <param name="logger">مسجل العمليات.</param>
    public ScreenRecordingService(
        IRecordingRepository recordingRepository,
        IStorageManagementService storageService,
        IOptionsMonitor<RecordingOptions> optionsMonitor,
        ILogger<ScreenRecordingService> logger, IOptionsMonitor<StorageOptions>? storageOptions = null)
    {
        _recordingRepository = recordingRepository;
        _storageService = storageService;
        _optionsMonitor = optionsMonitor;
        _logger = logger;
        _storageOptions = storageOptions;
    }

    /// <summary>
    /// بدء تسجيل مقطع شاشة جديد وتكوين إعدادات الفيديو والصوت وحفظ المقطع في قاعدة البيانات.
    /// </summary>
    /// <param name="ct">رمز إلغاء العملية.</param>
    /// <returns>كائن مقطع التسجيل الذي تم بدؤه أو null في حالة التعذر.</returns>
    public Task<RecordingSegment?> StartRecordingSegmentAsync(CancellationToken ct = default)
    {
        Volatile.Write(ref _desiredRecording, 1);
        var version = Interlocked.Increment(ref _requestVersion);
        return StartCoreAsync(version, ct);
    }

    private async Task<RecordingSegment?> StartCoreAsync(long version, CancellationToken ct)
    {
        // التحقق من خيار تفعيل التسجيل في ملف الإعدادات
        if (!Options.Enabled)
        {
            _logger.LogInformation("Screen recording is disabled in configuration.");
            return null;
        }

        // الحصول على قفل التزامن لمنع بدء مقطعين في نفس الوقت
        await _lock.WaitAsync(ct);
        try
        {
            // إذا كان التسجيل يعمل بالفعل نعيد المقطع الحالي
            if (_isDisposed || Volatile.Read(ref _desiredRecording) == 0 || version != Volatile.Read(ref _requestVersion)) return null;
            if (LifecycleState is RecordingLifecycleState.Starting or RecordingLifecycleState.Recording)
            {
                return _currentSegment;
            }

            // إذا كان المسجل في طور الإيقاف نتجاهل طلب البدء الآن
            if (LifecycleState == RecordingLifecycleState.Stopping)
            {
                _logger.LogDebug("Recording start ignored while the current segment is stopping.");
                return null;
            }

            // إذا كنا في فترة الانتظار بعد فشل سابق نؤجل المحاولة
            if (DateTime.UtcNow < _nextStartAttemptUtc)
            {
                _logger.LogDebug("Recording recovery is in backoff until {RetryAtUtc}.", _nextStartAttemptUtc);
                return null;
            }

            // تغيير الحالة إلى قيد البدء
            LifecycleState = RecordingLifecycleState.Starting;

            // التحقق من كفاية المساحة الحرة على القرص قبل بدء التسجيل
            var hasSpace = await _storageService.EnsureDiskSpaceAvailableAsync(ct);
            if (!hasSpace)
            {
                LastError = "Insufficient recording capacity";
                _logger.LogWarning("Insufficient free disk space to start screen recording. Skipping.");
                ScheduleStartRetry();
                LifecycleState = RecordingLifecycleState.Faulted;
                return null;
            }

            var now = DateTime.UtcNow;
            var machineId = Environment.MachineName;
            var sessionId = GetCurrentSessionId();

            // معالجة وإغلاق أي مقاطع سابقة ظلت معلقة بسبب إغلاق غير متوقع للنظام
            // Orphan recovery is performed by DataIntegrityService using ownership leases.

            // توليد اسم فريد لملف مقطع الفيديو لتجنب أي تضارب
            var uniqueFileName = $"{now:yyyy-MM-dd_HH-mm-ss-fff}_{sessionId}_{Guid.NewGuid():N}.mp4";
            var filePath = ActivityMonitorPaths.GetRecordingFilePath(machineId, now, uniqueFileName);
            if (!StoragePathGuard.TryResolvePathWithinRoot(filePath, ActivityMonitorPaths.RecordingsDirectory, out filePath, out var pathError))
                throw new UnauthorizedAccessException(pathError);
            var fileName = Path.GetFileName(filePath);

            // إنشاء كائن مقطع التسجيل في قاعدة البيانات
            var segment = new RecordingSegment
            {
                MachineId = machineId,
                SessionId = sessionId,
                UserIdentifier = Environment.UserName,
                OwnerInstanceId = _instanceId,
                OwnerProcessId = Environment.ProcessId,
                OwnerProcessStartedAtUtc = Process.GetCurrentProcess().StartTime.ToUniversalTime(),
                HeartbeatAtUtc = now,
                StartedAtUtc = now,
                FilePath = filePath,
                FileName = fileName,
                Status = RecordingStatus.InProgress,
                CreatedAtUtc = now
            };

            // حفظ المقطع في المستودع
            segment = await _recordingRepository.AddAsync(segment, ct);
            _currentSegment = segment;

            // Resolve actual Windows audio endpoints before passing sources to the native recorder.
            var audioOptions = new RecordingAudioOptionsFactory(_logger).Create(Options);

            // إعداد خيارات ترميز الفيديو (معدل الإطارات، الجودة، تسريع العتاد، وتجزئة MP4)
            var recOptions = new RecorderOptions
            {
                VideoEncoderOptions = new VideoEncoderOptions
                {
                    Framerate = Math.Clamp(Options.FrameRate, 5, 60),
                    Bitrate = Math.Clamp(Options.VideoBitrateKbps * 1000, 300_000, 10_000_000),
                    Quality = Math.Clamp(Options.VideoQuality, 10, 100),
                    IsHardwareEncodingEnabled = !_useSoftwareEncodingFallback,
                    IsLowLatencyEnabled = true,
                    IsFragmentedMp4Enabled = Options.IsFragmentedMp4Enabled,
                    IsMp4FastStartEnabled = !Options.IsFragmentedMp4Enabled
                },
                AudioOptions = audioOptions,
                MouseOptions = new MouseOptions
                {
                    IsMouseClicksDetected = false
                }
            };

            // إعداد مصدر شاشة العرض واختيار واجهة الالتقاط المناسبة (WindowsGraphicsCapture أو DesktopDuplication)
            try
            {
                var displays = Recorder.GetDisplays();
                recOptions.SourceOptions = new SourceOptions();
                DisplayRecordingSource displaySource;

                if (displays != null && displays.Count > 0)
                {
                    var targetDisplay = displays.FirstOrDefault(d => !string.IsNullOrEmpty(d.DeviceName)) ?? displays[0];
                    displaySource = new DisplayRecordingSource(targetDisplay.DeviceName);
                    _logger.LogInformation("Screen recorder configured with WindowsGraphicsCapture on display '{Display}'", targetDisplay.DeviceName);
                }
                else
                {
                    displaySource = DisplayRecordingSource.MainMonitor;
                    _logger.LogInformation("Screen recorder configured with WindowsGraphicsCapture on MainMonitor");
                }

                // تعطيل الإطار الأصفر في ويندوز 11 والإصدارات الحديثة من ويندوز 10
                bool canDisableBorder = Environment.OSVersion.Version.Build >= 20348;
                if (canDisableBorder)
                {
                    displaySource.RecorderApi = RecorderApi.WindowsGraphicsCapture;
                    displaySource.IsCursorCaptureEnabled = false;
                    displaySource.IsBorderRequired = false;
                    _logger.LogInformation("Configured WindowsGraphicsCapture (borderless)");
                }
                else
                {
                    displaySource.RecorderApi = RecorderApi.DesktopDuplication;
                    displaySource.IsCursorCaptureEnabled = false;
                    _logger.LogInformation("Configured DesktopDuplication to prevent yellow border on Windows 10 legacy");
                }

                recOptions.SourceOptions.RecordingSources.Add(displaySource);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to configure display source; using default.");
            }

            // إنشاء مسجل الشاشة والاشتراك في أحداثه
            var recorder = Recorder.CreateRecorder(recOptions);
            _recorder = recorder;
            recorder.OnRecordingComplete += (_, e) => OnRecordingComplete(recorder, segment, e);
            recorder.OnRecordingFailed += (_, e) => OnRecordingFailed(recorder, segment, e);
            recorder.OnStatusChanged += (_, e) => OnStatusChanged(recorder, segment, e);

            // بدء التسجيل الفعلي وحفظه في المسار المحدد
            _requestedEndUtc = null;
            Interlocked.Exchange(ref _captureStartedTicks, 0);
            // Metadata is anchored to the recorder's Recording status, not configuration/encoder setup.
            segment.StartedAtUtc = DateTime.UtcNow;
            if (Volatile.Read(ref _desiredRecording) == 0 || version != Volatile.Read(ref _requestVersion))
            {
                recorder.Dispose(); _recorder = null; _currentSegment = null;
                segment.Status = RecordingStatus.Corrupted; segment.EndedAtUtc = segment.StartedAtUtc;
                await _recordingRepository.UpdateAsync(segment, ct);
                LifecycleState = RecordingLifecycleState.Stopped;
                return null;
            }
            recorder.Record(filePath);
            await _recordingRepository.UpdateAsync(segment, ct);
            _healthTimer?.Dispose();
            _healthTimer = new Timer(async _ => await CheckRecordingHealthAsync(), null,
                TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5));
            _consecutiveStartFailures = 0;
            _nextStartAttemptUtc = DateTime.MinValue;

            _logger.LogInformation(
                "Video encoder mode: {Mode}",
                _useSoftwareEncodingFallback ? "software fallback" : "hardware accelerated");
            _logger.LogInformation("Started screen recording segment {SegmentId} -> {FilePath}", segment.Id, filePath);

            // تجهيز مؤقت تجزئة الفيديو لتبديل المقطع بعد انقضاء المدة المحددة
            var segmentInterval = TimeSpan.FromMinutes(Math.Max(1, Options.SegmentMinutes));
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
            // في حال حدوث خطأ، نقوم بتنظيف الموارد وتعيين حالة المقطع كـ تالف
            LastError = ex.Message;
            _useSoftwareEncodingFallback = true;
            _logger.LogError(ex, "Failed to start screen recording segment");
            ScheduleStartRetry();
            LifecycleState = RecordingLifecycleState.Faulted;
            _segmentTimer?.Dispose();
            _segmentTimer = null;
            try
            {
                _recorder?.Dispose();
            }
            catch { }
            _recorder = null;

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
            // تحرير قفل التزامن
            _lock.Release();
        }
    }

    /// <summary>
    /// إيقاف مقطع التسجيل الحالي بأمان وانتظار معالجة وحفظ الملف وتحديث السجل في قاعدة البيانات.
    /// </summary>
    /// <param name="ct">رمز إلغاء العملية.</param>
    /// <returns>مقطع التسجيل المنتهي أو null إن لم يكن هناك تسجيل قيد العمل.</returns>
    public Task<RecordingSegment?> StopRecordingSegmentAsync(CancellationToken ct = default)
    {
        Volatile.Write(ref _desiredRecording, 0);
        Interlocked.Increment(ref _requestVersion);
        return StopCoreAsync(ct);
    }

    private async Task<RecordingSegment?> StopCoreAsync(CancellationToken ct)
    {
        await _lock.WaitAsync(ct);
        try
        {
            // إيقاف مؤقت التجزئة
            _segmentTimer?.Change(Timeout.Infinite, Timeout.Infinite);

            // إذا لم يكن هناك مسجل أو مقطع نشط نخرج
            if (_recorder == null || _currentSegment == null)
            {
                LifecycleState = RecordingLifecycleState.Stopped;
                return null;
            }

            _healthTimer?.Change(Timeout.Infinite, Timeout.Infinite);
            _requestedEndUtc = _segmentClock.IsRunning
                ? _currentSegment.StartedAtUtc + _segmentClock.Elapsed : DateTime.UtcNow;
            _segmentClock.Stop();
            var segmentToFinalize = _currentSegment;
            var recorderToStop = _recorder;
            LifecycleState = RecordingLifecycleState.Stopping;
            _logger.LogInformation("Stopping screen recording segment {SegmentId}...", segmentToFinalize.Id);

            _stopTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            _recorderBeingStopped = recorderToStop;
            try
            {
                // إرسال أمر التوقف لمسجل الشاشة
                recorderToStop.Stop();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Screen recorder threw while stopping segment {SegmentId}.", segmentToFinalize.Id);
                try { recorderToStop.Dispose(); } catch { }

                if (ReferenceEquals(_recorder, recorderToStop)) _recorder = null;
                if (_currentSegment == segmentToFinalize) _currentSegment = null;
                _recorderBeingStopped = null;

                var now = DateTime.UtcNow;
                segmentToFinalize.EndedAtUtc = now;
                segmentToFinalize.DurationSeconds = Math.Max(0, (now - segmentToFinalize.StartedAtUtc).TotalSeconds);
                segmentToFinalize.Status = RecordingStatus.Corrupted;
                try { await _recordingRepository.UpdateAsync(segmentToFinalize, ct); } catch { }

                ScheduleStartRetry();
                LifecycleState = RecordingLifecycleState.Faulted;
                return segmentToFinalize;
            }

            // انتظار اكتمال إغلاق وحفظ الملف بمهلة 15 ثانية كحد أقصى
            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);
            try
            {
                await _stopTcs.Task.WaitAsync(linked.Token);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Timeout or error while waiting for recording finalization.");
                segmentToFinalize.Status = RecordingStatus.Corrupted;
                segmentToFinalize.EndedAtUtc = _requestedEndUtc;
                segmentToFinalize.DurationSeconds = Math.Max(0, (_requestedEndUtc!.Value - segmentToFinalize.StartedAtUtc).TotalSeconds);
                try { await _recordingRepository.UpdateAsync(segmentToFinalize, CancellationToken.None); }
                catch (Exception persistenceError) { _logger.LogError(persistenceError, "Could not persist timed-out recording finalization"); }
            }

            try
            {
                recorderToStop.Dispose();
            }
            catch { }
            if (ReferenceEquals(_recorder, recorderToStop))
            {
                _recorder = null;
            }
            _recorderBeingStopped = null;

            if (_currentSegment == segmentToFinalize)
            {
                _currentSegment = null;
            }
            LifecycleState = RecordingLifecycleState.Stopped;
            return segmentToFinalize;
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// التبديل السلس لمقطع فيديو جديد عبر إيقاف المقطع الحالي وبدء مقطع تالٍ فوراً.
    /// </summary>
    /// <param name="ct">رمز إلغاء العملية.</param>
    /// <returns>كائن المقطع الجديد المستحدث.</returns>
    public async Task<RecordingSegment?> SwitchToNewSegmentAsync(CancellationToken ct = default)
    {
        var version = Volatile.Read(ref _requestVersion);
        if (Volatile.Read(ref _desiredRecording) == 0) return null;
        await StopCoreAsync(ct);
        return await StartCoreAsync(version, ct);
    }

    /// <summary>
    /// معالج نبضات مؤقت التجزئة لتنفيذ عملية التبديل إلى مقطع جديد تلقائياً.
    /// </summary>
    private async Task OnSegmentTimerTickAsync()
    {
        try
        {
            await SwitchToNewSegmentAsync(_lifetime.Token);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error occurred during scheduled recording segmentation.");
        }
    }

    /// <summary>
    /// معالج حدث اكتمال التسجيل الصادر من مكتبة تسجيل الشاشة.
    /// </summary>
    /// <param name="completedRecorder">المسجل الذي اكتمل.</param>
    /// <param name="segmentToFinalize">المقطع المراد إنهاؤه وتوثيقه.</param>
    /// <param name="e">معلومات اكتمال التسجيل ومسار الملف النهائي.</param>
    private void OnRecordingComplete(Recorder completedRecorder, RecordingSegment segmentToFinalize, RecordingCompleteEventArgs e)
    {
        _logger.LogInformation("Screen recording successfully completed for file: {FilePath}", e.FilePath);
        var capturedEnd = _requestedEndUtc ?? (segmentToFinalize.StartedAtUtc + _segmentClock.Elapsed);

        _ = Task.Run(async () =>
        {
            try
            {
                if (segmentToFinalize != null)
                {
                    var valid = MediaFileInspector.TryReadVideoDuration(e.FilePath, out var mediaDuration);
                    segmentToFinalize.DurationSeconds = valid ? mediaDuration.TotalSeconds : Math.Max(0, (capturedEnd - segmentToFinalize.StartedAtUtc).TotalSeconds);
                    segmentToFinalize.EndedAtUtc = segmentToFinalize.StartedAtUtc.AddSeconds(segmentToFinalize.DurationSeconds);
                    segmentToFinalize.Status = valid ? RecordingStatus.Completed : RecordingStatus.Corrupted;
                    segmentToFinalize.HeartbeatAtUtc = capturedEnd;

                    // قراءة حجم الملف الفعلي على القرص
                    if (File.Exists(e.FilePath))
                    {
                        segmentToFinalize.FileSizeBytes = new FileInfo(e.FilePath).Length;
                    }

                    // تحديث بيانات المقطع في قاعدة البيانات
                    await _recordingRepository.UpdateAsync(segmentToFinalize);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to update completed segment state in database.");
            }
            finally
            {
                // إشعار دالة الإيقاف باكتمال المعالجة
                if (ReferenceEquals(_recorderBeingStopped, completedRecorder))
                {
                    _stopTcs?.TrySetResult(true);
                }
                else if (ReferenceEquals(_recorder, completedRecorder))
                {
                    await _lock.WaitAsync();
                    try
                    {
                        if (ReferenceEquals(_recorder, completedRecorder))
                        {
                            _healthTimer?.Change(Timeout.Infinite, Timeout.Infinite);
                            completedRecorder.Dispose(); _recorder = null; _currentSegment = null;
                            LifecycleState = RecordingLifecycleState.Faulted;
                            LastError = "Recorder completed unexpectedly";
                            ScheduleStartRetry();
                        }
                    }
                    finally { _lock.Release(); }
                }
            }
        });
    }

    /// <summary>
    /// معالج حدث فشل التسجيل الصادر من مكتبة تسجيل الشاشة.
    /// </summary>
    /// <param name="failedRecorder">المسجل الذي واجه الفشل.</param>
    /// <param name="segmentToFail">المقطع الفاشل.</param>
    /// <param name="e">معلومات سبب الفشل ومسار الملف.</param>
    private void OnRecordingFailed(Recorder failedRecorder, RecordingSegment segmentToFail, RecordingFailedEventArgs e)
    {
        LastError = e.Error;
        _logger.LogError("Screen recording failed! Error: {Error} File: {FilePath}", e.Error, e.FilePath);

        // التبديل إلى ترميز البرمجيات (Software Fallback) للمحاولة القادمة في حال فشل ترميز العتاد
        if (!_useSoftwareEncodingFallback)
        {
            _useSoftwareEncodingFallback = true;
            _logger.LogWarning("The next recording recovery attempt will disable hardware encoding for compatibility.");
        }

        // فك حظر أي دالة إيقاف تنتظر لتفادي قفل الخيوط (Deadlock)
        if (ReferenceEquals(_recorderBeingStopped, failedRecorder))
        {
            _stopTcs?.TrySetResult(false);
        }

        _ = Task.Run(async () =>
        {
            await _lock.WaitAsync();
            try
            {
                if (segmentToFail != null)
                {
                    segmentToFail.EndedAtUtc = DateTime.UtcNow;
                    segmentToFail.Status = RecordingStatus.Corrupted;
                    try
                    {
                        await _recordingRepository.UpdateAsync(segmentToFail);
                    }
                    catch { }

                    if (_currentSegment == segmentToFail)
                    {
                        _currentSegment = null;
                    }
                }

                if (ReferenceEquals(_recorder, failedRecorder))
                {
                    _segmentTimer?.Change(Timeout.Infinite, Timeout.Infinite);

                    try
                    {
                        failedRecorder.Dispose();
                    }
                    catch { }
                    _recorder = null;
                    ScheduleStartRetry();
                    LifecycleState = RecordingLifecycleState.Faulted;
                }
            }
            finally
            {
                _lock.Release();
            }
        });
    }

    /// <summary>
    /// التحقق من أن تسجيل الشاشة قيد العمل بالفعل وبدؤه تلقائياً في حال كان متوقفاً.
    /// </summary>
    /// <param name="ct">رمز إلغاء العملية.</param>
    /// <returns>مقطع التسجيل النشط.</returns>
    public async Task<RecordingSegment?> EnsureRecordingActiveAsync(CancellationToken ct = default)
    {
        if (!Options.Enabled || _isDisposed) return null;
        await _lock.WaitAsync(ct);
        try
        {
            if (LifecycleState is RecordingLifecycleState.Starting or RecordingLifecycleState.Recording) return _currentSegment;
            if (LifecycleState == RecordingLifecycleState.Stopping || DateTime.UtcNow < _nextStartAttemptUtc) return null;
        }
        finally { _lock.Release(); }
        return await StartRecordingSegmentAsync(ct);
    }

    /// <summary>
    /// معالج تغير الحالة الداخلية لمسجل الشاشة للتسجيل التشخيصي.
    /// </summary>
    /// <param name="sender">المصدر.</param>
    /// <param name="e">معلومات الحالة.</param>
    private void OnStatusChanged(Recorder recorder, RecordingSegment segment, RecordingStatusEventArgs e)
    {
        _logger.LogDebug("Recorder status changed: {Status}", e.Status);
        if (!ReferenceEquals(_recorder, recorder) || e.Status != RecorderStatus.Recording ||
            Volatile.Read(ref _desiredRecording) == 0 || LifecycleState == RecordingLifecycleState.Stopping) return;
        var now = DateTime.UtcNow;
        if (Interlocked.CompareExchange(ref _captureStartedTicks, now.Ticks, 0) == 0)
        {
            LastError = null;
            segment.StartedAtUtc = now;
            segment.HeartbeatAtUtc = now;
            _segmentClock.Restart();
            LifecycleState = RecordingLifecycleState.Recording;
        }
    }

    private async Task CheckRecordingHealthAsync()
    {
        if (_isDisposed || Interlocked.Exchange(ref _healthBusy, 1) != 0) return;
        try
        {
            if (LifecycleState == RecordingLifecycleState.Starting && _currentSegment != null &&
                DateTime.UtcNow - _currentSegment.StartedAtUtc > TimeSpan.FromSeconds(30))
            {
                LastError = "Recorder did not enter Recording state within 30 seconds";
                await StopRecordingSegmentAsync(_lifetime.Token);
                ScheduleStartRetry();
                return;
            }
            var minBytes = (_storageOptions?.CurrentValue.MinimumFreeDiskSpaceGB ?? Options.MinimumFreeDiskSpaceGB) * 1024L * 1024 * 1024;
            var freeBytes = new DriveInfo(Path.GetPathRoot(ActivityMonitorPaths.RootDirectory)!).AvailableFreeSpace;
            if (!Options.Enabled || freeBytes < minBytes)
            {
                LastError = "Recording disabled or disk space below minimum";
                _logger.LogWarning("Stopping recording: disabled or free disk space is below the configured minimum");
                await StopRecordingSegmentAsync(_lifetime.Token);
                return;
            }
            if (!await _lock.WaitAsync(0, _lifetime.Token)) return;
            try
            {
                if (_currentSegment == null || LifecycleState is not (RecordingLifecycleState.Starting or RecordingLifecycleState.Recording)) return;
                _currentSegment.HeartbeatAtUtc = _currentSegment.StartedAtUtc + _segmentClock.Elapsed;
                _currentSegment.DurationSeconds = _segmentClock.Elapsed.TotalSeconds;
                await _recordingRepository.UpdateAsync(_currentSegment, _lifetime.Token);
            }
            finally { _lock.Release(); }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { _logger.LogError(ex, "Recording health check failed"); }
        finally { Volatile.Write(ref _healthBusy, 0); }
    }

    /// <summary>
    /// جدولة تأجيل إعادة المحاولة تصاعدياً (Exponential Backoff) عند تكرار الفشل.
    /// </summary>
    private void ScheduleStartRetry()
    {
        _consecutiveStartFailures = Math.Min(_consecutiveStartFailures + 1, 6);
        var delaySeconds = Math.Min(60, 5 * (1 << (_consecutiveStartFailures - 1)));
        _nextStartAttemptUtc = DateTime.UtcNow.AddSeconds(delaySeconds);
        _logger.LogWarning("Screen recording recovery will retry in {DelaySeconds} seconds.", delaySeconds);
    }

    /// <summary>
    /// استخراج معرّف الجلسة الحالية من العملية.
    /// </summary>
    /// <returns>معرّف الجلسة أو 1.</returns>
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

    /// <summary>
    /// استرداد وإغلاق أي مقاطع سابقة تركت دون إغلاق سليم في قاعدة البيانات (بسبب إيقاف مفاجئ للجهاز).
    /// </summary>
    /// <param name="machineId">معرّف الجهاز.</param>
    /// <param name="ct">رمز الإلغاء.</param>


    /// <summary>
    /// التخلص من الموارد غير المدارة والمؤقتات وتحرير المسجل.
    /// </summary>
    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;

        _lifetime.Cancel();
        Volatile.Write(ref _desiredRecording, 0);
        _segmentTimer?.Dispose();
        _healthTimer?.Dispose();
        // Keep the semaphore alive for callbacks already queued by the native recorder.
        try { _recorder?.Dispose(); } catch (Exception ex) { _logger.LogWarning(ex, "Recorder disposal failed"); }
        LifecycleState = RecordingLifecycleState.Stopped;
        GC.SuppressFinalize(this);
    }
}
