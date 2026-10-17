using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace ActivityMonitor.Viewer.ViewModels;

/// <summary>
/// نموذج عرض عنصر النشاط (Activity Item ViewModel) لعرض بيانات النافذة أو فترة الخمول في واجهة المستخدم وقائمة الأنشطة.
/// </summary>
public class ActivityItemViewModel
{
    /// <summary>
    /// المعرف الرقمي الفريد لسجل النشاط في قاعدة البيانات.
    /// </summary>
    public long Id { get; set; }

    /// <summary>
    /// التوقيت العالمي (UTC) لبداية حدوث النشاط.
    /// </summary>
    public DateTime StartedAtUtc { get; set; }

    /// <summary>
    /// التوقيت العالمي (UTC) لنهاية حدوث النشاط (أو null إذا كان النشاط ما زال مستمراً).
    /// </summary>
    public DateTime? EndedAtUtc { get; set; }

    /// <summary>
    /// توقيت البداية بالتوقيت المحلي للمستخدم.
    /// </summary>
    public DateTime StartTimeLocal => StartedAtUtc.ToLocalTime();

    /// <summary>
    /// توقيت النهاية بالتوقيت المحلي للمستخدم.
    /// </summary>
    public DateTime EndTimeLocal => (EndedAtUtc ?? StartedAtUtc).ToLocalTime();

    /// <summary>
    /// النص المنسق لنطاق الوقت المعروض في القائمة (مثال: 14:30:00 - 14:45:12).
    /// </summary>
    public string TimeRangeDisplay => $"{StartTimeLocal:HH:mm:ss} - {EndTimeLocal:HH:mm:ss}";

    /// <summary>
    /// اسم العملية أو التطبيق المقترن بالنشاط (مثل chrome, devenv, explorer).
    /// </summary>
    public string ProcessName { get; set; } = string.Empty;

    /// <summary>
    /// عنوان النافذة النشطة في لحظة الرصد.
    /// </summary>
    public string WindowTitle { get; set; } = string.Empty;

    /// <summary>
    /// المسار السياقي الإضافي (كالمسار المفتوح في مستكشف الملفات أو الرابط).
    /// </summary>
    public string? ContextPath { get; set; }

    /// <summary>
    /// المسار الكامل للملف التنفيذي للتطبيق على القرص.
    /// </summary>
    public string? ProcessPath { get; set; }

    /// <summary>
    /// نوع الحدث (نافذة نشطة ActiveWindow أو بدء خمول IdleStart).
    /// </summary>
    public string EventType { get; set; } = "ActiveWindow";

    /// <summary>
    /// المدة الإجمالية للنشاط بالثواني.
    /// </summary>
    public double DurationSeconds { get; set; }

    /// <summary>
    /// العرض النصي المنسق لمدة النشاط بالدقائق والثواني والساعات (مثال: 1س 20د 15ث).
    /// </summary>
    public string DurationDisplay
    {
        get
        {
            var ts = TimeSpan.FromSeconds(Math.Max(0, DurationSeconds));
            if (ts.TotalHours >= 1)
            {
                return $"{(int)ts.TotalHours}س {ts.Minutes}د {ts.Seconds}ث";
            }
            return $"{ts.Minutes}د {ts.Seconds}ث";
        }
    }

    /// <summary>
    /// المعرف الرقمي لمقطع التسجيل المرئي المقترن بالنشاط.
    /// </summary>
    public long? RecordingSegmentId { get; set; }

    /// <summary>
    /// اسم ملف الفيديو المقترن بالنشاط.
    /// </summary>
    public string? RecordingFileName { get; set; }

    /// <summary>
    /// المسار الكامل لملف الفيديو على القرص.
    /// </summary>
    public string? RecordingFilePath { get; set; }

    /// <summary>
    /// إزاحة الوقت داخل مقطع الفيديو التي يبدأ عندها هذا النشاط المحدد.
    /// </summary>
    public TimeSpan VideoOffset { get; set; }

    /// <summary>
    /// قائمة الأجزاء والمقاطع المرئية المتداخلة مع هذا النشاط (في حال امتد عبر عدة مقاطع فيديو).
    /// </summary>
    public IReadOnlyList<ActivityRecordingPartViewModel> RecordingParts { get; set; } = [];

