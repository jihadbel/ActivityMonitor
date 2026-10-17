using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ActivityMonitor.Core.Entities;
using ActivityMonitor.Core.Enums;
using ActivityMonitor.Core.Interfaces;
using ActivityMonitor.Core.Models;
using ActivityMonitor.Core.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ActivityMonitor.Infrastructure.Services;

/// <summary>
/// خدمة مسؤولة عن إدارة استهلاك السعة التخزينية، مراقبة المساحة المتاحة على القرص،
/// تطبيق سياسات استبقاء وحذف البيانات والتسجيلات القديمة، وحذف المجلدات الفارغة بأمان.
/// </summary>
public class StorageManagementService : IStorageManagementService
{
    /// <summary>
    /// مستودع مقاطع التسجيل.
    /// </summary>
    private readonly IRecordingRepository _recordingRepository;

    /// <summary>
    /// مستودع أحداث الأنشطة.
    /// </summary>
    private readonly IActivityRepository _activityRepository;

    /// <summary>
    /// مراقب خيارات وسياسات التخزين للتحديث المباشر.
    /// </summary>
    private readonly IOptionsMonitor<StorageOptions> _storageOptionsMonitor;

    /// <summary>
    /// قراءة الإعدادات الحالية لخيارات التخزين.
    /// </summary>
    private StorageOptions _storageOptions => _storageOptionsMonitor.CurrentValue;

    /// <summary>
    /// مسجل السجلات التشخيصية.
    /// </summary>
    private readonly ILogger<StorageManagementService> _logger;

    /// <summary>
    /// منشئ الخدمة.
    /// </summary>
    /// <param name="recordingRepository">مستودع مقاطع التسجيل.</param>
    /// <param name="activityRepository">مستودع أحداث الأنشطة.</param>
    /// <param name="storageOptionsMonitor">مراقب خيارات التخزين.</param>
    /// <param name="logger">مسجل العمليات.</param>
    public StorageManagementService(
        IRecordingRepository recordingRepository,
        IActivityRepository activityRepository,
        IOptionsMonitor<StorageOptions> storageOptionsMonitor,
        ILogger<StorageManagementService> logger)
    {
        _recordingRepository = recordingRepository;
        _activityRepository = activityRepository;
        _storageOptionsMonitor = storageOptionsMonitor;
        _logger = logger;
    }

    /// <summary>
    /// حساب ملخص استهلاك السعة التخزينية لقاعدة البيانات، التسجيلات، التقارير، السجلات، والمساحة الحرة للقرص.
    /// </summary>
    /// <param name="ct">رمز الإلغاء.</param>
    /// <returns>كائن ملخص استهلاك التخزين.</returns>
    public Task<StorageUsageSummary> GetStorageUsageAsync(CancellationToken ct = default)
    {
        // التأكد من وجود مسارات المجلدات الأساسية
        ActivityMonitorPaths.EnsureDirectoriesExist();

        long dbSize = 0;
        bool storageSizeKnown = true;
        try
        {
            // حساب حجم ملف قاعدة البيانات الرئيسي
            if (File.Exists(ActivityMonitorPaths.DatabasePath))
            {
                var fi = new FileInfo(ActivityMonitorPaths.DatabasePath);
                dbSize += fi.Length;

                // شمول ملفات سجل التعديلات WAL وملفات الذاكرة المشتركة SHM في الحجم
                var walPath = ActivityMonitorPaths.DatabasePath + "-wal";
                if (File.Exists(walPath)) dbSize += new FileInfo(walPath).Length;

                var shmPath = ActivityMonitorPaths.DatabasePath + "-shm";
                if (File.Exists(shmPath)) dbSize += new FileInfo(shmPath).Length;
            }
        }
        catch (Exception ex)
        {
            storageSizeKnown = false;
            _logger.LogWarning(ex, "Could not determine database storage usage");
        }

        if (TryCalculateDirectorySize(Path.Combine(ActivityMonitorPaths.DataDirectory, "Backups"), out var backupBytes)) dbSize += backupBytes;
        else storageSizeKnown = false;

        // حساب أحجام مجلدات التسجيلات والتقارير والسجلات
        storageSizeKnown &= TryCalculateDirectorySize(ActivityMonitorPaths.RecordingsDirectory, out var recordingsSize);
        storageSizeKnown &= TryCalculateDirectorySize(ActivityMonitorPaths.ReportsDirectory, out var reportsSize);
        storageSizeKnown &= TryCalculateDirectorySize(ActivityMonitorPaths.LogsDirectory, out var logsSize);

        long freeDiskSpace = 0;
        long totalDiskSpace = 0;
        bool diskSpaceKnown = false;

        try
        {
            // استعلام نظام التشغيل عن مساحة محرك الأقراص
            var driveInfo = new DriveInfo(Path.GetPathRoot(ActivityMonitorPaths.RootDirectory) ?? "C:\\");
            freeDiskSpace = driveInfo.AvailableFreeSpace;
            totalDiskSpace = driveInfo.TotalSize;
            diskSpaceKnown = true;
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
            TotalDiskSpaceBytes = totalDiskSpace,
            IsDiskSpaceKnown = diskSpaceKnown,
            IsStorageSizeKnown = storageSizeKnown
        };

        return Task.FromResult(summary);
    }

