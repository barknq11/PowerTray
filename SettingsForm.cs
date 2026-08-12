using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace PowerTray
{
    // A textbox that swallows keystrokes and reports them as a binding. ProcessCmdKey
    // rather than KeyDown because Alt and Ctrl combinations are intercepted as command
    // keys before KeyDown ever fires, which is why naive capture boxes miss Alt+1.
    class HotkeyBox : TextBox
    {
        // Exposed as a property returning a copy: a struct field on a Control (which is
        // marshal-by-reference) triggers CS1690 at every call site that touches it.
        Hotkey current;
        public Hotkey Value { get { return current; } }

        public event Action Captured;

        public HotkeyBox()
        {
            ReadOnly = true;
            Text = "Click here, then press a combination";
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (!Focused) return base.ProcessCmdKey(ref msg, keyData);

            Keys code = keyData & Keys.KeyCode;
            if (code == Keys.Tab || code == Keys.Escape)
                return base.ProcessCmdKey(ref msg, keyData);

            Hotkey candidate = Hotkey.FromKeyData(keyData);
            if (candidate.IsUsable)
            {
                current = candidate;
                Text = candidate.ToString();
                if (Captured != null) Captured();
            }
            return true;
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            e.Handled = true;
            e.SuppressKeyPress = true;
        }
    }

    class SettingsForm : Form
    {
        readonly Func<List<PowerTarget>> getTargets;
        readonly Action<PowerTarget> activateTarget;
        readonly Func<bool> getAutoStart;
        readonly Action<bool> setAutoStart;
        readonly Action onChanged;

        readonly ListView list = new ListView();
        readonly HotkeyBox capture = new HotkeyBox();
        readonly Button activate = new Button();
        readonly Button assign = new Button();
        readonly Button clear = new Button();
        readonly Button close = new Button();
        readonly CheckBox autoStartBox = new CheckBox();
        readonly CheckBox cycleBox = new CheckBox();
        readonly CheckBox updateBox = new CheckBox();
        readonly RadioButton themeAuto = new RadioButton();
        readonly RadioButton themeLight = new RadioButton();
        readonly RadioButton themeDark = new RadioButton();
        readonly LinkLabel link = new LinkLabel();

        bool suppressThemeEvents;

        public SettingsForm(Func<List<PowerTarget>> getTargets, Action<PowerTarget> activateTarget,
                            Func<bool> getAutoStart, Action<bool> setAutoStart, Action onChanged)
        {
            this.getTargets = getTargets;
            this.activateTarget = activateTarget;
            this.getAutoStart = getAutoStart;
            this.setAutoStart = setAutoStart;
            this.onChanged = onChanged;

            // Font must be set before any control exists. Assigning it afterwards makes
            // AutoScaleMode.Font rescale the whole layout, which is what pushed the
            // Close button and link off the bottom of the previous build.
            AutoScaleMode = AutoScaleMode.Dpi;
            Font = Theme.UiFont;

            Text = "PowerTray settings";
            FormBorderStyle = FormBorderStyle.Sizable;
            MinimumSize = new Size(430, 520);
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = Config.WindowSize;
            try { Icon = System.Drawing.Icon.ExtractAssociatedIcon(Application.ExecutablePath); }
            catch { }

            Controls.Add(BuildLayout());

            ApplyTheme();
            Refresh_();
        }

        static Label Caption(string text, bool muted)
        {
            return new Label
            {
                Text = text,
                AutoSize = true,
                Margin = new Padding(3, 6, 3, 3),
                Tag = muted ? "muted" : null
            };
        }

        static TableLayoutPanel Stack(int rows)
        {
            var t = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = rows,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Margin = Padding.Empty
            };
            t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            return t;
        }

        static FlowLayoutPanel Row()
        {
            return new FlowLayoutPanel
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                Margin = new Padding(0, 2, 0, 2),
                Dock = DockStyle.Fill
            };
        }

        // Docked/auto-sizing layout rather than absolute coordinates: the window is
        // resizable now, and hardcoded pixel positions break the moment the font or
        // the DPI differs from whatever the numbers were written against.
        Control BuildLayout()
        {
            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 3,
                Padding = new Padding(12, 10, 12, 10)
            };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));   // list absorbs resizing
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            root.Controls.Add(Caption("Select a plan to switch to it, or give it a hotkey.", false), 0, 0);

            list.Dock = DockStyle.Fill;
            list.View = View.Details;
            list.FullRowSelect = true;
            list.MultiSelect = false;
            list.HideSelection = false;
            list.Margin = new Padding(0, 4, 0, 6);
            list.SelectedIndexChanged += (s, e) => SyncButtons();
            list.DoubleClick += (s, e) => ActivateSelection();
            list.DrawColumnHeader += OnDrawColumnHeader;
            list.DrawItem += (s, e) => e.DrawDefault = false;
            list.DrawSubItem += OnDrawSubItem;
            list.Resize += (s, e) => FitColumns();
            root.Controls.Add(list, 0, 1);

            root.Controls.Add(BuildBottom(), 0, 2);
            return root;
        }

        Control BuildBottom()
        {
            var stack = Stack(9);

            // Switching must not depend on hotkeys working. Some people never want
            // global bindings, and a combination another app owns cannot be registered.
            activate.Text = "Activate";
            activate.AutoSize = true;
            activate.Padding = new Padding(10, 3, 10, 3);
            activate.Click += (s, e) => ActivateSelection();

            var activateRow = Row();
            activateRow.Controls.Add(activate);
            activateRow.Controls.Add(Caption("or double-click a plan", true));
            stack.Controls.Add(activateRow, 0, 0);

            stack.Controls.Add(Caption("Hotkey for the selected plan", false), 0, 1);

            var hotkeyRow = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 3,
                RowCount = 1,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Margin = new Padding(0, 2, 0, 2)
            };
            hotkeyRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            hotkeyRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            hotkeyRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            capture.Dock = DockStyle.Fill;
            capture.Captured += SyncButtons;
            hotkeyRow.Controls.Add(capture, 0, 0);

            assign.Text = "Assign";
            assign.AutoSize = true;
            assign.Padding = new Padding(10, 2, 10, 2);
            assign.Click += (s, e) => AssignToSelection();
            hotkeyRow.Controls.Add(assign, 1, 0);

            clear.Text = "Clear";
            clear.AutoSize = true;
            clear.Padding = new Padding(10, 2, 10, 2);
            clear.Click += (s, e) => ClearSelection();
            hotkeyRow.Controls.Add(clear, 2, 0);
            stack.Controls.Add(hotkeyRow, 0, 2);

            stack.Controls.Add(Caption("A binding needs at least one of Ctrl, Alt or Shift.", true), 0, 3);

            autoStartBox.Text = "Start with Windows";
            autoStartBox.AutoSize = true;
            autoStartBox.Checked = getAutoStart();
            autoStartBox.CheckedChanged += (s, e) => setAutoStart(autoStartBox.Checked);
            stack.Controls.Add(autoStartBox, 0, 4);

            cycleBox.Text = "Cycle through plans with Ctrl+Alt+P";
            cycleBox.AutoSize = true;
            cycleBox.Checked = Config.CycleHotkeyEnabled;
            cycleBox.CheckedChanged += (s, e) =>
            {
                Config.CycleHotkeyEnabled = cycleBox.Checked;
                onChanged();
            };
            stack.Controls.Add(cycleBox, 0, 5);

            updateBox.Text = "Check GitHub for new versions at startup";
            updateBox.AutoSize = true;
            updateBox.Checked = Config.CheckForUpdates;
            updateBox.CheckedChanged += (s, e) => Config.CheckForUpdates = updateBox.Checked;
            stack.Controls.Add(updateBox, 0, 6);

            stack.Controls.Add(BuildThemeRow(), 0, 7);
            stack.Controls.Add(BuildFooter(), 0, 8);
            return stack;
        }

        // Radio buttons rather than a ComboBox: a WinForms combo keeps drawing its drop
        // button in system colours no matter what you set, so it stays a light chip on a
        // dark form. Three options do not need a dropdown anyway.
        Control BuildThemeRow()
        {
            var row = Row();
            row.Controls.Add(Caption("Appearance", false));

            ThemeMode mode = Config.Theme;
            SetupThemeOption(themeAuto, "Auto", ThemeMode.Auto, mode);
            SetupThemeOption(themeLight, "Light", ThemeMode.Light, mode);
            SetupThemeOption(themeDark, "Dark", ThemeMode.Dark, mode);

            row.Controls.Add(themeAuto);
            row.Controls.Add(themeLight);
            row.Controls.Add(themeDark);
            return row;
        }

        void SetupThemeOption(RadioButton button, string text, ThemeMode mode, ThemeMode active)
        {
            button.Text = text;
            button.AutoSize = true;
            button.Margin = new Padding(10, 6, 0, 3);
            button.Checked = mode == active;
            button.CheckedChanged += (s, e) =>
            {
                if (suppressThemeEvents || !button.Checked) return;
                Config.Theme = mode;
                ApplyTheme();
                onChanged();
            };
        }

        Control BuildFooter()
        {
            var footer = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 2,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Margin = new Padding(0, 6, 0, 0)
            };
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            footer.Controls.Add(Caption("Settings live in HKCU\\Software\\PowerTray. Nothing is written to disk.", true), 0, 0);

            var idRow = Row();
            idRow.Controls.Add(Caption("PowerTray " + Program.Version, false));

            link.Text = "Releases and source";
            link.AutoSize = true;
            link.Margin = new Padding(12, 6, 3, 3);
            link.LinkClicked += (s, e) =>
            {
                try { System.Diagnostics.Process.Start(Updater.ReleasesPage); }
                catch { }
            };
            idRow.Controls.Add(link);
            footer.Controls.Add(idRow, 0, 1);

            close.Text = "Close";
            close.AutoSize = true;
            close.Padding = new Padding(14, 3, 14, 3);
            close.Anchor = AnchorStyles.Right | AnchorStyles.Bottom;
            close.Click += (s, e) => Close();
            footer.Controls.Add(close, 1, 1);
            AcceptButton = close;

            return footer;
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (WindowState == FormWindowState.Normal) Config.WindowSize = ClientSize;
            base.OnFormClosing(e);
        }

        // Sizes the last column so the header exactly fills the control. Otherwise the
        // leftover strip past the final column is painted by the system in light colours
        // and shows up as a white bar in dark mode.
        void FitColumns()
        {
            if (list.Columns.Count == 0) return;

            int hotkey = 130;
            int type = list.Columns.Count == 3 ? 100 : 0;
            int plan = list.ClientSize.Width - hotkey - type - 1;
            if (plan < 120) plan = 120;

            list.Columns[0].Width = plan;
            if (list.Columns.Count == 3)
            {
                list.Columns[1].Width = type;
                list.Columns[2].Width = hotkey;
            }
            else
            {
                list.Columns[1].Width = hotkey;
            }
        }

        void ApplyTheme()
        {
            BackColor = Theme.Window;
            ForeColor = Theme.Text;
            Theme.ApplyTitleBar(Handle);

            StyleTree(this);

            list.BackColor = Theme.Surface;
            list.ForeColor = Theme.Text;
            list.BorderStyle = Theme.Dark ? BorderStyle.FixedSingle : BorderStyle.Fixed3D;

            // Owner-draw only in dark mode: in light mode the native rendering is better
            // than anything hand-drawn, and there is no reason to replace it.
            list.OwnerDraw = Theme.Dark;
            Theme.ApplyScrollbars(list.Handle);
            list.Invalidate();

            suppressThemeEvents = true;
            ThemeMode mode = Config.Theme;
            themeAuto.Checked = mode == ThemeMode.Auto;
            themeLight.Checked = mode == ThemeMode.Light;
            themeDark.Checked = mode == ThemeMode.Dark;
            suppressThemeEvents = false;
        }

        void StyleTree(Control parent)
        {
            foreach (Control control in parent.Controls)
            {
                StyleControl(control);
                if (control.Controls.Count > 0) StyleTree(control);
            }
        }

        void StyleControl(Control control)
        {
            if (control == list) return;

            var button = control as Button;
            if (button != null)
            {
                button.FlatStyle = Theme.Dark ? FlatStyle.Flat : FlatStyle.Standard;
                button.BackColor = Theme.Dark ? Theme.Hover : SystemColors.Control;
                button.ForeColor = Theme.Text;
                button.UseVisualStyleBackColor = !Theme.Dark;
                button.FlatAppearance.BorderColor = Theme.Border;
                return;
            }

            var linkLabel = control as LinkLabel;
            if (linkLabel != null)
            {
                linkLabel.BackColor = Color.Transparent;
                linkLabel.LinkColor = Theme.Dark ? Color.FromArgb(96, 174, 255) : Color.FromArgb(0, 102, 204);
                linkLabel.ActiveLinkColor = linkLabel.LinkColor;
                linkLabel.VisitedLinkColor = linkLabel.LinkColor;
                return;
            }

            var label = control as Label;
            if (label != null)
            {
                label.BackColor = Color.Transparent;
                label.ForeColor = (label.Tag as string) == "muted" ? Theme.SubText : Theme.Text;
                return;
            }

            if (control is CheckBox || control is RadioButton)
            {
                control.BackColor = Color.Transparent;
                control.ForeColor = Theme.Text;
                return;
            }

            if (control is TextBox)
            {
                control.BackColor = Theme.Surface;
                control.ForeColor = Theme.Text;
                return;
            }

            if (control is TableLayoutPanel || control is FlowLayoutPanel)
                control.BackColor = Color.Transparent;
        }

        void OnDrawColumnHeader(object sender, DrawListViewColumnHeaderEventArgs e)
        {
            if (!Theme.Dark) { e.DrawDefault = true; return; }

            using (var back = new SolidBrush(Theme.Window))
                e.Graphics.FillRectangle(back, e.Bounds);
            using (var pen = new Pen(Theme.Border))
            {
                e.Graphics.DrawLine(pen, e.Bounds.Right - 1, e.Bounds.Top + 4, e.Bounds.Right - 1, e.Bounds.Bottom - 5);
                e.Graphics.DrawLine(pen, e.Bounds.Left, e.Bounds.Bottom - 1, e.Bounds.Right, e.Bounds.Bottom - 1);
            }

            Rectangle text = e.Bounds;
            text.X += 6;
            text.Width -= 10;
            TextRenderer.DrawText(e.Graphics, e.Header.Text, Font, text, Theme.SubText,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        }

        void OnDrawSubItem(object sender, DrawListViewSubItemEventArgs e)
        {
            if (!Theme.Dark) { e.DrawDefault = true; return; }

            bool selected = e.Item.Selected;
            using (var back = new SolidBrush(selected ? Theme.Selection : Theme.Surface))
                e.Graphics.FillRectangle(back, e.Bounds);

            Rectangle text = e.Bounds;
            text.X += 6;
            text.Width -= 10;
            TextRenderer.DrawText(e.Graphics, e.SubItem.Text, Font, text,
                selected ? Theme.SelectionText : Theme.Text,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        }

        void Refresh_()
        {
            // Rebuilding the list drops the selection, which is jarring right after the
            // user acted on a row, so remember it by key and put it back.
            PowerTarget previous = Selected;
            string keepKey = previous == null ? null : previous.Key;

            List<PowerTarget> targets = getTargets();

            bool hasOverlays = false;
            foreach (PowerTarget t in targets)
                if (t.Kind == TargetKind.Overlay) { hasOverlays = true; break; }

            list.BeginUpdate();
            list.Items.Clear();

            // A Type column instead of ListView groups: group headers are drawn by the
            // system and stay light in dark mode unless you call undocumented uxtheme
            // entry points. The column costs one field and themes cleanly.
            list.Columns.Clear();
            list.Columns.Add("Plan");
            if (hasOverlays) list.Columns.Add("Type");
            list.Columns.Add("Hotkey");
            FitColumns();

            foreach (PowerTarget t in targets)
            {
                Hotkey bound;
                string shown = Hotkey.TryDecode(Config.GetHotkey(t.Key), out bound) ? bound.ToString() : "";

                var item = new ListViewItem(t.Active ? t.Name + "   (active)" : t.Name);
                if (hasOverlays) item.SubItems.Add(t.Kind == TargetKind.Overlay ? "Power mode" : "Plan");
                item.SubItems.Add(shown);
                item.Tag = t;
                if (t.Key == keepKey) item.Selected = true;
                list.Items.Add(item);
            }

            list.EndUpdate();
            SyncButtons();
        }

        PowerTarget Selected
        {
            get { return list.SelectedItems.Count == 0 ? null : (PowerTarget)list.SelectedItems[0].Tag; }
        }

        void SyncButtons()
        {
            PowerTarget target = Selected;
            activate.Enabled = target != null && !target.Active;
            assign.Enabled = target != null && capture.Value.IsUsable;
            clear.Enabled = target != null && Config.GetHotkey(target.Key) != null;
        }

        void ActivateSelection()
        {
            PowerTarget target = Selected;
            if (target == null || target.Active) return;

            activateTarget(target);
            Refresh_();   // moves the "(active)" marker without waiting for the poll
        }

        void AssignToSelection()
        {
            PowerTarget target = Selected;
            if (target == null || !capture.Value.IsUsable) return;

            string encoded = capture.Value.Encode();

            // One combination can only mean one thing, so hand it over rather than
            // leaving two plans claiming a binding only one of them will actually get.
            foreach (PowerTarget other in getTargets())
                if (other.Key != target.Key && Config.GetHotkey(other.Key) == encoded)
                    Config.ClearHotkey(other.Key);

            Config.SetHotkey(target.Key, encoded);
            Refresh_();
            onChanged();
        }

        void ClearSelection()
        {
            PowerTarget target = Selected;
            if (target == null) return;

            Config.ClearHotkey(target.Key);
            Refresh_();
            onChanged();
        }
    }
}
