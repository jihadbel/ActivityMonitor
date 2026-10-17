using System;
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

/// <summary>
/// فئة مساعدة لتسجيل خدمات البنية التحتية (Infrastructure) ومطابقة الإعدادات ومصانع قواعد البيانات في حاوية حقن الاعتماديات (DI Container).
/// </summary>
public static class DependencyInjection
{
    /// <summary>
    /// تسجيل كافة خدمات البنية التحتية، المستودعات، والخيارات والتحقق من صحتها في مجموعة الخدمات.
    /// </summary>
    /// <param name="services">مجموعة خدمات التطبيق.</param>
    /// <param name="configuration">كائن تكوين وإعدادات التطبيق.</param>
    /// <returns>مجموعة الخدمات بعد تسجيل مكونات البنية التحتية.</returns>
    public static IServiceCollection AddInfrastructureServices(this IServiceCollection services, IConfiguration configuration)
    {
        // ==========================================
        // ربط والتحقق من صحة خيارات تتبع الأنشطة
        // ==========================================
        services.AddOptions<ActivityTrackingOptions>()
            .Bind(configuration.GetSection(ActivityTrackingOptions.SectionName))
            .Validate(o => o.PollingIntervalMs is >= 250 and <= 60_000, "PollingIntervalMs must be between 250 and 60000.")
            .Validate(o => o.IdleThresholdMinutes is >= 1 and <= 1_440, "IdleThresholdMinutes must be between 1 and 1440.")
            .Validate(o => o.WindowDebounceMs is >= 0 and <= 10_000, "WindowDebounceMs must be between 0 and 10000.")
            .ValidateOnStart();

        // ==========================================
        // ربط والتحقق من صحة خيارات تسجيل الشاشة
        // ==========================================
        services.AddOptions<RecordingOptions>()
            .Bind(configuration.GetSection(RecordingOptions.SectionName))
            .Validate(o => o.SegmentMinutes is >= 1 and <= 1_440, "SegmentMinutes must be between 1 and 1440.")
            .Validate(o => o.FrameRate is >= 5 and <= 60, "FrameRate must be between 5 and 60.")
            .Validate(o => o.VideoBitrateKbps is >= 300 and <= 10_000, "VideoBitrateKbps must be between 300 and 10000.")
            .Validate(o => o.VideoQuality is >= 10 and <= 100, "VideoQuality must be between 10 and 100.")
            .Validate(o => o.RetentionDays is >= 1 and <= 3_650, "RetentionDays must be between 1 and 3650.")
            .Validate(o => o.MaxStorageSizeGB > 0, "MaxStorageSizeGB must be greater than zero.")
            .Validate(o => o.MinimumFreeDiskSpaceGB >= 0, "MinimumFreeDiskSpaceGB cannot be negative.")
            .Validate(o => o.MicrophoneVolume is >= 0.1f and <= 3.0f, "MicrophoneVolume must be between 0.1 and 3.0.")
            .Validate(o => o.AudioBitrateKbps is >= 32 and <= 320, "AudioBitrateKbps must be between 32 and 320.")
            .ValidateOnStart();

        // ==========================================
        // ربط والتحقق من صحة خيارات التقارير
        // ==========================================
        services.AddOptions<ReportsOptions>()
            .Bind(configuration.GetSection(ReportsOptions.SectionName))
            .Validate(o => o.DailyReportScheduleTime >= TimeSpan.Zero && o.DailyReportScheduleTime < TimeSpan.FromDays(1),
                "DailyReportScheduleTime must be within a single day.")
            .Validate(o => o.BackfillDays is >= 1 and <= 365, "BackfillDays must be between 1 and 365.")
            .ValidateOnStart();

        // ==========================================
        // ربط والتحقق من صحة خيارات التخزين
        // ==========================================
        services.AddOptions<StorageOptions>()
            .Bind(configuration.GetSection(StorageOptions.SectionName))
            .Validate(o => o.RecordingRetentionDays is >= 1 and <= 3_650, "RecordingRetentionDays must be between 1 and 3650.")
            .Validate(o => o.ActivityRetentionDays is >= 1 and <= 3_650, "ActivityRetentionDays must be between 1 and 3650.")
            .Validate(o => o.ReportRetentionDays is >= 1 and <= 3_650, "ReportRetentionDays must be between 1 and 3650.")
            .Validate(o => o.LogRetentionDays is >= 1 and <= 3_650, "LogRetentionDays must be between 1 and 3650.")
            .Validate(o => o.MaxStorageSizeGB > 0, "MaxStorageSizeGB must be greater than zero.")
            .Validate(o => o.MinimumFreeDiskSpaceGB >= 0, "MinimumFreeDiskSpaceGB cannot be negative.")
            .ValidateOnStart();

        // تسجيل مصنع سياق قاعدة البيانات (DbContext Factory) لدعم العمليات المتزامنة في SQLite
        services.AddDbContextFactory<ActivityMonitorDbContext>();

        // تسجيل مستودعات البيانات كـ Singletons
        services.AddSingleton<IActivityRepository, ActivityRepository>();
        services.AddSingleton<IRecordingRepository, RecordingRepository>();

        // تسجيل مزودات سياق النوافذ
        services.AddSingleton<IActivityContextProvider, WindowsExplorerContextProvider>();
        services.AddSingleton<IActivityContextProvider, DefaultContextProvider>();
        services.AddSingleton<ActivityContextProviderComposite>();

        // تسجيل الخدمات الأساسية لإدارة التخزين ومطابقة الفيديوهات وتدقيق البيانات
        services.AddSingleton<IRecordingLocatorService, RecordingLocatorService>();
        services.AddSingleton<IStorageManagementService, StorageManagementService>();
        services.AddSingleton<IDataIntegrityService, DataIntegrityService>();

        return services;
    }
}
