using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Gaming.Input;
using Windows.System;
using HUDRA.Interfaces;
using HUDRA.AttachedProperties;
using HUDRA.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;

namespace HUDRA.Services
{
    public class GamepadNavigationService : IDisposable
    {
        private Microsoft.UI.Dispatching.DispatcherQueueTimer? _gamepadTimer;
        private readonly List<Gamepad> _connectedGamepads = new();
        private FrameworkElement? _currentFocusedElement;
        private Frame? _currentFrame;
        private readonly HashSet<GamepadButtons> _pressedButtons = new();
        private DateTime _lastInputTime = DateTime.MinValue;
        private const double INPUT_REPEAT_DELAY_MS = 150;
        private Microsoft.UI.Dispatching.DispatcherQueue? _dispatcherQueue;

        // Trigger state tracking (analog triggers need separate tracking)
        private bool _leftTriggerPressed = false;
        private bool _rightTriggerPressed = false;
        private const double TRIGGER_THRESHOLD = 0.8;

        // Suppress auto focus on first gamepad activation after mouse/touch navigation
        private bool _suppressAutoFocusOnActivation = false;
        
        // Slider activation state
        private bool _isSliderActivated = false;
        private IGamepadNavigable? _activatedSliderControl = null;
        
        // ComboBox activation state
        private bool _isComboBoxOpen = false;
        private IGamepadNavigable? _activeComboBoxControl = null;

        // Polling suspension (for modal dialogs)
        private bool _isPollingPaused = false;

        // Dialog state tracking (to bypass activation input consumption)
        private bool _isDialogOpen = false;
        private ContentDialog? _currentDialog = null;

        // Navbar button cycling with LT/RT
        private List<Button> _navbarButtons = new();
        private int? _selectedNavbarButtonIndex = null; // null = no selection
        private Button? _selectedNavbarButton = null;

        public event EventHandler<GamepadNavigationEventArgs>? NavigationRequested;
        public event EventHandler<GamepadPageNavigationEventArgs>? PageNavigationRequested;
        public event EventHandler<GamepadNavbarButtonEventArgs>? NavbarButtonRequested;
        public event EventHandler<GamepadConnectionEventArgs>? GamepadConnected;
        public event EventHandler<GamepadConnectionEventArgs>? GamepadDisconnected;
        public event EventHandler<bool>? GamepadActiveStateChanged;

        private bool _isGamepadActive = false;
        public bool IsGamepadActive 
        { 
            get => _isGamepadActive;
            private set
            {
                if (_isGamepadActive != value)
                {
                    _isGamepadActive = value;
                    GamepadActiveStateChanged?.Invoke(this, value);
                    System.Diagnostics.Debug.WriteLine($"🎮 Gamepad active state changed: {value}");
                }
            }
        }

        public GamepadNavigationService()
        {
            System.Diagnostics.Debug.WriteLine("🎮 GamepadNavigationService initializing...");

            // Get dispatcher queue for UI thread operations
            _dispatcherQueue = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
            
            if (_dispatcherQueue != null)
            {
                // Create timer on UI thread
                _gamepadTimer = _dispatcherQueue.CreateTimer();
                _gamepadTimer.Interval = TimeSpan.FromMilliseconds(16); // ~60 FPS
                _gamepadTimer.Tick += OnGamepadTimerTick;
            }
            else
            {
                throw new InvalidOperationException("Failed to get DispatcherQueue for GamepadNavigationService");
            }

            // Subscribe to gamepad connection events
            Gamepad.GamepadAdded += OnGamepadAdded;
            Gamepad.GamepadRemoved += OnGamepadRemoved;

            // Check for already connected gamepads
            CheckForConnectedGamepads();

            System.Diagnostics.Debug.WriteLine("🎮 GamepadNavigationService initialized successfully");
        }

        public void SetCurrentFrame(Frame frame)
        {
            _currentFrame = frame;
            System.Diagnostics.Debug.WriteLine($"🎮 Set current frame: {frame?.GetType().Name}");
        }

        private void CheckForConnectedGamepads()
        {
            var gamepads = Gamepad.Gamepads;
            foreach (var gamepad in gamepads)
            {
                OnGamepadAdded(null, gamepad);
            }
        }

        private void OnGamepadAdded(object? sender, Gamepad gamepad)
        {
            if (!_connectedGamepads.Contains(gamepad))
            {
                _connectedGamepads.Add(gamepad);
                System.Diagnostics.Debug.WriteLine($"🎮 Gamepad connected: {gamepad}");
                GamepadConnected?.Invoke(this, new GamepadConnectionEventArgs(gamepad));
                
                // Start timer when first gamepad connects
                if (_connectedGamepads.Count == 1)
                {
                    _gamepadTimer.Start();
                    System.Diagnostics.Debug.WriteLine("🎮 Started gamepad input polling");
                }
            }
        }

