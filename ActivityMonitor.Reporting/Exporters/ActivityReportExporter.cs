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
using ActivityMonitor.Infrastructure.Services;
using ClosedXML.Excel;
using Microsoft.Extensions.Logging;

namespace ActivityMonitor.Reporting.Exporters;

/// <summary>
/// فئة مسؤولة عن إنشاء وتصدير التقارير اليومية لأنشطة المستخدم بصيغ جداول بيانات Excel (.xlsx)،
/// وملفات نصية مفصولة بفواصل (.csv)، وملخصات نصية منسقة (.txt)، مع حماية الملفات بروابط تشغيل ذكية وكتابة ذرية.
/// </summary>
public class ActivityReportExporter : IActivityReportExporter
{
    /// <summary>
    /// مستودع استعلام أحداث الأنشطة والملخصات اليومية.
    /// </summary>
    private readonly IActivityRepository _activityRepository;

    /// <summary>
    /// مستودع استعلام مقاطع تسجيل الفيديو.
    /// </summary>
    private readonly IRecordingRepository _recordingRepository;

    /// <summary>
    /// مسجل السجلات التشخيصية.
    /// </summary>
    private readonly ILogger<ActivityReportExporter> _logger;

    /// <summary>
    /// منشئ الفئة: يستقبل المستودعات ومسجل العمليات.
    /// </summary>
    /// <param name="activityRepository">مستودع الأنشطة.</param>
    /// <param name="recordingRepository">مستودع التسجيلات.</param>
    /// <param name="logger">مسجل العمليات.</param>
    public ActivityReportExporter(
        IActivityRepository activityRepository,
        IRecordingRepository recordingRepository,
        ILogger<ActivityReportExporter> logger)
    {
        _activityRepository = activityRepository;
        _recordingRepository = recordingRepository;
        _logger = logger;
    }