    /// <summary>
    /// تطبيق سياسات الاحتفاظ بالبيانات: حذف التسجيلات المنتهية الصلاحية،
    /// وتطهير المقاطع القديمة عند اقتراب امتلاء القرص أو تجاوز الحصة المحددة، وحذف السجلات والتقارير القديمة.
    /// </summary>
    /// <param name="ct">رمز الإلغاء.</param>
    /// <returns>عدد ملفات التسجيل التي تم حذفها لتحرير المساحة.</returns>
    public async Task<int> EnforceRetentionPoliciesAsync(CancellationToken ct = default)
    {
        int deletedRecordingsCount = 0;
        var now = DateTime.UtcNow;

        _logger.LogInformation("Enforcing storage retention policies...");

        // 1. حذف تسجيلات الفيديو التي تجاوزت مدة الاحتفاظ بالأيام (RetentionDays)
        var recordingCutoff = now.AddDays(-_storageOptions.RecordingRetentionDays);
        var oldSegments = await _recordingRepository.GetSegmentsOlderThanAsync(recordingCutoff, ct);

        foreach (var segment in oldSegments)
        {
            ct.ThrowIfCancellationRequested();
            var deletion = await TryDeleteRecordingAsync(segment, RecordingStatus.Deleted, ct);
            if (deletion.StateUpdated)
            {
                deletedRecordingsCount++;
            }
        }

        // 2. فحص تجاوز حصة التخزين المسموحة أو انخفاض مساحة القرص عن الحد الأدنى
        var usage = await GetStorageUsageAsync(ct);
        var maxBytes = _storageOptions.MaxStorageSizeGB * 1024L * 1024L * 1024L;
        var minFreeBytes = _storageOptions.MinimumFreeDiskSpaceGB * 1024L * 1024L * 1024L;

        var exceedsStorageLimit = usage.IsStorageSizeKnown && usage.TotalSizeBytes > maxBytes;
        var isLowDisk = usage.IsDiskSpaceKnown && usage.FreeDiskSpaceBytes < minFreeBytes;

        if (!usage.IsStorageSizeKnown)
        {
            _logger.LogWarning("Storage usage is unknown. Size-based deletion is skipped for safety.");
        }

        if (!usage.IsDiskSpaceKnown)
        {
            _logger.LogWarning("Free disk space is unknown. Free-space deletion is skipped for safety.");
        }

        // في حال تجاوز الحصة أو انخفاض المساحة، نحذف أقدم المقاطع المكتملة تدريجياً
        if (exceedsStorageLimit || isLowDisk)
        {
            _logger.LogWarning("Storage limit exceeded (Total: {TotalMB}MB, FreeDisk: {FreeGB}GB). Purging oldest completed or corrupted recordings...",
                usage.TotalSizeMB, usage.FreeDiskSpaceGB);

            var completedSegments = await _recordingRepository.GetAllCompletedSegmentsAsync(ct);
            foreach (var segment in completedSegments.OrderBy(s => s.StartedAtUtc))
            {
                ct.ThrowIfCancellationRequested();

                exceedsStorageLimit = usage.IsStorageSizeKnown && usage.TotalSizeBytes > maxBytes;
                isLowDisk = usage.IsDiskSpaceKnown && usage.FreeDiskSpaceBytes < minFreeBytes;

                // التوقف بمجرد عودة المساحة للحدود الآمنة
                if (!exceedsStorageLimit && !isLowDisk)
                {
                    break;
                }

                var deletion = await TryDeleteRecordingAsync(segment, RecordingStatus.DiskSpaceReclaimed, ct);
                if (deletion.StateUpdated) deletedRecordingsCount++;

                // تحديث مؤشرات المساحة فورياً
                if (deletion.ReclaimedBytes > 0)
                {
                    usage.RecordingsSizeBytes = Math.Max(0, usage.RecordingsSizeBytes - deletion.ReclaimedBytes);
                    if (usage.IsDiskSpaceKnown)
                    {
                        usage.FreeDiskSpaceBytes += deletion.ReclaimedBytes;
                    }
                }
            }
        }

        // 3. تنظيف المجلدات الفارغة الناتجة عن حذف التسجيلات
        CleanEmptyDirectories(ActivityMonitorPaths.RecordingsDirectory);

        // 4. تطهير سجلات الأنشطة القديمة في قاعدة البيانات
        var activityCutoff = now.AddDays(-_storageOptions.ActivityRetentionDays);
        var purgedActivities = await _activityRepository.PurgeOlderThanAsync(activityCutoff, ct);
        if (purgedActivities > 0)
        {
            _logger.LogInformation("Purged {Count} ActivityEvents older than {Cutoff}", purgedActivities, activityCutoff);
        }

        // 5. حذف ملفات التقارير القديمة
        var reportCutoff = now.AddDays(-_storageOptions.ReportRetentionDays);
        DeleteOldFiles(ActivityMonitorPaths.ReportsDirectory, reportCutoff);

        // 6. حذف ملفات السجلات التشخيصية القديمة
        var logCutoff = now.AddDays(-_storageOptions.LogRetentionDays);
        DeleteOldFiles(ActivityMonitorPaths.LogsDirectory, logCutoff);

        return deletedRecordingsCount;
    }

