@echo off
REM Exercises the non-GUI layers against the real machine, plus a construction
REM test for the settings window. Program.cs is excluded (it owns the real Main).
cd /d "%~dp0.."
set CSC=C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe
%CSC% /nologo /target:exe /out:"%TEMP%\pt_selftest.exe" /reference:System.dll,System.Windows.Forms.dll,System.Drawing.dll Power.cs Config.cs Hotkeys.cs Updater.cs Theme.cs TrayIcons.cs SettingsForm.cs tests\SelfTest.cs || exit /b 1
"%TEMP%\pt_selftest.exe"
echo.
echo Exit code: %ERRORLEVEL%
pause
