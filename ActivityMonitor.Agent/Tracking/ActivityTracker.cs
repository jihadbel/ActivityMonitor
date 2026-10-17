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
using ActivityMonitor.Core.Models;
using ActivityMonitor.Core.Options;
using ActivityMonitor.Infrastructure.ContextProviders;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Win32;
using ActivityEvent = ActivityMonitor.Core.Entities.ActivityEvent;
using ActivityTrackingOptions = ActivityMonitor.Core.Options.ActivityTrackingOptions;

namespace ActivityMonitor.Agent.Tracking;

/// <summary>
/// فئة مسؤولة عن تتبع نشاط المستخدم على مستوى نظام التشغيل Windows،
/// ومراقبة النوافذ النشطة، واكتشاف فترات الخمول، والتكامل مع خدمة تسجيل الشاشة.
/// </summary>
public class ActivityTracker : IActivityTracker, IDisposable
{
    // ==========================================
    // حقول الاعتماديات والخدمات المحقونة (Injected Dependencies)
    // ==========================================

    /// <summary>
    /// مستودع حفظ وتحديث أحداث النشاط في قاعدة البيانات.
    /// </summary>
    private readonly IActivityRepository _activityRepository;

    /// <summary>
    /// خدمة التحكم في تسجيل الشاشة بالفيديو.
    /// </summary>
    private readonly IScreenRecordingService _recordingService;

    /// <summary>
    /// مزود استخراج سياق العمل الإضافي للتطبيقات (مثل مسارات الملفات وعناوين الويب).
    /// </summary>
    private readonly ActivityContextProviderComposite _contextProvider;

    /// <summary>
    /// خيارات وإعدادات تتبع الأنشطة (مثل فترات الاستطلاع وحساسية الخمول).
    /// </summary>
    private readonly ActivityTrackingOptions _options;

    /// <summary>
    /// مراقب خيارات التسجيل لدعم قراءة الإعدادات المحدثة ديناميكياً.
    /// </summary>
    private readonly IOptionsMonitor<RecordingOptions> _recordingOptions;

    /// <summary>
    /// واجهة تسجيل السجلات والرسائل التشخيصية (Logger).
    /// </summary>
    private readonly ILogger<ActivityTracker> _logger;

    // ==========================================
    // حقول التزامن وإدارة الخيوط (Concurrency & Threading)
    // ==========================================

    /// <summary>
    /// قفل ثنائي خفيف لتأمين العمليات المتزامنة ومنع تداخل الخيوط.
    /// </summary>
    private readonly SemaphoreSlim _syncLock = new(1, 1);

    /// <summary>
    /// مصدر رموز الإلغاء لإيقاف كافة العمليات الخلفية بسلاسة.
    /// </summary>
    private CancellationTokenSource? _cts;

    /// <summary>
    /// خيط مخصص لتنفيذ حلقة مراقبة واستطلاع حالة النوافذ.
    /// </summary>
    private Thread? _monitorThread;

    /// <summary>
    /// خيط مخصص لاستقبال خطافات أحداث نظام ويندوز (WinEvents).
    /// </summary>
    private Thread? _winEventThread;

    /// <summary>
    /// المعرّف الرقمي لخيط أحداث ويندوز لإرسال رسائل الإيقاف إليه.
    /// </summary>
    private uint _winEventThreadId;

    /// <summary>
    /// دالة رد النداء (Delegate) المسؤولة عن معالجة خطافات أحداث ويندوز.
    /// </summary>
    private NativeMethods.WinEventDelegate? _winEventDelegate;

    /// <summary>
    /// إشارة تدل على اكتمال تثبيت وتجهيز خطافات أحداث ويندوز.
    /// </summary>
    private readonly ManualResetEventSlim _winEventHookReady = new(false);

    /// <summary>
    /// حدث لإيقاظ خيط المراقبة فوراً عند وقوع حدث نافذة دون انتظار مهلة الاستطلاع.
    /// </summary>
    private readonly AutoResetEvent _wakeUpEvent = new(false);

    // ==========================================
    // حقول الحالة التشغيلية (Internal State Tracking)
    // ==========================================

    /// <summary>
    /// سجل النشاط النشط الحالي للمستخدم.
    /// </summary>
    private ActivityEvent? _currentActivity;
    private readonly object _eventOrderLock = new();
    private Task _eventTail = Task.CompletedTask;
    public bool IsUserActive => _isStarted && !_isIdle && !_isLocked && !_isSuspended;

    private void QueueSystemTransition(Func<Task> transition)
    {
        lock (_eventOrderLock)
        {
            _eventTail = _eventTail.ContinueWith(async _ => {
                try { await transition(); }
                catch (Exception ex) { _logger.LogError(ex, "System transition failed"); }
            }, CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default).Unwrap();
        }
    }

    /// <summary>
    /// سجل نشاط الخمول الحالي إن كان المستخدم في حالة خمول.
    /// </summary>
    private ActivityEvent? _currentIdleActivity;

    /// <summary>
    /// مؤشر يحدد ما إذا كان المستخدم في حالة خمول حالياً.
    /// </summary>
    private bool _isIdle;

    /// <summary>
    /// مؤشر يحدد ما إذا كانت جلسة ويندوز مقفلة.
    /// </summary>
    private bool _isLocked;

    /// <summary>
    /// مؤشر يحدد ما إذا كان النظام في وضع السكون أو التعليق.
    /// </summary>
    private bool _isSuspended;

    /// <summary>
    /// مؤشر يحدد ما إذا كانت خدمة التتبع قيد التشغيل بالفعل.
    /// </summary>
    private bool _isStarted;

    /// <summary>
    /// الطابع الزمني لآخر عملية ترحيل وتحديث للبيانات في قاعدة البيانات.
    /// </summary>
    private DateTime _lastFlushTime = DateTime.UtcNow;

    /// <summary>
    /// مرشح النافذة المحتمل قيد الانتظار للتحقق من استقراره (Debounce).
    /// </summary>
    private WindowCandidate? _pendingWindowCandidate;

    // ==========================================
    // الأحداث العامة (Public Events)
    // ==========================================

    /// <summary>
    /// حدث يتم إطلاقه عند بدء نشاط جديد لنافذة نشطة.
    /// </summary>
    public event Func<ActivityEvent, Task>? ActivityStarted;

    /// <summary>
    /// حدث يتم إطلاقه عند انتهاء النشاط الحالي وحفظه.
    /// </summary>
    public event Func<ActivityEvent, Task>? ActivityEnded;