    /// <summary>
    /// التحقق من توفر مساحة كافية على القرص لبدء التسجيل، ومحاولة تحرير المساحة تلقائياً في حال الحاجة.
    /// </summary>
    /// <param name="ct">رمز الإلغاء.</param>
    /// <returns>القيمة true إذا كانت المساحة المتوفرة كافية ومطابقة للحدود؛ وإلا false.</returns>
    public async Task<bool> EnsureDiskSpaceAvailableAsync(CancellationToken ct = default)
    {
        var usage = await GetStorageUsageAsync(ct);
        var maxBytes = _storageOptions.MaxStorageSizeGB * 1024L * 1024L * 1024L;
        var minFreeBytes = _storageOptions.MinimumFreeDiskSpaceGB * 1024L * 1024L * 1024L;

        // رفض بدء التسجيل إذا تعذر قياس المساحة بصورة مؤكدة لحماية القرص
        if (!usage.IsDiskSpaceKnown || !usage.IsStorageSizeKnown)
        {
            _logger.LogWarning(
                "Cannot safely verify recording capacity (DiskKnown={DiskKnown}, SizeKnown={SizeKnown}). Recording start is denied.",
                usage.IsDiskSpaceKnown,
                usage.IsStorageSizeKnown);
            return false;
        }

        // إذا كانت المساحة منخفضة نقوم بتنفيذ تنظيف فوري لسياسات الاحتفاظ
        if (usage.FreeDiskSpaceBytes < minFreeBytes || usage.TotalSizeBytes > maxBytes)
        {
            _logger.LogWarning(
                "Recording capacity limits require cleanup (Total: {TotalMB:F2} MB, Free: {FreeGB:F2} GB). Attempting retention purge.",
                usage.TotalSizeMB,
                usage.FreeDiskSpaceGB);

            await EnforceRetentionPoliciesAsync(ct);
            usage = await GetStorageUsageAsync(ct);
        }

        return usage.IsDiskSpaceKnown &&
               usage.IsStorageSizeKnown &&
               usage.FreeDiskSpaceBytes >= minFreeBytes &&
               usage.TotalSizeBytes <= maxBytes;
    }

