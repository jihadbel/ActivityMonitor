using System;
using System.Linq;
using System.Runtime.InteropServices;
using ActivityMonitor.Agent;
using ActivityMonitor.Agent.Recording;
using ActivityMonitor.Agent.Startup;
using ActivityMonitor.Agent.Tracking;
using ActivityMonitor.Core.Interfaces;
using ActivityMonitor.Infrastructure;
using ActivityMonitor.Infrastructure.Logging;
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

var builder = Host.CreateApplicationBuilder(args);

// Technical File Logging
builder.Logging.ClearProviders();
builder.Logging.AddActivityFileLogger("agent");
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

var host = builder.Build();
await host.RunAsync();

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
