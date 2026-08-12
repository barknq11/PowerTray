# PowerTray

A tiny Windows tray app for switching power plans instantly — no Control Panel, no Settings app, no digging through menus.

Plain C#, WinForms, no dependencies. Compiles with the `csc.exe` that already ships with Windows, so there is **nothing to install** — no .NET SDK, no Visual Studio, no runtime download. Settings live in the registry, so the app stays a single file with no folder of its own.

## Features

- **Tray icon** — a colored bolt showing the active plan at a glance, updated live even when you change plans from Windows Settings
  - Balanced = blue, High performance = red, Power saver = green, Ultimate Performance = violet, custom plans get a hashed color
- **Dark mode** that follows Windows automatically, with a Light/Dark override if you want it to disagree
- **Sharp on scaled displays** — DPI-aware, and it uses the actual Windows UI font rather than the WinForms default from 1998
- **Right-click** the icon for the full plan list and menu
- **`Ctrl+Alt+P`** cycles to the next plan from anywhere in Windows
- **Per-plan hotkeys** — bind any combination to any specific plan in Settings
- **Laptop support** — on battery-powered devices the Windows 11 "Power mode" slider appears alongside classic plans, because that's what actually governs performance there
- **Unlock Ultimate Performance** in one click if Windows is hiding it
- **Start with Windows**, and it repairs its own path if you move the exe
- **Update notifications** from GitHub, throttled to once a day and switchable off
- Uses the native `powrprof.dll` API directly — no shelling out to `powercfg`, no text parsing, no process spawn

## Download

**[⬇ Download PowerTray.exe](https://github.com/barknq11/PowerTray/releases/latest/download/PowerTray.exe)**

That's it — a single 36 KB file. Run it and it lands in your tray. No installer, no cloning, no dependencies.

Requires Windows 7 or newer. .NET Framework 4.x is already present on every modern Windows install.

### "Windows protected your PC"

PowerTray isn't code-signed — certificates cost a few hundred dollars a year, which is hard to justify for a utility this small. So SmartScreen will warn you the first time. Click **More info**, then **Run anyway**.

If you'd rather not take that on faith, `build.bat` compiles the exe from the source in this repo in about a second, and you can compare the result to the released binary.

## Build from source

Only if you want to change something:

```
git clone https://github.com/barknq11/PowerTray.git
cd PowerTray
build.bat
```

`build.bat` invokes `C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe` and produces `PowerTray.exe` in about a second. No .NET SDK, no Visual Studio.

## Usage

| Action | Result |
| --- | --- |
| Right-click tray icon | Plan list + menu |
| Double-click tray icon | Open Settings |
| `Ctrl+Alt+P` | Cycle to the next plan |
| Settings → select a plan, press keys, Assign | Bind a hotkey to that specific plan |

Autostart is stored as a value under `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` — no shortcut files, no scheduled tasks, and unticking the box removes the key cleanly. Hotkeys and preferences live under `HKCU\Software\PowerTray`.

A hotkey needs at least one of Ctrl, Alt or Shift, otherwise it would swallow that key everywhere in Windows. If another app already owns a combination, PowerTray tells you which one failed rather than leaving a key that quietly does nothing.

## Files

| File | Purpose |
| --- | --- |
| `Program.cs` | Entry point and tray app |
| `Power.cs` | `powrprof.dll` wrappers for plans and Power mode |
| `Config.cs` | Registry-backed settings |
| `Hotkeys.cs` | Global hotkey registration |
| `SettingsForm.cs` | Settings window |
| `Updater.cs` | Background version check |
| `Theme.cs` | Light/dark palette and menu rendering |
| `TrayIcons.cs` | Draws the tray bolt |
| `PowerTray.manifest` | DPI awareness and common controls v6 |
| `build.bat` | Rebuild script |
| `make-icon.ps1` | Generates `PowerTray.ico` from code |
| `tests\run-tests.bat` | Self-test against the live machine |
| `PowerTray.exe` | Prebuilt binary |

## Notes

- Your available plans come from Windows itself. Laptops often hide High performance until you run `powercfg -duplicatescheme 8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c` once.
- Some OEM laptops (Lenovo, Dell, HP) override the active plan through their own power software. If a switch doesn't stick, that's the vendor utility, not PowerTray.
- Windows 11 machines using the newer "Power mode" slider still expose classic plans through this API, so the app works there too.

## License

MIT
