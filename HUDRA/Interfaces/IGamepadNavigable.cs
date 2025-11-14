using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace HUDRA.Interfaces
{
    public interface IGamepadNavigable
    {
        bool CanNavigateUp { get; }
        bool CanNavigateDown { get; }
        bool CanNavigateLeft { get; }
        bool CanNavigateRight { get; }
        bool CanActivate { get; }
        
        void OnGamepadNavigateUp();
        void OnGamepadNavigateDown();
        void OnGamepadNavigateLeft();
        void OnGamepadNavigateRight();
        void OnGamepadActivate();
        void OnGamepadFocusReceived();
        void OnGamepadFocusLost();
        void FocusLastElement(); // Focus the last navigable element within this control
        
        FrameworkElement NavigationElement { get; }
        
        // Slider-specific properties and methods
        bool IsSlider { get; }
        bool IsSliderActivated { get; set; }
        void AdjustSliderValue(int direction); // -1 for decrease, +1 for increase
        
        // ComboBox-specific properties and methods
        bool HasComboBoxes { get; }
        bool IsComboBoxOpen { get; set; }
        ComboBox? GetFocusedComboBox();
        int ComboBoxOriginalIndex { get; set; }
        bool IsNavigatingComboBox { get; set; }
        void ProcessCurrentSelection(); // Manually trigger selection processing
    }
    
    public enum GamepadNavigationAction
    {
        Up,
        Down,
        Left,
        Right,
        Activate,
        Back
    }
}