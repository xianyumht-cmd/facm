using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace FACM.League
{
    internal static class LeagueHotkeyReleaseWaiter
    {
        private const int DefaultTimeoutMs = 1500;
        private const int PollIntervalMs = 10;
        private const int VkControl = 0x11;
        private const int VkMenu = 0x12;
        private const int VkShift = 0x10;
        private const int VkLWin = 0x5B;
        private const int VkRWin = 0x5C;

        public static bool WaitUntilReleased(LeagueHotkeyBinding binding)
        {
            return WaitUntilReleased(
                binding,
                delegate(int virtualKey) { return (GetAsyncKeyState(virtualKey) & 0x8000) != 0; },
                Thread.Sleep,
                DefaultTimeoutMs,
                PollIntervalMs);
        }

        internal static bool WaitUntilReleased(
            LeagueHotkeyBinding binding,
            Func<int, bool> isDown,
            Action<int> delay,
            int timeoutMs,
            int pollIntervalMs)
        {
            if (binding == null || !binding.Enabled) return true;
            if (isDown == null) throw new ArgumentNullException(nameof(isDown));
            if (delay == null) throw new ArgumentNullException(nameof(delay));
            if (timeoutMs < 0) throw new ArgumentOutOfRangeException(nameof(timeoutMs));
            if (pollIntervalMs <= 0) throw new ArgumentOutOfRangeException(nameof(pollIntervalMs));

            var keys = GetReleaseKeys(binding);
            var elapsed = 0;
            while (true)
            {
                var anyDown = false;
                foreach (var key in keys)
                {
                    if (!isDown(key)) continue;
                    anyDown = true;
                    break;
                }

                if (!anyDown)
                {
                    // Give the foreground control one short scheduling slice after the physical
                    // trigger has fully released before we inject Ctrl+A / text / Tab.
                    delay(30);
                    return true;
                }

                if (elapsed >= timeoutMs) return false;
                delay(pollIntervalMs);
                elapsed += pollIntervalMs;
            }
        }

        internal static IReadOnlyList<int> GetReleaseKeys(LeagueHotkeyBinding binding)
        {
            var keys = new List<int>();
            if (binding == null || !binding.Enabled) return keys;

            keys.Add((int)(binding.Key & Keys.KeyCode));
            if ((binding.Modifiers & LeagueHotkeyModifiers.Control) != 0) keys.Add(VkControl);
            if ((binding.Modifiers & LeagueHotkeyModifiers.Alt) != 0) keys.Add(VkMenu);
            if ((binding.Modifiers & LeagueHotkeyModifiers.Shift) != 0) keys.Add(VkShift);
            if ((binding.Modifiers & LeagueHotkeyModifiers.Win) != 0)
            {
                keys.Add(VkLWin);
                keys.Add(VkRWin);
            }
            return keys;
        }

        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int virtualKey);
    }
}
