using System;
using System.IO;

namespace ActivityMonitor.Viewer.ViewModels;

public class ActivityItemViewModel
{
    public long Id { get; set; }
    public DateTime StartedAtUtc { get; set; }
    public DateTime? EndedAtUtc { get; set; }
    public DateTime StartTimeLocal => StartedAtUtc.ToLocalTime();
    public DateTime EndTimeLocal => (EndedAtUtc ?? StartedAtUtc).ToLocalTime();

    public string TimeRangeDisplay => $"{StartTimeLocal:HH:mm:ss} - {EndTimeLocal:HH:mm:ss}";
    public string ProcessName { get; set; } = string.Empty;
    public string WindowTitle { get; set; } = string.Empty;
    public string? ContextPath { get; set; }
    public string? ProcessPath { get; set; }
    public string EventType { get; set; } = "ActiveWindow";
    public double DurationSeconds { get; set; }

    public string DurationDisplay
    {
        get
        {
            var ts = TimeSpan.FromSeconds(Math.Max(1, DurationSeconds));
            if (ts.TotalHours >= 1)
            {
                return $"{(int)ts.TotalHours}س {ts.Minutes}د {ts.Seconds}ث";
            }
            return $"{ts.Minutes}د {ts.Seconds}ث";
        }
    }

    public long? RecordingSegmentId { get; set; }
    public string? RecordingFileName { get; set; }
    public string? RecordingFilePath { get; set; }
    public TimeSpan VideoOffset { get; set; }

    public string OffsetDisplay => $"{VideoOffset:mm\\:ss}";

    public bool HasVideo => !string.IsNullOrWhiteSpace(RecordingFilePath) && File.Exists(RecordingFilePath);

    public string AppBadgeColor
    {
        get
        {
            if (string.Equals(ProcessName, "Idle", StringComparison.OrdinalIgnoreCase)) return "#94A3B8"; // Slate gray
            if (string.Equals(ProcessName, "chrome", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(ProcessName, "msedge", StringComparison.OrdinalIgnoreCase)) return "#38BDF8"; // Sky blue
            if (string.Equals(ProcessName, "devenv", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(ProcessName, "code", StringComparison.OrdinalIgnoreCase)) return "#A855F7"; // Purple
            if (string.Equals(ProcessName, "explorer", StringComparison.OrdinalIgnoreCase)) return "#F59E0B"; // Amber
            return "#6366F1"; // Indigo
        }
    }
}

public class RecordingSegmentViewModel
{
    public long Id { get; set; }
    public DateTime StartedAtUtc { get; set; }
    public DateTime? EndedAtUtc { get; set; }
    public DateTime StartTimeLocal => StartedAtUtc.ToLocalTime();
    public DateTime EndTimeLocal => (EndedAtUtc ?? StartedAtUtc).ToLocalTime();
    public string FileName { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public double DurationSeconds { get; set; }
    public long FileSizeBytes { get; set; }
    public string Status { get; set; } = "Completed";

    public string DurationDisplay => $"{TimeSpan.FromSeconds(DurationSeconds):mm\\:ss}";
    public string FileSizeDisplay => $"{FileSizeBytes / (1024.0 * 1024.0):F2} MB";
    public bool ExistsOnDisk => File.Exists(FilePath);
}
