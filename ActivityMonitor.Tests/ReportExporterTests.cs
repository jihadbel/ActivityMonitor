using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using ActivityMonitor.Core.Entities;
using ActivityMonitor.Core.Enums;
using ActivityMonitor.Core.Interfaces;
using ActivityMonitor.Core.Models;
using ActivityMonitor.Core.Options;
using ActivityMonitor.Reporting.Exporters;
using ClosedXML.Excel;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ActivityMonitor.Tests;

[Collection(ActivityMonitorPathsCollection.Name)]
public class ReportExporterTests
{
    private class TestActivityRepository : IActivityRepository
    {
        private readonly string _processName;

        public TestActivityRepository(string processName = "devenv")
        {
            _processName = processName;
        }

        public Task<ActivityEvent> AddAsync(ActivityEvent activityEvent, CancellationToken ct = default) => Task.FromResult(activityEvent);
        public Task AddRangeAsync(IEnumerable<ActivityEvent> activityEvents, CancellationToken ct = default) => Task.CompletedTask;
        public Task UpdateAsync(ActivityEvent activityEvent, CancellationToken ct = default) => Task.CompletedTask;
        public Task<bool> SetRecordingSegmentIfMissingAsync(long activityEventId, long recordingSegmentId, CancellationToken ct = default) => Task.FromResult(true);
        public Task<ActivityEvent?> GetByIdAsync(long id, CancellationToken ct = default) => Task.FromResult<ActivityEvent?>(null);

        public Task<List<ActivityEvent>> GetByDateRangeAsync(DateTime startUtc, DateTime endUtc, string? machineId = null, CancellationToken ct = default)
        {
            var list = new List<ActivityEvent>
            {
                new ActivityEvent
                {
                    Id = 1,
                    MachineId = "PC-01",
                    UserIdentifier = "TestUser",
                    ProcessName = _processName,
                    WindowTitle = "ActivityMonitor - Solution Explorer",
                    ContextPath = @"C:\Users\Masoud\source\repos\ActivityMonitor",
                    ProcessPath = @"C:\Program Files\Microsoft Visual Studio\devenv.exe",
                    EventType = ActivityEventType.ActiveWindow,
                    StartedAtUtc = startUtc.AddHours(9),
                    EndedAtUtc = startUtc.AddHours(9).AddMinutes(30),
                    DurationSeconds = 1800
                }
            };
            return Task.FromResult(list);
        }

        public Task<List<ActivityEvent>> GetUnlinkedActivitiesAsync(DateTime beforeUtc, CancellationToken ct = default, long afterId = 0) => Task.FromResult(new List<ActivityEvent>());

        public Task<DailyActivitySummary> GetDailySummaryAsync(DateTime dateUtc, string? machineId = null, CancellationToken ct = default)
        {
            return Task.FromResult(new DailyActivitySummary
            {
                Date = dateUtc.Date,
                MachineId = machineId ?? "PC-01",
                UserIdentifier = "TestUser",
                TotalActiveTime = TimeSpan.FromHours(5),
                TotalIdleTime = TimeSpan.FromMinutes(25),
                FirstActivityUtc = dateUtc.Date.AddHours(9),
                LastActivityUtc = dateUtc.Date.AddHours(17),
                UniqueApplicationsCount = 4,
                TotalEventsCount = 120,
                RecordingSegmentsCount = 24,
                TotalRecordingSizeBytes = 500 * 1024 * 1024
            });
        }

        public Task<List<ApplicationUsageSummary>> GetTopApplicationsAsync(DateTime dateUtc, string? machineId = null, CancellationToken ct = default)
        {
            return Task.FromResult(new List<ApplicationUsageSummary>
            {
                new() { ApplicationName = "devenv", TotalDuration = TimeSpan.FromHours(4), UsageCount = 80, Percentage = 80.0 },
                new() { ApplicationName = "explorer", TotalDuration = TimeSpan.FromHours(1), UsageCount = 40, Percentage = 20.0 }
            });
        }

        public Task<int> PurgeOlderThanAsync(DateTime cutoffUtc, CancellationToken ct = default) => Task.FromResult(0);
    }

