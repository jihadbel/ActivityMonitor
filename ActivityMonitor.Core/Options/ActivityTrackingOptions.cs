namespace ActivityMonitor.Core.Options;

/// <summary>
/// خيارات وإعدادات تتبع نشاط النوافذ واكتشاف الخمول وجلسة المستخدم.
/// </summary>
public class ActivityTrackingOptions
{
    /// <summary>
    /// اسم قسم الإعدادات في ملف التكوين appsettings.json.
    /// </summary>
    public const string SectionName = "ActivityTracking";

    /// <summary>
    /// تفعيل أو تعطيل تتبع الأنشطة في النظام بالكامل.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// الفاصل الزمني بالمللي ثانية بين كل عملية فحص واستطلاع للنوافذ النشطة (الافتراضي: 2000 مللي ثانية).
    /// </summary>
    public int PollingIntervalMs { get; set; } = 2000;

    /// <summary>
    /// مدة غياب إدخال المستخدم بالدقائق التي بعدها يتم اعتبار المستخدم في حالة خمول (الافتراضي: 5 دقائق).
    /// </summary>
    public int IdleThresholdMinutes { get; set; } = 5;

    /// <summary>
    /// مدة الاستقرار بالمللي ثانية (Debounce) للتحقق من ثبات النافذة الجديدة قبل اعتمادها (الافتراضي: 300 مللي ثانية).
    /// </summary>
    public int WindowDebounceMs { get; set; } = 300;

    /// <summary>
    /// تشغيل تطبيق العميل (Agent) تلقائياً عند تسجيل دخول المستخدم لنظام ويندوز.
    /// </summary>
    public bool StartAgentAtLogon { get; set; } = true;
}
