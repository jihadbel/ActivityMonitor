using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ActivityMonitor.Core.Entities;
using ActivityMonitor.Core.Enums;
using ActivityMonitor.Core.Interfaces;
using ActivityMonitor.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ActivityMonitor.Infrastructure.Repositories;

public class RecordingRepository : IRecordingRepository
{
    private readonly IDbContextFactory<ActivityMonitorDbContext> _contextFactory;
    private readonly ILogger<RecordingRepository> _logger;

    public RecordingRepository(IDbContextFactory<ActivityMonitorDbContext> contextFactory, ILogger<RecordingRepository> logger)
    {
        _contextFactory = contextFactory;
        _logger = logger;
    }

    public async Task<RecordingSegment> AddAsync(RecordingSegment segment, CancellationToken ct = default)
    {
        try
        {
            await using var context = await _contextFactory.CreateDbContextAsync(ct);
            context.RecordingSegments.Add(segment);
            await context.SaveChangesAsync(ct);
            return segment;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to persist RecordingSegment {FileName}", segment.FileName);
            throw;
        }
    }

    public async Task UpdateAsync(RecordingSegment segment, CancellationToken ct = default)
    {
        try
        {
            await using var context = await _contextFactory.CreateDbContextAsync(ct);
            context.RecordingSegments.Update(segment);
            await context.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update RecordingSegment {Id}", segment.Id);
            throw;
        }
    }

    public async Task<RecordingSegment?> GetByIdAsync(long id, CancellationToken ct = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(ct);
        return await context.RecordingSegments.FirstOrDefaultAsync(r => r.Id == id, ct);
    }

    public async Task<RecordingSegment?> GetActiveSegmentAsync(string machineId, CancellationToken ct = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(ct);
        return await context.RecordingSegments
            .Where(r => r.MachineId == machineId && r.Status == RecordingStatus.InProgress)
            .OrderByDescending(r => r.StartedAtUtc)
            .FirstOrDefaultAsync(ct);
    }

    public async Task<List<RecordingSegment>> GetByDateRangeAsync(DateTime startUtc, DateTime endUtc, string? machineId = null, CancellationToken ct = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(ct);
        var query = context.RecordingSegments
            .AsNoTracking()
            .Where(r => r.StartedAtUtc >= startUtc && r.StartedAtUtc <= endUtc);

        if (!string.IsNullOrWhiteSpace(machineId))
        {
            query = query.Where(r => r.MachineId == machineId);
        }

        return await query.OrderBy(r => r.StartedAtUtc).ToListAsync(ct);
    }

    public async Task<List<RecordingSegment>> GetSegmentsCoveringTimeRangeAsync(DateTime startUtc, DateTime endUtc, string? machineId = null, CancellationToken ct = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(ct);
        var query = context.RecordingSegments
            .AsNoTracking()
            .Where(r => r.Status == RecordingStatus.Completed || r.Status == RecordingStatus.InProgress);

        if (!string.IsNullOrWhiteSpace(machineId))
        {
            query = query.Where(r => r.MachineId == machineId);
        }

        // Segment overlaps with [startUtc, endUtc] if:
        // segment.StartedAtUtc <= endUtc AND (segment.EndedAtUtc == null || segment.EndedAtUtc >= startUtc)
        query = query.Where(r => r.StartedAtUtc <= endUtc && (r.EndedAtUtc == null || r.EndedAtUtc >= startUtc));

        return await query.OrderBy(r => r.StartedAtUtc).ToListAsync(ct);
    }

    public async Task<List<RecordingSegment>> GetSegmentsOlderThanAsync(DateTime cutoffUtc, CancellationToken ct = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(ct);
        return await context.RecordingSegments
            .Where(r => r.StartedAtUtc < cutoffUtc && r.Status != RecordingStatus.Deleted && r.Status != RecordingStatus.DiskSpaceReclaimed)
            .OrderBy(r => r.StartedAtUtc)
            .ToListAsync(ct);
    }

    public async Task<List<RecordingSegment>> GetAllCompletedSegmentsAsync(CancellationToken ct = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(ct);
        return await context.RecordingSegments
            .Where(r => r.Status == RecordingStatus.Completed)
            .OrderBy(r => r.StartedAtUtc)
            .ToListAsync(ct);
    }

    public async Task<int> MarkAsDeletedAsync(long id, CancellationToken ct = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(ct);
        var segment = await context.RecordingSegments.FindAsync(new object[] { id }, ct);
        if (segment == null) return 0;

        segment.Status = RecordingStatus.Deleted;
        return await context.SaveChangesAsync(ct);
    }
}