    /// <summary>
    /// حساب حجم المجلد بالبايت بأمان مع تخطي نقاط إعادة التحليل (Reparse Points).
    /// </summary>
    /// <param name="directoryPath">مسار المجلد.</param>
    /// <param name="sizeBytes">الحجم المحسوب بالبايت.</param>
    /// <returns>القيمة true إذا نجح الحساب؛ وإلا false.</returns>
    private bool TryCalculateDirectorySize(string directoryPath, out long sizeBytes)
    {
        sizeBytes = 0;
        if (!Directory.Exists(directoryPath)) return true;

        try
        {
            foreach (var file in EnumerateFilesWithoutReparsePoints(directoryPath))
            {
                sizeBytes = checked(sizeBytes + file.Length);
            }
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not calculate directory size for {DirectoryPath}", directoryPath);
            return false;
        }
    }

    /// <summary>
    /// حذف ملف تسجيل معين وتحديث حالته في المستودع بعد التحقق الأمني من مساره.
    /// </summary>
    /// <param name="segment">كائن المقطع المراد حذفه.</param>
    /// <param name="requestedStatus">الحالة المطلوبة (Deleted أو DiskSpaceReclaimed).</param>
    /// <param name="ct">رمز الإلغاء.</param>
    /// <returns>نتيجة الحذف وعدد البايتات المحررة.</returns>
    private async Task<RecordingDeletionResult> TryDeleteRecordingAsync(
        RecordingSegment segment,
        RecordingStatus requestedStatus,
        CancellationToken ct)
    {
        // التحقق الأمني من أن مسار الملف يقع تماماً داخل مجلد التسجيلات المسموح به
        if (!StoragePathGuard.TryResolvePathWithinRoot(
                segment.FilePath,
                ActivityMonitorPaths.RecordingsDirectory,
                out var safePath,
                out var failureReason))
        {
            _logger.LogError(
                "Refused to delete recording segment {SegmentId}: path '{FilePath}' is unsafe. {Reason}",
                segment.Id,
                segment.FilePath,
                failureReason);
            return default;
        }

        long reclaimedBytes = 0;
        var fileExisted = File.Exists(safePath);

        // حذف الملف الفعلي من القرص
        if (fileExisted)
        {
            try
            {
                reclaimedBytes = new FileInfo(safePath).Length;
                File.Delete(safePath);
                if (File.Exists(safePath))
                {
                    _logger.LogWarning("Recording file still exists after delete request: {FilePath}", safePath);
                    return default;
                }

                _logger.LogInformation(
                    "Deleted recording file {FilePath} ({SizeMB:F2} MB)",
                    safePath,
                    reclaimedBytes / (1024.0 * 1024.0));
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not delete recording file: {FilePath}", safePath);
                return default;
            }
        }

        // تحديث حالة السجل في قاعدة البيانات
        segment.Status = fileExisted ? requestedStatus : RecordingStatus.Deleted;
        try
        {
            await _recordingRepository.UpdateAsync(segment, ct);
            return new RecordingDeletionResult(true, reclaimedBytes);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Recording file state changed but database update failed for segment {SegmentId}", segment.Id);
            return new RecordingDeletionResult(false, reclaimedBytes);
        }
    }