    private class TestRecordingRepository : IRecordingRepository
    {
        private readonly string? _spanningRoot;

        public TestRecordingRepository(string? spanningRoot = null)
        {
            _spanningRoot = spanningRoot;
        }

        public Task<RecordingSegment> AddAsync(RecordingSegment segment, CancellationToken ct = default) => Task.FromResult(segment);
        public Task UpdateAsync(RecordingSegment segment, CancellationToken ct = default) => Task.CompletedTask;
        public Task<RecordingSegment?> GetByIdAsync(long id, CancellationToken ct = default) => Task.FromResult<RecordingSegment?>(null);
        public Task<RecordingSegment?> GetActiveSegmentAsync(string machineId, CancellationToken ct = default, int? sessionId = null) => Task.FromResult<RecordingSegment?>(null);

        public Task<List<RecordingSegment>> GetByDateRangeAsync(DateTime startUtc, DateTime endUtc, string? machineId = null, CancellationToken ct = default)
        {
            if (_spanningRoot != null)
            {
                return Task.FromResult(new List<RecordingSegment>
                {
                    new()
                    {
                        Id = 1, MachineId = "PC-01", FileName = "part-1.mp4",
                        FilePath = Path.Combine(_spanningRoot, "part-1.mp4"),
                        StartedAtUtc = startUtc.AddHours(8).AddMinutes(52),
                        EndedAtUtc = startUtc.AddHours(9).AddMinutes(10),
                        DurationSeconds = 1080, Status = RecordingStatus.Completed
                    },
                    new()
                    {
                        Id = 2, MachineId = "PC-01", FileName = "part-2.mp4",
                        FilePath = Path.Combine(_spanningRoot, "part-2.mp4"),
                        StartedAtUtc = startUtc.AddHours(9).AddMinutes(10),
                        EndedAtUtc = startUtc.AddHours(9).AddMinutes(30),
                        DurationSeconds = 1200, Status = RecordingStatus.Completed
                    }
                });
            }

            return Task.FromResult(new List<RecordingSegment>
            {
                new()
                {
                    Id = 1,
                    MachineId = "PC-01",
                    FileName = "2026-10-04_09-00-00.mp4",
                    FilePath = @"C:\Fake\2026-10-04_09-00-00.mp4",
                    StartedAtUtc = startUtc.AddHours(9),
                    EndedAtUtc = startUtc.AddHours(9).AddMinutes(10),
                    DurationSeconds = 600,
                    FileSizeBytes = 15 * 1024 * 1024,
                    Status = RecordingStatus.Completed
                }
            });
        }

        public Task<List<RecordingSegment>> GetSegmentsCoveringTimeRangeAsync(DateTime startUtc, DateTime endUtc, string? machineId = null, CancellationToken ct = default, int? sessionId = null) => Task.FromResult(new List<RecordingSegment>());
        public Task<List<RecordingSegment>> GetSegmentsOlderThanAsync(DateTime cutoffUtc, CancellationToken ct = default) => Task.FromResult(new List<RecordingSegment>());
        public Task<List<RecordingSegment>> GetAllCompletedSegmentsAsync(CancellationToken ct = default) => Task.FromResult(new List<RecordingSegment>());
        public Task<int> MarkAsDeletedAsync(long id, CancellationToken ct = default) => Task.FromResult(1);
    }

