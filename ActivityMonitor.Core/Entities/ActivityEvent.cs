using System;
using ActivityMonitor.Core.Enums;

namespace ActivityMonitor.Core.Entities;

/// <summary>
/// يمثل كيان حدث النشاط (Activity Event) المسجل في النظام،
/// متضمناً تفاصيل النافذة النشطة، العملية، التوقيت، وحالة الخمول أو الجلسة.
/// </summary>
public class ActivityEvent
{
    /// <summary>
    /// المعرّف الفريد للحدث في قاعدة البيانات (المفتاح الأساسي).
    /// </summary>
    public long Id { get; set; }

    /// <summary>
    /// اسم أو معرّف الجهاز (Machine Name) الذي تم تسجيل النشاط عليه.
    /// </summary>
    public string MachineId { get; set; } = string.Empty;

    /// <summary>
    /// معرّف جلسة تسجيل دخول المستخدم (Session ID).
    /// </summary>
    public int SessionId { get; set; }

    /// <summary>
    /// اسم المستخدم أو معرّفه في نظام التشغيل (User Name).
    /// </summary>
    public string UserIdentifier { get; set; } = string.Empty;

    /// <summary>
    /// اسم العملية البرمجية النشطة (مثل chrome, devenv, explorer).
    /// </summary>
    public string ProcessName { get; set; } = string.Empty;

    /// <summary>
    /// المعرّف الرقمي للعملية في النظام (PID).
    /// </summary>
    public int ProcessId { get; set; }

    /// <summary>
    /// المسار الكامل للملف التنفيذي للبرنامج على القرص.
    /// </summary>
    public string? ProcessPath { get; set; }

    /// <summary>
    /// نص عنوان شريط النافذة النشطة (Window Title).
    /// </summary>
    public string? WindowTitle { get; set; }

    /// <summary>
    /// سياق العمل الإضافي للتطبيق (مثل مسار المجلد المفتوح أو الرابط أو الملف).
    /// </summary>
    public string? ContextPath { get; set; }

    /// <summary>
    /// نوع حدث النشاط (نافذة نشطة، بدء خمول، قفل شاشة، إلخ).
    /// </summary>
    public ActivityEventType EventType { get; set; } = ActivityEventType.ActiveWindow;

    /// <summary>
    /// وقت بدء النشاط بالتوقيت العالمي المنسق (UTC).
    /// </summary>
    public DateTime StartedAtUtc { get; set; }

    /// <summary>
    /// وقت انتهاء النشاط بالتوقيت العالمي المنسق (UTC)، أو فارغ إن كان مستمراً.
    /// </summary>
    public DateTime? EndedAtUtc { get; set; }

    /// <summary>
    /// المدة الإجمالية للنشاط بالثواني.
    /// </summary>
    public double DurationSeconds { get; set; }

    /// <summary>
    /// معرّف مقطع تسجيل الفيديو المرتبط بهذا النشاط (مفتاح خارجي اختياري).
    /// </summary>
    public long? RecordingSegmentId { get; set; }

    /// <summary>
    /// وقت إنشاء السجل في قاعدة البيانات بالتوقيت العالمي المنسق (UTC).
    /// </summary>
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// خاصية التنقل (Navigation Property) لمقطع الفيديو المرتبط بالنشاط.
    /// </summary>
    public RecordingSegment? RecordingSegment { get; set; }
}
