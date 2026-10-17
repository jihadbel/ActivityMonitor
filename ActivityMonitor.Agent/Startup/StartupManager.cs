using System;
using System.Diagnostics;
using System.IO;
using Microsoft.Win32;

namespace ActivityMonitor.Agent.Startup;

/// <summary>
/// فئة مساعدة مسؤولة عن إدارة التشغيل التلقائي للعميل (Agent) عند تسجيل دخول المستخدم،
/// وتدعم كلاً من سجل النظام (Windows Registry) ومجدول المهام (Task Scheduler).
/// </summary>
public static class StartupManager
{
    /// <summary>
    /// اسم التطبيق أو المفتاح المستخدم في سجل النظام ومجدول المهام.
    /// </summary>
    private const string AppName = "ActivityMonitorAgent";

    /// <summary>
    /// المسار النسبي لمفتاح التشغيل التلقائي داخل سجل نظام ويندوز.
    /// </summary>
    private const string RunRegistryKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    /// <summary>
    /// استخراج المسار الكامل للملف التنفيذي للعميل الحالي على القرص.
    /// </summary>
    /// <returns>المسار الكامل لملف ActivityMonitor.Agent.exe.</returns>
    public static string GetExecutablePath()
    {
        // محاولة جلب المسار من الوحدة الرئيسية للعملية الجارية
        return Process.GetCurrentProcess().MainModule?.FileName ??
               // محاولة جلب المسار من مسار العملية البيئي لـ .NET
               Environment.ProcessPath ??
               // كحل بديل: دمج مجلد التطبيق الأساسي مع اسم الملف التنفيذي
               Path.Combine(AppContext.BaseDirectory, "ActivityMonitor.Agent.exe");
    }

    /// <summary>
    /// التحقق مما إذا كان التشغيل التلقائي للتطبيق مفعلاً في سجل النظام أو مجدول المهام.
    /// </summary>
    /// <returns>القيمة true إذا كان التشغيل التلقائي مفعلاً؛ وإلا false.</returns>
    public static bool IsStartupEnabled() { return ActivityMonitor.Infrastructure.Services.InteractiveStartup.IsEnabled(); }

    /// <summary>
    /// تمكين التشغيل التلقائي للتطبيق في سجل المستخدم الحالي (HKEY_CURRENT_USER\Run).
    /// </summary>
    /// <returns>القيمة true إذا تم التسجيل بنجاح؛ وإلا false.</returns>
    public static bool EnableStartup()
    { return ActivityMonitor.Infrastructure.Services.InteractiveStartup.SetEnabled(true, GetExecutablePath()); }

    /// <summary>
    /// تعطيل التشغيل التلقائي وإزالة التطبيق من سجل النظام ومجدول المهام.
    /// </summary>
    /// <returns>القيمة true إذا تم الحذف بنجاح أو لم يكن موجوداً؛ وإلا false.</returns>
    public static bool DisableStartup()
    { return ActivityMonitor.Infrastructure.Services.InteractiveStartup.SetEnabled(false, GetExecutablePath()); }

    /// <summary>
    /// تسجيل مهمة مجدولة في نظام ويندوز لتشغيل التطبيق بأعلى صلاحيات عند تسجيل الدخول.
    /// </summary>
    /// <returns>القيمة true إذا تم إنشاء المهمة المجدولة بنجاح؛ وإلا false.</returns>
    public static bool RegisterScheduledTask() { return EnableStartup(); }

    /// <summary>
    /// إلغاء تسجيل وحذف المهمة المجدولة من مجدول مهام نظام ويندوز.
    /// </summary>
    /// <returns>القيمة true إذا تم حذف المهمة بنجاح؛ وإلا false.</returns>
    public static bool UnregisterScheduledTask() { return DisableStartup(); }

    /// <summary>
    /// التحقق الداخلي مما إذا كانت المهمة المجدولة مسجلة حالياً في مجدول مهام ويندوز.
    /// </summary>
    /// <returns>القيمة true إذا كانت مسجلة؛ وإلا false.</returns>
    private static bool IsScheduledTaskRegistered()
    {
        try
        {
            // استعلام عن وجود المهمة بواسطة اسمها
            var startInfo = new ProcessStartInfo
            {
                FileName = "schtasks.exe",
                Arguments = $"/Query /TN \"{AppName}\"",
                CreateNoWindow = true,
                UseShellExecute = false
            };
            using var process = Process.Start(startInfo);
            return process != null && process.WaitForExit(3000) && process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }
}
