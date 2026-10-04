using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ActivityMonitor.Core.Entities;
using ActivityMonitor.Core.Models;

namespace ActivityMonitor.Core.Interfaces;

public interface IActivityRepository
{
    Task<ActivityEvent> AddAsync(ActivityEvent activityEvent, CancellationToken ct = default);
    Task AddRangeAsync(IEnumerable<ActivityEvent> activityEvents, CancellationToken ct = default);
    Task UpdateAsync(ActivityEvent activityEvent, CancellationToken ct = default);
    Task<ActivityEvent?> GetByIdAsync(long id, CancellationToken ct = default);
    Task<List<ActivityEvent>> GetByDateRangeAsync(DateTime startUtc, DateTime endUtc, string? machineId = null, CancellationToken ct = default);
    Task<List<ActivityEvent>> GetUnlinkedActivitiesAsync(DateTime beforeUtc, CancellationToken ct = default);
    Task<DailyActivitySummary> GetDailySummaryAsync(DateTime dateUtc, string? machineId = null, CancellationToken ct = default);
    Task<List<ApplicationUsageSummary>> GetTopApplicationsAsync(DateTime dateUtc, string? machineId = null, CancellationToken ct = default);
    Task<int> PurgeOlderThanAsync(DateTime cutoffUtc, CancellationToken ct = default);
}
