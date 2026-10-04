namespace ActivityMonitor.Core.Options;

public class ActivityTrackingOptions
{
    public const string SectionName = "ActivityTracking";

    public bool Enabled { get; set; } = true;
    public int PollingIntervalMs { get; set; } = 500;
    public int IdleThresholdMinutes { get; set; } = 5;
    public int WindowDebounceMs { get; set; } = 300;
}
