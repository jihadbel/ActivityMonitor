using System;
using System.Threading;
using System.Threading.Tasks;
using ActivityMonitor.Agent.Recording;
using ActivityMonitor.Agent.Startup;
using ActivityMonitor.Agent.Tracking;
using ActivityMonitor.Core.Interfaces;
using ActivityMonitor.Core.Options;
using ActivityMonitor.Core.Models;
using System.Text.Json;
using System.Diagnostics;
using ActivityMonitor.Infrastructure.Data;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ActivityTrackingOptions = ActivityMonitor.Core.Options.ActivityTrackingOptions;

namespace ActivityMonitor.Agent;

/// <summary>
/// خدمة الخلفية الرئيسية للعميل (Agent Background Service)،
/// تقوم بتهيئة قاعدة البيانات، بدء وإيقاف تتبع النوافذ، ومراقبة استمرار تسجيل الشاشة وإجراء المطابقة الدورية.
/// </summary>
public class Worker : BackgroundService
{
    /// <summary>
    /// خدمة تتبع أنشطة النوافذ والخمول.
    /// </summary>
    private readonly IActivityTracker _tracker;

    /// <summary>
    /// خدمة تسجيل الشاشة بالفيديو.
    /// </summary>
    private readonly IScreenRecordingService _recordingService;

    /// <summary>
    /// خدمة تحديد وربط الأنشطة بمقاطع الفيديو.
    /// </summary>
    private readonly IRecordingLocatorService _locatorService;

    /// <summary>
    /// خيارات تتبع الأنشطة.
    /// </summary>
    private readonly IOptions<ActivityTrackingOptions> _trackingOptions;

    /// <summary>
    /// مراقب خيارات تسجيل الشاشة للتحديث المباشر.
    /// </summary>
    private readonly IOptionsMonitor<RecordingOptions> _recordingOptions;

    /// <summary>
    /// مسجل السجلات التشخيصية للخدمة.
    /// </summary>
    private readonly ILogger<Worker> _logger;

    /// <summary>
    /// منشئ الفئة: يقوم بحقن كافة الخدمات والخيارات المطلوبة لعمل العميل.
    /// </summary>
    /// <param name="tracker">خدمة تتبع الأنشطة.</param>
    /// <param name="recordingService">خدمة تسجيل الشاشة.</param>
    /// <param name="locatorService">خدمة ربط الأنشطة بالفيديوهات.</param>
    /// <param name="trackingOptions">خيارات التتبع.</param>
    /// <param name="recordingOptions">خيارات التسجيل.</param>
    /// <param name="logger">مسجل العمليات.</param>
    public Worker(
        IActivityTracker tracker,
        IScreenRecordingService recordingService,
        IRecordingLocatorService locatorService,
        IOptions<ActivityTrackingOptions> trackingOptions,
        IOptionsMonitor<RecordingOptions> recordingOptions,
        ILogger<Worker> logger)
    {
        _tracker = tracker;
        _recordingService = recordingService;
        _locatorService = locatorService;
        _trackingOptions = trackingOptions;
        _recordingOptions = recordingOptions;
        _logger = logger;
    }

    /// <summary>
    /// يتم تنفيذها عند بدء تشغيل خدمة العميل في جلسة المستخدم؛ لتهيئة قاعدة البيانات وتسجيل التشغيل التلقائي وبدء التتبع.
    /// </summary>
    /// <param name="cancellationToken">رمز إلغاء العملية.</param>
    /// <returns>مهمة تمثل العملية غير المتزامنة.</returns>
    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("ActivityMonitor Agent starting up in user session...");

        // تهيئة قاعدة البيانات وإنشاء الجداول إذا لم تكن موجودة؛ في حال فشلها يتوقف التشغيل لإشعار النظام
        await ActivityMonitorDbContext.InitializeDatabaseAsync(null, cancellationToken);
        _logger.LogInformation("Database initialized successfully.");

