using System;
using System.Threading;
using System.Threading.Tasks;
using ActivityMonitor.Core.Interfaces;
using ActivityMonitor.Core.Options;
using ActivityMonitor.Infrastructure.Data;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ActivityMonitor.Service;

public class Worker : BackgroundService
{
    private readonly IStorageManagementService _storageService;
    private readonly IActivityReportExporter _reportExporter;
    private readonly ReportsOptions _reportsOptions;
    private readonly StorageOptions _storageOptions;
    private readonly ILogger<Worker> _logger;

    private DateTime _lastDailyReportDate = DateTime.MinValue;

    public Worker(
        IStorageManagementService storageService,
        IActivityReportExporter reportExporter,
        IOptions<ReportsOptions> reportsOptions,
        IOptions<StorageOptions> storageOptions,
        ILogger<Worker> logger)
    {
        _storageService = storageService;
        _reportExporter = reportExporter;
        _reportsOptions = reportsOptions.Value;
        _storageOptions = storageOptions.Value;
        _logger = logger;
    }

    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("ActivityMonitor Service is starting in Session 0...");

        try
        {
            await ActivityMonitorDbContext.InitializeDatabaseAsync(null, cancellationToken);
            _logger.LogInformation("Database initialized and verified by Windows Service.");

            // Initial retention check on startup
            await _storageService.EnforceRetentionPoliciesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Initial startup checks encountered an error");
        }

        await base.StartAsync(cancellationToken);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("ActivityMonitor Service background worker active.");

        var lastRetentionRun = DateTime.UtcNow;
        var lastHealthRun = DateTime.MinValue;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var now = DateTime.UtcNow;

                // 1. Health Log Check (every 4 hours)
                if ((now - lastHealthRun).TotalHours >= 4)
                {
                    await LogHealthMetricsAsync(stoppingToken);
                    lastHealthRun = now;
                }

                // 2. Storage Retention Check (every 1 hour)
                if ((now - lastRetentionRun).TotalHours >= 1)
                {
                    _logger.LogInformation("Running scheduled retention policy check...");
                    await _storageService.EnforceRetentionPoliciesAsync(stoppingToken);
                    lastRetentionRun = now;
                }

                // 3. Daily Report Generation Check
                if (_reportsOptions.DailyReportEnabled)
                {
                    await CheckAndGenerateDailyReportAsync(now, stoppingToken);
                }

                // Wait 60 seconds before next evaluation
                await Task.Delay(TimeSpan.FromSeconds(60), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unhandled exception in Windows Service execution loop");
                await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
            }
        }
    }

    private async Task LogHealthMetricsAsync(CancellationToken ct)
    {
        try
        {
            var usage = await _storageService.GetStorageUsageAsync(ct);
            _logger.LogInformation(
                "Health Summary -> DB: {DbMB:F2} MB | Recordings: {RecMB:F2} MB | Reports: {RepMB:F2} MB | Logs: {LogMB:F2} MB | Free Disk: {FreeGB:F2} GB / {TotalGB:F2} GB",
                usage.DatabaseSizeMB, usage.RecordingsSizeMB, usage.ReportsSizeMB, usage.LogsSizeMB, usage.FreeDiskSpaceGB, usage.TotalDiskSpaceGB);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not collect health metrics");
        }
    }

    private async Task CheckAndGenerateDailyReportAsync(DateTime nowUtc, CancellationToken ct)
    {
        var localNow = DateTime.Now;
        var scheduledTime = _reportsOptions.DailyReportScheduleTime;

        // Check if current time is past scheduled time today and hasn't been generated yet for today
        if (localNow.TimeOfDay >= scheduledTime && _lastDailyReportDate.Date != localNow.Date)
        {
            _logger.LogInformation("Scheduled time reached for Daily Report ({Date:yyyy-MM-dd}). Generating reports...", localNow.Date);

            try
            {
                if (_reportsOptions.ExcelEnabled)
                {
                    var excelPath = await _reportExporter.ExportDailyExcelReportAsync(localNow.Date, null, ct);
                    _logger.LogInformation("Excel daily report generated: {Path}", excelPath);
                }

                if (_reportsOptions.CsvEnabled)
                {
                    var csvPath = await _reportExporter.ExportDailyCsvReportAsync(localNow.Date, null, ct);
                    _logger.LogInformation("CSV daily report generated: {Path}", csvPath);
                }

                if (_reportsOptions.TxtEnabled)
                {
                    var txtPath = await _reportExporter.ExportDailySummaryTextAsync(localNow.Date, null, ct);
                    _logger.LogInformation("TXT daily summary generated: {Path}", txtPath);
                }

                _lastDailyReportDate = localNow.Date;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to generate daily report");
            }
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("ActivityMonitor Service is stopping cleanly...");
        await base.StopAsync(cancellationToken);
    }
}
