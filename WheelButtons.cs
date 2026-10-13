using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace AlmostFastOverlays
{
    /// <summary>
    /// Reads buttons on wheels, button boxes and other game controllers through Windows' own joystick
    /// interface (winmm), so a dashboard can change page from a button on the wheel. iRacing doesn't
    /// pass button presses on, and nothing beyond Windows is needed. Up to 32 buttons per device.
    /// A button is stored as "device name|button number", so it survives devices being plugged in a
    /// different order.
    /// </summary>
    internal static class WheelButtons
    {
        private const int MaxDevices = 16;

        /// <summary>The pressed buttons of every connected controller, by device name (bit n is button n + 1).</summary>
        public static Dictionary<string, uint> Read()
        {
            var pressed = new Dictionary<string, uint>();
            for (int id = 0; id < MaxDevices; id++)
            {
                var state = new JoyInfoEx { Size = Marshal.SizeOf(typeof(JoyInfoEx)), Flags = ReturnButtons };
                if (joyGetPosEx(id, ref state) != 0) continue;
                string name = Name(id);
                pressed[name] = pressed.TryGetValue(name, out uint other) ? other | state.Buttons : state.Buttons;
            }
            return pressed;
        }

        /// <summary>Whether a stored button ("device|number") is held right now.</summary>
        public static bool IsDown(Dictionary<string, uint> state, string button)
        {
            if (!Parse(button, out string device, out int number) || state == null) return false;
            return state.TryGetValue(device, out uint bits) && (bits & (1u << (number - 1))) != 0;
        }

        /// <summary>The first button that went down between two readings, as "device|number", or null.</summary>
        public static string NewlyPressed(Dictionary<string, uint> before, Dictionary<string, uint> now)
        {
            foreach (KeyValuePair<string, uint> device in now)
            {
                before.TryGetValue(device.Key, out uint was);
                uint fresh = device.Value & ~was;
                for (int b = 0; b < 32; b++)
                    if ((fresh & (1u << b)) != 0) return device.Key + "|" + (b + 1);
            }
            return null;
        }

        /// <summary>"Button 7 on Simucube 2 Pro" for a stored button.</summary>
        public static string Describe(string button) =>
            Parse(button, out string device, out int number) ? "Button " + number + " on " + device : "Not set";

        private static bool Parse(string button, out string device, out int number)
        {
            device = null;
            number = 0;
            if (string.IsNullOrEmpty(button)) return false;
            int bar = button.LastIndexOf('|');
            if (bar <= 0 || !int.TryParse(button.Substring(bar + 1), out number) || number < 1 || number > 32) return false;
            device = button.Substring(0, bar);
            return true;
        }

        private static readonly Dictionary<int, string> Names = new Dictionary<int, string>();
        private static DateTime _namesFresh;

        private static string Name(int id)
        {
            lock (Names)
            {
                // Devices can be unplugged and others take their slot; look names up afresh now and then.
                if (DateTime.UtcNow > _namesFresh) { Names.Clear(); _namesFresh = DateTime.UtcNow.AddSeconds(20); }
                if (Names.TryGetValue(id, out string known)) return known;
                var caps = new JoyCaps();
                string name = "Controller " + (id + 1);
                if (joyGetDevCaps((IntPtr)id, ref caps, Marshal.SizeOf(typeof(JoyCaps))) == 0)
                    name = OemName(caps.RegKey, id) ?? (string.IsNullOrWhiteSpace(caps.Name) ? name : caps.Name.Trim());
                Names[id] = name;
                return name;
            }
        }

        /// <summary>
        /// The device's own name (such as "Simucube 2 Pro"), which winmm leaves to the registry: the joystick
        /// slot points at its USB id, and the USB id at the name. Falls back to the USB id.
        /// </summary>
        private static string OemName(string regKey, int id)
        {
            if (string.IsNullOrEmpty(regKey)) return null;
            try
            {
                string slot = null;
                foreach (Microsoft.Win32.RegistryKey root in new[] { Microsoft.Win32.Registry.CurrentUser, Microsoft.Win32.Registry.LocalMachine })
                    using (Microsoft.Win32.RegistryKey settings = root.OpenSubKey(@"System\CurrentControlSet\Control\MediaResources\Joystick\" + regKey + @"\CurrentJoystickSettings"))
                        if ((slot = settings?.GetValue("Joystick" + (id + 1) + "OEMName") as string) != null) break;
                if (string.IsNullOrEmpty(slot)) return null;
                foreach (Microsoft.Win32.RegistryKey root in new[] { Microsoft.Win32.Registry.CurrentUser, Microsoft.Win32.Registry.LocalMachine })
                    using (Microsoft.Win32.RegistryKey oem = root.OpenSubKey(@"System\CurrentControlSet\Control\MediaProperties\PrivateProperties\Joystick\OEM\" + slot))
                        if (oem?.GetValue("OEMName") is string friendly && friendly.Trim().Length > 0) return friendly.Trim();
                return slot;
            }
            catch { return null; }
        }

        private const int ReturnButtons = 0x80;

        [StructLayout(LayoutKind.Sequential)]
        private struct JoyInfoEx
        {
            public int Size, Flags, X, Y, Z, R, U, V;
            public uint Buttons;
            public int ButtonNumber, Pov, Reserved1, Reserved2;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct JoyCaps
        {
            public ushort Mid, Pid;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string Name;
            public int XMin, XMax, YMin, YMax, ZMin, ZMax, NumButtons, PeriodMin, PeriodMax, RMin, RMax, UMin, UMax, VMin, VMax, Caps, MaxAxes, NumAxes, MaxButtons;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string RegKey;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string OemVxd;
        }

        [DllImport("winmm.dll")]
        private static extern int joyGetPosEx(int id, ref JoyInfoEx info);

        [DllImport("winmm.dll", CharSet = CharSet.Unicode, EntryPoint = "joyGetDevCapsW")]
        private static extern int joyGetDevCaps(IntPtr id, ref JoyCaps caps, int size);
    }
}
