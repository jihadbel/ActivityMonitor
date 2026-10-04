namespace ActivityMonitor.Core.Enums;

public enum ActivityEventType
{
    ActiveWindow = 1,
    IdleStart = 2,
    IdleEnd = 3,
    SessionLock = 4,
    SessionUnlock = 5,
    SessionLogon = 6,
    SessionLogoff = 7,
    SystemSleep = 8,
    SystemResume = 9,
    SystemShutdown = 10
}
