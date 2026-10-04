using System;
using System.IO;
using ActivityMonitor.Core.Options;
using Xunit;

namespace ActivityMonitor.Tests;

public class ActivityMonitorPathsTests
{
    [Fact]
    public void GetRecordingFilePath_ShouldFollowSpecificationStructure()
    {
        // %ProgramData%\ActivityMonitor\Recordings\MachineId\YYYY\MM\DD\HH\YYYY-MM-DD_HH-mm-ss.mp4
        var time = new DateTime(2026, 10, 4, 14, 20, 0, DateTimeKind.Utc);
        var machineId = "TEST-PC-01";

        var path = ActivityMonitorPaths.GetRecordingFilePath(machineId, time);

        Assert.Contains(machineId, path);
        Assert.Contains("2026", path);
        Assert.Contains("10", path);
        Assert.Contains("04", path);
        Assert.Contains("14", path);
        Assert.EndsWith("2026-10-04_14-20-00.mp4", path);
    }

    [Fact]
    public void GetReportFilePath_ShouldFollowSpecificationStructure()
    {
        // %ProgramData%\ActivityMonitor\Reports\MachineId\YYYY\MM\YYYY-MM-DD.xlsx
        var date = new DateTime(2026, 10, 4);
        var machineId = "TEST-PC-01";

        var path = ActivityMonitorPaths.GetReportFilePath(machineId, date, "xlsx");

        Assert.Contains(machineId, path);
        Assert.Contains("2026", path);
        Assert.Contains("10", path);
        Assert.EndsWith("2026-10-04.xlsx", path);
    }

    [Fact]
    public void GetLogFilePath_ShouldFormatCorrectly()
    {
        var date = new DateTime(2026, 10, 4);
        var path = ActivityMonitorPaths.GetLogFilePath("agent", date);

        Assert.EndsWith("agent-2026-10-04.log", path);
    }
}
