using System.Threading;
using System.Threading.Tasks;
using ActivityMonitor.Core.Entities;

namespace ActivityMonitor.Core.Interfaces;

public interface IScreenRecordingService
{
    bool IsRecording { get; }
    RecordingSegment? CurrentSegment { get; }

    Task<RecordingSegment?> StartRecordingSegmentAsync(CancellationToken ct = default);
    Task<RecordingSegment?> StopRecordingSegmentAsync(CancellationToken ct = default);
    Task<RecordingSegment?> SwitchToNewSegmentAsync(CancellationToken ct = default);
}
