using System.Security.AccessControl;
using System.Security.Principal;

namespace ActivityMonitor.Infrastructure.Services;

public static class AgentShutdownSignal
{
    public static EventWaitHandle Create(string name)
    {
        using var identity = WindowsIdentity.GetCurrent();
        var security = new EventWaitHandleSecurity();
        security.SetAccessRuleProtection(true, false);
        foreach (var sid in new[] { identity.User!, new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
                     new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null) }.Distinct())
            security.AddAccessRule(new EventWaitHandleAccessRule(sid, EventWaitHandleRights.FullControl, AccessControlType.Allow));
        return EventWaitHandleAcl.Create(false, EventResetMode.AutoReset, name, out _, security);
    }
}
