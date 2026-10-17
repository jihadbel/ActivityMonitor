using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ActivityMonitor.Core.Entities;
using ActivityMonitor.Core.Models;

namespace ActivityMonitor.Core.Interfaces;

/// <summary>
/// واجهة خدمة تحديد موقع وتوقيت مقطع الفيديو المطابق لحدث نشاط معين في التسجيلات.
/// </summary>
public interface IRecordingLocatorService
{
    /// <summary>
    /// العثور على مقاطع الفيديو المتزامنة مع حدث نشاط محدد وحساب الإزاحة الزمنية (Offset).
    /// </summary>
    /// <param name="activityEvent">حدث النشاط المطلوب العثور على تسجيله.</param>
    /// <param name="ct">رمز الإلغاء.</param>
    /// <returns>قائمة بنتائج التحديد تتضمن مسار الفيديو والإزاحة الزمنية والمدة ووجود الملف.</returns>
    Task<List<RecordingLocationResult>> LocateAsync(ActivityEvent activityEvent, CancellationToken ct = default);

    /// <summary>
    /// العثور على مقاطع الفيديو المتزامنة مع طابع زمني محدد وفترة معينة.
    /// </summary>
    /// <param name="timestampUtc">الطابع الزمني بالتوقيت العالمي.</param>
    /// <param name="duration">المدة الزمنية المراد تغطيتها (اختياري).</param>
    /// <param name="machineId">معرّف الجهاز (اختياري).</param>
    /// <param name="ct">رمز الإلغاء.</param>
    /// <returns>قائمة بنتائج تحديد المواقع.</returns>
    Task<List<RecordingLocationResult>> LocateByTimestampAsync(DateTime timestampUtc, TimeSpan? duration = null, string? machineId = null, CancellationToken ct = default, int? sessionId = null);

    /// <summary>
    /// إعادة مطابقة وربط الأنشطة غير المرتبطة بمقاطع الفيديو التابعة لها.
    /// </summary>
    /// <param name="beforeUtc">الحد الزمني الأقصى للأنشطة المراد ربطها.</param>
    /// <param name="ct">رمز الإلغاء.</param>
    /// <returns>عدد الأنشطة التي تم ربطها بنجاح بمقاطع الفيديو.</returns>
    Task<int> LinkUnlinkedActivitiesAsync(DateTime beforeUtc, CancellationToken ct = default);
}
