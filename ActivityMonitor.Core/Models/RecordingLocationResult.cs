using System;

namespace ActivityMonitor.Core.Models;

public class RecordingLocationResult
{
    public long ActivityEventId { get; set; }
    public long RecordingSegmentId { get; set; }
    public string FilePath { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public TimeSpan Offset { get; set; }
    public TimeSpan Duration { get; set; }
    public bool FileExists { get; set; }
}
