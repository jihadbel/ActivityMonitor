using ActivityMonitor.Core.Options;
using ActivityMonitor.Reporting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ActivityMonitor.Service;

public sealed class Worker(IServiceScopeFactory scopes, IOptionsMonitor<ReportsOptions> reports,
    IOptionsMonitor<StorageOptions> storage, ILogger<MaintenanceWorker> logger)
    : MaintenanceWorker(scopes, reports, storage, logger);
