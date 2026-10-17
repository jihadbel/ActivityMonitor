using System;
using System.IO;

namespace ActivityMonitor.Infrastructure.Services;

/// <summary>
/// فئة أمان مسؤولة عن التحقق وتدقيق مسارات التخزين والملفات قبل إجراء أي عمليات حذف أو تعديل،
/// لمنع هجمات اجتياز المسارات (Path Traversal) ونقاط إعادة التحليل (Reparse Points / Symlinks).
/// </summary>
public static class StoragePathGuard
{
    /// <summary>
    /// التحقق مما إذا كان المسار المستهدف يقع بصورة آمنة وصحيحة داخل المجلد الجذري المسموح به.
    /// </summary>
    /// <param name="candidatePath">المسار المرشح للفحص.</param>
    /// <param name="allowedRoot">المجلد الجذري المسموح به.</param>
    /// <param name="resolvedPath">المسار الكامل المحلول في حال النجاح.</param>
    /// <param name="failureReason">سبب الفشل إن وجد.</param>
    /// <returns>القيمة true إذا كان المسار آمناً وداخلياً؛ وإلا false.</returns>
    public static bool TryResolvePathWithinRoot(
        string? candidatePath,
        string? allowedRoot,
        out string resolvedPath,
        out string failureReason)
    {
        resolvedPath = string.Empty;
        failureReason = string.Empty;

        // التحقق من أن المسار ليس فارغاً
        if (string.IsNullOrWhiteSpace(candidatePath))
        {
            failureReason = "The candidate path is empty.";
            return false;
        }

        // التحقق من أن المجلد الجذري ليس فارغاً
        if (string.IsNullOrWhiteSpace(allowedRoot))
        {
            failureReason = "The allowed storage root is empty.";
            return false;
        }

        try
        {
            // استخراج المسار الكامل للجذر وتنظيف الفواصل النهائية
            var rootPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(allowedRoot));
            var fullCandidatePath = Path.GetFullPath(candidatePath);

            // حساب المسار النسبي بين الجذر والمسار المرشح
            var relativePath = Path.GetRelativePath(rootPath, fullCandidatePath);

            // رفض المسار إذا كان يخرج عن الجذر (يبدأ بـ .. أو جذر مستقل)
            if (relativePath == "." ||
                Path.IsPathRooted(relativePath) ||
                relativePath.Equals("..", StringComparison.Ordinal) ||
                relativePath.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal) ||
                relativePath.StartsWith($"..{Path.AltDirectorySeparatorChar}", StringComparison.Ordinal))
            {
                failureReason = "The candidate path is outside the allowed storage root.";
                return false;
            }

            // فحص نقاط إعادة التحليل والروابط الرمزية (Reparse Points / Symlinks) لتجنب الهروب من المجلد
            if (ContainsExistingReparsePoint(rootPath, relativePath))
            {
                failureReason = "The candidate path traverses a reparse point.";
                return false;
            }

            resolvedPath = fullCandidatePath;
            return true;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException or IOException or UnauthorizedAccessException)
        {
            failureReason = ex.Message;
            return false;
        }
    }

    /// <summary>
    /// التحقق مما إذا كان أي جزء من أجزاء المسار يمثل نقطة إعادة توجيه أو رابطاً رمزياً (Reparse Point).
    /// </summary>
    /// <param name="rootPath">مسار الجذر الكامل.</param>
    /// <param name="relativePath">المسار النسبي الداخلي.</param>
    /// <returns>القيمة true إذا كان يحتوي على نقطة إعادة توجيه؛ وإلا false.</returns>
    private static bool ContainsExistingReparsePoint(string rootPath, string relativePath)
    {
        // A reparse point above the nominated root is equally capable of escaping it.
        for (var ancestor = new DirectoryInfo(rootPath); ancestor != null; ancestor = ancestor.Parent)
        {
            if (ancestor.Exists && (ancestor.Attributes & FileAttributes.ReparsePoint) != 0) return true;
        }
        var currentPath = rootPath;

        // تقسيم المسار إلى أجزائه الفرعية وفحص كل مجلد على حدة
        var pathParts = relativePath.Split(
            new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar },
            StringSplitOptions.RemoveEmptyEntries);

        foreach (var pathPart in pathParts)
        {
            currentPath = Path.Combine(currentPath, pathPart);
            if (PathExists(currentPath) && IsReparsePoint(currentPath))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// التحقق مما إذا كان الملف أو المجلد موجوداً بالفعل على القرص.
    /// </summary>
    private static bool PathExists(string path) => File.Exists(path) || Directory.Exists(path);

    /// <summary>
    /// التحقق من خاصية ReparsePoint في سمات الملف أو المجلد.
    /// </summary>
    private static bool IsReparsePoint(string path) =>
        (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
}
