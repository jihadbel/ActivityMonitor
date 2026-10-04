using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ActivityMonitor.Core.Entities;
using ActivityMonitor.Core.Enums;
using ActivityMonitor.Core.Interfaces;
using ActivityMonitor.Core.Models;
using ActivityMonitor.Core.Options;
using ClosedXML.Excel;
using Microsoft.Extensions.Logging;

namespace ActivityMonitor.Reporting.Exporters;

public class ActivityReportExporter : IActivityReportExporter
{
    private readonly IActivityRepository _activityRepository;
    private readonly IRecordingRepository _recordingRepository;
    private readonly IRecordingLocatorService _locatorService;
    private readonly ILogger<ActivityReportExporter> _logger;

    public ActivityReportExporter(
        IActivityRepository activityRepository,
        IRecordingRepository recordingRepository,
        IRecordingLocatorService locatorService,
        ILogger<ActivityReportExporter> logger)
    {
        _activityRepository = activityRepository;
        _recordingRepository = recordingRepository;
        _locatorService = locatorService;
        _logger = logger;
    }

    public async Task<string> ExportDailyExcelReportAsync(DateTime dateUtc, string? machineId = null, CancellationToken ct = default)
    {
        var targetDate = dateUtc.Date;
        var startUtc = targetDate;
        var endUtc = startUtc.AddDays(1).AddTicks(-1);

        var machine = machineId ?? Environment.MachineName;
        var summary = await _activityRepository.GetDailySummaryAsync(targetDate, machine, ct);
        var activities = await _activityRepository.GetByDateRangeAsync(startUtc, endUtc, machine, ct);
        var recordings = await _recordingRepository.GetByDateRangeAsync(startUtc, endUtc, machine, ct);
        var topApps = await _activityRepository.GetTopApplicationsAsync(targetDate, machine, ct);

        var filePath = ActivityMonitorPaths.GetReportFilePath(machine, targetDate, "xlsx");

        using var workbook = new XLWorkbook();

        // ----------------- Sheet 1: Summary -----------------
        var wsSummary = workbook.Worksheets.Add("Summary");
        wsSummary.ShowGridLines = true;

        wsSummary.Cell(1, 1).Value = "ActivityMonitor - Daily Executive Summary";
        wsSummary.Range(1, 1, 1, 4).Merge();
        wsSummary.Cell(1, 1).Style.Font.Bold = true;
        wsSummary.Cell(1, 1).Style.Font.FontSize = 16;
        wsSummary.Cell(1, 1).Style.Font.FontColor = XLColor.White;
        wsSummary.Range(1, 1, 1, 4).Style.Fill.BackgroundColor = XLColor.FromHtml("#1E3A8A"); // Navy blue
        wsSummary.Row(1).Height = 35;
        wsSummary.Cell(1, 1).Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;

        var summaryItems = new (string Key, object Value)[]
        {
            ("Date", targetDate.ToString("yyyy-MM-dd")),
            ("Machine ID", summary.MachineId),
            ("User Identifier", summary.UserIdentifier),
            ("Total Active Time", FormatDuration(summary.TotalActiveTime)),
            ("Total Idle Time", FormatDuration(summary.TotalIdleTime)),
            ("First Activity (UTC)", summary.FirstActivityUtc?.ToString("HH:mm:ss") ?? "N/A"),
            ("Last Activity (UTC)", summary.LastActivityUtc?.ToString("HH:mm:ss") ?? "N/A"),
            ("Unique Applications", summary.UniqueApplicationsCount),
            ("Total Activity Events", summary.TotalEventsCount),
            ("Recording Segments", summary.RecordingSegmentsCount),
            ("Total Recordings Size", $"{summary.TotalRecordingSizeBytes / (1024.0 * 1024.0):F2} MB")
        };

        wsSummary.Cell(3, 1).Value = "Metric";
        wsSummary.Cell(3, 2).Value = "Value";
        ApplyHeaderStyle(wsSummary.Range(3, 1, 3, 2), "#2563EB");

        int summaryRow = 4;
        foreach (var (k, v) in summaryItems)
        {
            wsSummary.Cell(summaryRow, 1).Value = k;
            wsSummary.Cell(summaryRow, 1).Style.Font.Bold = true;
            wsSummary.Cell(summaryRow, 2).Value = v.ToString();

            if (summaryRow % 2 == 0)
            {
                wsSummary.Range(summaryRow, 1, summaryRow, 2).Style.Fill.BackgroundColor = XLColor.FromHtml("#F8FAFC");
            }
            summaryRow++;
        }
        wsSummary.Range(3, 1, summaryRow - 1, 2).Style.Border.SetOutsideBorder(XLBorderStyleValues.Thin);
        wsSummary.Range(3, 1, summaryRow - 1, 2).Style.Border.SetInsideBorder(XLBorderStyleValues.Thin);
        wsSummary.Columns(1, 4).AdjustToContents();

        // ----------------- Sheet 2: Timeline -----------------
        var wsTimeline = workbook.Worksheets.Add("Timeline");
        wsTimeline.ShowGridLines = true;

        var timelineHeaders = new[]
        {
            "Start Time (UTC)", "End Time (UTC)", "Duration", "Application",
            "Window Title", "Context / File Path", "Process Path",
            "Event Type", "Recording File", "Recording Offset", "Video Link"
        };

        for (int i = 0; i < timelineHeaders.Length; i++)
        {
            wsTimeline.Cell(1, i + 1).Value = timelineHeaders[i];
        }
        ApplyHeaderStyle(wsTimeline.Range(1, 1, 1, timelineHeaders.Length), "#1E3A8A");

        int row = 2;
        foreach (var act in activities)
        {
            var dur = TimeSpan.FromSeconds(act.DurationSeconds);
            wsTimeline.Cell(row, 1).Value = act.StartedAtUtc.ToString("HH:mm:ss");
            wsTimeline.Cell(row, 2).Value = act.EndedAtUtc?.ToString("HH:mm:ss") ?? act.StartedAtUtc.Add(dur).ToString("HH:mm:ss");
            wsTimeline.Cell(row, 3).Value = FormatDuration(dur);
            wsTimeline.Cell(row, 4).Value = act.ProcessName;
            wsTimeline.Cell(row, 5).Value = act.WindowTitle ?? string.Empty;
            wsTimeline.Cell(row, 6).Value = act.ContextPath ?? string.Empty;
            wsTimeline.Cell(row, 7).Value = act.ProcessPath ?? string.Empty;
            wsTimeline.Cell(row, 8).Value = act.EventType.ToString();

            // Recording link & offset
            if (act.RecordingSegment != null)
            {
                wsTimeline.Cell(row, 9).Value = act.RecordingSegment.FileName;
                var offset = act.StartedAtUtc - act.RecordingSegment.StartedAtUtc;
                if (offset < TimeSpan.Zero) offset = TimeSpan.Zero;
                wsTimeline.Cell(row, 10).Value = FormatDuration(offset);

                if (File.Exists(act.RecordingSegment.FilePath))
                {
                    var linkCell = wsTimeline.Cell(row, 11);
                    linkCell.Value = "Play Recording";
                    linkCell.Style.Font.Underline = XLFontUnderlineValues.Single;
                    linkCell.Style.Font.FontColor = XLColor.Blue;
                    linkCell.SetHyperlink(new XLHyperlink(new Uri(act.RecordingSegment.FilePath).AbsoluteUri));
                }
                else
                {
                    wsTimeline.Cell(row, 11).Value = "File Removed";
                    wsTimeline.Cell(row, 11).Style.Font.FontColor = XLColor.Gray;
                }
            }
            else
            {
                wsTimeline.Cell(row, 9).Value = "-";
                wsTimeline.Cell(row, 10).Value = "-";
                wsTimeline.Cell(row, 11).Value = "-";
            }

            if (row % 2 == 1)
            {
                wsTimeline.Range(row, 1, row, timelineHeaders.Length).Style.Fill.BackgroundColor = XLColor.FromHtml("#F8FAFC");
            }
            row++;
        }

        if (activities.Count > 0)
        {
            wsTimeline.Range(1, 1, row - 1, timelineHeaders.Length).SetAutoFilter();
            wsTimeline.Range(1, 1, row - 1, timelineHeaders.Length).Style.Border.SetOutsideBorder(XLBorderStyleValues.Thin);
            wsTimeline.Range(1, 1, row - 1, timelineHeaders.Length).Style.Border.SetInsideBorder(XLBorderStyleValues.Thin);
        }
        wsTimeline.SheetView.FreezeRows(1);
        wsTimeline.Columns().AdjustToContents(1, 60);

        // ----------------- Sheet 3: Applications -----------------
        var wsApps = workbook.Worksheets.Add("Applications");
        wsApps.ShowGridLines = true;

        var appHeaders = new[] { "Application", "Total Duration", "Usage Count", "Percentage" };
        for (int i = 0; i < appHeaders.Length; i++)
        {
            wsApps.Cell(1, i + 1).Value = appHeaders[i];
        }
        ApplyHeaderStyle(wsApps.Range(1, 1, 1, appHeaders.Length), "#1E3A8A");

        int appRow = 2;
        foreach (var app in topApps)
        {
            wsApps.Cell(appRow, 1).Value = app.ApplicationName;
            wsApps.Cell(appRow, 2).Value = FormatDuration(app.TotalDuration);
            wsApps.Cell(appRow, 3).Value = app.UsageCount;
            wsApps.Cell(appRow, 4).Value = $"{app.Percentage:F2}%";

            if (appRow % 2 == 1)
            {
                wsApps.Range(appRow, 1, appRow, appHeaders.Length).Style.Fill.BackgroundColor = XLColor.FromHtml("#F8FAFC");
            }
            appRow++;
        }

        if (topApps.Count > 0)
        {
            wsApps.Range(1, 1, appRow - 1, appHeaders.Length).SetAutoFilter();
            wsApps.Range(1, 1, appRow - 1, appHeaders.Length).Style.Border.SetOutsideBorder(XLBorderStyleValues.Thin);
            wsApps.Range(1, 1, appRow - 1, appHeaders.Length).Style.Border.SetInsideBorder(XLBorderStyleValues.Thin);
        }
        wsApps.SheetView.FreezeRows(1);
        wsApps.Columns().AdjustToContents();

        // ----------------- Sheet 4: Recordings -----------------
        var wsRecordings = workbook.Worksheets.Add("Recordings");
        wsRecordings.ShowGridLines = true;

        var recHeaders = new[] { "Start Time (UTC)", "End Time (UTC)", "Duration", "File Name", "Size (MB)", "Status", "Full Path", "Open" };
        for (int i = 0; i < recHeaders.Length; i++)
        {
            wsRecordings.Cell(1, i + 1).Value = recHeaders[i];
        }
        ApplyHeaderStyle(wsRecordings.Range(1, 1, 1, recHeaders.Length), "#1E3A8A");

        int recRow = 2;
        foreach (var rec in recordings)
        {
            var recDur = TimeSpan.FromSeconds(rec.DurationSeconds);
            wsRecordings.Cell(recRow, 1).Value = rec.StartedAtUtc.ToString("HH:mm:ss");
            wsRecordings.Cell(recRow, 2).Value = rec.EndedAtUtc?.ToString("HH:mm:ss") ?? "-";
            wsRecordings.Cell(recRow, 3).Value = FormatDuration(recDur);
            wsRecordings.Cell(recRow, 4).Value = rec.FileName;
            wsRecordings.Cell(recRow, 5).Value = Math.Round(rec.FileSizeBytes / (1024.0 * 1024.0), 2);
            wsRecordings.Cell(recRow, 6).Value = rec.Status.ToString();
            wsRecordings.Cell(recRow, 7).Value = rec.FilePath;

            if (File.Exists(rec.FilePath))
            {
                var playCell = wsRecordings.Cell(recRow, 8);
                playCell.Value = "Open Video";
                playCell.Style.Font.Underline = XLFontUnderlineValues.Single;
                playCell.Style.Font.FontColor = XLColor.Blue;
                playCell.SetHyperlink(new XLHyperlink(new Uri(rec.FilePath).AbsoluteUri));
            }
            else
            {
                wsRecordings.Cell(recRow, 8).Value = "Missing";
                wsRecordings.Cell(recRow, 8).Style.Font.FontColor = XLColor.Gray;
            }

            if (recRow % 2 == 1)
            {
                wsRecordings.Range(recRow, 1, recRow, recHeaders.Length).Style.Fill.BackgroundColor = XLColor.FromHtml("#F8FAFC");
            }
            recRow++;
        }

        if (recordings.Count > 0)
        {
            wsRecordings.Range(1, 1, recRow - 1, recHeaders.Length).SetAutoFilter();
            wsRecordings.Range(1, 1, recRow - 1, recHeaders.Length).Style.Border.SetOutsideBorder(XLBorderStyleValues.Thin);
            wsRecordings.Range(1, 1, recRow - 1, recHeaders.Length).Style.Border.SetInsideBorder(XLBorderStyleValues.Thin);
        }
        wsRecordings.SheetView.FreezeRows(1);
        wsRecordings.Columns().AdjustToContents();

        workbook.SaveAs(filePath);
        _logger.LogInformation("Generated daily Excel report at {FilePath}", filePath);
        return filePath;
    }

