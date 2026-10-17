using ActivityMonitor.Core.Interfaces;
using ActivityMonitor.Reporting.Exporters;
using Microsoft.Extensions.DependencyInjection;

namespace ActivityMonitor.Reporting;

/// <summary>
/// فئة مساعدة لتسجيل خدمات إنشاء وتصدير التقارير في حاوية حقن الاعتماديات.
/// </summary>
public static class DependencyInjection
{
    /// <summary>
    /// تسجيل خدمة تصدير تقارير الأنشطة بصيغ Excel و CSV و Text كـ Singleton.
    /// </summary>
    /// <param name="services">مجموعة خدمات التطبيق.</param>
    /// <returns>مجموعة الخدمات بعد إضافة خدمة التقارير.</returns>
    public static IServiceCollection AddReportingServices(this IServiceCollection services)
    {
        // تسجيل خدمة تصدير التقارير
        services.AddSingleton<IActivityReportExporter, ActivityReportExporter>();
        return services;
    }
}
