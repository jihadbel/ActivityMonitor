using System.Diagnostics;
using ActivityMonitor.Core.Entities;

namespace ActivityMonitor.Infrastructure.Services;

public static class RecordingOwnership
{
    public static bool IsAlive(RecordingSegment segment, DateTime nowUtc)
    {
        if (segment.OwnerProcessId > 0 && segment.OwnerProcessStartedAtUtc.HasValue &&
            string.Equals(segment.MachineId, Environment.MachineName, StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                using var process = Process.GetProcessById(segment.OwnerProcessId);
                // A recycled PID must never inherit ownership of an old recording.
                return process.StartTime.ToUniversalTime() == segment.OwnerProcessStartedAtUtc.Value && !process.HasExited &&
                       segment.HeartbeatAtUtc > nowUtc.AddSeconds(-45);
            }
            catch (ArgumentException) { return false; }
            catch (InvalidOperationException) { return false; }
            catch (System.ComponentModel.Win32Exception) { /* Fall back to the lease if access is denied. */ }
        }
        return segment.HeartbeatAtUtc.HasValue && segment.HeartbeatAtUtc.Value > nowUtc.AddSeconds(-45);
    }
}
