using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using ActivityMonitor.Core.Interfaces;
using ActivityMonitor.Core.Options;
using ActivityMonitor.Infrastructure.Data;
using ActivityMonitor.Infrastructure.Services;
using ActivityMonitor.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ActivityMonitor.Reporting;

/// <summary>
/// العامل الخلفي لخدمة ويندوز (Windows Service Worker) التي تعمل في الجلسة صفر (Session 0)،
/// مسؤولة عن الصيانة الدورية لقاعدة البيانات، تطبيق سياسات الاحتفاظ بالسعة، تدقيق سلامة التسجيلات، وجدولة إصدار التقارير اليومية.
/// </summary>
public class MaintenanceWorker : BackgroundService
{
    /// <summary>
    /// مصنع إنشاء نطاقات الخدمات (Scopes) للحصول على الخدمات قصيرة العمر.
    /// </summary>
    private readonly IServiceScopeFactory _scopeFactory;

    /// <summary>
    /// مراقب خيارات التقارير.
    /// </summary>
    private readonly IOptionsMonitor<ReportsOptions> _reportsOptionsMonitor;

    /// <summary>
    /// مراقب خيارات التخزين.
    /// </summary>
    private readonly IOptionsMonitor<StorageOptions> _storageOptionsMonitor;

    /// <summary>
    /// مسجل السجلات التشخيصية للخدمة.
    /// </summary>
    private readonly ILogger<MaintenanceWorker> _logger;

    /// <summary>
    /// قراءة الإعدادات الحالية لخيارات التقارير.
    /// </summary>
    private ReportsOptions ReportsOptions => _reportsOptionsMonitor.CurrentValue;

    /// <summary>
    /// قراءة الإعدادات الحالية لخيارات التخزين.
    /// </summary>
    private StorageOptions StorageOptions => _storageOptionsMonitor.CurrentValue;

    /// <summary>
    /// منشئ الفئة: يستقبل مصنع النطاقات ومراقبي الخيارات والمسجل.
    /// </summary>
    /// <param name="scopeFactory">مصنع نطاقات الخدمات.</param>
    /// <param name="reportsOptionsMonitor">مراقب خيارات التقارير.</param>
    /// <param name="storageOptionsMonitor">مراقب خيارات التخزين.</param>
    /// <param name="logger">مسجل العمليات.</param>
    public MaintenanceWorker(
        IServiceScopeFactory scopeFactory,
        IOptionsMonitor<ReportsOptions> reportsOptionsMonitor,
        IOptionsMonitor<StorageOptions> storageOptionsMonitor,
        ILogger<MaintenanceWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _reportsOptionsMonitor = reportsOptionsMonitor;
        _storageOptionsMonitor = storageOptionsMonitor;
        _logger = logger;
    }

