using System;

namespace ActivityMonitor.Core.Models;

/// <summary>
/// يمثل الملخص الإحصائي المجمع لأنشطة المستخدم خلال يوم كامل.
/// </summary>
public class DailyActivitySummary
{
    /// <summary>
    /// التاريخ الذي يمثله هذا الملخص.
    /// </summary>
    public DateTime Date { get; set; }

    /// <summary>
    /// اسم أو معرّف الجهاز المسجل عليه النشاط.
    /// </summary>
    public string MachineId { get; set; } = string.Empty;

    /// <summary>
    /// معرّف أو اسم المستخدم.
    /// </summary>
    public string UserIdentifier { get; set; } = string.Empty;

    /// <summary>
    /// إجمالي وقت النشاط الفعلي (Active Time) أمام البرامج.
    /// </summary>
    public TimeSpan TotalActiveTime { get; set; }

    /// <summary>
    /// إجمالي وقت الخمول (Idle Time) الذي قضاه المستخدم دون مدخلات.
    /// </summary>
    public TimeSpan TotalIdleTime { get; set; }

    /// <summary>
    /// توقيت تسجيل أول نشاط خلال هذا اليوم (UTC).
    /// </summary>
    public DateTime? FirstActivityUtc { get; set; }

    /// <summary>
    /// توقيت تسجيل آخر نشاط خلال هذا اليوم (UTC).
    /// </summary>
    public DateTime? LastActivityUtc { get; set; }

    /// <summary>
    /// عدد التطبيقات المختلفة والفريدة التي تم استخدامها في اليوم.
    /// </summary>
    public int UniqueApplicationsCount { get; set; }

    /// <summary>
    /// العدد الإجمالي لأحداث الأنشطة المسجلة في اليوم.
    /// </summary>
    public int TotalEventsCount { get; set; }

    /// <summary>
    /// عدد مقاطع تسجيل الفيديو التي تم إنشاؤها خلال اليوم.
    /// </summary>
    public int RecordingSegmentsCount { get; set; }

    /// <summary>
    /// إجمالي حجم ملفات تسجيل الفيديو لهذا اليوم مقاساً بالبايت.
    /// </summary>
    public long TotalRecordingSizeBytes { get; set; }
}
