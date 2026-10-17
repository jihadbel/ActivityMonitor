using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ActivityMonitor.Core.Entities;
using ActivityMonitor.Core.Enums;
using ActivityMonitor.Core.Interfaces;
using ActivityMonitor.Core.Models;
using ActivityMonitor.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ActivityMonitor.Infrastructure.Repositories;

/// <summary>
/// مستودع إدارة وتخزين واستعلام أحداث الأنشطة وتتبع النوافذ في قاعدة بيانات SQLite.
/// </summary>
public class ActivityRepository : IActivityRepository
{
    /// <summary>
    /// مصنع إنشاء سياق قاعدة البيانات لتنفيذ كل عملية بسياق مستقل وآمن عبر الخيوط.
    /// </summary>
    private readonly IDbContextFactory<ActivityMonitorDbContext> _contextFactory;

    /// <summary>
    /// مسجل السجلات لتسجيل الأخطاء والعمليات.
    /// </summary>
    private readonly ILogger<ActivityRepository> _logger;

    /// <summary>
    /// منشئ المستودع.
    /// </summary>
    /// <param name="contextFactory">مصنع سياق قاعدة البيانات.</param>
    /// <param name="logger">مسجل السجلات.</param>
    public ActivityRepository(IDbContextFactory<ActivityMonitorDbContext> contextFactory, ILogger<ActivityRepository> logger)
    {
        _contextFactory = contextFactory;
        _logger = logger;
    }

    /// <summary>
    /// إضافة حدث نشاط جديد وحفظه في قاعدة البيانات.
    /// </summary>
    /// <param name="activityEvent">كائن حدث النشاط.</param>
    /// <param name="ct">رمز الإلغاء.</param>
    /// <returns>كائن حدث النشاط بعد الحفظ وتوليد المفتاح الأساسي.</returns>
    public async Task<ActivityEvent> AddAsync(ActivityEvent activityEvent, CancellationToken ct = default)
    {
        try
        {
            await using var context = await _contextFactory.CreateDbContextAsync(ct);
            context.ActivityEvents.Add(activityEvent);
            await context.SaveChangesAsync(ct);
            return activityEvent;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to persist ActivityEvent {ProcessName} ({StartedAtUtc})", activityEvent.ProcessName, activityEvent.StartedAtUtc);
            throw;
        }
    }

    /// <summary>
    /// إضافة مجموعة من أحداث الأنشطة دفعة واحدة.
    /// </summary>
    /// <param name="activityEvents">قائمة أحداث النشاط.</param>
    /// <param name="ct">رمز الإلغاء.</param>
    public async Task AddRangeAsync(IEnumerable<ActivityEvent> activityEvents, CancellationToken ct = default)
    {
        var list = activityEvents.ToList();
        if (list.Count == 0) return;

        try
        {
            await using var context = await _contextFactory.CreateDbContextAsync(ct);
            await context.ActivityEvents.AddRangeAsync(list, ct);
            await context.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to persist batch of {Count} ActivityEvents", list.Count);
            throw;
        }
    }

    /// <summary>
    /// تحديث بيانات حدث نشاط موجود مسبقاً (مع الحفاظ على ربط مقطع الفيديو إن كان مرتبطاً مسبقاً).
    /// </summary>
    /// <param name="activityEvent">كائن حدث النشاط المعدل.</param>
    /// <param name="ct">رمز الإلغاء.</param>
    public async Task UpdateAsync(ActivityEvent activityEvent, CancellationToken ct = default)
    {
        try
        {
            await using var context = await _contextFactory.CreateDbContextAsync(ct);

            // استرجاع معرّف مقطع التسجيل المحفوظ حالياً لعدم فقدانه في حال كان الكائن الحالي خالياً منه
            var persistedRecordingId = await context.ActivityEvents
                .AsNoTracking()
                .Where(existing => existing.Id == activityEvent.Id)
                .Select(existing => existing.RecordingSegmentId)
                .FirstOrDefaultAsync(ct);

            activityEvent.RecordingSegmentId ??= persistedRecordingId;
            context.Entry(activityEvent).State = EntityState.Modified;
            await context.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update ActivityEvent {Id}", activityEvent.Id);
            throw;
        }
    }