    /// <summary>
    /// هيكل داخلي لتخزين نتائج حذف مقطع الفيديو.
    /// </summary>
    private readonly record struct RecordingDeletionResult(bool StateUpdated, long ReclaimedBytes);

    /// <summary>
    /// حذف الملفات القديمة التي تجاوزت تاريخ حد معين داخل مجلد محدد.
    /// </summary>
    /// <param name="directoryPath">المجلد المستهدف.</param>
    /// <param name="cutoffUtc">تاريخ الحد الأقصى للاحتفاظ.</param>
    private void DeleteOldFiles(string directoryPath, DateTime cutoffUtc)
    {
        if (!Directory.Exists(directoryPath)) return;

        foreach (var file in EnumerateFilesWithoutReparsePoints(directoryPath))
        {
            if (file.LastWriteTimeUtc >= cutoffUtc)
            {
                continue;
            }

            // التحقق الأمني من المسار قبل حذفه
            if (!StoragePathGuard.TryResolvePathWithinRoot(
                    file.FullName,
                    directoryPath,
                    out var safePath,
                    out var failureReason))
            {
                _logger.LogWarning(
                    "Skipped unsafe retention path '{FilePath}'. {Reason}",
                    file.FullName,
                    failureReason);
                continue;
            }

            try { File.Delete(safePath); }
            catch (Exception ex) { _logger.LogWarning(ex, "Could not delete expired file {FilePath}", safePath); }
        }
    }

    /// <summary>
    /// حذف المجلدات الفرعية الفارغة بشكل تعاودي بعد حذف الملفات منها.
    /// </summary>
    /// <param name="directoryPath">المجلد المراد تنظيفه.</param>
    private static void CleanEmptyDirectories(string directoryPath)
    {
        if (!Directory.Exists(directoryPath)) return;

        try
        {
            foreach (var subDir in Directory.GetDirectories(directoryPath))
            {
                // تخطي الروابط الرمزية ونقاط إعادة التحليل
                if ((File.GetAttributes(subDir) & FileAttributes.ReparsePoint) != 0)
                {
                    continue;
                }

                CleanEmptyDirectories(subDir);
                if (!Directory.EnumerateFileSystemEntries(subDir).Any())
                {
                    try { Directory.Delete(subDir, false); } catch { }
                }
            }
        }
        catch { }
    }

    /// <summary>
    /// استعراض الملفات داخل المجلد بشكل تعاودي وتخطي الروابط الرمزية ونقاط إعادة التحليل.
    /// </summary>
    /// <param name="rootDirectory">المجلد الجذري.</param>
    /// <returns>قائمة معلومات الملفات الآمنة.</returns>
    private static IEnumerable<FileInfo> EnumerateFilesWithoutReparsePoints(string rootDirectory)
    {
        var pendingDirectories = new Stack<DirectoryInfo>();
        pendingDirectories.Push(new DirectoryInfo(rootDirectory));

        while (pendingDirectories.Count > 0)
        {
            var directory = pendingDirectories.Pop();
            FileInfo[] files;
            DirectoryInfo[] children;

            try
            {
                if ((directory.Attributes & FileAttributes.ReparsePoint) != 0)
                {
                    continue;
                }

                files = directory.GetFiles();
                children = directory.GetDirectories();
            }
            catch (Exception ex)
            {
                throw new IOException($"Cannot enumerate storage directory {directory.FullName}", ex);
            }

            foreach (var file in files)
            {
                FileAttributes attributes;
                try { attributes = file.Attributes; }
                catch (Exception ex) { throw new IOException($"Cannot inspect storage file {file.FullName}", ex); }

                if ((attributes & FileAttributes.ReparsePoint) == 0)
                {
                    yield return file;
                }
            }

            foreach (var child in children)
            {
                try
                {
                    if ((child.Attributes & FileAttributes.ReparsePoint) == 0)
                    {
                        pendingDirectories.Push(child);
                    }
                }
                catch
                {
                    throw new IOException($"Cannot inspect storage directory {child.FullName}");
                }
            }
        }
    }
}
