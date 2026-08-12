using System;
using System.Collections.Generic;
using System.Drawing;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;

[assembly: AssemblyTitle("PowerTray")]
[assembly: AssemblyDescription("Switch Windows power plans from the system tray")]
[assembly: AssemblyProduct("PowerTray")]
[assembly: AssemblyVersion("2.0.0.0")]
[assembly: AssemblyFileVersion("2.0.0.0")]

namespace PowerTray
{
    static class Program
    {
        public const string Version = "2.0.0";

        [STAThread]
        static void Main()
        {
            // A second copy would stack another tray icon and silently lose the race
            // for the hotkeys, so the instance already running wins and this one leaves.
            bool isFirstInstance;
            using (var mutex = new System.Threading.Mutex(true, @"Local\PowerTray.SingleInstance", out isFirstInstance))
            {
                if (!isFirstInstance) return;

                Application.EnableVisualStyles();
                Application.Run(new TrayApp());
                GC.KeepAlive(mutex);
            }
        }
    }

    class TrayApp : ApplicationContext
    {
        readonly NotifyIcon icon;
        readonly ContextMenuStrip menu;
        readonly HotkeyManager hotkeys = new HotkeyManager();
        readonly Timer poll;
        readonly Dictionary<Color, Icon> iconCache = new Dictionary<Color, Icon>();

        string lastIndicatorKey;
        string announcedThisSession;

        public TrayApp()
        {
            menu = new ContextMenuStrip();
            menu.Opening += (s, e) => BuildMenu();

            icon = new NotifyIcon
            {
                Icon = SystemIcons.Shield,
                Text = "PowerTray " + Program.Version,
                Visible = true,
                ContextMenuStrip = menu
            };
            icon.MouseDoubleClick += (s, e) => ShowSettings();
            icon.BalloonTipClicked += (s, e) =>
            {
                if (Updater.AvailableVersion != null) OpenReleasesPage();
            };

            // Autostart stores an absolute path. People download the exe, switch
            // autostart on, then move the file somewhere permanent, which leaves a Run
            // entry pointing at nothing. Rewriting it on launch keeps it honest.
            if (IsAutoStartEnabled()) SetAutoStart(true);

            ApplyHotkeys();
            BuildMenu();
            UpdateIcon();

            // Plans change from Settings, OEM utilities, and scheduled tasks. Nothing
            // notifies us, so without polling the coloured dot quietly goes stale.
            poll = new Timer { Interval = 2000 };
            poll.Tick += (s, e) => { UpdateIcon(); PumpUpdateNotice(); };
            poll.Start();

            Updater.StartCheck();
            ReportHotkeyFailures();
        }

        List<PowerTarget> AllTargets()
        {
            var all = new List<PowerTarget>();
            all.AddRange(Power.GetOverlays());
            all.AddRange(Power.GetSchemes());
            return all;
        }

        void ApplyHotkeys()
        {
            hotkeys.UnregisterAll();

            if (Config.CycleHotkeyEnabled)
            {
                var cycle = new Hotkey { Modifiers = Hotkey.MOD_CONTROL | Hotkey.MOD_ALT, Key = (uint)Keys.P };
                hotkeys.Register(cycle, "Cycle plans", CycleNext);
            }

            foreach (PowerTarget target in AllTargets())
            {
                Hotkey bound;
                if (!Hotkey.TryDecode(Config.GetHotkey(target.Key), out bound)) continue;

                PowerTarget captured = target;   // avoid the closure capturing the loop variable
                hotkeys.Register(bound, captured.Name, () => ActivateAndNotify(captured));
            }
        }

        void ReportHotkeyFailures()
        {
            if (hotkeys.Failures.Count == 0) return;

            var names = new List<string>(hotkeys.Failures);
            icon.ShowBalloonTip(6000, "PowerTray: some hotkeys are unavailable",
                "Another app already owns:\n" + string.Join("\n", names.ToArray()),
                ToolTipIcon.Warning);
        }

