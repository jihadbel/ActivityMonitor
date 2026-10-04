using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ActivityMonitor.Core.Enums;
using ActivityMonitor.Core.Interfaces;
using ActivityMonitor.Core.Models;
using ActivityMonitor.Core.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ActivityMonitor.Infrastructure.Services;

public class StorageManagementService : IStorageManagementService
{
    private readonly IRecordingRepository _recordingRepository;
    private readonly IActivityRepository _activityRepository;
    private readonly StorageOptions _storageOptions;
    private readonly ILogger<StorageManagementService> _logger;

    public StorageManagementService(
        IRecordingRepository recordingRepository,
        IActivityRepository activityRepository,
        IOptions<StorageOptions> storageOptions,
        ILogger<StorageManagementService> logger)
    {
        _recordingRepository = recordingRepository;
        _activityRepository = activityRepository;
        _storageOptions = storageOptions.Value;
        _logger = logger;
    }

    public Task<StorageUsageSummary> GetStorageUsageAsync(CancellationToken ct = default)
    {
        ActivityMonitorPaths.EnsureDirectoriesExist();

        long dbSize = 0;
        if (File.Exists(ActivityMonitorPaths.DatabasePath))
        {
            var fi = new FileInfo(ActivityMonitorPaths.DatabasePath);
            dbSize += fi.Length;

            // Also include WAL and SHM files if present
            var walPath = ActivityMonitorPaths.DatabasePath + "-wal";
            if (File.Exists(walPath)) dbSize += new FileInfo(walPath).Length;

            var shmPath = ActivityMonitorPaths.DatabasePath + "-shm";
            if (File.Exists(shmPath)) dbSize += new FileInfo(shmPath).Length;
        }

        long recordingsSize = CalculateDirectorySize(ActivityMonitorPaths.RecordingsDirectory);
        long reportsSize = CalculateDirectorySize(ActivityMonitorPaths.ReportsDirectory);
        long logsSize = CalculateDirectorySize(ActivityMonitorPaths.LogsDirectory);

        long freeDiskSpace = 0;
        long totalDiskSpace = 0;

        try
        {
            var driveInfo = new DriveInfo(Path.GetPathRoot(ActivityMonitorPaths.RootDirectory) ?? "C:\\");
            freeDiskSpace = driveInfo.AvailableFreeSpace;
            totalDiskSpace = driveInfo.TotalSize;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not determine drive metrics for root {Root}", ActivityMonitorPaths.RootDirectory);
        }

        var summary = new StorageUsageSummary
        {
            DatabaseSizeBytes = dbSize,
            RecordingsSizeBytes = recordingsSize,
            ReportsSizeBytes = reportsSize,
            LogsSizeBytes = logsSize,
            FreeDiskSpaceBytes = freeDiskSpace,
            TotalDiskSpaceBytes = totalDiskSpace
        };

        return Task.FromResult(summary);
    }