    /// <summary>
    /// تصدير تقرير شامل ليوم كامل بصيغة ملف Excel (.xlsx) متعدد أوراق العمل (الملخص، الخط الزمني، التطبيقات، والتسجيلات).
    /// </summary>
    /// <param name="dateUtc">تاريخ اليوم المستهدف (UTC).</param>
    /// <param name="machineId">معرّف الجهاز (اختياري).</param>
    /// <param name="ct">رمز إلغاء العملية.</param>
    /// <returns>المسار الكامل لملف Excel المنشأ.</returns>
    public async Task<string> ExportDailyExcelReportAsync(DateTime dateUtc, string? machineId = null, CancellationToken ct = default)
    {
        var targetDate = dateUtc.Date;
        var (startUtc, endUtc) = ActivityTimelineMath.GetUtcDayRange(dateUtc);

        var machine = machineId ?? Environment.MachineName;

        // جلب البيانات اللازمة للتقرير من المستودعات
        var summary = await _activityRepository.GetDailySummaryAsync(targetDate, machine, ct);
        var activities = await _activityRepository.GetByDateRangeAsync(startUtc, endUtc, machine, ct);
        var recordings = await _recordingRepository.GetByDateRangeAsync(startUtc, endUtc, machine, ct);
        var topApps = await _activityRepository.GetTopApplicationsAsync(targetDate, machine, ct);

        // مسار حفظ التقرير
        var filePath = ActivityMonitorPaths.GetReportFilePath(machine, targetDate, "xlsx");
        EnsureSafeReportPath(filePath);

        using var workbook = new XLWorkbook();

        // ----------------------------------------------------
        // ورقة العمل الأولى: الملخص التنفيذي (Summary)
        // ----------------------------------------------------
        var wsSummary = workbook.Worksheets.Add("Summary");
        wsSummary.ShowGridLines = true;

        // شريط العنوان الرئيسي للتقرير
        wsSummary.Cell(1, 1).Value = "ActivityMonitor - Daily Executive Summary";
        wsSummary.Range(1, 1, 1, 4).Merge();
        wsSummary.Cell(1, 1).Style.Font.Bold = true;
        wsSummary.Cell(1, 1).Style.Font.FontSize = 16;
        wsSummary.Cell(1, 1).Style.Font.FontColor = XLColor.White;
        wsSummary.Range(1, 1, 1, 4).Style.Fill.BackgroundColor = XLColor.FromHtml("#1E3A8A"); // أزرق داكن
        wsSummary.Row(1).Height = 35;
        wsSummary.Cell(1, 1).Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;

        // عناصر ومؤشرات الملخص اليومي
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

        // ترويسة جدول المؤشرات
        wsSummary.Cell(3, 1).Value = "Metric";
        wsSummary.Cell(3, 2).Value = "Value";
        ApplyHeaderStyle(wsSummary.Range(3, 1, 3, 2), "#2563EB");

        int summaryRow = 4;
        foreach (var (k, v) in summaryItems)
        {
            wsSummary.Cell(summaryRow, 1).Value = k;
            wsSummary.Cell(summaryRow, 1).Style.Font.Bold = true;
            wsSummary.Cell(summaryRow, 2).Value = v.ToString();

            // تظليل تبادلي للأسطر (Zebra Striping)
            if (summaryRow % 2 == 0)
            {
                wsSummary.Range(summaryRow, 1, summaryRow, 2).Style.Fill.BackgroundColor = XLColor.FromHtml("#F8FAFC");
            }
            summaryRow++;
        }
        wsSummary.Range(3, 1, summaryRow - 1, 2).Style.Border.SetOutsideBorder(XLBorderStyleValues.Thin);
        wsSummary.Range(3, 1, summaryRow - 1, 2).Style.Border.SetInsideBorder(XLBorderStyleValues.Thin);
        wsSummary.Columns(1, 4).AdjustToContents();

        // ----------------------------------------------------
        // ورقة العمل الثانية: الخط الزمني للأنشطة (Timeline)
        // ----------------------------------------------------
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
            var recordingLocations = ResolveRecordingLocations(act, recordings, startUtc, endUtc);
            var displayStart = act.StartedAtUtc < startUtc ? startUtc : act.StartedAtUtc;
            var resolvedEnd = act.EndedAtUtc ?? act.StartedAtUtc.AddSeconds(Math.Max(0, act.DurationSeconds));
            var displayEnd = resolvedEnd > endUtc ? endUtc : resolvedEnd;
            var dur = TimeSpan.FromSeconds(ActivityTimelineMath.CalculateOverlapSeconds(
                act.StartedAtUtc, act.EndedAtUtc, act.DurationSeconds, startUtc, endUtc));

            wsTimeline.Cell(row, 1).Value = displayStart.ToString("HH:mm:ss");
            wsTimeline.Cell(row, 2).Value = displayEnd.ToString("HH:mm:ss");
            wsTimeline.Cell(row, 3).Value = FormatDuration(dur);
            wsTimeline.Cell(row, 4).Value = act.ProcessName;
            wsTimeline.Cell(row, 5).Value = act.WindowTitle ?? string.Empty;
            wsTimeline.Cell(row, 6).Value = act.ContextPath ?? string.Empty;
            wsTimeline.Cell(row, 7).Value = act.ProcessPath ?? string.Empty;
            wsTimeline.Cell(row, 8).Value = act.EventType.ToString();

            // حفظ تفاصيل أسماء ملفات التسجيل ونقاط الإزاحة
            if (recordingLocations.Count > 0)
            {
                wsTimeline.Cell(row, 9).Value = string.Join(" | ", recordingLocations.Select(location => location.FileName));
                wsTimeline.Cell(row, 10).Value = string.Join(" | ", recordingLocations.Select(location => FormatDuration(location.Offset)));

                var firstAvailable = recordingLocations.FirstOrDefault(location =>
                    IsSafeExistingRecording(location.FilePath, out _));

                // إنشاء رابط تشغيل تشعبي للملف إذا كان متاحاً على القرص
                if (firstAvailable != null && IsSafeExistingRecording(firstAvailable.FilePath, out var safeRecordingPath))
                {
                    var linkCell = wsTimeline.Cell(row, 11);
                    linkCell.Value = recordingLocations.Count > 1
                        ? $"Play first of {recordingLocations.Count} parts"
                        : "Play Recording";
                    linkCell.Style.Font.Underline = XLFontUnderlineValues.Single;
                    linkCell.Style.Font.FontColor = XLColor.Blue;
                    linkCell.SetHyperlink(new XLHyperlink(new Uri(safeRecordingPath).AbsoluteUri));
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

        // ----------------------------------------------------
        // ورقة العمل الثالثة: إحصائيات التطبيقات (Applications)
        // ----------------------------------------------------
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

        // ----------------------------------------------------
        // ورقة العمل الرابعة: مقاطع الفيديو المسجلة (Recordings)
        // ----------------------------------------------------
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

            // رابط مباشر لتشغيل الفيديو
            if (IsSafeExistingRecording(rec.FilePath, out var safeRecordingPath))
            {
                var playCell = wsRecordings.Cell(recRow, 8);
                playCell.Value = "Open Video";
                playCell.Style.Font.Underline = XLFontUnderlineValues.Single;
                playCell.Style.Font.FontColor = XLColor.Blue;
                playCell.SetHyperlink(new XLHyperlink(new Uri(safeRecordingPath).AbsoluteUri));
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

        // حفظ ملف المصنف بشكل ذري لتفادي التلف
        ct.ThrowIfCancellationRequested();
        SaveWorkbookAtomically(workbook, filePath);
        ReportFreshness.MarkComplete(filePath, ReportFreshness.Fingerprint(activities, recordings));
        _logger.LogInformation("Generated daily Excel report at {FilePath}", filePath);
        return filePath;
    }

    /// <summary>
    /// تصدير تقرير الأنشطة اليومية بصيغة ملف نصي مفصول بفواصل CSV (.csv) مع حماية ضد هجمات حقن الصيغ (CSV Injection).
    /// </summary>
    /// <param name="dateUtc">تاريخ اليوم المستهدف (UTC).</param>
    /// <param name="machineId">اسم الجهاز (اختياري).</param>
    /// <param name="ct">رمز الإلغاء.</param>
    /// <returns>المسار الكامل لملف CSV المنشأ.</returns>
    public async Task<string> ExportDailyCsvReportAsync(DateTime dateUtc, string? machineId = null, CancellationToken ct = default)
    {
        var targetDate = dateUtc.Date;
        var (startUtc, endUtc) = ActivityTimelineMath.GetUtcDayRange(dateUtc);

        var machine = machineId ?? Environment.MachineName;
        var activities = await _activityRepository.GetByDateRangeAsync(startUtc, endUtc, machine, ct);
        var recordings = await _recordingRepository.GetByDateRangeAsync(startUtc, endUtc, machine, ct);
        var filePath = ActivityMonitorPaths.GetReportFilePath(machine, targetDate, "csv");
        EnsureSafeReportPath(filePath);

        var sb = new StringBuilder();
        // ترويسة أعمدة ملف CSV
        sb.AppendLine("TimestampUtc,EndTimeUtc,Duration,Application,WindowTitle,ContextPath,ProcessPath,EventType,RecordingFile,RecordingOffset");

        foreach (var act in activities)
        {
            var recordingLocations = ResolveRecordingLocations(act, recordings, startUtc, endUtc);
            var displayStart = act.StartedAtUtc < startUtc ? startUtc : act.StartedAtUtc;
            var resolvedEnd = act.EndedAtUtc ?? act.StartedAtUtc.AddSeconds(Math.Max(0, act.DurationSeconds));
            var displayEnd = resolvedEnd > endUtc ? endUtc : resolvedEnd;
            var dur = TimeSpan.FromSeconds(ActivityTimelineMath.CalculateOverlapSeconds(
                act.StartedAtUtc, act.EndedAtUtc, act.DurationSeconds, startUtc, endUtc));
            var endStr = displayEnd.ToString("yyyy-MM-dd HH:mm:ss");

            string recFile = "-";
            string recOffset = "-";

            if (recordingLocations.Count > 0)
            {
                recFile = string.Join(" | ", recordingLocations.Select(location => location.FileName));
                recOffset = string.Join(" | ", recordingLocations.Select(location => FormatDuration(location.Offset)));
            }

            // كتابة السطر وتأمين القيم ضد CSV Injection
            sb.Append(displayStart.ToString("yyyy-MM-dd HH:mm:ss")).Append(',');
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

        // حفظ الملف بشكل ذري
        await WriteTextAtomicallyAsync(filePath, sb.ToString(), ct);
        ReportFreshness.MarkComplete(filePath, ReportFreshness.Fingerprint(activities, recordings));
        _logger.LogInformation("Generated daily CSV report at {FilePath}", filePath);
        return filePath;
    }

    /// <summary>
    /// تصدير تقرير نصي منسق ومقروء بالعين المجردة (.txt) يتضمن ملخص اليوم وأعلى التطبيقات والتسجيلات.
    /// </summary>
    /// <param name="dateUtc">تاريخ اليوم المستهدف (UTC).</param>
    /// <param name="machineId">اسم الجهاز (اختياري).</param>
    /// <param name="ct">رمز الإلغاء.</param>
    /// <returns>المسار الكامل لملف TXT المنشأ.</returns>
    public async Task<string> ExportDailySummaryTextAsync(DateTime dateUtc, string? machineId = null, CancellationToken ct = default)
    {
        var targetDate = dateUtc.Date;
        var (startUtc, endUtc) = ActivityTimelineMath.GetUtcDayRange(dateUtc);

        var machine = machineId ?? Environment.MachineName;
        var activities = await _activityRepository.GetByDateRangeAsync(startUtc, endUtc, machine, ct);
        var summary = await _activityRepository.GetDailySummaryAsync(targetDate, machine, ct);
        var topApps = await _activityRepository.GetTopApplicationsAsync(targetDate, machine, ct);
        var recordings = await _recordingRepository.GetByDateRangeAsync(startUtc, endUtc, machine, ct);

        var filePath = ActivityMonitorPaths.GetReportFilePath(machine, targetDate, "txt");
        EnsureSafeReportPath(filePath);

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

        await WriteTextAtomicallyAsync(filePath, sb.ToString(), ct);
        ReportFreshness.MarkComplete(filePath, ReportFreshness.Fingerprint(activities, recordings));
        _logger.LogInformation("Generated daily TXT report at {FilePath}", filePath);
        return filePath;
    }

    /// <summary>
    /// تطبيق النمط والتنسيق البصري على صفوف الترويسة في جداول Excel.
    /// </summary>
    private static void ApplyHeaderStyle(IXLRange range, string hexColor)
    {
        range.Style.Font.Bold = true;
        range.Style.Font.FontColor = XLColor.White;
        range.Style.Fill.BackgroundColor = XLColor.FromHtml(hexColor);
        range.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        range.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
    }

    /// <summary>
    /// تنسيق الفترة الزمنية بصيغة ساعات:دقائق:ثوانٍ (HH:mm:ss).
    /// </summary>
    private static string FormatDuration(TimeSpan ts)
    {
        return $"{(int)ts.TotalHours:D2}:{ts.Minutes:D2}:{ts.Seconds:D2}";
    }

    /// <summary>
    /// تنقية وتجهيز النصوص لتصدير CSV وحمايتها من الفواصل وعلامات التنصيص وهجمات الصيغ.
    /// </summary>
    private static string EscapeCsv(string field)
    {
        var safeField = ProtectSpreadsheetCell(field);
        if (safeField.Contains(',') || safeField.Contains('"') || safeField.Contains('\n') || safeField.Contains('\r'))
        {
            return $"\"{safeField.Replace("\"", "\"\"")}\"";
        }
        return safeField;
    }

    /// <summary>
    /// حماية خلايا الجداول من هجمات حقن الصيغ (Formula Injection) ببادئة علامة الاقتباس الفردية إذا بدأت بـ =, +, -, @.
    /// </summary>
    private static string ProtectSpreadsheetCell(string field)
    {
        if (string.IsNullOrEmpty(field)) return string.Empty;

        var firstMeaningfulIndex = 0;
        while (firstMeaningfulIndex < field.Length && char.IsWhiteSpace(field[firstMeaningfulIndex]))
        {
            firstMeaningfulIndex++;
        }

        if (firstMeaningfulIndex < field.Length &&
            field[firstMeaningfulIndex] is '=' or '+' or '-' or '@')
        {
            return "'" + field;
        }

        return field;
    }

    /// <summary>
    /// التحقق الأمني من سلامة مسار تسجيل الفيديو ووجوده الفعلي على القرص.
    /// </summary>
    private static bool IsSafeExistingRecording(string candidatePath, out string safePath) =>
        StoragePathGuard.TryResolvePathWithinRoot(
            candidatePath,
            ActivityMonitorPaths.RecordingsDirectory,
            out safePath,
            out _) &&
        File.Exists(safePath);

    private static void EnsureSafeReportPath(string path)
    {
        if (!StoragePathGuard.TryResolvePathWithinRoot(path, ActivityMonitorPaths.ReportsDirectory, out _, out var reason))
            throw new UnauthorizedAccessException(reason);
    }

    /// <summary>
    /// استنتاج وحساب مواقع مقاطع الفيديو المتزامنة مع حدث النشاط وحساب الإزاحات الزمنية.
    /// </summary>
    private static List<RecordingLocationResult> ResolveRecordingLocations(
        ActivityEvent activity, IReadOnlyList<RecordingSegment> recordings,
        DateTime? rangeStartUtc = null, DateTime? rangeEndUtc = null)
    {
        var locations = RecordingTimeline.Resolve(activity, recordings, DateTime.UtcNow, rangeStartUtc, rangeEndUtc);
        foreach (var location in locations) location.FileExists = IsSafeExistingRecording(location.FilePath, out _);
        return locations;
    }

    /// <summary>
    /// حفظ ملف مصنف Excel بشكل ذري (Atomic Save) عبر ملف مؤقت ثم نقله، لتفادي إتلاف الملف الأصلي عند انقطاع العملية.
    /// </summary>
    private static void SaveWorkbookAtomically(XLWorkbook workbook, string destinationPath)
    {
        var temporaryPath = CreateTemporarySiblingPath(destinationPath, preserveDestinationExtension: true);
        try
        {
            workbook.SaveAs(temporaryPath);
            File.Move(temporaryPath, destinationPath, true);
        }
        finally
        {
            TryDeleteTemporaryFile(temporaryPath);
        }
    }

    /// <summary>
    /// كتابة الملف النصي بشكل ذري عبر ملف مؤقت لتفادي التلف أثناء الكتابة.
    /// </summary>
    private static async Task WriteTextAtomicallyAsync(
        string destinationPath,
        string contents,
        CancellationToken ct)
    {
        var temporaryPath = CreateTemporarySiblingPath(destinationPath, preserveDestinationExtension: false);
        try
        {
            await File.WriteAllTextAsync(temporaryPath, contents, Encoding.UTF8, ct);
            File.Move(temporaryPath, destinationPath, true);
        }
        finally
        {
            TryDeleteTemporaryFile(temporaryPath);
        }
    }

    /// <summary>
    /// توليد مسار مؤقت بجوار الملف الأصلي.
    /// </summary>
    private static string CreateTemporarySiblingPath(string destinationPath, bool preserveDestinationExtension)
    {
        var parent = Path.GetDirectoryName(destinationPath) ??
            throw new InvalidOperationException("Report path has no parent directory.");
        var extension = preserveDestinationExtension ? Path.GetExtension(destinationPath) : string.Empty;
        var temporaryName = $".{Path.GetFileName(destinationPath)}.{Guid.NewGuid():N}.tmp{extension}";
        return Path.Combine(parent, temporaryName);
    }

    /// <summary>
    /// محاولة حذف الملف المؤقت بأمان عند الانتهاء أو الفشل.
    /// </summary>
    private static void TryDeleteTemporaryFile(string temporaryPath)
    {
        try
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
        catch
        {
        }
    }

    /// <summary>
    /// تقصير النصوص الطويلة مع إضافة ثلاث نقاط (...) لتناسب الجداول النصية.
    /// </summary>
    private static string Truncate(string val, int maxLen)
    {
        if (string.IsNullOrEmpty(val)) return string.Empty;
        return val.Length <= maxLen ? val : val.Substring(0, maxLen - 3) + "...";
    }
}
