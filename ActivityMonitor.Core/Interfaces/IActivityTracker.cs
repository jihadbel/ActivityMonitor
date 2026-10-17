using System;
using System.Threading;
using System.Threading.Tasks;
using ActivityMonitor.Core.Entities;

namespace ActivityMonitor.Core.Interfaces;

/// <summary>
/// واجهة متتبع الأنشطة المسؤولة عن بدء وإيقاف تتبع أحداث النوافذ والخمول.
/// </summary>
public interface IActivityTracker
{
    bool IsUserActive => false;
    /// <summary>
    /// حدث يتم إطلاقه عند بداية نشاط جديد لنافذة تطبيق في المقدمة.
    /// </summary>
    event Func<ActivityEvent, Task>? ActivityStarted;

    /// <summary>
    /// حدث يتم إطلاقه عند انتهاء وحفظ النشاط الحالي.
    /// </summary>
    event Func<ActivityEvent, Task>? ActivityEnded;

    /// <summary>
    /// بدء تشغيل عملية تتبع النوافذ وأحداث النظام.
    /// </summary>
    /// <param name="ct">رمز الإلغاء.</param>
    /// <returns>مهمة تمثل العملية غير المتزامنة.</returns>
    Task StartAsync(CancellationToken ct = default);

    /// <summary>
    /// إيقاف عملية تتبع النوافذ وحفظ الأنشطة المعلقة.
    /// </summary>
    /// <param name="ct">رمز الإلغاء.</param>
    /// <returns>مهمة تمثل العملية غير المتزامنة.</returns>
    Task StopAsync(CancellationToken ct = default);
}
