using ActivityMonitor.Core.Interfaces;
using ActivityMonitor.Reporting.Exporters;
using Microsoft.Extensions.DependencyInjection;

namespace ActivityMonitor.Reporting;

public static class DependencyInjection
{
    public static IServiceCollection AddReportingServices(this IServiceCollection services)
    {
        services.AddScoped<IActivityReportExporter, ActivityReportExporter>();
        return services;
    }
}