    /// <summary>
    /// يتم تنفيذها عند بدء تشغيل خدمة ويندوز لتهيئة قاعدة البيانات وإجراء تدقيق أولي للسلامة والتخزين.
    /// </summary>
    /// <param name="cancellationToken">رمز إلغاء العملية.</param>
    public override Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("ActivityMonitor maintenance starting for {Root}", ActivityMonitorPaths.RootDirectory);
        return base.StartAsync(cancellationToken);
    }

    /// <summary>
    /// حلقة التنفيذ الدورية المستمرة لخدمة ويندوز؛ تتولى المهام المجدولة (تدقيق السلامة، الاستبقاء، مؤشرات الصحة، وتوليد التقارير).
    /// </summary>
    /// <param name="stoppingToken">رمز إيقاف الخدمة.</param>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("ActivityMonitor Service background worker active.");

        var lastRetentionRun = DateTime.MinValue;
        var lastIntegrityRun = DateTime.MinValue;
        var lastBackfillRun = DateTime.MinValue;
        var initialized = false;
        var lastHealthRun = DateTime.MinValue;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (!initialized)
                {
                    await ActivityMonitorDbContext.InitializeDatabaseAsync(null, stoppingToken);
                    initialized = true;
                }
                var now = DateTime.UtcNow;

                // 1. تسجيل مؤشرات صحة النظام والسعة التخزينية كل 4 ساعات
                if ((now - lastHealthRun).TotalHours >= 4)
                {
                    await LogHealthMetricsAsync(stoppingToken);
                    lastHealthRun = now;
                }

                // 2. تدقيق سلامة البيانات والتسجيلات وإصلاح التلفيات كل 30 دقيقة
                if ((now - lastIntegrityRun).TotalMinutes >= 30)
                {
                    try
                    {
                        using var scope = _scopeFactory.CreateScope();
                        var integrityService = scope.ServiceProvider.GetRequiredService<IDataIntegrityService>();
                        await integrityService.AuditAndRepairAsync(stoppingToken);
                        lastIntegrityRun = now;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Scheduled data-integrity audit failed; other maintenance tasks will continue");
                    }
                }

                // 3. تطبيق سياسات استبقاء وحذف البيانات والتسجيلات القديمة كل ساعة
                if ((now - lastRetentionRun).TotalHours >= 1)
                {
                    try
                    {
                        _logger.LogInformation("Running scheduled retention policy check...");
                        using var scope = _scopeFactory.CreateScope();
                        var storageService = scope.ServiceProvider.GetRequiredService<IStorageManagementService>();
                        await storageService.EnforceRetentionPoliciesAsync(stoppingToken);
                        lastRetentionRun = now;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Scheduled retention failed; report scheduling will continue");
                    }
                }

                // 4. فحص استحقاق إنشاء التقارير اليومية عن اليوم المكتمل السابق
                if (ReportsOptions.DailyReportEnabled)
                {
                    if (now - lastBackfillRun >= TimeSpan.FromHours(1))
                    {
                        await CheckAndBackfillMissedReportsAsync(stoppingToken);
                        lastBackfillRun = now;
                    }
                    await CheckAndGenerateDailyReportAsync(now, stoppingToken);
                }

                // الانتظار لمدة 60 ثانية قبل الدورة التالية
                await Task.Delay(TimeSpan.FromSeconds(60), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unhandled exception in Windows Service execution loop");
                await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
            }
        }
    }

    /// <summary>
    /// قراءة وتسجيل مؤشرات السعة التخزينية والمساحة الحرة على القرص في السجلات.
    /// </summary>
    private async Task LogHealthMetricsAsync(CancellationToken ct)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var storageService = scope.ServiceProvider.GetRequiredService<IStorageManagementService>();
            var usage = await storageService.GetStorageUsageAsync(ct);
            _logger.LogInformation(
                "Health Summary -> DB: {DbMB:F2} MB | Recordings: {RecMB:F2} MB | Reports: {RepMB:F2} MB | Logs: {LogMB:F2} MB | Free Disk: {FreeGB:F2} GB / {TotalGB:F2} GB",
                usage.DatabaseSizeMB, usage.RecordingsSizeMB, usage.ReportsSizeMB, usage.LogsSizeMB, usage.FreeDiskSpaceGB, usage.TotalDiskSpaceGB);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not collect health metrics");
        }
    }

    /// <summary>
    /// توليد كافة صيغ التقارير المفعلة ليوم محدد.
    /// </summary>
    /// <param name="targetDate">التاريخ المستهدف.</param>
    /// <param name="ct">رمز الإلغاء.</param>
    /// <param name="onlyMissing">توليد التقارير المفقودة فقط إذا كان true.</param>
    private async Task GenerateDailyReportForDateAsync(DateTime targetDate, CancellationToken ct, bool onlyMissing = false)
    {
        using var scope = _scopeFactory.CreateScope();
        var reportExporter = scope.ServiceProvider.GetRequiredService<IActivityReportExporter>();
        var machine = Environment.MachineName;

        // توليد تقرير Excel
        if (ReportsOptions.ExcelEnabled &&
            true)
        {
            var excelPath = await reportExporter.ExportDailyExcelReportAsync(targetDate, null, ct);
            _logger.LogInformation("Excel daily report generated: {Path}", excelPath);
        }

        // توليد تقرير CSV
        if (ReportsOptions.CsvEnabled &&
            true)
        {
            var csvPath = await reportExporter.ExportDailyCsvReportAsync(targetDate, null, ct);
            _logger.LogInformation("CSV daily report generated: {Path}", csvPath);
        }

        // توليد تقرير النصي TXT
        if (ReportsOptions.TxtEnabled &&
            true)
        {
            var txtPath = await reportExporter.ExportDailySummaryTextAsync(targetDate, null, ct);
            _logger.LogInformation("TXT daily summary generated: {Path}", txtPath);
        }
    }

    /// <summary>
    /// التحقق من الأيام السابقة وتوليد أي تقارير مفقودة كانت مجدولة أثناء إغلاق الجهاز.
    /// </summary>
    private async Task CheckAndBackfillMissedReportsAsync(CancellationToken ct)
    {
        try
        {
            var completedDay = DateTime.Today.AddDays(-1);
            for (var offset = Math.Max(1, ReportsOptions.BackfillDays) - 1; offset >= 0; offset--)
            {
                ct.ThrowIfCancellationRequested();
                var targetDate = completedDay.AddDays(-offset);
                if (await AreEnabledReportsCompleteAsync(targetDate, ct))
                {
                    continue;
                }

                _logger.LogInformation("One or more reports for {Date:yyyy-MM-dd} are missing. Generating backfill...", targetDate);
                await GenerateDailyReportForDateAsync(targetDate, ct, onlyMissing: true);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to backfill yesterday's daily report");
        }
    }

    /// <summary>
    /// التحقق من حلول موعد الجدولة اليومية وإنشاء التقرير لليوم المكتمل السابق.
    /// </summary>
    private async Task CheckAndGenerateDailyReportAsync(DateTime nowUtc, CancellationToken ct)
    {
        var localNow = TimeZoneInfo.ConvertTimeFromUtc(nowUtc, TimeZoneInfo.Local);
        var scheduledTime = ReportsOptions.DailyReportScheduleTime;
        var completedDay = localNow.Date.AddDays(-1);

        // إنشاء التقرير فقط بعد انقضاء اليوم وحلول وقت الجدولة المحدد
        if (localNow.TimeOfDay >= scheduledTime && !await AreEnabledReportsCompleteAsync(completedDay, ct))
        {
            _logger.LogInformation("Scheduled time reached. Generating missing reports for completed day {Date:yyyy-MM-dd}...", completedDay);

            try
            {
                await GenerateDailyReportForDateAsync(completedDay, ct, onlyMissing: true);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to generate daily report");
            }
        }
    }

    /// <summary>
    /// التحقق مما إذا كانت جميع ملفات التقارير المفعلة موجودة ومكتملة ليوم معين.
    /// </summary>
    private async Task<bool> AreEnabledReportsCompleteAsync(DateTime targetDate, CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var activity = scope.ServiceProvider.GetRequiredService<IActivityRepository>();
        var recording = scope.ServiceProvider.GetRequiredService<IRecordingRepository>();
        var (start, end) = ActivityTimelineMath.GetUtcDayRange(targetDate);
        var machine = Environment.MachineName;
        var fingerprint = ReportFreshness.Fingerprint(await activity.GetByDateRangeAsync(start, end, machine, ct),
            await recording.GetByDateRangeAsync(start, end, machine, ct));
        return (!ReportsOptions.ExcelEnabled || ReportFreshness.IsCurrent(ActivityMonitorPaths.GetReportFilePath(machine, targetDate, "xlsx"), fingerprint)) &&
               (!ReportsOptions.CsvEnabled || ReportFreshness.IsCurrent(ActivityMonitorPaths.GetReportFilePath(machine, targetDate, "csv"), fingerprint)) &&
               (!ReportsOptions.TxtEnabled || ReportFreshness.IsCurrent(ActivityMonitorPaths.GetReportFilePath(machine, targetDate, "txt"), fingerprint));
    }

    /// <summary>
    /// يتم تنفيذها عند إيقاف خدمة ويندوز لإغلاق العمليات بنظافة.
    /// </summary>
    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("ActivityMonitor Service is stopping cleanly...");
        await base.StopAsync(cancellationToken);
    }
}
