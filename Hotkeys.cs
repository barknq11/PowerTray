using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace PowerTray
{
    struct Hotkey
    {
        public const uint MOD_ALT = 0x1;
        public const uint MOD_CONTROL = 0x2;
        public const uint MOD_SHIFT = 0x4;
        public const uint MOD_NOREPEAT = 0x4000;

        public uint Modifiers;
        public uint Key;

        public bool IsEmpty { get { return Key == 0; } }

        public string Encode() { return Modifiers + ":" + Key; }

        public static bool TryDecode(string encoded, out Hotkey hotkey)
        {
            hotkey = new Hotkey();
            if (string.IsNullOrEmpty(encoded)) return false;

            string[] parts = encoded.Split(':');
            uint mods, key;
            if (parts.Length != 2) return false;
            if (!uint.TryParse(parts[0], out mods) || !uint.TryParse(parts[1], out key)) return false;
            if (key == 0) return false;

            hotkey.Modifiers = mods;
            hotkey.Key = key;
            return true;
        }

        // Converts a KeyDown into a binding. Returns an empty hotkey while the user is
        // still only holding modifiers, which is what makes capture-as-you-type work.
        public static Hotkey FromKeyData(Keys keyData)
        {
            var hotkey = new Hotkey();
            if ((keyData & Keys.Control) == Keys.Control) hotkey.Modifiers |= MOD_CONTROL;
            if ((keyData & Keys.Alt) == Keys.Alt) hotkey.Modifiers |= MOD_ALT;
            if ((keyData & Keys.Shift) == Keys.Shift) hotkey.Modifiers |= MOD_SHIFT;

            Keys code = keyData & Keys.KeyCode;
            if (code == Keys.ControlKey || code == Keys.Menu || code == Keys.ShiftKey ||
                code == Keys.LWin || code == Keys.RWin || code == Keys.None)
                return hotkey;

            hotkey.Key = (uint)code;
            return hotkey;
        }

        // A binding with no modifier would swallow that key system-wide, so require one.
        public bool IsUsable { get { return !IsEmpty && Modifiers != 0; } }

        public override string ToString()
        {
            if (IsEmpty) return "";
            string text = "";
            if ((Modifiers & MOD_CONTROL) != 0) text += "Ctrl+";
            if ((Modifiers & MOD_ALT) != 0) text += "Alt+";
            if ((Modifiers & MOD_SHIFT) != 0) text += "Shift+";
            return text + FriendlyKeyName((Keys)Key);
        }

        static string FriendlyKeyName(Keys key)
        {
            if (key >= Keys.D0 && key <= Keys.D9) return ((char)('0' + (key - Keys.D0))).ToString();
            if (key >= Keys.NumPad0 && key <= Keys.NumPad9) return "Num" + (key - Keys.NumPad0);
            return key.ToString();
        }
    }

    class HotkeyWindow : NativeWindow
    {
        const int WM_HOTKEY = 0x0312;
        public event Action<int> Pressed;

        public HotkeyWindow() { CreateHandle(new CreateParams()); }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_HOTKEY && Pressed != null) Pressed((int)m.WParam);
            base.WndProc(ref m);
        }
    }

    class HotkeyManager : IDisposable
    {
        [DllImport("user32.dll")]
        static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll")]
        static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        readonly HotkeyWindow window = new HotkeyWindow();
        readonly Dictionary<int, Action> actions = new Dictionary<int, Action>();
        readonly List<string> failures = new List<string>();
        int nextId = 1;

        public HotkeyManager()
        {
            window.Pressed += id =>
            {
                Action action;
                if (actions.TryGetValue(id, out action)) action();
            };
        }

        // Bindings other apps already own fail here. Collecting them lets the caller
        // tell the user once, rather than leaving a dead key that looks like a bug.
        public IList<string> Failures { get { return failures; } }

        public bool Register(Hotkey hotkey, string label, Action action)
        {
            if (!hotkey.IsUsable) return false;

            int id = nextId++;
            // MOD_NOREPEAT: holding the key should switch once, not spam the API.
            if (!RegisterHotKey(window.Handle, id, hotkey.Modifiers | Hotkey.MOD_NOREPEAT, hotkey.Key))
            {
                failures.Add(label + "  (" + hotkey + ")");
                return false;
            }

            actions[id] = action;
            return true;
        }

        public void UnregisterAll()
        {
            foreach (int id in new List<int>(actions.Keys))
                UnregisterHotKey(window.Handle, id);

            actions.Clear();
            failures.Clear();
            nextId = 1;
        }

        public void Dispose()
        {
            UnregisterAll();
            window.DestroyHandle();
        }
    }
}
