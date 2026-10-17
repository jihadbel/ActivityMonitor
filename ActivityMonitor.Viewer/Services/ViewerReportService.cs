using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ActivityMonitor.Infrastructure.Data;
using ActivityMonitor.Infrastructure.Repositories;
using ActivityMonitor.Reporting.Exporters;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace ActivityMonitor.Viewer.Services;

/// <summary>
/// خدمة تصدير التقارير المخصصة لتطبيق العارض (Viewer).
/// توفر واجهة مبسطة مستقلة لتهيئة مستودعات البيانات ومحرك التصدير دون الحاجة لحاوية حقن التبعيات الكاملة.
/// </summary>
public sealed class ViewerReportService
{
    // أداة تصدير التقارير الفعلية (Excel, CSV, Text)
    private readonly ActivityReportExporter _exporter;

    /// <summary>
    /// تهيئة خدمة التقارير للعارض وبناء المستودعات ومصنع سياق قاعدة البيانات محلياً.
    /// </summary>
    public ViewerReportService()
    {
        // إنشاء مصنع سياق قاعدة البيانات المحلي للعارض
        var contextFactory = new ViewerDbContextFactory();

        // تهيئة مستودع الأنشطة باستخدام مسجل سجلات فارغ (NullLogger)
        var activityRepository = new ActivityRepository(
            contextFactory,
            NullLogger<ActivityRepository>.Instance);

        // تهيئة مستودع التسجيلات المرئية
        var recordingRepository = new RecordingRepository(
            contextFactory,
            NullLogger<RecordingRepository>.Instance);

        // بناء خدمة تصدير التقارير بتمرير المستودعات المهيأة
        _exporter = new ActivityReportExporter(
            activityRepository,
            recordingRepository,
            NullLogger<ActivityReportExporter>.Instance);
    }

    /// <summary>
    /// تصدير جميع تقارير اليوم المحدد بصيغ متعددة (Excel و CSV وملخص نصي) وحفظها في المجلد الافتراضي.
    /// </summary>
    /// <param name="selectedDate">التاريخ المراد تصدير تقاريره.</param>
    /// <param name="ct">رمز إلغاء العملية غير المتزامنة.</param>
    /// <returns>قائمة بمسارات الملفات التي تم إنشاؤها بنجاح على القرص.</returns>
    public Task<IReadOnlyList<string>> ExportSelectedDayAsync(DateTime selectedDate, CancellationToken ct = default) =>
        Task.Run(() => ExportCoreAsync(selectedDate, ct), ct);

    private async Task<IReadOnlyList<string>> ExportCoreAsync(DateTime selectedDate, CancellationToken ct)
    {
        // ضمان تهيئة قاعدة البيانات وتطبيق الترحيلات والتأكد من فتح الاتصال بنجاح قبل القراءة
        await ActivityMonitorDbContext.InitializeDatabaseAsync(null, ct);

        // إنشاء قائمة بالملفات المخرجة لجمع مسارات التقارير الثلاثة
        var files = new List<string>(3)
        {
            // 1. تصدير تقرير الإكسل المفصل
            await _exporter.ExportDailyExcelReportAsync(selectedDate, null, ct),

            // 2. تصدير تقرير القيم المفصولة بفواصل (CSV)
            await _exporter.ExportDailyCsvReportAsync(selectedDate, null, ct),

            // 3. تصدير الملخص الإحصائي النصي
            await _exporter.ExportDailySummaryTextAsync(selectedDate, null, ct)
        };

        // إعادة قائمة الملفات الناتجة
        return files;
    }

    /// <summary>
    /// مصنع محلي لسياق قاعدة البيانات (IDbContextFactory) يلبي متطلبات المستودعات داخل تطبيق العارض.
    /// </summary>
    private sealed class ViewerDbContextFactory : IDbContextFactory<ActivityMonitorDbContext>
    {
        /// <summary>
        /// إنشاء نسخة جديدة من سياق قاعدة بيانات ActivityMonitorDbContext.
        /// </summary>
        /// <returns>نسخة مهيأة من سياق قاعدة البيانات.</returns>
        public ActivityMonitorDbContext CreateDbContext() => new();

        /// <summary>
        /// إنشاء نسخة جديدة من سياق قاعدة البيانات بشكل غير متزامن.
        /// </summary>
        /// <param name="cancellationToken">رمز إلغاء العملية.</param>
        /// <returns>مهمة تُرجع سياق قاعدة البيانات المنشأ.</returns>
        public Task<ActivityMonitorDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(CreateDbContext());
    }
}
