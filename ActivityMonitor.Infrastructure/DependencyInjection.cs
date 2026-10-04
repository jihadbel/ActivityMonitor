using ActivityMonitor.Core.Interfaces;
using ActivityMonitor.Core.Options;
using ActivityMonitor.Infrastructure.ContextProviders;
using ActivityMonitor.Infrastructure.Data;
using ActivityMonitor.Infrastructure.Repositories;
using ActivityMonitor.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ActivityMonitor.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructureServices(this IServiceCollection services, IConfiguration configuration)
    {
        // Bind Options
        services.Configure<ActivityTrackingOptions>(configuration.GetSection(ActivityTrackingOptions.SectionName));
        services.Configure<RecordingOptions>(configuration.GetSection(RecordingOptions.SectionName));
        services.Configure<ReportsOptions>(configuration.GetSection(ReportsOptions.SectionName));
        services.Configure<StorageOptions>(configuration.GetSection(StorageOptions.SectionName));

        // Register DbContext Factory
        services.AddDbContextFactory<ActivityMonitorDbContext>();

        // Register Repositories
        services.AddScoped<IActivityRepository, ActivityRepository>();
        services.AddScoped<IRecordingRepository, RecordingRepository>();

        // Register Context Providers
        services.AddSingleton<IActivityContextProvider, WindowsExplorerContextProvider>();
        services.AddSingleton<IActivityContextProvider, DefaultContextProvider>();
        services.AddSingleton<ActivityContextProviderComposite>();

        // Register Services
        services.AddScoped<IRecordingLocatorService, RecordingLocatorService>();
        services.AddScoped<IStorageManagementService, StorageManagementService>();

        return services;
    }
}
