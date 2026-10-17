using System;
using System.IO;
using System.Linq;
using ActivityMonitor.Infrastructure;
using ActivityMonitor.Infrastructure.Logging;
using ActivityMonitor.Reporting;
using ActivityMonitor.Service;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

// ==========================================
// نقطة الدخول الرئيسية لخدمة نظام ويندوز (ActivityMonitor.Service)
// ==========================================

ActivityMonitor.Core.Options.ActivityMonitorPaths.SetCustomRootDirectory(ActivityMonitor.Core.Options.ActivityMonitorPaths.SharedRootDirectory);
var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { Args = args, ContentRootPath = AppContext.BaseDirectory });

// تحميل التكوين المشترك من مجلد ProgramData في حال وجوده لتمكين التعديل المباشر من واجهة المستخدم
var sharedConfigPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "ActivityMonitor", "appsettings.json");
if (File.Exists(sharedConfigPath))
{
    // إضافة ملف الإعدادات المشترك مع تمكين إعادة التحميل عند التغيير
    builder.Configuration.AddJsonFile(sharedConfigPath, optional: true, reloadOnChange: true);
}

// تكوين التطبيق ليعمل كخدمة نظام تشغيل Windows رسمية (Windows Service)
builder.Services.AddWindowsService(options =>
{
    options.ServiceName = "ActivityMonitor.Service";
});

// تكوين نظام السجلات التقنية في ملفات وموجه الأوامر
builder.Logging.ClearProviders();
builder.Logging.AddActivityFileLogger("service");
builder.Logging.AddConsole();

// تسجيل خدمات البنية التحتية وإنشاء التقارير
builder.Services.AddInfrastructureServices(builder.Configuration);
builder.Services.AddReportingServices();

// تسجيل العامل الخلفي الرئيسي للخدمة (Worker)
builder.Services.AddHostedService<Worker>();

using var host = builder.Build();

// فحص معامل سطر الأوامر لتصدير التقارير يدوياً وفورياً
if (args.Contains("--export-reports", StringComparer.OrdinalIgnoreCase))
{
    using var scope = host.Services.CreateScope();
    var exporter = scope.ServiceProvider.GetRequiredService<ActivityMonitor.Core.Interfaces.IActivityReportExporter>();
    await ActivityMonitor.Infrastructure.Data.ActivityMonitorDbContext.InitializeDatabaseAsync();
    var today = DateTime.Today;

    // تصدير التقارير بالصيغ الثلاث
    var excel = await exporter.ExportDailyExcelReportAsync(today);
    var csv = await exporter.ExportDailyCsvReportAsync(today);
    var txt = await exporter.ExportDailySummaryTextAsync(today);

    Console.WriteLine($"[+] Daily Excel Report generated: {excel}");
    Console.WriteLine($"[+] Daily CSV Report generated: {csv}");
    Console.WriteLine($"[+] Daily TXT Summary generated: {txt}");
    return;
}

// بدء تشغيل مضيف الخدمة
await host.RunAsync();
