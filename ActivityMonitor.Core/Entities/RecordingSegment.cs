using System;
using System.Collections.Generic;
using ActivityMonitor.Core.Enums;

namespace ActivityMonitor.Core.Entities;

/// <summary>
/// يمثل كيان مقطع تسجيل الفيديو (Recording Segment) لشاشة المستخدم،
/// متضمناً مسار الملف على القرص، حجمه، حالته، والفترة الزمنية المسجلة.
/// </summary>
public class RecordingSegment
{
    public string UserIdentifier { get; set; } = string.Empty;
    public string OwnerInstanceId { get; set; } = string.Empty;
    public int OwnerProcessId { get; set; }
    public DateTime? OwnerProcessStartedAtUtc { get; set; }
    public DateTime? HeartbeatAtUtc { get; set; }
    /// <summary>
    /// المعرّف الفريد لمقطع التسجيل في قاعدة البيانات (المفتاح الأساسي).
    /// </summary>
    public long Id { get; set; }

    /// <summary>
    /// اسم أو معرّف الجهاز الذي تم إجراء التسجيل عليه.
    /// </summary>
    public string MachineId { get; set; } = string.Empty;

    /// <summary>
    /// معرّف جلسة المستخدم لنظام ويندوز المرتبطة بالتسجيل.
    /// </summary>
    public int SessionId { get; set; }

    /// <summary>
    /// وقت بدء تسجيل مقطع الفيديو بالتوقيت العالمي المنسق (UTC).
    /// </summary>
    public DateTime StartedAtUtc { get; set; }

    /// <summary>
    /// وقت انتهاء تسجيل مقطع الفيديو بالتوقيت العالمي المنسق (UTC).
    /// </summary>
    public DateTime? EndedAtUtc { get; set; }

    /// <summary>
    /// المدة الإجمالية للفيديو المسجل بالثواني.
    /// </summary>
    public double DurationSeconds { get; set; }

    /// <summary>
    /// المسار الكامل لملف الفيديو المخزن على القرص الصلب.
    /// </summary>
    public string FilePath { get; set; } = string.Empty;

    /// <summary>
    /// اسم ملف الفيديو (مثال: 2026-10-10_12-00-00.mp4).
    /// </summary>
    public string FileName { get; set; } = string.Empty;

    /// <summary>
    /// حجم ملف الفيديو على القرص مقاساً بالبايت.
    /// </summary>
    public long FileSizeBytes { get; set; }

    /// <summary>
    /// حالة مقطع التسجيل (قيد التسجيل، مكتمل، تالف، محذوف، إلخ).
    /// </summary>
    public RecordingStatus Status { get; set; } = RecordingStatus.InProgress;

    /// <summary>
    /// وقت إنشاء السجل في قاعدة البيانات بالتوقيت العالمي المنسق (UTC).
    /// </summary>
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// قائمة الأنشطة ونوافذ البرامج التي حدثت وتزامنت أثناء هذا المقطع.
    /// </summary>
    public ICollection<ActivityEvent> Activities { get; set; } = new List<ActivityEvent>();
}