    /// <summary>
    /// منشئ الفئة: يقوم بحقن كافة الاعتماديات اللازمة لعمل متتبع الأنشطة.
    /// </summary>
    /// <param name="activityRepository">مستودع الأنشطة لتخزين البيانات.</param>
    /// <param name="recordingService">خدمة تسجيل الشاشة بالفيديو.</param>
    /// <param name="contextProvider">مزود سياق النوافذ والتطبيقات.</param>
    /// <param name="options">خيارات تتبع الأنشطة.</param>
    /// <param name="recordingOptions">مراقب خيارات تسجيل الفيديو.</param>
    /// <param name="logger">مسجل العمليات والأخطاء.</param>
    public ActivityTracker(
        IActivityRepository activityRepository,
        IScreenRecordingService recordingService,
        ActivityContextProviderComposite contextProvider,
        IOptions<ActivityTrackingOptions> options,
        IOptionsMonitor<RecordingOptions> recordingOptions,
        ILogger<ActivityTracker> logger)
    {
        // تعيين مستودع الأنشطة
        _activityRepository = activityRepository;

        // تعيين خدمة تسجيل الشاشة
        _recordingService = recordingService;

        // تعيين مزود سياق النوافذ
        _contextProvider = contextProvider;

        // قراءة وتعيين خيارات التتبع
        _options = options.Value;

        // تعيين مراقب خيارات التسجيل الحي
        _recordingOptions = recordingOptions;

        // تعيين مسجل السجلات التشخيصية
        _logger = logger;
    }

    /// <summary>
    /// يبدأ تشغيل خدمة تتبع الأنشطة وإنشاء خيوط المراقبة وخطافات أحداث ويندوز.
    /// </summary>
    /// <param name="ct">رمز إلغاء اختياري لقطع العملية.</param>
    /// <returns>مهمة تمثل العملية غير المتزامنة.</returns>
    public Task StartAsync(CancellationToken ct = default)
    {
        // التحقق مما إذا كانت ميزة التتبع معطلة في ملف الإعدادات
        if (!_options.Enabled)
            _logger.LogInformation("Application history is disabled; session/idle lifecycle remains active for recording.");

        // إذا كانت الخدمة قد بدأت بالفعل، نتجاهل طلب البدء مجدداً
        if (_isStarted)
        {
            return Task.CompletedTask;
        }

        // تسجيل بدء تشغيل المتتبع
        _logger.LogInformation("Starting ActivityTracker...");

        // إنشاء رمز إلغاء مرتبط بالرمز الممرر
        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);

        // تسجيل الاستماع لأحداث تبديل جلسة المستخدم في نظام ويندوز
        SystemEvents.SessionSwitch += OnSessionSwitch;

        // تسجيل الاستماع لأحداث تغير وضع الطاقة (السكون والاستيقاظ)
        SystemEvents.PowerModeChanged += OnPowerModeChanged;

        // تسجيل الاستماع لأحداث إنهاء الجلسة أو إيقاف تشغيل الجهاز
        SystemEvents.SessionEnding += OnSessionEnding;

        // محاولة فتح سطح المكتب التفاعلي الافتراضي للتطبيق
        IntPtr hDesk = NativeMethods.OpenDesktop("default", 0, false, NativeMethods.DESKTOP_ALL_ACCESS);

        // إذا فشل فتح الافتراضي، نحاول فتح سطح مكتب الإدخال النشط
        if (hDesk == IntPtr.Zero)
        {
            hDesk = NativeMethods.OpenInputDesktop(0, false, NativeMethods.DESKTOP_ALL_ACCESS);
        }

        // إنشاء خيط مخصص لمراقبة النوافذ وسطح المكتب
        _monitorThread = new Thread(() => RunMonitoringThread(hDesk))
        {
            // تعيين الخيط كخيط خلفي حتى لا يمنع إغلاق التطبيق
            IsBackground = true,
            // تسمية الخيط لتسهيل تتبعه أثناء التنقيح
            Name = "ActivityMonitorThread"
        };

        // بدء تشغيل خيط المراقبة
        _monitorThread.Start();

        // إنشاء خيط مخصص لاستقبال خطافات ويندوز (WinEvent Hooks)
        _winEventThread = new Thread(RunWinEventHookThread)
        {
            // تعيين الخيط كخيط خلفي
            IsBackground = true,
            // تسمية خيط أحداث ويندوز
            Name = "ActivityMonitorWinEventThread"
        };

        // بدء تشغيل خيط خطافات ويندوز
        _winEventThread.Start();

        // انتظار إشارة جاهزية خطافات ويندوز لمدة ثانيتين كحد أقصى
        _winEventHookReady.Wait(TimeSpan.FromSeconds(2), ct);

        // تحديث حالة التشغيل إلى نعم
        _isStarted = true;