        private void OnGamepadRemoved(object? sender, Gamepad gamepad)
        {
            if (_connectedGamepads.Contains(gamepad))
            {
                _connectedGamepads.Remove(gamepad);
                System.Diagnostics.Debug.WriteLine($"🎮 Gamepad disconnected: {gamepad}");
                GamepadDisconnected?.Invoke(this, new GamepadConnectionEventArgs(gamepad));
                
                // Stop timer when no gamepads connected
                if (_connectedGamepads.Count == 0)
                {
                    _gamepadTimer.Stop();
                    SetGamepadActive(false);
                    System.Diagnostics.Debug.WriteLine("🎮 Stopped gamepad input polling");
                }
            }
        }

        private void OnGamepadTimerTick(object? sender, object e)
        {
            if (_connectedGamepads.Count == 0) return;

            foreach (var gamepad in _connectedGamepads.ToList())
            {
                try
                {
                    var reading = gamepad.GetCurrentReading();
                    ProcessGamepadInput(reading);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"🎮 Error reading gamepad: {ex.Message}");
                }
            }
        }

        private void ProcessGamepadInput(GamepadReading reading)
        {
            // Check if any input is being received
            bool hasInput = reading.Buttons != GamepadButtons.None ||
                           Math.Abs(reading.LeftThumbstickX) > 0.1 ||
                           Math.Abs(reading.LeftThumbstickY) > 0.1 ||
                           Math.Abs(reading.RightThumbstickX) > 0.1 ||
                           Math.Abs(reading.RightThumbstickY) > 0.1 ||
                           reading.LeftTrigger > TRIGGER_THRESHOLD ||
                           reading.RightTrigger > TRIGGER_THRESHOLD;

            if (!hasInput) 
            {
                // Still need to update button state even when no input to clear released buttons
                UpdatePressedButtonsState(reading.Buttons);
                return;
            }

            // Activate gamepad navigation on first input (unless dialog is open)
            if (!_isGamepadActive && !_isDialogOpen)
            {
                SetGamepadActive(true);
                System.Diagnostics.Debug.WriteLine("🎮 Gamepad activated on first input");

                // Initialize focus on first input if we have a current frame
                if (_currentFrame?.Content is FrameworkElement rootElement)
                {
                    // Respect suppression flag set by non-gamepad navigation
                    if (!_suppressAutoFocusOnActivation)
                    {
                        InitializePageNavigation(rootElement);
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine("dYZr Auto-focus on activation suppressed (non-gamepad navigation)");
                    }
                }

                // Reset suppression after first activation regardless
                _suppressAutoFocusOnActivation = false;

                // Don't process the activation input as navigation - just consume it for activation
                // Set last input time to prevent repeat logic from triggering immediately
                _lastInputTime = DateTime.Now;

                // Update pressed buttons state to prevent next frame from treating held button as "new"
                UpdatePressedButtonsState(reading.Buttons);

                return;
            }

            // If dialog is open but gamepad not active, activate it without consuming input
            if (!_isGamepadActive && _isDialogOpen)
            {
                SetGamepadActive(true);
                System.Diagnostics.Debug.WriteLine("🎮 Gamepad activated for dialog - input will be processed");
                // Don't return - let the input be processed below
            }

            // Get newly pressed buttons
            var newButtons = GetNewlyPressedButtons(reading.Buttons);

            // Check if we should process input (avoid spam)
            bool shouldProcessRepeats = (DateTime.Now - _lastInputTime).TotalMilliseconds >= INPUT_REPEAT_DELAY_MS;

            // Include trigger input in addition to digital buttons and repeats
            bool hasTriggerInput = reading.LeftTrigger > TRIGGER_THRESHOLD || reading.RightTrigger > TRIGGER_THRESHOLD;

            if (newButtons.Count > 0 || shouldProcessRepeats || hasTriggerInput)
            {
                ProcessNavigationInput(reading, newButtons, shouldProcessRepeats);
                _lastInputTime = DateTime.Now;
            }

            // Update pressed buttons state at END of frame after processing input
            UpdatePressedButtonsState(reading.Buttons);
        }

        private void ActivateSlider(IGamepadNavigable sliderControl)
        {
            _isSliderActivated = true;
            _activatedSliderControl = sliderControl;
            sliderControl.IsSliderActivated = true;
            System.Diagnostics.Debug.WriteLine($"🎮 Slider activated for {sliderControl.GetType().Name}");
        }

        private void DeactivateSlider()
        {
            if (_activatedSliderControl != null)
            {
                _activatedSliderControl.IsSliderActivated = false;
                System.Diagnostics.Debug.WriteLine($"🎮 Slider deactivated for {_activatedSliderControl.GetType().Name}");
            }
            
            _isSliderActivated = false;
            _activatedSliderControl = null;
        }

