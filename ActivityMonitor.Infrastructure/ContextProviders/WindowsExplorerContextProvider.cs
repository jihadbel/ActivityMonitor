using System;
using System.IO;
using System.Runtime.InteropServices;
using ActivityMonitor.Core.Interfaces;
using Microsoft.Extensions.Logging;

namespace ActivityMonitor.Infrastructure.ContextProviders;

/// <summary>
/// مزود سياق مخصص لمستكشف ملفات ويندوز (Windows Explorer)،
/// يستخدم كائنات COM عبر واجهة Shell.Application لقراءة مسار المجلد المفتوح أو الملف المحدد حالياً.
/// </summary>
public class WindowsExplorerContextProvider : IActivityContextProvider
{
    /// <summary>
    /// مسجل السجلات لتتبع الأخطاء التشخيصية.
    /// </summary>
    private readonly ILogger<WindowsExplorerContextProvider> _logger;

    /// <summary>
    /// آخر مقبض نافذة تم فحصه لتسريع القراءة عبر الذاكرة المؤقتة (Cache).
    /// </summary>
    private IntPtr _lastWindowHandle = IntPtr.Zero;

    /// <summary>
    /// النتيجة المخزنة مؤقتاً للنافذة الأخيرة.
    /// </summary>
    private string? _cachedResult;

    /// <summary>
    /// توقيت آخر عملية استخراج ناجحة للسياق.
    /// </summary>
    private DateTime _lastResolveTime = DateTime.MinValue;

    /// <summary>
    /// أولوية التنفيذ (10 لضمان تنفيذه قبل المزود الافتراضي عند التعامل مع explorer).
    /// </summary>
    public int Order => 10;

