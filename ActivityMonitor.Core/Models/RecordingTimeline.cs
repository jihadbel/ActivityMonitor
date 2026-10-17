using ActivityMonitor.Core.Entities;
using ActivityMonitor.Core.Enums;

namespace ActivityMonitor.Core.Models;

/// <summary>One overlap definition used by lookup, exports and the viewer.</summary>
public static class RecordingTimeline
{
    public static DateTime EndUtc(RecordingSegment segment, DateTime nowUtc) =>
        segment.EndedAtUtc ?? (segment.Status == RecordingStatus.InProgress
            ? segment.HeartbeatAtUtc ?? nowUtc
            : segment.StartedAtUtc.AddSeconds(Math.Max(0, segment.DurationSeconds)));

    public static List<RecordingLocationResult> Resolve(
        ActivityEvent activity, IEnumerable<RecordingSegment> recordings, DateTime nowUtc,
        DateTime? rangeStartUtc = null, DateTime? rangeEndUtc = null)
    {
        var start = activity.StartedAtUtc;
        var end = activity.EndedAtUtc ?? start.AddSeconds(Math.Max(1, activity.DurationSeconds));
        if (end <= start) end = start.AddTicks(1); // A session marker is a point, not an empty interval.
        if (rangeStartUtc.HasValue && start < rangeStartUtc.Value) start = rangeStartUtc.Value;
        if (rangeEndUtc.HasValue && end > rangeEndUtc.Value) end = rangeEndUtc.Value;
        if (end <= start) return [];

        return recordings.Where(r => r.MachineId == activity.MachineId && r.SessionId == activity.SessionId &&
                (string.IsNullOrEmpty(r.UserIdentifier) || string.IsNullOrEmpty(activity.UserIdentifier) ||
                 string.Equals(r.UserIdentifier, activity.UserIdentifier, StringComparison.OrdinalIgnoreCase)) &&
                r.Status is RecordingStatus.Completed or RecordingStatus.Recovered or RecordingStatus.InProgress)
            .OrderBy(r => r.StartedAtUtc).ThenBy(r => r.Id)
            .Select(r => {
                var overlapStart = start > r.StartedAtUtc ? start : r.StartedAtUtc;
                var recordingEnd = EndUtc(r, nowUtc);
                var overlapEnd = end < recordingEnd ? end : recordingEnd;
                return new RecordingLocationResult {
                    ActivityEventId = activity.Id, RecordingSegmentId = r.Id,
                    FileName = r.FileName, FilePath = r.FilePath,
                    Offset = overlapStart - r.StartedAtUtc, Duration = overlapEnd - overlapStart
                };
            }).Where(r => r.Duration > TimeSpan.Zero).ToList();
    }
}
