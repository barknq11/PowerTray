using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace PowerTray
{
    // Windows exposes two independent notions of "power setting":
    //
    //   Scheme  - the classic power plan (Balanced, High performance, ...).
    //   Overlay - the Windows 10/11 "Power mode" slider on battery-capable devices.
    //
    // On most modern laptops the scheme is permanently Balanced and the overlay is
    // what the user actually feels, so treating only schemes as real would make the
    // app look broken there. Both are modelled as a PowerTarget so the menu, the
    // hotkey manager, and the settings window can stay ignorant of the difference.
    enum TargetKind { Scheme, Overlay }

    class PowerTarget
    {
        public TargetKind Kind;
        public Guid Id;
        public string Name;
        public bool Active;

        // Stable identity for registry keys and menu tags.
        public string Key { get { return (Kind == TargetKind.Scheme ? "S_" : "O_") + Id.ToString("D"); } }
    }

    static class Power
    {
        const uint ACCESS_SCHEME = 16;
        const uint ERROR_SUCCESS = 0;

        public static readonly Guid Balanced = new Guid("381b4222-f694-41f0-9685-ff5bb260df2e");
        public static readonly Guid HighPerformance = new Guid("8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c");
        public static readonly Guid PowerSaver = new Guid("a1841308-3541-4fab-bc81-f71556f20b4a");
        public static readonly Guid UltimatePerformance = new Guid("80c5d2a2-a757-4aaf-a446-d8b3c15045ef");

        // Overlay GUIDs. The zero GUID is "Balanced/Recommended" - the slider's middle
        // position - which is why it is a legitimate value rather than an error.
        public static readonly Guid OverlayBalanced = Guid.Empty;
        public static readonly Guid OverlayBestEfficiency = new Guid("961cc777-2547-4f9d-8174-7d86181b8a7a");
        public static readonly Guid OverlayBestPerformance = new Guid("ded574b5-45a0-4f42-8737-46345c09c238");

        [DllImport("powrprof.dll")]
        static extern uint PowerEnumerate(IntPtr RootPowerKey, IntPtr SchemeGuid, IntPtr SubGroupOfPowerSettingsGuid,
            uint AccessFlags, uint Index, ref Guid Buffer, ref uint BufferSize);

        [DllImport("powrprof.dll")]
        static extern uint PowerReadFriendlyName(IntPtr RootPowerKey, ref Guid SchemeGuid,
            IntPtr SubGroupOfPowerSettingsGuid, IntPtr PowerSettingGuid, IntPtr Buffer, ref uint BufferSize);

        [DllImport("powrprof.dll")]
        static extern uint PowerGetActiveScheme(IntPtr UserRootPowerKey, out IntPtr ActivePolicyGuid);

        [DllImport("powrprof.dll")]
        static extern uint PowerSetActiveScheme(IntPtr UserRootPowerKey, ref Guid SchemeGuid);

        [DllImport("powrprof.dll")]
        static extern uint PowerDuplicateScheme(IntPtr RootPowerKey, ref Guid SourceSchemeGuid, ref IntPtr DestinationSchemeGuid);

        // Undocumented but exported by name from powrprof.dll since Windows 10 1709.
        // Wrapped in try/catch everywhere because there is no contract guaranteeing them.
        [DllImport("powrprof.dll")]
        static extern uint PowerSetActiveOverlayScheme(Guid OverlaySchemeGuid);

        [DllImport("powrprof.dll")]
        static extern uint PowerGetEffectiveOverlayScheme(out Guid EffectiveOverlayGuid);

        [DllImport("kernel32.dll")]
        static extern IntPtr LocalFree(IntPtr hMem);

        public static Guid GetActiveScheme()
        {
            IntPtr ptr;
            if (PowerGetActiveScheme(IntPtr.Zero, out ptr) != ERROR_SUCCESS) return Guid.Empty;
            try { return (Guid)Marshal.PtrToStructure(ptr, typeof(Guid)); }
            finally { LocalFree(ptr); }
        }

        public static string ReadFriendlyName(Guid scheme)
        {
            uint size = 0;
            PowerReadFriendlyName(IntPtr.Zero, ref scheme, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, ref size);
            if (size == 0) return scheme.ToString("D");

            IntPtr buffer = Marshal.AllocHGlobal((int)size);
            try
            {
                if (PowerReadFriendlyName(IntPtr.Zero, ref scheme, IntPtr.Zero, IntPtr.Zero, buffer, ref size) != ERROR_SUCCESS)
                    return scheme.ToString("D");
                return Marshal.PtrToStringUni(buffer);
            }
            finally { Marshal.FreeHGlobal(buffer); }
        }

        public static List<PowerTarget> GetSchemes()
        {
            var list = new List<PowerTarget>();
            Guid active = GetActiveScheme();

            uint index = 0;
            while (true)
            {
                Guid scheme = Guid.Empty;
                uint size = (uint)Marshal.SizeOf(typeof(Guid));
                if (PowerEnumerate(IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, ACCESS_SCHEME, index, ref scheme, ref size) != ERROR_SUCCESS)
                    break;

                list.Add(new PowerTarget
                {
                    Kind = TargetKind.Scheme,
                    Id = scheme,
                    Name = ReadFriendlyName(scheme),
                    Active = scheme == active
                });
                index++;
            }
            return list;
        }

        public static void SetScheme(Guid scheme)
        {
            PowerSetActiveScheme(IntPtr.Zero, ref scheme);
        }

        public static bool HasUltimatePerformance()
        {
            foreach (var t in GetSchemes())
                if (t.Id == UltimatePerformance) return true;
            return false;
        }

        // Ultimate Performance ships hidden on most installs. Duplicating the built-in
        // scheme is precisely what "powercfg -duplicatescheme" does, without shelling out.
        public static bool UnlockUltimatePerformance()
        {
            try
            {
                Guid source = UltimatePerformance;
                IntPtr dest = IntPtr.Zero;
                uint result = PowerDuplicateScheme(IntPtr.Zero, ref source, ref dest);
                if (dest != IntPtr.Zero) LocalFree(dest);
                return result == ERROR_SUCCESS;
            }
            catch { return false; }
        }

        public static bool HasBattery
        {
            get
            {
                try { return SystemInformation.PowerStatus.BatteryChargeStatus != BatteryChargeStatus.NoSystemBattery; }
                catch { return false; }
            }
        }

        static bool? overlaysProbed;

        public static bool OverlaysAvailable
        {
            get
            {
                if (overlaysProbed.HasValue) return overlaysProbed.Value;
                try
                {
                    Guid probe;
                    overlaysProbed = PowerGetEffectiveOverlayScheme(out probe) == ERROR_SUCCESS;
                }
                catch { overlaysProbed = false; }   // EntryPointNotFound on pre-1709
                return overlaysProbed.Value;
            }
        }

        // Desktops have no slider in Windows' own UI, so surfacing one here would be
        // inventing a control the user cannot otherwise see.
        public static bool UseOverlays { get { return HasBattery && OverlaysAvailable; } }

        public static Guid GetActiveOverlay()
        {
            try
            {
                Guid g;
                return PowerGetEffectiveOverlayScheme(out g) == ERROR_SUCCESS ? g : Guid.Empty;
            }
            catch { return Guid.Empty; }
        }

        public static void SetOverlay(Guid overlay)
        {
            try { PowerSetActiveOverlayScheme(overlay); }
            catch { }
        }

        public static List<PowerTarget> GetOverlays()
        {
            var list = new List<PowerTarget>();
            if (!UseOverlays) return list;

            Guid active = GetActiveOverlay();
            AddOverlay(list, OverlayBestEfficiency, "Best power efficiency", active);
            AddOverlay(list, OverlayBalanced, "Balanced", active);
            AddOverlay(list, OverlayBestPerformance, "Best performance", active);
            return list;
        }

        static void AddOverlay(List<PowerTarget> list, Guid id, string name, Guid active)
        {
            list.Add(new PowerTarget { Kind = TargetKind.Overlay, Id = id, Name = name, Active = id == active });
        }

        public static void Activate(PowerTarget target)
        {
            if (target.Kind == TargetKind.Overlay) SetOverlay(target.Id);
            else SetScheme(target.Id);
        }

        // The tray dot should reflect whichever control the user actually operates.
        public static PowerTarget GetActiveIndicator()
        {
            if (UseOverlays)
                foreach (var t in GetOverlays())
                    if (t.Active) return t;

            foreach (var t in GetSchemes())
                if (t.Active) return t;

            return null;
        }
    }
}
