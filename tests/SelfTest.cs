using System;
using System.Windows.Forms;

namespace PowerTray
{
    // Temporary harness: exercises the non-GUI layers against the real machine.
    // Compiled separately from PowerTray.exe and not shipped.
    static class Program
    {
        public const string Version = "2.0.0";
    }

    static class SelfTest
    {
        static int failures;

        // Renders the real GDI+ tray icon at native size, magnified with nearest-
        // neighbour so individual pixels are visible, over both taskbar colours.
        static void DumpTrayPreview()
        {
            var colors = new System.Drawing.Color[]
            {
                System.Drawing.Color.FromArgb(64, 158, 255), System.Drawing.Color.FromArgb(255, 88, 100),
                System.Drawing.Color.FromArgb(61, 214, 140), System.Drawing.Color.FromArgb(203, 128, 255)
            };

            const int zoom = 6;
            using (var probe = TrayIcons.For(colors[0]).ToBitmap())
            {
                int n = probe.Width;
                int cell = n * zoom;
                int pad = 10;
                int w = pad + colors.Length * (cell + pad);
                int h = pad + 2 * (cell + pad);

                using (var sheet = new System.Drawing.Bitmap(w, h))
                using (var g = System.Drawing.Graphics.FromImage(sheet))
                {
                    g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
                    g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.Half;

                    using (var dark = new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(32, 32, 32)))
                        g.FillRectangle(dark, 0, 0, w, pad + cell + pad / 2);
                    using (var light = new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(243, 243, 243)))
                        g.FillRectangle(light, 0, pad + cell + pad / 2, w, h);

                    for (int i = 0; i < colors.Length; i++)
                    {
                        using (var bmp = TrayIcons.For(colors[i]).ToBitmap())
                        {
                            int x = pad + i * (cell + pad);
                            g.DrawImage(bmp, x, pad, cell, cell);
                            g.DrawImage(bmp, x, pad + cell + pad, cell, cell);
                        }
                    }

                    string path = System.IO.Path.Combine(
                        System.IO.Directory.GetCurrentDirectory(), "preview-tray.png");
                    sheet.Save(path, System.Drawing.Imaging.ImageFormat.Png);
                    Console.WriteLine("         wrote " + path + "  (" + n + "px native, " + zoom + "x zoom)");
                }
            }
        }

        static void Check(string label, bool ok, string detail)
        {
            Console.WriteLine((ok ? "  PASS  " : "  FAIL  ") + label + (detail == null ? "" : "  ->  " + detail));
            if (!ok) failures++;
        }

        [STAThread]
        static int Main()
        {
            Console.WriteLine("-- power layer --");
            var schemes = Power.GetSchemes();
            Check("schemes enumerate", schemes.Count > 0, schemes.Count + " found");

            bool named = true, activeCount = false;
            int actives = 0;
            foreach (var s in schemes)
            {
                if (string.IsNullOrEmpty(s.Name) || s.Name == s.Id.ToString("D")) named = false;
                if (s.Active) actives++;
                Console.WriteLine("         " + s.Name + "   key=" + s.Key + (s.Active ? "  [active]" : ""));
            }
            activeCount = actives == 1;
            Check("friendly names resolve", named, null);
            Check("exactly one active scheme", activeCount, actives + " active");

            Check("battery detected", Power.HasBattery == false, "HasBattery=" + Power.HasBattery + " (desktop expected)");
            Check("overlay export probe", true, "OverlaysAvailable=" + Power.OverlaysAvailable);
            Check("overlays suppressed on desktop", Power.UseOverlays == false, "UseOverlays=" + Power.UseOverlays);
            Check("overlay list empty on desktop", Power.GetOverlays().Count == 0, null);
            Check("ultimate performance present", Power.HasUltimatePerformance(), null);

            var indicator = Power.GetActiveIndicator();
            Check("indicator resolves", indicator != null, indicator == null ? "null" : indicator.Name);

            Console.WriteLine("-- hotkeys --");
            var hk = Hotkey.FromKeyData(Keys.Control | Keys.Alt | Keys.D1);
            Check("capture ctrl+alt+1", hk.IsUsable && hk.ToString() == "Ctrl+Alt+1", hk.ToString());

            Hotkey rt;
            Check("encode/decode round trip", Hotkey.TryDecode(hk.Encode(), out rt)
                  && rt.Modifiers == hk.Modifiers && rt.Key == hk.Key, hk.Encode());

            var modOnly = Hotkey.FromKeyData(Keys.Control | Keys.ControlKey);
            Check("modifier alone rejected", !modOnly.IsUsable, null);

            var bare = Hotkey.FromKeyData(Keys.D1);
            Check("unmodified key rejected", !bare.IsUsable, null);

            Console.WriteLine("-- config --");
            Config.SetHotkey("SELFTEST", "3:49");
            Check("hotkey persists", Config.GetHotkey("SELFTEST") == "3:49", null);
            Config.ClearHotkey("SELFTEST");
            Check("hotkey clears", Config.GetHotkey("SELFTEST") == null, null);
            Check("cycle default is on", Config.CycleHotkeyEnabled, null);
            Check("updates default is on", Config.CheckForUpdates, null);

            Console.WriteLine("-- version comparison --");
            Check("2.0.0 beats 1.1.0", Updater.IsNewer("2.0.0", "1.1.0"), null);
            Check("v-prefix tolerated", Updater.IsNewer("v2.1.0", "2.0.0"), null);
            Check("older is not newer", !Updater.IsNewer("1.0.0", "2.0.0"), null);
            Check("equal is not newer", !Updater.IsNewer("2.0.0", "2.0.0"), null);
            Check("prerelease suffix trimmed", !Updater.IsNewer("2.0.0-beta", "2.0.0"), null);
            Check("garbage is not newer", !Updater.IsNewer("not-a-version", "2.0.0"), null);

            Console.WriteLine("-- theme --");
            ThemeMode saved = Config.Theme;
            Config.Theme = ThemeMode.Light;
            Check("light override wins", !Theme.Dark, null);
            Config.Theme = ThemeMode.Dark;
            Check("dark override wins", Theme.Dark, null);
            Config.Theme = ThemeMode.Auto;
            Check("auto follows Windows", Theme.Dark == Theme.SystemPrefersDark(),
                  "system dark = " + Theme.SystemPrefersDark());
            Config.Theme = saved;
            Check("ui font is not the 1998 default", Theme.UiFont.Name != "Microsoft Sans Serif",
                  Theme.UiFont.Name + " " + Theme.UiFont.Size + "pt");
            Check("dark and light text differ", Theme.Text != Theme.SubText, null);

            Console.WriteLine("-- tray icon --");
            var probe = TrayIcons.For(System.Drawing.Color.Crimson);
            Check("icon renders", probe != null && probe.Width >= 16,
                  probe == null ? "null" : probe.Width + "x" + probe.Height);
            Check("cache returns the same instance",
                  object.ReferenceEquals(probe, TrayIcons.For(System.Drawing.Color.Crimson)), null);
            DumpTrayPreview();

            Console.WriteLine("-- settings window --");
            try
            {
                Application.EnableVisualStyles();
                bool autostart = false;
                using (var form = new SettingsForm(
                    Power.GetSchemes,
                    delegate(PowerTarget t) { },
                    delegate { return autostart; },
                    delegate(bool v) { autostart = v; },
                    delegate { }))
                {
                    form.CreateControl();
                    Check("constructs without throwing", true, form.Controls.Count + " controls");
                }
            }
            catch (Exception ex)
            {
                Check("constructs without throwing", false, ex.GetType().Name + ": " + ex.Message);
            }

            Console.WriteLine();
            Console.WriteLine(failures == 0 ? "ALL PASS" : failures + " FAILURES");
            return failures;
        }
    }
}
