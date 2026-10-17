using System;
using System.IO;
using System.Text.RegularExpressions;
using ActivityMonitor.Core.Interfaces;

namespace ActivityMonitor.Infrastructure.ContextProviders;

/// <summary>
/// مزود السياق الافتراضي العام (Default Context Provider)، يقوم بتحليل عنوان النافذة
/// للبحث عن مسارات ملفات ومجلدات أو استخراج اسم المشروع لبيئات التطوير (Visual Studio و VS Code).
/// </summary>
public class DefaultContextProvider : IActivityContextProvider
{
    /// <summary>
    /// تعبير نمطي (Regex) للبحث عن مسارات الملفات في نظام ويندوز داخل نص عنوان النافذة.
    /// </summary>
    private static readonly Regex FilePathRegex = new(
        @"[a-zA-Z]:\\(?:[^\\/:*?""<>|\r\n]+\\)*[^\\/:*?""<>|\r\n]*",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>
    /// أولوية التنفيذ (100 تعني أنه يعمل كخيار احتياطي أخير بعد المزودات المتخصصة).
    /// </summary>
    public int Order => 100;

    /// <summary>
    /// المزود الافتراضي يقبل التعامل مع أي عملية كنافذة احتياطية.
    /// </summary>
    /// <param name="processName">اسم العملية.</param>
    /// <param name="windowHandle">مقبض النافذة.</param>
    /// <returns>القيمة true دائماً.</returns>
    public bool CanHandle(string processName, IntPtr windowHandle) => true;

    /// <summary>
    /// استخراج السياق من عنوان النافذة بالبحث عن مسارات ملفات حقيقية أو أسماء مشاريع برمجية.
    /// </summary>
    /// <param name="processName">اسم العملية.</param>
    /// <param name="windowHandle">مقبض النافذة.</param>
    /// <param name="windowTitle">عنوان النافذة.</param>
    /// <returns>المسار أو اسم المشروع أو null.</returns>
    public string? GetContext(string processName, IntPtr windowHandle, string windowTitle)
    {
        // إذا كان العنوان فارغاً لا يمكن استخراج سياق
        if (string.IsNullOrWhiteSpace(windowTitle)) return null;

        // 1. محاولة استخراج مسار ملف أو مجلد حقيقي مذكور في عنوان النافذة (شائع في برامج التحرير مثل Notepad)
        var match = FilePathRegex.Match(windowTitle);
        if (match.Success)
        {
            var potentialPath = match.Value.Trim();
            // التحقق من أن المسار موجود فعلياً على القرص
            if (File.Exists(potentialPath) || Directory.Exists(potentialPath))
            {
                return potentialPath;
            }
        }

        // 2. لبيئات التطوير مثل Visual Studio و VS Code، استخراج اسم الملف أو الحل البرمجي النشط
        if (processName.Contains("devenv", StringComparison.OrdinalIgnoreCase) ||
            processName.Contains("code", StringComparison.OrdinalIgnoreCase))
        {
            var parts = windowTitle.Split('-', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length > 0)
            {
                return parts[0];
            }
        }

        return null;
    }
}
