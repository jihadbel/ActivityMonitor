using System;

namespace ActivityMonitor.Core.Interfaces;

public interface IActivityContextProvider
{
    int Order => 0;
    bool CanHandle(string processName, IntPtr windowHandle);
    string? GetContext(string processName, IntPtr windowHandle, string windowTitle);
}
