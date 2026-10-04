using System;
using System.Threading;
using System.Threading.Tasks;
using ActivityMonitor.Core.Entities;

namespace ActivityMonitor.Core.Interfaces;

public interface IActivityTracker
{
    event Func<ActivityEvent, Task>? ActivityStarted;
    event Func<ActivityEvent, Task>? ActivityEnded;

    Task StartAsync(CancellationToken ct = default);
    Task StopAsync(CancellationToken ct = default);
}
