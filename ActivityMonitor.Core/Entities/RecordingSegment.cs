using System;
using System.Collections.Generic;
using ActivityMonitor.Core.Enums;

namespace ActivityMonitor.Core.Entities;

public class RecordingSegment
{
    public long Id { get; set; }
    public string MachineId { get; set; } = string.Empty;
    public int SessionId { get; set; }
    public DateTime StartedAtUtc { get; set; }
    public DateTime? EndedAtUtc { get; set; }
    public double DurationSeconds { get; set; }
    public string FilePath { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public long FileSizeBytes { get; set; }
    public RecordingStatus Status { get; set; } = RecordingStatus.InProgress;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public ICollection<ActivityEvent> Activities { get; set; } = new List<ActivityEvent>();
}
