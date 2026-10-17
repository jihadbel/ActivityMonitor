namespace ActivityMonitor.Core.Options;

/// <summary>
/// خيارات وإعدادات إدارة التخزين وسياسات استبقاء وحذف البيانات القديمة.
/// </summary>
public class StorageOptions
{
    /// <summary>
    /// اسم قسم الإعدادات في ملف التكوين appsettings.json.
    /// </summary>
    public const string SectionName = "Storage";

    /// <summary>
    /// عدد أيام الاحتفاظ بملفات تسجيلات الفيديو قبل حذفها (الافتراضي: 14 يوماً).
    /// </summary>
    public int RecordingRetentionDays { get; set; } = 14;

    /// <summary>
    /// عدد أيام الاحتفاظ بسجلات الأنشطة في قاعدة البيانات قبل تنظيفها (الافتراضي: 90 يوماً).
    /// </summary>
    public int ActivityRetentionDays { get; set; } = 90;

    /// <summary>
    /// عدد أيام الاحتفاظ بملفات التقارير المصدرة (الافتراضي: 90 يوماً).
    /// </summary>
    public int ReportRetentionDays { get; set; } = 90;

    /// <summary>
    /// عدد أيام الاحتفاظ بملفات السجلات التشخيصية Logs (الافتراضي: 30 يوماً).
    /// </summary>
    public int LogRetentionDays { get; set; } = 30;

    /// <summary>
    /// الحد الأقصى الإجمالي المسموح باستهلاكه لكافة ملفات التطبيق بالجيجابايت (الافتراضي: 50 GB).
    /// </summary>
    public long MaxStorageSizeGB { get; set; } = 50;

    /// <summary>
    /// الحد الأدنى الإلزامي للمساحة الحرة على القرص الصلب بالجيجابايت لتجنب امتلاء القرص (الافتراضي: 2 GB).
    /// </summary>
    public long MinimumFreeDiskSpaceGB { get; set; } = 2;
}
