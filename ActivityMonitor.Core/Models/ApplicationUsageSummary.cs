using System;

namespace ActivityMonitor.Core.Models;

/// <summary>
/// يمثل ملخص استخدام تطبيق معين متضمناً المدة الإجمالية وعدد مرات التبديل والنسبة المئوية من وقت العمل.
/// </summary>
public class ApplicationUsageSummary
{
    /// <summary>
    /// اسم التطبيق أو العملية (مثل chrome, code, excel).
    /// </summary>
    public string ApplicationName { get; set; } = string.Empty;

    /// <summary>
    /// إجمالي المدة الزمنية التي قضاها المستخدم على هذا التطبيق.
    /// </summary>
    public TimeSpan TotalDuration { get; set; }

    /// <summary>
    /// عدد المرات التي تم فيها تنشيط أو التبديل إلى هذا التطبيق.
    /// </summary>
    public int UsageCount { get; set; }

    /// <summary>
    /// النسبة المئوية لاستخدام التطبيق من إجمالي وقت النشاط (0 - 100%).
    /// </summary>
    public double Percentage { get; set; }
}
