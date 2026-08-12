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

        readonly ListView list;
        readonly HotkeyBox capture;
        readonly Button activate;
        readonly Button assign;
        readonly Button clear;
        readonly ComboBox themeBox;
        readonly CheckBox autoStartBox;
        readonly CheckBox cycleBox;
        readonly CheckBox updateBox;

        public SettingsForm(Func<List<PowerTarget>> getTargets, Action<PowerTarget> activateTarget,
                            Func<bool> getAutoStart, Action<bool> setAutoStart, Action onChanged)
        {
            this.getTargets = getTargets;
            this.activateTarget = activateTarget;
            this.getAutoStart = getAutoStart;
            this.setAutoStart = setAutoStart;
            this.onChanged = onChanged;

            Text = "PowerTray settings";
            Font = Theme.UiFont;
            ClientSize = new Size(440, 474);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            try { Icon = System.Drawing.Icon.ExtractAssociatedIcon(Application.ExecutablePath); }
            catch { }

            Controls.Add(Caption("Select a plan to switch to it, or give it a hotkey.", 12, 10, false));

            list = new ListView
            {
                Location = new Point(12, 32),
                Size = new Size(416, 154),
                View = View.Details,
                FullRowSelect = true,
                MultiSelect = false,
                HideSelection = false
            };
            list.SelectedIndexChanged += (s, e) => SyncButtons();
            list.DoubleClick += (s, e) => ActivateSelection();
            list.DrawColumnHeader += OnDrawColumnHeader;
            list.DrawItem += (s, e) => e.DrawDefault = false;
            list.DrawSubItem += OnDrawSubItem;
            Controls.Add(list);

            // Switching must not depend on hotkeys working. Some people never want
            // global bindings, and a combination another app owns cannot be registered.
            activate = new Button
            {
                Text = "Activate",
                Location = new Point(12, 192),
                Size = new Size(110, 27)
            };
            activate.Click += (s, e) => ActivateSelection();
            Controls.Add(activate);

            Controls.Add(Caption("or double-click a plan", 130, 198, true));
            Controls.Add(Caption("Hotkey for the selected plan", 12, 232, false));

            capture = new HotkeyBox { Location = new Point(12, 253), Size = new Size(244, 23) };
            capture.Captured += SyncButtons;
            Controls.Add(capture);

            assign = new Button { Text = "Assign", Location = new Point(264, 252), Size = new Size(78, 25) };
            assign.Click += (s, e) => AssignToSelection();
            Controls.Add(assign);

            clear = new Button { Text = "Clear", Location = new Point(350, 252), Size = new Size(78, 25) };
            clear.Click += (s, e) => ClearSelection();
            Controls.Add(clear);

            Controls.Add(Caption("A binding needs at least one of Ctrl, Alt or Shift.", 12, 281, true));

            autoStartBox = new CheckBox
            {
                Text = "Start with Windows",
                Location = new Point(12, 306),
                Size = new Size(416, 22),
                Checked = getAutoStart()
            };
            autoStartBox.CheckedChanged += (s, e) => setAutoStart(autoStartBox.Checked);
            Controls.Add(autoStartBox);

            cycleBox = new CheckBox
            {
                Text = "Cycle through plans with Ctrl+Alt+P",
                Location = new Point(12, 330),
                Size = new Size(416, 22),
                Checked = Config.CycleHotkeyEnabled
            };
            cycleBox.CheckedChanged += (s, e) =>
            {
                Config.CycleHotkeyEnabled = cycleBox.Checked;
                onChanged();
            };
            Controls.Add(cycleBox);

            updateBox = new CheckBox
            {
                Text = "Check GitHub for new versions at startup",
                Location = new Point(12, 354),
                Size = new Size(416, 22),
                Checked = Config.CheckForUpdates
            };
            updateBox.CheckedChanged += (s, e) => Config.CheckForUpdates = updateBox.Checked;
            Controls.Add(updateBox);

            Controls.Add(Caption("Appearance", 12, 383, false));

            themeBox = new ComboBox
            {
                Location = new Point(106, 380),
                Size = new Size(130, 23),
                DropDownStyle = ComboBoxStyle.DropDownList,
                FlatStyle = FlatStyle.Flat
            };
            themeBox.Items.AddRange(new object[] { "Auto (match Windows)", "Light", "Dark" });
            themeBox.SelectedIndex = (int)Config.Theme;
            themeBox.SelectedIndexChanged += (s, e) =>
            {
                Config.Theme = (ThemeMode)themeBox.SelectedIndex;
                ApplyTheme();
                onChanged();
            };
            Controls.Add(themeBox);

            Controls.Add(Caption("Settings live in HKCU\\Software\\PowerTray. Nothing is written to disk.", 12, 410, true));
            Controls.Add(Caption("PowerTray " + Program.Version, 12, 438, false));

            var link = new LinkLabel
            {
                Text = "Releases and source",
                Location = new Point(140, 438),
                Size = new Size(160, 18)
            };
            link.LinkClicked += (s, e) =>
            {
                try { System.Diagnostics.Process.Start(Updater.ReleasesPage); }
                catch { }
            };
            Controls.Add(link);

            var close = new Button
            {
                Text = "Close",
                Location = new Point(350, 434),
                Size = new Size(78, 26),
                DialogResult = DialogResult.OK
            };
            close.Click += (s, e) => Close();
            Controls.Add(close);
            AcceptButton = close;

            ApplyTheme();
            Refresh_();
        }

        // Tag marks the muted secondary labels so re-theming can tell them apart from
        // primary text without keeping a separate list of references.
        static Label Caption(string text, int x, int y, bool muted)
        {
            return new Label
            {
                Text = text,
                Location = new Point(x, y),
                Size = new Size(x == 12 ? 416 : 180, 18),
                AutoSize = false,
                Tag = muted ? "muted" : null
            };
        }

        void ApplyTheme()
        {
            Font = Theme.UiFont;
            BackColor = Theme.Window;
            ForeColor = Theme.Text;
            Theme.ApplyTitleBar(Handle);

            foreach (Control control in Controls) StyleControl(control);

            list.BackColor = Theme.Surface;
            list.ForeColor = Theme.Text;
            list.BorderStyle = Theme.Dark ? BorderStyle.FixedSingle : BorderStyle.Fixed3D;

            // Owner-draw only in dark mode: in light mode the native rendering is better
            // than anything hand-drawn, and there is no reason to replace it.
            list.OwnerDraw = Theme.Dark;
            Theme.ApplyScrollbars(list.Handle);
            list.Invalidate();
        }

        void StyleControl(Control control)
        {
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

            if (control is CheckBox)
            {
                control.BackColor = Color.Transparent;
                control.ForeColor = Theme.Text;
                return;
            }

            if (control is TextBox || control is ComboBox)
            {
                control.BackColor = Theme.Surface;
                control.ForeColor = Theme.Text;
            }
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
            list.Columns.Add("Plan", hasOverlays ? 196 : 286);
            if (hasOverlays) list.Columns.Add("Type", 90);
            list.Columns.Add("Hotkey", 122);

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
