namespace ActivityMonitor.Core.Models;

/// <summary>
/// يمثل نتائج وملخص عملية تدقيق سلامة البيانات والتسجيلات وإصلاح التلفيات.
/// </summary>
public sealed class DataIntegritySummary
{
    /// <summary>
    /// عدد مقاطع التسجيل المعلقة دون إغلاق التي تم استردادها وإصلاح نهاياتها وحالتها بنجاح.
    /// </summary>
    public int StaleRecordingsRecovered { get; init; }

    /// <summary>
    /// عدد سجلات التسجيلات المفقودة من القرص التي تم تعليمها كـ "محذوفة" في قاعدة البيانات.
    /// </summary>
    public int MissingRecordingsMarkedDeleted { get; init; }

    /// <summary>
    /// عدد المسارات المشبوهة أو غير الآمنة خارج مسار التطبيق التي تم تخطيها لحماية النظام.
    /// </summary>
    public int UnsafePathsSkipped { get; init; }

    /// <summary>
    /// إجمالي عدد التعديلات والإصلاحات التي تم تنفيذها على قاعدة البيانات.
    /// </summary>
    public int TotalChanges => StaleRecordingsRecovered + MissingRecordingsMarkedDeleted;
}
