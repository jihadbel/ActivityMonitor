@echo off
setlocal
title ActivityMonitor Uninstaller - Windows 10

:: Check for Administrator privileges
net session >nul 2>&1
if %errorlevel% equ 0 goto :RunUninstaller

:: Elevation required
echo ==============================================================
echo  ActivityMonitor - Windows 10 Uninstaller
echo ==============================================================
echo.
echo  [!] Administrator privileges are required.
echo  [!] Please right-click Uninstall.bat and select:
echo      'Run as administrator'
echo.
echo  Attempting automatic elevation...
echo.

powershell.exe -NoProfile -ExecutionPolicy Bypass -Command "Start-Process cmd.exe -ArgumentList '/k cd /d """"%~dp0"""" && """"%~f0""""' -Verb RunAs" 2>nul

echo.
echo ==============================================================
echo  If the elevated prompt did not appear:
echo  Right-click Uninstall.bat and choose: 'Run as administrator'
echo ==============================================================
echo.
pause
exit /b

:RunUninstaller
cd /d "%~dp0"
echo =======================================================
echo         ActivityMonitor Windows 10 Uninstaller
echo =======================================================
echo.
echo [+] Running with Administrator privileges...
echo.

powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Uninstall.ps1"

echo.
echo =======================================================
echo  Uninstallation process completed.
echo =======================================================
echo.
pause
