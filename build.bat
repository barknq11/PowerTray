@echo off
set CSC=C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe
if not exist PowerTray.ico powershell -ExecutionPolicy Bypass -NoProfile -File make-icon.ps1
%CSC% /target:winexe /out:PowerTray.exe /win32icon:PowerTray.ico /reference:System.dll,System.Windows.Forms.dll,System.Drawing.dll *.cs
echo Done. Run PowerTray.exe
pause
