using System.Text.Json.Nodes;
using ActivityMonitor.Core.Options;
using Microsoft.Win32;

namespace ActivityMonitor.Infrastructure.Services;

/// <summary>All interactive startup paths use Run keys at the user's normal privilege level.</summary>
public static class InteractiveStartup
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string Name = "ActivityMonitorAgent";

    public static bool IsEnabled()
    {
        try
        {
            if (File.Exists(ActivityMonitorPaths.SettingsPath) &&
                JsonNode.Parse(File.ReadAllText(ActivityMonitorPaths.SettingsPath))?["ActivityTracking"]?["StartAgentAtLogon"]?.GetValue<bool>() == false)
                return false;
            using var user = Registry.CurrentUser.OpenSubKey(RunKey);
            using var machine = Registry.LocalMachine.OpenSubKey(RunKey);
            return user?.GetValue(Name) is string || machine?.GetValue(Name) is string;
        }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
        catch (System.Text.Json.JsonException) { return false; }
    }

    public static bool SetEnabled(bool enabled, string executable)
    {
        try
        {
            using var user = Registry.CurrentUser.CreateSubKey(RunKey, true);
            using var machine = Registry.LocalMachine.OpenSubKey(RunKey);
            if (enabled && machine?.GetValue(Name) is not string)
                user.SetValue(Name, $"\"{Path.GetFullPath(executable)}\" --silent --autostart");
            else user.DeleteValue(Name, false);
            // The shared Run registration remains in place; --autostart respects this user's preference.
            Directory.CreateDirectory(ActivityMonitorPaths.RootDirectory);
            var root = File.Exists(ActivityMonitorPaths.SettingsPath)
                ? JsonNode.Parse(File.ReadAllText(ActivityMonitorPaths.SettingsPath))!.AsObject() : new JsonObject();
            root["ActivityTracking"] ??= new JsonObject();
            root["ActivityTracking"]!["StartAgentAtLogon"] = enabled;
            var temp = ActivityMonitorPaths.SettingsPath + $".{Guid.NewGuid():N}.tmp";
            try { File.WriteAllText(temp, root.ToJsonString()); File.Move(temp, ActivityMonitorPaths.SettingsPath, true); }
            finally { if (File.Exists(temp)) File.Delete(temp); }
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException or InvalidOperationException)
        { return false; }
    }
}