    [Fact]
    public async Task ExportDailyExcelReportAsync_ShouldCreateWorkbookWithAllRequiredSheets()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "ActivityMonitor_Test_" + Guid.NewGuid().ToString("N"));
        ActivityMonitorPaths.SetCustomRootDirectory(tempDir);

        try
        {
            var exporter = new ActivityReportExporter(
                new TestActivityRepository(),
                new TestRecordingRepository(),
                NullLogger<ActivityReportExporter>.Instance);

            var date = new DateTime(2026, 10, 4);
            var excelPath = await exporter.ExportDailyExcelReportAsync(date, "PC-01");

            Assert.True(File.Exists(excelPath));

            using var wb = new XLWorkbook(excelPath);
            Assert.Contains(wb.Worksheets, s => s.Name == "Summary");
            Assert.Contains(wb.Worksheets, s => s.Name == "Timeline");
            Assert.Contains(wb.Worksheets, s => s.Name == "Applications");
            Assert.Contains(wb.Worksheets, s => s.Name == "Recordings");
        }
        finally
        {
            ActivityMonitorPaths.SetCustomRootDirectory(null);
            if (Directory.Exists(tempDir))
            {
                try { Directory.Delete(tempDir, true); } catch { }
            }
        }
    }

    [Fact]
    public async Task ExportDailyCsvAndTxtReportAsync_ShouldGenerateFilesWithCorrectHeaders()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "ActivityMonitor_Test_" + Guid.NewGuid().ToString("N"));
        ActivityMonitorPaths.SetCustomRootDirectory(tempDir);

        try
        {
            var exporter = new ActivityReportExporter(
                new TestActivityRepository(),
                new TestRecordingRepository(),
                NullLogger<ActivityReportExporter>.Instance);

            var date = new DateTime(2026, 10, 4);
            var csvPath = await exporter.ExportDailyCsvReportAsync(date, "PC-01");
            var txtPath = await exporter.ExportDailySummaryTextAsync(date, "PC-01");

            Assert.True(File.Exists(csvPath));
            Assert.True(File.Exists(txtPath));

            var csvContent = await File.ReadAllTextAsync(csvPath);
            Assert.Contains("TimestampUtc,EndTimeUtc,Duration,Application,WindowTitle", csvContent);
            Assert.Contains("devenv", csvContent);
            Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(csvPath)!, "*.tmp"));

            var txtContent = await File.ReadAllTextAsync(txtPath);
            Assert.Contains("ACTIVITYMONITOR DAILY REPORT", txtContent);
            Assert.Contains("Total Active Time:", txtContent);
        }
        finally
        {
            ActivityMonitorPaths.SetCustomRootDirectory(null);
            if (Directory.Exists(tempDir))
            {
                try { Directory.Delete(tempDir, true); } catch { }
            }
        }
    }

    [Fact]
    public async Task ExportDailyCsvReportAsync_ShouldNeutralizeSpreadsheetFormulas()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "ActivityMonitor_Test_" + Guid.NewGuid().ToString("N"));
        ActivityMonitorPaths.SetCustomRootDirectory(tempDir);

        try
        {
            var exporter = new ActivityReportExporter(
                new TestActivityRepository("=2+2"),
                new TestRecordingRepository(),
                NullLogger<ActivityReportExporter>.Instance);

            var csvPath = await exporter.ExportDailyCsvReportAsync(new DateTime(2026, 10, 4), "PC-01");
            var csvContent = await File.ReadAllTextAsync(csvPath);

            Assert.Contains(",'=2+2,", csvContent);
            Assert.DoesNotContain(",=2+2,", csvContent);
        }
        finally
        {
            ActivityMonitorPaths.SetCustomRootDirectory(null);
            if (Directory.Exists(tempDir))
            {
                try { Directory.Delete(tempDir, true); } catch { }
            }
        }
    }

    [Fact]
    public async Task ExportDailyCsvReportAsync_ShouldIncludeEveryRecordingPartForSpanningActivity()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "ActivityMonitor_Test_" + Guid.NewGuid().ToString("N"));
        ActivityMonitorPaths.SetCustomRootDirectory(tempDir);

        try
        {
            var exporter = new ActivityReportExporter(
                new TestActivityRepository(),
                new TestRecordingRepository(tempDir),
                NullLogger<ActivityReportExporter>.Instance);

            var csvPath = await exporter.ExportDailyCsvReportAsync(new DateTime(2026, 10, 4), "PC-01");
            var csvContent = await File.ReadAllTextAsync(csvPath);

            Assert.Contains("part-1.mp4 | part-2.mp4", csvContent);
            Assert.Contains("00:08:00 | 00:00:00", csvContent);
        }
        finally
        {
            ActivityMonitorPaths.SetCustomRootDirectory(null);
            if (Directory.Exists(tempDir))
            {
                try { Directory.Delete(tempDir, true); } catch { }
            }
        }
    }
}
