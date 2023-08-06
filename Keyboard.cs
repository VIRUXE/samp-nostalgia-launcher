using System.Runtime.InteropServices;

namespace NostalgiaAnticheat {
    internal class Keyboard {

        public const byte KEYEVENTF_EXTENDEDKEY = 0x1;
        public const byte KEYEVENTF_KEYUP = 0x2;
        [DllImport("user32.dll")]
        public static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, uint dwExtraInfo);

        public static void PressF6() {
            const byte VK_F6 = 0x75;
            keybd_event(VK_F6, 0, KEYEVENTF_EXTENDEDKEY, 0); // Key down
            keybd_event(VK_F6, 0, KEYEVENTF_EXTENDEDKEY | KEYEVENTF_KEYUP, 0); // Key up
        }

        public static void PressEnter() {
            const byte VK_ENTER = 0x0D;
            keybd_event(VK_ENTER, 0, KEYEVENTF_EXTENDEDKEY, 0); // Key down
            keybd_event(VK_ENTER, 0, KEYEVENTF_EXTENDEDKEY | KEYEVENTF_KEYUP, 0); // Key up
        }
    }
}
