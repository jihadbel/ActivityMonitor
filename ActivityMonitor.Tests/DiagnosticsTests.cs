using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ActivityMonitor.Core.Options;
using ActivityMonitor.Infrastructure.Data;
using ActivityMonitor.Viewer.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;
using Xunit.Abstractions;

namespace ActivityMonitor.Tests;

[Collection(ActivityMonitorPathsCollection.Name)]
public class DiagnosticsTests
{
    private readonly ITestOutputHelper _output;

    public DiagnosticsTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact(Skip = "Manual diagnostic: reads the installed ProgramData database.")]
    public async Task DumpActualDatabaseInfo()
    {
        var dbPath = ActivityMonitorPaths.DatabasePath;
        _output.WriteLine($"Database Path: {dbPath}");
        _output.WriteLine($"Database Exists: {File.Exists(dbPath)}");

        if (!File.Exists(dbPath)) return;

        var options = new DbContextOptionsBuilder<ActivityMonitorDbContext>()
            .UseSqlite($"Data Source={dbPath}")
            .Options;

        using var context = new ActivityMonitorDbContext(options);

        var eventCount = await context.ActivityEvents.CountAsync();
        var segmentCount = await context.RecordingSegments.CountAsync();

        _output.WriteLine($"Total ActivityEvents: {eventCount}");
        _output.WriteLine($"Total RecordingSegments: {segmentCount}");

        var events = await context.ActivityEvents
            .OrderByDescending(e => e.Id)
            .Take(15)
            .ToListAsync();

        foreach (var ev in events)
        {
            _output.WriteLine($"Event #{ev.Id} | Started: {ev.StartedAtUtc:HH:mm:ss} | End: {ev.EndedAtUtc:HH:mm:ss} | Dur: {ev.DurationSeconds:F1}s | Type: {ev.EventType} | Proc: {ev.ProcessName} | Title: '{ev.WindowTitle}' | Context: '{ev.ContextPath}' | SegId: {ev.RecordingSegmentId}");
        }

        var segments = await context.RecordingSegments
            .OrderByDescending(s => s.Id)
            .Take(10)
            .ToListAsync();

        foreach (var seg in segments)
        {
            _output.WriteLine($"Segment #{seg.Id} | Start: {seg.StartedAtUtc:HH:mm:ss} | End: {seg.EndedAtUtc:HH:mm:ss} | Dur: {seg.DurationSeconds:F1}s | Size: {seg.FileSizeBytes} bytes | Status: {seg.Status} | Path: {seg.FilePath}");
        }
    }

    [Fact(Skip = "Manual diagnostic: enumerates physical recording devices.")]
    public void DumpScreenRecorderLibDevices()
    {
        try
        {
            var captureDevices = ScreenRecorderLib.Recorder.GetSystemAudioCaptureDevices();
            _output.WriteLine($"Capture Devices Count: {captureDevices?.Count ?? 0}");
            if (captureDevices != null)
            {
                for (int i = 0; i < captureDevices.Count; i++)
                {
                    var d = captureDevices[i];
                    _output.WriteLine($"Capture #{i}: DeviceName='{d.DeviceName}', FriendlyName='{d.FriendlyName}'");
                }
            }

            var loopbackDevices = ScreenRecorderLib.Recorder.GetSystemAudioLoopbackDevices();
            _output.WriteLine($"Loopback Devices Count: {loopbackDevices?.Count ?? 0}");
            if (loopbackDevices != null)
            {
                for (int i = 0; i < loopbackDevices.Count; i++)
                {
                    var d = loopbackDevices[i];
                    _output.WriteLine($"Loopback #{i}: DeviceName='{d.DeviceName}', FriendlyName='{d.FriendlyName}'");
                }
            }
        }
        catch (Exception ex)
        {
            _output.WriteLine($"ScreenRecorderLib Audio dump error: {ex}");
        }
    }

    [Fact(Skip = "Manual diagnostic: opens a live microphone session.")]
    public void TestAudioMeterServiceDirectly()
    {
        var devices = AudioMeterService.GetCaptureDevices();
        _output.WriteLine($"Total Discovered Devices in AudioMeterService: {devices.Count}");
        foreach (var dev in devices)
        {
            _output.WriteLine($"Device: Id='{dev.Id}', Name='{dev.DisplayName}'");
        }

        // Test creating a live audio session
        using var session = AudioMeterService.CreateLiveSession(null);
        Assert.NotNull(session);

        for (int i = 0; i < 3; i++)
        {
            float peak = session.GetPeakLevel();
            _output.WriteLine($"Sample #{i}: Peak={peak:F4} ({peak * 100:F1}%)");
            System.Threading.Thread.Sleep(50);
        }
    }
}
