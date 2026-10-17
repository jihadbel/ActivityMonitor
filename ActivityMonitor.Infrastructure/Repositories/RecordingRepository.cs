using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ActivityMonitor.Core.Entities;
using ActivityMonitor.Core.Enums;
using ActivityMonitor.Core.Interfaces;
using ActivityMonitor.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ActivityMonitor.Infrastructure.Repositories;

/// <summary>
/// مستودع إدارة وتخزين واستعلام مقاطع تسجيل شاشة الفيديو في قاعدة بيانات SQLite.
/// </summary>
public class RecordingRepository : IRecordingRepository
{
    /// <summary>
    /// مصنع إنشاء سياق قاعدة البيانات المعزول لكل عملية لضمان التزامن السليم.
    /// </summary>
    private readonly IDbContextFactory<ActivityMonitorDbContext> _contextFactory;

    /// <summary>
    /// مسجل السجلات التشخيصية.
    /// </summary>
    private readonly ILogger<RecordingRepository> _logger;

    /// <summary>
    /// منشئ المستودع.
    /// </summary>
    /// <param name="contextFactory">مصنع سياق قاعدة البيانات.</param>
    /// <param name="logger">مسجل العمليات.</param>
    public RecordingRepository(IDbContextFactory<ActivityMonitorDbContext> contextFactory, ILogger<RecordingRepository> logger)
    {
        _contextFactory = contextFactory;
        _logger = logger;
    }

    /// <summary>
    /// إضافة سجل مقطع فيديو جديد إلى قاعدة البيانات.
    /// </summary>
    /// <param name="segment">كائن مقطع التسجيل.</param>
    /// <param name="ct">رمز الإلغاء.</param>
    /// <returns>كائن المقطع بعد الحفظ.</returns>
    public async Task<RecordingSegment> AddAsync(RecordingSegment segment, CancellationToken ct = default)
    {
        try
        {
            await using var context = await _contextFactory.CreateDbContextAsync(ct);
            context.RecordingSegments.Add(segment);
            await context.SaveChangesAsync(ct);
            return segment;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to persist RecordingSegment {FileName}", segment.FileName);
            throw;
        }
    }

    /// <summary>
    /// تحديث بيانات مقطع فيديو مع حماية حالات الحذف واستعادة المساحة من الكتابة فوقها بتحديثات قديمة.
    /// </summary>
    /// <param name="segment">كائن المقطع المعدل.</param>
    /// <param name="ct">رمز الإلغاء.</param>
    public async Task UpdateAsync(RecordingSegment segment, CancellationToken ct = default)
    {
        try
        {
            await using var context = await _contextFactory.CreateDbContextAsync(ct);
            var currentStatus = await context.RecordingSegments
                .AsNoTracking()
                .Where(existing => existing.Id == segment.Id)
                .Select(existing => (RecordingStatus?)existing.Status)
                .FirstOrDefaultAsync(ct);

            if (currentStatus == null)
            {
                throw new InvalidOperationException($"Recording segment {segment.Id} no longer exists.");
            }

            // منع التراجع عن حالات الحذف واستعادة المساحة في حال وصول تحديث قديم متأخر
            if (currentStatus is RecordingStatus.Deleted or RecordingStatus.DiskSpaceReclaimed &&
                segment.Status is RecordingStatus.Completed or RecordingStatus.Recovered or RecordingStatus.Corrupted or RecordingStatus.InProgress)
            {
                _logger.LogWarning(
                    "Ignored stale status transition for segment {Id}: {CurrentStatus} -> {RequestedStatus}",
                    segment.Id,
                    currentStatus,
                    segment.Status);
                return;
            }

            context.Entry(segment).State = EntityState.Modified;
            context.Entry(segment).Property(existing => existing.Status).OriginalValue = currentStatus.Value;
            await context.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update RecordingSegment {Id}", segment.Id);
            throw;
        }
    }

    /// <summary>
    /// استرجاع مقطع تسجيل معين بواسطة معرّفه الفريد.
    /// </summary>
    /// <param name="id">معرّف المقطع.</param>
    /// <param name="ct">رمز الإلغاء.</param>
    /// <returns>كائن المقطع أو null.</returns>
    public async Task<RecordingSegment?> GetByIdAsync(long id, CancellationToken ct = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(ct);
        return await context.RecordingSegments.FirstOrDefaultAsync(r => r.Id == id, ct);
    }