        try
        {
            // إدارة تسجيل التشغيل التلقائي مع بدء تشغيل ويندوز وفقاً لخيار المستخدم
            if (_trackingOptions.Value.StartAgentAtLogon)
            {
                // إذا لم يكن مسجلاً في بدء التشغيل، نقوم بتسجيله
                if (!StartupManager.IsStartupEnabled())
                {
                    if (StartupManager.EnableStartup())
                    {
                        _logger.LogInformation("Registered ActivityMonitor Agent in user startup.");
                    }
                    else
                    {
                        _logger.LogWarning("Could not register ActivityMonitor Agent in user startup.");
                    }
                }
            }
            else
            {
                // إذا كان مسجلاً والمستخدم عطل الخيار، نقوم بإلغاء تسجيله
                if (StartupManager.IsStartupEnabled())
                {
                    if (StartupManager.DisableStartup())
                    {
                        _logger.LogInformation("Disabled ActivityMonitor Agent in user startup per user configuration.");
                    }
                    else
                    {
                        _logger.LogWarning("Could not fully disable Agent startup registration; elevated settings may be required.");
                    }
                }
            }
        }
        catch (Exception ex)
        {
            // عدم إيقاف التطبيق في حال فشل تعديل تسجيل بدء التشغيل
            _logger.LogWarning(ex, "Could not update Agent auto-start registration");
        }

        // بدء خدمة تتبع الأنشطة وخيوط الاستطلاع
        await _tracker.StartAsync(cancellationToken);

        // إذا كان التسجيل مفعلاً وفي وضع التسجيل المستمر (ليس عند النشاط فقط)، نبدأ أول مقطع فيديو فوراً


        _logger.LogInformation("ActivityMonitor Agent is now running silently.");

        // استدعاء دالة البدء الأساسية
        await base.StartAsync(cancellationToken);
    }

    /// <summary>
    /// حلقة التنفيذ الخلفية المستمرة للعميل (Watchdog Loop)، تقوم بفحص صحة التسجيل وإعادة ربط الأنشطة كل 60 ثانية.
    /// </summary>
    /// <param name="stoppingToken">رمز إيقاف الخدمة.</param>
    /// <returns>مهمة تمثل العملية غير المتزامنة.</returns>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var lastLink = DateTime.MinValue;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var sessionId = Process.GetCurrentProcess().SessionId;
                var health = new AgentHealthSnapshot { ProcessId = Environment.ProcessId, SessionId = sessionId,
                    UpdatedAtUtc = DateTime.UtcNow, ActivityTrackingEnabled = _trackingOptions.Value.Enabled,
                    RecordingRequested = _recordingOptions.CurrentValue.Enabled &&
                        (!_recordingOptions.CurrentValue.RecordOnlyWhenUserActive || _tracker.IsUserActive),
                    IsRecording = _recordingService.IsRecording, SegmentId = _recordingService.CurrentSegment?.Id,
                    Error = _recordingService.LastError };
                var path = Path.Combine(ActivityMonitorPaths.RootDirectory, $"agent-health-{sessionId}.json");
                var temp = path + $".{Guid.NewGuid():N}.tmp";
                try { await File.WriteAllTextAsync(temp, JsonSerializer.Serialize(health), stoppingToken); File.Move(temp, path, true); }
                finally { if (File.Exists(temp)) File.Delete(temp); }
                if (DateTime.UtcNow - lastLink >= TimeSpan.FromSeconds(60))
                {
                    await _locatorService.LinkUnlinkedActivitiesAsync(DateTime.UtcNow.AddSeconds(-5), stoppingToken);
                    lastLink = DateTime.UtcNow;
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex) { _logger.LogError(ex, "Agent maintenance heartbeat failed"); }
            try { await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
        }
    }

    /// <summary>
    /// يتم تنفيذها عند إيقاف خدمة العميل؛ لإغلاق التتبع ومقاطع التسجيل وحفظ كافة البيانات العالقة.
    /// </summary>
    /// <param name="cancellationToken">رمز إلغاء عملية الإيقاف.</param>
    /// <returns>مهمة تمثل العملية غير المتزامنة.</returns>
    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("ActivityMonitor Agent stopping...");
        try { await _tracker.StopAsync(cancellationToken); }
        catch (Exception ex) { _logger.LogError(ex, "Activity tracking shutdown failed"); }
        try { await _recordingService.StopRecordingSegmentAsync(cancellationToken); }
        catch (Exception ex) { _logger.LogError(ex, "Recording finalization failed"); }
        try { await _locatorService.LinkUnlinkedActivitiesAsync(DateTime.UtcNow, cancellationToken); }
        catch (Exception ex) { _logger.LogError(ex, "Final activity linking failed"); }
        await base.StopAsync(cancellationToken);
    }
}
