using System.Reflection;
using System.Text.Json;
using ActivityMonitor.Agent.Tracking;
using ActivityMonitor.Agent.Recording;
using ActivityMonitor.Core.Entities;
using ActivityMonitor.Core.Enums;
using ActivityMonitor.Core.Interfaces;
using ActivityMonitor.Core.Models;
using ActivityMonitor.Core.Options;
using ActivityMonitor.Infrastructure.ContextProviders;
using ActivityMonitor.Infrastructure.Data;
using ActivityMonitor.Infrastructure.Repositories;
using ActivityMonitor.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

// Isolated diagnostic probes: no screen capture, service changes, or production data access.
var root = Path.Combine(AppContext.BaseDirectory, "isolated-data", Guid.NewGuid().ToString("N"));
ActivityMonitorPaths.SetCustomRootDirectory(root);
await ActivityMonitorDbContext.InitializeDatabaseAsync();
var factory = new Factory();
var activities = new ActivityRepository(factory, NullLogger<ActivityRepository>.Instance);
var recordings = new RecordingRepository(factory, NullLogger<RecordingRepository>.Instance);
var locator = new RecordingLocatorService(recordings, activities, NullLogger<RecordingLocatorService>.Instance);
var results = new List<object>();
var t = new DateTime(2026, 10, 10, 10, 0, 0, DateTimeKind.Utc);

var foreignSession = await recordings.AddAsync(new RecordingSegment {
    MachineId = "PROBE", SessionId = 2, StartedAtUtc = t, EndedAtUtc = t.AddMinutes(10),
    DurationSeconds = 600, Status = RecordingStatus.Completed,
    FileName = "session2.mp4", FilePath = Path.Combine(ActivityMonitorPaths.RecordingsDirectory, "session2.mp4")
});
var matches = await locator.LocateAsync(new ActivityEvent {
    MachineId = "PROBE", SessionId = 1, StartedAtUtc = t.AddMinutes(1),
    EndedAtUtc = t.AddMinutes(2), DurationSeconds = 60
});
results.Add(new { Probe = "Cross-session recording lookup", ActivitySession = 1,
    ReturnedSession = matches.Count > 0 ? foreignSession.SessionId : (int?)null,
    IssueReproduced = matches.Any(x => x.RecordingSegmentId == foreignSession.Id) });

await recordings.AddAsync(new RecordingSegment {
    MachineId = "LIVE", SessionId = 1, StartedAtUtc = t, Status = RecordingStatus.InProgress,
    FileName = "live.mp4", FilePath = Path.Combine(ActivityMonitorPaths.RecordingsDirectory, "live.mp4")
});
var liveMatches = await locator.LocateByTimestampAsync(t.AddSeconds(30), machineId: "LIVE");
results.Add(new { Probe = "In-progress recording after first second", ReturnedParts = liveMatches.Count,
    IssueReproduced = liveMatches.Count == 0 });

var cutoff = t.AddDays(1);
await activities.AddAsync(new ActivityEvent {
    MachineId = "RETENTION", SessionId = 1, UserIdentifier = "probe", ProcessName = "probe",
    StartedAtUtc = cutoff.AddMinutes(-5), EndedAtUtc = cutoff.AddMinutes(5), DurationSeconds = 600
});
var purged = await activities.PurgeOlderThanAsync(cutoff);
results.Add(new { Probe = "Retention deletes activity overlapping retained period", PurgedRows = purged,
    IssueReproduced = purged == 1 });

