using System;

namespace ActivityMonitor.Core.Options;

public class ReportsOptions
{
    public const string SectionName = "Reports";

    public bool ExcelEnabled { get; set; } = true;
    public bool CsvEnabled { get; set; } = true;
    public bool TxtEnabled { get; set; } = true;
    public bool DailyReportEnabled { get; set; } = true;
    public TimeSpan DailyReportScheduleTime { get; set; } = new TimeSpan(23, 55, 0); // 23:55
}
