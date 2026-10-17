using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ActivityMonitor.Core.Entities;
using ActivityMonitor.Core.Interfaces;
using ActivityMonitor.Core.Models;
using ActivityMonitor.Core.Options;
using Microsoft.Extensions.Logging;

namespace ActivityMonitor.Infrastructure.Services;

/// <summary>
/// خدمة مسؤولة عن مطابقة وتحديد موقع مقاطع الفيديو المرتبطة بأحداث الأنشطة،
/// وحساب نقطة الإزاحة الزمنية (Offset) داخل الفيديو بدقة، وإعادة ربط الأنشطة غير المرتبطة.
/// </summary>
public class RecordingLocatorService : IRecordingLocatorService
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
    /// مسجل السجلات.
    /// </summary>
    private readonly ILogger<RecordingLocatorService> _logger;
    private readonly SemaphoreSlim _linkGate = new(1, 1);
    private long _linkCursor;

    /// <summary>
    /// منشئ الخدمة.
    /// </summary>
    /// <param name="recordingRepository">مستودع مقاطع التسجيل.</param>
    /// <param name="activityRepository">مستودع أحداث الأنشطة.</param>
    /// <param name="logger">مسجل العمليات.</param>
    public RecordingLocatorService(
        IRecordingRepository recordingRepository,
        IActivityRepository activityRepository,
        ILogger<RecordingLocatorService> logger)
    {
        _recordingRepository = recordingRepository;
        _activityRepository = activityRepository;
        _logger = logger;
    }

    /// <summary>
    /// العثور على مقاطع الفيديو المتزامنة مع حدث نشاط معين وحساب نقطة البداية والمدة داخل الفيديو.
    /// </summary>
    /// <param name="activityEvent">حدث النشاط المستهدف.</param>
    /// <param name="ct">رمز الإلغاء.</param>
    /// <returns>قائمة بنتائج التحديد تتضمن مسارات الملفات ونقاط الإزاحة.</returns>
    public async Task<List<RecordingLocationResult>> LocateAsync(ActivityEvent activityEvent, CancellationToken ct = default)
    {
        var end = activityEvent.EndedAtUtc ?? activityEvent.StartedAtUtc.AddSeconds(Math.Max(1, activityEvent.DurationSeconds));
        if (end <= activityEvent.StartedAtUtc) end = activityEvent.StartedAtUtc.AddTicks(1);
        var segments = await _recordingRepository.GetSegmentsCoveringTimeRangeAsync(
            activityEvent.StartedAtUtc, end, activityEvent.MachineId, ct, activityEvent.SessionId);
        var results = RecordingTimeline.Resolve(activityEvent, segments, DateTime.UtcNow);
        foreach (var result in results) result.FileExists = IsSafeExistingRecording(result.FilePath);
        return results;
    }

    /// <summary>
    /// العثور على مقاطع الفيديو المتزامنة مع طابع زمني محدد وفترة معينة دون الحاجة لكائن نشاط.
    /// </summary>
    /// <param name="timestampUtc">الطابع الزمني بالتوقيت العالمي.</param>
    /// <param name="duration">المدة الزمنية المطلوب البحث فيها (اختياري).</param>
    /// <param name="machineId">معرّف الجهاز (اختياري).</param>
    /// <param name="ct">رمز الإلغاء.</param>
    /// <returns>قائمة بنتائج مواقع التسجيل.</returns>
    public async Task<List<RecordingLocationResult>> LocateByTimestampAsync(DateTime timestampUtc, TimeSpan? duration = null, string? machineId = null, CancellationToken ct = default, int? sessionId = null)
    {
        var length = duration ?? TimeSpan.FromSeconds(1);
        if (length <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(duration));
        var segments = await _recordingRepository.GetSegmentsCoveringTimeRangeAsync(timestampUtc, timestampUtc.Add(length), machineId, ct, sessionId);
        var results = new List<RecordingLocationResult>();
        foreach (var group in segments.GroupBy(s => (s.MachineId, s.SessionId)))
        {
            var activity = new ActivityEvent { MachineId = group.Key.MachineId, SessionId = group.Key.SessionId,
                StartedAtUtc = timestampUtc, EndedAtUtc = timestampUtc.Add(length) };
            results.AddRange(RecordingTimeline.Resolve(activity, group, DateTime.UtcNow));
        }
        foreach (var result in results) result.FileExists = IsSafeExistingRecording(result.FilePath);
        return results.OrderBy(r => r.Offset).ToList();
    }

    /// <summary>
    /// فحص الأنشطة التي تفتقر لمعرّف مقطع الفيديو ومطابقتها بمقاطع الفيديو وتحديث السجلات في قاعدة البيانات.
    /// </summary>
    /// <param name="beforeUtc">الحد الزمني الأعلى للأنشطة المعلقة.</param>
    /// <param name="ct">رمز الإلغاء.</param>
    /// <returns>عدد الأنشطة التي تم ربطها بنجاح.</returns>
    public async Task<int> LinkUnlinkedActivitiesAsync(DateTime beforeUtc, CancellationToken ct = default)
    {
        await _linkGate.WaitAsync(ct);
        try
        {
            var linked = 0;
            for (var page = 0; page < 20; page++)
            {
                var batch = await _activityRepository.GetUnlinkedActivitiesAsync(beforeUtc, ct, _linkCursor);
                if (batch.Count == 0) { _linkCursor = 0; break; }
                foreach (var activity in batch)
                {
                    ct.ThrowIfCancellationRequested();
                    var locations = await LocateAsync(activity, ct);
                    if (locations.Count > 0 && await _activityRepository.SetRecordingSegmentIfMissingAsync(activity.Id, locations[0].RecordingSegmentId, ct)) linked++;
                }
                var next = batch.Max(a => a.Id);
                if (next <= _linkCursor || batch.Count < 500) { _linkCursor = 0; break; }
                _linkCursor = next;
            }
            return linked;
        }
        finally { _linkGate.Release(); }
    }

    /// <summary>
    /// التحقق من أن مسار ملف التسجيل يقع بأمان داخل مجلد التسجيلات المسموح به وأنه موجود فعلياً على القرص.
    /// </summary>
    /// <param name="candidatePath">المسار المرشح للفحص.</param>
    /// <returns>القيمة true إذا كان المسار آمناً وموجوداً؛ وإلا false.</returns>
    private static bool IsSafeExistingRecording(string candidatePath) =>
        StoragePathGuard.TryResolvePathWithinRoot(
            candidatePath,
            ActivityMonitorPaths.RecordingsDirectory,
            out var safePath,
            out _) &&
        File.Exists(safePath);
}
