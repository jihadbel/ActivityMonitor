using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.ServiceProcess;
using System.Threading.Tasks;

namespace ActivityMonitor.Viewer.Services;

public enum ServiceState
{
    Running,
    Stopped,
    Transitioning,
    NotInstalled
}

public class WindowsServiceManager
{
    public static readonly string[] PossibleServiceNames = new[] { "ActivityMonitor.Service", "ActivityMonitorService" };
    public const string AgentProcessName = "ActivityMonitor.Agent";

    public string GetActiveServiceName()
    {
        foreach (var name in PossibleServiceNames)
        {
            try
            {
                using var sc = new ServiceController(name);
                var _ = sc.Status;
                return name;
            }
            catch { }
        }
        return PossibleServiceNames[0];
    }

    public ServiceState GetServiceState()
    {
        foreach (var name in PossibleServiceNames)
        {
            try
            {
                using var sc = new ServiceController(name);
                return sc.Status switch
                {
                    ServiceControllerStatus.Running => ServiceState.Running,
                    ServiceControllerStatus.Stopped => ServiceState.Stopped,
                    ServiceControllerStatus.StartPending or ServiceControllerStatus.StopPending => ServiceState.Transitioning,
                    _ => ServiceState.Stopped
                };
            }
            catch { }
        }
        return ServiceState.NotInstalled;
    }


    public bool IsAgentRunning()
    {
        try
        {
            return Process.GetProcessesByName(AgentProcessName).Length > 0;
        }
        catch
        {
            return false;
        }
    }

    public async Task<string> ToggleServiceAsync()
    {
        var state = GetServiceState();
        if (state == ServiceState.Running)
        {
            return await StopServiceAsync();
        }
        else if (state == ServiceState.Stopped)
        {
            return await StartServiceAsync();
        }
        else if (state == ServiceState.NotInstalled)
        {
            return "Windows Service is not installed. Use deploy\\Install.ps1 to install it.";
        }
        return "Service is busy transitioning.";
    }

    public async Task<string> StartServiceAsync()
    {
        return await Task.Run(() =>
        {
            var serviceName = GetActiveServiceName();
            try
            {
                using var sc = new ServiceController(serviceName);
                if (sc.Status == ServiceControllerStatus.Running)
                {
                    return "Service is already running.";
                }

                sc.Start();
                sc.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(10));
                return "Service started successfully.";
            }
            catch (Exception ex)
            {
                // Try via net start or sc.exe as fallback
                try
                {
                    var psi = new ProcessStartInfo("net", $"start \"{serviceName}\"")
                    {
                        Verb = "runas",
                        UseShellExecute = true,
                        CreateNoWindow = true
                    };
                    var p = Process.Start(psi);
                    p?.WaitForExit(5000);
                    return "Service start requested.";
                }
                catch
                {
                    return $"Failed to start service: {ex.Message}";
                }
            }
        });
    }

    public async Task<string> StopServiceAsync()
    {
        return await Task.Run(() =>
        {
            var serviceName = GetActiveServiceName();
            try
            {
                using var sc = new ServiceController(serviceName);
                if (sc.Status == ServiceControllerStatus.Stopped)
                {
                    return "Service is already stopped.";
                }

                sc.Stop();
                sc.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(10));
                return "Service stopped successfully.";
            }
            catch (Exception ex)
            {
                try
                {
                    var psi = new ProcessStartInfo("net", $"stop \"{serviceName}\"")
                    {
                        Verb = "runas",
                        UseShellExecute = true,
                        CreateNoWindow = true
                    };
                    var p = Process.Start(psi);
                    p?.WaitForExit(5000);
                    return "Service stop requested.";
                }
                catch
                {
                    return $"Failed to stop service: {ex.Message}";
                }
            }
        });
    }

    public async Task<string> ToggleAgentAsync(string? agentExePath = null)
    {
        return await Task.Run(() =>
        {
            if (IsAgentRunning())
            {
                try
                {
                    var procs = Process.GetProcessesByName(AgentProcessName);
                    foreach (var p in procs)
                    {
                        p.Kill();
                    }
                    return "ActivityMonitor Agent stopped.";
                }
                catch (Exception ex)
                {
                    return $"Failed to stop Agent: {ex.Message}";
                }
            }
            else
            {
                try
                {
                    string target = agentExePath ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Agent", "ActivityMonitor.Agent.exe");
                    if (!File.Exists(target))
                    {
                        // Check deploy dist
                        string rootDeploy = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"..\..\..\..\deploy\dist\Agent\ActivityMonitor.Agent.exe"));
                        if (File.Exists(rootDeploy)) target = rootDeploy;
                    }

                    if (File.Exists(target))
                    {
                        Process.Start(new ProcessStartInfo(target)
                        {
                            UseShellExecute = true,
                            WindowStyle = ProcessWindowStyle.Hidden
                        });
                        return "ActivityMonitor Agent started.";
                    }
                    return "ActivityMonitor.Agent.exe not found.";
                }
                catch (Exception ex)
                {
                    return $"Failed to start Agent: {ex.Message}";
                }
            }
        });
    }
}
