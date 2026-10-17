using System;

namespace ActivityMonitor.Core.Models;

/// <summary>
/// يمثل نتيجة العثور على موقع تسجيل فيديو لحدث نشاط معين مع نقطة الإزاحة داخل الفيديو.
/// </summary>
public class RecordingLocationResult
{
    /// <summary>
    /// معرّف حدث النشاط المرتبط.
    /// </summary>
    public long ActivityEventId { get; set; }

    /// <summary>
    /// معرّف مقطع تسجيل الفيديو في قاعدة البيانات.
    /// </summary>
    public long RecordingSegmentId { get; set; }

    /// <summary>
    /// المسار الكامل لملف الفيديو على القرص.
    /// </summary>
    public string FilePath { get; set; } = string.Empty;

    /// <summary>
    /// اسم ملف الفيديو.
    /// </summary>
    public string FileName { get; set; } = string.Empty;

    /// <summary>
    /// الإزاحة الزمنية (الوقت من بداية الفيديو حتى لحظة وقوع الحدث).
    /// </summary>
    public TimeSpan Offset { get; set; }

    /// <summary>
    /// مدة النشاط داخل هذا المقطع.
    /// </summary>
    public TimeSpan Duration { get; set; }

    /// <summary>
    /// هل ملف الفيديو موجود ومتاح بالفعل على القرص الصلب حالياً.
    /// </summary>
    public bool FileExists { get; set; }
}
