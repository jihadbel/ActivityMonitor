using ActivityMonitor.Core.Entities;
using ActivityMonitor.Core.Enums;
using ActivityMonitor.Core.Options;
using ActivityMonitor.Infrastructure.Data;
using ActivityMonitor.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace ActivityMonitor.Tests;

[Collection(ActivityMonitorPathsCollection.Name)]
public class DataIntegrityServiceTests
{
    [Fact]
    public async Task AuditAndRepairAsync_ShouldRecoverStaleAndMarkMissingWithoutTouchingUnsafePath()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "ActivityMonitor_IntegrityTest_" + Guid.NewGuid().ToString("N"));
        ActivityMonitorPaths.SetCustomRootDirectory(tempRoot);

        try
        {
            ActivityMonitorPaths.EnsureDirectoriesExist();
            var databasePath = Path.Combine(tempRoot, "integrity.db");
            var options = new DbContextOptionsBuilder<ActivityMonitorDbContext>()
                .UseSqlite($"Data Source={databasePath}")
                .Options;
            var factory = new TestDbContextFactory(options);

            var existingPath = ActivityMonitorPaths.GetRecordingFilePath(
                "PC-01",
                DateTime.UtcNow.AddHours(-1),
                "stale.mp4");
            await File.WriteAllBytesAsync(existingPath, [1, 2, 3, 4]);
            File.SetLastWriteTimeUtc(existingPath, DateTime.UtcNow.AddMinutes(-10));
            var activePath = ActivityMonitorPaths.GetRecordingFilePath(
                "PC-01",
                DateTime.UtcNow.AddHours(-1),
                "active.mp4");
            await File.WriteAllBytesAsync(activePath, [1, 2, 3, 4, 5]);
            var missingPath = ActivityMonitorPaths.GetRecordingFilePath(
                "PC-01",
                DateTime.UtcNow.AddHours(-2),
                "missing.mp4");
            var unsafePath = Path.Combine(Path.GetTempPath(), "outside-recording.mp4");

            await using (var context = factory.CreateDbContext())
            {
                await context.Database.EnsureCreatedAsync();
                context.RecordingSegments.AddRange(
                    CreateSegment(existingPath, RecordingStatus.InProgress, DateTime.UtcNow.AddHours(-1)),
                    CreateSegment(activePath, RecordingStatus.InProgress, DateTime.UtcNow.AddHours(-1)),
                    CreateSegment(missingPath, RecordingStatus.Completed, DateTime.UtcNow.AddHours(-2)),
                    CreateSegment(unsafePath, RecordingStatus.Completed, DateTime.UtcNow.AddHours(-3)));
                await context.SaveChangesAsync();
            }

            var service = new DataIntegrityService(
                factory,
                new TestOptionsMonitor<RecordingOptions>(new RecordingOptions { SegmentMinutes = 1 }),
                NullLogger<DataIntegrityService>.Instance);

            var result = await service.AuditAndRepairAsync();

            Assert.Equal(1, result.StaleRecordingsRecovered);
            Assert.Equal(1, result.MissingRecordingsMarkedDeleted);
            Assert.Equal(1, result.UnsafePathsSkipped);

            await using var verificationContext = factory.CreateDbContext();
            var segments = await verificationContext.RecordingSegments.ToListAsync();
            Assert.Equal(RecordingStatus.Completed, segments.Single(segment => segment.FilePath == unsafePath).Status);
            Assert.Equal(RecordingStatus.Deleted, segments.Single(segment => segment.FilePath == missingPath).Status);
            var recovered = segments.Single(segment => segment.FilePath == existingPath);
            Assert.Equal(RecordingStatus.Corrupted, recovered.Status);
            Assert.Equal(4, recovered.FileSizeBytes);
            Assert.Equal(RecordingStatus.InProgress, segments.Single(segment => segment.FilePath == activePath).Status);
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

    private static RecordingSegment CreateSegment(string path, RecordingStatus status, DateTime startedAtUtc) =>
        new()
        {
            MachineId = "PC-01",
            SessionId = 1,
            StartedAtUtc = startedAtUtc,
            FilePath = path,
            FileName = Path.GetFileName(path),
            Status = status
        };

    private sealed class TestDbContextFactory : IDbContextFactory<ActivityMonitorDbContext>
    {
        private readonly DbContextOptions<ActivityMonitorDbContext> _options;

        public TestDbContextFactory(DbContextOptions<ActivityMonitorDbContext> options) => _options = options;
        public ActivityMonitorDbContext CreateDbContext() => new(_options);
        public Task<ActivityMonitorDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(CreateDbContext());
    }

    private sealed class TestOptionsMonitor<T> : IOptionsMonitor<T> where T : class
    {
        public TestOptionsMonitor(T currentValue) => CurrentValue = currentValue;
        public T CurrentValue { get; }
        public T Get(string? name) => CurrentValue;
        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }
}
