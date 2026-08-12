# PowerTray

A tiny Windows tray app for switching power plans instantly — no Control Panel, no Settings app, no digging through menus.

Single C# file, ~320 lines, WinForms. Compiles with the `csc.exe` that already ships with Windows, so there is **nothing to install** — no .NET SDK, no Visual Studio, no runtime download.

## Features

- **Tray icon** with a colored dot showing the active plan at a glance
  - Balanced = blue, High performance = red, Power saver = green, custom plans get a hashed color
- **Right-click** the icon for the full plan list and menu
- **Double-click** for a small window with a list and an Activate button
- **`Ctrl+Alt+P`** cycles to the next plan from anywhere in Windows
- **Start with Windows** toggle in the tray menu (off by default)
- Uses the native `powrprof.dll` API directly — no shelling out to `powercfg`, no text parsing, no process spawn

## Install on a new machine

Requires Windows 7 or newer. .NET Framework 4.x is already present on every modern Windows install.

```
git clone https://github.com/barknq11/PowerTray.git
cd PowerTray
PowerTray.exe
```

The prebuilt `PowerTray.exe` is committed, so you can run it straight away. If you'd rather build it yourself:

```
build.bat
```

That invokes `C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe` and produces `PowerTray.exe` in about a second.

Not a git user? Just download the ZIP from the green **Code** button and run the exe.

## Usage

| Action | Result |
| --- | --- |
| Right-click tray icon | Plan list + menu |
| Double-click tray icon | Open the window GUI |
| `Ctrl+Alt+P` | Cycle to the next plan |
| Tray menu → Start with Windows | Toggle autostart |

Autostart is stored as a value under `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` — no shortcut files, no scheduled tasks, and unticking the menu item removes the key cleanly.

## Files

| File | Purpose |
| --- | --- |
| `Program.cs` | The entire app |
| `build.bat` | Rebuild script |
| `PowerTray.exe` | Prebuilt binary |
| `handoff.md` | Development notes and history |

## Notes

- Your available plans come from Windows itself. Laptops often hide High performance until you run `powercfg -duplicatescheme 8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c` once.
- Some OEM laptops (Lenovo, Dell, HP) override the active plan through their own power software. If a switch doesn't stick, that's the vendor utility, not PowerTray.
- Windows 11 machines using the newer "Power mode" slider still expose classic plans through this API, so the app works there too.

## License

MIT
