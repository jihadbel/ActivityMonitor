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

[Collection(ActivityMonitorPathsCollection.Name)]
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
        public Task<RecordingSegment?> GetActiveSegmentAsync(string machineId, CancellationToken ct = default, int? sessionId = null) => Task.FromResult<RecordingSegment?>(null);
        public Task<List<RecordingSegment>> GetByDateRangeAsync(DateTime startUtc, DateTime endUtc, string? machineId = null, CancellationToken ct = default) => Task.FromResult(Segments);

        public Task<List<RecordingSegment>> GetSegmentsOlderThanAsync(DateTime cutoffUtc, CancellationToken ct = default)
        {
            return Task.FromResult(Segments.FindAll(s => s.StartedAtUtc < cutoffUtc && s.Status != RecordingStatus.Deleted));
        }

        public Task<List<RecordingSegment>> GetAllCompletedSegmentsAsync(CancellationToken ct = default)
        {
            return Task.FromResult(Segments.FindAll(s => s.Status == RecordingStatus.Completed));
        }

        public Task<List<RecordingSegment>> GetSegmentsCoveringTimeRangeAsync(DateTime startUtc, DateTime endUtc, string? machineId = null, CancellationToken ct = default, int? sessionId = null) => Task.FromResult(Segments);
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
        public Task<bool> SetRecordingSegmentIfMissingAsync(long activityEventId, long recordingSegmentId, CancellationToken ct = default) => Task.FromResult(true);
        public Task<ActivityEvent?> GetByIdAsync(long id, CancellationToken ct = default) => Task.FromResult(Events.Find(e => e.Id == id));
        public Task<List<ActivityEvent>> GetByDateRangeAsync(DateTime startUtc, DateTime endUtc, string? machineId = null, CancellationToken ct = default) => Task.FromResult(Events);
        public Task<List<ActivityEvent>> GetUnlinkedActivitiesAsync(DateTime beforeUtc, CancellationToken ct = default, long afterId = 0) => Task.FromResult(new List<ActivityEvent>());
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
        var tempRoot = Path.Combine(Path.GetTempPath(), "ActivityMonitor_StorageTest_" + Guid.NewGuid().ToString("N"));
        ActivityMonitorPaths.SetCustomRootDirectory(tempRoot);
        var recRepo = new InMemoryRecordingRepository();
        var actRepo = new InMemoryActivityRepository();

        try
        {
            // 30 days old recording and activity
            var oldDate = DateTime.UtcNow.AddDays(-30);
            var recordingPath = ActivityMonitorPaths.GetRecordingFilePath("PC-01", oldDate, "old_rec.mp4");
            await File.WriteAllBytesAsync(recordingPath, new byte[] { 1, 2, 3, 4 });

            var oldSegment = new RecordingSegment
            {
                Id = 1,
                MachineId = "PC-01",
                FileName = "old_rec.mp4",
                FilePath = recordingPath,
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

            var optionsMonitor = new TestOptionsMonitor<StorageOptions>(new StorageOptions
            {
                RecordingRetentionDays = 14,
                ActivityRetentionDays = 90
            });

            var storageService = new StorageManagementService(recRepo, actRepo, optionsMonitor, NullLogger<StorageManagementService>.Instance);

            var deletedCount = await storageService.EnforceRetentionPoliciesAsync();

            Assert.Equal(1, deletedCount);
            Assert.Equal(RecordingStatus.Deleted, oldSegment.Status);
            Assert.False(File.Exists(recordingPath));

            // Crucial requirement: ActivityEvents must NOT be deleted just because the video was deleted!
            Assert.Single(actRepo.Events);
            Assert.Equal(1, actRepo.Events[0].RecordingSegmentId);
        }
        finally
        {
            ActivityMonitorPaths.SetCustomRootDirectory(null);
            if (Directory.Exists(tempRoot))
            {
                try { Directory.Delete(tempRoot, true); } catch { }
            }
        }
    }

    [Fact]
    public async Task RetentionPolicy_ShouldRefusePathOutsideRecordingsRoot()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "ActivityMonitor_StorageTest_" + Guid.NewGuid().ToString("N"));
        var outsideFile = Path.Combine(Path.GetTempPath(), "ActivityMonitor_Outside_" + Guid.NewGuid().ToString("N") + ".mp4");
        ActivityMonitorPaths.SetCustomRootDirectory(tempRoot);

        try
        {
            await File.WriteAllBytesAsync(outsideFile, new byte[] { 1, 2, 3, 4 });
            var segment = new RecordingSegment
            {
                Id = 7,
                MachineId = "PC-01",
                FileName = Path.GetFileName(outsideFile),
                FilePath = outsideFile,
                StartedAtUtc = DateTime.UtcNow.AddDays(-30),
                Status = RecordingStatus.Completed
            };

            var recRepo = new InMemoryRecordingRepository();
            await recRepo.AddAsync(segment);
            var storageService = CreateStorageService(recRepo);

            var deletedCount = await storageService.EnforceRetentionPoliciesAsync();

            Assert.Equal(0, deletedCount);
            Assert.True(File.Exists(outsideFile));
            Assert.Equal(RecordingStatus.Completed, segment.Status);
        }
        finally
        {
            ActivityMonitorPaths.SetCustomRootDirectory(null);
            if (File.Exists(outsideFile)) File.Delete(outsideFile);
            if (Directory.Exists(tempRoot))
            {
                try { Directory.Delete(tempRoot, true); } catch { }
            }
        }
    }

    [Fact]
    public async Task RetentionPolicy_ShouldKeepStatus_WhenFileDeletionFails()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "ActivityMonitor_StorageTest_" + Guid.NewGuid().ToString("N"));
        ActivityMonitorPaths.SetCustomRootDirectory(tempRoot);

        try
        {
            var oldDate = DateTime.UtcNow.AddDays(-30);
            var recordingPath = ActivityMonitorPaths.GetRecordingFilePath("PC-01", oldDate, "locked.mp4");
            await File.WriteAllBytesAsync(recordingPath, new byte[] { 1, 2, 3, 4 });

            var segment = new RecordingSegment
            {
                Id = 8,
                MachineId = "PC-01",
                FileName = "locked.mp4",
                FilePath = recordingPath,
                StartedAtUtc = oldDate,
                Status = RecordingStatus.Completed
            };

            var recRepo = new InMemoryRecordingRepository();
            await recRepo.AddAsync(segment);
            var storageService = CreateStorageService(recRepo);

            await using (var lockStream = new FileStream(recordingPath, FileMode.Open, FileAccess.Read, FileShare.None))
            {
                var deletedCount = await storageService.EnforceRetentionPoliciesAsync();
                Assert.Equal(0, deletedCount);
                Assert.Equal(RecordingStatus.Completed, segment.Status);
                Assert.True(File.Exists(recordingPath));
            }
        }
        finally
        {
            ActivityMonitorPaths.SetCustomRootDirectory(null);
            if (Directory.Exists(tempRoot))
            {
                try { Directory.Delete(tempRoot, true); } catch { }
            }
        }
    }

    [Theory]
    [InlineData(@"C:\Windows\System32\config\SAM")]
    [InlineData(@"..\outside.mp4")]
    public void StoragePathGuard_ShouldRejectPathsOutsideAllowedRoot(string candidate)
    {
        var root = Path.Combine(Path.GetTempPath(), "ActivityMonitor", "Recordings");

        var accepted = StoragePathGuard.TryResolvePathWithinRoot(candidate, root, out _, out _);

        Assert.False(accepted);
    }

    private static StorageManagementService CreateStorageService(InMemoryRecordingRepository recRepo)
    {
        var optionsMonitor = new TestOptionsMonitor<StorageOptions>(new StorageOptions
        {
            RecordingRetentionDays = 14,
            ActivityRetentionDays = 90,
            ReportRetentionDays = 90,
            LogRetentionDays = 30,
            MaxStorageSizeGB = 50,
            MinimumFreeDiskSpaceGB = 0
        });

        return new StorageManagementService(
            recRepo,
            new InMemoryActivityRepository(),
            optionsMonitor,
            NullLogger<StorageManagementService>.Instance);
    }

    private class TestOptionsMonitor<T> : IOptionsMonitor<T> where T : class
    {
        public TestOptionsMonitor(T currentValue) => CurrentValue = currentValue;
        public T CurrentValue { get; }
        public T Get(string? name) => CurrentValue;
        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }
}
