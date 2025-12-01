using HUDRA.Interfaces;
using HUDRA.AttachedProperties;
using HUDRA.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using Keys = System.Windows.Forms.Keys;
using VirtualKey = Windows.System.VirtualKey;

namespace HUDRA.Controls
{
    public sealed partial class HotkeySelector : UserControl, INotifyPropertyChanged, IGamepadNavigable
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        public event EventHandler<HotkeyChangedEventArgs>? HotkeyChanged;

        private bool _isCapturing = false;
        private HashSet<Keys> _pressedKeys = new();
        private string _currentModifiers = "Win+Alt+Ctrl";
        private string _currentKey = "";

        // Store original values for cancel functionality
        private string _originalModifiers = "";
        private string _originalKey = "";

        // Gamepad navigation fields
        private GamepadNavigationService? _gamepadNavigationService;
        private bool _isFocused = false;

        public string CurrentModifiers
        {
            get => _currentModifiers;
            set
            {
                if (_currentModifiers != value)
                {
                    _currentModifiers = value;
                    OnPropertyChanged();
                    UpdateDisplay();
                }
            }
        }

        public string CurrentKey
        {
            get => _currentKey;
            set
            {
                if (_currentKey != value)
                {
                    _currentKey = value;
                    OnPropertyChanged();
                    UpdateDisplay();
                }
            }
        }

        // IGamepadNavigable implementation
        public bool CanNavigateUp => false;
        public bool CanNavigateDown => false;
        public bool CanNavigateLeft => false;
        public bool CanNavigateRight => false;
        public bool CanActivate => true;
        public FrameworkElement NavigationElement => this;

        // Slider interface implementations - HotkeySelector has no sliders
        public bool IsSlider => false;
        public bool IsSliderActivated { get; set; } = false;

        // ComboBox interface implementations - HotkeySelector has no ComboBoxes
        public bool HasComboBoxes => false;
        public bool IsComboBoxOpen { get; set; } = false;
        public ComboBox? GetFocusedComboBox() => null;
        public int ComboBoxOriginalIndex { get; set; } = -1;
        public bool IsNavigatingComboBox { get; set; } = false;
        public void ProcessCurrentSelection() { /* Not applicable - no ComboBoxes */ }

        // Focus brush property for XAML binding
        public Brush HotkeySelectorFocusBrush
        {
            get
            {
                if (_isFocused && _gamepadNavigationService?.IsGamepadActive == true)
                {
                    return new SolidColorBrush(_isCapturing ? Microsoft.UI.Colors.DodgerBlue : Microsoft.UI.Colors.DarkViolet);
                }
                return new SolidColorBrush(Microsoft.UI.Colors.Transparent);
            }
        }

        public HotkeySelector()
        {
            this.InitializeComponent();
            this.DataContext = this;
            UpdateDisplay();
            InitializeGamepadNavigation();
        }

        private void InitializeGamepadNavigation()
        {
            GamepadNavigation.SetIsEnabled(this, true);
            GamepadNavigation.SetNavigationGroup(this, "MainControls");
            GamepadNavigation.SetNavigationOrder(this, 1);
        }

        private void InitializeGamepadNavigationService()
        {
            if (Application.Current is App app && app.MainWindow is MainWindow mainWindow)
            {
                _gamepadNavigationService = mainWindow.GamepadNavigationService;
            }
        }

        public void SetHotkey(string modifiers, string key)
        {
            CurrentModifiers = modifiers;
            CurrentKey = key;
        }

        private void UpdateDisplay()
        {
            string display = CurrentModifiers;
            if (!string.IsNullOrEmpty(CurrentKey))
            {
                display += string.IsNullOrEmpty(CurrentModifiers) ? CurrentKey : $" + {CurrentKey}";
            }
            HotkeyDisplay.Text = string.IsNullOrEmpty(display) ? "None" : display;
        }

        private void EditButton_Click(object sender, RoutedEventArgs e)
        {
            if (!_isCapturing)
            {
                StartCapture();
            }
            else
            {
                CancelCapture();
            }
        }

