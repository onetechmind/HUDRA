using Gma.System.MouseKeyHook;
using HUDRA.Services.FanControl;
using OpenLibSys;
using System;
using System.Collections.Generic;
using System.Drawing.Text;
using System.Linq;
using System.Net;
using System.Windows.Forms;
using static System.Runtime.InteropServices.JavaScript.JSType;
using static System.Windows.Forms.VisualStyles.VisualStyleElement.ListView;

namespace HUDRA.Services
{
    public class TurboService : IDisposable
    {
        private readonly IKeyboardMouseEvents _hook;
        private readonly HashSet<Keys> _pressed = new();
        private readonly OpenLibSys.Ols _ec;
        private readonly IFanControlDevice? _device;

        // Dynamic hotkey configuration
        private HashSet<Keys> _requiredKeys = new();
        private Keys _mainKey = Keys.None;

        public event EventHandler? TurboButtonPressed;
        
        public TurboService() : this(null)
        {
        }

        public TurboService(IFanControlDevice? device)
        {
            _device = device;
            
            try
            {
                _ec = new Ols();

                if (_ec.GetStatus() != (uint)Ols.Status.NO_ERROR)
                {
                    throw new InvalidOperationException("Failed to initialize OpenLibSys");
                }

                // Initialize turbo button if device supports it
                if (_device?.TurboButtonECAddress.HasValue == true)
                {
                    InitializeTurboButton(_device.TurboButtonECAddress.Value);
                }

                // Load hotkey configuration from settings
                LoadHotkeyConfiguration();

                _hook = Hook.GlobalEvents();
                _hook.KeyDown += OnKeyDown;
                _hook.KeyUp += OnKeyUp;
            }
            catch (Exception ex)
            {
                Dispose(); // Clean up on failure
                throw;
            }
        }

