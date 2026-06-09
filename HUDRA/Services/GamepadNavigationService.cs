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
using HUDRA.Services.GamepadInput;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;

namespace HUDRA.Services
{
    public class GamepadNavigationService : IDisposable
    {
        private readonly GamepadInputReader _reader;
        private FrameworkElement? _currentFocusedElement;
        private Frame? _currentFrame;
        private UIElement? _layoutRoot;
        private Microsoft.UI.Dispatching.DispatcherQueue? _dispatcherQueue;

        // Suppress auto focus on first gamepad activation after mouse/touch navigation
        private bool _suppressAutoFocusOnActivation = false;

        // Slider activation state
        private bool _isSliderActivated = false;
        private IGamepadNavigable? _activatedSliderControl = null;

        // ComboBox activation state
        private bool _isComboBoxOpen = false;
        private IGamepadNavigable? _activeComboBoxControl = null;

        // While input processing is paused (Library page), buttons we intercept
        // (navbar invoke, page nav) are masked out of the forwarded raw readings
        // until released, so the page's own edge detection never sees a phantom
        // "new" press one tick after we consumed it.
        private GamepadButtons _pausedInterceptMask = GamepadButtons.None;
        private bool _skipRawForwardThisTick = false;

        // Dialog state tracking (to bypass activation input consumption)
        private bool _isDialogOpen = false;
        private ContentDialog? _currentDialog = null;

        // Navbar button cycling with LT/RT
        private List<Button> _navbarButtons = new();
        private int? _selectedNavbarButtonIndex = null; // null = no selection
        private Button? _selectedNavbarButton = null;

        // Window visibility tracking - ignore input when window is hidden
        private WindowManagementService? _windowManager;

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

        // Flag to pause all input processing (for pages that use custom navigation like Library)
        private bool _inputProcessingPaused = false;
        public bool IsInputProcessingPaused => _inputProcessingPaused;

        // Delegate for forwarding raw gamepad input to custom pages
        public event EventHandler<GamepadReading>? RawGamepadInput;

        public void PauseInputProcessing()
        {
            _inputProcessingPaused = true;
            System.Diagnostics.Debug.WriteLine("🎮 GamepadNavigationService: Input processing PAUSED - will forward raw input");
        }

        public void ResumeInputProcessing()
        {
            _inputProcessingPaused = false;
            System.Diagnostics.Debug.WriteLine("🎮 GamepadNavigationService: Input processing RESUMED");
        }

        public GamepadNavigationService()
        {
            System.Diagnostics.Debug.WriteLine("🎮 GamepadNavigationService initializing...");

            // Get dispatcher queue for UI thread operations
            _dispatcherQueue = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();

            if (_dispatcherQueue == null)
            {
                throw new InvalidOperationException("Failed to get DispatcherQueue for GamepadNavigationService");
            }

            _reader = new GamepadInputReader();
            _reader.ActionDispatched += OnReaderAction;
            _reader.ReadingAvailable += OnReaderReading;
            _reader.GamepadConnected += (s, e) => GamepadConnected?.Invoke(this, e);
            _reader.GamepadDisconnected += (s, e) =>
            {
                GamepadDisconnected?.Invoke(this, e);
                if (!_reader.HasConnectedGamepads)
                {
                    SetGamepadActive(false);
                }
            };

            System.Diagnostics.Debug.WriteLine("🎮 GamepadNavigationService initialized successfully");
        }

        /// <summary>The raw input layer. Exposed for consumers that need direct access (haptics, key mapping).</summary>
        public GamepadInputReader InputReader => _reader;

        public void SetCurrentFrame(Frame frame)
        {
            _currentFrame = frame;
            System.Diagnostics.Debug.WriteLine($"🎮 Set current frame: {frame?.GetType().Name}");
        }

        public void SetLayoutRoot(UIElement layoutRoot)
        {
            _layoutRoot = layoutRoot;
            System.Diagnostics.Debug.WriteLine($"🎮 Set layout root: {layoutRoot?.GetType().Name}");
        }

