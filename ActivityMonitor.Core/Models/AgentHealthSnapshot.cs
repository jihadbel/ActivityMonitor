namespace ActivityMonitor.Core.Models;

public sealed class AgentHealthSnapshot
{
    public int ProcessId { get; set; }
    public int SessionId { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public bool ActivityTrackingEnabled { get; set; }
    public bool RecordingRequested { get; set; }
    public bool IsRecording { get; set; }
    public long? SegmentId { get; set; }
    public string? Error { get; set; }
}
