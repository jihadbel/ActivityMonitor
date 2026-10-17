using System.Reflection;
using ActivityMonitor.Agent.Recording;
using ActivityMonitor.Agent.Tracking;
using ActivityMonitor.Core.Entities;
using ActivityMonitor.Core.Enums;
using ActivityMonitor.Core.Interfaces;
using ActivityMonitor.Core.Models;
using ActivityMonitor.Core.Options;
using ActivityMonitor.Infrastructure.ContextProviders;
using ActivityMonitor.Infrastructure.Data;
using ActivityMonitor.Infrastructure.Repositories;
using ActivityMonitor.Infrastructure.Services;
using ActivityMonitor.Viewer.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace ActivityMonitor.Tests;

[Collection(ActivityMonitorPathsCollection.Name)]
public sealed class ReliabilityRegressionTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ActivityMonitor_Regression_" + Guid.NewGuid().ToString("N"));
    private readonly Factory _factory = new();
    private static readonly DateTime Start = new(2026, 10, 10, 10, 0, 0, DateTimeKind.Utc);

    public ReliabilityRegressionTests() { ActivityMonitorPaths.SetCustomRootDirectory(_root); }
    private ActivityRepository Activities => new(_factory, NullLogger<ActivityRepository>.Instance);
    private RecordingRepository Recordings => new(_factory, NullLogger<RecordingRepository>.Instance);
    private RecordingLocatorService Locator => new(Recordings, Activities, NullLogger<RecordingLocatorService>.Instance);

    [Fact]
    public async Task Lookup_DoesNotReturnAnotherSessionsRecording()
    {
        await ActivityMonitorDbContext.InitializeDatabaseAsync();
        await Recordings.AddAsync(Segment(2));
        var correct = await Recordings.AddAsync(Segment(1));
        var matches = await Locator.LocateAsync(Activity(1));
        Assert.Single(matches);
        Assert.Equal(correct.Id, matches[0].RecordingSegmentId);
        Assert.Null(await Recordings.GetActiveSegmentAsync("PC", sessionId: 3));
    }

    [Fact]
    public async Task Lookup_FindsLiveRecordingPastItsFirstSecond()
    {
        await ActivityMonitorDbContext.InitializeDatabaseAsync();
        var segment = Segment(1);
        segment.Status = RecordingStatus.InProgress;
        segment.EndedAtUtc = null;
        segment.DurationSeconds = 0;
        segment.HeartbeatAtUtc = Start.AddMinutes(3);
        await Recordings.AddAsync(segment);
        var matches = await Locator.LocateAsync(Activity(1));
        Assert.Single(matches);
        Assert.Equal(TimeSpan.FromMinutes(1), matches[0].Offset);
    }

    [Fact]
    public void MidnightClipping_UsesTheVisibleDaysVideoOffset()
    {
        var segment = Segment(1);
        var activity = Activity(1);
        var rangeStart = Start.AddMinutes(1.5);
        var match = Assert.Single(RecordingTimeline.Resolve(activity, [segment], Start.AddMinutes(10), rangeStart, Start.AddMinutes(10)));
        Assert.Equal(TimeSpan.FromSeconds(90), match.Offset);
        Assert.Equal(TimeSpan.FromSeconds(30), match.Duration);
    }

    [Fact]
    public async Task Linking_AdvancesPastFiveHundredUnlinkableRows()
    {
        await ActivityMonitorDbContext.InitializeDatabaseAsync();
        await Activities.AddRangeAsync(Enumerable.Range(0, 500).Select(i => new ActivityEvent {
            MachineId = "NO-VIDEO", SessionId = 1, ProcessName = "System", UserIdentifier = "User",
            StartedAtUtc = Start.AddSeconds(i), EndedAtUtc = Start.AddSeconds(i), EventType = ActivityEventType.SessionLock
        }));
        var activity = await Activities.AddAsync(Activity(1));
        var segment = await Recordings.AddAsync(Segment(1));
        Assert.Equal(1, await Locator.LinkUnlinkedActivitiesAsync(Start.AddDays(1)));
        Assert.Equal(segment.Id, (await Activities.GetByIdAsync(activity.Id))!.RecordingSegmentId);
    }

    [Fact]
    public async Task Retention_KeepsOverlappingAndOpenActivities()
    {
        await ActivityMonitorDbContext.InitializeDatabaseAsync();
        var overlap = Activity(1); overlap.StartedAtUtc = Start.AddMinutes(-5); overlap.EndedAtUtc = Start.AddMinutes(5);
        var open = Activity(1); open.StartedAtUtc = Start.AddDays(-5); open.EndedAtUtc = null;
        var expired = Activity(1); expired.StartedAtUtc = Start.AddMinutes(-10); expired.EndedAtUtc = Start.AddMinutes(-1);
        await Activities.AddRangeAsync([overlap, open, expired]);
        Assert.Equal(1, await Activities.PurgeOlderThanAsync(Start));
        Assert.NotNull(await Activities.GetByIdAsync(overlap.Id));
        Assert.NotNull(await Activities.GetByIdAsync(open.Id));
    }

    [Fact]
    public async Task Tracker_RetriesInsertAfterTemporaryFailure_ThenFinalizesPersistedId()
    {
        var attempts = 0;
        var updates = new List<long>();
        var repository = DispatchProxy.Create<IActivityRepository, TestInterfaceProxy>();
        ((TestInterfaceProxy)(object)repository).Handler = (method, args) => {
            if (method.Name == "AddAsync") {
                if (++attempts == 1) return Task.FromException<ActivityEvent>(new IOException("Temporary write failure"));
                var activity = (ActivityEvent)args![0]!; activity.Id = 42; return Task.FromResult(activity);
            }
            if (method.Name == "UpdateAsync") { updates.Add(((ActivityEvent)args![0]!).Id); return Task.CompletedTask; }
            throw new NotSupportedException(method.Name);
        };
        var recording = DispatchProxy.Create<IScreenRecordingService, TestInterfaceProxy>();
        ((TestInterfaceProxy)(object)recording).Handler = (method, _) => method.Name switch {
            "get_CurrentSegment" => new RecordingSegment { Id = 1 }, "get_IsRecording" => true,
            _ => throw new NotSupportedException(method.Name)
        };
        using var tracker = new ActivityTracker(repository, recording, new ActivityContextProviderComposite([]),
            Options.Create(new ActivityTrackingOptions { WindowDebounceMs = 0 }), new Monitor<RecordingOptions>(new()), NullLogger<ActivityTracker>.Instance);
        var type = typeof(ActivityTracker);
        var snapshot = Activator.CreateInstance(type.GetNestedType("WindowSnapshot", BindingFlags.NonPublic)!, [IntPtr.Zero, 0, "Desktop", TimeSpan.Zero]);
        var handle = type.GetMethod("HandleActiveWindowStateAsync", BindingFlags.NonPublic | BindingFlags.Instance)!;
        await Assert.ThrowsAsync<IOException>(() => (Task)handle.Invoke(tracker, [snapshot])!);
        await (Task)handle.Invoke(tracker, [snapshot])!;
        var finalize = type.GetMethod("FinalizeCurrentActivityAsync", BindingFlags.NonPublic | BindingFlags.Instance)!;
        await (Task)finalize.Invoke(tracker, new object?[] { null })!;
        Assert.Equal(2, attempts);
        Assert.Equal(new long[] { 42 }, updates);
    }

    [Fact]
    public async Task StoppedRecorder_DoesNotRestartFromAnAlreadyQueuedRotation()
    {
        using var recorder = new ScreenRecordingService(null!, null!, new Monitor<RecordingOptions>(new()), NullLogger<ScreenRecordingService>.Instance);
        await recorder.StopRecordingSegmentAsync();
        Assert.Null(await recorder.SwitchToNewSegmentAsync());
        Assert.False(recorder.IsRecording);
    }

    [Fact]
    public async Task Integrity_NeverCompletesRandomBytes_AndProtectsLiveLease()
    {
        await ActivityMonitorDbContext.InitializeDatabaseAsync();
        Directory.CreateDirectory(ActivityMonitorPaths.RecordingsDirectory);
        var random = Segment(1);
        random.StartedAtUtc = DateTime.UtcNow.AddHours(-2); random.EndedAtUtc = null;
        random.Status = RecordingStatus.InProgress; random.OwnerInstanceId = "dead";
        random.HeartbeatAtUtc = DateTime.UtcNow.AddHours(-1);
        random.FilePath = Path.Combine(ActivityMonitorPaths.RecordingsDirectory, "invalid.mp4");
        await File.WriteAllBytesAsync(random.FilePath, new byte[4096]);
        await Recordings.AddAsync(random);
        var live = Segment(2); live.Status = RecordingStatus.InProgress; live.EndedAtUtc = null;
        live.OwnerInstanceId = "live"; live.HeartbeatAtUtc = DateTime.UtcNow;
        live.FilePath = Path.Combine(ActivityMonitorPaths.RecordingsDirectory, "live.mp4");
        await File.WriteAllBytesAsync(live.FilePath, new byte[4096]);
        await Recordings.AddAsync(live);
        var integrity = new DataIntegrityService(_factory, new Monitor<RecordingOptions>(new()), NullLogger<DataIntegrityService>.Instance);
        await integrity.AuditAndRepairAsync();
        Assert.Equal(RecordingStatus.Corrupted, (await Recordings.GetByIdAsync(random.Id))!.Status);
        Assert.Equal(RecordingStatus.InProgress, (await Recordings.GetByIdAsync(live.Id))!.Status);
        Assert.False(MediaFileInspector.TryReadVideoDuration(random.FilePath, out _));
    }

    [Fact]
    public async Task Settings_PreserveIndependentRetention_WithoutChangingWindowsStartup()
    {
        Directory.CreateDirectory(_root);
        await File.WriteAllTextAsync(ActivityMonitorPaths.SettingsPath, """
            {"Recording":{"VideoQuality":60},"Storage":{"ActivityRetentionDays":365,"ReportRetentionDays":180,"LogRetentionDays":45}}
            """);
        var service = new SettingsService(new WindowsServiceManager());
        var model = await service.LoadSettingsAsync();
        Assert.Equal(365, model.ActivityRetentionDays);
        model.VideoQuality = 75;
        // Build/validate the JSON separately from external startup changes.
        var result = SettingsService.ApplySettings(System.Text.Json.Nodes.JsonNode.Parse(await File.ReadAllTextAsync(ActivityMonitorPaths.SettingsPath))!, model);
        Assert.Equal(365, result["Storage"]!["ActivityRetentionDays"]!.GetValue<int>());
        Assert.Equal(180, result["Storage"]!["ReportRetentionDays"]!.GetValue<int>());
        Assert.Equal(45, result["Storage"]!["LogRetentionDays"]!.GetValue<int>());
        Assert.Equal(14, result["Storage"]!["RecordingRetentionDays"]!.GetValue<int>());
    }

    [Fact]
    public async Task CorruptSettings_FailInsteadOfMasqueradingAsDefaults()
    {
        Directory.CreateDirectory(_root);
        await File.WriteAllTextAsync(ActivityMonitorPaths.SettingsPath, "{invalid json");
        await Assert.ThrowsAsync<InvalidDataException>(() => new SettingsService(new WindowsServiceManager()).LoadSettingsAsync());
    }

    [Fact]
    public async Task ReportFreshness_DetectsEmptyFilesAndSourceChanges()
    {
        Directory.CreateDirectory(_root);
        var path = Path.Combine(_root, "report.csv");
        var activity = Activity(1); var segment = Segment(1);
        var hash = ReportFreshness.Fingerprint([activity], [segment]);
        await File.WriteAllTextAsync(path, "nonempty report");
        ReportFreshness.MarkComplete(path, hash);
        Assert.True(ReportFreshness.IsCurrent(path, hash));
        activity.DurationSeconds++;
        Assert.False(ReportFreshness.IsCurrent(path, ReportFreshness.Fingerprint([activity], [segment])));
        await File.WriteAllTextAsync(path, "");
        Assert.False(ReportFreshness.IsCurrent(path, hash));
    }

    [Fact]
    public async Task DatabaseInitialization_ConcurrentCallersFinishAndStampSchema()
    {
        await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Task.Run(() => ActivityMonitorDbContext.InitializeDatabaseAsync())));
        await using var context = new ActivityMonitorDbContext();
        var connection = context.Database.GetDbConnection(); await connection.OpenAsync();
        await using var command = connection.CreateCommand(); command.CommandText = "PRAGMA user_version;";
        Assert.Equal(ActivityMonitorDbContext.CurrentSchemaVersion, Convert.ToInt32(await command.ExecuteScalarAsync()));
        command.CommandText = "PRAGMA busy_timeout;";
        Assert.Equal(30000, Convert.ToInt32(await command.ExecuteScalarAsync()));
    }

    [Fact]
    public void MediaInspector_ReadsRealSyntheticVideoDuration()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "synthetic.mp4");
        Assert.True(MediaFileInspector.TryReadVideoDuration(path, out var duration));
        Assert.InRange(duration.TotalSeconds, 0.5, 3);
    }

    [Fact]
    public async Task Integrity_RecoversVerifiedVideoWithMediaDuration()
    {
        await ActivityMonitorDbContext.InitializeDatabaseAsync();
        Directory.CreateDirectory(ActivityMonitorPaths.RecordingsDirectory);
        var segment = Segment(1);
        segment.Status = RecordingStatus.InProgress; segment.EndedAtUtc = null;
        segment.OwnerInstanceId = "stopped-owner"; segment.HeartbeatAtUtc = DateTime.UtcNow.AddDays(-1);
        File.Copy(Path.Combine(AppContext.BaseDirectory, "Fixtures", "synthetic.mp4"), segment.FilePath);
        await Recordings.AddAsync(segment);
        var integrity = new DataIntegrityService(_factory, new Monitor<RecordingOptions>(new()), NullLogger<DataIntegrityService>.Instance);
        await integrity.AuditAndRepairAsync();
        var recovered = (await Recordings.GetByIdAsync(segment.Id))!;
        Assert.Equal(RecordingStatus.Recovered, recovered.Status);
        Assert.InRange(recovered.DurationSeconds, 0.5, 3);
        var activity = Activity(1); activity.StartedAtUtc = segment.StartedAtUtc.AddMilliseconds(100);
        activity.EndedAtUtc = activity.StartedAtUtc.AddMilliseconds(100);
        Assert.Single(await Locator.LocateAsync(activity));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public async Task Migration_PreservesOldRows_AndBacksUpBeforeAddingColumns(int oldVersion)
    {
        Directory.CreateDirectory(ActivityMonitorPaths.DataDirectory);
        await using (var context = new ActivityMonitorDbContext())
        {
            await context.Database.EnsureCreatedAsync();
            await context.Database.ExecuteSqlRawAsync("""
                DROP INDEX IX_RecordingSegments_HeartbeatAtUtc;
                ALTER TABLE RecordingSegments DROP COLUMN UserIdentifier;
                ALTER TABLE RecordingSegments DROP COLUMN OwnerInstanceId;
                ALTER TABLE RecordingSegments DROP COLUMN OwnerProcessId;
                ALTER TABLE RecordingSegments DROP COLUMN OwnerProcessStartedAtUtc;
                ALTER TABLE RecordingSegments DROP COLUMN HeartbeatAtUtc;
                """);
            context.ActivityEvents.Add(Activity(1)); await context.SaveChangesAsync();
            await context.Database.ExecuteSqlRawAsync(oldVersion == 0 ? "PRAGMA user_version=0;" : "PRAGMA user_version=1;");
        }
        await ActivityMonitorDbContext.InitializeDatabaseAsync();
        await using var upgraded = new ActivityMonitorDbContext();
        Assert.Single(await upgraded.ActivityEvents.ToListAsync());
        await upgraded.RecordingSegments.ToListAsync(); // The new mapped fields must all be readable.
        var backup = Assert.Single(Directory.GetFiles(Path.Combine(ActivityMonitorPaths.DataDirectory, "Backups"), "*.db"));
        await using var backupConnection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={backup};Pooling=False");
        await backupConnection.OpenAsync();
        await using var command = backupConnection.CreateCommand(); command.CommandText = "PRAGMA user_version;";
        Assert.Equal(oldVersion, Convert.ToInt32(await command.ExecuteScalarAsync()));
        command.CommandText = "SELECT COUNT(*) FROM ActivityEvents;";
        Assert.Equal(1, Convert.ToInt32(await command.ExecuteScalarAsync()));
    }

    [Fact]
    public async Task NewConnections_ApplySqlitePolicyWithoutDependingOnPoolReuse()
    {
        await ActivityMonitorDbContext.InitializeDatabaseAsync();
        var options = new DbContextOptionsBuilder<ActivityMonitorDbContext>()
            .UseSqlite($"Data Source={ActivityMonitorPaths.DatabasePath};Pooling=False").Options;
        await using var context = new ActivityMonitorDbContext(options);
        await context.Database.OpenConnectionAsync();
        await using var command = context.Database.GetDbConnection().CreateCommand();
        foreach (var (pragma, expected) in new[] { ("busy_timeout", 30000), ("synchronous", 1), ("foreign_keys", 1) })
        {
            command.CommandText = $"PRAGMA {pragma};";
            Assert.Equal(expected, Convert.ToInt32(await command.ExecuteScalarAsync()));
        }
    }

    [Fact]
    public async Task Backup_IncludesCommittedWalRows_AndCanBeRestored()
    {
        await ActivityMonitorDbContext.InitializeDatabaseAsync();
        var saved = await Activities.AddAsync(Activity(1));
        var backup = Path.Combine(_root, "snapshot.db");
        await DatabaseBackupService.BackupAsync(ActivityMonitorPaths.DatabasePath, backup);
        var options = new DbContextOptionsBuilder<ActivityMonitorDbContext>().UseSqlite($"Data Source={backup}").Options;
        await using var restored = new ActivityMonitorDbContext(options);
        Assert.Equal(saved.ProcessName, (await restored.ActivityEvents.SingleAsync()).ProcessName);
        await Assert.ThrowsAsync<IOException>(() => DatabaseBackupService.BackupAsync(ActivityMonitorPaths.DatabasePath, backup));
    }

    [Fact]
    public void PlaybackTime_DoesNotWrapAfterOneHour()
    {
        Assert.Equal("01:02:03", ActivityTimelineMath.FormatPlaybackTime(new TimeSpan(1, 2, 3)));
        Assert.Equal("02:03", ActivityTimelineMath.FormatPlaybackTime(new TimeSpan(0, 2, 3)));
    }

    [Fact]
    public void FileLogger_DrainsQueuedDebugAndInformationOnDispose()
    {
        var provider = new ActivityMonitor.Infrastructure.Logging.ActivityFileLoggerProvider("test");
        var logger = provider.CreateLogger("regression");
        for (var index = 0; index < 100; index++)
            Microsoft.Extensions.Logging.LoggerExtensions.LogInformation(logger, "Message {Index}", index);
        Microsoft.Extensions.Logging.LoggerExtensions.LogDebug(logger, "debug-is-enabled");
        provider.Dispose();
        var content = File.ReadAllText(ActivityMonitorPaths.GetLogFilePath("test", DateTime.UtcNow));
        Assert.Contains("Message 99", content);
        Assert.Contains("debug-is-enabled", content);
        Assert.Equal(0, provider.WriteFailures);
        Assert.Equal(0, provider.DroppedMessages);
    }

    [Fact]
    public void ShutdownSignal_AllowsOwnerAndAdministrators_AndIsTemporary()
    {
        var name = "Local\\ActivityMonitor_Test_" + Guid.NewGuid().ToString("N");
        using (var signal = AgentShutdownSignal.Create(name))
        using (var opened = EventWaitHandle.OpenExisting(name))
        {
            opened.Set();
            Assert.True(signal.WaitOne(TimeSpan.FromSeconds(1)));
            var rules = signal.GetAccessControl().GetAccessRules(true, true, typeof(System.Security.Principal.SecurityIdentifier));
            Assert.Contains(rules.Cast<System.Security.AccessControl.EventWaitHandleAccessRule>(),
                r => r.IdentityReference.Value == "S-1-5-32-544" && r.AccessControlType == System.Security.AccessControl.AccessControlType.Allow);
            Assert.DoesNotContain(rules.Cast<System.Security.AccessControl.EventWaitHandleAccessRule>(),
                r => r.IdentityReference.Value is "S-1-1-0" or "S-1-5-32-545");
        }
        Assert.False(EventWaitHandle.TryOpenExisting(name, out _));
    }

    [Fact]
    public void Paths_RejectTraversalComponents()
    {
        Assert.Throws<ArgumentException>(() => ActivityMonitorPaths.GetRecordingFilePath("..", Start));
        Assert.Throws<ArgumentException>(() => ActivityMonitorPaths.GetRecordingFilePath("PC", Start, @"..\outside.mp4"));
        Assert.Throws<ArgumentException>(() => ActivityMonitorPaths.GetReportFilePath("PC", Start, @"..\outside"));
    }

    private static ActivityEvent Activity(int session) => new() { MachineId = "PC", SessionId = session, UserIdentifier = "User",
        ProcessName = "Editor", StartedAtUtc = Start.AddMinutes(1), EndedAtUtc = Start.AddMinutes(2), DurationSeconds = 60 };
    private static RecordingSegment Segment(int session) => new() { MachineId = "PC", SessionId = session, UserIdentifier = "User",
        StartedAtUtc = Start, EndedAtUtc = Start.AddMinutes(10), DurationSeconds = 600, Status = RecordingStatus.Completed,
        FileName = "video.mp4", FilePath = Path.Combine(ActivityMonitorPaths.RecordingsDirectory, $"session{session}.mp4") };

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        ActivityMonitorPaths.SetCustomRootDirectory(null);
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }
    private sealed class Factory : IDbContextFactory<ActivityMonitorDbContext> { public ActivityMonitorDbContext CreateDbContext() => new(); }
    private sealed class Monitor<T>(T value) : IOptionsMonitor<T> {
        public T CurrentValue => value; public T Get(string? name) => value;
        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }
}

public class TestInterfaceProxy : DispatchProxy
{
    public Func<MethodInfo, object?[]?, object?> Handler { get; set; } = null!;
    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => Handler(targetMethod!, args);
}
