using System;
using System.Collections.Generic;
using System.Linq;
using ActivityMonitor.Core.Interfaces;

namespace ActivityMonitor.Infrastructure.ContextProviders;

/// <summary>
/// مزود مركب لسياق الأنشطة (Composite Provider) يقوم بالمرور على جميع المزودين المسجلين
/// بترتيب أولوياتهم حتى ينجح أحدهم في استخراج سياق النافذة.
/// </summary>
public class ActivityContextProviderComposite
{
    /// <summary>
    /// قائمة المزودين مرتبة تصاعدياً حسب خاصية Order.
    /// </summary>
    private readonly List<IActivityContextProvider> _providers;

    /// <summary>
    /// منشئ الفئة: يستقبل كافة المزودين المحقونين ويرتبهم حسب الأولوية.
    /// </summary>
    /// <param name="providers">مجموعة مزودات السياق المسجلة.</param>
    public ActivityContextProviderComposite(IEnumerable<IActivityContextProvider> providers)
    {
        // ترتيب المزودين حسب الأولوية (الرقم الأقل ينفذ أولاً)
        _providers = providers.OrderBy(p => p.Order).ToList();
    }

    /// <summary>
    /// حل واستخراج سياق العمل للنافذة من أول مزود مؤهل قادر على التعامل معها.
    /// </summary>
    /// <param name="processName">اسم العملية النشطة.</param>
    /// <param name="windowHandle">مقبض النافذة.</param>
    /// <param name="windowTitle">عنوان النافذة الحالي.</param>
    /// <returns>السياق المستخرج أو null في حال عدم توفره من أي مزود.</returns>
    public string? ResolveContext(string processName, IntPtr windowHandle, string windowTitle)
    {
        // التكرار عبر قائمة المزودين حسب ترتيب الأولوية
        foreach (var provider in _providers)
        {
            try
            {
                // إذا كان المزود يدعم هذه العملية والنافذة
                if (provider.CanHandle(processName, windowHandle))
                {
                    // محاولة استخراج السياق
                    var ctx = provider.GetContext(processName, windowHandle, windowTitle);

                    // إذا نجح الاستخراج وكان النص غير فارغ نعيده فوراً
                    if (!string.IsNullOrWhiteSpace(ctx))
                    {
                        return ctx;
                    }
                }
            }
            catch
            {
                // تخطي أي مزود يفشل بصمت ودون التأثير على سير العمل
            }
        }

        return null;
    }
}
