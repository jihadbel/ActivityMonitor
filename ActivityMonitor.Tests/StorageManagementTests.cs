using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using ActivityMonitor.Core.Entities;
using ActivityMonitor.Core.Enums;
using ActivityMonitor.Core.Interfaces;
using ActivityMonitor.Core.Options;
using ActivityMonitor.Infrastructure.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace ActivityMonitor.Tests;

public class StorageManagementTests
{
    private class InMemoryRecordingRepository : IRecordingRepository
    {
        public List<RecordingSegment> Segments { get; } = new();

        public Task<RecordingSegment> AddAsync(RecordingSegment segment, CancellationToken ct = default)
        {
            Segments.Add(segment);
            return Task.FromResult(segment);
        }

        public Task UpdateAsync(RecordingSegment segment, CancellationToken ct = default) => Task.CompletedTask;
        public Task<RecordingSegment?> GetByIdAsync(long id, CancellationToken ct = default) => Task.FromResult(Segments.Find(s => s.Id == id));
        public Task<RecordingSegment?> GetActiveSegmentAsync(string machineId, CancellationToken ct = default) => Task.FromResult<RecordingSegment?>(null);
        public Task<List<RecordingSegment>> GetByDateRangeAsync(DateTime startUtc, DateTime endUtc, string? machineId = null, CancellationToken ct = default) => Task.FromResult(Segments);

        public Task<List<RecordingSegment>> GetSegmentsOlderThanAsync(DateTime cutoffUtc, CancellationToken ct = default)
        {
            return Task.FromResult(Segments.FindAll(s => s.StartedAtUtc < cutoffUtc && s.Status != RecordingStatus.Deleted));
        }

        public Task<List<RecordingSegment>> GetAllCompletedSegmentsAsync(CancellationToken ct = default)
        {
            return Task.FromResult(Segments.FindAll(s => s.Status == RecordingStatus.Completed));
        }

        public Task<List<RecordingSegment>> GetSegmentsCoveringTimeRangeAsync(DateTime startUtc, DateTime endUtc, string? machineId = null, CancellationToken ct = default) => Task.FromResult(Segments);
        public Task<int> MarkAsDeletedAsync(long id, CancellationToken ct = default) => Task.FromResult(1);
    }

    private class InMemoryActivityRepository : IActivityRepository
    {
        public List<ActivityEvent> Events { get; } = new();

        public Task<ActivityEvent> AddAsync(ActivityEvent activityEvent, CancellationToken ct = default)
        {
            Events.Add(activityEvent);
            return Task.FromResult(activityEvent);
        }

        public Task AddRangeAsync(IEnumerable<ActivityEvent> activityEvents, CancellationToken ct = default)
        {
            Events.AddRange(activityEvents);
            return Task.CompletedTask;
        }

        public Task UpdateAsync(ActivityEvent activityEvent, CancellationToken ct = default) => Task.CompletedTask;
        public Task<ActivityEvent?> GetByIdAsync(long id, CancellationToken ct = default) => Task.FromResult(Events.Find(e => e.Id == id));
        public Task<List<ActivityEvent>> GetByDateRangeAsync(DateTime startUtc, DateTime endUtc, string? machineId = null, CancellationToken ct = default) => Task.FromResult(Events);
        public Task<List<ActivityEvent>> GetUnlinkedActivitiesAsync(DateTime beforeUtc, CancellationToken ct = default) => Task.FromResult(new List<ActivityEvent>());
        public Task<Core.Models.DailyActivitySummary> GetDailySummaryAsync(DateTime dateUtc, string? machineId = null, CancellationToken ct = default) => Task.FromResult(new Core.Models.DailyActivitySummary());
        public Task<List<Core.Models.ApplicationUsageSummary>> GetTopApplicationsAsync(DateTime dateUtc, string? machineId = null, CancellationToken ct = default) => Task.FromResult(new List<Core.Models.ApplicationUsageSummary>());

        public Task<int> PurgeOlderThanAsync(DateTime cutoffUtc, CancellationToken ct = default)
        {
            int count = Events.RemoveAll(e => e.StartedAtUtc < cutoffUtc);
            return Task.FromResult(count);
        }
    }

    [Fact]
    public async Task RetentionPolicy_ShouldMarkExpiredRecordingsAsDeleted_WithoutDeletingActivityEvents()
    {
        var recRepo = new InMemoryRecordingRepository();
        var actRepo = new InMemoryActivityRepository();

        // 30 days old recording and activity
        var oldDate = DateTime.UtcNow.AddDays(-30);
        var oldSegment = new RecordingSegment
        {
            Id = 1,
            MachineId = "PC-01",
            FileName = "old_rec.mp4",
            FilePath = @"C:\Fake\old_rec.mp4",
            StartedAtUtc = oldDate,
            EndedAtUtc = oldDate.AddMinutes(10),
            Status = RecordingStatus.Completed
        };
        await recRepo.AddAsync(oldSegment);

        var oldActivity = new ActivityEvent
        {
            Id = 1,
            MachineId = "PC-01",
            ProcessName = "notepad",
            StartedAtUtc = oldDate.AddMinutes(2),
            DurationSeconds = 120,
            RecordingSegmentId = 1
        };
        await actRepo.AddAsync(oldActivity);

        var options = Microsoft.Extensions.Options.Options.Create(new StorageOptions
        {
            RecordingRetentionDays = 14,
            ActivityRetentionDays = 90
        });

        var storageService = new StorageManagementService(recRepo, actRepo, options, NullLogger<StorageManagementService>.Instance);

        var deletedCount = await storageService.EnforceRetentionPoliciesAsync();

        Assert.Equal(1, deletedCount);
        Assert.Equal(RecordingStatus.Deleted, oldSegment.Status);

        // Crucial requirement: ActivityEvents must NOT be deleted just because the video was deleted!
        Assert.Single(actRepo.Events);
        Assert.Equal(1, actRepo.Events[0].RecordingSegmentId);
    }
}