    /// <summary>
    /// ربط حدث النشاط بمقطع فيديو إذا لم يكن مرتبطاً بعد عبر استعلام تحديث مباشر فعال.
    /// </summary>
    /// <param name="activityEventId">معرّف حدث النشاط.</param>
    /// <param name="recordingSegmentId">معرّف مقطع الفيديو.</param>
    /// <param name="ct">رمز الإلغاء.</param>
    /// <returns>القيمة true إذا تم التحديث والربط بنجاح؛ وإلا false.</returns>
    public async Task<bool> SetRecordingSegmentIfMissingAsync(
        long activityEventId,
        long recordingSegmentId,
        CancellationToken ct = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(ct);
        var affected = await context.ActivityEvents
            .Where(activity => activity.Id == activityEventId && activity.RecordingSegmentId == null)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(activity => activity.RecordingSegmentId, recordingSegmentId),
                ct);
        return affected > 0;
    }

    /// <summary>
    /// استرجاع حدث نشاط معين بواسطة معرّفه الفريد متضمناً تفاصيل مقطع الفيديو المرتبط.
    /// </summary>
    /// <param name="id">معرّف الحدث.</param>
    /// <param name="ct">رمز الإلغاء.</param>
    /// <returns>كائن النشاط أو null.</returns>
    public async Task<ActivityEvent?> GetByIdAsync(long id, CancellationToken ct = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(ct);
        return await context.ActivityEvents
            .Include(a => a.RecordingSegment)
            .FirstOrDefaultAsync(a => a.Id == id, ct);
    }

    /// <summary>
    /// استرجاع قائمة أحداث الأنشطة المتقاطعة مع نطاق زمني محدد لجهاز معين.
    /// </summary>
    /// <param name="startUtc">بداية النطاق الزمني (UTC).</param>
    /// <param name="endUtc">نهاية النطاق الزمني (UTC).</param>
    /// <param name="machineId">معرّف الجهاز (اختياري).</param>
    /// <param name="ct">رمز الإلغاء.</param>
    /// <returns>قائمة أحداث الأنشطة مرتبة زمنياً.</returns>
    public async Task<List<ActivityEvent>> GetByDateRangeAsync(DateTime startUtc, DateTime endUtc, string? machineId = null, CancellationToken ct = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(ct);
        var query = context.ActivityEvents
            .AsNoTracking()
            .Include(a => a.RecordingSegment)
            .Where(a => a.StartedAtUtc < endUtc && (a.EndedAtUtc == null || a.EndedAtUtc > startUtc));

        if (!string.IsNullOrWhiteSpace(machineId))
        {
            query = query.Where(a => a.MachineId == machineId);
        }

        return await query.OrderBy(a => a.StartedAtUtc).ToListAsync(ct);
    }

    /// <summary>
    /// استرجاع الأنشطة التي لم ترتبط بعد بأي مقطع فيديو قبل وقت محدد (بحد أقصى 500 نشاط في الدفعة).
    /// </summary>
    /// <param name="beforeUtc">الحد الزمني الأعلى للتفتيش.</param>
    /// <param name="ct">رمز الإلغاء.</param>
    /// <returns>قائمة بالأنشطة غير المربوطة بمقاطع.</returns>
    public async Task<List<ActivityEvent>> GetUnlinkedActivitiesAsync(DateTime beforeUtc, CancellationToken ct = default, long afterId = 0)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(ct);
        return await context.ActivityEvents.AsNoTracking()
            .Where(a => a.Id > afterId && a.RecordingSegmentId == null && a.StartedAtUtc <= beforeUtc)
            .OrderBy(a => a.Id).Take(500).ToListAsync(ct);
    }

    /// <summary>
    /// تجميع وحساب الملخص اليومي الدقيق لأوقات النشاط والخمول وعدد التطبيقات والتسجيلات ليوم كامل.
    /// </summary>
    /// <param name="dateUtc">التاريخ المطلوب (UTC).</param>
    /// <param name="machineId">معرّف الجهاز (اختياري).</param>
    /// <param name="ct">رمز الإلغاء.</param>
    /// <returns>كائن الملخص اليومي المجمع.</returns>
    public async Task<DailyActivitySummary> GetDailySummaryAsync(DateTime dateUtc, string? machineId = null, CancellationToken ct = default)
    {
        var targetDate = dateUtc.Date;
        var (startOfDay, endOfDay) = ActivityTimelineMath.GetUtcDayRange(dateUtc);

        await using var context = await _contextFactory.CreateDbContextAsync(ct);

        // استعلام أحداث اليوم
        var query = context.ActivityEvents
            .AsNoTracking()
            .Where(a => a.StartedAtUtc < endOfDay && (a.EndedAtUtc == null || a.EndedAtUtc > startOfDay));

        if (!string.IsNullOrWhiteSpace(machineId))
        {
            query = query.Where(a => a.MachineId == machineId);
        }

        var events = await query.ToListAsync(ct);

        // استعلام مقاطع تسجيل اليوم
        var recordingQuery = context.RecordingSegments
            .AsNoTracking()
            .Where(r => r.StartedAtUtc < endOfDay && (r.EndedAtUtc == null || r.EndedAtUtc > startOfDay));

        if (!string.IsNullOrWhiteSpace(machineId))
        {
            recordingQuery = recordingQuery.Where(r => r.MachineId == machineId);
        }

        var recordings = await recordingQuery.ToListAsync(ct);

        // تصنيف الأحداث بين نوافذ نشطة وبدايات خمول
        var activeEvents = events.Where(e => e.EventType == ActivityEventType.ActiveWindow).ToList();
        var idleEvents = events.Where(e => e.EventType == ActivityEventType.IdleStart).ToList();

        // حساب الثواني الفعلية بدقة عبر معادلة التداخل الرياضية
        var totalActiveSeconds = activeEvents.Sum(e => ActivityTimelineMath.CalculateOverlapSeconds(
            e.StartedAtUtc, e.EndedAtUtc, e.DurationSeconds, startOfDay, endOfDay));
        var totalIdleSeconds = idleEvents.Sum(e => ActivityTimelineMath.CalculateOverlapSeconds(
            e.StartedAtUtc, e.EndedAtUtc, e.DurationSeconds, startOfDay, endOfDay));

        var firstActivityUtc = events.Count == 0
            ? (DateTime?)null
            : events.Min(e => e.StartedAtUtc) < startOfDay
                ? startOfDay
                : events.Min(e => e.StartedAtUtc);

        var lastActivityUtc = events.Count == 0
            ? (DateTime?)null
            : events
                .Select(e => e.EndedAtUtc ?? e.StartedAtUtc.AddSeconds(Math.Max(0, e.DurationSeconds)))
                .Select(end => end > endOfDay ? endOfDay : end)
                .Max();

        // حساب حجم التسجيلات المنسوب لهذا اليوم بناءً على نسبة التداخل الزمني
        var recordingBytesWithinDay = recordings.Sum(recording =>
        {
            var recordingEnd = recording.EndedAtUtc ??
                               recording.StartedAtUtc.AddSeconds(Math.Max(0, recording.DurationSeconds));
            var totalSeconds = Math.Max(0, (recordingEnd - recording.StartedAtUtc).TotalSeconds);
            if (totalSeconds <= 0 || recording.FileSizeBytes <= 0) return 0L;
            var overlapSeconds = ActivityTimelineMath.CalculateOverlapSeconds(
                recording.StartedAtUtc,
                recording.EndedAtUtc,
                recording.DurationSeconds,
                startOfDay,
                endOfDay);
            return (long)Math.Round(recording.FileSizeBytes * Math.Min(1, overlapSeconds / totalSeconds));
        });

        return new DailyActivitySummary
        {
            Date = targetDate,
            MachineId = machineId ?? (events.FirstOrDefault()?.MachineId ?? Environment.MachineName),
            UserIdentifier = string.Join(", ", events.Select(e => e.UserIdentifier)
                .Where(user => !string.IsNullOrWhiteSpace(user)).Distinct(StringComparer.OrdinalIgnoreCase)
                .DefaultIfEmpty(Environment.UserName)),
            TotalActiveTime = TimeSpan.FromSeconds(totalActiveSeconds),
            TotalIdleTime = TimeSpan.FromSeconds(totalIdleSeconds),
            FirstActivityUtc = firstActivityUtc,
            LastActivityUtc = lastActivityUtc,
            UniqueApplicationsCount = activeEvents.Select(e => e.ProcessName).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
            TotalEventsCount = events.Count,
            RecordingSegmentsCount = recordings.Count,
            TotalRecordingSizeBytes = recordingBytesWithinDay
        };
    }

    /// <summary>
    /// حساب التطبيقات الأكثر استخداماً خلال اليوم مرتبة تنازلياً حسب المدة والنسبة المئوية.
    /// </summary>
    /// <param name="dateUtc">التاريخ المطلوب (UTC).</param>
    /// <param name="machineId">معرّف الجهاز (اختياري).</param>
    /// <param name="ct">رمز الإلغاء.</param>
    /// <returns>قائمة استخدام التطبيقات مرتبة تنازلياً.</returns>
    public async Task<List<ApplicationUsageSummary>> GetTopApplicationsAsync(DateTime dateUtc, string? machineId = null, CancellationToken ct = default)
    {
        var (startOfDay, endOfDay) = ActivityTimelineMath.GetUtcDayRange(dateUtc);

        await using var context = await _contextFactory.CreateDbContextAsync(ct);
        var query = context.ActivityEvents
            .AsNoTracking()
            .Where(a =>
                a.StartedAtUtc < endOfDay &&
                (a.EndedAtUtc == null || a.EndedAtUtc > startOfDay) &&
                a.EventType == ActivityEventType.ActiveWindow);

        if (!string.IsNullOrWhiteSpace(machineId))
        {
            query = query.Where(a => a.MachineId == machineId);
        }

        var events = await query.ToListAsync(ct);

        // حساب مدد التداخل الدقيقة لكل حدث
        var durations = events.ToDictionary(
            e => e.Id,
            e => ActivityTimelineMath.CalculateOverlapSeconds(
                e.StartedAtUtc, e.EndedAtUtc, e.DurationSeconds, startOfDay, endOfDay));

        var totalSeconds = durations.Values.Sum();
        if (totalSeconds <= 0) totalSeconds = 1;

        // تجميع النتائج حسب اسم التطبيق
        return events
            .GroupBy(e => e.ProcessName, StringComparer.OrdinalIgnoreCase)
            .Select(g =>
            {
                var duration = g.Sum(x => durations[x.Id]);
                return new ApplicationUsageSummary
                {
                    ApplicationName = g.Key,
                    TotalDuration = TimeSpan.FromSeconds(duration),
                    UsageCount = g.Count(),
                    Percentage = Math.Round((duration / totalSeconds) * 100.0, 2)
                };
            })
            .OrderByDescending(a => a.TotalDuration)
            .ToList();
    }

    /// <summary>
    /// حذف وتطهير سجلات الأنشطة القديمة الأسبق من تاريخ حد معين لتحرير المساحة.
    /// </summary>
    /// <param name="cutoffUtc">تاريخ الحد الأقصى للاحتفاظ.</param>
    /// <param name="ct">رمز الإلغاء.</param>
    /// <returns>عدد السجلات المحذوفة.</returns>
    public async Task<int> PurgeOlderThanAsync(DateTime cutoffUtc, CancellationToken ct = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(ct);
        return await context.ActivityEvents
            .Where(a => a.EndedAtUtc != null && a.EndedAtUtc < cutoffUtc)
            .ExecuteDeleteAsync(ct);
    }
}
