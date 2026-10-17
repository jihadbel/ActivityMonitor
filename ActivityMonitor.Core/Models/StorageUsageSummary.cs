namespace ActivityMonitor.Core.Models;

/// <summary>
/// يمثل ملخصاً شاملاً لاستهلاك السعة التخزينية لجميع مكونات التطبيق والمساحة الحرة على القرص.
/// </summary>
public class StorageUsageSummary
{
    /// <summary>
    /// حجم ملف قاعدة البيانات SQLite بالبايت.
    /// </summary>
    public long DatabaseSizeBytes { get; set; }

    /// <summary>
    /// إجمالي حجم مجلد تسجيلات الفيديو بالبايت.
    /// </summary>
    public long RecordingsSizeBytes { get; set; }

    /// <summary>
    /// إجمالي حجم ملفات التقارير المصدرة بالبايت.
    /// </summary>
    public long ReportsSizeBytes { get; set; }

    /// <summary>
    /// إجمالي حجم ملفات السجلات التشخيصية (Logs) بالبايت.
    /// </summary>
    public long LogsSizeBytes { get; set; }

    /// <summary>
    /// الحجم الكلي المستهلك بواسطة كافة بيانات التطبيق بالبايت.
    /// </summary>
    public long TotalSizeBytes => DatabaseSizeBytes + RecordingsSizeBytes + ReportsSizeBytes + LogsSizeBytes;

    /// <summary>
    /// المساحة الخالية المتبقية على محرك الأقراص بالبايت.
    /// </summary>
    public long FreeDiskSpaceBytes { get; set; }

    /// <summary>
    /// السعة الإجمالية الكاملة لمحرك الأقراص بالبايت.
    /// </summary>
    public long TotalDiskSpaceBytes { get; set; }

    /// <summary>
    /// مؤشر يحدد ما إذا كانت قراءة مساحة القرص قد تمت بنجاح.
    /// </summary>
    public bool IsDiskSpaceKnown { get; set; }

    /// <summary>
    /// مؤشر يحدد ما إذا كان حساب أحجام مجلدات التطبيق قد تم بنجاح.
    /// </summary>
    public bool IsStorageSizeKnown { get; set; }

    /// <summary>
    /// حجم قاعدة البيانات بالميجابايت (MB).
    /// </summary>
    public double DatabaseSizeMB => DatabaseSizeBytes / (1024.0 * 1024.0);

    /// <summary>
    /// حجم مجلد التسجيلات بالميجابايت (MB).
    /// </summary>
    public double RecordingsSizeMB => RecordingsSizeBytes / (1024.0 * 1024.0);

    /// <summary>
    /// حجم مجلد التقارير بالميجابايت (MB).
    /// </summary>
    public double ReportsSizeMB => ReportsSizeBytes / (1024.0 * 1024.0);

    /// <summary>
    /// حجم ملفات السجلات بالميجابايت (MB).
    /// </summary>
    public double LogsSizeMB => LogsSizeBytes / (1024.0 * 1024.0);

    /// <summary>
    /// إجمالي المساحة المستهلكة بالميجابايت (MB).
    /// </summary>
    public double TotalSizeMB => TotalSizeBytes / (1024.0 * 1024.0);

    /// <summary>
    /// المساحة الخالية على القرص بالجيجابايت (GB).
    /// </summary>
    public double FreeDiskSpaceGB => FreeDiskSpaceBytes / (1024.0 * 1024.0 * 1024.0);

    /// <summary>
    /// السعة الإجمالية للقرص بالجيجابايت (GB).
    /// </summary>
    public double TotalDiskSpaceGB => TotalDiskSpaceBytes / (1024.0 * 1024.0 * 1024.0);
}
