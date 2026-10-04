using System;

namespace ActivityMonitor.Core.Models;

public class DailyActivitySummary
{
    public DateTime Date { get; set; }
    public string MachineId { get; set; } = string.Empty;
    public string UserIdentifier { get; set; } = string.Empty;
    public TimeSpan TotalActiveTime { get; set; }
    public TimeSpan TotalIdleTime { get; set; }
    public DateTime? FirstActivityUtc { get; set; }
    public DateTime? LastActivityUtc { get; set; }
    public int UniqueApplicationsCount { get; set; }
    public int TotalEventsCount { get; set; }
    public int RecordingSegmentsCount { get; set; }
    public long TotalRecordingSizeBytes { get; set; }
}
