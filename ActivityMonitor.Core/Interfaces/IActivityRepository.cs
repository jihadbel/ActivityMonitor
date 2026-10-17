using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ActivityMonitor.Core.Entities;
using ActivityMonitor.Core.Models;

namespace ActivityMonitor.Core.Interfaces;

/// <summary>
/// واجهة مستودع حفظ واسترجاع واستعلام أحداث الأنشطة في قاعدة البيانات.
/// </summary>
public interface IActivityRepository
{
    /// <summary>
    /// إضافة حدث نشاط جديد إلى قاعدة البيانات وحفظه.
    /// </summary>
    /// <param name="activityEvent">كائن حدث النشاط.</param>
    /// <param name="ct">رمز الإلغاء.</param>
    /// <returns>كائن حدث النشاط بعد إضافته وتوليد معرّفه.</returns>
    Task<ActivityEvent> AddAsync(ActivityEvent activityEvent, CancellationToken ct = default);

    /// <summary>
    /// إضافة مجموعة من أحداث الأنشطة دفعة واحدة.
    /// </summary>
    /// <param name="activityEvents">قائمة أحداث النشاط.</param>
    /// <param name="ct">رمز الإلغاء.</param>
    /// <returns>مهمة تمثل العملية غير المتزامنة.</returns>
    Task AddRangeAsync(IEnumerable<ActivityEvent> activityEvents, CancellationToken ct = default);

    /// <summary>
    /// تحديث بيانات حدث نشاط موجود مسبقاً (مثل وقت الانتهاء والمدة).
    /// </summary>
    /// <param name="activityEvent">كائن حدث النشاط بعد التعديل.</param>
    /// <param name="ct">رمز الإلغاء.</param>
    /// <returns>مهمة تمثل العملية غير المتزامنة.</returns>
    Task UpdateAsync(ActivityEvent activityEvent, CancellationToken ct = default);

    /// <summary>
    /// ربط حدث النشاط بمقطع فيديو إذا لم يكن مقترناً بالفعل.
    /// </summary>
    /// <param name="activityEventId">معرّف حدث النشاط.</param>
    /// <param name="recordingSegmentId">معرّف مقطع التسجيل.</param>
    /// <param name="ct">رمز الإلغاء.</param>
    /// <returns>القيمة true إذا تم التحديث والربط بنجاح؛ وإلا false.</returns>
    Task<bool> SetRecordingSegmentIfMissingAsync(long activityEventId, long recordingSegmentId, CancellationToken ct = default);

    /// <summary>
    /// استرجاع حدث نشاط معين بواسطة معرّفه الفريد.
    /// </summary>
    /// <param name="id">معرّف الحدث.</param>
    /// <param name="ct">رمز الإلغاء.</param>
    /// <returns>كائن النشاط إن وجد؛ أو null.</returns>
    Task<ActivityEvent?> GetByIdAsync(long id, CancellationToken ct = default);

    /// <summary>
    /// استرجاع قائمة أحداث الأنشطة ضمن نطاق زمني محدد لجهاز معين.
    /// </summary>
    /// <param name="startUtc">بداية النطاق الزمني (UTC).</param>
    /// <param name="endUtc">نهاية النطاق الزمني (UTC).</param>
    /// <param name="machineId">معرّف الجهاز (اختياري).</param>
    /// <param name="ct">رمز الإلغاء.</param>
    /// <returns>قائمة بأحداث الأنشطة المكتشفة.</returns>
    Task<List<ActivityEvent>> GetByDateRangeAsync(DateTime startUtc, DateTime endUtc, string? machineId = null, CancellationToken ct = default);

    /// <summary>
    /// استرجاع الأنشطة التي لم ترتبط بعد بأي مقطع تسجيل فيديو قبل وقت معين.
    /// </summary>
    /// <param name="beforeUtc">الحد الزمني الأعلى للتفتيش (UTC).</param>
    /// <param name="ct">رمز الإلغاء.</param>
    /// <returns>قائمة بالأنشطة غير المربوطة بمقاطع.</returns>
    Task<List<ActivityEvent>> GetUnlinkedActivitiesAsync(DateTime beforeUtc, CancellationToken ct = default, long afterId = 0);

    /// <summary>
    /// حساب وإنشاء الملخص الإحصائي اليومي للأنشطة والخمول لجهاز معين.
    /// </summary>
    /// <param name="dateUtc">تاريخ اليوم (UTC).</param>
    /// <param name="machineId">معرّف الجهاز (اختياري).</param>
    /// <param name="ct">رمز الإلغاء.</param>
    /// <returns>كائن ملخص الأنشطة اليومية.</returns>
    Task<DailyActivitySummary> GetDailySummaryAsync(DateTime dateUtc, string? machineId = null, CancellationToken ct = default);

    /// <summary>
    /// استرجاع قائمة أكثر التطبيقات استخداماً ومرتبة تنازلياً حسب المدة والنسبة المئوية.
    /// </summary>
    /// <param name="dateUtc">تاريخ اليوم المطلوب (UTC).</param>
    /// <param name="machineId">معرّف الجهاز (اختياري).</param>
    /// <param name="ct">رمز الإلغاء.</param>
    /// <returns>قائمة ملخصات استخدام التطبيقات.</returns>
    Task<List<ApplicationUsageSummary>> GetTopApplicationsAsync(DateTime dateUtc, string? machineId = null, CancellationToken ct = default);

    /// <summary>
    /// تنظيف وحذف سجلات الأنشطة القديمة التي تجاوزت تاريخ حد معين لتحرير المساحة.
    /// </summary>
    /// <param name="cutoffUtc">تاريخ الحد الأقصى للاحتفاظ (UTC).</param>
    /// <param name="ct">رمز الإلغاء.</param>
    /// <returns>عدد السجلات المحذوفة.</returns>
    Task<int> PurgeOlderThanAsync(DateTime cutoffUtc, CancellationToken ct = default);
}
