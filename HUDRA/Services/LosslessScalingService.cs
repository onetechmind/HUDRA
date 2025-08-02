using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;

namespace HUDRA.Services
{
    public class LosslessScalingService : IDisposable
    {
        public event EventHandler<bool>? LosslessScalingStatusChanged;

        private const string SETTINGS_PATH = @"%LOCALAPPDATA%\Lossless Scaling\Settings.xml";
        private const string DEFAULT_HOTKEY = "S";
        private const string DEFAULT_MODIFIERS = "Alt Control";
        private const string PROCESS_NAME = "LosslessScaling";

        private readonly Timer _detectionTimer;
        private bool _isLosslessScalingRunning = false;
        private bool _disposed = false;

        [DllImport("user32.dll", SetLastError = true)]
        private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

        private const uint KEYEVENTF_KEYUP = 0x0002;

        private readonly Dictionary<string, byte> _keyMap = new()
        {
            { "A", 0x41 }, { "B", 0x42 }, { "C", 0x43 }, { "D", 0x44 }, { "E", 0x45 },
            { "F", 0x46 }, { "G", 0x47 }, { "H", 0x48 }, { "I", 0x49 }, { "J", 0x4A },
            { "K", 0x4B }, { "L", 0x4C }, { "M", 0x4D }, { "N", 0x4E }, { "O", 0x4F },
            { "P", 0x50 }, { "Q", 0x51 }, { "R", 0x52 }, { "S", 0x53 }, { "T", 0x54 },
            { "U", 0x55 }, { "V", 0x56 }, { "W", 0x57 }, { "X", 0x58 }, { "Y", 0x59 },
            { "Z", 0x5A },
            { "0", 0x30 }, { "1", 0x31 }, { "2", 0x32 }, { "3", 0x33 }, { "4", 0x34 },
            { "5", 0x35 }, { "6", 0x36 }, { "7", 0x37 }, { "8", 0x38 }, { "9", 0x39 },
            { "Alt", 0x12 }, { "Control", 0x11 }, { "Shift", 0x10 },
            { "F1", 0x70 }, { "F2", 0x71 }, { "F3", 0x72 }, { "F4", 0x73 },
            { "F5", 0x74 }, { "F6", 0x75 }, { "F7", 0x76 }, { "F8", 0x77 },
            { "F9", 0x78 }, { "F10", 0x79 }, { "F11", 0x7A }, { "F12", 0x7B }
        };

        public LosslessScalingService()
        {
            _detectionTimer = new Timer(DetectionCallback, null,
                TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(3));
        }

        public bool IsLosslessScalingRunning()
        {
            try
            {
                var processes = Process.GetProcessesByName(PROCESS_NAME);
                return processes.Length > 0;
            }
            catch
            {
                return false;
            }
        }

        public (string hotkey, string modifiers) ParseHotkeyFromSettings()
        {
            try
            {
                string settingsPath = Environment.ExpandEnvironmentVariables(SETTINGS_PATH);
                if (!File.Exists(settingsPath))
                    return (DEFAULT_HOTKEY, DEFAULT_MODIFIERS);

                var doc = new XmlDocument();
                doc.Load(settingsPath);

                string hotkey = doc.SelectSingleNode("//Hotkey")?.InnerText ?? DEFAULT_HOTKEY;
                string modifiers = doc.SelectSingleNode("//HotkeyModifierKeys")?.InnerText ?? DEFAULT_MODIFIERS;

                return (hotkey, modifiers);
            }
            catch
            {
                return (DEFAULT_HOTKEY, DEFAULT_MODIFIERS);
            }
        }

        public async Task<bool> ExecuteHotkeyAsync(string hotkey, string modifiers)
        {
            try
            {
                var modifierKeys = ParseModifiers(modifiers);
                var mainKey = _keyMap.TryGetValue(hotkey.ToUpper(), out byte key) ? key : (byte)0x53;

                foreach (var mod in modifierKeys)
                    keybd_event(mod, 0, 0, UIntPtr.Zero);

                keybd_event(mainKey, 0, 0, UIntPtr.Zero);

                await Task.Delay(50);

                keybd_event(mainKey, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);

                for (int i = modifierKeys.Count - 1; i >= 0; i--)
                    keybd_event(modifierKeys[i], 0, KEYEVENTF_KEYUP, UIntPtr.Zero);

                return true;
            }
            catch
            {
                return false;
            }
        }

        private List<byte> ParseModifiers(string modifiers)
        {
            var keys = new List<byte>();
            if (string.IsNullOrEmpty(modifiers))
                return keys;

            var parts = modifiers.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            foreach (var part in parts)
            {
                if (_keyMap.TryGetValue(part, out byte key))
                    keys.Add(key);
            }

            return keys;
        }

        private void DetectionCallback(object? state)
        {
            if (_disposed)
                return;

            try
            {
                bool isRunning = IsLosslessScalingRunning();
                if (isRunning != _isLosslessScalingRunning)
                {
                    _isLosslessScalingRunning = isRunning;
                    LosslessScalingStatusChanged?.Invoke(this, isRunning);
                }
            }
            catch
            {
                // Ignore detection errors
            }
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            _detectionTimer?.Dispose();
        }
    }
}