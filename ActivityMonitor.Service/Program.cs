using ActivityMonitor.Infrastructure;
using ActivityMonitor.Infrastructure.Logging;
using ActivityMonitor.Reporting;
using ActivityMonitor.Service;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

var builder = Host.CreateApplicationBuilder(args);

// Configure as official Windows Service
builder.Services.AddWindowsService(options =>
{
    options.ServiceName = "ActivityMonitor.Service";
});

// Technical File Logging
builder.Logging.ClearProviders();
builder.Logging.AddActivityFileLogger("service");
builder.Logging.AddConsole();

// Register Infrastructure & Reporting
builder.Services.AddInfrastructureServices(builder.Configuration);
builder.Services.AddReportingServices();

// Register Service Worker
builder.Services.AddHostedService<Worker>();

var host = builder.Build();

if (args.Contains("--export-reports", StringComparer.OrdinalIgnoreCase))
{
    using var scope = host.Services.CreateScope();
    var exporter = scope.ServiceProvider.GetRequiredService<ActivityMonitor.Core.Interfaces.IActivityReportExporter>();
    var today = DateTime.UtcNow.Date;
    var excel = await exporter.ExportDailyExcelReportAsync(today);
    var csv = await exporter.ExportDailyCsvReportAsync(today);
    var txt = await exporter.ExportDailySummaryTextAsync(today);
    Console.WriteLine($"[+] Daily Excel Report generated: {excel}");
    Console.WriteLine($"[+] Daily CSV Report generated: {csv}");
    Console.WriteLine($"[+] Daily TXT Summary generated: {txt}");
    return;
}

await host.RunAsync();
