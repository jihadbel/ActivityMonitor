using System;
using System.IO;

namespace ActivityMonitor.Core.Options;

/// <summary>
/// فئة مسؤولة عن إدارة المسارات المشتركة لتخزين بيانات التطبيق (قاعدة البيانات، التسجيلات، التقارير، والسجلات).
/// </summary>
public static class ActivityMonitorPaths
{
    /// <summary>
    /// مسار مجلد مخصص يحدده المستخدم (اختياري للاختبارات أو البيئات المعزولة).
    /// </summary>
    private static string? _customRootDirectory;

    /// <summary>
    /// تعيين مسار مجلد رئيسي مخصص بدلاً من المسار الافتراضي للنظام.
    /// </summary>
    /// <param name="root">المسار الجديد أو null لإعادة التعيين للافتراضي.</param>
    public static void SetCustomRootDirectory(string? root)
    {
        _customRootDirectory = root == null ? null : Path.GetFullPath(root);
    }

    /// <summary>
    /// المجلد الرئيسي العام لتخزين كافة ملفات التطبيق (عادة تحت ProgramData/ActivityMonitor).
    /// </summary>
    public static string SharedRootDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "ActivityMonitor");

    public static string RootDirectory => _customRootDirectory ??
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ActivityMonitor");

    public static string SettingsPath => Path.Combine(RootDirectory, "appsettings.json");

    /// <summary>
    /// مجلد تخزين بيانات النظام وقاعدة البيانات.
    /// </summary>
    public static string DataDirectory => Path.Combine(RootDirectory, "Data");

    /// <summary>
    /// المسار الكامل لملف قاعدة البيانات SQLite (activity.db).
    /// </summary>
    public static string DatabasePath => Path.Combine(DataDirectory, "activity.db");

    /// <summary>
    /// المجلد الرئيسي لتخزين تسجيلات الفيديو المجدولة والمقسمة حسب التاريخ.
    /// </summary>
    public static string RecordingsDirectory => Path.Combine(RootDirectory, "Recordings");

    /// <summary>
    /// المجلد الرئيسي لحفظ التقارير الدورية المصدرة (Excel, CSV, Text).
    /// </summary>
    public static string ReportsDirectory => Path.Combine(RootDirectory, "Reports");

    /// <summary>
    /// المجلد الرئيسي لملفات السجلات التشخيصية (Logs).
    /// </summary>
    public static string LogsDirectory => Path.Combine(RootDirectory, "Logs");

    /// <summary>
    /// التأكد من وجود كافة المجلدات الأساسية على القرص وإنشائها في حال عدم توفرها.
    /// </summary>
    public static void EnsureDirectoriesExist()
    {
        // إنشاء المجلد الرئيسي للتطبيق
        Directory.CreateDirectory(RootDirectory);

        // إنشاء مجلد البيانات
        Directory.CreateDirectory(DataDirectory);

        // إنشاء مجلد تسجيلات الفيديو
        Directory.CreateDirectory(RecordingsDirectory);

        // إنشاء مجلد التقارير
        Directory.CreateDirectory(ReportsDirectory);

        // إنشاء مجلد السجلات التشخيصية
        Directory.CreateDirectory(LogsDirectory);
    }

    /// <summary>
    /// إنشاء مسار المجلد الهرمي لتسجيلات الفيديو منظم حسب اسم الجهاز، السنة، الشهر، اليوم، والساعة.
    /// </summary>
    /// <param name="machineId">اسم الجهاز.</param>
    /// <param name="timestamp">التوقيت الزمني للتسجيل.</param>
    /// <returns>مسار المجلد الفرعي المناسب للتسجيل.</returns>
    public static string GetRecordingDirectory(string machineId, DateTime timestamp)
    {
        return Path.Combine(
            RecordingsDirectory,
            Sanitize(machineId),
            timestamp.ToString("yyyy"),
            timestamp.ToString("MM"),
            timestamp.ToString("dd"),
            timestamp.ToString("HH")
        );
    }

    /// <summary>
    /// إنشاء المسار الكامل لملف فيديو جديد والتحقق من إنشاء المجلد التابع له مسبقاً.
    /// </summary>
    /// <param name="machineId">اسم الجهاز.</param>
    /// <param name="timestamp">التوقيت الزمني للتسجيل.</param>
    /// <param name="fileName">اسم الملف المخصص (اختياري).</param>
    /// <returns>المسار الكامل لملف الفيديو (.mp4).</returns>
    public static string GetRecordingFilePath(string machineId, DateTime timestamp, string? fileName = null)
    {
        // استخراج مسار المجلد التابع للتوقيت وإنشاؤه إن لم يكن موجوداً
        var dir = GetRecordingDirectory(machineId, timestamp);
        Directory.CreateDirectory(dir);

        // تحديد اسم الملف: إما الاسم الممرر أو التنسيق الزمني الافتراضي
        var actualFileName = fileName ?? $"{timestamp:yyyy-MM-dd_HH-mm-ss}.mp4";
        if (Path.GetFileName(actualFileName) != actualFileName || actualFileName is "." or "..")
            throw new ArgumentException("A recording filename must be a single filename.", nameof(fileName));
        return Path.Combine(dir, actualFileName);
    }

    /// <summary>
    /// إنشاء مسار مجلد التقارير منظم حسب اسم الجهاز والسنة والشهر.
    /// </summary>
    /// <param name="machineId">اسم الجهاز.</param>
    /// <param name="date">تاريخ التقرير.</param>
    /// <returns>مسار مجلد التقارير.</returns>
    public static string GetReportDirectory(string machineId, DateTime date)
    {
        return Path.Combine(
            ReportsDirectory,
            Sanitize(machineId),
            date.ToString("yyyy"),
            date.ToString("MM")
        );
    }

    /// <summary>
    /// إنشاء المسار الكامل لملف تقرير معين بالامتداد المطلوب.
    /// </summary>
    /// <param name="machineId">اسم الجهاز.</param>
    /// <param name="date">تاريخ التقرير.</param>
    /// <param name="extension">امتداد الملف (مثل xlsx أو csv).</param>
    /// <returns>المسار الكامل لملف التقرير.</returns>
    public static string GetReportFilePath(string machineId, DateTime date, string extension)
    {
        // استخراج مسار مجلد التقرير وإنشاؤه
        var dir = GetReportDirectory(machineId, date);
        Directory.CreateDirectory(dir);

        // تنظيف الامتداد من أي نقطة بادئة
        var ext = extension.TrimStart('.').ToLowerInvariant();
        if (ext is not ("xlsx" or "csv" or "txt")) throw new ArgumentException("Unsupported report extension.", nameof(extension));
        return Path.Combine(dir, $"{date:yyyy-MM-dd}.{ext}");
    }

    /// <summary>
    /// إنشاء المسار الكامل لملف سجل تشخيصي لمكون معين وتاريخ محدد.
    /// </summary>
    /// <param name="componentName">اسم المكون (مثل Agent أو Service أو Viewer).</param>
    /// <param name="date">التاريخ المستهدف.</param>
    /// <returns>المسار الكامل لملف السجل (.log).</returns>
    public static string GetLogFilePath(string componentName, DateTime date)
    {
        // التأكد من وجود مجلد السجلات
        Directory.CreateDirectory(LogsDirectory);
        return Path.Combine(LogsDirectory, $"{componentName.ToLowerInvariant()}-{date:yyyy-MM-dd}.log");
    }

    /// <summary>
    /// تنقية النص من الرموز غير الصالحة لأسماء المجلدات والملفات في نظام ويندوز واستبدالها بـ شرطة سفلية.
    /// </summary>
    /// <param name="input">النص المراد تنقيته.</param>
    /// <returns>نص آمن للاستخدام في مسارات الملفات.</returns>
    private static string Sanitize(string input)
    {
        // إذا كان الإدخال فارغاً نعيد اسماً افتراضياً
        if (string.IsNullOrWhiteSpace(input))
            return "DEFAULT";

        // استبدال أي حرف غير مسموح بنظام الملفات بشرطة سفلية
        foreach (var c in Path.GetInvalidFileNameChars())
        {
            input = input.Replace(c, '_');
        }
        if (input is "." or "..") throw new ArgumentException("Invalid path component.", nameof(input));
        return input;
    }
}
