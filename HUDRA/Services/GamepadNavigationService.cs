using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Gaming.Input;
using Windows.System;
using HUDRA.Interfaces;
using HUDRA.AttachedProperties;
using HUDRA.Controls;
using Microsoft.UI.Xaml.Input;

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
            // Handle page navigation (L1/R1 shoulder buttons) - only on new presses
            if (newButtons.Contains(GamepadButtons.LeftShoulder))
            {
                PageNavigationRequested?.Invoke(this, new GamepadPageNavigationEventArgs(GamepadPageDirection.Previous));
                return;
            }

            if (newButtons.Contains(GamepadButtons.RightShoulder))
            {
                PageNavigationRequested?.Invoke(this, new GamepadPageNavigationEventArgs(GamepadPageDirection.Next));
                return;
            }

            // Handle navbar button shortcuts (L2/R2 triggers) - only on new presses
            bool leftTriggerActive = reading.LeftTrigger > TRIGGER_THRESHOLD;
            bool rightTriggerActive = reading.RightTrigger > TRIGGER_THRESHOLD;

            if (leftTriggerActive && !_leftTriggerPressed)
            {
                _leftTriggerPressed = true;
                NavbarButtonRequested?.Invoke(this, new GamepadNavbarButtonEventArgs(GamepadNavbarButton.BackToGame));
                return;
            }
            else if (!leftTriggerActive && _leftTriggerPressed)
            {
                _leftTriggerPressed = false;
            }

            if (rightTriggerActive && !_rightTriggerPressed)
            {
                _rightTriggerPressed = true;
                NavbarButtonRequested?.Invoke(this, new GamepadNavbarButtonEventArgs(GamepadNavbarButton.LosslessScaling));
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
                action = GamepadNavigationAction.Activate;
            }
            else if (newButtons.Contains(GamepadButtons.B))
            {
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
                newButtons.Contains(GamepadButtons.LeftShoulder) ||
                newButtons.Contains(GamepadButtons.RightShoulder) ||
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

        // Set dialog open state (prevents activation input from being consumed)
        public void SetDialogOpen()
        {
            _isDialogOpen = true;
            System.Diagnostics.Debug.WriteLine("🎮 Dialog opened - activation input will not be consumed");
        }

        // Clear dialog open state
        public void SetDialogClosed()
        {
            _isDialogOpen = false;
            System.Diagnostics.Debug.WriteLine("🎮 Dialog closed - normal activation logic resumed");
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
