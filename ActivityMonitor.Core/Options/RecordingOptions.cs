namespace ActivityMonitor.Core.Options;

public class RecordingOptions
{
    public const string SectionName = "Recording";

    public bool Enabled { get; set; } = true;
    public int SegmentMinutes { get; set; } = 10;
    public int FrameRate { get; set; } = 15;
    public int VideoBitrateKbps { get; set; } = 1200;
    public int VideoQuality { get; set; } = 60;
    public int RetentionDays { get; set; } = 14;
    public long MaxStorageSizeGB { get; set; } = 50;
    public long MinimumFreeDiskSpaceGB { get; set; } = 2;
}
