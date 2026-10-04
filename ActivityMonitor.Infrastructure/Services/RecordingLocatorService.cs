using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ActivityMonitor.Core.Entities;
using ActivityMonitor.Core.Interfaces;
using ActivityMonitor.Core.Models;
using Microsoft.Extensions.Logging;

namespace ActivityMonitor.Infrastructure.Services;

public class RecordingLocatorService : IRecordingLocatorService
{
    private readonly IRecordingRepository _recordingRepository;
    private readonly IActivityRepository _activityRepository;
    private readonly ILogger<RecordingLocatorService> _logger;

    public RecordingLocatorService(
        IRecordingRepository recordingRepository,
        IActivityRepository activityRepository,
        ILogger<RecordingLocatorService> logger)
    {
        _recordingRepository = recordingRepository;
        _activityRepository = activityRepository;
        _logger = logger;
    }

    public async Task<List<RecordingLocationResult>> LocateAsync(ActivityEvent activityEvent, CancellationToken ct = default)
    {
        var startUtc = activityEvent.StartedAtUtc;
        var endUtc = activityEvent.EndedAtUtc ?? startUtc.AddSeconds(Math.Max(1, activityEvent.DurationSeconds));

        var segments = await _recordingRepository.GetSegmentsCoveringTimeRangeAsync(startUtc, endUtc, activityEvent.MachineId, ct);

        var results = new List<RecordingLocationResult>();

        foreach (var segment in segments)
        {
            var segmentEnd = segment.EndedAtUtc ?? (segment.StartedAtUtc.AddSeconds(Math.Max(1, segment.DurationSeconds)));

            // Calculate overlap between activity and segment
            var overlapStart = startUtc > segment.StartedAtUtc ? startUtc : segment.StartedAtUtc;
            var overlapEnd = endUtc < segmentEnd ? endUtc : segmentEnd;

            if (overlapEnd >= overlapStart)
            {
                var offset = overlapStart - segment.StartedAtUtc;
                if (offset < TimeSpan.Zero) offset = TimeSpan.Zero;

                var duration = overlapEnd - overlapStart;

                results.Add(new RecordingLocationResult
                {
                    ActivityEventId = activityEvent.Id,
                    RecordingSegmentId = segment.Id,
                    FilePath = segment.FilePath,
                    FileName = segment.FileName,
                    Offset = offset,
                    Duration = duration,
                    FileExists = File.Exists(segment.FilePath)
                });
            }
        }

        return results;
    }

    public async Task<List<RecordingLocationResult>> LocateByTimestampAsync(DateTime timestampUtc, TimeSpan? duration = null, string? machineId = null, CancellationToken ct = default)
    {
        var dur = duration ?? TimeSpan.FromSeconds(1);
        var endUtc = timestampUtc.Add(dur);

        var segments = await _recordingRepository.GetSegmentsCoveringTimeRangeAsync(timestampUtc, endUtc, machineId, ct);
        var results = new List<RecordingLocationResult>();

        foreach (var segment in segments)
        {
            var segmentEnd = segment.EndedAtUtc ?? (segment.StartedAtUtc.AddSeconds(Math.Max(1, segment.DurationSeconds)));

            var overlapStart = timestampUtc > segment.StartedAtUtc ? timestampUtc : segment.StartedAtUtc;
            var overlapEnd = endUtc < segmentEnd ? endUtc : segmentEnd;

            if (overlapEnd >= overlapStart)
            {
                var offset = overlapStart - segment.StartedAtUtc;
                if (offset < TimeSpan.Zero) offset = TimeSpan.Zero;

                results.Add(new RecordingLocationResult
                {
                    ActivityEventId = 0,
                    RecordingSegmentId = segment.Id,
                    FilePath = segment.FilePath,
                    FileName = segment.FileName,
                    Offset = offset,
                    Duration = overlapEnd - overlapStart,
                    FileExists = File.Exists(segment.FilePath)
                });
            }
        }

        return results;
    }

    public async Task<int> LinkUnlinkedActivitiesAsync(DateTime beforeUtc, CancellationToken ct = default)
    {
        var unlinked = await _activityRepository.GetUnlinkedActivitiesAsync(beforeUtc, ct);
        if (unlinked.Count == 0) return 0;

        int linkedCount = 0;
        foreach (var activity in unlinked)
        {
            var results = await LocateAsync(activity, ct);
            if (results.Count > 0)
            {
                // Link to the first covering segment
                activity.RecordingSegmentId = results[0].RecordingSegmentId;
                await _activityRepository.UpdateAsync(activity, ct);
                linkedCount++;
            }
        }

        _logger.LogInformation("Successfully linked {Count} activities to video recording segments", linkedCount);
        return linkedCount;
    }
}
