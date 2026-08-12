using System;
using System.Drawing;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;

namespace PowerTray
{
    static class Program
    {
        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.Run(new TrayApp());
        }
    }

    class PlanForm : Form
    {
        readonly ListBox list;
        readonly Func<List<Plan>> getPlans;
        readonly Action<string> setActive;
        List<Plan> plans;

        public PlanForm(Func<List<Plan>> getPlans, Action<string> setActive)
        {
            this.getPlans = getPlans;
            this.setActive = setActive;

            Text = "PowerTray";
            Width = 320;
            Height = 260;
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;

            list = new ListBox { Dock = DockStyle.Top, Height = 160 };
            list.DoubleClick += (s, e) => Activate_Click();

            var activateBtn = new Button { Text = "Activate", Dock = DockStyle.Bottom, Height = 36 };
            activateBtn.Click += (s, e) => Activate_Click();

            Controls.Add(list);
            Controls.Add(activateBtn);

            Refresh_();
        }

        void Refresh_()
        {
            plans = getPlans();
            list.Items.Clear();
            int selectIndex = -1;
            for (int i = 0; i < plans.Count; i++)
            {
                list.Items.Add(plans[i].Active ? plans[i].Name + "  (active)" : plans[i].Name);
                if (plans[i].Active) selectIndex = i;
            }
            if (selectIndex >= 0) list.SelectedIndex = selectIndex;
        }

        void Activate_Click()
        {
            if (list.SelectedIndex < 0) return;
            setActive(plans[list.SelectedIndex].Guid);
            Refresh_();
        }
    }

    class Plan
    {
        public string Guid;
        public string Name;
        public bool Active;
    }

    class HotkeyWindow : NativeWindow
    {
        const int WM_HOTKEY = 0x0312;
        public event Action Pressed;

        public HotkeyWindow()
        {
            CreateHandle(new CreateParams());
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_HOTKEY && Pressed != null)
                Pressed();
            base.WndProc(ref m);
        }
    }

    class TrayApp : ApplicationContext
    {
        readonly NotifyIcon icon;
        readonly ContextMenuStrip menu;
        readonly HotkeyWindow hotkeyWindow;

        const int HOTKEY_ID = 1;
        const uint MOD_ALT = 0x1;
        const uint MOD_CONTROL = 0x2;
        const uint VK_P = 0x50;

        [DllImport("user32.dll")]
        static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);
        [DllImport("user32.dll")]
        static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        public TrayApp()
        {
            menu = new ContextMenuStrip();
            menu.Opening += (s, e) => BuildMenu();

            icon = new NotifyIcon
            {
                Icon = SystemIcons.Shield,
                Text = "PowerTray (Ctrl+Alt+P to cycle plans)",
                Visible = true,
                ContextMenuStrip = menu
            };
            icon.MouseDoubleClick += (s, e) => ShowWindow();

            hotkeyWindow = new HotkeyWindow();
            hotkeyWindow.Pressed += CycleNext;
            RegisterHotKey(hotkeyWindow.Handle, HOTKEY_ID, MOD_CONTROL | MOD_ALT, VK_P);

            BuildMenu();
            UpdateIcon();
        }

        void CycleNext()
        {
            var plans = GetPlans();
            if (plans.Count == 0) return;
            int i = plans.FindIndex(p => p.Active);
            var next = plans[(i + 1) % plans.Count];
            SetActive(next.Guid);
            UpdateIcon();
            icon.ShowBalloonTip(1500, "PowerTray", "Switched to " + next.Name, ToolTipIcon.Info);
        }

        void ShowWindow()
        {
            using (var f = new PlanForm(GetPlans, SetActive))
                f.ShowDialog();
            UpdateIcon();
        }

        readonly Dictionary<Color, Icon> iconCache = new Dictionary<Color, Icon>();

        void UpdateIcon()
        {
            var active = GetPlans().Find(p => p.Active);
            if (active != null)
                icon.Icon = GetIcon(ColorForPlan(active));
        }

        Icon GetIcon(Color c)
        {
            Icon cached;
            if (iconCache.TryGetValue(c, out cached)) return cached;

            using (var bmp = new Bitmap(16, 16))
            {
                using (var g = Graphics.FromImage(bmp))
                {
                    g.Clear(Color.Transparent);
                    using (var brush = new SolidBrush(c))
                        g.FillEllipse(brush, 1, 1, 14, 14);
                    using (var pen = new Pen(Color.Black))
                        g.DrawEllipse(pen, 1, 1, 13, 13);
                }
                var made = Icon.FromHandle(bmp.GetHicon());
                iconCache[c] = made;
                return made;
            }
        }

        // known scheme GUIDs are fixed on every Windows install; anything else gets a color hashed from its GUID
        Color ColorForPlan(Plan p)
        {
            switch (p.Guid.ToLowerInvariant())
            {
                case "381b4222-f694-41f0-9685-ff5bb260df2e": return Color.DodgerBlue;   // Balanced
                case "8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c": return Color.Crimson;      // High performance
                case "a1841308-3541-4fab-bc81-f71556f20b4a": return Color.SeaGreen;     // Power saver
                default:
                    var rand = new Random(p.Guid.GetHashCode());
                    return Color.FromArgb(rand.Next(80, 220), rand.Next(80, 220), rand.Next(80, 220));
            }
        }

        void BuildMenu()
        {
            menu.Items.Clear();

            foreach (var plan in GetPlans())
            {
                var item = new ToolStripMenuItem(plan.Name)
                {
                    Checked = plan.Active,
                    Tag = plan.Guid
                };
                item.Click += (s, e) =>
                {
                    var g = (string)((ToolStripMenuItem)s).Tag;
                    SetActive(g);
                    UpdateIcon();
                    icon.ShowBalloonTip(1500, "PowerTray", "Switched power plan.", ToolTipIcon.Info);
                };
                menu.Items.Add(item);
            }

            menu.Items.Add(new ToolStripSeparator());
            var openWin = new ToolStripMenuItem("Open Window");
            openWin.Click += (s, e) => ShowWindow();
            menu.Items.Add(openWin);

            var autoStart = new ToolStripMenuItem("Start with Windows") { Checked = IsAutoStartEnabled() };
            autoStart.Click += (s, e) => SetAutoStart(!IsAutoStartEnabled());
            menu.Items.Add(autoStart);

            var exit = new ToolStripMenuItem("Exit");
            exit.Click += (s, e) =>
            {
                UnregisterHotKey(hotkeyWindow.Handle, HOTKEY_ID);
                icon.Visible = false;
                Application.Exit();
            };
            menu.Items.Add(exit);
        }

        const uint ACCESS_SCHEME = 16;

        [DllImport("powrprof.dll")]
        static extern uint PowerEnumerate(IntPtr RootPowerKey, IntPtr SchemeGuid, IntPtr SubGroupOfPowerSettingsGuid, uint AccessFlags, uint Index, ref Guid Buffer, ref uint BufferSize);

        [DllImport("powrprof.dll")]
        static extern uint PowerReadFriendlyName(IntPtr RootPowerKey, ref Guid SchemeGuid, IntPtr SubGroupOfPowerSettingsGuid, IntPtr PowerSettingGuid, IntPtr Buffer, ref uint BufferSize);

        [DllImport("powrprof.dll")]
        static extern uint PowerGetActiveScheme(IntPtr UserRootPowerKey, out IntPtr ActivePolicyGuid);

        [DllImport("powrprof.dll")]
        static extern uint PowerSetActiveScheme(IntPtr UserRootPowerKey, ref Guid SchemeGuid);

        [DllImport("kernel32.dll")]
        static extern IntPtr LocalFree(IntPtr hMem);

        List<Plan> GetPlans()
        {
            var plans = new List<Plan>();

            IntPtr activePtr;
            PowerGetActiveScheme(IntPtr.Zero, out activePtr);
            Guid activeGuid = (Guid)Marshal.PtrToStructure(activePtr, typeof(Guid));
            LocalFree(activePtr);

            uint index = 0;
            while (true)
            {
                Guid schemeGuid = Guid.Empty;
                uint guidSize = (uint)Marshal.SizeOf(typeof(Guid));
                uint result = PowerEnumerate(IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, ACCESS_SCHEME, index, ref schemeGuid, ref guidSize);
                if (result != 0) break; // ERROR_NO_MORE_ITEMS

                plans.Add(new Plan
                {
                    Guid = schemeGuid.ToString(),
                    Name = ReadFriendlyName(schemeGuid),
                    Active = schemeGuid == activeGuid
                });
                index++;
            }
            return plans;
        }

        string ReadFriendlyName(Guid schemeGuid)
        {
            uint size = 0;
            PowerReadFriendlyName(IntPtr.Zero, ref schemeGuid, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, ref size);
            IntPtr buffer = Marshal.AllocHGlobal((int)size);
            try
            {
                PowerReadFriendlyName(IntPtr.Zero, ref schemeGuid, IntPtr.Zero, IntPtr.Zero, buffer, ref size);
                return Marshal.PtrToStringUni(buffer);
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }

        void SetActive(string guid)
        {
            Guid g = new Guid(guid);
            PowerSetActiveScheme(IntPtr.Zero, ref g);
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
            using (var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, true))
            {
                if (enable)
                    key.SetValue(RunValueName, "\"" + Application.ExecutablePath + "\"");
                else
                    key.DeleteValue(RunValueName, false);
            }
        }
    }
}