var addAttempts = 0;
var updateIds = new List<long>();
var flaky = DispatchProxy.Create<IActivityRepository, InterfaceProxy>();
((InterfaceProxy)(object)flaky).Handler = (method, args) => {
    if (method.Name == "AddAsync") {
        if (++addAttempts == 1) return Task.FromException<ActivityEvent>(new IOException("Simulated temporary persistence failure"));
        var activity = (ActivityEvent)args![0]!; activity.Id = 42; return Task.FromResult(activity);
    }
    if (method.Name == "UpdateAsync") { updateIds.Add(((ActivityEvent)args![0]!).Id); return Task.CompletedTask; }
    throw new NotSupportedException(method.Name);
};
var recordingStub = DispatchProxy.Create<IScreenRecordingService, InterfaceProxy>();
((InterfaceProxy)(object)recordingStub).Handler = (method, args) => method.Name switch {
    "get_CurrentSegment" => new RecordingSegment { Id = 1, StartedAtUtc = t },
    "get_IsRecording" => true,
    _ => throw new NotSupportedException(method.Name)
};
using (var tracker = new ActivityTracker(flaky, recordingStub, new ActivityContextProviderComposite([]),
    Options.Create(new ActivityTrackingOptions { WindowDebounceMs = 0 }),
    new Monitor<RecordingOptions>(new()), NullLogger<ActivityTracker>.Instance)) {
    var trackerType = typeof(ActivityTracker);
    var snapshotType = trackerType.GetNestedType("WindowSnapshot", BindingFlags.NonPublic)!;
    var snapshot = Activator.CreateInstance(snapshotType, [IntPtr.Zero, 0, "probe", TimeSpan.Zero]);
    var handle = trackerType.GetMethod("HandleActiveWindowStateAsync", BindingFlags.NonPublic | BindingFlags.Instance)!;
    try { await (Task)handle.Invoke(tracker, [snapshot])!; } catch (IOException) { }
    trackerType.GetField("_lastFlushTime", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(tracker, DateTime.UtcNow.AddMinutes(-1));
    await (Task)handle.Invoke(tracker, [snapshot])!;
    var current = (ActivityEvent?)trackerType.GetField("_currentActivity", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(tracker);
    results.Add(new { Probe = "Tracking after temporary insert failure", AddAttempts = addAttempts,
        UpdateIds = updateIds, CurrentActivityId = current?.Id,
        IssueReproduced = addAttempts == 1 && current?.Id == 0 && updateIds.Contains(0) });
}

// Prove that the startup recovery helper selects a segment belonging to another session.
var active = await recordings.GetActiveSegmentAsync("LIVE");
results.Add(new { Probe = "Active segment lookup has no session isolation", SelectedSession = active?.SessionId,
    ApiHasSessionArgument = typeof(IRecordingRepository).GetMethod("GetActiveSegmentAsync")!.GetParameters().Any(p => p.Name == "sessionId") });

Directory.CreateDirectory(ActivityMonitorPaths.RecordingsDirectory);
var orphanPath = Path.Combine(ActivityMonitorPaths.RecordingsDirectory, "other-session-active.mp4");
await File.WriteAllBytesAsync(orphanPath, new byte[4096]);
var otherSession = await recordings.AddAsync(new RecordingSegment {
    MachineId = "ORPHAN", SessionId = 9999, StartedAtUtc = DateTime.UtcNow,
    Status = RecordingStatus.InProgress, FileName = Path.GetFileName(orphanPath), FilePath = orphanPath
});
var integrity = new DataIntegrityService(factory, new Monitor<RecordingOptions>(new()), NullLogger<DataIntegrityService>.Instance);
await integrity.AuditAndRepairAsync();
var recoveredOther = await recordings.GetByIdAsync(otherSession.Id);
results.Add(new { Probe = "Recovery finalizes another session and accepts non-video bytes",
    Before = "InProgress", After = recoveredOther!.Status.ToString(), BytesAreValidVideo = false,
    IssueReproduced = recoveredOther.Status == RecordingStatus.Completed });

var starvationStart = DateTime.UtcNow.AddHours(-1);
await activities.AddRangeAsync(Enumerable.Range(0, 500).Select(i => new ActivityEvent {
    MachineId = "STARVE", SessionId = 1, UserIdentifier = "probe", ProcessName = "System",
    EventType = ActivityEventType.SessionLock, StartedAtUtc = starvationStart.AddSeconds(i),
    EndedAtUtc = starvationStart.AddSeconds(i)
}));
var linkableStart = starvationStart.AddMinutes(20);
var linkable = await activities.AddAsync(new ActivityEvent {
    MachineId = "STARVE", SessionId = 1, UserIdentifier = "probe", ProcessName = "probe",
    StartedAtUtc = linkableStart, EndedAtUtc = linkableStart.AddMinutes(1), DurationSeconds = 60
});
await recordings.AddAsync(new RecordingSegment {
    MachineId = "STARVE", SessionId = 1, StartedAtUtc = linkableStart,
    EndedAtUtc = linkableStart.AddMinutes(2), DurationSeconds = 120, Status = RecordingStatus.Completed,
    FileName = "starve.mp4", FilePath = Path.Combine(ActivityMonitorPaths.RecordingsDirectory, "starve.mp4")
});
var linkedCount = await locator.LinkUnlinkedActivitiesAsync(DateTime.UtcNow);
var linkableAfter = await activities.GetByIdAsync(linkable.Id);
results.Add(new { Probe = "Unlinkable first 500 events starve later linkable events", LinkedCount = linkedCount,
    LaterActivityHasVideo = (await locator.LocateAsync(linkable)).Count > 0,
    LaterActivityLinked = linkableAfter!.RecordingSegmentId.HasValue,
    IssueReproduced = !linkableAfter.RecordingSegmentId.HasValue && linkedCount == 0 });

var json = JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true });
Console.WriteLine(json);
await File.WriteAllTextAsync(Path.Combine(Directory.GetCurrentDirectory(), "probe-results.after-fixes.json"), json);
ActivityMonitorPaths.SetCustomRootDirectory(null);

sealed class Factory : IDbContextFactory<ActivityMonitorDbContext> {
    public ActivityMonitorDbContext CreateDbContext() => new();
}
sealed class Monitor<T>(T value) : IOptionsMonitor<T> {
    public T CurrentValue => value;
    public T Get(string? name) => value;
    public IDisposable? OnChange(Action<T, string?> listener) => null;
}
public class InterfaceProxy : DispatchProxy {
    public Func<MethodInfo, object?[]?, object?> Handler { get; set; } = null!;
    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => Handler(targetMethod!, args);
}
