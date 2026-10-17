using System;

namespace ActivityMonitor.Core.Options;

/// <summary>
/// خيارات وإعدادات تصدير التقارير وجدولتها الزمنية والصيغ المدعومة.
/// </summary>
public class ReportsOptions
{
    /// <summary>
    /// اسم قسم الإعدادات في ملف التكوين appsettings.json.
    /// </summary>
    public const string SectionName = "Reports";

    /// <summary>
    /// تمكين تصدير تقارير بصيغة Excel (.xlsx).
    /// </summary>
    public bool ExcelEnabled { get; set; } = true;

    /// <summary>
    /// تمكين تصدير تقارير بصيغة CSV (.csv).
    /// </summary>
    public bool CsvEnabled { get; set; } = true;

    /// <summary>
    /// تمكين تصدير تقارير بصيغة نصية عادية (.txt).
    /// </summary>
    public bool TxtEnabled { get; set; } = true;

    /// <summary>
    /// تفعيل الجدولة التلقائية لإنشاء التقارير اليومية بنهاية كل يوم.
    /// </summary>
    public bool DailyReportEnabled { get; set; } = true;

    /// <summary>
    /// الوقت اليومي المحدد لتنفيذ مهمة تصدير التقرير اليومي عن اليوم السابق (الافتراضي: الساعة 00:05 ليلاً).
    /// </summary>
    public TimeSpan DailyReportScheduleTime { get; set; } = new TimeSpan(0, 5, 0);

    /// <summary>
    /// عدد الأيام السابقة المطلوب التحقق منها وإنشاء أي تقارير مفقودة لها (Backfill - الافتراضي: 7 أيام).
    /// </summary>
    public int BackfillDays { get; set; } = 7;
}