        private void ActivateComboBox(IGamepadNavigable comboBoxControl)
        {
            _isComboBoxOpen = true;
            _activeComboBoxControl = comboBoxControl;
            comboBoxControl.IsComboBoxOpen = true;
            
            // Store the original selection index for cancellation
            var comboBox = comboBoxControl.GetFocusedComboBox();
            if (comboBox != null)
            {
                comboBoxControl.ComboBoxOriginalIndex = comboBox.SelectedIndex;
                comboBoxControl.IsNavigatingComboBox = false;
            }
            
            System.Diagnostics.Debug.WriteLine($"🎮 ComboBox activated for {comboBoxControl.GetType().Name}, original index: {comboBoxControl.ComboBoxOriginalIndex}");
        }

        private void DeactivateComboBox()
        {
            if (_activeComboBoxControl != null)
            {
                _activeComboBoxControl.IsComboBoxOpen = false;
                System.Diagnostics.Debug.WriteLine($"🎮 ComboBox deactivated for {_activeComboBoxControl.GetType().Name}");
            }
            
            _isComboBoxOpen = false;
            _activeComboBoxControl = null;
        }

        private void NavigateComboBoxItems(ComboBox comboBox, int direction)
        {
            if (comboBox.Items.Count == 0 || _activeComboBoxControl == null) return;

            int currentIndex = comboBox.SelectedIndex;
            int newIndex;

            if (direction > 0)
            {
                // Navigate down
                newIndex = (currentIndex + 1) % comboBox.Items.Count;
            }
            else
            {
                // Navigate up
                newIndex = currentIndex <= 0 ? comboBox.Items.Count - 1 : currentIndex - 1;
            }

            // Set navigation flag to prevent SelectionChanged from applying changes
            _activeComboBoxControl.IsNavigatingComboBox = true;
            comboBox.SelectedIndex = newIndex;
            
            System.Diagnostics.Debug.WriteLine($"🎮 ComboBox navigated to item {newIndex} (direction: {direction}) - navigation mode active");
        }

        private void UpdatePressedButtonsState(GamepadButtons currentButtons)
        {
            _pressedButtons.Clear();
            foreach (GamepadButtons button in Enum.GetValues<GamepadButtons>())
            {
                if (currentButtons.HasFlag(button))
                {
                    _pressedButtons.Add(button);
                }
            }
        }

        private List<GamepadButtons> GetNewlyPressedButtons(GamepadButtons currentButtons)
        {
            var newButtons = new List<GamepadButtons>();
            
            foreach (GamepadButtons button in Enum.GetValues<GamepadButtons>())
            {
                if (currentButtons.HasFlag(button) && !_pressedButtons.Contains(button))
                {
                    newButtons.Add(button);
                }
            }

            return newButtons;
        }

        private void ProcessNavigationInput(GamepadReading reading, List<GamepadButtons> newButtons, bool shouldProcessRepeats)
        {
            // When dialog is open, only process A/B buttons to control the dialog
            if (_isDialogOpen && _currentDialog != null)
            {
                if (newButtons.Contains(GamepadButtons.A))
                {
                    // A button = Primary button (Force Quit)
                    System.Diagnostics.Debug.WriteLine("🎮 A button pressed - triggering dialog primary action");
                    _dispatcherQueue?.TryEnqueue(() =>
                    {
                        if (_currentDialog != null)
                        {
                            // Programmatically click the primary button
                            TriggerDialogPrimaryButton(_currentDialog);
                        }
                    });
                    return;
                }
                else if (newButtons.Contains(GamepadButtons.B))
                {
                    // B button = Close/Cancel button
                    System.Diagnostics.Debug.WriteLine("🎮 B button pressed - triggering dialog cancel");
                    _dispatcherQueue?.TryEnqueue(() =>
                    {
                        _currentDialog?.Hide();
                    });
                    return;
                }
                // Ignore all other input when dialog is open
                return;
            }

            // Handle navbar button cycling with LT/RT triggers - only on new presses
            bool leftTriggerActive = reading.LeftTrigger > TRIGGER_THRESHOLD;
            bool rightTriggerActive = reading.RightTrigger > TRIGGER_THRESHOLD;

            if (leftTriggerActive && !_leftTriggerPressed)
            {
                _leftTriggerPressed = true;
                CycleNavbarButtonSelection(-1); // Cycle up (toward top)
                return;
            }
            else if (!leftTriggerActive && _leftTriggerPressed)
            {
                _leftTriggerPressed = false;
            }

            if (rightTriggerActive && !_rightTriggerPressed)
            {
                _rightTriggerPressed = true;
                CycleNavbarButtonSelection(1); // Cycle down (toward bottom)
                return;
            }
            else if (!rightTriggerActive && _rightTriggerPressed)
            {
                _rightTriggerPressed = false;
            }

            // Handle standard navigation
            GamepadNavigationAction? action = null;
            
            // D-pad navigation (both new presses and repeats)
            if (reading.Buttons.HasFlag(GamepadButtons.DPadUp) || reading.LeftThumbstickY > 0.7)
            {
                if (newButtons.Contains(GamepadButtons.DPadUp) || shouldProcessRepeats)
                    action = GamepadNavigationAction.Up;
            }
            else if (reading.Buttons.HasFlag(GamepadButtons.DPadDown) || reading.LeftThumbstickY < -0.7)
            {
                if (newButtons.Contains(GamepadButtons.DPadDown) || shouldProcessRepeats)
                    action = GamepadNavigationAction.Down;
            }
            else if (reading.Buttons.HasFlag(GamepadButtons.DPadLeft) || reading.LeftThumbstickX < -0.7)
            {
                if (newButtons.Contains(GamepadButtons.DPadLeft) || shouldProcessRepeats)
                    action = GamepadNavigationAction.Left;
            }
            else if (reading.Buttons.HasFlag(GamepadButtons.DPadRight) || reading.LeftThumbstickX > 0.7)
            {
                if (newButtons.Contains(GamepadButtons.DPadRight) || shouldProcessRepeats)
                    action = GamepadNavigationAction.Right;
            }

            // Action buttons (only on new presses)
            if (newButtons.Contains(GamepadButtons.A))
            {
                // Check if a navbar button is selected - invoke it directly
                if (_selectedNavbarButtonIndex.HasValue && _selectedNavbarButton != null)
                {
                    InvokeSelectedNavbarButton();
                    return;
                }
                action = GamepadNavigationAction.Activate;
            }
            else if (newButtons.Contains(GamepadButtons.B))
            {
                // B button clears navbar selection if one exists
                if (_selectedNavbarButtonIndex.HasValue)
                {
                    ClearNavbarButtonSelection();
                    return;
                }
                action = GamepadNavigationAction.Back;
            }

            if (action.HasValue)
            {
                HandleNavigationAction(action.Value);
            }

            // Add haptic feedback for important actions
            // Check trigger state changes for haptic feedback
            bool leftTriggerJustPressed = leftTriggerActive && !_leftTriggerPressed;
            bool rightTriggerJustPressed = rightTriggerActive && !_rightTriggerPressed;

            if (newButtons.Contains(GamepadButtons.A) ||
                leftTriggerJustPressed ||
                rightTriggerJustPressed)
            {
                TriggerHapticFeedback();
            }
        }

