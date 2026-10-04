using System;

namespace ActivityMonitor.Core.Models;

public class ApplicationUsageSummary
{
    public string ApplicationName { get; set; } = string.Empty;
    public TimeSpan TotalDuration { get; set; }
    public int UsageCount { get; set; }
    public double Percentage { get; set; }
}
