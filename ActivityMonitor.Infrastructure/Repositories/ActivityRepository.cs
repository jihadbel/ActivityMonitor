using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ActivityMonitor.Core.Entities;
using ActivityMonitor.Core.Enums;
using ActivityMonitor.Core.Interfaces;
using ActivityMonitor.Core.Models;
using ActivityMonitor.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ActivityMonitor.Infrastructure.Repositories;

public class ActivityRepository : IActivityRepository
{
    private readonly IDbContextFactory<ActivityMonitorDbContext> _contextFactory;
    private readonly ILogger<ActivityRepository> _logger;

    public ActivityRepository(IDbContextFactory<ActivityMonitorDbContext> contextFactory, ILogger<ActivityRepository> logger)
    {
        _contextFactory = contextFactory;
        _logger = logger;
    }

    public async Task<ActivityEvent> AddAsync(ActivityEvent activityEvent, CancellationToken ct = default)
    {
        try
        {
            await using var context = await _contextFactory.CreateDbContextAsync(ct);
            context.ActivityEvents.Add(activityEvent);
            await context.SaveChangesAsync(ct);
            return activityEvent;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to persist ActivityEvent {ProcessName} ({StartedAtUtc})", activityEvent.ProcessName, activityEvent.StartedAtUtc);
            throw;
        }
    }

    public async Task AddRangeAsync(IEnumerable<ActivityEvent> activityEvents, CancellationToken ct = default)
    {
        var list = activityEvents.ToList();
        if (list.Count == 0) return;

        try
        {
            await using var context = await _contextFactory.CreateDbContextAsync(ct);
            await context.ActivityEvents.AddRangeAsync(list, ct);
            await context.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to persist batch of {Count} ActivityEvents", list.Count);
            throw;
        }
    }

    public async Task UpdateAsync(ActivityEvent activityEvent, CancellationToken ct = default)
    {
        try
        {
            await using var context = await _contextFactory.CreateDbContextAsync(ct);
            context.ActivityEvents.Update(activityEvent);
            await context.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update ActivityEvent {Id}", activityEvent.Id);
            throw;
        }
    }

    public async Task<ActivityEvent?> GetByIdAsync(long id, CancellationToken ct = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(ct);
        return await context.ActivityEvents
            .Include(a => a.RecordingSegment)
            .FirstOrDefaultAsync(a => a.Id == id, ct);
    }

    public async Task<List<ActivityEvent>> GetByDateRangeAsync(DateTime startUtc, DateTime endUtc, string? machineId = null, CancellationToken ct = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(ct);
        var query = context.ActivityEvents
            .AsNoTracking()
            .Include(a => a.RecordingSegment)
            .Where(a => a.StartedAtUtc >= startUtc && a.StartedAtUtc <= endUtc);

        if (!string.IsNullOrWhiteSpace(machineId))
        {
            query = query.Where(a => a.MachineId == machineId);
        }

        return await query.OrderBy(a => a.StartedAtUtc).ToListAsync(ct);
    }

    public async Task<List<ActivityEvent>> GetUnlinkedActivitiesAsync(DateTime beforeUtc, CancellationToken ct = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(ct);
        return await context.ActivityEvents
            .Where(a => a.RecordingSegmentId == null && a.StartedAtUtc <= beforeUtc)
            .OrderBy(a => a.StartedAtUtc)
            .Take(500)
            .ToListAsync(ct);
    }

    public async Task<DailyActivitySummary> GetDailySummaryAsync(DateTime dateUtc, string? machineId = null, CancellationToken ct = default)
    {
        var startOfDay = dateUtc.Date;
        var endOfDay = startOfDay.AddDays(1).AddTicks(-1);

        await using var context = await _contextFactory.CreateDbContextAsync(ct);
        var query = context.ActivityEvents
            .AsNoTracking()
            .Where(a => a.StartedAtUtc >= startOfDay && a.StartedAtUtc <= endOfDay);

        if (!string.IsNullOrWhiteSpace(machineId))
        {
            query = query.Where(a => a.MachineId == machineId);
        }

        var events = await query.ToListAsync(ct);

        var recordingQuery = context.RecordingSegments
            .AsNoTracking()
            .Where(r => r.StartedAtUtc >= startOfDay && r.StartedAtUtc <= endOfDay);

        if (!string.IsNullOrWhiteSpace(machineId))
        {
            recordingQuery = recordingQuery.Where(r => r.MachineId == machineId);
        }

        var recordings = await recordingQuery.ToListAsync(ct);

        var activeEvents = events.Where(e => e.EventType == ActivityEventType.ActiveWindow).ToList();
        var idleEvents = events.Where(e => e.EventType == ActivityEventType.IdleStart).ToList();

        var totalActiveSeconds = activeEvents.Sum(e => e.DurationSeconds);
        var totalIdleSeconds = idleEvents.Sum(e => e.DurationSeconds);

        return new DailyActivitySummary
        {
            Date = startOfDay,
            MachineId = machineId ?? (events.FirstOrDefault()?.MachineId ?? Environment.MachineName),
            UserIdentifier = events.FirstOrDefault()?.UserIdentifier ?? Environment.UserName,
            TotalActiveTime = TimeSpan.FromSeconds(totalActiveSeconds),
            TotalIdleTime = TimeSpan.FromSeconds(totalIdleSeconds),
            FirstActivityUtc = events.OrderBy(e => e.StartedAtUtc).FirstOrDefault()?.StartedAtUtc,
            LastActivityUtc = events.OrderByDescending(e => e.EndedAtUtc ?? e.StartedAtUtc).FirstOrDefault()?.EndedAtUtc,
            UniqueApplicationsCount = events.Select(e => e.ProcessName).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
            TotalEventsCount = events.Count,
            RecordingSegmentsCount = recordings.Count,
            TotalRecordingSizeBytes = recordings.Sum(r => r.FileSizeBytes)
        };
    }

    public async Task<List<ApplicationUsageSummary>> GetTopApplicationsAsync(DateTime dateUtc, string? machineId = null, CancellationToken ct = default)
    {
        var startOfDay = dateUtc.Date;
        var endOfDay = startOfDay.AddDays(1).AddTicks(-1);

        await using var context = await _contextFactory.CreateDbContextAsync(ct);
        var query = context.ActivityEvents
            .AsNoTracking()
            .Where(a => a.StartedAtUtc >= startOfDay && a.StartedAtUtc <= endOfDay && a.EventType == ActivityEventType.ActiveWindow);

        if (!string.IsNullOrWhiteSpace(machineId))
        {
            query = query.Where(a => a.MachineId == machineId);
        }

        var events = await query.ToListAsync(ct);
        var totalSeconds = events.Sum(e => e.DurationSeconds);
        if (totalSeconds <= 0) totalSeconds = 1;

        return events
            .GroupBy(e => e.ProcessName, StringComparer.OrdinalIgnoreCase)
            .Select(g =>
            {
                var duration = g.Sum(x => x.DurationSeconds);
                return new ApplicationUsageSummary
                {
                    ApplicationName = g.Key,
                    TotalDuration = TimeSpan.FromSeconds(duration),
                    UsageCount = g.Count(),
                    Percentage = Math.Round((duration / totalSeconds) * 100.0, 2)
                };
            })
            .OrderByDescending(a => a.TotalDuration)
            .ToList();
    }

    public async Task<int> PurgeOlderThanAsync(DateTime cutoffUtc, CancellationToken ct = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(ct);
        return await context.ActivityEvents
            .Where(a => a.StartedAtUtc < cutoffUtc)
            .ExecuteDeleteAsync(ct);
    }
}
