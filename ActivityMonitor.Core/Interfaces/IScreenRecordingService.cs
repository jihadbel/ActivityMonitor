using System.Threading;
using System.Threading.Tasks;
using ActivityMonitor.Core.Entities;

namespace ActivityMonitor.Core.Interfaces;

/// <summary>
/// واجهة خدمة التقاط وتسجيل شاشة المستخدم كملفات فيديو.
/// </summary>
public interface IScreenRecordingService
{
    string? LastError => null;
    /// <summary>
    /// مؤشر يحدد ما إذا كانت الشاشة قيد التسجيل حالياً.
    /// </summary>
    bool IsRecording { get; }

    /// <summary>
    /// كائن مقطع التسجيل الحالي الجاري التقاطه.
    /// </summary>
    RecordingSegment? CurrentSegment { get; }

    /// <summary>
    /// بدء تسجيل مقطع شاشة جديد وحفظ سجله في قاعدة البيانات.
    /// </summary>
    /// <param name="ct">رمز الإلغاء.</param>
    /// <returns>مقطع التسجيل الجديد الذي تم إنشاؤه وبدء تسجيله.</returns>
    Task<RecordingSegment?> StartRecordingSegmentAsync(CancellationToken ct = default);

    /// <summary>
    /// إيقاف المقطع الجاري تسجيله حالياً وإغلاق الملف وتحديث حالته في قاعدة البيانات.
    /// </summary>
    /// <param name="ct">رمز الإلغاء.</param>
    /// <returns>مقطع التسجيل المنتهي بعد تحديثه.</returns>
    Task<RecordingSegment?> StopRecordingSegmentAsync(CancellationToken ct = default);

    /// <summary>
    /// التبديل السلس إلى مقطع فيديو جديد (إغلاق المقطع الحالي وبدء مقطع تالٍ فوراً دون انقطاع).
    /// </summary>
    /// <param name="ct">رمز الإلغاء.</param>
    /// <returns>مقطع التسجيل الجديد الناتج عن عملية التبديل.</returns>
    Task<RecordingSegment?> SwitchToNewSegmentAsync(CancellationToken ct = default);

    /// <summary>
    /// التحقق من أن تسجيل الشاشة يعمل حالياً وبدؤه فوراً إذا كان متوقفاً.
    /// </summary>
    /// <param name="ct">رمز الإلغاء.</param>
    /// <returns>مقطع التسجيل النشط.</returns>
    Task<RecordingSegment?> EnsureRecordingActiveAsync(CancellationToken ct = default);
}
