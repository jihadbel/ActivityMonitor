using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ActivityMonitor.Core.Entities;

namespace ActivityMonitor.Core.Interfaces;

/// <summary>
/// واجهة مستودع حفظ واسترجاع وإدارة مقاطع تسجيل الفيديو في قاعدة البيانات.
/// </summary>
public interface IRecordingRepository
{
    /// <summary>
    /// إضافة سجل مقطع فيديو جديد إلى قاعدة البيانات.
    /// </summary>
    /// <param name="segment">كائن مقطع التسجيل.</param>
    /// <param name="ct">رمز الإلغاء.</param>
    /// <returns>كائن مقطع التسجيل بعد الإضافة وتوليد المعرّف.</returns>
    Task<RecordingSegment> AddAsync(RecordingSegment segment, CancellationToken ct = default);

    /// <summary>
    /// تحديث بيانات مقطع فيديو موجود مسبقاً (مثل وقت الانتهاء والحجم والحالة).
    /// </summary>
    /// <param name="segment">كائن مقطع التسجيل بعد التعديل.</param>
    /// <param name="ct">رمز الإلغاء.</param>
    /// <returns>مهمة تمثل العملية غير المتزامنة.</returns>
    Task UpdateAsync(RecordingSegment segment, CancellationToken ct = default);

    /// <summary>
    /// استرجاع مقطع تسجيل معين بواسطة معرّفه الفريد.
    /// </summary>
    /// <param name="id">معرّف المقطع.</param>
    /// <param name="ct">رمز الإلغاء.</param>
    /// <returns>كائن مقطع التسجيل إن وجد؛ أو null.</returns>
    Task<RecordingSegment?> GetByIdAsync(long id, CancellationToken ct = default);

    /// <summary>
    /// استرجاع مقطع الفيديو النشط حالياً (الذي لا يزال قيد التسجيل ولم ينتهِ) لجهاز معين.
    /// </summary>
    /// <param name="machineId">معرّف الجهاز.</param>
    /// <param name="ct">رمز الإلغاء.</param>
    /// <returns>المقطع النشط إن وجد؛ أو null.</returns>
    Task<RecordingSegment?> GetActiveSegmentAsync(string machineId, CancellationToken ct = default, int? sessionId = null);

    /// <summary>
    /// استرجاع قائمة مقاطع الفيديو ضمن نطاق زمني محدد.
    /// </summary>
    /// <param name="startUtc">بداية النطاق الزمني (UTC).</param>
    /// <param name="endUtc">نهاية النطاق الزمني (UTC).</param>
    /// <param name="machineId">معرّف الجهاز (اختياري).</param>
    /// <param name="ct">رمز الإلغاء.</param>
    /// <returns>قائمة مقاطع التسجيل المكتشفة.</returns>
    Task<List<RecordingSegment>> GetByDateRangeAsync(DateTime startUtc, DateTime endUtc, string? machineId = null, CancellationToken ct = default);

    /// <summary>
    /// استرجاع مقاطع الفيديو التي تتقاطع زمنياً وتغطي فترة محددة.
    /// </summary>
    /// <param name="startUtc">بداية الفترة الزمنية (UTC).</param>
    /// <param name="endUtc">نهاية الفترة الزمنية (UTC).</param>
    /// <param name="machineId">معرّف الجهاز (اختياري).</param>
    /// <param name="ct">رمز الإلغاء.</param>
    /// <returns>قائمة المقاطع المتقاطعة زمنياً.</returns>
    Task<List<RecordingSegment>> GetSegmentsCoveringTimeRangeAsync(DateTime startUtc, DateTime endUtc, string? machineId = null, CancellationToken ct = default, int? sessionId = null);

    /// <summary>
    /// استرجاع المقاطع الأقدم من تاريخ حد معين لتطبيق سياسات الحذف والاحتفاظ.
    /// </summary>
    /// <param name="cutoffUtc">تاريخ الحد الأقصى (UTC).</param>
    /// <param name="ct">رمز الإلغاء.</param>
    /// <returns>قائمة المقاطع القديمة.</returns>
    Task<List<RecordingSegment>> GetSegmentsOlderThanAsync(DateTime cutoffUtc, CancellationToken ct = default);

    /// <summary>
    /// استرجاع كافة المقاطع المكتملة المسجلة في النظام.
    /// </summary>
    /// <param name="ct">رمز الإلغاء.</param>
    /// <returns>قائمة المقاطع المكتملة.</returns>
    Task<List<RecordingSegment>> GetAllCompletedSegmentsAsync(CancellationToken ct = default);

    /// <summary>
    /// تعليم مقطع التسجيل كـ "محذوف" في قاعدة البيانات.
    /// </summary>
    /// <param name="id">معرّف المقطع.</param>
    /// <param name="ct">رمز الإلغاء.</param>
    /// <returns>عدد السجلات المتأثرة (1 في حالة النجاح).</returns>
    Task<int> MarkAsDeletedAsync(long id, CancellationToken ct = default);
}
