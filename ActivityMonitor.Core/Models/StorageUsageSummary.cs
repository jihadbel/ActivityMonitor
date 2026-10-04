namespace ActivityMonitor.Core.Models;

public class StorageUsageSummary
{
    public long DatabaseSizeBytes { get; set; }
    public long RecordingsSizeBytes { get; set; }
    public long ReportsSizeBytes { get; set; }
    public long LogsSizeBytes { get; set; }
    public long TotalSizeBytes => DatabaseSizeBytes + RecordingsSizeBytes + ReportsSizeBytes + LogsSizeBytes;
    public long FreeDiskSpaceBytes { get; set; }
    public long TotalDiskSpaceBytes { get; set; }

    public double DatabaseSizeMB => DatabaseSizeBytes / (1024.0 * 1024.0);
    public double RecordingsSizeMB => RecordingsSizeBytes / (1024.0 * 1024.0);
    public double ReportsSizeMB => ReportsSizeBytes / (1024.0 * 1024.0);
    public double LogsSizeMB => LogsSizeBytes / (1024.0 * 1024.0);
    public double TotalSizeMB => TotalSizeBytes / (1024.0 * 1024.0);
    public double FreeDiskSpaceGB => FreeDiskSpaceBytes / (1024.0 * 1024.0 * 1024.0);
    public double TotalDiskSpaceGB => TotalDiskSpaceBytes / (1024.0 * 1024.0 * 1024.0);
}