    /// <summary>
    /// منشئ الفئة.
    /// </summary>
    /// <param name="logger">مسجل السجلات.</param>
    public WindowsExplorerContextProvider(ILogger<WindowsExplorerContextProvider> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// التحقق مما إذا كانت العملية المستهدفة هي مستكشف ملفات ويندوز (explorer.exe).
    /// </summary>
    /// <param name="processName">اسم العملية.</param>
    /// <param name="windowHandle">مقبض النافذة.</param>
    /// <returns>القيمة true إذا كانت العملية explorer؛ وإلا false.</returns>
    public bool CanHandle(string processName, IntPtr windowHandle)
    {
        return string.Equals(processName, "explorer", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(processName, "explorer.exe", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// استخراج المسار المفتوح أو الملف المحدد في نافذة مستكشف الملفات.
    /// </summary>
    /// <param name="processName">اسم العملية.</param>
    /// <param name="windowHandle">مقبض النافذة المعنية.</param>
    /// <param name="windowTitle">عنوان النافذة الحالي.</param>
    /// <returns>المسار الكامل للمجلد أو الملف المحدد.</returns>
    public string? GetContext(string processName, IntPtr windowHandle, string windowTitle)
    {
        if (windowHandle == IntPtr.Zero) return null;

        var now = DateTime.UtcNow;

        // مسار سريع (Fast-path): إعادة النتيجة المؤقتة إذا كانت نفس النافذة خلال 1.5 ثانية لتخفيف استهلاك COM
        if (windowHandle == _lastWindowHandle && (now - _lastResolveTime).TotalMilliseconds < 1500 && _cachedResult != null)
        {
            return _cachedResult;
        }

        string? resolvedPath = null;
        object? shell = null;
        object? windows = null;

        try
        {
            // إنشاء كائن Shell.Application للوصول لنوافذ النظام
            var shellType = Type.GetTypeFromProgID("Shell.Application");
            if (shellType != null)
            {
                shell = Activator.CreateInstance(shellType);
                if (shell != null)
                {
                    dynamic dynamicShell = shell;
                    windows = dynamicShell.Windows();
                    if (windows != null)
                    {
                        dynamic dynamicWindows = windows;
                        int count = (int)dynamicWindows.Count;

                        // المرور على كافة نوافذ المستكشف المفتوحة
                        for (int i = 0; i < count; i++)
                        {
                            object? item = null;
                            try
                            {
                                item = dynamicWindows.Item(i);
                                if (item is not null)
                                {
                                    dynamic dynamicItem = item;
                                    long hwnd = (long)dynamicItem.HWND;

                                    // مطابقة مقبض النافذة مع المقبض النشط المطلوب
                                    if (hwnd == windowHandle.ToInt64())
                                    {
                                        object? docObj = null;
                                        try
                                        {
                                            docObj = dynamicItem.Document;
                                            if (docObj is not null)
                                            {
                                                dynamic doc = docObj;

                                                // 1. محاولة استخراج مسار العنصر المركز عليه حالياً (Focused Item)
                                                object? focusedObj = null;
                                                try
                                                {
                                                    focusedObj = doc.FocusedItem;
                                                    if (focusedObj is not null)
                                                    {
                                                        dynamic focused = focusedObj;
                                                        string? filePath = (string?)focused?.Path;
                                                        if (!string.IsNullOrWhiteSpace(filePath))
                                                        {
                                                            resolvedPath = filePath;
                                                        }
                                                    }
                                                }
                                                catch { }
                                                finally { SafeReleaseCom(focusedObj); }

                                                // 2. إذا لم يتوفر، نحاول استخراج أول عنصر محدد (Selected Item)
                                                if (string.IsNullOrWhiteSpace(resolvedPath))
                                                {
                                                    object? selectedObj = null;
                                                    try
                                                    {
                                                        selectedObj = doc.SelectedItems();
                                                        if (selectedObj is not null)
                                                        {
                                                            dynamic selected = selectedObj;
                                                            if ((int)(selected?.Count ?? 0) > 0)
                                                            {
                                                                object? firstItem = selected?.Item(0);
                                                                try
                                                                {
                                                                    dynamic? dynamicFirst = firstItem;
                                                                    string? filePath = (string?)dynamicFirst?.Path;
                                                                    if (!string.IsNullOrWhiteSpace(filePath))
                                                                    {
                                                                        resolvedPath = filePath;
                                                                    }
                                                                }
                                                                finally { SafeReleaseCom(firstItem); }
                                                            }
                                                        }
                                                    }
                                                    catch { }
                                                    finally { SafeReleaseCom(selectedObj); }
                                                }

                                                // 3. إذا لم يحدد أي ملف، نستخرج مسار المجلد المفتوح نفسه (Folder.Self.Path)
                                                if (string.IsNullOrWhiteSpace(resolvedPath))
                                                {
                                                    object? folderObj = null;
                                                    try
                                                    {
                                                        folderObj = doc.Folder;
                                                        if (folderObj is not null)
                                                        {
                                                            dynamic? folder = folderObj;
                                                            object? selfObj = folder?.Self;
                                                            try
                                                            {
                                                                dynamic? self = selfObj;
                                                                string? path = (string?)self?.Path;
                                                                if (!string.IsNullOrWhiteSpace(path))
                                                                {
                                                                    resolvedPath = path;
                                                                }
                                                            }
                                                            finally { SafeReleaseCom(selfObj); }
                                                        }
                                                    }
                                                    catch { }
                                                    finally { SafeReleaseCom(folderObj); }
                                                }

                                                // إذا عثرنا على المسار نخرج من الحلقة
                                                if (!string.IsNullOrWhiteSpace(resolvedPath))
                                                {
                                                    break;
                                                }
                                            }
                                        }
                                        catch { }
                                        finally { SafeReleaseCom(docObj); }
                                    }
                                }
                            }
                            catch { }
                            finally
                            {
                                SafeReleaseCom(item);
                            }
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogTrace(ex, "Could not extract Explorer path via Shell.Application");
        }
        finally
        {
            // تحرير كائنات COM لمنع تسريب الذاكرة في نظام ويندوز
            SafeReleaseCom(windows);
            SafeReleaseCom(shell);
        }

        // إذا تم حل المسار بنجاح، نحفظه في الذاكرة المؤقتة ونعيده
        if (!string.IsNullOrWhiteSpace(resolvedPath))
        {
            _lastWindowHandle = windowHandle;
            _cachedResult = resolvedPath;
            _lastResolveTime = now;
            return resolvedPath;
        }

        // حل احتياطي: فحص عنوان النافذة بأمان دون حظر الخيط على مشاركات الشبكة البطيئة
        if (!string.IsNullOrWhiteSpace(windowTitle))
        {
            // لمسارات الشبكة (\\server\share)، نقبلها مباشرة دون فحص وجود الدليل لتجنب تجميد SMB
            if (windowTitle.StartsWith(@"\\"))
            {
                _lastWindowHandle = windowHandle;
                _cachedResult = windowTitle;
                _lastResolveTime = now;
                return windowTitle;
            }

            // للأقراص المحلية (مثل C:\ و D:\)، نتحقق من وجود المجلد
            if (windowTitle.Length >= 3 && windowTitle[1] == ':' && Directory.Exists(windowTitle))
            {
                _lastWindowHandle = windowHandle;
                _cachedResult = windowTitle;
                _lastResolveTime = now;
                return windowTitle;
            }
        }

        // حفظ القيمة الاحتياطية وإرجاعها
        _lastWindowHandle = windowHandle;
        _cachedResult = !string.IsNullOrWhiteSpace(windowTitle) ? windowTitle : null;
        _lastResolveTime = now;
        return _cachedResult;
    }

    /// <summary>
    /// تحرير كائن COM بأمان عبر Marshal.ReleaseComObject في حال كان كائناً غير مدار.
    /// </summary>
    /// <param name="obj">الكائن المراد تحريره.</param>
    private static void SafeReleaseCom(object? obj)
    {
        if (obj != null && Marshal.IsComObject(obj))
        {
            try { Marshal.ReleaseComObject(obj); } catch { }
        }
    }
}
