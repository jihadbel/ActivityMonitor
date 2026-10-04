using System;
using System.Diagnostics;
using System.IO;
using Microsoft.Win32;

namespace ActivityMonitor.Agent.Startup;

public static class StartupManager
{
    private const string AppName = "ActivityMonitorAgent";
    private const string RunRegistryKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    public static string GetExecutablePath()
    {
        return Process.GetCurrentProcess().MainModule?.FileName ??
               Environment.ProcessPath ??
               Path.Combine(AppContext.BaseDirectory, "ActivityMonitor.Agent.exe");
    }

    public static bool IsStartupEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunRegistryKey, false);
            var value = key?.GetValue(AppName) as string;
            return !string.IsNullOrEmpty(value);
        }
        catch
        {
            return false;
        }
    }

    public static bool EnableStartup()
    {
        try
        {
            var exePath = GetExecutablePath();
            var command = $"\"{exePath}\" --silent";

            using var key = Registry.CurrentUser.OpenSubKey(RunRegistryKey, true);
            if (key != null)
            {
                key.SetValue(AppName, command);
                return true;
            }
        }
        catch
        {
            // fallback or report failure
        }

        return false;
    }

    public static bool DisableStartup()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunRegistryKey, true);
            if (key != null && key.GetValue(AppName) != null)
            {
                key.DeleteValue(AppName, false);
                return true;
            }
        }
        catch
        {
            // fallback
        }

        return false;
    }

    public static bool RegisterScheduledTask()
    {
        try
        {
            var exePath = GetExecutablePath();
            var arguments = $"/Create /TN \"{AppName}\" /TR \"\\\"{exePath}\\\" --silent\" /SC ONLOGON /RL HIGHEST /F";

            var startInfo = new ProcessStartInfo
            {
                FileName = "schtasks.exe",
                Arguments = arguments,
                CreateNoWindow = true,
                UseShellExecute = false
            };

            using var process = Process.Start(startInfo);
            process?.WaitForExit(5000);
            return process?.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    public static bool UnregisterScheduledTask()
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "schtasks.exe",
                Arguments = $"/Delete /TN \"{AppName}\" /F",
                CreateNoWindow = true,
                UseShellExecute = false
            };

            using var process = Process.Start(startInfo);
            process?.WaitForExit(5000);
            return process?.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }
}