        public void SetWindowManager(WindowManagementService windowManager)
        {
            _windowManager = windowManager;
            System.Diagnostics.Debug.WriteLine("🎮 Set window manager for visibility tracking");
        }

        /// <summary>
        /// Semantic input from the reader. Single priority chain: window hidden →
        /// dialog → paused (Library) → activation → normal handling. Dialog A/B
        /// handling lives ONLY here (it was previously duplicated in two methods).
        /// </summary>
        private void OnReaderAction(object? sender, GamepadEvent e)
        {
            // PRIORITY 0: Ignore all input when window is hidden
            // This prevents accidental navigation/actions while user plays a game
            if (_windowManager != null && !_windowManager.IsVisible) return;

            // Keyboard-mapped input only participates once gamepad mode is active
            if (e.Source == GamepadEventSource.Keyboard && !_isGamepadActive) return;

            // PRIORITY 1: Dialog has exclusive input (even while input processing is paused)
            if (_isDialogOpen && _currentDialog != null)
            {
                if (!_isGamepadActive)
                {
                    SetGamepadActive(true);
                }

                if (e.IsRepeat) return;

                if (e.Action == GamepadAction.Accept)
                {
                    System.Diagnostics.Debug.WriteLine("🎮 A button pressed - triggering dialog primary action");
                    _dispatcherQueue?.TryEnqueue(() =>
                    {
                        if (_currentDialog != null)
                        {
                            TriggerDialogPrimaryButton(_currentDialog);
                        }
                    });
                }
                else if (e.Action == GamepadAction.Back)
                {
                    System.Diagnostics.Debug.WriteLine("🎮 B button pressed - triggering dialog cancel");
                    _dispatcherQueue?.TryEnqueue(() => _currentDialog?.Hide());
                }

                // Block all other input while dialog is open
                return;
            }

            // PRIORITY 2: Input processing paused (Library page) - intercept chrome
            // input (page nav, navbar); everything else reaches the page through
            // the raw reading forwarded in OnReaderReading.
            if (_inputProcessingPaused)
            {
                HandlePausedAction(e);
                return;
            }

            // PRIORITY 3: Activate gamepad mode on first input
            if (!_isGamepadActive)
            {
                SetGamepadActive(true);
                System.Diagnostics.Debug.WriteLine("🎮 Gamepad activated on first input");

                // CRITICAL: Clear any existing keyboard focus borders before gamepad takes over
                ClearFocus();

                // L1/R1/L2/R2 are processed even on the wake press; everything else
                // is consumed by activation (it just summons the focus visuals)
                bool isChromeInput = e.Action is GamepadAction.LB or GamepadAction.RB
                                              or GamepadAction.LT or GamepadAction.RT;
                if (!isChromeInput)
                {
                    if (_currentFrame?.Content is FrameworkElement rootElement && !_suppressAutoFocusOnActivation)
                    {
                        InitializePageNavigation(rootElement);
                    }
                    _suppressAutoFocusOnActivation = false;
                    return;
                }

                _suppressAutoFocusOnActivation = false;
            }

            HandleAction(e);
        }

        /// <summary>
        /// Chrome input that stays live while a page (Library) handles its own
        /// raw input: L1/R1 page nav, L2/R2 navbar cycling, A/B on a navbar selection.
        /// Intercepted buttons are masked out of the forwarded raw readings.
        /// </summary>
        private void HandlePausedAction(GamepadEvent e)
        {
            switch (e.Action)
            {
                case GamepadAction.LB when !e.IsRepeat:
                    InterceptPausedButton(GamepadButtons.LeftShoulder);
                    PageNavigationRequested?.Invoke(this, new GamepadPageNavigationEventArgs(GamepadPageDirection.Previous));
                    return;

                case GamepadAction.RB when !e.IsRepeat:
                    InterceptPausedButton(GamepadButtons.RightShoulder);
                    PageNavigationRequested?.Invoke(this, new GamepadPageNavigationEventArgs(GamepadPageDirection.Next));
                    return;

                case GamepadAction.LT when !e.IsRepeat:
                    _skipRawForwardThisTick = true;
                    CycleNavbarButtonSelection(-1);
                    _reader.PulseHaptics();
                    return;

                case GamepadAction.RT when !e.IsRepeat:
                    _skipRawForwardThisTick = true;
                    CycleNavbarButtonSelection(1);
                    _reader.PulseHaptics();
                    return;

                case GamepadAction.Accept when !e.IsRepeat
                                               && _selectedNavbarButtonIndex.HasValue
                                               && _selectedNavbarButton != null:
                    InterceptPausedButton(GamepadButtons.A);
                    InvokeSelectedNavbarButton();
                    return;

                case GamepadAction.Back when !e.IsRepeat && _selectedNavbarButtonIndex.HasValue:
                    InterceptPausedButton(GamepadButtons.B);
                    ClearNavbarButtonSelection();
                    return;
            }
            // Anything else flows to the page via raw forwarding
        }