        private void HandleNavigationAction(GamepadNavigationAction action)
        {
            // Handle slider-specific actions when a slider is activated
            if (_isSliderActivated && _activatedSliderControl != null)
            {
                switch (action)
                {
                    case GamepadNavigationAction.Left:
                        _activatedSliderControl.AdjustSliderValue(-1);
                        return;
                    case GamepadNavigationAction.Right:
                        _activatedSliderControl.AdjustSliderValue(1);
                        return;
                    case GamepadNavigationAction.Activate:
                    case GamepadNavigationAction.Back:
                        DeactivateSlider();
                        return;
                }
                // Block all other navigation when slider is active
                return;
            }
            
            // Handle ComboBox-specific actions when a ComboBox is open
            if (_isComboBoxOpen && _activeComboBoxControl != null)
            {
                var comboBox = _activeComboBoxControl.GetFocusedComboBox();
                if (comboBox != null)
                {
                    switch (action)
                    {
                        case GamepadNavigationAction.Up:
                            // Navigate up in ComboBox items
                            NavigateComboBoxItems(comboBox, -1);
                            return;
                        case GamepadNavigationAction.Down:
                            // Navigate down in ComboBox items
                            NavigateComboBoxItems(comboBox, 1);
                            return;
                        case GamepadNavigationAction.Activate:
                            // Clear navigation flag and process selection
                            _activeComboBoxControl.IsNavigatingComboBox = false;
                            
                            // Manually trigger selection processing
                            _activeComboBoxControl.ProcessCurrentSelection();
                            
                            comboBox.IsDropDownOpen = false;
                            DeactivateComboBox();
                            System.Diagnostics.Debug.WriteLine($"🎮 ComboBox A button - confirmed selection: {comboBox.SelectedIndex}");
                            return;
                        case GamepadNavigationAction.Back:
                            // Cancel and restore original selection before closing
                            int originalIndex = _activeComboBoxControl.ComboBoxOriginalIndex;
                            comboBox.SelectedIndex = originalIndex;
                            _activeComboBoxControl.IsNavigatingComboBox = false;
                            comboBox.IsDropDownOpen = false;
                            DeactivateComboBox();
                            System.Diagnostics.Debug.WriteLine($"🎮 ComboBox B button - cancelled, restored to index: {originalIndex}");
                            return;
                    }
                }
                // Block all other navigation when ComboBox is open
                return;
            }

            if (_currentFocusedElement != null)
            {
                // Try to handle action with current focused element first
                if (_currentFocusedElement is IGamepadNavigable navigableControl)
                {
                    bool handled = false;
                    switch (action)
                    {
                        case GamepadNavigationAction.Up when navigableControl.CanNavigateUp:
                            navigableControl.OnGamepadNavigateUp();
                            handled = true;
                            break;
                        case GamepadNavigationAction.Down when navigableControl.CanNavigateDown:
                            navigableControl.OnGamepadNavigateDown();
                            handled = true;
                            break;
                        case GamepadNavigationAction.Left when navigableControl.CanNavigateLeft:
                            navigableControl.OnGamepadNavigateLeft();
                            handled = true;
                            break;
                        case GamepadNavigationAction.Right when navigableControl.CanNavigateRight:
                            navigableControl.OnGamepadNavigateRight();
                            handled = true;
                            break;
                        case GamepadNavigationAction.Activate when navigableControl.CanActivate:
                            // Check if this is a slider that should be activated
                            if (navigableControl.IsSlider && !_isSliderActivated)
                            {
                                ActivateSlider(navigableControl);
                            }
                            else
                            {
                                navigableControl.OnGamepadActivate();

                                // Check if a ComboBox was opened and needs to be tracked
                                if (navigableControl.HasComboBoxes)
                                {
                                    var comboBox = navigableControl.GetFocusedComboBox();
                                    if (comboBox != null && comboBox.IsDropDownOpen)
                                    {
                                        ActivateComboBox(navigableControl);
                                    }
                                }
                            }
                            handled = true;
                            break;
                        case GamepadNavigationAction.Back:
                            // If no control is active (slider/combobox already handled above),
                            // collapse parent expander if inside one
                            var parentExpander = FindNavigableParent(_currentFocusedElement);
                            if (parentExpander is NavigableExpander expander && expander.IsExpanded)
                            {
                                // Collapse the expander and return focus to it
                                expander.IsExpanded = false;
                                SetFocus(expander);
                                System.Diagnostics.Debug.WriteLine($"🎮 B button: Collapsed parent expander and returned focus to header");
                                handled = true;
                            }
                            break;
                    }

                    if (handled) return;
                }
            }

            // Handle focus movement between controls
            if (action == GamepadNavigationAction.Up ||
                action == GamepadNavigationAction.Down ||
                action == GamepadNavigationAction.Left ||
                action == GamepadNavigationAction.Right)
            {
                NavigateToAdjacentElement(action);
            }

            // Notify any listeners
            NavigationRequested?.Invoke(this, new GamepadNavigationEventArgs(action, _currentFocusedElement));
        }

