using System;
using System.Threading;
using System.Threading.Tasks;
using ActivityMonitor.Agent.Recording;
using ActivityMonitor.Agent.Startup;
using ActivityMonitor.Agent.Tracking;
using ActivityMonitor.Core.Interfaces;
using ActivityMonitor.Infrastructure.Data;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ActivityMonitor.Agent;

public class Worker : BackgroundService
{
    private readonly IActivityTracker _tracker;
    private readonly IScreenRecordingService _recordingService;
    private readonly IRecordingLocatorService _locatorService;
    private readonly ILogger<Worker> _logger;

    public Worker(
        IActivityTracker tracker,
        IScreenRecordingService recordingService,
        IRecordingLocatorService locatorService,
        ILogger<Worker> logger)
    {
        _tracker = tracker;
        _recordingService = recordingService;
        _locatorService = locatorService;
        _logger = logger;
    }

    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("ActivityMonitor Agent starting up in user session...");

        try
        {
            // 1. Initialize SQLite Database & Tables
            await ActivityMonitorDbContext.InitializeDatabaseAsync(null, cancellationToken);
            _logger.LogInformation("Database initialized successfully.");

            // 2. Ensure auto-start with Windows is enabled
            if (!StartupManager.IsStartupEnabled())
            {
                StartupManager.EnableStartup();
                _logger.LogInformation("Registered ActivityMonitor Agent in user startup.");
            }

            // 3. Start Screen Recording
            await _recordingService.StartRecordingSegmentAsync(cancellationToken);

            // 4. Start Activity Tracking
            await _tracker.StartAsync(cancellationToken);

            _logger.LogInformation("ActivityMonitor Agent is now running silently.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error occurred during Agent startup initialization");
        }

        await base.StartAsync(cancellationToken);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // Run background reconciliation every 60 seconds
                await Task.Delay(TimeSpan.FromSeconds(60), stoppingToken);

                // Correlate unlinked activities with video recordings
                await _locatorService.LinkUnlinkedActivitiesAsync(DateTime.UtcNow.AddSeconds(-5), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error in Agent background loop");
            }
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("ActivityMonitor Agent stopping...");

        try
        {
            await _tracker.StopAsync(cancellationToken);
            await _recordingService.StopRecordingSegmentAsync(cancellationToken);

            // Final linking sweep
            await _locatorService.LinkUnlinkedActivitiesAsync(DateTime.UtcNow, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error while stopping Agent");
        }

        _logger.LogInformation("ActivityMonitor Agent stopped cleanly.");
        await base.StopAsync(cancellationToken);
    }
}
