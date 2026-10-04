using System;

namespace ActivityMonitor.Core.Models;

public class ActivityWindowInfo
{
    public IntPtr WindowHandle { get; set; }
    public string ProcessName { get; set; } = string.Empty;
    public int ProcessId { get; set; }
    public string? ProcessPath { get; set; }
    public string? WindowTitle { get; set; }
    public string? ContextPath { get; set; }
}
