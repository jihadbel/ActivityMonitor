using System;

namespace ActivityMonitor.Core.Models;

/// <summary>
/// نموذج معلومات النافذة النشطة المستخرجة من نظام ويندوز.
/// </summary>
public class ActivityWindowInfo
{
    /// <summary>
    /// مقبض النافذة في نظام التشغيل (HWND).
    /// </summary>
    public IntPtr WindowHandle { get; set; }

    /// <summary>
    /// اسم العملية المالكة للنافذة (Process Name).
    /// </summary>
    public string ProcessName { get; set; } = string.Empty;

    /// <summary>
    /// المعرّف الرقمي للعملية (PID).
    /// </summary>
    public int ProcessId { get; set; }

    /// <summary>
    /// المسار التنفيذي الكامل للتطبيق على القرص.
    /// </summary>
    public string? ProcessPath { get; set; }

    /// <summary>
    /// نص شريط عنوان النافذة.
    /// </summary>
    public string? WindowTitle { get; set; }

    /// <summary>
    /// سياق العمل الإضافي (مثل عنوان الصفحة أو مسار المجلد المفتوح).
    /// </summary>
    public string? ContextPath { get; set; }
}
