namespace ActivityMonitor.Core.Options;

public class StorageOptions
{
    public const string SectionName = "Storage";

    public int RecordingRetentionDays { get; set; } = 14;
    public int ActivityRetentionDays { get; set; } = 90;
    public int ReportRetentionDays { get; set; } = 90;
    public int LogRetentionDays { get; set; } = 30;
    public long MaxStorageSizeGB { get; set; } = 50;
    public long MinimumFreeDiskSpaceGB { get; set; } = 2;
}