    public async Task<string> ExportDailyCsvReportAsync(DateTime dateUtc, string? machineId = null, CancellationToken ct = default)
    {
        var targetDate = dateUtc.Date;
        var startUtc = targetDate;
        var endUtc = startUtc.AddDays(1).AddTicks(-1);

        var machine = machineId ?? Environment.MachineName;
        var activities = await _activityRepository.GetByDateRangeAsync(startUtc, endUtc, machine, ct);
        var filePath = ActivityMonitorPaths.GetReportFilePath(machine, targetDate, "csv");

        var sb = new StringBuilder();
        sb.AppendLine("TimestampUtc,EndTimeUtc,Duration,Application,WindowTitle,ContextPath,ProcessPath,EventType,RecordingFile,RecordingOffset");

        foreach (var act in activities)
        {
            var dur = TimeSpan.FromSeconds(act.DurationSeconds);
            var endStr = act.EndedAtUtc?.ToString("yyyy-MM-dd HH:mm:ss") ?? act.StartedAtUtc.Add(dur).ToString("yyyy-MM-dd HH:mm:ss");

            string recFile = "-";
            string recOffset = "-";

            if (act.RecordingSegment != null)
            {
                recFile = act.RecordingSegment.FileName;
                var offset = act.StartedAtUtc - act.RecordingSegment.StartedAtUtc;
                if (offset < TimeSpan.Zero) offset = TimeSpan.Zero;
                recOffset = FormatDuration(offset);
            }

            sb.Append(act.StartedAtUtc.ToString("yyyy-MM-dd HH:mm:ss")).Append(',');
            sb.Append(endStr).Append(',');
            sb.Append(EscapeCsv(FormatDuration(dur))).Append(',');
            sb.Append(EscapeCsv(act.ProcessName)).Append(',');
            sb.Append(EscapeCsv(act.WindowTitle ?? string.Empty)).Append(',');
            sb.Append(EscapeCsv(act.ContextPath ?? string.Empty)).Append(',');
            sb.Append(EscapeCsv(act.ProcessPath ?? string.Empty)).Append(',');
            sb.Append(EscapeCsv(act.EventType.ToString())).Append(',');
            sb.Append(EscapeCsv(recFile)).Append(',');
            sb.AppendLine(EscapeCsv(recOffset));
        }

        await File.WriteAllTextAsync(filePath, sb.ToString(), Encoding.UTF8, ct);
        _logger.LogInformation("Generated daily CSV report at {FilePath}", filePath);
        return filePath;
    }

