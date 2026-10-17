using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ActivityMonitor.Core.Entities;
using ActivityMonitor.Core.Enums;
using ActivityMonitor.Core.Interfaces;
using ActivityMonitor.Infrastructure.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ActivityMonitor.Tests;

public class RecordingLocatorTests
{
    private class FakeRecordingRepository : IRecordingRepository
    {
        public List<RecordingSegment> Segments { get; } = new();

        public Task<RecordingSegment> AddAsync(RecordingSegment segment, CancellationToken ct = default)
        {
            segment.Id = Segments.Count + 1;
            Segments.Add(segment);
            return Task.FromResult(segment);
        }

        public Task<List<RecordingSegment>> GetSegmentsCoveringTimeRangeAsync(DateTime startUtc, DateTime endUtc, string? machineId = null, CancellationToken ct = default, int? sessionId = null)
        {
            var matches = new List<RecordingSegment>();
            foreach (var s in Segments)
            {
                var sEnd = s.EndedAtUtc ?? s.StartedAtUtc.AddSeconds(s.DurationSeconds);
                if (s.StartedAtUtc <= endUtc && sEnd >= startUtc)
                {
                    matches.Add(s);
                }
            }
            return Task.FromResult(matches);
        }

        public Task UpdateAsync(RecordingSegment segment, CancellationToken ct = default) => Task.CompletedTask;
        public Task<RecordingSegment?> GetByIdAsync(long id, CancellationToken ct = default) => Task.FromResult(Segments.Find(s => s.Id == id));
        public Task<RecordingSegment?> GetActiveSegmentAsync(string machineId, CancellationToken ct = default, int? sessionId = null) => Task.FromResult<RecordingSegment?>(null);
        public Task<List<RecordingSegment>> GetByDateRangeAsync(DateTime startUtc, DateTime endUtc, string? machineId = null, CancellationToken ct = default) => Task.FromResult(Segments);
        public Task<List<RecordingSegment>> GetSegmentsOlderThanAsync(DateTime cutoffUtc, CancellationToken ct = default) => Task.FromResult(new List<RecordingSegment>());
        public Task<List<RecordingSegment>> GetAllCompletedSegmentsAsync(CancellationToken ct = default) => Task.FromResult(Segments);
        public Task<int> MarkAsDeletedAsync(long id, CancellationToken ct = default) => Task.FromResult(1);
    }

    private class FakeActivityRepository : IActivityRepository
    {
        public Task<ActivityEvent> AddAsync(ActivityEvent activityEvent, CancellationToken ct = default) => Task.FromResult(activityEvent);
        public Task AddRangeAsync(IEnumerable<ActivityEvent> activityEvents, CancellationToken ct = default) => Task.CompletedTask;
        public Task UpdateAsync(ActivityEvent activityEvent, CancellationToken ct = default) => Task.CompletedTask;
        public Task<bool> SetRecordingSegmentIfMissingAsync(long activityEventId, long recordingSegmentId, CancellationToken ct = default) => Task.FromResult(true);
        public Task<ActivityEvent?> GetByIdAsync(long id, CancellationToken ct = default) => Task.FromResult<ActivityEvent?>(null);
        public Task<List<ActivityEvent>> GetByDateRangeAsync(DateTime startUtc, DateTime endUtc, string? machineId = null, CancellationToken ct = default) => Task.FromResult(new List<ActivityEvent>());
        public Task<List<ActivityEvent>> GetUnlinkedActivitiesAsync(DateTime beforeUtc, CancellationToken ct = default, long afterId = 0) => Task.FromResult(new List<ActivityEvent>());
        public Task<Core.Models.DailyActivitySummary> GetDailySummaryAsync(DateTime dateUtc, string? machineId = null, CancellationToken ct = default) => Task.FromResult(new Core.Models.DailyActivitySummary());
        public Task<List<Core.Models.ApplicationUsageSummary>> GetTopApplicationsAsync(DateTime dateUtc, string? machineId = null, CancellationToken ct = default) => Task.FromResult(new List<Core.Models.ApplicationUsageSummary>());
        public Task<int> PurgeOlderThanAsync(DateTime cutoffUtc, CancellationToken ct = default) => Task.FromResult(0);
    }