    /// <summary>
    /// العرض النصي المنسق لإزاحة الفيديو بالدقائق والثواني (مثال: 02:45).
    /// </summary>
    public string OffsetDisplay => ActivityMonitor.Core.Models.ActivityTimelineMath.FormatPlaybackTime(VideoOffset);

    /// <summary>
    /// التحقق مما إذا كان هناك ملف فيديو موجود فعلياً على القرص مرتبط بهذا النشاط.
    /// </summary>
    public bool HasVideo => RecordingParts.Any(part => part.ExistsOnDisk) ||
                            (!string.IsNullOrWhiteSpace(RecordingFilePath) && File.Exists(RecordingFilePath));

    /// <summary>
    /// عدد أجزاء الفيديو المرتبطة بالنشاط.
    /// </summary>
    public int RecordingPartCount => RecordingParts.Count;

    /// <summary>
    /// هل النشاط موزع على أكثر من مقطع فيديو مسجل.
    /// </summary>
    public bool HasMultipleRecordings => RecordingPartCount > 1;

    /// <summary>
    /// لون الشارة المميز للتطبيق بناءً على نوعه واسم العملية لتسهيل التمييز البصري.
    /// </summary>
    public string AppBadgeColor
    {
        get
        {
            // خمول المستخدم - رمادي داكن
            if (string.Equals(ProcessName, "Idle", StringComparison.OrdinalIgnoreCase)) return "#94A3B8";

            // متصفحات الويب - أزرق سماوي
            if (string.Equals(ProcessName, "chrome", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(ProcessName, "msedge", StringComparison.OrdinalIgnoreCase)) return "#38BDF8";

            // بيئات التطوير والبرمجة - بنفسجي
            if (string.Equals(ProcessName, "devenv", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(ProcessName, "code", StringComparison.OrdinalIgnoreCase)) return "#A855F7";

            // مستكشف الملفات - عنبري برتقالي
            if (string.Equals(ProcessName, "explorer", StringComparison.OrdinalIgnoreCase)) return "#F59E0B";

            // التطبيقات الأخرى - نيلي أرجواني
            return "#6366F1";
        }
    }
}

/// <summary>
/// نموذج عرض جزء التسجيل المرئي المقترن بنشاط ممتد عبر أكثر من مقطع فيديو (Multi-part recording).
/// </summary>
public sealed class ActivityRecordingPartViewModel
{
    /// <summary>
    /// معرف مقطع التسجيل في قاعدة البيانات.
    /// </summary>
    public long SegmentId { get; init; }

    /// <summary>
    /// رقم الجزء الترتيبي لهذا النشاط (يبدأ من 1).
    /// </summary>
    public int PartNumber { get; init; }

    /// <summary>
    /// إجمالي عدد الأجزاء التي يمتد عليها النشاط.
    /// </summary>
    public int TotalParts { get; init; }

    /// <summary>
    /// اسم ملف الفيديو الخاص بهذا الجزء.
    /// </summary>
    public string FileName { get; init; } = string.Empty;

    /// <summary>
    /// المسار الكامل للملف على القرص.
    /// </summary>
    public string FilePath { get; init; } = string.Empty;

    /// <summary>
    /// نقطة الإزاحة الزمنية للبدء داخل هذا المقطع المحدد.
    /// </summary>
    public TimeSpan Offset { get; init; }

    /// <summary>
    /// مدة التداخل الزمني للنشاط داخل هذا المقطع.
    /// </summary>
    public TimeSpan OverlapDuration { get; init; }

    /// <summary>
    /// التحقق من وجود الملف فعلياً على وحدة التخزين.
    /// </summary>
    public bool ExistsOnDisk => !string.IsNullOrWhiteSpace(FilePath) && File.Exists(FilePath);

    /// <summary>
    /// النص التوضيحي للجزء المعروض في شريط تحكم المشغل (مثال: المقطع 1 من 3 • rec.mp4).
    /// </summary>
    public string Display => $"المقطع {PartNumber} من {TotalParts} • {FileName}";
}

/// <summary>
/// نموذج عرض كتلة النشاط في المخطط الزمني الأفقي اليومي (Daily Timeline Block).
/// </summary>
public sealed class TimelineBlockViewModel
{
    /// <summary>
    /// كائن النشاط الأصلي المرتبط بهذه الكتلة.
    /// </summary>
    public required ActivityItemViewModel Activity { get; init; }

    /// <summary>
    /// موضع البداية الأفقي للكتلة بالنسبة لعرض الشريط بالبكسل.
    /// </summary>
    public double Left { get; init; }

    /// <summary>
    /// عرض الكتلة بالبكسل متناسباً مع مدة النشاط خلال الـ 24 ساعة.
    /// </summary>
    public double Width { get; init; }

    /// <summary>
    /// لون الكتلة في الشريط الزمني.
    /// </summary>
    public string Color { get; init; } = "#6366F1";

    /// <summary>
    /// تلميح الفأرة التوضيحي (Tooltip) عند التمرير فوق الكتلة.
    /// </summary>
    public string ToolTip { get; init; } = string.Empty;
}

/// <summary>
/// خيار فلترة الأنشطة المعروض في القائمة المنسدلة (Filter Dropdown Option).
/// </summary>
/// <param name="Key">المفتاح البرمجي للفلتر (All, Active, Idle, WithVideo, WithoutVideo).</param>
/// <param name="DisplayName">الاسم النصي المعروض للمستخدم.</param>
public sealed record ActivityFilterOption(string Key, string DisplayName);

/// <summary>
/// نموذج عرض مقطع التسجيل المرئي في جدول ومستعرض التسجيلات (Recording Segment ViewModel).
/// </summary>
public class RecordingSegmentViewModel
{
    /// <summary>
    /// المعرف الرقمي لمقطع التسجيل في قاعدة البيانات.
    /// </summary>
    public long Id { get; set; }

    /// <summary>
    /// التوقيت العالمي (UTC) لبدء التسجيل.
    /// </summary>
    public DateTime StartedAtUtc { get; set; }

    /// <summary>
    /// التوقيت العالمي (UTC) لانتهاء التسجيل.
    /// </summary>
    public DateTime? EndedAtUtc { get; set; }

    /// <summary>
    /// توقيت البدء بالتوقيت المحلي.
    /// </summary>
    public DateTime StartTimeLocal => StartedAtUtc.ToLocalTime();

    /// <summary>
    /// توقيت الانتهاء بالتوقيت المحلي.
    /// </summary>
    public DateTime EndTimeLocal => (EndedAtUtc ?? StartedAtUtc).ToLocalTime();

    /// <summary>
    /// اسم ملف التسجيل على القرص.
    /// </summary>
    public string FileName { get; set; } = string.Empty;

    /// <summary>
    /// المسار الكامل لملف الفيديو.
    /// </summary>
    public string FilePath { get; set; } = string.Empty;

    /// <summary>
    /// مدة المقطع بالثواني.
    /// </summary>
    public double DurationSeconds { get; set; }

    /// <summary>
    /// حجم الملف بالبايت.
    /// </summary>
    public long FileSizeBytes { get; set; }

    /// <summary>
    /// حالة المقطع (Completed, Recording, Corrupted, Deleted).
    /// </summary>
    public string Status { get; set; } = "Completed";

    /// <summary>
    /// العرض النصي المنسق لمدة التسجيل (مثال: 10:00).
    /// </summary>
    public string DurationDisplay => ActivityMonitor.Core.Models.ActivityTimelineMath.FormatPlaybackTime(TimeSpan.FromSeconds(DurationSeconds));

    /// <summary>
    /// العرض النصي المنسق لحجم الملف بالميغابايت (مثال: 15.24 MB).
    /// </summary>
    public string FileSizeDisplay => $"{FileSizeBytes / (1024.0 * 1024.0):F2} MB";

    /// <summary>
    /// هل الملف موجود فعلياً على وحدة التخزين.
    /// </summary>
    public bool ExistsOnDisk => File.Exists(FilePath);
}