        private void NavigateToAdjacentElement(GamepadNavigationAction direction)
        {
            if (_currentFrame?.Content is not FrameworkElement rootElement) return;

            var navigableElements = GamepadNavigation.GetNavigableElements(rootElement).ToList();
            if (navigableElements.Count == 0) return;

            int currentIndex = _currentFocusedElement != null
                ? navigableElements.IndexOf(_currentFocusedElement)
                : -1;

            // If current element is not in the list, check if it's inside a NavigableExpander
            if (currentIndex == -1 && _currentFocusedElement != null)
            {
                // Find parent NavigableExpander
                var parent = FindNavigableParent(_currentFocusedElement);
                if (parent != null)
                {
                    currentIndex = navigableElements.IndexOf(parent);
                    System.Diagnostics.Debug.WriteLine($"🎮 Current element not in nav list, using parent expander at index {currentIndex}");

                    // For UP navigation, return focus to the parent expander
                    if (direction == GamepadNavigationAction.Up || direction == GamepadNavigationAction.Left)
                    {
                        SetFocus(parent);
                        return;
                    }
                }
            }

            int nextIndex = currentIndex;
            switch (direction)
            {
                case GamepadNavigationAction.Up:
                case GamepadNavigationAction.Left:
                    nextIndex = currentIndex > 0 ? currentIndex - 1 : navigableElements.Count - 1;
                    break;

                case GamepadNavigationAction.Down:
                case GamepadNavigationAction.Right:
                    nextIndex = currentIndex < navigableElements.Count - 1 ? currentIndex + 1 : 0;
                    break;
            }

            if (nextIndex != currentIndex)
            {
                var nextElement = navigableElements[nextIndex];

                // Check if next element is an open NavigableExpander
                if (nextElement is NavigableExpander expander && expander.IsExpanded && expander.Body is IGamepadNavigable bodyControl && expander.Body is FrameworkElement bodyElement)
                {
                    // For UP navigation, enter the body at the LAST element
                    if (direction == GamepadNavigationAction.Up || direction == GamepadNavigationAction.Left)
                    {
                        SetFocus(bodyElement);
                        bodyControl.FocusLastElement();
                        System.Diagnostics.Debug.WriteLine($"🎮 Navigated UP into expanded expander at last element");
                        return;
                    }
                    // For DOWN navigation, the expander's CanNavigateDown will handle it
                }

                SetFocus(nextElement);
            }
        }

        private FrameworkElement? FindNavigableParent(FrameworkElement element)
        {
            var parent = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(element);
            while (parent != null)
            {
                if (parent is NavigableExpander expander)
                {
                    return expander;
                }
                parent = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(parent);
            }
            return null;
        }