        void CycleNext()
        {
            // Cycle within whichever set the tray dot represents, so the hotkey and the
            // icon always agree about what "the next one" means.
            List<PowerTarget> pool = Power.UseOverlays ? Power.GetOverlays() : Power.GetSchemes();
            if (pool.Count == 0) return;

            int current = pool.FindIndex(p => p.Active);
            ActivateAndNotify(pool[(current + 1) % pool.Count]);
        }

        void ActivateAndNotify(PowerTarget target)
        {
            Power.Activate(target);
            UpdateIcon();
            icon.ShowBalloonTip(1500, "PowerTray", "Switched to " + target.Name, ToolTipIcon.Info);
        }

        // Runs every 2 seconds, so resolve the cheap identity first and only enumerate
        // everything when it has actually changed.
        void UpdateIcon()
        {
            string key = Power.UseOverlays
                ? "O_" + Power.GetActiveOverlay().ToString("D")
                : "S_" + Power.GetActiveScheme().ToString("D");

            if (key == lastIndicatorKey) return;
            lastIndicatorKey = key;

            PowerTarget active = Power.GetActiveIndicator();
            if (active == null) return;

            icon.Icon = GetIcon(ColorFor(active));

            string tip = "PowerTray - " + active.Name;
            icon.Text = tip.Length > 62 ? tip.Substring(0, 62) : tip;
        }

        void PumpUpdateNotice()
        {
            string found = Updater.AvailableVersion;
            if (found == null || found == announcedThisSession) return;
            announcedThisSession = found;

            // Balloon once per new release, then let the menu item carry it. Notifying
            // at every login until the user updates is how tray apps become resented.
            if (Config.AnnouncedVersion == found) return;
            Config.AnnouncedVersion = found;

            icon.ShowBalloonTip(6000, "PowerTray " + found + " is available",
                "You are running " + Program.Version + ". Click here to download it.",
                ToolTipIcon.Info);
        }

        static Color ColorFor(PowerTarget target)
        {
            if (target.Kind == TargetKind.Overlay)
            {
                if (target.Id == Power.OverlayBestEfficiency) return Color.SeaGreen;
                if (target.Id == Power.OverlayBestPerformance) return Color.Crimson;
                return Color.DodgerBlue;
            }

            if (target.Id == Power.Balanced) return Color.DodgerBlue;
            if (target.Id == Power.HighPerformance) return Color.Crimson;
            if (target.Id == Power.PowerSaver) return Color.SeaGreen;
            if (target.Id == Power.UltimatePerformance) return Color.MediumOrchid;

            var rand = new Random(target.Id.GetHashCode());
            return Color.FromArgb(rand.Next(80, 220), rand.Next(80, 220), rand.Next(80, 220));
        }

        [DllImport("user32.dll")]
        static extern bool DestroyIcon(IntPtr hIcon);

        Icon GetIcon(Color color)
        {
            Icon cached;
            if (iconCache.TryGetValue(color, out cached)) return cached;

            using (var bmp = new Bitmap(16, 16))
            {
                using (var g = Graphics.FromImage(bmp))
                {
                    g.Clear(Color.Transparent);
                    using (var brush = new SolidBrush(color))
                        g.FillEllipse(brush, 1, 1, 14, 14);
                    using (var pen = new Pen(Color.Black))
                        g.DrawEllipse(pen, 1, 1, 13, 13);
                }

                // Icon.FromHandle does not take ownership of the GDI handle, so clone
                // into a managed copy and destroy the original rather than leaking one
                // handle per distinct colour.
                IntPtr hIcon = bmp.GetHicon();
                try
                {
                    using (var temp = Icon.FromHandle(hIcon))
                    {
                        var made = (Icon)temp.Clone();
                        iconCache[color] = made;
                        return made;
                    }
                }
                finally { DestroyIcon(hIcon); }
            }
        }

