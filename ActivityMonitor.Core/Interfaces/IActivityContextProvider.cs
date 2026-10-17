using System;

namespace ActivityMonitor.Core.Interfaces;

/// <summary>
/// واجهة استخراج وتوفير سياق العمل الإضافي للنوافذ النشطة (مثل مستكشف الملفات والمتصفحات).
/// </summary>
public interface IActivityContextProvider
{
    /// <summary>
    /// ترتيب أو أولوية تنفيذ المزود في مصفوفة المزودين المركبة (الأقل ينفذ أولاً).
    /// </summary>
    int Order => 0;

    /// <summary>
    /// التحقق مما إذا كان هذا المزود يدعم استخراج السياق من العملية والنافذة المعينة.
    /// </summary>
    /// <param name="processName">اسم العملية النشطة.</param>
    /// <param name="windowHandle">مقبض النافذة المعنية.</param>
    /// <returns>القيمة true إذا كان المزود قادراً على التعامل مع النافذة؛ وإلا false.</returns>
    bool CanHandle(string processName, IntPtr windowHandle);

    /// <summary>
    /// استخراج سياق العمل (مثل مسار المجلد المفتوح أو المستند).
    /// </summary>
    /// <param name="processName">اسم العملية.</param>
    /// <param name="windowHandle">مقبض النافذة.</param>
    /// <param name="windowTitle">عنوان النافذة الحالي.</param>
    /// <returns>نص يمثل السياق المستخرج أو null إذا لم يتوفر.</returns>
    string? GetContext(string processName, IntPtr windowHandle, string windowTitle);
}
