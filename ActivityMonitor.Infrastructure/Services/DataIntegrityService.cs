using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ActivityMonitor.Core.Enums;
using ActivityMonitor.Core.Interfaces;
using ActivityMonitor.Core.Models;
using ActivityMonitor.Core.Options;
using ActivityMonitor.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ActivityMonitor.Infrastructure.Services;

/// <summary>
/// خدمة مسؤولة عن فحص وتدقيق سلامة البيانات والتسجيلات وإصلاح التلفيات،
/// مثل المقاطع التي ظلت معلقة بحالة قيد التسجيل دون إغلاق بعد إيقاف التشغيل، أو المقاطع المحذوفة من القرص.
/// </summary>
public sealed class DataIntegrityService : IDataIntegrityService
{
    /// <summary>
    /// مصنع سياق قاعدة البيانات.
    /// </summary>
    private readonly IDbContextFactory<ActivityMonitorDbContext> _contextFactory;

    /// <summary>
    /// مراقب خيارات تسجيل الشاشة.
    /// </summary>
    private readonly IOptionsMonitor<RecordingOptions> _recordingOptions;

    /// <summary>
    /// مسجل السجلات التشخيصية.
    /// </summary>
    private readonly ILogger<DataIntegrityService> _logger;

    /// <summary>
    /// منشئ الخدمة.
    /// </summary>
    /// <param name="contextFactory">مصنع سياق قاعدة البيانات.</param>
    /// <param name="recordingOptions">خيارات التسجيل.</param>
    /// <param name="logger">مسجل العمليات.</param>
    public DataIntegrityService(
        IDbContextFactory<ActivityMonitorDbContext> contextFactory,
        IOptionsMonitor<RecordingOptions> recordingOptions,
        ILogger<DataIntegrityService> logger)
    {
        _contextFactory = contextFactory;
        _recordingOptions = recordingOptions;
        _logger = logger;
    }

    /// <summary>
    /// إجراء فحص تدقيقي شامل وتصحيح التناقضات بين ملفات القرص وقاعدة البيانات.
    /// </summary>
    /// <param name="ct">رمز الإلغاء.</param>
    /// <returns>ملخص لنتائج التدقيق والإصلاحات المطبقة.</returns>
    public async Task<DataIntegritySummary> AuditAndRepairAsync(CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var legacyCutoff = now.AddMinutes(-Math.Max(10, _recordingOptions.CurrentValue.SegmentMinutes * 2 + 5));
        var recovered = 0; var missing = 0; var unsafePaths = 0;
        long cursor = 0;
        while (true)
        {
            await using var context = await _contextFactory.CreateDbContextAsync(ct);
            var batch = await context.RecordingSegments.AsNoTracking()
                .Where(s => s.Id > cursor && (s.Status == RecordingStatus.InProgress ||
                    s.Status == RecordingStatus.Completed || s.Status == RecordingStatus.Corrupted || s.Status == RecordingStatus.Recovered))
                .OrderBy(s => s.Id).Take(200).ToListAsync(ct);
            if (batch.Count == 0) break;
            foreach (var segment in batch)
            {
                ct.ThrowIfCancellationRequested();
                cursor = segment.Id;
                if (!StoragePathGuard.TryResolvePathWithinRoot(segment.FilePath, ActivityMonitorPaths.RecordingsDirectory, out var path, out var reason))
                {
                    unsafePaths++;
                    _logger.LogWarning("Skipped recording {Id}: {Reason}", segment.Id, reason);
                    continue;
                }
                try
                {
                    if (segment.Status == RecordingStatus.InProgress &&
                        (RecordingOwnership.IsAlive(segment, now) ||
                         (string.IsNullOrEmpty(segment.OwnerInstanceId) && segment.StartedAtUtc >= legacyCutoff))) continue;
                    var expectedStatus = segment.Status;
                    if (!File.Exists(path))
                    {
                        missing += await context.RecordingSegments.Where(s => s.Id == segment.Id && s.Status == expectedStatus)
                            .ExecuteUpdateAsync(setters => setters.SetProperty(s => s.Status, RecordingStatus.Deleted)
                                .SetProperty(s => s.EndedAtUtc, s => s.EndedAtUtc ?? s.HeartbeatAtUtc ?? s.StartedAtUtc), ct);
                        continue;
                    }
                    if (segment.Status != RecordingStatus.InProgress) continue;
                    var file = new FileInfo(path);
                    // Legacy files have no ownership lease: a recent write still protects them.
                    if (string.IsNullOrEmpty(segment.OwnerInstanceId) && file.LastWriteTimeUtc > now.AddMinutes(-2)) continue;
                    var valid = MediaFileInspector.TryReadVideoDuration(path, out var duration);
                    var end = valid ? segment.StartedAtUtc + duration : segment.HeartbeatAtUtc ?? file.LastWriteTimeUtc;
                    if (end < segment.StartedAtUtc) end = segment.StartedAtUtc;
                    var seconds = Math.Max(0, (end - segment.StartedAtUtc).TotalSeconds);
                    var size = file.Length;
                    var status = valid ? RecordingStatus.Recovered : RecordingStatus.Corrupted;
                    recovered += await context.RecordingSegments.Where(s => s.Id == segment.Id && s.Status == expectedStatus && s.HeartbeatAtUtc == segment.HeartbeatAtUtc)
                        .ExecuteUpdateAsync(setters => setters.SetProperty(s => s.Status, status)
                            .SetProperty(s => s.EndedAtUtc, end).SetProperty(s => s.DurationSeconds, seconds)
                            .SetProperty(s => s.FileSizeBytes, size), ct);
                }
                catch (IOException ex) { _logger.LogWarning(ex, "Recording {Id} changed during audit", segment.Id); }
                catch (UnauthorizedAccessException ex) { _logger.LogWarning(ex, "Cannot audit recording {Id}", segment.Id); }
            }
        }
        return new DataIntegritySummary { StaleRecordingsRecovered = recovered,
            MissingRecordingsMarkedDeleted = missing, UnsafePathsSkipped = unsafePaths };
    }
}