        void BuildMenu()
        {
            menu.Items.Clear();

            string newVersion = Updater.AvailableVersion;
            if (newVersion != null)
            {
                var update = new ToolStripMenuItem("Update available: " + newVersion);
                update.Font = new Font(update.Font, FontStyle.Bold);
                update.Click += (s, e) => OpenReleasesPage();
                menu.Items.Add(update);
                menu.Items.Add(new ToolStripSeparator());
            }

            List<PowerTarget> overlays = Power.GetOverlays();
            if (overlays.Count > 0)
            {
                menu.Items.Add(new ToolStripMenuItem("Power mode") { Enabled = false });
                foreach (PowerTarget t in overlays) menu.Items.Add(TargetItem(t));
                menu.Items.Add(new ToolStripSeparator());
                menu.Items.Add(new ToolStripMenuItem("Power plan") { Enabled = false });
            }

            foreach (PowerTarget t in Power.GetSchemes()) menu.Items.Add(TargetItem(t));

            menu.Items.Add(new ToolStripSeparator());

            if (!Power.HasUltimatePerformance())
            {
                var unlock = new ToolStripMenuItem("Unlock Ultimate Performance");
                unlock.Click += (s, e) => UnlockUltimate();
                menu.Items.Add(unlock);
            }

            var settings = new ToolStripMenuItem("Settings...");
            settings.Click += (s, e) => ShowSettings();
            menu.Items.Add(settings);

            var exit = new ToolStripMenuItem("Exit");
            exit.Click += (s, e) => ExitApp();
            menu.Items.Add(exit);
        }

        ToolStripMenuItem TargetItem(PowerTarget target)
        {
            var item = new ToolStripMenuItem(target.Name) { Checked = target.Active, Tag = target };

            Hotkey bound;
            if (Hotkey.TryDecode(Config.GetHotkey(target.Key), out bound))
                item.ShortcutKeyDisplayString = bound.ToString();

            item.Click += (s, e) => ActivateAndNotify((PowerTarget)((ToolStripMenuItem)s).Tag);
            return item;
        }

        void UnlockUltimate()
        {
            if (Power.UnlockUltimatePerformance())
            {
                lastIndicatorKey = null;   // force the icon to re-evaluate
                icon.ShowBalloonTip(2500, "PowerTray",
                    "Ultimate Performance is now in your plan list.", ToolTipIcon.Info);
            }
            else
            {
                icon.ShowBalloonTip(3000, "PowerTray",
                    "Windows would not add Ultimate Performance on this system.", ToolTipIcon.Warning);
            }
        }

        void ShowSettings()
        {
            using (var form = new SettingsForm(AllTargets, IsAutoStartEnabled, SetAutoStart, OnSettingsChanged))
                form.ShowDialog();

            lastIndicatorKey = null;
            UpdateIcon();
        }

        void OnSettingsChanged()
        {
            ApplyHotkeys();
            ReportHotkeyFailures();
        }

        static void OpenReleasesPage()
        {
            try { System.Diagnostics.Process.Start(Updater.ReleasesPage); }
            catch { }
        }

        void ExitApp()
        {
            poll.Stop();
            hotkeys.Dispose();
            icon.Visible = false;
            Application.Exit();
        }

        const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        const string RunValueName = "PowerTray";

        bool IsAutoStartEnabled()
        {
            using (var key = Registry.CurrentUser.OpenSubKey(RunKeyPath))
                return key != null && key.GetValue(RunValueName) != null;
        }

        void SetAutoStart(bool enable)
        {
            // CreateSubKey rather than OpenSubKey: the latter returns null when the key
            // is absent, which would throw on a machine with an unusual Run hive.
            using (var key = Registry.CurrentUser.CreateSubKey(RunKeyPath))
            {
                if (key == null) return;
                if (enable)
                    key.SetValue(RunValueName, "\"" + Application.ExecutablePath + "\"");
                else
                    key.DeleteValue(RunValueName, false);
            }
        }
    }
}