        private void StartCapture()
        {
            // Store original values for potential cancel
            _originalModifiers = _currentModifiers;
            _originalKey = _currentKey;
            
            _isCapturing = true;
            _pressedKeys.Clear();
            EditButton.Content = "Cancel";
            EditButton.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 0, 100, 0));
            HotkeyDisplay.Text = "Press up to 4 keys.";
            
            // Focus this control to capture key events
            this.Focus(FocusState.Programmatic);
            this.KeyDown += OnKeyDown;
            this.KeyUp += OnKeyUp;
        }

        private void StopCapture()
        {
            _isCapturing = false;
            EditButton.Content = "Edit";
            EditButton.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 74, 74, 74));
            
            this.KeyDown -= OnKeyDown;
            this.KeyUp -= OnKeyUp;

            ProcessCapturedKeys();
            UpdateDisplay();
        }

        private void CancelCapture()
        {
            System.Diagnostics.Debug.WriteLine($"🔍 Cancelling hotkey capture, restoring original values");
            
            _isCapturing = false;
            EditButton.Content = "Edit";
            EditButton.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 74, 74, 74));
            
            this.KeyDown -= OnKeyDown;
            this.KeyUp -= OnKeyUp;

            // Restore original values without triggering change event
            _currentModifiers = _originalModifiers;
            _currentKey = _originalKey;
            UpdateDisplay();
            
            System.Diagnostics.Debug.WriteLine($"🔍 Restored to: '{_currentModifiers}' + '{_currentKey}'");
        }

        private void OnKeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (!_isCapturing) return;

            System.Diagnostics.Debug.WriteLine($"🔍 KeyDown detected: VirtualKey={e.Key}");

            // Check for Escape key as first input to cancel
            if (e.Key == VirtualKey.Escape && _pressedKeys.Count == 0)
            {
                System.Diagnostics.Debug.WriteLine($"🔍 Escape pressed as first key - cancelling capture");
                CancelCapture();
                return;
            }
            
            var key = ConvertToFormsKey(e.Key);
            System.Diagnostics.Debug.WriteLine($"🔍 Converted to Forms.Key: {key}");
            
            if (key != Keys.None)
            {
                _pressedKeys.Add(key);
                System.Diagnostics.Debug.WriteLine($"🔍 Added to pressed keys. Total pressed: {_pressedKeys.Count}");
                System.Diagnostics.Debug.WriteLine($"🔍 Current pressed keys: {string.Join(", ", _pressedKeys)}");
                UpdateCaptureDisplay();
            }
        }

        private void OnKeyUp(object sender, KeyRoutedEventArgs e)
        {
            if (!_isCapturing) return;

            // Stop capture when any key is released
            StopCapture();
        }

        private void UpdateCaptureDisplay()
        {
            var modifiers = new List<string>();
            string mainKey = "";

            foreach (var key in _pressedKeys)
            {
                switch (key)
                {
                    case Keys.LWin:
                    case Keys.RWin:
                        if (!modifiers.Contains("Win")) modifiers.Add("Win");
                        break;
                    case Keys.LControlKey:
                    case Keys.RControlKey:
                        if (!modifiers.Contains("Ctrl")) modifiers.Add("Ctrl");
                        break;
                    case Keys.LMenu:
                    case Keys.RMenu:
                        if (!modifiers.Contains("Alt")) modifiers.Add("Alt");
                        break;
                    case Keys.LShiftKey:
                    case Keys.RShiftKey:
                        if (!modifiers.Contains("Shift")) modifiers.Add("Shift");
                        break;
                    default:
                        if (IsValidMainKey(key))
                        {
                            mainKey = GetKeyDisplayName(key);
                        }
                        break;
                }
            }

            string display = string.Join(" + ", modifiers);
            if (!string.IsNullOrEmpty(mainKey))
            {
                display += string.IsNullOrEmpty(display) ? mainKey : $" + {mainKey}";
            }

            HotkeyDisplay.Text = string.IsNullOrEmpty(display) ? "Press keys..." : display;
        }

        private void ProcessCapturedKeys()
        {
            System.Diagnostics.Debug.WriteLine($"🔍 ProcessCapturedKeys: Processing {_pressedKeys.Count} keys");
            
            var modifiers = new List<string>();
            string mainKey = "";

            foreach (var key in _pressedKeys)
            {
                System.Diagnostics.Debug.WriteLine($"🔍 Processing key: {key}");
                
                switch (key)
                {
                    case Keys.LWin:
                    case Keys.RWin:
                        if (!modifiers.Contains("Win")) modifiers.Add("Win");
                        System.Diagnostics.Debug.WriteLine($"🔍 Added Win modifier");
                        break;
                    case Keys.LControlKey:
                    case Keys.RControlKey:
                        if (!modifiers.Contains("Ctrl")) modifiers.Add("Ctrl");
                        System.Diagnostics.Debug.WriteLine($"🔍 Added Ctrl modifier");
                        break;
                    case Keys.LMenu:
                    case Keys.RMenu:
                        if (!modifiers.Contains("Alt")) modifiers.Add("Alt");
                        System.Diagnostics.Debug.WriteLine($"🔍 Added Alt modifier");
                        break;
                    case Keys.LShiftKey:
                    case Keys.RShiftKey:
                        if (!modifiers.Contains("Shift")) modifiers.Add("Shift");
                        System.Diagnostics.Debug.WriteLine($"🔍 Added Shift modifier");
                        break;
                    default:
                        if (IsValidMainKey(key))
                        {
                            mainKey = GetKeyDisplayName(key);
                            System.Diagnostics.Debug.WriteLine($"🔍 Set main key: {mainKey}");
                        }
                        break;
                }
            }

            CurrentModifiers = string.Join("+", modifiers);
            CurrentKey = mainKey;
            
            System.Diagnostics.Debug.WriteLine($"🔍 Final result - Modifiers: '{CurrentModifiers}', Key: '{CurrentKey}'");

            // Notify of change
            HotkeyChanged?.Invoke(this, new HotkeyChangedEventArgs(CurrentModifiers, CurrentKey));
        }

        private bool IsValidMainKey(Keys key)
        {
            // Allow letters, numbers, function keys, and some special keys
            return (key >= Keys.A && key <= Keys.Z) ||
                   (key >= Keys.D0 && key <= Keys.D9) ||
                   (key >= Keys.F1 && key <= Keys.F24) ||
                   key == Keys.Space || key == Keys.Tab || key == Keys.Enter ||
                   key == Keys.Escape || key == Keys.Insert || key == Keys.Delete ||
                   key == Keys.Home || key == Keys.End || key == Keys.PageUp || key == Keys.PageDown;
        }

        private string GetKeyDisplayName(Keys key)
        {
            // Convert to readable display names
            return key switch
            {
                Keys.Space => "Space",
                Keys.Tab => "Tab",
                Keys.Enter => "Enter",
                Keys.Escape => "Esc",
                Keys.Insert => "Insert",
                Keys.Delete => "Delete",
                Keys.Home => "Home",
                Keys.End => "End",
                Keys.PageUp => "PgUp",
                Keys.PageDown => "PgDn",
                _ when key >= Keys.D0 && key <= Keys.D9 => key.ToString().Replace("D", ""),
                _ => key.ToString()
            };
        }

        private Keys ConvertToFormsKey(VirtualKey virtualKey)
        {
            // Convert WinUI VirtualKey to Windows.Forms.Keys
            return virtualKey switch
            {
                VirtualKey.LeftWindows => Keys.LWin,
                VirtualKey.RightWindows => Keys.RWin,
                VirtualKey.LeftControl => Keys.LControlKey,
                VirtualKey.RightControl => Keys.RControlKey,
                VirtualKey.Control => Keys.LControlKey,  // Generic Control -> Left Control
                VirtualKey.LeftMenu => Keys.LMenu,
                VirtualKey.RightMenu => Keys.RMenu,
                VirtualKey.Menu => Keys.LMenu,  // Generic Alt -> Left Alt
                VirtualKey.LeftShift => Keys.LShiftKey,
                VirtualKey.RightShift => Keys.RShiftKey,
                VirtualKey.Shift => Keys.LShiftKey,  // Generic Shift -> Left Shift
                VirtualKey.A => Keys.A,
                VirtualKey.B => Keys.B,
                VirtualKey.C => Keys.C,
                VirtualKey.D => Keys.D,
                VirtualKey.E => Keys.E,
                VirtualKey.F => Keys.F,
                VirtualKey.G => Keys.G,
                VirtualKey.H => Keys.H,
                VirtualKey.I => Keys.I,
                VirtualKey.J => Keys.J,
                VirtualKey.K => Keys.K,
                VirtualKey.L => Keys.L,
                VirtualKey.M => Keys.M,
                VirtualKey.N => Keys.N,
                VirtualKey.O => Keys.O,
                VirtualKey.P => Keys.P,
                VirtualKey.Q => Keys.Q,
                VirtualKey.R => Keys.R,
                VirtualKey.S => Keys.S,
                VirtualKey.T => Keys.T,
                VirtualKey.U => Keys.U,
                VirtualKey.V => Keys.V,
                VirtualKey.W => Keys.W,
                VirtualKey.X => Keys.X,
                VirtualKey.Y => Keys.Y,
                VirtualKey.Z => Keys.Z,
                VirtualKey.Number0 => Keys.D0,
                VirtualKey.Number1 => Keys.D1,
                VirtualKey.Number2 => Keys.D2,
                VirtualKey.Number3 => Keys.D3,
                VirtualKey.Number4 => Keys.D4,
                VirtualKey.Number5 => Keys.D5,
                VirtualKey.Number6 => Keys.D6,
                VirtualKey.Number7 => Keys.D7,
                VirtualKey.Number8 => Keys.D8,
                VirtualKey.Number9 => Keys.D9,
                VirtualKey.F1 => Keys.F1,
                VirtualKey.F2 => Keys.F2,
                VirtualKey.F3 => Keys.F3,
                VirtualKey.F4 => Keys.F4,
                VirtualKey.F5 => Keys.F5,
                VirtualKey.F6 => Keys.F6,
                VirtualKey.F7 => Keys.F7,
                VirtualKey.F8 => Keys.F8,
                VirtualKey.F9 => Keys.F9,
                VirtualKey.F10 => Keys.F10,
                VirtualKey.F11 => Keys.F11,
                VirtualKey.F12 => Keys.F12,
                VirtualKey.Space => Keys.Space,
                VirtualKey.Tab => Keys.Tab,
                VirtualKey.Enter => Keys.Enter,
                VirtualKey.Escape => Keys.Escape,
                VirtualKey.Insert => Keys.Insert,
                VirtualKey.Delete => Keys.Delete,
                VirtualKey.Home => Keys.Home,
                VirtualKey.End => Keys.End,
                VirtualKey.PageUp => Keys.PageUp,
                VirtualKey.PageDown => Keys.PageDown,
                _ => Keys.None
            };
        }

        // IGamepadNavigable event handlers
        public void OnGamepadNavigateUp()
        {
            // No up/down navigation in HotkeySelector
        }

        public void OnGamepadNavigateDown()
        {
            // No up/down navigation in HotkeySelector
        }

        public void OnGamepadNavigateLeft()
        {
            // No left/right navigation in HotkeySelector
        }

        public void OnGamepadNavigateRight()
        {
            // No left/right navigation in HotkeySelector
        }

        public void OnGamepadActivate()
        {
            // Trigger the Edit button when activated with gamepad
            EditButton_Click(EditButton, new RoutedEventArgs());
            System.Diagnostics.Debug.WriteLine($"🎮 HotkeySelector: Activated Edit button");
        }

        public void OnGamepadBack() { }

        public void OnGamepadFocusReceived()
        {
            // Initialize gamepad service if needed
            if (_gamepadNavigationService == null)
            {
                InitializeGamepadNavigationService();
            }

            _isFocused = true;
            UpdateFocusVisuals();
            System.Diagnostics.Debug.WriteLine($"🎮 HotkeySelector: Received gamepad focus");
        }

        public void OnGamepadFocusLost()
        {
            _isFocused = false;

            // Cancel any ongoing capture when losing focus
            if (_isCapturing)
            {
                CancelCapture();
            }

            UpdateFocusVisuals();
            System.Diagnostics.Debug.WriteLine($"🎮 HotkeySelector: Lost gamepad focus");
        }

        public void FocusLastElement()
        {
            // Not used - HotkeySelector is not in a NavigableExpander
        }

        public void AdjustSliderValue(int direction)
        {
            // No sliders in HotkeySelector control
        }

        private void UpdateFocusVisuals()
        {
            // Dispatch on UI thread to ensure bindings update reliably with gamepad navigation
            DispatcherQueue.TryEnqueue(() =>
            {
                OnPropertyChanged(nameof(HotkeySelectorFocusBrush));
            });
        }

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public class HotkeyChangedEventArgs : EventArgs
    {
        public string Modifiers { get; }
        public string Key { get; }

        public HotkeyChangedEventArgs(string modifiers, string key)
        {
            Modifiers = modifiers;
            Key = key;
        }
    }
}