        public void SetFocus(FrameworkElement? element)
        {
            if (_currentFocusedElement == element) return; // Already focused

            if (_currentFocusedElement != null)
            {
                GamepadNavigation.SetIsCurrentFocus(_currentFocusedElement, false);
                System.Diagnostics.Debug.WriteLine($"🎮 Removed focus from: {_currentFocusedElement.GetType().Name}");
            }

            _currentFocusedElement = element;

            if (_currentFocusedElement != null)
            {
                GamepadNavigation.SetIsCurrentFocus(_currentFocusedElement, true);
                _currentFocusedElement.Focus(FocusState.Programmatic);

                // Scroll element into view if it's out of viewport
                try
                {
                    _currentFocusedElement.StartBringIntoView();
                    System.Diagnostics.Debug.WriteLine($"🎮 Set focus to: {_currentFocusedElement.GetType().Name} and scrolled into view");
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"🎮 Set focus to: {_currentFocusedElement.GetType().Name} (scroll failed: {ex.Message})");
                }
            }
        }

        public void ClearFocus()
        {
            if (_currentFocusedElement != null)
            {
                GamepadNavigation.SetIsCurrentFocus(_currentFocusedElement, false);
                System.Diagnostics.Debug.WriteLine($"🎮 Cleared focus from: {_currentFocusedElement.GetType().Name}");
                _currentFocusedElement = null;
            }
        }

        public void InitializePageNavigation(FrameworkElement rootElement, bool isFromPageNavigation = false)
        {
            System.Diagnostics.Debug.WriteLine($"🎮 InitializePageNavigation called for {rootElement.GetType().Name}, fromPageNav: {isFromPageNavigation}");
            
            // Clear any existing focus first to prevent lingering borders
            ClearFocus();
            
            var navigableElements = GamepadNavigation.GetNavigableElements(rootElement).ToList();
            System.Diagnostics.Debug.WriteLine($"🎮 Found {navigableElements.Count} navigable elements");
            
            if (navigableElements.Count > 0)
            {
                // Always set focus when navigating between pages, 
                // only wait for gamepad input on initial app load
                if (_isGamepadActive || isFromPageNavigation)
                {
                    SetFocus(navigableElements[0]);
                    System.Diagnostics.Debug.WriteLine($"🎮 Initialized page navigation with focus on: {navigableElements[0].GetType().Name}");
                    
                    // If navigating between pages but gamepad wasn't active, activate it now
                    if (!_isGamepadActive && isFromPageNavigation)
                    {
                        SetGamepadActive(true);
                        System.Diagnostics.Debug.WriteLine($"🎮 Activated gamepad due to page navigation");
                    }
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"🎮 Page navigation ready with {navigableElements.Count} elements, waiting for gamepad input");
                }
            }
            else
            {
                System.Diagnostics.Debug.WriteLine("🎮 No navigable elements found on page");
            }
        }

        public void SetGamepadActive(bool active)
        {
            IsGamepadActive = active;
        }

        // Public API: call when navigating via mouse/touch/keyboard to avoid immediate focus when gamepad wakes up
        public void SuppressAutoFocusOnNextActivation()
        {
            _suppressAutoFocusOnActivation = true;
            System.Diagnostics.Debug.WriteLine("dYZr Suppressing auto-focus on next gamepad activation");
        }

        public void DeactivateGamepadMode()
        {
            if (IsGamepadActive)
            {
                ClearFocus();
                SetGamepadActive(false);
                
                // Also clear any active states
                if (_isSliderActivated)
                {
                    DeactivateSlider();
                }
                if (_isComboBoxOpen)
                {
                    DeactivateComboBox();
                }
                
                System.Diagnostics.Debug.WriteLine("🎮 Gamepad mode deactivated");
            }
        }

        private void TriggerHapticFeedback()
        {
            try
            {
                // Simple haptic feedback for connected gamepads
                foreach (var gamepad in _connectedGamepads)
                {
                    gamepad.Vibration = new GamepadVibration
                    {
                        LeftMotor = 0.2,
                        RightMotor = 0.2,
                        LeftTrigger = 0.0,
                        RightTrigger = 0.0
                    };

                    // Stop vibration after short duration
                    if (_dispatcherQueue != null)
                    {
                        var timer = _dispatcherQueue.CreateTimer();
                        timer.Interval = TimeSpan.FromMilliseconds(100);
                        timer.Tick += (s, e) =>
                        {
                            gamepad.Vibration = new GamepadVibration();
                            timer.Stop();
                        };
                        timer.Start();
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"🎮 Haptic feedback error: {ex.Message}");
            }
        }

        private void TriggerDialogPrimaryButton(ContentDialog dialog)
        {
            try
            {
                // Find the primary button in the ContentDialog's visual tree and invoke it
                var primaryButton = FindPrimaryButtonInDialog(dialog);
                if (primaryButton != null)
                {
                    // Use automation peer to invoke the button
                    var peer = new ButtonAutomationPeer(primaryButton);
                    var invokeProvider = peer.GetPattern(PatternInterface.Invoke) as IInvokeProvider;
                    invokeProvider?.Invoke();
                    System.Diagnostics.Debug.WriteLine("🎮 Primary button invoked via automation");
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine("🎮 Warning: Could not find primary button in dialog");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"🎮 Error triggering dialog primary button: {ex.Message}");
            }
        }

        private Button? FindPrimaryButtonInDialog(DependencyObject parent)
        {
            // Search the visual tree for a button with specific names used by ContentDialog
            int childCount = VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < childCount; i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);

                // ContentDialog typically names its buttons "PrimaryButton", "SecondaryButton", "CloseButton"
                if (child is Button button && child is FrameworkElement element)
                {
                    if (element.Name == "PrimaryButton")
                    {
                        return button;
                    }
                }

                // Recursively search children
                var result = FindPrimaryButtonInDialog(child);
                if (result != null)
                {
                    return result;
                }
            }
            return null;
        }

        // Keyboard fallback for testing
        public void OnKeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (!_isGamepadActive) return; // Only process when gamepad navigation is active
            
            System.Diagnostics.Debug.WriteLine($"🎮 Keyboard input: {e.Key}");
            
            GamepadNavigationAction? action = null;
            
            // Map keyboard keys to gamepad actions
            switch (e.Key)
            {
                case VirtualKey.Up:
                case VirtualKey.W:
                    action = GamepadNavigationAction.Up;
                    break;
                case VirtualKey.Down:
                case VirtualKey.S:
                    action = GamepadNavigationAction.Down;
                    break;
                case VirtualKey.Left:
                case VirtualKey.A:
                    action = GamepadNavigationAction.Left;
                    break;
                case VirtualKey.Right:
                case VirtualKey.D:
                    action = GamepadNavigationAction.Right;
                    break;
                case VirtualKey.Enter:
                case VirtualKey.Space:
                    action = GamepadNavigationAction.Activate;
                    break;
                case VirtualKey.Escape:
                    action = GamepadNavigationAction.Back;
                    break;
                case VirtualKey.F1:
                    // Special key for manual focus testing
                    if (_currentFrame?.Content is FrameworkElement rootElement)
                    {
                        InitializePageNavigation(rootElement);
                    }
                    e.Handled = true;
                    return;
            }

            if (action.HasValue)
            {
                HandleNavigationAction(action.Value);
                e.Handled = true;
            }
        }

        // Suspend gamepad polling (for modal dialogs)
        public void SuspendPolling()
        {
            if (!_isPollingPaused && _gamepadTimer?.IsRunning == true)
            {
                _gamepadTimer.Stop();
                _isPollingPaused = true;
                DeactivateGamepadMode();
                System.Diagnostics.Debug.WriteLine("🎮 Gamepad polling suspended (modal dialog)");
            }
        }

        // Resume gamepad polling after modal dialog
        public void ResumePolling()
        {
            if (_isPollingPaused && _connectedGamepads.Count > 0)
            {
                _gamepadTimer?.Start();
                _isPollingPaused = false;
                System.Diagnostics.Debug.WriteLine("🎮 Gamepad polling resumed");
            }
        }

        // Set dialog open state (prevents activation input from being consumed and blocks UI navigation)
        public void SetDialogOpen(ContentDialog dialog)
        {
            _isDialogOpen = true;
            _currentDialog = dialog;
            // Clear focus from UI to prevent background controls from receiving input
            ClearFocus();
            System.Diagnostics.Debug.WriteLine("🎮 Dialog opened - UI navigation blocked, dialog has exclusive input");
        }

        // Clear dialog open state
        public void SetDialogClosed()
        {
            _isDialogOpen = false;
            _currentDialog = null;
            System.Diagnostics.Debug.WriteLine("🎮 Dialog closed - normal activation logic resumed");
        }

        // Register navbar buttons for spatial navigation
        public void RegisterNavbarButtons(List<Button> buttons)
        {
            _navbarButtons = buttons ?? new List<Button>();
            System.Diagnostics.Debug.WriteLine($"🎮 Registered {_navbarButtons.Count} navbar buttons");
        }

        // Cycle through navbar buttons with LT/RT triggers
        private void CycleNavbarButtonSelection(int direction)
        {
            if (_navbarButtons.Count == 0) return;

            // Check if any navbar buttons are visible
            bool anyVisible = _navbarButtons.Any(b => b.Visibility == Visibility.Visible);
            if (!anyVisible)
            {
                ClearNavbarButtonSelection();
                return;
            }

            // If no button currently selected, start from appropriate end
            if (!_selectedNavbarButtonIndex.HasValue)
            {
                // LT (-1) starts from top, RT (+1) starts from bottom
                _selectedNavbarButtonIndex = direction < 0 ? 0 : _navbarButtons.Count - 1;
            }
            else
            {
                // Cycle to next/previous button
                _selectedNavbarButtonIndex += direction;
            }

            // Wrap around
            if (_selectedNavbarButtonIndex < 0)
            {
                _selectedNavbarButtonIndex = _navbarButtons.Count - 1;
            }
            else if (_selectedNavbarButtonIndex >= _navbarButtons.Count)
            {
                _selectedNavbarButtonIndex = 0;
            }

            // Find next visible button (skip invisible ones)
            int startIndex = _selectedNavbarButtonIndex.Value;
            int attempts = 0;
            int maxAttempts = _navbarButtons.Count;

            while (attempts < maxAttempts)
            {
                var button = _navbarButtons[_selectedNavbarButtonIndex.Value];
                if (button.Visibility == Visibility.Visible)
                {
                    // Found visible button - select it
                    SetNavbarButtonSelection(button);
                    System.Diagnostics.Debug.WriteLine($"🎮 LT/RT: Selected navbar button {_selectedNavbarButtonIndex}: {button.Name}");
                    return;
                }

                // Not visible, continue cycling
                _selectedNavbarButtonIndex += direction;
                if (_selectedNavbarButtonIndex < 0)
                {
                    _selectedNavbarButtonIndex = _navbarButtons.Count - 1;
                }
                else if (_selectedNavbarButtonIndex >= _navbarButtons.Count)
                {
                    _selectedNavbarButtonIndex = 0;
                }

                attempts++;
            }

            // No visible buttons found
            ClearNavbarButtonSelection();
        }

        // Set visual selection on navbar button
        private void SetNavbarButtonSelection(Button button)
        {
            // Clear previous selection
            if (_selectedNavbarButton != null)
            {
                _selectedNavbarButton.BorderBrush = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
                _selectedNavbarButton.BorderThickness = new Thickness(0);
            }

            // Set new selection
            _selectedNavbarButton = button;
            button.BorderBrush = new SolidColorBrush(Microsoft.UI.Colors.DarkViolet);
            button.BorderThickness = new Thickness(2);
            button.Focus(FocusState.Programmatic);
        }

        // Clear navbar button selection
        private void ClearNavbarButtonSelection()
        {
            if (_selectedNavbarButton != null)
            {
                _selectedNavbarButton.BorderBrush = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
                _selectedNavbarButton.BorderThickness = new Thickness(0);
                _selectedNavbarButton = null;
            }
            _selectedNavbarButtonIndex = null;
            System.Diagnostics.Debug.WriteLine("🎮 Cleared navbar button selection");
        }

        // Invoke the currently selected navbar button
        private void InvokeSelectedNavbarButton()
        {
            if (_selectedNavbarButton == null) return;

            System.Diagnostics.Debug.WriteLine($"🎮 A button: Invoking selected navbar button {_selectedNavbarButton.Name}");

            // Programmatically click the button using UI Automation
            var peer = new ButtonAutomationPeer(_selectedNavbarButton);
            var invokeProvider = peer.GetPattern(PatternInterface.Invoke) as IInvokeProvider;
            invokeProvider?.Invoke();

            // Clear selection after invocation
            ClearNavbarButtonSelection();
        }

        public void Dispose()
        {
            System.Diagnostics.Debug.WriteLine("🎮 GamepadNavigationService disposing...");

            _gamepadTimer?.Stop();

            Gamepad.GamepadAdded -= OnGamepadAdded;
            Gamepad.GamepadRemoved -= OnGamepadRemoved;

            _connectedGamepads.Clear();

            System.Diagnostics.Debug.WriteLine("🎮 GamepadNavigationService disposed");
        }
    }

    // Event argument classes
    public class GamepadNavigationEventArgs : EventArgs
    {
        public GamepadNavigationAction Action { get; }
        public FrameworkElement? CurrentElement { get; }

        public GamepadNavigationEventArgs(GamepadNavigationAction action, FrameworkElement? currentElement)
        {
            Action = action;
            CurrentElement = currentElement;
        }
    }

    public class GamepadPageNavigationEventArgs : EventArgs
    {
        public GamepadPageDirection Direction { get; }

        public GamepadPageNavigationEventArgs(GamepadPageDirection direction)
        {
            Direction = direction;
        }
    }

    public class GamepadConnectionEventArgs : EventArgs
    {
        public Gamepad Gamepad { get; }

        public GamepadConnectionEventArgs(Gamepad gamepad)
        {
            Gamepad = gamepad;
        }
    }

    public class GamepadNavbarButtonEventArgs : EventArgs
    {
        public GamepadNavbarButton Button { get; }

        public GamepadNavbarButtonEventArgs(GamepadNavbarButton button)
        {
            Button = button;
        }
    }

    public enum GamepadPageDirection
    {
        Previous,
        Next
    }

    public enum GamepadNavbarButton
    {
        BackToGame,
        LosslessScaling
    }
}