    /// <summary>
    /// استرجاع مقطع الفيديو النشط حالياً (الذي لا يزال بحالة InProgress) لجهاز معين.
    /// </summary>
    /// <param name="machineId">اسم الجهاز.</param>
    /// <param name="ct">رمز الإلغاء.</param>
    /// <returns>المقطع النشط إن وجد أو null.</returns>
    public async Task<RecordingSegment?> GetActiveSegmentAsync(string machineId, CancellationToken ct = default, int? sessionId = null)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(ct);
        return await context.RecordingSegments
            .Where(r => r.MachineId == machineId && r.Status == RecordingStatus.InProgress &&
                        (!sessionId.HasValue || r.SessionId == sessionId.Value))
            .OrderByDescending(r => r.StartedAtUtc)
            .FirstOrDefaultAsync(ct);
    }

    /// <summary>
    /// استرجاع قائمة مقاطع الفيديو الواقعة ضمن نطاق زمني محدد.
    /// </summary>
    /// <param name="startUtc">بداية النطاق (UTC).</param>
    /// <param name="endUtc">نهاية النطاق (UTC).</param>
    /// <param name="machineId">اسم الجهاز (اختياري).</param>
    /// <param name="ct">رمز الإلغاء.</param>
    /// <returns>قائمة مقاطع الفيديو مرتبة زمنياً.</returns>
    public async Task<List<RecordingSegment>> GetByDateRangeAsync(DateTime startUtc, DateTime endUtc, string? machineId = null, CancellationToken ct = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(ct);
        var query = context.RecordingSegments
            .AsNoTracking()
            .Where(r => r.StartedAtUtc < endUtc && (r.EndedAtUtc == null || r.EndedAtUtc > startUtc));

        if (!string.IsNullOrWhiteSpace(machineId))
        {
            query = query.Where(r => r.MachineId == machineId);
        }

        return await query.OrderBy(r => r.StartedAtUtc).ToListAsync(ct);
    }

    /// <summary>
    /// استرجاع المقاطع المكتملة أو الجارية التي تتقاطع زمنياً وتغطي فترة محددة.
    /// </summary>
    /// <param name="startUtc">بداية الفترة (UTC).</param>
    /// <param name="endUtc">نهاية الفترة (UTC).</param>
    /// <param name="machineId">اسم الجهاز (اختياري).</param>
    /// <param name="ct">رمز الإلغاء.</param>
    /// <returns>قائمة المقاطع المتقاطعة زمنياً.</returns>
    public async Task<List<RecordingSegment>> GetSegmentsCoveringTimeRangeAsync(DateTime startUtc, DateTime endUtc, string? machineId = null, CancellationToken ct = default, int? sessionId = null)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(ct);
        var query = context.RecordingSegments
            .AsNoTracking()
            .Where(r => r.Status == RecordingStatus.Completed || r.Status == RecordingStatus.Recovered || r.Status == RecordingStatus.InProgress);

        if (!string.IsNullOrWhiteSpace(machineId))
        {
            query = query.Where(r => r.MachineId == machineId);
        }

        // تقاطع الفترات الزمنية نصف المفتوحة
        query = query.Where(r => r.StartedAtUtc < endUtc && (r.EndedAtUtc == null || r.EndedAtUtc > startUtc));
        if (sessionId.HasValue) query = query.Where(r => r.SessionId == sessionId.Value);

        return await query.OrderBy(r => r.StartedAtUtc).ToListAsync(ct);
    }

    /// <summary>
    /// استرجاع المقاطع القديمة المنتهية قبل تاريخ حد معين لتطبيق سياسات الحذف.
    /// </summary>
    /// <param name="cutoffUtc">تاريخ الحد الأقصى (UTC).</param>
    /// <param name="ct">رمز الإلغاء.</param>
    /// <returns>قائمة المقاطع القديمة المكتملة أو التالفة.</returns>
    public async Task<List<RecordingSegment>> GetSegmentsOlderThanAsync(DateTime cutoffUtc, CancellationToken ct = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(ct);
        return await context.RecordingSegments
            .Where(r => (r.EndedAtUtc ?? r.StartedAtUtc) < cutoffUtc &&
                        (r.Status == RecordingStatus.Completed || r.Status == RecordingStatus.Recovered || r.Status == RecordingStatus.Corrupted))
            .OrderBy(r => r.StartedAtUtc)
            .ToListAsync(ct);
    }

    /// <summary>
    /// استرجاع كافة المقاطع المكتملة أو التالفة المسجلة في النظام.
    /// </summary>
    /// <param name="ct">رمز الإلغاء.</param>
    /// <returns>قائمة المقاطع.</returns>
    public async Task<List<RecordingSegment>> GetAllCompletedSegmentsAsync(CancellationToken ct = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(ct);
        return await context.RecordingSegments
            .Where(r => r.Status == RecordingStatus.Completed || r.Status == RecordingStatus.Recovered || r.Status == RecordingStatus.Corrupted)
            .OrderBy(r => r.StartedAtUtc)
            .ToListAsync(ct);
    }

    /// <summary>
    /// وضع علامة "محذوف" (Deleted) على مقطع التسجيل في قاعدة البيانات.
    /// </summary>
    /// <param name="id">معرّف المقطع.</param>
    /// <param name="ct">رمز الإلغاء.</param>
    /// <returns>عدد السجلات المعدلة (1 في حالة النجاح).</returns>
    public async Task<int> MarkAsDeletedAsync(long id, CancellationToken ct = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(ct);
        var segment = await context.RecordingSegments.FindAsync(new object[] { id }, ct);
        if (segment == null) return 0;

        segment.Status = RecordingStatus.Deleted;
        return await context.SaveChangesAsync(ct);
    }
}
