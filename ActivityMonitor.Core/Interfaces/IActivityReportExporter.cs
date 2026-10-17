using System;
using System.Threading;
using System.Threading.Tasks;

namespace ActivityMonitor.Core.Interfaces;

/// <summary>
/// واجهة تصدير تقارير الأنشطة بصيغ متعددة (Excel و CSV ونصي).
/// </summary>
public interface IActivityReportExporter
{
    /// <summary>
    /// تصدير تقرير الأنشطة اليومية بصيغة جدول بيانات Excel (.xlsx).
    /// </summary>
    /// <param name="dateUtc">تاريخ اليوم المراد تصديره بالتوقيت العالمي.</param>
    /// <param name="machineId">معرّف الجهاز (اختياري).</param>
    /// <param name="ct">رمز إلغاء العملية.</param>
    /// <returns>المسار الكامل لملف التقرير الذي تم إنشاؤه.</returns>
    Task<string> ExportDailyExcelReportAsync(DateTime dateUtc, string? machineId = null, CancellationToken ct = default);

    /// <summary>
    /// تصدير تقرير الأنشطة اليومية بصيغة ملف نصي مفصول بفواصل CSV (.csv).
    /// </summary>
    /// <param name="dateUtc">تاريخ اليوم المراد تصديره بالتوقيت العالمي.</param>
    /// <param name="machineId">معرّف الجهاز (اختياري).</param>
    /// <param name="ct">رمز إلغاء العملية.</param>
    /// <returns>المسار الكامل لملف التقرير الذي تم إنشاؤه.</returns>
    Task<string> ExportDailyCsvReportAsync(DateTime dateUtc, string? machineId = null, CancellationToken ct = default);

    /// <summary>
    /// تصدير ملخص نصي مقروء للأنشطة اليومية (.txt).
    /// </summary>
    /// <param name="dateUtc">تاريخ اليوم المراد تصديره بالتوقيت العالمي.</param>
    /// <param name="machineId">معرّف الجهاز (اختياري).</param>
    /// <param name="ct">رمز إلغاء العملية.</param>
    /// <returns>المسار الكامل لملف الملخص النصي المنشأ.</returns>
    Task<string> ExportDailySummaryTextAsync(DateTime dateUtc, string? machineId = null, CancellationToken ct = default);
}
