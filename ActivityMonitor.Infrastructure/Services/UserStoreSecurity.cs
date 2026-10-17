using System.Security.AccessControl;
using System.Security.Principal;
using ActivityMonitor.Core.Options;

namespace ActivityMonitor.Infrastructure.Services;

/// <summary>Private stores are writable by their owner and Windows administrators, never by all Users.</summary>
public static class UserStoreSecurity
{
    public static void EnsurePrivateStore()
    {
        var root = new DirectoryInfo(ActivityMonitorPaths.RootDirectory);
        if (!StoragePathGuard.TryResolvePathWithinRoot(Path.Combine(root.FullName, ".store-check"), root.FullName,
                out _, out var reason)) throw new UnauthorizedAccessException(reason);
        root.Create();
        using var identity = WindowsIdentity.GetCurrent();
        var owner = identity.User ?? throw new UnauthorizedAccessException("Windows user identity is unavailable.");
        var acl = new DirectorySecurity();
        acl.SetAccessRuleProtection(true, false);
        acl.SetOwner(owner);
        foreach (var sid in new[] { owner, new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
                     new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null) }.Distinct())
            acl.AddAccessRule(new FileSystemAccessRule(sid, FileSystemRights.FullControl,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                PropagationFlags.None, AccessControlType.Allow));
        root.SetAccessControl(acl);
    }
}
