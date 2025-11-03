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
        private OpenLibSys.Ols? _ec;
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

        private void InitializeTurboButton(uint ecAddress, Ols? ec = null)
        {
            try
            {
                var ecToUse = ec ?? _ec;
                if (ecToUse == null)
                {
                    throw new InvalidOperationException("No EC connection available");
                }

                byte addr_upper = (byte)((ecAddress >> 8) & byte.MaxValue);
                byte addr_lower = (byte)(ecAddress & byte.MaxValue);

                ecToUse.WriteIoPortByte(0x4E, 0x2E);
                ecToUse.WriteIoPortByte(0x4F, 0x11);
                ecToUse.WriteIoPortByte(0x4E, 0x2F);
                ecToUse.WriteIoPortByte(0x4F, addr_upper);
                ecToUse.WriteIoPortByte(0x4E, 0x2E);
                ecToUse.WriteIoPortByte(0x4F, 0x10);
                ecToUse.WriteIoPortByte(0x4E, 0x2F);
                ecToUse.WriteIoPortByte(0x4F, addr_lower);
                ecToUse.WriteIoPortByte(0x4E, 0x2E);
                ecToUse.WriteIoPortByte(0x4F, 0x12);
                ecToUse.WriteIoPortByte(0x4E, 0x2F);
                ecToUse.WriteIoPortByte(0x4F, 0x40);

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
                                break;
                            case "ctrl":
                                _requiredKeys.Add(Keys.LControlKey);
                                break;
                            case "alt":
                                _requiredKeys.Add(Keys.LMenu);
                                break;
                            case "shift":
                                _requiredKeys.Add(Keys.LShiftKey);
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

        public (bool Success, string Message) ReinitializeAfterResume()
        {
            try
            {
                System.Diagnostics.Debug.WriteLine("⚡ Reinitializing TurboService after hibernation resume...");

                // Dispose the existing EC connection
                _ec?.Dispose();
                _ec = null;

                // Re-create the OpenLibSys connection
                _ec = new Ols();
                if (_ec.GetStatus() != (uint)Ols.Status.NO_ERROR)
                {
                    _ec.Dispose();
                    _ec = null;
                    var errorMessage = "Failed to reinitialize OpenLibSys for turbo button";
                    System.Diagnostics.Debug.WriteLine($"⚠️ {errorMessage}");
                    return (false, errorMessage);
                }

                // Re-initialize turbo button if device supports it
                if (_device?.TurboButtonECAddress.HasValue == true)
                {
                    try
                    {
                        InitializeTurboButton(_device.TurboButtonECAddress.Value);
                        var successMessage = "TurboService successfully reinitialized after hibernation resume";
                        System.Diagnostics.Debug.WriteLine($"⚡ {successMessage}");
                        return (true, successMessage);
                    }
                    catch (Exception turboEx)
                    {
                        var errorMessage = $"Failed to reinitialize turbo button: {turboEx.Message}";
                        System.Diagnostics.Debug.WriteLine($"⚠️ {errorMessage}");
                        return (false, errorMessage);
                    }
                }
                else
                {
                    var message = "TurboService reinitialized - no turbo button device configured";
                    System.Diagnostics.Debug.WriteLine($"⚡ {message}");
                    return (true, message);
                }
            }
            catch (Exception ex)
            {
                var errorMessage = $"Exception during TurboService reinitialization: {ex.Message}";
                System.Diagnostics.Debug.WriteLine($"⚠️ {errorMessage}");
                return (false, errorMessage);
            }
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
