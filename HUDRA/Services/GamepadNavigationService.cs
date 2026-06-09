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
        private readonly InputRouter _router = new();
        private readonly ShellScope _shellScope;
        private readonly PageScope _pageScope;
        private readonly LegacyRawScope _legacyRawScope;
        private FrameworkElement? _currentFocusedElement;
        private Frame? _currentFrame;
        private UIElement? _layoutRoot;
        private Microsoft.UI.Dispatching.DispatcherQueue? _dispatcherQueue;

        // Suppress auto focus on first gamepad activation after mouse/touch navigation
        private bool _suppressAutoFocusOnActivation = false;

        // While input processing is paused (Library page), buttons consumed by
        // other scopes (navbar invoke, dialogs, page nav) are masked out of the
        // forwarded raw readings until released, so the page's own edge detection
        // never sees a phantom "new" press one tick after we consumed it.
        private GamepadButtons _pausedInterceptMask = GamepadButtons.None;
        private bool _skipRawForwardThisTick = false;

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

        // Pages that use custom navigation (Library) push the legacy raw scope
        // and consume forwarded raw readings instead of semantic events
        public bool IsInputProcessingPaused => _router.Contains(_legacyRawScope);

        private bool IsDialogOpen => _router.HasScope<DialogScope>();

        // Delegate for forwarding raw gamepad input to custom pages
        public event EventHandler<GamepadReading>? RawGamepadInput;

        public void PauseInputProcessing()
        {
            // Clear any transient editing scopes; the page takes over from here
            _router.PopWhile(s => s is ValueEditScope or DropdownScope);
            _router.Push(_legacyRawScope);
            System.Diagnostics.Debug.WriteLine("🎮 GamepadNavigationService: Input processing PAUSED - will forward raw input");
        }

        public void ResumeInputProcessing()
        {
            _router.Pop(_legacyRawScope);
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

            // Permanent bottom of the input stack: shell chrome, then page navigation
            _shellScope = new ShellScope(this);
            _pageScope = new PageScope(this, _shellScope);
            _legacyRawScope = new LegacyRawScope(_shellScope);
            _router.Push(_shellScope);
            _router.Push(_pageScope);

            System.Diagnostics.Debug.WriteLine("🎮 GamepadNavigationService initialized successfully");
        }

        internal void RaisePageNavigationRequested(GamepadPageDirection direction)
        {
            PageNavigationRequested?.Invoke(this, new GamepadPageNavigationEventArgs(direction));
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
        /// Semantic input from the reader. Gate (window hidden / keyboard /
        /// activation), then dispatch through the scope stack. All mode-specific
        /// behavior (dialog, Library raw mode, slider edit, dropdown, navbar,
        /// page navigation) lives in the scopes.
        /// </summary>
        private void OnReaderAction(object? sender, GamepadEvent e)
        {
            // Ignore all input when window is hidden - prevents accidental
            // navigation/actions while the user plays a game
            if (_windowManager != null && !_windowManager.IsVisible) return;

            // Keyboard-mapped input only participates once gamepad mode is active
            if (e.Source == GamepadEventSource.Keyboard && !_isGamepadActive) return;

            // Safety net: if a ContentDialog was shown without the GamepadDialog
            // helper, detect it and give it a DialogScope so every modal behaves
            // the same regardless of how it was opened
            if (!e.IsRepeat)
            {
                EnsureDialogScopeForOpenPopups();
            }

            // Activate gamepad mode on first input
            if (!_isGamepadActive)
            {
                if (IsInputProcessingPaused)
                {
                    // Library owns its own visuals - no auto-activation (legacy behavior)
                }
                else if (IsDialogOpen)
                {
                    SetGamepadActive(true);
                }
                else
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
            }

            var consumer = _router.Dispatch(in e);

            // While the Library consumes raw readings, input another scope consumed
            // must be hidden from the raw stream (mask held buttons until release)
            if (consumer != null && consumer is not LegacyRawScope && IsInputProcessingPaused)
            {
                switch (e.Action)
                {
                    case GamepadAction.Accept: InterceptPausedButton(GamepadButtons.A); break;
                    case GamepadAction.Back: InterceptPausedButton(GamepadButtons.B); break;
                    case GamepadAction.LB: InterceptPausedButton(GamepadButtons.LeftShoulder); break;
                    case GamepadAction.RB: InterceptPausedButton(GamepadButtons.RightShoulder); break;
                    default: _skipRawForwardThisTick = true; break;
                }
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

            if (!IsInputProcessingPaused)
            {
                _pausedInterceptMask = GamepadButtons.None;
                return;
            }

            if (_windowManager != null && !_windowManager.IsVisible) return;
            if (IsDialogOpen) return;
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

        /// <summary>
        /// Detect a ContentDialog opened outside GamepadDialog.ShowAsync and
        /// push a DialogScope for it (auto-popped when the dialog closes).
        /// </summary>
        private void EnsureDialogScopeForOpenPopups()
        {
            if (IsDialogOpen) return;

            try
            {
                var xamlRoot = _currentFrame?.XamlRoot ?? (_layoutRoot as FrameworkElement)?.XamlRoot;
                if (xamlRoot == null) return;

                var popups = VisualTreeHelper.GetOpenPopupsForXamlRoot(xamlRoot);
                foreach (var popup in popups)
                {
                    if (popup.Child is ContentDialog dialog)
                    {
                        System.Diagnostics.Debug.WriteLine("🎮 Safety net: unmanaged ContentDialog detected - pushing DialogScope");
                        ClearFocus();
                        var scope = new DialogScope(dialog, _dispatcherQueue!);
                        _router.Push(scope);
                        dialog.Closed += (s, args) => _router.Pop(scope);
                        return;
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"🎮 Dialog safety net check failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Route a navigation action to the focused element (IGamepadNavigable
        /// dispatch, expander handling) or move focus between elements.
        /// Called by PageScope; slider/dropdown editing is handled by the
        /// ValueEditScope/DropdownScope pushed above the page.
        /// </summary>
        internal void HandleNavigationAction(GamepadNavigationAction action)
        {
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
                            // Sliders enter edit mode instead of activating directly
                            if (navigableControl.IsSlider)
                            {
                                _router.Push(new ValueEditScope(navigableControl));
                            }
                            else
                            {
                                navigableControl.OnGamepadActivate();

                                // If a ComboBox dropdown was opened, route input to it
                                if (navigableControl.HasComboBoxes)
                                {
                                    var comboBox = navigableControl.GetFocusedComboBox();
                                    if (comboBox != null && comboBox.IsDropDownOpen)
                                    {
                                        _router.Push(new DropdownScope(navigableControl));
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

            var nextElement = Configuration.HudraSettings.UseSpatialNavigation
                ? PickSpatialNeighbor(navigableElements, direction)
                : PickLinearNeighbor(navigableElements, direction);

            if (nextElement == null || nextElement == _currentFocusedElement) return;

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

        /// <summary>
        /// Geometry-based neighbor selection: best candidate strictly in the
        /// requested direction, by XYFocus-style scoring. Bounds are computed
        /// fresh on every press (pages are recreated per navigation and scroll
        /// offsets move elements), so there is no cache to go stale. Stops at
        /// edges - no wrap-around.
        /// </summary>
        private FrameworkElement? PickSpatialNeighbor(List<FrameworkElement> navigableElements, GamepadNavigationAction direction)
        {
            // No current focus (or it left the tree): start at the first element
            if (_currentFocusedElement == null || !TryGetScreenRect(_currentFocusedElement, out var fromRect))
            {
                return navigableElements.FirstOrDefault(el => TryGetScreenRect(el, out _));
            }

            var candidates = new List<FrameworkElement>();
            var rects = new List<ElementRect>();
            foreach (var element in navigableElements)
            {
                if (element == _currentFocusedElement) continue;
                if (TryGetScreenRect(element, out var rect))
                {
                    candidates.Add(element);
                    rects.Add(rect);
                }
            }
            if (candidates.Count == 0) return null;

            var semanticDirection = direction switch
            {
                GamepadNavigationAction.Up => GamepadAction.NavUp,
                GamepadNavigationAction.Down => GamepadAction.NavDown,
                GamepadNavigationAction.Left => GamepadAction.NavLeft,
                _ => GamepadAction.NavRight
            };

            int best = SpatialScorer.PickBest(fromRect, semanticDirection, rects);
            return best >= 0 ? candidates[best] : null;
        }

        /// <summary>Legacy NavigationOrder-based traversal (escape hatch via HudraSettings.UseSpatialNavigation).</summary>
        private FrameworkElement? PickLinearNeighbor(List<FrameworkElement> navigableElements, GamepadNavigationAction direction)
        {
            int currentIndex = _currentFocusedElement != null
                ? navigableElements.IndexOf(_currentFocusedElement)
                : -1;

            // If current element is not in the list, check if it's inside a NavigableExpander
            if (currentIndex == -1 && _currentFocusedElement != null)
            {
                var parent = FindNavigableParent(_currentFocusedElement);
                if (parent != null)
                {
                    currentIndex = navigableElements.IndexOf(parent);

                    // For UP navigation, return focus to the parent expander
                    if (direction == GamepadNavigationAction.Up || direction == GamepadNavigationAction.Left)
                    {
                        return parent;
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

            return nextIndex != currentIndex ? navigableElements[nextIndex] : null;
        }

        /// <summary>Screen-space bounds of an element; false if it is unloaded, collapsed, or detached.</summary>
        private static bool TryGetScreenRect(FrameworkElement element, out ElementRect rect)
        {
            rect = default;
            try
            {
                if (!element.IsLoaded || element.ActualWidth <= 0 || element.ActualHeight <= 0) return false;

                var origin = element.TransformToVisual(null).TransformPoint(new Windows.Foundation.Point(0, 0));
                rect = new ElementRect(origin.X, origin.Y, element.ActualWidth, element.ActualHeight);
                return true;
            }
            catch
            {
                // Element detached mid-press - skip it
                return false;
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

            // Editing scopes from the previous page no longer apply
            _router.PopWhile(s => s is ValueEditScope or DropdownScope);

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

                // Editing scopes don't survive leaving gamepad mode
                _router.PopWhile(s => s is ValueEditScope or DropdownScope);

                System.Diagnostics.Debug.WriteLine("🎮 Gamepad mode deactivated");
            }
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

        // Give a ContentDialog exclusive gamepad input (A = primary, B = cancel)
        public void SetDialogOpen(ContentDialog dialog)
        {
            // Clear focus from UI to prevent background controls from receiving input
            ClearFocus();
            _router.Push(new DialogScope(dialog, _dispatcherQueue!));
            System.Diagnostics.Debug.WriteLine("🎮 Dialog opened - UI navigation blocked, dialog has exclusive input");
        }

        // Release dialog input capture
        public void SetDialogClosed()
        {
            var dialogScope = _router.FindScope<DialogScope>();
            if (dialogScope != null)
            {
                _router.Pop(dialogScope);
            }
            System.Diagnostics.Debug.WriteLine("🎮 Dialog closed - normal activation logic resumed");
        }

        // Register navbar buttons for LT/RT cycling
        public void RegisterNavbarButtons(List<Button> buttons)
        {
            _shellScope.RegisterNavbarButtons(buttons);
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
