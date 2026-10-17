ActivityMonitor — Windows x64

1. Generate a fresh package with deploy/Publish.ps1. The installer requires manifest.json and verifies all listed SHA-256 hashes before changing an installation.
2. Extract the whole package. Right-click Install.bat and run as Administrator. Start Menu access to the Viewer is created by default.
3. The Agent starts with the user's normal Windows token at next logon. To start it earlier, open the Viewer and start the Agent. -StartImmediately is optional; installation does not capture a screen by default.
4. New per-user data and settings: %LOCALAPPDATA%\ActivityMonitor. Each account owns its own database, videos, exports and settings. Owner, SYSTEM and administrators have access.
5. Existing shared data remains in %ProgramData%\ActivityMonitor and is restricted to administrators/SYSTEM by this installer. Shared appsettings.json contains readable defaults only. To review the previous archive, run Viewer as Administrator with --legacy-archive.
6. Activity, video, report and technical-log retention periods are independent. This package's Agent configuration enables microphone audio only; system audio is disabled. Obtain the user's informed consent before recording. Windows microphone privacy indicators remain visible; the Agent adds no windows or pop-up notifications. Saved per-user/shared settings override package settings, so existing installations retain their audio choices. Recording state is visible in the Viewer; audio can be disabled in Settings.
7. Close the Viewer before upgrading. Installation/removal asks Agents to finalize gracefully; if an Agent cannot be signaled/stopped, it aborts instead of killing the encoder. Review the error before retrying.
8. Uninstall.bat removes application files and startup registration. Data remains unless Uninstall.ps1 -PurgeData is used explicitly. PurgeData covers the shared archive and current account only; other users' private stores remain.
9. Back up the current account database without starting capture:
   ActivityMonitor.Agent.exe --backup-db "D:\Backups\activity.db"
   The destination must not already exist. Stop all Agents for the account before restoring, and never combine a backup with old WAL/SHM files.
10. Packages published with SelfContained=false need both .NET 10 and Windows Desktop Runtime 10. The default package is self-contained. Windows Media Foundation and the recording library's Windows/native prerequisites are still required.

Hashes detect accidental package changes; they do not replace trusted publisher signing. Real multi-session installation, hardware encoding, audio, lock/sleep and long-running performance still require validation on the intended Windows machines.