        // تسجيل اكتمال تشغيل المتتبع بنجاح
        _logger.LogInformation("ActivityTracker started successfully.");
        return Task.CompletedTask;
    }

    /// <summary>
    /// هيكل بيانات خفيف يمثل لقطة لحظية لحالة النافذة الأمامية وفترة خمول المستخدم.
    /// </summary>
    private readonly struct WindowSnapshot
    {
        /// <summary>
        /// مقبض نافذة الواجهة النشطة (Window Handle).
        /// </summary>
        public IntPtr HWnd { get; init; }

        /// <summary>
        /// معرّف العملية (Process ID) التابعة لها النافذة.
        /// </summary>
        public int ProcessId { get; init; }

        /// <summary>
        /// عنوان شريط النافذة النشطة.
        /// </summary>
        public string WindowTitle { get; init; }

        /// <summary>
        /// المدة الزمنية التي قضاها المستخدم دون أي مدخلات فأرة أو لوحة مفاتيح.
        /// </summary>
        public TimeSpan IdleTime { get; init; }
        public bool CanCapture { get; init; }

        /// <summary>
        /// منشئ هيكل لقطة النافذة.
        /// </summary>
        /// <param name="hWnd">مقبض النافذة.</param>
        /// <param name="processId">معرّف العملية.</param>
        /// <param name="windowTitle">عنوان النافذة.</param>
        /// <param name="idleTime">وقت الخمول.</param>
        public WindowSnapshot(IntPtr hWnd, int processId, string windowTitle, TimeSpan idleTime)
        {
            HWnd = hWnd;
            ProcessId = processId;
            WindowTitle = windowTitle;
            IdleTime = idleTime;
        }
    }

    /// <summary>
    /// سجل لتخزين بيانات النافذة المرشحة لتكون النشاط التالي للتحقق من استقرارها عبر آلية الـ Debounce.
    /// </summary>
    /// <param name="ProcessId">معرّف العملية.</param>
    /// <param name="ProcessName">اسم العملية البرمجية.</param>
    /// <param name="WindowTitle">عنوان النافذة.</param>
    /// <param name="ProcessPath">المسار الكامل للملف التنفيذي.</param>
    /// <param name="ContextPath">مسار الملف أو سياق التصفح.</param>
    /// <param name="FirstObservedAtUtc">التوقيت اللحظي لأول رصد لهذه النافذة (UTC).</param>
    private sealed record WindowCandidate(
        int ProcessId,
        string ProcessName,
        string WindowTitle,
        string? ProcessPath,
        string? ContextPath,
        DateTime FirstObservedAtUtc);

    /// <summary>
    /// الحلقة المستمرة لخيط المراقبة لاستطلاع النوافذ وفترات الخمول والتفاعل مع أحداث الإيقاظ.
    /// </summary>
    /// <param name="hDesk">مقبض سطح المكتب التفاعلي لربطه بالخيط.</param>
    private void RunMonitoringThread(IntPtr hDesk)
    {
        try
        {
            // إذا توفر مقبض سطح المكتب، نقوم بربط الخيط الحالي به ليتمكن من قراءة واجهات المستخدم
            if (hDesk != IntPtr.Zero)
            {
                var bound = NativeMethods.SetThreadDesktop(hDesk);
                int err = Marshal.GetLastWin32Error();
                _logger.LogInformation("Attached ActivityTracker thread to desktop: {Bound} (LastError={Err}, TargetHandle={Handle})", bound, err, hDesk);
            }
        }
        catch (Exception ex)
        {
            // تسجيل تحذير في حال تعذر ربط الخيط بسطح المكتب الافتراضي
            _logger.LogWarning(ex, "Could not attach thread to desktop 'default'");
        }

        // تحديد الفاصل الزمني للاستطلاع بحد أدنى 250 ميلي ثانية
        var interval = Math.Max(250, _options.PollingIntervalMs);

        // الاستمرار في حلقة العمل ما لم يتم طلب إلغاء التشغيل
        while (_cts != null && !_cts.IsCancellationRequested)
        {
            try
            {
                // التقاط حالة النافذة الأمامية ووقت الخمول على هذا الخيط
                var snapshot = CaptureWindowSnapshotOnThread();

                // معالجة اللقطة بشكل غير متزامن مع انتظار اكتمالها
                ProcessSnapshotAsync(snapshot).GetAwaiter().GetResult();

                // حساب مدة الانتظار بناءً على الحالة الحالية:
                // - إذا كان خاملًا: انتظار 250 ميلي ثانية لتفقد عودة النشاط بسرعة.
                // - إذا كانت هناك نافذة معلقة للتحقق من استقرارها: ننتظر فترة الـ Debounce المحددة.
                // - خلاف ذلك: ننتظر الفاصل الزمني العادي.
                var waitInterval = _isIdle
                    ? Math.Min(interval, 250)
                    : _pendingWindowCandidate == null
                        ? interval
                        : Math.Min(interval, Math.Max(50, _options.WindowDebounceMs));

                // انتظار انقضاء الفترة أو تلقي إشارة إيقاظ فورية
                _wakeUpEvent.WaitOne(waitInterval);
            }
            catch (Exception ex)
            {
                // تسجيل الخطأ الناتج عن حلقة التتبع
                _logger.LogError(ex, "Error in ActivityTracker loop");

                // انتظار فترة فاصلة لتجنب استهلاك الموارد بحلقة أخطاء سريعة
                _wakeUpEvent.WaitOne(interval);
            }
        }

        // إغلاق مقبض سطح المكتب عند انتهاء الخيط لتحرير الموارد
        if (hDesk != IntPtr.Zero)
        {
            NativeMethods.CloseDesktop(hDesk);
        }
    }

    /// <summary>
    /// خيط مخصص لتثبيت خطافات أحداث نظام ويندوز (WinEvent Hooks) وإدارة مضخة الرسائل (Message Pump).
    /// </summary>
    private void RunWinEventHookThread()
    {
        IntPtr foregroundHook = IntPtr.Zero;
        IntPtr nameChangeHook = IntPtr.Zero;
        try
        {
            // تخزين معرّف الخيط الحالي ليتمكن خيط الإيقاف من إرسال رسالة WM_QUIT إليه
            _winEventThreadId = NativeMethods.GetCurrentThreadId();

            // تجهيز دالة الاستدعاء لخطافات النظام
            _winEventDelegate = OnWinEvent;

            // تثبيت خطاف للاستماع لتغير النافذة الأمامية النشطة
            foregroundHook = NativeMethods.SetWinEventHook(
                NativeMethods.EVENT_SYSTEM_FOREGROUND,
                NativeMethods.EVENT_SYSTEM_FOREGROUND,
                IntPtr.Zero,
                _winEventDelegate,
                0,
                0,
                NativeMethods.WINEVENT_OUTOFCONTEXT | NativeMethods.WINEVENT_SKIPOWNPROCESS);

            // تثبيت خطاف للاستماع لتغير عناوين وأسماء النوافذ
            nameChangeHook = NativeMethods.SetWinEventHook(
                NativeMethods.EVENT_OBJECT_NAMECHANGE,
                NativeMethods.EVENT_OBJECT_NAMECHANGE,
                IntPtr.Zero,
                _winEventDelegate,
                0,
                0,
                NativeMethods.WINEVENT_OUTOFCONTEXT | NativeMethods.WINEVENT_SKIPOWNPROCESS);

            // إجبار النظام على إنشاء طابور رسائل لهذا الخيط فوراً
            NativeMethods.PeekMessage(out _, IntPtr.Zero, 0, 0, NativeMethods.PM_NOREMOVE);

            // إشعار الخيط الرئيسي بأن الخطافات أصبحت جاهزة
            _winEventHookReady.Set();

            // إذا فشل تثبيت كلا الخطافين، نعتمد على آلية الاستطلاع الدورية (Polling) فقط
            if (foregroundHook == IntPtr.Zero && nameChangeHook == IntPtr.Zero)
            {
                _logger.LogWarning("Windows event hooks could not be installed; polling fallback remains active.");
                return;
            }

            // مضخة الرسائل القياسية لنظام ويندوز للحفاظ على عمل الخطافات
            while (_cts != null && !_cts.IsCancellationRequested)
            {
                // سحب الرسالة التالية من طابور الرسائل
                var result = NativeMethods.GetMessage(out var message, IntPtr.Zero, 0, 0);

                // الخروج من الحلقة عند استقبال رسالة الخروج WM_QUIT أو حدوث خطأ
                if (result <= 0)
                {
                    break;
                }

                // ترجمة الرسائل إذا لزم الأمر
                NativeMethods.TranslateMessage(in message);

                // إرسال وتوزيع الرسالة لمعالجها
                NativeMethods.DispatchMessage(in message);
            }
        }
        catch (Exception ex)
        {
            // تسجيل تحذير في حال فشل خيط الخطافات والاعتماد على الاستطلاع
            _logger.LogWarning(ex, "Windows event hook thread failed; polling fallback remains active.");
            _winEventHookReady.Set();
        }
        finally
        {
            // إلغاء تثبيت خطاف النافذة الأمامية وتحريره
            if (foregroundHook != IntPtr.Zero) NativeMethods.UnhookWinEvent(foregroundHook);

            // إلغاء تثبيت خطاف تغير العنوان وتحريره
            if (nameChangeHook != IntPtr.Zero) NativeMethods.UnhookWinEvent(nameChangeHook);

            // تصفير معرّف الخيط بعد الانتهاء
            _winEventThreadId = 0;
        }
    }

    /// <summary>
    /// دالة استدعاء يتم تنفيذها عند حدوث حدث ويندوز خاص بتغيير النافذة الأمامية أو اسمها.
    /// </summary>
    /// <param name="hook">مقبض الخطاف.</param>
    /// <param name="eventType">نوع الحدث المستلم من النظام.</param>
    /// <param name="window">مقبض النافذة المعنية.</param>
    /// <param name="objectId">معرّف الكائن داخل النافذة.</param>
    /// <param name="childId">معرّف العنصر الفرعي.</param>
    /// <param name="eventThread">معرّف الخيط المسبب للحدث.</param>
    /// <param name="eventTime">الوقت الزمني للنظام بالمللي ثانية.</param>
    private void OnWinEvent(
        IntPtr hook,
        uint eventType,
        IntPtr window,
        int objectId,
        int childId,
        uint eventThread,
        uint eventTime)
    {
        // تجاهل الأحداث التي لا ترتبط بنافذة صالحة
        if (window == IntPtr.Zero || (eventType == NativeMethods.EVENT_OBJECT_NAMECHANGE && window != NativeMethods.GetForegroundWindow()))
        {
            return;
        }

        // إذا كان الحدث هو تبديل النافذة الأمامية أو تعديل عنوان نافذة رئيسية
        if (eventType == NativeMethods.EVENT_SYSTEM_FOREGROUND ||
            (eventType == NativeMethods.EVENT_OBJECT_NAMECHANGE &&
             objectId == NativeMethods.OBJID_WINDOW && childId == 0))
        {
            // إيقاظ خيط المراقبة فوراً لمعالجة التغيير دون تأخير
            _wakeUpEvent.Set();
        }
    }

    /// <summary>
    /// تلتقط لقطة لحظية لحالة النافذة الأمامية ووقت خمول المستخدم عبر استدعاء دوال Win32.
    /// </summary>
    /// <returns>كائن يحتوي على مقبض النافذة ومعرّف العملية وعنوانها ووقت الخمول.</returns>
    private WindowSnapshot CaptureWindowSnapshotOnThread()
    {
        // الحصول على مدة خمول المستخدم عن طريق فحص آخر إدخال (GetLastInputInfo)
        var idleTime = NativeMethods.GetIdleTime();

        // الحصول على مقبض النافذة النشطة في الواجهة حالياً
        var hWnd = NativeMethods.GetForegroundWindow();
        int processId = 0;
        string windowTitle = string.Empty;

        // إذا كانت النافذة صالحة وليست صفراً
        if (hWnd != IntPtr.Zero)
        {
            // استخراج معرّف العملية (Process ID) التابعة للنافذة
            NativeMethods.GetWindowThreadProcessId(hWnd, out uint pid);
            processId = (int)pid;

            // قراءة نص عنوان النافذة من شريط العنوان
            windowTitle = NativeMethods.GetWindowTitle(hWnd);
        }

        // إرجاع لقطة النافذة متضمنة كافة التفاصيل الملتقطة
        return new WindowSnapshot(hWnd, processId, windowTitle, idleTime) { CanCapture = NativeMethods.IsInteractiveDesktopAvailable() };
    }

    /// <summary>
    /// إيقاف خدمة التتبع بشكل آمن، وإلغاء خيوط المراقبة، وإنهاء وحفظ كافة الأنشطة المعلقة.
    /// </summary>
    /// <param name="ct">رمز إلغاء اختياري لقطع عملية الإيقاف.</param>
    /// <returns>مهمة تمثل العملية غير المتزامنة.</returns>
    public async Task StopAsync(CancellationToken ct = default)
    {
        // إذا لم تكن الخدمة تعمل أصلاً، نخرج مباشرة
        if (!_isStarted)
        {
            return;
        }

        // تسجيل رسالة بدء عملية الإيقاف
        _logger.LogInformation("Stopping ActivityTracker...");

        // إلغاء الاشتراك في أحداث نظام ويندوز لمنع استقبالها أثناء الإيقاف
        SystemEvents.SessionSwitch -= OnSessionSwitch;
        SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        SystemEvents.SessionEnding -= OnSessionEnding;

        // طلب إلغاء المهام والخيوط عبر الرمز
        _cts?.Cancel();

        // إطلاق حدث الإيقاظ لفك حظر خيط المراقبة من الانتظار
        _wakeUpEvent.Set();

        // إرسال رسالة WM_QUIT لخيط خطافات ويندوز لإغلاق مضخة الرسائل
        if (_winEventThreadId != 0)
        {
            NativeMethods.PostThreadMessage(_winEventThreadId, NativeMethods.WM_QUIT, UIntPtr.Zero, IntPtr.Zero);
        }

        // انتظار انتهاء خيط المراقبة لمدة ثانيتين كحد أقصى
        if (_monitorThread != null)
        {
            try
            {
                _monitorThread.Join(35000);
            }
            catch { }
        }

        // انتظار انتهاء خيط خطافات ويندوز لمدة ثانيتين كحد أقصى
        if (_winEventThread != null)
        {
            try { _winEventThread.Join(2000); } catch { }
        }

        // الحصول على قفل التزامن لإنهاء الأنشطة وتخزينها في قاعدة البيانات بأمان
        Task events;
        lock (_eventOrderLock) events = _eventTail;
        await events.WaitAsync(ct);
        await _syncLock.WaitAsync(ct);
        try
        {
            // إنهاء النشاط النشط الحالي إن وجد
            if (_currentActivity != null)
            {
                await FinalizeCurrentActivityAsync();
            }

            // إنهاء نشاط الخمول الحالي إن وجد
            if (_currentIdleActivity != null)
            {
                await FinalizeCurrentIdleActivityAsync();
            }
        }
        finally
        {
            // تحرير القفل التزامني
            _syncLock.Release();
        }

        // تصفير المرشح المعلق وإعادة تعيين الحالة إلى غير مشغلة
        _pendingWindowCandidate = null;
        _isStarted = false;

        // تسجيل اكتمال إيقاف المتتبع
        _logger.LogInformation("ActivityTracker stopped.");
    }

    /// <summary>
    /// معالجة لقطة النافذة الملتقطة، وتحديد حالات الخمول، وتغطية التسجيل، وتبديل النوافذ.
    /// </summary>
    /// <param name="snapshot">لقطة بيانات النافذة الحالية.</param>
    /// <returns>مهمة تمثل العملية غير المتزامنة.</returns>
    private async Task ProcessSnapshotAsync(WindowSnapshot snapshot)
    {
        // إذا كان النظام مقفلاً أو في وضع السكون، نتجاهل المعالجة لتجنب تسجيل بيانات غير دقيقة
        if (_isLocked || _isSuspended) return;

        // تسجيل تفاصيل اللقطة في السجل التشخيصي المفصل (Trace)
        _logger.LogTrace("Sample: hWnd={HWnd}, PID={Pid}, Title='{Title}', Idle={Idle:mm\\:ss}",
            snapshot.HWnd, snapshot.ProcessId, snapshot.WindowTitle, snapshot.IdleTime);

        // انتظار الحصول على قفل التزامن لمنع تضارب الخيوط
        await _syncLock.WaitAsync();
        try
        {
            // استخراج الحد الزمني المسموح به للخمول من الإعدادات
            if (_isLocked || _isSuspended || _cts?.IsCancellationRequested == true) return;
            if (!snapshot.CanCapture)
            {
                if (_currentActivity != null) await FinalizeCurrentActivityAsync();
                await _recordingService.StopRecordingSegmentAsync();
                return;
            }
            var idleThreshold = TimeSpan.FromMinutes(_options.IdleThresholdMinutes);

            // إذا تجاوز وقت خمول المستخدم الحد المسموح، ننتقل لمعالجة حالة الخمول
            if (snapshot.IdleTime >= idleThreshold)
            {
                await HandleIdleStateAsync(snapshot.IdleTime);
                return;
            }

            // إذا كان المستخدم في حالة خمول وعاد للنشاط الآن، نقوم بمعالجة استئناف النشاط
            if (_isIdle)
            {
                if (!await HandleResumeFromIdleAsync())
                {
                    return;
                }
            }

            // التأكد من أن تسجيل الشاشة بالفيديو نشط ويغطي هذه الفترة
            if (!await EnsureRecordingCoverageAsync())
            {
                // إذا لم يتوفر تسجيل فيديو نشط، نقوم بإنهاء النشاط المعلق لمنع فقدان التزامن
                if (_currentActivity != null)
                {
                    await FinalizeCurrentActivityAsync();
                }
                _pendingWindowCandidate = null;
                return;
            }

            // معالجة حالة النافذة النشطة الحالية والتحقق من التغييرات
            if (_options.Enabled) await HandleActiveWindowStateAsync(snapshot);
        }
        finally
        {
            // تحرير القفل التزامني في كل الأحوال
            _syncLock.Release();
        }
    }

    /// <summary>
    /// معالجة دخول المستخدم في حالة خمول، وحساب بداية وقت الخمول، وتحديث التسجيل وقاعدة البيانات.
    /// </summary>
    /// <param name="idleTime">المدة الزمنية الإجمالية للخمول.</param>
    /// <returns>مهمة تمثل العملية غير المتزامنة.</returns>
    private async Task HandleIdleStateAsync(TimeSpan idleTime)
    {
        if (!_recordingOptions.CurrentValue.RecordOnlyWhenUserActive && !_isLocked && !_isSuspended)
            await _recordingService.EnsureRecordingActiveAsync(_cts?.Token ?? CancellationToken.None);
        // التحقق مما إذا كان هذا هو الدخول الأول في حالة الخمول
        if (!_isIdle)
        {
            // تسجيل رسالة معلوماتية تدل على بدء حالة الخمول
            _logger.LogInformation("User entered IDLE state (Idle duration: {Duration})", idleTime);

            var now = DateTime.UtcNow;

            // حساب التوقيت الفعلي الدقيق لبدء الخمول بطرح مدة الخمول من الوقت الحالي
            var idleStartUtc = ActivityTimelineMath.CalculateIdleStartUtc(now, idleTime);

            // إنهاء النشاط النشط السابق حتى لحظة بدء الخمول الدقيقة
            if (_currentActivity != null)
            {
                await FinalizeCurrentActivityAsync(idleStartUtc);
            }

            // إنشاء سجل نشاط جديد خاص بحالة الخمول
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
                EndedAtUtc = now,
                DurationSeconds = ActivityTimelineMath.CalculateDurationSeconds(idleStartUtc, now),
                RecordingSegmentId = _recordingService.CurrentSegment?.Id,
                CreatedAtUtc = now
            };

            // حفظ سجل الخمول في قاعدة البيانات
            _isIdle = true;
            await PersistActivityAsync(_currentIdleActivity);

            // وضع علامة الخمول إلى صحيح
            _isIdle = true;
            _lastFlushTime = DateTime.UtcNow;

            // إيقاف مقطع تسجيل الشاشة إذا كان خيار التسجيل مقتصراً على فترة نشاط المستخدم فقط
            if (_recordingOptions.CurrentValue.RecordOnlyWhenUserActive)
            {
                await _recordingService.StopRecordingSegmentAsync();
            }
        }
        // في حال كان المستخدم لا يزال في حالة الخمول بالفعل
        else if (_currentIdleActivity != null)
        {
            var now = DateTime.UtcNow;

            // تحديث وقت النهاية والمدة الإجمالية لنشاط الخمول المستمر
            _currentIdleActivity.EndedAtUtc = now;
            _currentIdleActivity.DurationSeconds = ActivityTimelineMath.CalculateDurationSeconds(_currentIdleActivity.StartedAtUtc, now);

            // حفظ دوري كل 30 ثانية لتحديث مدة الخمول في قاعدة البيانات
            if (_currentIdleActivity.Id == 0 || (now - _lastFlushTime).TotalSeconds >= 30)
            {
                await PersistActivityAsync(_currentIdleActivity);
                _lastFlushTime = now;
            }
        }
    }

    /// <summary>
    /// معالجة استئناف المستخدم للنشاط بعد فترة الخمول وإنهاء سجل الخمول الحالي.
    /// </summary>
    /// <returns>القيمة true إذا تم الاستئناف بنجاح وتوفر التسجيل؛ وإلا false.</returns>
    private async Task<bool> HandleResumeFromIdleAsync()
    {
        // التحقق من ضمان توفر تغطية تسجيل فيديو نشطة
        if (!await EnsureRecordingCoverageAsync())
        {
            return false;
        }

        // تسجيل استئناف المستخدم للنشاط
        _logger.LogInformation("User returned from IDLE state; video recording and application tracking resumed together.");

        // إنهاء وإغلاق نشاط الخمول السابق في قاعدة البيانات
        if (_currentIdleActivity != null)
        {
            await FinalizeCurrentIdleActivityAsync();
        }

        // إعادة ضبط علامة الخمول
        _isIdle = false;
        return true;
    }

    /// <summary>
    /// التأكد من وجود مقطع تسجيل فيديو نشط وفعال ومطابق للإعدادات.
    /// </summary>
    /// <returns>القيمة true إذا كان التسجيل نشطاً ومتاحاً؛ وإلا false.</returns>
    private async Task<bool> EnsureRecordingCoverageAsync()
    {
        // التحقق مما إذا كان خيار تسجيل الفيديو مفعلاً من الإعدادات
        if (!_recordingOptions.CurrentValue.Enabled) return true;

        // إذا كان التسجيل قيد العمل ومقطع الفيديو الحالي موجود وصالح
        if (_recordingService.IsRecording && _recordingService.CurrentSegment != null)
        {
            return true;
        }

        // طلب بدء أو التأكد من تشغيل مقطع تسجيل جديد
        var segment = await _recordingService.EnsureRecordingActiveAsync(_cts?.Token ?? CancellationToken.None);

        // إرجاع النتيجة بناءً على نجاح الحصول على مقطع وحالة التسجيل
        return segment != null && _recordingService.IsRecording;
    }

    /// <summary>
    /// معالجة حالة النافذة النشطة المحددة، استخراج سياقها، وتطبيق آلية الاستقرار (Debounce)، وحفظ النشاط.
    /// </summary>
    /// <param name="snapshot">لقطة بيانات النافذة الحالية.</param>
    /// <returns>مهمة تمثل العملية غير المتزامنة.</returns>
    private async Task HandleActiveWindowStateAsync(WindowSnapshot snapshot)
    {
        string processName = "explorer";
        int processId = snapshot.ProcessId;
        string windowTitle = snapshot.WindowTitle;
        string? processPath = null;
        string? contextPath = null;

        // إذا كانت النافذة صالحة ومعرّف العملية أكبر من صفر
        if (snapshot.HWnd != IntPtr.Zero && processId > 0)
        {
            try
            {
                // استخراج اسم العملية البرمجية من معرّفها
                using var proc = Process.GetProcessById(processId);
                processName = proc.ProcessName;
            }
            catch { }

            // إذا كان عنوان النافذة فارغاً، نستخدم اسم العملية كعنوان بديل
            if (string.IsNullOrWhiteSpace(windowTitle))
            {
                windowTitle = processName;
            }

            // استخراج المسار الكامل للملف التنفيذي
            processPath = NativeMethods.GetProcessExecutablePath(processId);

            // استخراج سياق العمل الإضافي (مثل مسار الملف المفتوح أو الرابط)
            contextPath = _contextProvider.ResolveContext(processName, snapshot.HWnd, windowTitle);
        }
        else
        {
            // حالة سطح المكتب (Desktop) عندما لا تكون هناك نافذة محددة في المقدمة
            windowTitle = "Desktop";
            contextPath = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
        }

        var now = DateTime.UtcNow;

        // التحقق مما إذا كان المستخدم لا يزال يعمل على نفس التطبيق والسياق الحاليين
        if (MatchesCurrentActivity(processId, processName, windowTitle, contextPath))
        {
            // إلغاء أي مرشح نافذة معلق لكوننا في نفس النشاط المستقر
            _pendingWindowCandidate = null;

            // تحديث وقت النهاية والمدة الزمنية للنشاط الحالي
            _currentActivity!.EndedAtUtc = now;
            _currentActivity.DurationSeconds = ActivityTimelineMath.CalculateDurationSeconds(_currentActivity.StartedAtUtc, now);

            // ربط النشاط بمعرّف مقطع التسجيل إذا لم يكن مرتبطاً بعد
            if (_currentActivity.RecordingSegmentId == null && _recordingService.CurrentSegment != null)
            {
                _currentActivity.RecordingSegmentId = _recordingService.CurrentSegment.Id;
            }

            // ترحيل وتحديث دوري في قاعدة البيانات كل 30 ثانية
            if (_currentActivity.Id == 0 || (now - _lastFlushTime).TotalSeconds >= 30)
            {
                await PersistActivityAsync(_currentActivity);
                _lastFlushTime = now;
            }

            return;
        }

        // إنشاء كائن مرشح جديد يمثل الانتقال المحتمل للنافذة الجديدة
        var candidate = new WindowCandidate(
            processId,
            processName,
            windowTitle,
            processPath,
            contextPath,
            now);

        // استخراج مهلة استقرار النوافذ (Window Debounce) لمنع تسجيل النوافذ السريعة العابرة
        var debounce = TimeSpan.FromMilliseconds(Math.Max(0, _options.WindowDebounceMs));
        if (debounce > TimeSpan.Zero)
        {
            // إذا لم يكن هناك مرشح معلق أو تغيرت النافذة المرشحة، نحفظ المرشح الجديد وننتظر
            if (_pendingWindowCandidate == null || !MatchesCandidate(_pendingWindowCandidate, candidate))
            {
                _pendingWindowCandidate = candidate;
                return;
            }

            // إذا لم تنقضِ مهلة الاستقرار بعد، ننتظر دورة الاستطلاع التالية
            if (now - _pendingWindowCandidate.FirstObservedAtUtc < debounce)
            {
                return;
            }

            // اعتمدنا المرشح بعد استقراره وانقضاء المهلة
            candidate = _pendingWindowCandidate;
        }

        // تصفير المرشح المعلق بعد اعتماده
        _pendingWindowCandidate = null;

        // اعتماد التوقيت الفعلي لأول ظهور للنافذة كوقت بدء الانتقال
        var transitionAtUtc = candidate.FirstObservedAtUtc;

        // إذا كان هناك نشاط سابق، نقوم بإنهائه عند لحظة الانتقال المعتمدة
        if (_currentActivity != null)
        {
            await FinalizeCurrentActivityAsync(transitionAtUtc);
        }

        // إنشاء سجل نشاط جديد للنافذة الجديدة
        _currentActivity = new ActivityEvent
        {
            MachineId = Environment.MachineName,
            SessionId = GetCurrentSessionId(),
            UserIdentifier = Environment.UserName,
            ProcessName = candidate.ProcessName,
            ProcessId = candidate.ProcessId,
            ProcessPath = candidate.ProcessPath,
            WindowTitle = candidate.WindowTitle,
            ContextPath = candidate.ContextPath,
            EventType = ActivityEventType.ActiveWindow,
            StartedAtUtc = transitionAtUtc,
            EndedAtUtc = now,
            DurationSeconds = ActivityTimelineMath.CalculateDurationSeconds(transitionAtUtc, now),
            RecordingSegmentId = _recordingService.CurrentSegment?.Id,
            CreatedAtUtc = now
        };

        // إضافة سجل النشاط الجديد في مستودع قاعدة البيانات
        await PersistActivityAsync(_currentActivity);
        _lastFlushTime = now;

        // تسجيل رسالة تنقيح تدل على تبديل النافذة النشطة
        _logger.LogDebug("Active window changed to process {ProcessName} (PID {ProcessId})",
            candidate.ProcessName, candidate.ProcessId);

        // إطلاق حدث بدء النشاط إن كان هناك مشتركون فيه
        if (ActivityStarted != null)
        {
            await ActivityStarted.Invoke(_currentActivity);
        }
    }

    /// <summary>
    /// يقارن البيانات الملتقطة حالياً مع النشاط النشط لتحديد ما إذا كانا متطابقين.
    /// </summary>
    /// <param name="processId">معرّف العملية.</param>
    /// <param name="processName">اسم العملية.</param>
    /// <param name="windowTitle">عنوان النافذة.</param>
    /// <param name="contextPath">سياق العمل أو المسار.</param>
    /// <returns>القيمة true إذا كان النشاط متطابقاً؛ وإلا false.</returns>
    private bool MatchesCurrentActivity(int processId, string processName, string windowTitle, string? contextPath) =>
        _currentActivity != null &&
        _currentActivity.ProcessId == processId &&
        string.Equals(_currentActivity.ProcessName, processName, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(_currentActivity.WindowTitle, windowTitle, StringComparison.Ordinal) &&
        string.Equals(_currentActivity.ContextPath, contextPath, StringComparison.Ordinal);

    /// <summary>
    /// يقارن بين مرشحين للتأكد من أنهما يشيران إلى نفس النافذة والسياق.
    /// </summary>
    /// <param name="left">المرشح الأول.</param>
    /// <param name="right">المرشح الثاني.</param>
    /// <returns>القيمة true إذا كانا متطابقين؛ وإلا false.</returns>
    private static bool MatchesCandidate(WindowCandidate left, WindowCandidate right) =>
        left.ProcessId == right.ProcessId &&
        string.Equals(left.ProcessName, right.ProcessName, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(left.WindowTitle, right.WindowTitle, StringComparison.Ordinal) &&
        string.Equals(left.ContextPath, right.ContextPath, StringComparison.Ordinal);

    /// <summary>
    /// إنهاء النشاط النشط الحالي، وتحديد وقت انتهائه وحساب مدته، وتحديثه في قاعدة البيانات.
    /// </summary>
    /// <param name="requestedEndUtc">توقيت الانتهاء المطلوب (UTC) أو استخدام الوقت الحالي اختيارياً.</param>
    /// <returns>مهمة تمثل العملية غير المتزامنة.</returns>
    private async Task PersistActivityAsync(ActivityEvent activity)
    {
        if (!_options.Enabled) return;
        if (activity.Id == 0) await _activityRepository.AddAsync(activity);
        else await _activityRepository.UpdateAsync(activity);
    }

    private async Task FinalizeCurrentActivityAsync(DateTime? requestedEndUtc = null)
    {
        var activity = _currentActivity;
        if (activity == null) return;
        var end = ActivityTimelineMath.ClampEndUtc(activity.StartedAtUtc, requestedEndUtc ?? DateTime.UtcNow);
        activity.EndedAtUtc = end;
        activity.DurationSeconds = ActivityTimelineMath.CalculateDurationSeconds(activity.StartedAtUtc, end);
        activity.RecordingSegmentId ??= _recordingService.CurrentSegment?.Id;
        await PersistActivityAsync(activity);
        if (ActivityEnded != null) await ActivityEnded.Invoke(activity);
        if (ReferenceEquals(_currentActivity, activity)) _currentActivity = null;
    }

    /// <summary>
    /// إنهاء نشاط الخمول الحالي وتحديثه في قاعدة البيانات قبل العودة للنشاط العادي.
    /// </summary>
    /// <returns>مهمة تمثل العملية غير المتزامنة.</returns>
    private async Task FinalizeCurrentIdleActivityAsync()
    {
        // إذا لم يكن هناك نشاط خمول نشط، نخرج
        if (_currentIdleActivity == null) return;

        var now = DateTime.UtcNow;

        // تعيين وقت نهاية الخمول وحساب مدته الكلية
        _currentIdleActivity.EndedAtUtc = now;
        _currentIdleActivity.DurationSeconds = ActivityTimelineMath.CalculateDurationSeconds(_currentIdleActivity.StartedAtUtc, now);

        // تحديث سجل الخمول في المستودع
        if (_currentIdleActivity != null)
        {
            await PersistActivityAsync(_currentIdleActivity);
        }

        // تفريغ نشاط الخمول الحالي
        _currentIdleActivity = null;
    }

    /// <summary>
    /// معالج أحداث تبديل جلسة نظام ويندوز (مثل قفل الشاشة، إلغاء القفل، تسجيل الدخول أو الخروج).
    /// </summary>
    /// <param name="sender">مصدر الحدث.</param>
    /// <param name="e">معلومات حدث تبديل الجلسة والسبب.</param>
    private void OnSessionSwitch(object sender, SessionSwitchEventArgs e)
    {
        // تسجيل سبب تبديل الجلسة
        var observedAtUtc = DateTime.UtcNow;
        if (e.Reason is SessionSwitchReason.SessionLock or SessionSwitchReason.SessionLogoff or
            SessionSwitchReason.ConsoleDisconnect or SessionSwitchReason.RemoteDisconnect) _isLocked = true;
        else if (e.Reason is SessionSwitchReason.SessionUnlock or SessionSwitchReason.SessionLogon or
                 SessionSwitchReason.ConsoleConnect or SessionSwitchReason.RemoteConnect) _isLocked = false;
        _logger.LogInformation("Windows Session Switch event received: {Reason}", e.Reason);

        // تنفيذ المعالجة في مهمة خلفية غير متزامنة
        QueueSystemTransition(async () =>
        {
            // الحصول على قفل التزامن لحماية العمليات
            await _syncLock.WaitAsync();
            try
            {
                // إنهاء النشاط الحالي ونشاط الخمول عند تبديل حالة الجلسة
                if (_currentActivity != null) await FinalizeCurrentActivityAsync(observedAtUtc);
                if (_currentIdleActivity != null) await FinalizeCurrentIdleActivityAsync();
                _isIdle = false;
                _pendingWindowCandidate = null;

                // تحويل سبب تبديل الجلسة إلى نوع حدث النشاط المناسب
                ActivityEventType eventType = e.Reason switch
                {
                    SessionSwitchReason.SessionLock => ActivityEventType.SessionLock,
                    SessionSwitchReason.SessionUnlock => ActivityEventType.SessionUnlock,
                    SessionSwitchReason.SessionLogon => ActivityEventType.SessionLogon,
                    SessionSwitchReason.SessionLogoff => ActivityEventType.SessionLogoff,
                    SessionSwitchReason.ConsoleDisconnect => ActivityEventType.SessionLogoff,
                    SessionSwitchReason.RemoteDisconnect => ActivityEventType.SessionLogoff,
                    SessionSwitchReason.ConsoleConnect => ActivityEventType.SessionLogon,
                    SessionSwitchReason.RemoteConnect => ActivityEventType.SessionLogon,
                    _ => ActivityEventType.ActiveWindow
                };

                // تحديد ما إذا كانت الجلسة أصبحت غير نشطة (قفل، خروج، قطع اتصال)
                var sessionBecameInactive = e.Reason is
                    SessionSwitchReason.SessionLock or
                    SessionSwitchReason.SessionLogoff or
                    SessionSwitchReason.ConsoleDisconnect or
                    SessionSwitchReason.RemoteDisconnect;

                // تحديد ما إذا كانت الجلسة أصبحت نشطة (إلغاء قفل، تسجيل دخول، اتصال)
                var sessionBecameActive = e.Reason is
                    SessionSwitchReason.SessionUnlock or
                    SessionSwitchReason.SessionLogon or
                    SessionSwitchReason.ConsoleConnect or
                    SessionSwitchReason.RemoteConnect;

                // تحديث حالة قفل الجلسة
                if (sessionBecameInactive)
                {
                    await _recordingService.StopRecordingSegmentAsync();
                }
                // بدء أو التأكد من استمرار التسجيل فور تنشيط الجلسة ما لم يكن الجهاز معلقاً
                else if (sessionBecameActive && !_isLocked && !_isSuspended)
                {
                    await _recordingService.EnsureRecordingActiveAsync();
                }

                // إنشاء كائن نشاط يوثق حدث تبديل الجلسة
                var sessionEvent = new ActivityEvent
                {
                    MachineId = Environment.MachineName,
                    SessionId = GetCurrentSessionId(),
                    UserIdentifier = Environment.UserName,
                    ProcessName = "System",
                    ProcessId = 0,
                    WindowTitle = e.Reason.ToString(),
                    EventType = eventType,
                    StartedAtUtc = observedAtUtc,
                    EndedAtUtc = observedAtUtc,
                    DurationSeconds = 0,
                    RecordingSegmentId = _recordingService.CurrentSegment?.Id,
                    CreatedAtUtc = DateTime.UtcNow
                };

                // إضافة حدث الجلسة إلى المستودع
                if (_options.Enabled) await _activityRepository.AddAsync(sessionEvent);
            }
            catch (Exception ex)
            {
                // تسجيل الخطأ في حال فشل توثيق حدث تبديل الجلسة
                _logger.LogError(ex, "Failed to record session switch event {Reason}", e.Reason);
            }
            finally
            {
                // تحرير القفل التزامني
                _syncLock.Release();
            }
        });
    }

    /// <summary>
    /// معالج أحداث تغير وضع طاقة النظام (مثل دخول الجهاز في وضع السكون أو استئناف التشغيل).
    /// </summary>
    /// <param name="sender">مصدر الحدث.</param>
    /// <param name="e">معلومات وضع الطاقة.</param>
    private void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
    {
        // تسجيل نوع حدث وضع الطاقة
        var observedAtUtc = DateTime.UtcNow;
        if (e.Mode == PowerModes.Suspend) _isSuspended = true;
        else if (e.Mode == PowerModes.Resume) _isSuspended = false;
        _logger.LogInformation("PowerModeChanged event: {Mode}", e.Mode);

        // تجاهل تغيرات حالة البطارية/الشاحن العادية التي لا تتضمن سكوناً أو استيقاظاً
        if (e.Mode == PowerModes.StatusChange)
        {
            _logger.LogDebug("Ignoring power status-only change; no sleep/resume transition occurred.");
            return;
        }

        // تشغيل المعالجة في مهمة خلفية
        QueueSystemTransition(async () =>
        {
            // انتظار الحصول على القفل التزامني
            await _syncLock.WaitAsync();
            try
            {
                // إنهاء الأنشطة النشطة والخمول فوراً
                if (_currentActivity != null) await FinalizeCurrentActivityAsync(observedAtUtc);
                if (_currentIdleActivity != null) await FinalizeCurrentIdleActivityAsync();
                _isIdle = false;
                _pendingWindowCandidate = null;

                // في حالة دخول النظام في وضع السكون (Suspend)
                if (e.Mode == PowerModes.Suspend)
                {
                    // إيقاف تسجيل الفيديو قبل نوم النظام
                    await _recordingService.StopRecordingSegmentAsync();
                }
                // في حالة استئناف النظام من وضع السكون (Resume)
                else if (e.Mode == PowerModes.Resume)
                {
                }

                // تحديد نوع الحدث: نوم النظام أو استيقاظه
                var eventType = e.Mode == PowerModes.Suspend
                    ? ActivityEventType.SystemSleep
                    : ActivityEventType.SystemResume;

                // إنشاء حدث يوثق تغير وضع الطاقة في النظام
                var powerEvent = new ActivityEvent
                {
                    MachineId = Environment.MachineName,
                    SessionId = GetCurrentSessionId(),
                    UserIdentifier = Environment.UserName,
                    ProcessName = "System",
                    ProcessId = 0,
                    WindowTitle = e.Mode.ToString(),
                    EventType = eventType,
                    StartedAtUtc = observedAtUtc,
                    EndedAtUtc = observedAtUtc,
                    DurationSeconds = 0,
                    RecordingSegmentId = _recordingService.CurrentSegment?.Id,
                    CreatedAtUtc = DateTime.UtcNow
                };

                // حفظ حدث الطاقة في قاعدة البيانات
                if (_options.Enabled) await _activityRepository.AddAsync(powerEvent);
            }
            catch (Exception ex)
            {
                // تسجيل الخطأ في حال فشل تسجيل حدث الطاقة
                _logger.LogError(ex, "Failed to record power mode event {Mode}", e.Mode);
            }
            finally
            {
                // تحرير القفل التزامني
                _syncLock.Release();
            }
        });
    }

    /// <summary>
    /// معالج أحداث إنهاء الجلسة أو إيقاف تشغيل نظام ويندوز.
    /// </summary>
    /// <param name="sender">مصدر الحدث.</param>
    /// <param name="e">معلومات إنهاء الجلسة.</param>
    private void OnSessionEnding(object sender, SessionEndingEventArgs e)
    {
        // تسجيل استقبال حدث إنهاء الجلسة والسبب
        _logger.LogInformation("SessionEnding event: {Reason}", e.Reason);

        bool lockAcquired = false;
        try
        {
            _isLocked = true;

            // محاولة الحصول على القفل خلال ثانية واحدة كحد أقصى نظراً لسرعة إغلاق النظام
            lockAcquired = _syncLock.Wait(1000);
            if (lockAcquired)
            {
                // إنهاء النشاط الحالي بشكل متزامن
                if (_currentActivity != null)
                {
                    FinalizeCurrentActivityAsync().GetAwaiter().GetResult();
                }

                // إنهاء نشاط الخمول إن وجد
                if (_currentIdleActivity != null)
                {
                    FinalizeCurrentIdleActivityAsync().GetAwaiter().GetResult();
                }
            }

            // إيقاف تسجيل الفيديو بمهلة زمنية قدرها 5 ثوانٍ
            using var stopCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            _recordingService.StopRecordingSegmentAsync(stopCts.Token).GetAwaiter().GetResult();

            // تحديد ما إذا كان الحدث تسجيل خروج أو إيقاف تشغيل كلي
            var eventType = e.Reason == SessionEndReasons.Logoff
                ? ActivityEventType.SessionLogoff
                : ActivityEventType.SystemShutdown;

            // إنشاء وتوثيق حدث إنهاء النظام
            var shutdownEvent = new ActivityEvent
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

            // حفظ الحدث فوراً في قاعدة البيانات
            _activityRepository.AddAsync(shutdownEvent).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            // تسجيل الخطأ في حال حدوث استثناء أثناء إنهاء الجلسة
            _logger.LogError(ex, "Failed to record session ending event");
        }
        finally
        {
            // تحرير القفل إن تم الاستحواذ عليه
            if (lockAcquired)
            {
                _syncLock.Release();
            }
        }
    }

    /// <summary>
    /// يستخرج المعرّف الرقمي للجلسة الحالية من العملية المشغلة.
    /// </summary>
    /// <returns>معرّف الجلسة (Session ID) أو القيمة 1 كقيمة افتراضية.</returns>
    private static int GetCurrentSessionId()
    {
        try
        {
            // الحصول على معرّف الجلسة للعملية الحالية
            return Process.GetCurrentProcess().SessionId;
        }
        catch
        {
            // إرجاع القيمة الافتراضية 1 عند فشل القراءة
            return 1;
        }
    }

    /// <summary>
    /// تحرير وإلغاء كافة الموارد غير المدارة ومؤشرات التزامن ومصادر الإلغاء.
    /// </summary>
    public void Dispose()
    {
        // التخلص من مصدر رموز الإلغاء
        if (_monitorThread?.IsAlive == true || !_eventTail.IsCompleted) return;
        _cts?.Dispose();

        // التخلص من حدث الإيقاظ التلقائي
        _wakeUpEvent.Dispose();

        // التخلص من إشارة جاهزية خطافات ويندوز
        _winEventHookReady.Dispose();

        // التخلص من قفل التزامن
        _syncLock.Dispose();

        // منع استدعاء المنظف النهائي (Finalizer) لأن الموارد تم تحريرها بالكامل
        GC.SuppressFinalize(this);
    }
}
