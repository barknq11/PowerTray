@echo off
set CSC=C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe
%CSC% /target:winexe /out:PowerTray.exe /reference:System.dll,System.Windows.Forms.dll,System.Drawing.dll Program.cs
echo Done. Run PowerTray.exe
pause