    public async Task<int> EnforceRetentionPoliciesAsync(CancellationToken ct = default)
    {
        int deletedRecordingsCount = 0;
        var now = DateTime.UtcNow;

        _logger.LogInformation("Enforcing storage retention policies...");

        // 1. Retention by date for recordings
        var recordingCutoff = now.AddDays(-_storageOptions.RecordingRetentionDays);
        var oldSegments = await _recordingRepository.GetSegmentsOlderThanAsync(recordingCutoff, ct);

        foreach (var segment in oldSegments)
        {
            if (File.Exists(segment.FilePath))
            {
                try
                {
                    File.Delete(segment.FilePath);
                    _logger.LogInformation("Deleted expired recording file: {FilePath}", segment.FilePath);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Could not delete expired recording file: {FilePath}", segment.FilePath);
                }
            }

            segment.Status = RecordingStatus.Deleted;
            await _recordingRepository.UpdateAsync(segment, ct);
            deletedRecordingsCount++;
        }

        // 2. Storage quota & disk space check
        var usage = await GetStorageUsageAsync(ct);
        var maxBytes = _storageOptions.MaxStorageSizeGB * 1024L * 1024L * 1024L;
        var minFreeBytes = _storageOptions.MinimumFreeDiskSpaceGB * 1024L * 1024L * 1024L;

        if (usage.TotalSizeBytes > maxBytes || usage.FreeDiskSpaceBytes < minFreeBytes)
        {
            _logger.LogWarning("Storage limit exceeded (Total: {TotalMB}MB, FreeDisk: {FreeGB}GB). Purging oldest completed recordings...",
                usage.TotalSizeMB, usage.FreeDiskSpaceGB);

            var completedSegments = await _recordingRepository.GetAllCompletedSegmentsAsync(ct);
            foreach (var segment in completedSegments.OrderBy(s => s.StartedAtUtc))
            {
                if (usage.TotalSizeBytes <= maxBytes && usage.FreeDiskSpaceBytes >= minFreeBytes)
                {
                    break;
                }

                if (File.Exists(segment.FilePath))
                {
                    try
                    {
                        var fi = new FileInfo(segment.FilePath);
                        var size = fi.Length;
                        File.Delete(segment.FilePath);
                        _logger.LogInformation("Reclaimed disk space by deleting: {FilePath} ({SizeMB:F2} MB)", segment.FilePath, size / (1024.0 * 1024.0));
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Could not delete recording file to reclaim space: {FilePath}", segment.FilePath);
                    }
                }

                segment.Status = RecordingStatus.DiskSpaceReclaimed;
                await _recordingRepository.UpdateAsync(segment, ct);
                deletedRecordingsCount++;

                usage = await GetStorageUsageAsync(ct);
            }
        }

        // 3. Clean up empty recording directories
        CleanEmptyDirectories(ActivityMonitorPaths.RecordingsDirectory);

        // 4. Activity retention
        var activityCutoff = now.AddDays(-_storageOptions.ActivityRetentionDays);
        var purgedActivities = await _activityRepository.PurgeOlderThanAsync(activityCutoff, ct);
        if (purgedActivities > 0)
        {
            _logger.LogInformation("Purged {Count} ActivityEvents older than {Cutoff}", purgedActivities, activityCutoff);
        }

        // 5. Old reports retention
        var reportCutoff = now.AddDays(-_storageOptions.ReportRetentionDays);
        DeleteOldFiles(ActivityMonitorPaths.ReportsDirectory, reportCutoff);

        // 6. Old logs retention
        var logCutoff = now.AddDays(-_storageOptions.LogRetentionDays);
        DeleteOldFiles(ActivityMonitorPaths.LogsDirectory, logCutoff);

        return deletedRecordingsCount;
    }

    public async Task<bool> EnsureDiskSpaceAvailableAsync(CancellationToken ct = default)
    {
        var usage = await GetStorageUsageAsync(ct);
        var minFreeBytes = _storageOptions.MinimumFreeDiskSpaceGB * 1024L * 1024L * 1024L;

        if (usage.FreeDiskSpaceBytes < minFreeBytes)
        {
            _logger.LogWarning("Free disk space {FreeGB:F2} GB is below minimum required {MinGB} GB. Attempting retention purge.",
                usage.FreeDiskSpaceGB, _storageOptions.MinimumFreeDiskSpaceGB);

            await EnforceRetentionPoliciesAsync(ct);
            usage = await GetStorageUsageAsync(ct);
        }

        return usage.FreeDiskSpaceBytes >= minFreeBytes;
    }

    private static long CalculateDirectorySize(string directoryPath)
    {
        if (!Directory.Exists(directoryPath)) return 0;

        try
        {
            var di = new DirectoryInfo(directoryPath);
            return di.EnumerateFiles("*", SearchOption.AllDirectories).Sum(fi => fi.Length);
        }
        catch
        {
            return 0;
        }
    }

    private static void DeleteOldFiles(string directoryPath, DateTime cutoffUtc)
    {
        if (!Directory.Exists(directoryPath)) return;

        try
        {
            var di = new DirectoryInfo(directoryPath);
            foreach (var fi in di.EnumerateFiles("*", SearchOption.AllDirectories))
            {
                if (fi.LastWriteTimeUtc < cutoffUtc)
                {
                    try { fi.Delete(); } catch { }
                }
            }
        }
        catch { }
    }

    private static void CleanEmptyDirectories(string directoryPath)
    {
        if (!Directory.Exists(directoryPath)) return;

        try
        {
            foreach (var subDir in Directory.GetDirectories(directoryPath))
            {
                CleanEmptyDirectories(subDir);
                if (!Directory.EnumerateFileSystemEntries(subDir).Any())
                {
                    try { Directory.Delete(subDir, false); } catch { }
                }
            }
        }
        catch { }
    }
}
