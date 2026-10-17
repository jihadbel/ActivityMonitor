namespace ActivityMonitor.Core.Enums;

/// <summary>
/// يحدد أنواع أحداث النشاط التي يتم التقاطها وتتبعها في النظام.
/// </summary>
public enum ActivityEventType
{
    /// <summary>
    /// نافذة تطبيق نشطة تم التركيز عليها في واجهة المستخدم.
    /// </summary>
    ActiveWindow = 1,

    /// <summary>
    /// بداية فترة خمول المستخدم بسبب غياب أي مدخلات من الفأرة أو لوحة المفاتيح.
    /// </summary>
    IdleStart = 2,

    /// <summary>
    /// نهاية فترة الخمول واستئناف المستخدم لنشاطه.
    /// </summary>
    IdleEnd = 3,

    /// <summary>
    /// قفل جلسة نظام ويندوز بواسطة المستخدم أو المؤقت التلقائي.
    /// </summary>
    SessionLock = 4,

    /// <summary>
    /// إلغاء قفل جلسة ويندوز وعودة المستخدم للعمل.
    /// </summary>
    SessionUnlock = 5,

    /// <summary>
    /// تسجيل دخول مستخدم إلى جلسة ويندوز.
    /// </summary>
    SessionLogon = 6,

    /// <summary>
    /// تسجيل خروج المستخدم من نظام ويندوز.
    /// </summary>
    SessionLogoff = 7,

    /// <summary>
    /// دخول نظام التشغيل في وضع السكون أو التعليق (Sleep / Hibernate).
    /// </summary>
    SystemSleep = 8,

    /// <summary>
    /// استيقاظ نظام التشغيل واستئناف العمل من وضع السكون.
    /// </summary>
    SystemResume = 9,

    /// <summary>
    /// إيقاف تشغيل جهاز الكمبيوتر أو إعادة تشغيله.
    /// </summary>
    SystemShutdown = 10
}
