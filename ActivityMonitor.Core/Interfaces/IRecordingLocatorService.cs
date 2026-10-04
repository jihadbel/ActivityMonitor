using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ActivityMonitor.Core.Entities;
using ActivityMonitor.Core.Models;

namespace ActivityMonitor.Core.Interfaces;

public interface IRecordingLocatorService
{
    Task<List<RecordingLocationResult>> LocateAsync(ActivityEvent activityEvent, CancellationToken ct = default);
    Task<List<RecordingLocationResult>> LocateByTimestampAsync(DateTime timestampUtc, TimeSpan? duration = null, string? machineId = null, CancellationToken ct = default);
    Task<int> LinkUnlinkedActivitiesAsync(DateTime beforeUtc, CancellationToken ct = default);
}
