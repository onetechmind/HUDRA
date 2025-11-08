using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.System;
using Windows.Gaming.Input;
using HUDRA.Services;
using System;

namespace HUDRA.Helpers
{
    public static class GamepadComboBoxHelper
    {
        public static readonly DependencyProperty IsGamepadEnabledProperty =
            DependencyProperty.RegisterAttached(
                "IsGamepadEnabled",
                typeof(bool),
                typeof(GamepadComboBoxHelper),
                new PropertyMetadata(false, OnIsGamepadEnabledChanged));

        public static bool GetIsGamepadEnabled(DependencyObject obj)
        {
            return (bool)obj.GetValue(IsGamepadEnabledProperty);
        }

        public static void SetIsGamepadEnabled(DependencyObject obj, bool value)
        {
            obj.SetValue(IsGamepadEnabledProperty, value);
        }

        private static void OnIsGamepadEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is ComboBox comboBox)
            {
                if ((bool)e.NewValue)
                {
                    comboBox.PreviewKeyDown += OnComboBoxPreviewKeyDown;
                    comboBox.KeyDown += OnComboBoxKeyDown;
                }
                else
                {
                    comboBox.PreviewKeyDown -= OnComboBoxPreviewKeyDown;
                    comboBox.KeyDown -= OnComboBoxKeyDown;
                }
            }
        }

        private static void OnComboBoxPreviewKeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (sender is ComboBox comboBox && IsGamepadActiveForControl(comboBox))
            {
                // Prevent default D-pad handling when gamepad is active and ComboBox is not expanded
                if (!comboBox.IsDropDownOpen)
                {
                    switch (e.Key)
                    {
                        case VirtualKey.GamepadDPadUp:
                        case VirtualKey.GamepadDPadDown:
                        case VirtualKey.GamepadDPadLeft:
                        case VirtualKey.GamepadDPadRight:
                        case VirtualKey.GamepadLeftThumbstickUp:
                        case VirtualKey.GamepadLeftThumbstickDown:
                        case VirtualKey.GamepadLeftThumbstickLeft:
                        case VirtualKey.GamepadLeftThumbstickRight:
                            // Prevent default ComboBox behavior
                            e.Handled = true;
                            System.Diagnostics.Debug.WriteLine($"🎮 Prevented default D-pad handling for ComboBox: {e.Key}");
                            break;
                    }
                }
            }
        }

        private static void OnComboBoxKeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (sender is ComboBox comboBox && IsGamepadActiveForControl(comboBox))
            {
                switch (e.Key)
                {
                    case VirtualKey.GamepadA:
                        if (!comboBox.IsDropDownOpen)
                        {
                            // Expand the ComboBox
                            comboBox.IsDropDownOpen = true;
                            e.Handled = true;
                            System.Diagnostics.Debug.WriteLine($"🎮 A button expanded ComboBox");
                        }
                        else
                        {
                            // Select the current item and close ComboBox
                            // The ComboBox will automatically handle selection based on current highlighted item
                            comboBox.IsDropDownOpen = false;
                            e.Handled = true;
                            System.Diagnostics.Debug.WriteLine($"🎮 A button selected ComboBox item and closed dropdown");
                        }
                        break;
                        
                    case VirtualKey.GamepadB:
                        // B button closes dropdown without selecting
                        if (comboBox.IsDropDownOpen)
                        {
                            comboBox.IsDropDownOpen = false;
                            e.Handled = true;
                            System.Diagnostics.Debug.WriteLine($"🎮 B button cancelled ComboBox selection");
                        }
                        break;
                }
            }
        }

        private static bool IsGamepadActiveForControl(ComboBox comboBox)
        {
            // Check if gamepad navigation is active
            try
            {
                if (Application.Current is App app && app.MainWindow is MainWindow mainWindow)
                {
                    var gamepadService = mainWindow.GamepadNavigationService;
                    return gamepadService?.IsGamepadActive == true;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error checking gamepad state: {ex.Message}");
            }
            
            return false;
        }
    }
}