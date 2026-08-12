using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;

namespace PowerTray
{
    enum ThemeMode { Auto, Light, Dark }

    // WinForms has no dark mode of its own, so this is hand-rolled. Everything here
    // uses documented entry points: reading the Personalize key, SetWindowTheme, and
    // DwmSetWindowAttribute. The undocumented uxtheme ordinals that some apps call to
    // get dark scrollbars are deliberately avoided - they can vanish in a Windows update
    // and would take the whole settings window down with them.
    static class Theme
    {
        const string PersonalizeKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

        public static ThemeMode Mode
        {
            get { return Config.Theme; }
            set { Config.Theme = value; }
        }

        public static bool SystemPrefersDark()
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(PersonalizeKey))
                {
                    if (key == null) return false;
                    object v = key.GetValue("AppsUseLightTheme");
                    return v is int && (int)v == 0;
                }
            }
            catch { return false; }
        }

        public static bool Dark
        {
            get
            {
                ThemeMode mode = Mode;
                if (mode == ThemeMode.Light) return false;
                if (mode == ThemeMode.Dark) return true;
                return SystemPrefersDark();
            }
        }

        public static Color Window { get { return Dark ? Color.FromArgb(32, 32, 32) : SystemColors.Control; } }
        public static Color Surface { get { return Dark ? Color.FromArgb(43, 43, 43) : SystemColors.Window; } }
        public static Color Text { get { return Dark ? Color.FromArgb(240, 240, 240) : SystemColors.ControlText; } }
        public static Color SubText { get { return Dark ? Color.FromArgb(160, 160, 160) : SystemColors.GrayText; } }
        public static Color Border { get { return Dark ? Color.FromArgb(70, 70, 70) : SystemColors.ControlDark; } }
        public static Color Selection { get { return Dark ? Color.FromArgb(0, 90, 158) : SystemColors.Highlight; } }
        public static Color SelectionText { get { return Dark ? Color.White : SystemColors.HighlightText; } }
        public static Color Hover { get { return Dark ? Color.FromArgb(60, 60, 60) : SystemColors.ControlLight; } }

        // The taskbar follows the system setting, not our override: an app forced to
        // light mode on a dark desktop still sits in a dark tray.
        public static Color TrayContrast { get { return SystemPrefersDark() ? Color.FromArgb(225, 225, 225) : Color.FromArgb(60, 60, 60); } }

        [DllImport("dwmapi.dll")]
        static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

        [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
        static extern int SetWindowTheme(IntPtr hwnd, string subAppName, string subIdList);

        const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
        const int DWMWA_USE_IMMERSIVE_DARK_MODE_LEGACY = 19;

        public static void ApplyTitleBar(IntPtr handle)
        {
            if (handle == IntPtr.Zero) return;
            int on = Dark ? 1 : 0;
            try
            {
                // Attribute 20 since Windows 10 2004; 19 on the builds before it. Both
                // simply fail on older Windows, leaving a light title bar.
                if (DwmSetWindowAttribute(handle, DWMWA_USE_IMMERSIVE_DARK_MODE, ref on, sizeof(int)) != 0)
                    DwmSetWindowAttribute(handle, DWMWA_USE_IMMERSIVE_DARK_MODE_LEGACY, ref on, sizeof(int));
            }
            catch { }
        }

        public static void ApplyScrollbars(IntPtr handle)
        {
            if (handle == IntPtr.Zero) return;
            try { SetWindowTheme(handle, Dark ? "DarkMode_Explorer" : "Explorer", null); }
            catch { }
        }

        public static Font UiFont
        {
            get
            {
                // The WinForms default is Microsoft Sans Serif 8.25pt, which is what
                // Windows shipped in 1998. MessageBoxFont is the actual system UI font
                // (Segoe UI 9pt on anything modern) and is what native apps use.
                try { return SystemFonts.MessageBoxFont; }
                catch { return SystemFonts.DefaultFont; }
            }
        }

        public static ToolStripRenderer MenuRenderer()
        {
            return Dark ? (ToolStripRenderer)new DarkMenuRenderer() : new ToolStripProfessionalRenderer();
        }
    }

    class DarkColorTable : ProfessionalColorTable
    {
        public override Color ToolStripDropDownBackground { get { return Theme.Surface; } }
        public override Color ImageMarginGradientBegin { get { return Theme.Surface; } }
        public override Color ImageMarginGradientMiddle { get { return Theme.Surface; } }
        public override Color ImageMarginGradientEnd { get { return Theme.Surface; } }
        public override Color MenuBorder { get { return Theme.Border; } }
        public override Color MenuItemBorder { get { return Theme.Border; } }
        public override Color MenuItemSelected { get { return Theme.Hover; } }
        public override Color MenuItemSelectedGradientBegin { get { return Theme.Hover; } }
        public override Color MenuItemSelectedGradientEnd { get { return Theme.Hover; } }
        public override Color MenuItemPressedGradientBegin { get { return Theme.Hover; } }
        public override Color MenuItemPressedGradientEnd { get { return Theme.Hover; } }
        public override Color SeparatorDark { get { return Theme.Border; } }
        public override Color SeparatorLight { get { return Theme.Surface; } }
        public override Color CheckBackground { get { return Theme.Selection; } }
        public override Color CheckSelectedBackground { get { return Theme.Selection; } }
        public override Color CheckPressedBackground { get { return Theme.Selection; } }
    }

    class DarkMenuRenderer : ToolStripProfessionalRenderer
    {
        public DarkMenuRenderer() : base(new DarkColorTable()) { }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            // Disabled items are the section headers ("Power mode", "Power plan"), so
            // the dimmer colour reads as a heading rather than as something broken.
            e.TextColor = e.Item.Enabled ? Theme.Text : Theme.SubText;
            base.OnRenderItemText(e);
        }

        protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
        {
            e.ArrowColor = Theme.Text;
            base.OnRenderArrow(e);
        }
    }
}
