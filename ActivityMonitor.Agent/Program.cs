using System;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using ActivityMonitor.Agent;
using ActivityMonitor.Agent.Recording;
using ActivityMonitor.Agent.Startup;
using ActivityMonitor.Agent.Tracking;
using ActivityMonitor.Core.Interfaces;
using ActivityMonitor.Infrastructure;
using ActivityMonitor.Infrastructure.Logging;
using ActivityMonitor.Reporting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

// Handle command line switches
if (args.Contains("--install-startup", StringComparer.OrdinalIgnoreCase))
{
    var ok = StartupManager.EnableStartup();
    Console.WriteLine(ok ? "Successfully enabled startup registration." : "Failed to enable startup registration.");
    return;
}

if (args.Contains("--uninstall-startup", StringComparer.OrdinalIgnoreCase))
{
    var ok = StartupManager.DisableStartup();
    Console.WriteLine(ok ? "Successfully removed startup registration." : "Failed to remove startup registration.");
    return;
}

// In silent mode, hide console window immediately for complete unobtrusiveness
if (args.Contains("--silent", StringComparer.OrdinalIgnoreCase))
{
    HideConsoleWindow();
}

// Enforce single instance per user session
if (args.Length == 2 && string.Equals(args[0], "--backup-db", StringComparison.OrdinalIgnoreCase))
{
    await ActivityMonitor.Infrastructure.Services.DatabaseBackupService.BackupAsync(
        ActivityMonitor.Core.Options.ActivityMonitorPaths.DatabasePath, args[1]);
    return;
}

Mutex? singleInstanceMutex = null;
bool isNewInstance = false;
var interactiveSessionId = Process.GetCurrentProcess().SessionId;
try
{
    singleInstanceMutex = new Mutex(true, $"Local\\ActivityMonitor_Agent_{Environment.UserName}_{interactiveSessionId}", out isNewInstance);
}
catch
{
    try
    {
        singleInstanceMutex = new Mutex(true, $"ActivityMonitor_Agent_{Environment.UserName}_{interactiveSessionId}", out isNewInstance);
    }
    catch
    {
        isNewInstance = true;
    }
}

if (!isNewInstance)
{
    // Another instance is already active for this user session; exit immediately
    return;
}

// Named shutdown event for graceful termination from Viewer or Service Manager
EventWaitHandle? shutdownEvent = null;
try
{
    shutdownEvent = ActivityMonitor.Infrastructure.Services.AgentShutdownSignal.Create($"Global\\ActivityMonitor_Agent_Shutdown_{Environment.UserName}_{interactiveSessionId}");
}
catch
{
    try
    {
        shutdownEvent = ActivityMonitor.Infrastructure.Services.AgentShutdownSignal.Create($"Local\\ActivityMonitor_Agent_Shutdown_{Environment.UserName}_{interactiveSessionId}");
    }
    catch { }
}
using var agentCts = new CancellationTokenSource();
using var instanceGuard = singleInstanceMutex;

if (shutdownEvent != null)
{
    _ = Task.Run(() =>
    {
        try
        {
            var signaled = WaitHandle.WaitAny([shutdownEvent, agentCts.Token.WaitHandle]);
            if (signaled == 0) agentCts.Cancel();
        }
        catch { }
    });
}

ActivityMonitor.Infrastructure.Services.UserStoreSecurity.EnsurePrivateStore();
var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { Args = args, ContentRootPath = AppContext.BaseDirectory });

// Load shared configuration from ProgramData if available for live user adjustments
var sharedConfigPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "ActivityMonitor", "appsettings.json");
if (File.Exists(sharedConfigPath))
{
    builder.Configuration.AddJsonFile(sharedConfigPath, optional: true, reloadOnChange: true);
}

// Technical File Logging
builder.Configuration.AddJsonFile(ActivityMonitor.Core.Options.ActivityMonitorPaths.SettingsPath, optional: true, reloadOnChange: true);
builder.Logging.ClearProviders();
builder.Logging.AddActivityFileLogger($"agent-{interactiveSessionId}");
if (!args.Contains("--silent", StringComparer.OrdinalIgnoreCase))
{
    builder.Logging.AddConsole();
}

// Register Infrastructure & Reporting
builder.Services.AddInfrastructureServices(builder.Configuration);

// Register Agent Services
builder.Services.AddSingleton<IScreenRecordingService, ScreenRecordingService>();
builder.Services.AddSingleton<IActivityTracker, ActivityTracker>();
builder.Services.AddHostedService<Worker>();
builder.Services.AddReportingServices();
builder.Services.AddHostedService<ActivityMonitor.Reporting.MaintenanceWorker>();

if (args.Contains("--autostart", StringComparer.OrdinalIgnoreCase) &&
    !builder.Configuration.GetValue("ActivityTracking:StartAgentAtLogon", true)) return;
using var host = builder.Build();

try
{
    await host.RunAsync(agentCts.Token);
}
catch (OperationCanceledException)
{
    // Clean exit requested by shutdown event
}
finally { agentCts.Cancel(); }

static void HideConsoleWindow()
{
    try
    {
        var handle = GetConsoleWindow();
        if (handle != IntPtr.Zero)
        {
            ShowWindow(handle, 0); // SW_HIDE
        }
    }
    catch
    {
        // Ignore in environments without standard console
    }
}

[DllImport("kernel32.dll")]
static extern IntPtr GetConsoleWindow();

[DllImport("user32.dll")]
static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