    public async Task<string> ExportDailySummaryTextAsync(DateTime dateUtc, string? machineId = null, CancellationToken ct = default)
    {
        var targetDate = dateUtc.Date;
        var machine = machineId ?? Environment.MachineName;
        var summary = await _activityRepository.GetDailySummaryAsync(targetDate, machine, ct);
        var topApps = await _activityRepository.GetTopApplicationsAsync(targetDate, machine, ct);
        var recordings = await _recordingRepository.GetByDateRangeAsync(targetDate, targetDate.AddDays(1).AddTicks(-1), machine, ct);

        var filePath = ActivityMonitorPaths.GetReportFilePath(machine, targetDate, "txt");

        var sb = new StringBuilder();
        sb.AppendLine("================================================================================");
        sb.AppendLine($"           ACTIVITYMONITOR DAILY REPORT - {targetDate:yyyy-MM-dd}");
        sb.AppendLine("================================================================================");
        sb.AppendLine();
        sb.AppendLine("--- SUMMARY ---");
        sb.AppendLine($"Machine ID:              {summary.MachineId}");
        sb.AppendLine($"User Identifier:         {summary.UserIdentifier}");
        sb.AppendLine($"Total Active Time:       {FormatDuration(summary.TotalActiveTime)}");
        sb.AppendLine($"Total Idle Time:         {FormatDuration(summary.TotalIdleTime)}");
        sb.AppendLine($"First Activity (UTC):    {summary.FirstActivityUtc?.ToString("HH:mm:ss") ?? "N/A"}");
        sb.AppendLine($"Last Activity (UTC):     {summary.LastActivityUtc?.ToString("HH:mm:ss") ?? "N/A"}");
        sb.AppendLine($"Unique Applications:     {summary.UniqueApplicationsCount}");
        sb.AppendLine($"Total Events Recorded:   {summary.TotalEventsCount}");
        sb.AppendLine($"Recording Segments:      {summary.RecordingSegmentsCount}");
        sb.AppendLine($"Total Recordings Size:   {summary.TotalRecordingSizeBytes / (1024.0 * 1024.0):F2} MB");
        sb.AppendLine();

        sb.AppendLine("--- TOP APPLICATIONS ---");
        sb.AppendLine(string.Format("{0,-25} {1,-15} {2,-10} {3,-10}", "Application", "Duration", "Count", "Percentage"));
        sb.AppendLine(new string('-', 65));
        foreach (var app in topApps)
        {
            sb.AppendLine(string.Format("{0,-25} {1,-15} {2,-10} {3,-10:F2}%",
                Truncate(app.ApplicationName, 24),
                FormatDuration(app.TotalDuration),
                app.UsageCount,
                app.Percentage));
        }
        sb.AppendLine();

        sb.AppendLine("--- SCREEN RECORDING SEGMENTS ---");
        sb.AppendLine(string.Format("{0,-12} {1,-12} {2,-12} {3,-25} {4,-10}", "Start (UTC)", "End (UTC)", "Duration", "File Name", "Size (MB)"));
        sb.AppendLine(new string('-', 75));
        foreach (var rec in recordings)
        {
            var dur = TimeSpan.FromSeconds(rec.DurationSeconds);
            sb.AppendLine(string.Format("{0,-12} {1,-12} {2,-12} {3,-25} {4,-10:F2}",
                rec.StartedAtUtc.ToString("HH:mm:ss"),
                rec.EndedAtUtc?.ToString("HH:mm:ss") ?? "-",
                FormatDuration(dur),
                Truncate(rec.FileName, 24),
                rec.FileSizeBytes / (1024.0 * 1024.0)));
        }

        sb.AppendLine();
        sb.AppendLine("================================================================================");
        sb.AppendLine($"Generated on: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC");
        sb.AppendLine("================================================================================");

        await File.WriteAllTextAsync(filePath, sb.ToString(), Encoding.UTF8, ct);
        _logger.LogInformation("Generated daily TXT report at {FilePath}", filePath);
        return filePath;
    }

    private static void ApplyHeaderStyle(IXLRange range, string hexColor)
    {
        range.Style.Font.Bold = true;
        range.Style.Font.FontColor = XLColor.White;
        range.Style.Fill.BackgroundColor = XLColor.FromHtml(hexColor);
        range.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        range.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
    }

    private static string FormatDuration(TimeSpan ts)
    {
        return $"{(int)ts.TotalHours:D2}:{ts.Minutes:D2}:{ts.Seconds:D2}";
    }

    private static string EscapeCsv(string field)
    {
        if (field.Contains(',') || field.Contains('"') || field.Contains('\n') || field.Contains('\r'))
        {
            return $"\"{field.Replace("\"", "\"\"")}\"";
        }
        return field;
    }

    private static string Truncate(string val, int maxLen)
    {
        if (string.IsNullOrEmpty(val)) return string.Empty;
        return val.Length <= maxLen ? val : val.Substring(0, maxLen - 3) + "...";
    }
}
