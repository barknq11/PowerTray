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
            BackColor = SystemColors.Window;
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
        readonly Func<bool> getAutoStart;
        readonly Action<bool> setAutoStart;
        readonly Action onChanged;

        readonly ListView list;
        readonly HotkeyBox capture;
        readonly Button assign;
        readonly Button clear;

        public SettingsForm(Func<List<PowerTarget>> getTargets, Func<bool> getAutoStart,
                            Action<bool> setAutoStart, Action onChanged)
        {
            this.getTargets = getTargets;
            this.getAutoStart = getAutoStart;
            this.setAutoStart = setAutoStart;
            this.onChanged = onChanged;

            Text = "PowerTray settings";
            ClientSize = new Size(440, 452);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            try { Icon = System.Drawing.Icon.ExtractAssociatedIcon(Application.ExecutablePath); }
            catch { }

            Controls.Add(new Label
            {
                Text = "Assign a hotkey to any plan. Select one, press a combination, then Assign.",
                Location = new Point(12, 10),
                Size = new Size(416, 32)
            });

            list = new ListView
            {
                Location = new Point(12, 44),
                Size = new Size(416, 172),
                View = View.Details,
                FullRowSelect = true,
                MultiSelect = false,
                HideSelection = false
            };
            list.Columns.Add("Plan", 270);
            list.Columns.Add("Hotkey", 122);
            list.SelectedIndexChanged += (s, e) => SyncButtons();
            Controls.Add(list);

            capture = new HotkeyBox { Location = new Point(12, 228), Size = new Size(244, 23) };
            capture.Captured += SyncButtons;
            Controls.Add(capture);

            assign = new Button { Text = "Assign", Location = new Point(264, 227), Size = new Size(78, 25) };
            assign.Click += (s, e) => AssignToSelection();
            Controls.Add(assign);

            clear = new Button { Text = "Clear", Location = new Point(350, 227), Size = new Size(78, 25) };
            clear.Click += (s, e) => ClearSelection();
            Controls.Add(clear);

            Controls.Add(new Label
            {
                Text = "A binding needs at least one of Ctrl, Alt or Shift.",
                Location = new Point(12, 256),
                Size = new Size(416, 18),
                ForeColor = SystemColors.GrayText
            });

            BuildOptions();
            Refresh_();
        }

        CheckBox autoStartBox;
        CheckBox cycleBox;
        CheckBox updateBox;

        void BuildOptions()
        {
            autoStartBox = new CheckBox
            {
                Text = "Start with Windows",
                Location = new Point(12, 284),
                Size = new Size(416, 22),
                Checked = getAutoStart()
            };
            autoStartBox.CheckedChanged += (s, e) => setAutoStart(autoStartBox.Checked);
            Controls.Add(autoStartBox);

            cycleBox = new CheckBox
            {
                Text = "Cycle through plans with Ctrl+Alt+P",
                Location = new Point(12, 310),
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
                Location = new Point(12, 336),
                Size = new Size(416, 22),
                Checked = Config.CheckForUpdates
            };
            updateBox.CheckedChanged += (s, e) => Config.CheckForUpdates = updateBox.Checked;
            Controls.Add(updateBox);

            Controls.Add(new Label
            {
                Text = "Settings are stored in HKCU\\Software\\PowerTray. Nothing is written to disk.",
                Location = new Point(12, 360),
                Size = new Size(416, 18),
                ForeColor = SystemColors.GrayText
            });

            Controls.Add(new Label
            {
                Text = "PowerTray " + Program.Version,
                Location = new Point(12, 392),
                Size = new Size(140, 18)
            });

            var link = new LinkLabel
            {
                Text = "Releases and source",
                Location = new Point(12, 412),
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
                Location = new Point(350, 408),
                Size = new Size(78, 26),
                DialogResult = DialogResult.OK
            };
            close.Click += (s, e) => Close();
            Controls.Add(close);
            AcceptButton = close;
        }

        void Refresh_()
        {
            list.BeginUpdate();
            list.Items.Clear();
            list.Groups.Clear();

            var targets = getTargets();

            ListViewGroup overlayGroup = null;
            ListViewGroup schemeGroup = null;
            foreach (var t in targets)
            {
                if (t.Kind == TargetKind.Overlay && overlayGroup == null)
                {
                    overlayGroup = new ListViewGroup("Power mode (this device's slider)");
                    list.Groups.Add(overlayGroup);
                }
                if (t.Kind == TargetKind.Scheme && schemeGroup == null)
                {
                    schemeGroup = new ListViewGroup("Power plans");
                    list.Groups.Add(schemeGroup);
                }
            }

            foreach (var t in targets)
            {
                string encoded = Config.GetHotkey(t.Key);
                Hotkey hk;
                string shown = Hotkey.TryDecode(encoded, out hk) ? hk.ToString() : "";

                var item = new ListViewItem(t.Active ? t.Name + "   (active)" : t.Name);
                item.SubItems.Add(shown);
                item.Tag = t;
                item.Group = t.Kind == TargetKind.Overlay ? overlayGroup : schemeGroup;
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
            assign.Enabled = target != null && capture.Value.IsUsable;
            clear.Enabled = target != null && Config.GetHotkey(target.Key) != null;
        }

        void AssignToSelection()
        {
            PowerTarget target = Selected;
            if (target == null || !capture.Value.IsUsable) return;

            string encoded = capture.Value.Encode();

            // One combination can only mean one thing, so hand it over rather than
            // leaving two plans claiming a binding only one of them will actually get.
            foreach (var other in getTargets())
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
