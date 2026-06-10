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
        void OnGamepadBack(); // Handle B button - can be used for page-level back navigation
        void OnGamepadFocusReceived();
        void OnGamepadFocusLost();
        void FocusLastElement(); // Focus the last navigable element within this control

        // Internal focus position for cross-navigation focus memory. Composite
        // controls (several sub-elements behind one focus candidate) override
        // both accessors so the position inside them can be saved when leaving
        // a page and restored on return. Default: no internal state.
        int GamepadFocusMemory { get => 0; set { } }
        
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