        /// <summary>Normal-mode handling of a semantic action.</summary>
        private void HandleAction(GamepadEvent e)
        {
            switch (e.Action)
            {
                case GamepadAction.LB:
                    if (e.IsRepeat) return;
                    if (_selectedNavbarButtonIndex.HasValue)
                    {
                        ClearNavbarButtonSelection();
                    }
                    PageNavigationRequested?.Invoke(this, new GamepadPageNavigationEventArgs(GamepadPageDirection.Previous));
                    _reader.PulseHaptics();
                    return;

                case GamepadAction.RB:
                    if (e.IsRepeat) return;
                    if (_selectedNavbarButtonIndex.HasValue)
                    {
                        ClearNavbarButtonSelection();
                    }
                    PageNavigationRequested?.Invoke(this, new GamepadPageNavigationEventArgs(GamepadPageDirection.Next));
                    _reader.PulseHaptics();
                    return;

                case GamepadAction.LT:
                    if (e.IsRepeat) return;
                    CycleNavbarButtonSelection(-1);
                    _reader.PulseHaptics();
                    return;

                case GamepadAction.RT:
                    if (e.IsRepeat) return;
                    CycleNavbarButtonSelection(1);
                    _reader.PulseHaptics();
                    return;

                case GamepadAction.NavUp:
                case GamepadAction.NavDown:
                case GamepadAction.NavLeft:
                case GamepadAction.NavRight:
                    // D-pad/analog use clears any navbar selection
                    if (_selectedNavbarButtonIndex.HasValue)
                    {
                        ClearNavbarButtonSelection();
                    }
                    HandleNavigationAction(e.Action switch
                    {
                        GamepadAction.NavUp => GamepadNavigationAction.Up,
                        GamepadAction.NavDown => GamepadNavigationAction.Down,
                        GamepadAction.NavLeft => GamepadNavigationAction.Left,
                        _ => GamepadNavigationAction.Right
                    });
                    return;

                case GamepadAction.Accept:
                    if (e.IsRepeat) return;
                    _reader.PulseHaptics();
                    if (_selectedNavbarButtonIndex.HasValue && _selectedNavbarButton != null)
                    {
                        InvokeSelectedNavbarButton();
                        return;
                    }
                    HandleNavigationAction(GamepadNavigationAction.Activate);
                    return;

                case GamepadAction.Back:
                    if (e.IsRepeat) return;
                    if (_selectedNavbarButtonIndex.HasValue)
                    {
                        ClearNavbarButtonSelection();
                        return;
                    }
                    HandleNavigationAction(GamepadNavigationAction.Back);
                    return;

                // X/Y have no global function (Library consumes X via raw input)
            }
        }

        /// <summary>
        /// Raw per-tick reading from the reader, fired after that tick's semantic
        /// events. While paused, forwards to RawGamepadInput subscribers with any
        /// intercepted buttons masked out until they are released.
        /// </summary>
        private void OnReaderReading(object? sender, GamepadReading reading)
        {
            bool skipThisTick = _skipRawForwardThisTick;
            _skipRawForwardThisTick = false;

            if (!_inputProcessingPaused)
            {
                _pausedInterceptMask = GamepadButtons.None;
                return;
            }

            if (_windowManager != null && !_windowManager.IsVisible) return;
            if (_isDialogOpen) return;
            if (skipThisTick) return;

            // Drop released buttons from the mask, then hide still-held intercepted
            // buttons from the page so it never sees them as fresh presses
            _pausedInterceptMask &= reading.Buttons;
            reading.Buttons &= ~_pausedInterceptMask;

            RawGamepadInput?.Invoke(this, reading);
        }

