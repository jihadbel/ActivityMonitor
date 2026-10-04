using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ActivityMonitor.Core.Entities;

namespace ActivityMonitor.Core.Interfaces;

public interface IRecordingRepository
{
    Task<RecordingSegment> AddAsync(RecordingSegment segment, CancellationToken ct = default);
    Task UpdateAsync(RecordingSegment segment, CancellationToken ct = default);
    Task<RecordingSegment?> GetByIdAsync(long id, CancellationToken ct = default);
    Task<RecordingSegment?> GetActiveSegmentAsync(string machineId, CancellationToken ct = default);
    Task<List<RecordingSegment>> GetByDateRangeAsync(DateTime startUtc, DateTime endUtc, string? machineId = null, CancellationToken ct = default);
    Task<List<RecordingSegment>> GetSegmentsCoveringTimeRangeAsync(DateTime startUtc, DateTime endUtc, string? machineId = null, CancellationToken ct = default);
    Task<List<RecordingSegment>> GetSegmentsOlderThanAsync(DateTime cutoffUtc, CancellationToken ct = default);
    Task<List<RecordingSegment>> GetAllCompletedSegmentsAsync(CancellationToken ct = default);
    Task<int> MarkAsDeletedAsync(long id, CancellationToken ct = default);
}
