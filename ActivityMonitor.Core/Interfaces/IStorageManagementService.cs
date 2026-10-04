using System.Threading;
using System.Threading.Tasks;
using ActivityMonitor.Core.Models;

namespace ActivityMonitor.Core.Interfaces;

public interface IStorageManagementService
{
    Task<StorageUsageSummary> GetStorageUsageAsync(CancellationToken ct = default);
    Task<int> EnforceRetentionPoliciesAsync(CancellationToken ct = default);
    Task<bool> EnsureDiskSpaceAvailableAsync(CancellationToken ct = default);
}
