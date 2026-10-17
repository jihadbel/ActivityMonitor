namespace ActivityMonitor.Core.Enums;

/// <summary>
/// يحدد الحالة التشغيلية لدورة حياة مقطع تسجيل الفيديو.
/// </summary>
public enum RecordingStatus
{
    /// <summary>
    /// المقطع قيد التسجيل حالياً بواسطة محرك التقاط الشاشة.
    /// </summary>
    InProgress = 1,

    /// <summary>
    /// تم اكتمال تسجيل المقطع بنجاح وإغلاق الملف بصورة سليمة.
    /// </summary>
    Completed = 2,

    /// <summary>
    /// حدث تلف أو انقطاع غير متوقع للمقطع (مثل الإغلاق القسري للجهاز).
    /// </summary>
    Corrupted = 3,

    /// <summary>
    /// تم حذف ملف التسجيل (إما يدوياً أو بواسطة سياسة الاحتفاظ بالبيانات).
    /// </summary>
    Deleted = 4,

    /// <summary>
    /// تم حذف الملف الفعلي لاستعادة المساحة على القرص الصلب مع الاحتفاظ بسجل البيانات.
    /// </summary>
    DiskSpaceReclaimed = 5,
    /// <summary>A stopped owner's file was opened and its video stream verified.</summary>
    Recovered = 6
}