    [Fact]
    public async Task LocateAsync_ShouldCalculateExactOffsetAndDuration_WhenInsideSingleRecording()
    {
        // Setup: Recording from 14:20:00 to 14:30:00 (10 min = 600s)
        var recStart = new DateTime(2026, 10, 4, 14, 20, 0, DateTimeKind.Utc);
        var recEnd = new DateTime(2026, 10, 4, 14, 30, 0, DateTimeKind.Utc);

        var fakeRecRepo = new FakeRecordingRepository();
        await fakeRecRepo.AddAsync(new RecordingSegment
        {
            Id = 1,
            MachineId = "PC-01",
            FileName = "2026-10-04_14-20-00.mp4",
            FilePath = @"C:\ActivityMonitor\Recordings\2026-10-04_14-20-00.mp4",
            StartedAtUtc = recStart,
            EndedAtUtc = recEnd,
            DurationSeconds = 600,
            Status = RecordingStatus.Completed
        });

        var locator = new RecordingLocatorService(fakeRecRepo, new FakeActivityRepository(), NullLogger<RecordingLocatorService>.Instance);

        // Activity from 14:23:10 to 14:27:42 (Duration: 4m 32s = 272s, Offset: 3m 10s = 190s)
        var actStart = new DateTime(2026, 10, 4, 14, 23, 10, DateTimeKind.Utc);
        var actEnd = new DateTime(2026, 10, 4, 14, 27, 42, DateTimeKind.Utc);

        var activity = new ActivityEvent
        {
            Id = 100,
            MachineId = "PC-01",
            StartedAtUtc = actStart,
            EndedAtUtc = actEnd,
            DurationSeconds = 272
        };

        var results = await locator.LocateAsync(activity);

        Assert.Single(results);
        var match = results[0];
        Assert.Equal("2026-10-04_14-20-00.mp4", match.FileName);
        Assert.Equal(TimeSpan.FromSeconds(190), match.Offset); // 00:03:10
        Assert.Equal(TimeSpan.FromSeconds(272), match.Duration); // 00:04:32
    }

    [Fact]
    public async Task LocateAsync_ShouldReturnMultipleResults_WhenActivitySpansMultipleRecordings()
    {
        // Segment 1: 14:20:00 to 14:30:00
        var seg1Start = new DateTime(2026, 10, 4, 14, 20, 0, DateTimeKind.Utc);
        var seg1End = new DateTime(2026, 10, 4, 14, 30, 0, DateTimeKind.Utc);

        // Segment 2: 14:30:00 to 14:40:00
        var seg2Start = new DateTime(2026, 10, 4, 14, 30, 0, DateTimeKind.Utc);
        var seg2End = new DateTime(2026, 10, 4, 14, 40, 0, DateTimeKind.Utc);

        var fakeRecRepo = new FakeRecordingRepository();
        await fakeRecRepo.AddAsync(new RecordingSegment
        {
            Id = 1,
            MachineId = "PC-01",
            FileName = "segment1.mp4",
            FilePath = @"C:\segment1.mp4",
            StartedAtUtc = seg1Start,
            EndedAtUtc = seg1End,
            DurationSeconds = 600,
            Status = RecordingStatus.Completed
        });
        await fakeRecRepo.AddAsync(new RecordingSegment
        {
            Id = 2,
            MachineId = "PC-01",
            FileName = "segment2.mp4",
            FilePath = @"C:\segment2.mp4",
            StartedAtUtc = seg2Start,
            EndedAtUtc = seg2End,
            DurationSeconds = 600,
            Status = RecordingStatus.Completed
        });

        var locator = new RecordingLocatorService(fakeRecRepo, new FakeActivityRepository(), NullLogger<RecordingLocatorService>.Instance);

        // Activity spanning: 14:28:00 to 14:33:00 (2 minutes in segment 1, 3 minutes in segment 2)
        var actStart = new DateTime(2026, 10, 4, 14, 28, 0, DateTimeKind.Utc);
        var actEnd = new DateTime(2026, 10, 4, 14, 33, 0, DateTimeKind.Utc);

        var activity = new ActivityEvent
        {
            Id = 101,
            MachineId = "PC-01",
            StartedAtUtc = actStart,
            EndedAtUtc = actEnd,
            DurationSeconds = 300
        };

        var results = await locator.LocateAsync(activity);

        Assert.Equal(2, results.Count);

        // Result 1 in segment 1: starts at 14:28:00 (offset: 8m = 480s), ends at 14:30:00 (duration: 2m = 120s)
        Assert.Equal("segment1.mp4", results[0].FileName);
        Assert.Equal(TimeSpan.FromMinutes(8), results[0].Offset);
        Assert.Equal(TimeSpan.FromMinutes(2), results[0].Duration);

        // Result 2 in segment 2: starts at 14:30:00 (offset: 0s), ends at 14:33:00 (duration: 3m = 180s)
        Assert.Equal("segment2.mp4", results[1].FileName);
        Assert.Equal(TimeSpan.Zero, results[1].Offset);
        Assert.Equal(TimeSpan.FromMinutes(3), results[1].Duration);
    }
}
