using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ActivityMonitor.Core.Entities;

namespace ActivityMonitor.Infrastructure.Services;

public static class ReportFreshness
{
    public static string Fingerprint(IEnumerable<ActivityEvent> activities, IEnumerable<RecordingSegment> recordings)
    {
        // Serialize scalar projections only; navigation properties can form cycles.
        var value = new {
            Version = 2,
            Events = activities.OrderBy(a => a.Id).Select(a => new { a.Id, a.MachineId, a.SessionId, a.UserIdentifier,
                a.ProcessName, a.ProcessId, a.ProcessPath, a.WindowTitle, a.ContextPath, a.EventType,
                a.StartedAtUtc, a.EndedAtUtc, a.DurationSeconds, a.RecordingSegmentId }),
            Recordings = recordings.OrderBy(r => r.Id).Select(r => new { r.Id, r.MachineId, r.SessionId, r.UserIdentifier,
                r.StartedAtUtc, r.EndedAtUtc, r.DurationSeconds, r.FileName, r.FilePath, r.FileSizeBytes, r.Status, r.HeartbeatAtUtc })
        };
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value))));
    }

    public static void MarkComplete(string path, string fingerprint)
    {
        var file = new FileInfo(path);
        var value = string.Join('|', fingerprint, file.Length.ToString(CultureInfo.InvariantCulture),
            file.LastWriteTimeUtc.Ticks.ToString(CultureInfo.InvariantCulture));
        var stamp = path + ".state";
        var temporary = stamp + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { File.WriteAllText(temporary, value); File.Move(temporary, stamp, true); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    public static bool IsCurrent(string path, string fingerprint)
    {
        try
        {
            var file = new FileInfo(path);
            if (!file.Exists || file.Length == 0) return false;
            var parts = File.ReadAllText(path + ".state").Split('|');
            return parts.Length == 3 && parts[0] == fingerprint &&
                parts[1] == file.Length.ToString(CultureInfo.InvariantCulture) &&
                parts[2] == file.LastWriteTimeUtc.Ticks.ToString(CultureInfo.InvariantCulture);
        }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }
}
