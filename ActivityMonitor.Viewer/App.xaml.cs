using System;
using System.IO;
using System.Windows;
using ActivityMonitor.Core.Options;

namespace ActivityMonitor.Viewer;

/// <summary>
/// نقطة الدخول الرئيسية لتطبيق واجهة المستخدم (WPF Application Lifecycle).
/// تتولى معالجة الاستثناءات العامة غير المعالجة وتسجيل أعطال النظام وحفظها لمنع الانهيار المفاجئ بدون أثر.
/// </summary>
public partial class App : Application
{
    public static bool IsReviewOnlyMode { get; private set; }
    /// <summary>
    /// يتم استدعاؤها عند بدء تشغيل تطبيق العارض لتهيئة معالجات الأخطاء العامة ومسارات التسجيل.
    /// </summary>
    /// <param name="e">معاملات حدث بدء التشغيل.</param>
    protected override void OnStartup(StartupEventArgs e)
    {
        var storeArgument = Array.FindIndex(e.Args, arg => string.Equals(arg, "--user-store", StringComparison.OrdinalIgnoreCase));
        if (e.Args.Contains("--legacy-archive", StringComparer.OrdinalIgnoreCase) || storeArgument >= 0)
        {
            using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
            if (!new System.Security.Principal.WindowsPrincipal(identity).IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator))
            {
                MessageBox.Show("الأرشيف القديم متاح للمسؤول فقط. شغّل العارض كمسؤول مع --legacy-archive.");
                Shutdown(1); return;
            }
            var root = ActivityMonitorPaths.SharedRootDirectory;
            if (storeArgument >= 0)
            {
                if (storeArgument + 1 >= e.Args.Length) { MessageBox.Show("حدد مسار مخزن المستخدم بعد --user-store."); Shutdown(1); return; }
                root = Path.GetFullPath(e.Args[storeArgument + 1]);
                var database = Path.Combine(root, "Data", "activity.db");
                if (!Directory.Exists(root) || !File.Exists(database) ||
                    !ActivityMonitor.Infrastructure.Services.StoragePathGuard.TryResolvePathWithinRoot(database, root, out _, out _))
                { MessageBox.Show("مخزن المستخدم غير صالح أو يحتوي على إعادة توجيه غير آمنة."); Shutdown(1); return; }
            }
            ActivityMonitorPaths.SetCustomRootDirectory(root);
            IsReviewOnlyMode = true;
        }
        else ActivityMonitor.Infrastructure.Services.UserStoreSecurity.EnsurePrivateStore();
        // اعتراض كافة الاستثناءات غير المعالجة الصادرة من خيوط المعالجة الخلفية غير المتصلة بـ Dispatcher
        AppDomain.CurrentDomain.UnhandledException += (s, ev) =>
        {
            try
            {
                // مسار مجلد السجلات الموحد للتطبيق
                var logDir = ActivityMonitorPaths.LogsDirectory;

                // التأكد من وجود مجلد السجلات قبل محاولة كتابة ملف الانهيار
                if (!Directory.Exists(logDir))
                {
                    Directory.CreateDirectory(logDir);
                }

                // حفظ تفاصيل الاستثناء في ملف نصي لتسهيل استكشاف الأخطاء وإصلاحها
                File.WriteAllText(
                    Path.Combine(logDir, "viewer_crash.txt"),
                    ev.ExceptionObject?.ToString() ?? "Unknown domain error");
            }
            catch
            {
                // تجاهل أخطاء كتابة السجل في الحالات الحرجة لتفادي التعليق غير المتوقع
            }
        };

        // اعتراض استثناءات خيط واجهة المستخدم (WPF Dispatcher) غير المعالجة لمنع انهيار البرنامج وعرض تنبيه للمستخدم
        DispatcherUnhandledException += (s, ev) =>
        {
            try
            {
                // مسار مجلد السجلات المركزي
                var logDir = ActivityMonitorPaths.LogsDirectory;

                // إنشاء المجلد إذا لم يكن موجوداً مسبقاً
                if (!Directory.Exists(logDir))
                {
                    Directory.CreateDirectory(logDir);
                }

                // توثيق الخطأ الصادر في ملف الانهيار
                File.WriteAllText(
                    Path.Combine(logDir, "viewer_crash.txt"),
                    ev.Exception.ToString());
            }
            catch
            {
                // منع التكرار في حال فشل الوصول للقرص أثناء كتابة السجل
            }

            // عرض رسالة توضيحية للمستخدم توضح حدوث خطأ غير متوقع
            MessageBox.Show(
                $"حدث خطأ غير متوقع أثناء عرض البيانات:\n{ev.Exception.Message}",
                "ActivityMonitor Viewer",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);

            // الإشارة إلى أنه تم التعامل مع الاستثناء لمنع نظام التشغيل من إنهاء التطبيق قسرياً
            ev.Handled = true;
        };

        // استدعاء تنفيذ البداية الأساسي لنظام WPF
        base.OnStartup(e);
    }
}