        private void InterceptPausedButton(GamepadButtons button)
        {
            _pausedInterceptMask |= button;
            _skipRawForwardThisTick = true;
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
                            else
                            {
                                // No expander to collapse - call OnGamepadBack on the control or page
                                navigableControl.OnGamepadBack();
                                System.Diagnostics.Debug.WriteLine($"🎮 B button: Called OnGamepadBack on {navigableControl.GetType().Name}");
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

                // Handle hardcoded navigation between dual-control elements
                // This ensures proper column-aligned navigation (Resolution↔FPS, RefreshRate↔HDR)
                HandleHardcodedNavigation(_currentFocusedElement, nextElement, direction);

                SetFocus(nextElement);
            }
        }

        /// <summary>
        /// Handles hardcoded navigation between controls that have multiple internal sub-controls.
        /// This ensures column-aligned navigation:
        /// - Resolution (left) ↔ FPS Limit (left)
        /// - Refresh Rate (right) ↔ HDR Toggle (right)
        /// - FPS Limit (left) ↔ Audio Mute button (left)
        /// - HDR Toggle (right) ↔ Audio Volume slider (right)
        /// </summary>
        private void HandleHardcodedNavigation(FrameworkElement? fromElement, FrameworkElement toElement, GamepadNavigationAction direction)
        {
            // Only handle UP/DOWN navigation for hardcoded paths
            if (direction != GamepadNavigationAction.Up && direction != GamepadNavigationAction.Down)
                return;

            // Navigation from ResolutionPicker to FpsLimiter (DOWN)
            if (fromElement is Controls.ResolutionPickerControl resolutionPicker &&
                toElement is Controls.FpsLimiterControl fpsLimiter &&
                direction == GamepadNavigationAction.Down)
            {
                // Resolution (0) -> FPS Limit (0), Refresh Rate (1) -> HDR (1)
                fpsLimiter.SetInitialFocusedControl(resolutionPicker.CurrentFocusedControl);
                System.Diagnostics.Debug.WriteLine($"🎮 Hardcoded nav: Resolution[{resolutionPicker.CurrentFocusedControl}] -> FpsLimiter[{resolutionPicker.CurrentFocusedControl}]");
            }
            // Navigation from FpsLimiter to ResolutionPicker (UP)
            else if (fromElement is Controls.FpsLimiterControl fpsLimiterUp &&
                     toElement is Controls.ResolutionPickerControl resolutionPickerUp &&
                     direction == GamepadNavigationAction.Up)
            {
                // FPS Limit (0) -> Resolution (0), HDR (1) -> Refresh Rate (1)
                resolutionPickerUp.SetInitialFocusedControl(fpsLimiterUp.CurrentFocusedControl);
                System.Diagnostics.Debug.WriteLine($"🎮 Hardcoded nav: FpsLimiter[{fpsLimiterUp.CurrentFocusedControl}] -> Resolution[{fpsLimiterUp.CurrentFocusedControl}]");
            }
            // Navigation from FpsLimiter to AudioControls (DOWN)
            else if (fromElement is Controls.FpsLimiterControl fpsLimiterDown &&
                     toElement is Controls.AudioControlsControl audioControls &&
                     direction == GamepadNavigationAction.Down)
            {
                // FPS Limit (0) -> Mute button (0), HDR (1) -> Volume slider (1)
                audioControls.SetInitialFocusedControl(fpsLimiterDown.CurrentFocusedControl);
                System.Diagnostics.Debug.WriteLine($"🎮 Hardcoded nav: FpsLimiter[{fpsLimiterDown.CurrentFocusedControl}] -> Audio[{fpsLimiterDown.CurrentFocusedControl}]");
            }
            // Navigation from AudioControls to FpsLimiter (UP)
            else if (fromElement is Controls.AudioControlsControl audioControlsUp &&
                     toElement is Controls.FpsLimiterControl fpsLimiterFromAudio &&
                     direction == GamepadNavigationAction.Up)
            {
                // Mute button (0) -> FPS Limit (0), Volume slider (1) -> HDR (1)
                fpsLimiterFromAudio.SetInitialFocusedControl(audioControlsUp.CurrentFocusedControl);
                System.Diagnostics.Debug.WriteLine($"🎮 Hardcoded nav: Audio[{audioControlsUp.CurrentFocusedControl}] -> FpsLimiter[{audioControlsUp.CurrentFocusedControl}]");
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

                if (_isGamepadActive)
                {
                    // When gamepad is active, clear any existing WinUI focus on inner controls
                    // to prevent double borders (Tab focus lingering + gamepad focus)
                    try
                    {
                        if (_layoutRoot != null && _currentFrame?.XamlRoot != null)
                        {
                            var winuiFocusedElement = FocusManager.GetFocusedElement(_currentFrame.XamlRoot) as UIElement;
                            if (winuiFocusedElement != null)
                            {
                                // Focus LayoutRoot using Pointer state to mimic clicking in open space
                                // LayoutRoot now has Background="Transparent" so it can accept focus
                                _layoutRoot.Focus(FocusState.Pointer);
                                System.Diagnostics.Debug.WriteLine($"🎮 Cleared WinUI focus from: {winuiFocusedElement.GetType().Name}");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"🎮 Failed to clear WinUI focus: {ex.Message}");
                    }
                }
                else
                {
                    // Only set WinUI focus when gamepad is NOT active
                    _currentFocusedElement.Focus(FocusState.Programmatic);
                }

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
                System.Diagnostics.Debug.WriteLine($"🎮 Cleared gamepad focus from: {_currentFocusedElement.GetType().Name}");
                _currentFocusedElement = null;
            }

            // Also clear any WinUI system focus (keyboard Tab focus) to prevent double borders
            try
            {
                if (_layoutRoot != null && _currentFrame?.XamlRoot != null)
                {
                    var focusedElement = FocusManager.GetFocusedElement(_currentFrame.XamlRoot) as UIElement;
                    if (focusedElement != null)
                    {
                        // Focus LayoutRoot using Pointer state to mimic clicking in open space
                        _layoutRoot.Focus(FocusState.Pointer);
                        System.Diagnostics.Debug.WriteLine($"🎮 Cleared WinUI system focus from: {focusedElement.GetType().Name}");
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"🎮 Failed to clear WinUI focus: {ex.Message}");
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

            if (e.Key == VirtualKey.F1)
            {
                // Special key for manual focus testing
                if (_currentFrame?.Content is FrameworkElement rootElement)
                {
                    InitializePageNavigation(rootElement);
                }
                e.Handled = true;
                return;
            }

            // Reader maps the key to a semantic action and dispatches it through
            // the same pipeline as polled input (with dedupe against it)
            if (_reader.ProcessKeyDown(e.Key))
            {
                e.Handled = true;
            }
        }

        // Suspend gamepad polling (for modal dialogs)
        public void SuspendPolling()
        {
            _reader.SuspendPolling();
            DeactivateGamepadMode();
            System.Diagnostics.Debug.WriteLine("🎮 Gamepad polling suspended (modal dialog)");
        }

        // Resume gamepad polling after modal dialog
        public void ResumePolling()
        {
            _reader.ResumePolling();
            System.Diagnostics.Debug.WriteLine("🎮 Gamepad polling resumed");
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

        // Cycle through navbar buttons with L2/R2 triggers
        private void CycleNavbarButtonSelection(int direction)
        {
            if (_navbarButtons.Count == 0) return;

            // Check if any navbar buttons are visible
            var visibleButtons = _navbarButtons
                .Select((button, index) => new { button, index })
                .Where(x => x.button.Visibility == Visibility.Visible)
                .ToList();

            if (visibleButtons.Count == 0)
            {
                ClearNavbarButtonSelection();
                return;
            }

            // If only one visible button, just select it and don't cycle
            if (visibleButtons.Count == 1)
            {
                int singleIndex = visibleButtons[0].index;
                if (_selectedNavbarButtonIndex == singleIndex)
                {
                    return; // Already selected, nothing to do
                }
                _selectedNavbarButtonIndex = singleIndex;
                SetNavbarButtonSelection(_navbarButtons[singleIndex]);
                return;
            }

            int newIndex;

            // If no button currently selected, always start from top-most visible button
            if (!_selectedNavbarButtonIndex.HasValue)
            {
                // Always start from the first visible button (top-most)
                newIndex = visibleButtons[0].index;
            }
            else
            {
                // Find current button in visible list
                int currentVisibleIndex = visibleButtons.FindIndex(x => x.index == _selectedNavbarButtonIndex.Value);

                if (currentVisibleIndex == -1)
                {
                    // Current button no longer visible, start from top-most visible button
                    newIndex = visibleButtons[0].index;
                }
                else
                {
                    // Move to next/previous visible button
                    int nextVisibleIndex = currentVisibleIndex + direction;

                    // Wrap around within visible buttons
                    if (nextVisibleIndex < 0)
                    {
                        nextVisibleIndex = visibleButtons.Count - 1;
                    }
                    else if (nextVisibleIndex >= visibleButtons.Count)
                    {
                        nextVisibleIndex = 0;
                    }

                    newIndex = visibleButtons[nextVisibleIndex].index;
                }
            }

            // Find the actual visible button at newIndex (may need to search)
            int searchAttempts = 0;
            int searchIndex = newIndex;

            while (searchAttempts < _navbarButtons.Count)
            {
                if (_navbarButtons[searchIndex].Visibility == Visibility.Visible)
                {
                    _selectedNavbarButtonIndex = searchIndex;
                    SetNavbarButtonSelection(_navbarButtons[searchIndex]);
                    return;
                }

                // Not visible, continue searching in direction
                searchIndex += direction;

                // Wrap around
                if (searchIndex < 0)
                {
                    searchIndex = _navbarButtons.Count - 1;
                }
                else if (searchIndex >= _navbarButtons.Count)
                {
                    searchIndex = 0;
                }

                searchAttempts++;
            }

            // Should never reach here since we checked for visible buttons above
            ClearNavbarButtonSelection();
        }

        // Set visual selection on navbar button
        private void SetNavbarButtonSelection(Button button)
        {
            try
            {
                // Clear previous selection
                if (_selectedNavbarButton != null && _selectedNavbarButton != button)
                {
                    _selectedNavbarButton.BorderBrush = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
                    _selectedNavbarButton.BorderThickness = new Thickness(0);
                }

                // Clear main app focus so DarkViolet borders disappear from page controls
                ClearFocus();

                // Set new selection
                _selectedNavbarButton = button;

                // Create border properties
                var darkVioletBrush = new SolidColorBrush(Microsoft.UI.Colors.DarkViolet);
                var borderThickness = new Thickness(3);

                // Set properties directly (gamepad timer runs on UI thread)
                button.BorderBrush = darkVioletBrush;
                button.BorderThickness = borderThickness;

                // DON'T call Focus() - it can cause WinUI to add its own focus visual
                // creating a "double border" effect. We only need our custom border.

                // Force visual update
                button.UpdateLayout();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"🎮 ERROR setting navbar button selection: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"🎮 Stack trace: {ex.StackTrace}");
            }
        }

        // Clear navbar button selection
        private void ClearNavbarButtonSelection()
        {
            try
            {
                if (_selectedNavbarButton != null)
                {
                    _selectedNavbarButton.BorderBrush = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
                    _selectedNavbarButton.BorderThickness = new Thickness(0);
                    _selectedNavbarButton = null;
                }
                _selectedNavbarButtonIndex = null;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"🎮 ERROR clearing navbar button selection: {ex.Message}");
            }
        }

        // Invoke the currently selected navbar button
        private void InvokeSelectedNavbarButton()
        {
            if (_selectedNavbarButton == null) return;

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

            _reader.ActionDispatched -= OnReaderAction;
            _reader.ReadingAvailable -= OnReaderReading;
            _reader.Dispose();

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
