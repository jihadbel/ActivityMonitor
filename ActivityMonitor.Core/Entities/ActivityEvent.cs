using System;
using ActivityMonitor.Core.Enums;

namespace ActivityMonitor.Core.Entities;

public class ActivityEvent
{
    public long Id { get; set; }
    public string MachineId { get; set; } = string.Empty;
    public int SessionId { get; set; }
    public string UserIdentifier { get; set; } = string.Empty;
    public string ProcessName { get; set; } = string.Empty;
    public int ProcessId { get; set; }
    public string? ProcessPath { get; set; }
    public string? WindowTitle { get; set; }
    public string? ContextPath { get; set; }
    public ActivityEventType EventType { get; set; } = ActivityEventType.ActiveWindow;
    public DateTime StartedAtUtc { get; set; }
    public DateTime? EndedAtUtc { get; set; }
    public double DurationSeconds { get; set; }
    public long? RecordingSegmentId { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public RecordingSegment? RecordingSegment { get; set; }
}
