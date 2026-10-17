@echo off
setlocal
title ActivityMonitor Installer - Windows 10

:: Check for Administrator privileges
net session >nul 2>&1
if %errorlevel% equ 0 goto :RunInstaller

:: Elevation required
echo ==============================================================
echo  ActivityMonitor - Windows 10 Installer
echo ==============================================================
echo.
echo  [!] Administrator privileges are required.
echo  [!] Please right-click Install.bat and select:
echo      'Run as administrator'
echo.
echo  Attempting automatic elevation...
echo.

powershell.exe -NoProfile -ExecutionPolicy Bypass -Command "Start-Process cmd.exe -ArgumentList '/k cd /d """"%~dp0"""" && """"%~f0""""' -Verb RunAs" 2>nul

echo.
echo ==============================================================
echo  If the elevated prompt did not appear:
echo  Right-click Install.bat and choose: 'Run as administrator'
echo ==============================================================
echo.
pause
exit /b

:RunInstaller
cd /d "%~dp0"
echo =======================================================
echo          ActivityMonitor Windows 10 Installer
echo =======================================================
echo.
echo [+] Running with Administrator privileges...
echo.

powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Install.ps1"
set "INSTALL_EXIT_CODE=%ERRORLEVEL%"

if not "%INSTALL_EXIT_CODE%"=="0" goto :InstallFailed

echo.
echo =======================================================
echo  Installation completed successfully.
echo =======================================================
echo.
pause
exit /b 0

:InstallFailed
echo.
echo =======================================================
echo  Installation failed with exit code %INSTALL_EXIT_CODE%.
echo  Review the error above, then run Install.bat again.
echo =======================================================
echo.
pause
exit /b %INSTALL_EXIT_CODE%
