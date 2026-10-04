using System;
using System.IO;
using ActivityMonitor.Core.Interfaces;
using Microsoft.Extensions.Logging;

namespace ActivityMonitor.Infrastructure.ContextProviders;

public class WindowsExplorerContextProvider : IActivityContextProvider
{
    private readonly ILogger<WindowsExplorerContextProvider> _logger;

    public int Order => 10;

    public WindowsExplorerContextProvider(ILogger<WindowsExplorerContextProvider> logger)
    {
        _logger = logger;
    }

    public bool CanHandle(string processName, IntPtr windowHandle)
    {
        return string.Equals(processName, "explorer", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(processName, "explorer.exe", StringComparison.OrdinalIgnoreCase);
    }

    public string? GetContext(string processName, IntPtr windowHandle, string windowTitle)
    {
        if (windowHandle == IntPtr.Zero) return null;

        try
        {
            // Official, stable COM automation method for Windows Explorer (Shell.Application)
            var shellType = Type.GetTypeFromProgID("Shell.Application");
            if (shellType != null)
            {
                dynamic? shell = Activator.CreateInstance(shellType);
                if (shell != null)
                {
                    dynamic? windows = shell.Windows();
                    if (windows != null)
                    {
                        int count = windows.Count;
                        for (int i = 0; i < count; i++)
                        {
                            try
                            {
                                dynamic? item = windows.Item(i);
                                if (item is not null)
                                {
                                    long hwnd = (long)item.HWND;
                                    if (hwnd == windowHandle.ToInt64())
                                    {
                                        var doc = item.Document;
                                        if (doc is not null && doc.Folder is not null && doc.Folder.Self is not null)
                                        {
                                            string? path = doc.Folder.Self.Path;
                                            if (!string.IsNullOrWhiteSpace(path))
                                            {
                                                return path;
                                            }
                                        }
                                    }
                                }
                            }
                            catch
                            {
                                // continue iterating
                            }
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogTrace(ex, "Could not extract Explorer path via Shell.Application");
        }

        // Fallback: check if windowTitle contains a valid directory path
        if (!string.IsNullOrWhiteSpace(windowTitle) && Directory.Exists(windowTitle))
        {
            return windowTitle;
        }

        return !string.IsNullOrWhiteSpace(windowTitle) ? windowTitle : null;
    }
}