        private void InitializeTurboButton(uint ecAddress)
        {
            try
            {
                byte addr_upper = (byte)((ecAddress >> 8) & byte.MaxValue);
                byte addr_lower = (byte)(ecAddress & byte.MaxValue);

                _ec.WriteIoPortByte(0x4E, 0x2E);
                _ec.WriteIoPortByte(0x4F, 0x11);
                _ec.WriteIoPortByte(0x4E, 0x2F);
                _ec.WriteIoPortByte(0x4F, addr_upper);
                _ec.WriteIoPortByte(0x4E, 0x2E);
                _ec.WriteIoPortByte(0x4F, 0x10);
                _ec.WriteIoPortByte(0x4E, 0x2F);
                _ec.WriteIoPortByte(0x4F, addr_lower);
                _ec.WriteIoPortByte(0x4E, 0x2E);
                _ec.WriteIoPortByte(0x4F, 0x12);
                _ec.WriteIoPortByte(0x4E, 0x2F);
                _ec.WriteIoPortByte(0x4F, 0x40);

                System.Diagnostics.Debug.WriteLine($"🎮 Turbo button initialized for device with EC address: 0x{ecAddress:X}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"⚠️ Failed to initialize turbo button for EC address 0x{ecAddress:X}: {ex.Message}");
                throw;
            }
        }

        private void OnKeyDown(object? sender, KeyEventArgs e)
        {
            _pressed.Add(e.KeyCode);
            System.Diagnostics.Debug.WriteLine($"🔑 Key pressed: {e.KeyCode}. Total pressed: {_pressed.Count}");
            System.Diagnostics.Debug.WriteLine($"🔑 Currently pressed: {string.Join(", ", _pressed)}");

            // Check if all required keys are pressed
            if (IsHotkeyPressed())
            {
                System.Diagnostics.Debug.WriteLine($"🔑 ✅ HOTKEY ACTIVATED!");
                TurboButtonPressed?.Invoke(this, EventArgs.Empty);
            }
        }

        private void OnKeyUp(object? sender, KeyEventArgs e)
        {
            _pressed.Remove(e.KeyCode);
        }

        private void LoadHotkeyConfiguration()
        {
            try
            {
                string modifiers = SettingsService.GetHideShowHotkeyModifiers();
                string key = SettingsService.GetHideShowHotkeyKey();

                System.Diagnostics.Debug.WriteLine($"🔑 Loading hotkey config - Modifiers: '{modifiers}', Key: '{key}'");

                _requiredKeys.Clear();
                _mainKey = Keys.None;

                // Parse modifiers
                if (!string.IsNullOrEmpty(modifiers))
                {
                    var modifierList = modifiers.Split('+', StringSplitOptions.RemoveEmptyEntries)
                                               .Select(m => m.Trim())
                                               .ToList();

                    foreach (var modifier in modifierList)
                    {
                        switch (modifier.ToLower())
                        {
                            case "win":
                                _requiredKeys.Add(Keys.LWin);
                                System.Diagnostics.Debug.WriteLine($"🔑 Added Win modifier");
                                break;
                            case "ctrl":
                                _requiredKeys.Add(Keys.LControlKey);
                                System.Diagnostics.Debug.WriteLine($"🔑 Added Ctrl modifier");
                                break;
                            case "alt":
                                _requiredKeys.Add(Keys.LMenu);
                                System.Diagnostics.Debug.WriteLine($"🔑 Added Alt modifier");
                                break;
                            case "shift":
                                _requiredKeys.Add(Keys.LShiftKey);
                                System.Diagnostics.Debug.WriteLine($"🔑 Added Shift modifier");
                                break;
                        }
                    }
                }

                // Parse main key
                if (!string.IsNullOrEmpty(key))
                {
                    _mainKey = ParseKeyString(key);
                    if (_mainKey != Keys.None)
                    {
                        _requiredKeys.Add(_mainKey);
                        System.Diagnostics.Debug.WriteLine($"🔑 Added main key: {_mainKey}");
                    }
                }

                // Fallback to default if no valid hotkey is configured
                if (_requiredKeys.Count == 0)
                {
                    _requiredKeys.Add(Keys.LWin);
                    _requiredKeys.Add(Keys.LMenu);
                    _requiredKeys.Add(Keys.LControlKey);
                    System.Diagnostics.Debug.WriteLine($"🔑 Using default hotkey: Win+Alt+Ctrl");
                }

                System.Diagnostics.Debug.WriteLine($"🔑 Final required keys: {string.Join(", ", _requiredKeys)}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading hotkey configuration: {ex.Message}");
                // Fallback to default
                _requiredKeys.Clear();
                _requiredKeys.Add(Keys.LWin);
                _requiredKeys.Add(Keys.LMenu);
                _requiredKeys.Add(Keys.LControlKey);
            }
        }

        public void ReloadHotkeyConfiguration()
        {
            System.Diagnostics.Debug.WriteLine($"🔑 Reloading hotkey configuration...");
            LoadHotkeyConfiguration();
        }

        private bool IsHotkeyPressed()
        {
            // Check if all required keys are currently pressed
            bool allPressed = _requiredKeys.All(key => _pressed.Contains(key));
            
            if (_requiredKeys.Count > 0)
            {
                System.Diagnostics.Debug.WriteLine($"🔑 Checking hotkey: Required={string.Join(",", _requiredKeys)}, Pressed={string.Join(",", _pressed)}, Match={allPressed}");
            }
            
            return allPressed;
        }

        private Keys ParseKeyString(string keyString)
        {
            if (string.IsNullOrEmpty(keyString))
                return Keys.None;

            // Handle special cases
            return keyString.ToUpper() switch
            {
                "SPACE" => Keys.Space,
                "TAB" => Keys.Tab,
                "ENTER" => Keys.Enter,
                "ESC" => Keys.Escape,
                "INSERT" => Keys.Insert,
                "DELETE" => Keys.Delete,
                "HOME" => Keys.Home,
                "END" => Keys.End,
                "PGUP" => Keys.PageUp,
                "PGDN" => Keys.PageDown,
                _ when keyString.Length == 1 && char.IsLetter(keyString[0]) => 
                    (Keys)Enum.Parse(typeof(Keys), keyString.ToUpper()),
                _ when keyString.Length == 1 && char.IsDigit(keyString[0]) => 
                    (Keys)Enum.Parse(typeof(Keys), $"D{keyString}"),
                _ when keyString.StartsWith("F") && int.TryParse(keyString.Substring(1), out int fNum) && fNum >= 1 && fNum <= 24 => 
                    (Keys)Enum.Parse(typeof(Keys), keyString.ToUpper()),
                _ => Keys.None
            };
        }

        public void Dispose()
        {
            if (_hook != null)
            {
                _hook.KeyDown -= OnKeyDown;
                _hook.KeyUp -= OnKeyUp;
                _hook.Dispose();
            }
            _ec?.Dispose();
        }
    }
}
