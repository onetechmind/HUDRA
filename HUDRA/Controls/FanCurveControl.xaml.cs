using HUDRA.Services;
using HUDRA.Services.FanControl;
using HUDRA.Interfaces;
using HUDRA.AttachedProperties;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Windows.Foundation;

namespace HUDRA.Controls
{
    public sealed partial class FanCurveControl : UserControl, IGamepadNavigable, INotifyPropertyChanged
    {
        public event EventHandler<FanCurveChangedEventArgs>? FanCurveChanged;
        public event PropertyChangedEventHandler? PropertyChanged;

        private FanControlService? _fanControlService;
        private bool _isUpdatingControls = false;
        private bool _isInitialized = false;
        private bool _isDragging = false;
        private int _dragPointIndex = -1;
        private string _activePresetName = string.Empty;
        private readonly List<Button> _presetButtons = new();
        private GamepadNavigationService? _gamepadNavigationService;
        private int _currentFocusedElement = 0; // 0=Toggle, 1-4=Preset Buttons, 5-9=Control Points
        private bool _isFocused = false;
        
        // Control point activation tracking
        private bool _isControlPointActivated = false;
        private int _activeControlPointIndex = -1;
        private FanCurvePoint _originalControlPointPosition;

        // Focus state property with change notification (matching TdpPickerControl pattern)
        public bool IsFocused
        {
            get => _isFocused;
            set
            {
                if (_isFocused != value)
                {
                    _isFocused = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(ToggleFocusBrush));
                    OnPropertyChanged(nameof(PresetButtonFocusBrush));
                    OnPropertyChanged(nameof(ControlPointFocusBrush));
                }
            }
        }

        // Focus brush fields for proper binding
        private SolidColorBrush _focusBorderBrush = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        private Thickness _focusBorderThickness = new Thickness(0);

        // ADD: Temperature monitoring fields
        private TemperatureData? _currentTemperature;
        private bool _temperatureControlEnabled = false;

        // Canvas dimensions and layout
        private const double CANVAS_HEIGHT = 140;
        private const double POINT_RADIUS = 10;
        private const double GRID_STROKE = 0.5;

        // Curve data
        private FanCurve _currentCurve;
        private readonly List<Ellipse> _controlPoints = new();
        private readonly List<Border> _controlPointFocusBorders = new();
        private Polyline? _curveVisualization;
        private readonly List<Line> _gridLines = new();
        private readonly List<TextBlock> _axisLabels = new();

        //touch handling
        private bool _isTouchDragging = false;
        private uint _touchPointerId = 0;

        // IGamepadNavigable implementation
        public bool CanNavigateUp => _currentFocusedElement >= 1; // Can move up from preset buttons/control points to previous level
        public bool CanNavigateDown => (_currentFocusedElement == 0 && PresetButtonsPanel?.Visibility == Visibility.Visible) || (_currentFocusedElement == 4 && _currentCurve.ActivePreset == "Custom" && CurvePanel?.Visibility == Visibility.Visible); // Can move down from toggle to buttons OR from Custom button to control points
        public bool CanNavigateLeft => (_currentFocusedElement > 1 && _currentFocusedElement <= 4) || (_currentFocusedElement > 5); // Can move left from preset buttons or between control points
        public bool CanNavigateRight => (_currentFocusedElement == 0) || (_currentFocusedElement >= 1 && _currentFocusedElement < 4) || (_currentFocusedElement >= 5 && _currentFocusedElement < 9); // Can move right from toggle, between preset buttons, or between control points
        public bool CanActivate => true;
        public FrameworkElement NavigationElement => this;
        
        // Slider interface implementations - FanCurve is not a slider control
        public bool IsSlider => false;
        public bool IsSliderActivated { get; set; } = false;
        public void AdjustSliderValue(int direction) 
        {
            if (_isControlPointActivated && _activeControlPointIndex >= 0)
            {
                AdjustControlPoint(direction);
            }
        }
        
        // ComboBox interface implementations - FanCurve has no ComboBoxes
        public bool HasComboBoxes => false;
        public bool IsComboBoxOpen { get; set; } = false;
        public ComboBox? GetFocusedComboBox() => null;
        public int ComboBoxOriginalIndex { get; set; } = -1;
        public bool IsNavigatingComboBox { get; set; } = false;
        public void ProcessCurrentSelection() { /* Not applicable - no ComboBoxes */ }

        public Brush FocusBorderBrush
        {
            get => _focusBorderBrush;
        }

        public Thickness FocusBorderThickness
        {
            get => _focusBorderThickness;
        }

        public Brush ToggleFocusBrush
        {
            get
            {
                // Ensure gamepad service is initialized before checking its state
                if (_gamepadNavigationService == null)
                {
                    InitializeGamepadNavigationService();
                }
                
                // Check all conditions after initialization
                bool shouldShowFocus = IsFocused && 
                                      _gamepadNavigationService != null && 
                                      _gamepadNavigationService.IsGamepadActive && 
                                      _currentFocusedElement == 0;
                
                return shouldShowFocus 
                    ? new SolidColorBrush(Microsoft.UI.Colors.DarkViolet)
                    : new SolidColorBrush(Microsoft.UI.Colors.Transparent);
            }
        }

        public Brush PresetButtonFocusBrush
        {
            get
            {
                if (IsFocused && _gamepadNavigationService?.IsGamepadActive == true && _currentFocusedElement >= 1 && _currentFocusedElement <= 4)
                {
                    return new SolidColorBrush(Microsoft.UI.Colors.DarkViolet);
                }
                return new SolidColorBrush(Microsoft.UI.Colors.Transparent);
            }
        }
        
        public Brush ControlPointFocusBrush
        {
            get
            {
                if (IsFocused && _gamepadNavigationService?.IsGamepadActive == true && _currentFocusedElement >= 5 && _currentFocusedElement <= 9)
                {
                    // Use DodgerBlue when activated for editing, DarkViolet for navigation
                    return new SolidColorBrush(_isControlPointActivated ? Microsoft.UI.Colors.DodgerBlue : Microsoft.UI.Colors.DarkViolet);
                }
                return new SolidColorBrush(Microsoft.UI.Colors.Transparent);
            }
        }

        public FanCurveControl()
        {
            this.InitializeComponent();
            this.DataContext = this;
            // Don't load curve here - wait for Initialize() when everything is ready
            InitializeDefaultCurve();
            InitializeGamepadNavigation();
        }

        private void InitializeDefaultCurve()
        {
            // Create a temporary default curve - will be replaced during Initialize()
            _currentCurve = new FanCurve
            {
                IsEnabled = false,
                Points = new FanCurvePoint[]
                {
                    new FanCurvePoint { Temperature = 30, FanSpeed = 20 },
                    new FanCurvePoint { Temperature = 40, FanSpeed = 30 },
                    new FanCurvePoint { Temperature = 55, FanSpeed = 50 },
                    new FanCurvePoint { Temperature = 70, FanSpeed = 75 },
                    new FanCurvePoint { Temperature = 85, FanSpeed = 100 }
                }
            };
        }

        public async void Initialize()
        {
            if (_isInitialized) return;

            System.Diagnostics.Debug.WriteLine("🎛️ FanCurveControl.Initialize() called");

            try
            {
                // Load saved curve from settings NOW (when everything is ready)
                _currentCurve = SettingsService.GetFanCurve();
                System.Diagnostics.Debug.WriteLine($"=== Fan Curve Loading ===");
                System.Diagnostics.Debug.WriteLine($"Loaded fan curve: {_currentCurve.Points.Length} points, enabled: {_currentCurve.IsEnabled}");

                // Debug: Print all curve points
                for (int i = 0; i < _currentCurve.Points.Length; i++)
                {
                    var point = _currentCurve.Points[i];
                    System.Diagnostics.Debug.WriteLine($"  Point {i}: {point.Temperature:F1}°C → {point.FanSpeed:F1}%");
                }

                // Use the global FanControlService instance from App instead of creating a new one
                _fanControlService = ((App)Application.Current).FanControlService;

                if (_fanControlService == null)
                {
                    System.Diagnostics.Debug.WriteLine("❌ Global FanControlService not available");
                    return;
                }

                // Initialize gamepad service early to ensure it's available for focus
                InitializeGamepadNavigationService();

                // ADD: Connect to global temperature monitoring
                ConnectToGlobalTemperatureMonitoring();

                // The service should already be initialized by App.xaml.cs, but check status
                bool serviceReady = _fanControlService.IsDeviceAvailable;

                if (serviceReady)
                {
                    System.Diagnostics.Debug.WriteLine($"✅ Using global FanControlService with device: {_fanControlService.DeviceInfo}");

                    // Set up canvas rendering
                    SetupCanvas();
                    RenderCurveCanvas();
                    InitializePresetButtons();
                    DetectActivePreset();

                    // Load saved curve state
                    System.Diagnostics.Debug.WriteLine($"=== Setting UI State ===");
                    System.Diagnostics.Debug.WriteLine($"Current curve enabled: {_currentCurve.IsEnabled}");

                    _isUpdatingControls = true;
                    FanCurveToggle.IsOn = _currentCurve.IsEnabled; // CHANGED: Use FanCurveToggle instead of CurveEnabledToggle

                    // ADD: Update temperature monitoring state
                    UpdateTemperatureMonitoringState();

                    CurvePanel.Visibility = _currentCurve.IsEnabled ? Visibility.Visible : Visibility.Collapsed;
                    TemperatureStatusPanel.Visibility = _currentCurve.IsEnabled ? Visibility.Visible : Visibility.Collapsed;
                    PresetButtonsPanel.Visibility = _currentCurve.IsEnabled ? Visibility.Visible : Visibility.Collapsed;
                    _isUpdatingControls = false;

                    // Apply saved curve if it was enabled (with delay to ensure service is ready)
                    if (_currentCurve.IsEnabled)
                    {

                        // Use dispatcher to ensure fan service is fully ready
                        DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Normal, async () =>
                        {
                            // Small delay to ensure service is completely initialized
                            await Task.Delay(500);

                            if (ApplyFanCurveOnStartup())
                            {

                                // ADD: Enable temperature-based control
                                EnableTemperatureBasedControl();
                            }
                            else
                            {

                                // Disable the curve if it failed to apply
                                _isUpdatingControls = true;
                                FanCurveToggle.IsOn = false;
                                _currentCurve.IsEnabled = false;
                                SettingsService.SetFanCurveEnabled(false);
                                CurvePanel.Visibility = Visibility.Collapsed;
                                TemperatureStatusPanel.Visibility = Visibility.Collapsed;
                                _isUpdatingControls = false;
                            }
                        });
                    }

                }
                else
                {
                    // Try to reinitialize the service
                    DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Normal, async () =>
                    {
                        await Task.Delay(1000); // Wait a bit longer
                        
                        var reinitResult = await _fanControlService.InitializeAsync();
                        
                        if (reinitResult.Success)
                        {
                            // Enable the toggle and set up UI
                            _isUpdatingControls = true;
                            FanCurveToggle.IsEnabled = true;
                            FanCurveToggle.IsOn = _currentCurve.IsEnabled;
                            
                            SetupCanvas();
                            RenderCurveCanvas();
                            InitializePresetButtons();
                            DetectActivePreset();
                            UpdateTemperatureMonitoringState();
                            
                            CurvePanel.Visibility = _currentCurve.IsEnabled ? Visibility.Visible : Visibility.Collapsed;
                            TemperatureStatusPanel.Visibility = _currentCurve.IsEnabled ? Visibility.Visible : Visibility.Collapsed;
                            PresetButtonsPanel.Visibility = _currentCurve.IsEnabled ? Visibility.Visible : Visibility.Collapsed;
                            _isUpdatingControls = false;
                            
                            UpdateStatusText();
                            FanCurveChanged?.Invoke(this, new FanCurveChangedEventArgs(
                                _currentCurve, $"Fan control ready: {_fanControlService.DeviceInfo}"));
                        }
                        else
                        {
                            FanCurveToggle.IsEnabled = false;
                        }
                    });
                }

                _isInitialized = true;
                
                // Update status text after initialization
                UpdateStatusText();
                
                FanCurveChanged?.Invoke(this, new FanCurveChangedEventArgs(
                    _currentCurve, $"Fan control ready: {_fanControlService.DeviceInfo}"));
            }
            catch (Exception ex)
            {

            }

        }

        // ADD: Temperature monitoring methods

        private void ConnectToGlobalTemperatureMonitoring()
        {
            try
            {
                System.Diagnostics.Debug.WriteLine("🔌 Attempting to connect to global temperature monitoring...");

                if (Application.Current is App app && app.TemperatureMonitor != null)
                {
                    app.TemperatureMonitor.TemperatureChanged += OnTemperatureChanged;

                    // Show current temperature if available
                    if (app.TemperatureMonitor.CurrentTemperature.MaxTemperature > 0)
                    {
                        UpdateTemperatureDisplay(app.TemperatureMonitor.CurrentTemperature);
                    }

                    System.Diagnostics.Debug.WriteLine("🌡️ Connected to global temperature monitoring");
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine("⚠️ Global temperature monitoring not available");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error connecting to temperature monitoring: {ex.Message}");
            }
        }

        private void DisconnectFromGlobalTemperatureMonitoring()
        {
            try
            {
                if (Application.Current is App app && app.TemperatureMonitor != null)
                {
                    app.TemperatureMonitor.TemperatureChanged -= OnTemperatureChanged;
                    System.Diagnostics.Debug.WriteLine("🔌 Disconnected from global temperature monitoring");
                }

                _temperatureControlEnabled = false;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error disconnecting from temperature monitoring: {ex.Message}");
            }
        }

        private void OnTemperatureChanged(object? sender, TemperatureChangedEventArgs e)
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                UpdateTemperatureDisplay(e.TemperatureData);

                // Apply temperature-based fan control if enabled
                if (_temperatureControlEnabled && _currentCurve.IsEnabled)
                {
                    ApplyTemperatureBasedFanControl(e.TemperatureData.MaxTemperature);
                }
            });
        }

        private void UpdateTemperatureDisplay(TemperatureData temperatureData)
        {
            _currentTemperature = temperatureData;

            if (!_currentCurve.IsEnabled) return;

            try
            {
                var currentTemp = temperatureData.MaxTemperature;

                if (currentTemp > 0)
                {
                    var fanSpeed = InterpolateFanSpeed(currentTemp);

                    // Update status text only (no visual indicators)
                    TempStatusText.Text = $"Temp: {currentTemp:F1}°C";
                    FanSpeedStatusText.Text = $"Fan Speed: {fanSpeed:F0}%";
                }
                else
                {
                    TempStatusText.Text = "🌡️ Waiting for temperature data...";
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error updating temperature display: {ex.Message}");
                TempStatusText.Text = "🌡️ Temperature sensor error";
            }
        }

        private void ApplyTemperatureBasedFanControl(double temperature)
        {
            if (_fanControlService == null || !_currentCurve.IsEnabled) return;

            try
            {
                // Use your existing interpolation method
                var targetFanSpeed = InterpolateFanSpeed(temperature);

                // Apply the calculated fan speed using your existing method
                var result = _fanControlService.SetFanSpeed(targetFanSpeed);

                if (result.Success)
                {

                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"Failed to apply temperature-based fan control: {result.Message}");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in temperature-based fan control: {ex.Message}");
            }
        }

        private void EnableTemperatureBasedControl()
        {
            _temperatureControlEnabled = true;

            // Apply current temperature if available
            if (_currentTemperature != null && _currentTemperature.MaxTemperature > 0)
            {
                ApplyTemperatureBasedFanControl(_currentTemperature.MaxTemperature);
            }

            System.Diagnostics.Debug.WriteLine("🌡️ Temperature-based fan control enabled");
        }

        private void DisableTemperatureBasedControl()
        {
            _temperatureControlEnabled = false;
            System.Diagnostics.Debug.WriteLine("🚫 Temperature-based fan control disabled");
        }

        private void UpdateTemperatureMonitoringState()
        {
            // Update UI visibility based on curve enabled state
            var showTempStatus = _currentCurve.IsEnabled;

            if (TemperatureStatusPanel != null)
            {
                TemperatureStatusPanel.Visibility = showTempStatus ? Visibility.Visible : Visibility.Collapsed;
            }

            // No visual indicators to manage anymore
        }

        private void UpdateStatusText()
        {
            try
            {
                if (FanCurveStatusText != null)
                {
                    if (_currentCurve.IsEnabled)
                    {
                        FanCurveStatusText.Text = "Using HUDRA software fan curve.";
                    }
                    else
                    {
                        FanCurveStatusText.Text = "Using built-in hardware fan curve.";
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"ERROR updating fan curve status text: {ex.Message}");
                if (FanCurveStatusText != null)
                {
                    FanCurveStatusText.Text = "Fan curve status unknown.";
                }
            }
        }

        private void SetupCanvas()
        {
            // Essential for touch hit testing
            FanCurveCanvas.Background = new SolidColorBrush(Colors.Transparent);
            FanCurveCanvas.IsHitTestVisible = true;

            // CRITICAL: Prevent parent scroll viewers from stealing touch events
            FanCurveCanvas.ManipulationMode = Microsoft.UI.Xaml.Input.ManipulationModes.None;

        }

        private void InitializePresetButtons()
        {
            _presetButtons.Clear();
            _presetButtons.AddRange(new[] { StealthPresetButton, CruisePresetButton, WarpPresetButton });

            // Load active preset from saved curve
            _activePresetName = _currentCurve.ActivePreset ?? string.Empty;
            UpdatePresetButtonStates();
        }

        private void RenderCurveCanvas()
        {
            FanCurveCanvas.Children.Clear();
            _controlPoints.Clear();
            _controlPointFocusBorders.Clear();
            _gridLines.Clear();
            _axisLabels.Clear();

            // Render grid lines and labels
            RenderGrid();

            // Render curve line
            RenderCurveLine();

            // Render control points (on top)
            RenderControlPoints();

        }

        private void RenderGrid()
        {
            var gridBrush = new SolidColorBrush(ColorHelper.FromArgb(40, 255, 255, 255));
            var labelBrush = new SolidColorBrush(ColorHelper.FromArgb(100, 255, 255, 255));

            // Vertical grid lines (temperature)
            for (int temp = 0; temp <= 90; temp += 10)
            {
                double x = TemperatureToX(temp);

                var gridLine = new Line
                {
                    X1 = x,
                    Y1 = 0,
                    X2 = x,
                    Y2 = 140, // Use fixed canvas height
                    Stroke = gridBrush,
                    StrokeThickness = GRID_STROKE
                };

                FanCurveCanvas.Children.Add(gridLine);
                _gridLines.Add(gridLine);

                // Temperature labels (every 10°)
                if (temp % 10 == 0)
                {
                    var label = new TextBlock
                    {
                        Text = temp.ToString(),
                        FontSize = 12,
                        FontFamily = new FontFamily("Cascadia Code"),
                        Foreground = labelBrush
                    };

                    Canvas.SetLeft(label, x - 8);
                    Canvas.SetTop(label, 140 + 2); // Use fixed canvas height
                    FanCurveCanvas.Children.Add(label);
                    _axisLabels.Add(label);
                }
            }

            // Horizontal grid lines (fan speed)
            for (int speed = 0; speed <= 100; speed += 10)
            {
                double y = FanSpeedToY(speed);

                var gridLine = new Line
                {
                    X1 = 0,
                    Y1 = y,
                    X2 = 190, // Use fixed canvas width
                    Y2 = y,
                    Stroke = gridBrush,
                    StrokeThickness = GRID_STROKE
                };

                FanCurveCanvas.Children.Add(gridLine);
                _gridLines.Add(gridLine);

                // Fan speed labels (every 20%)
                if (speed % 20 == 0)
                {
                    var label = new TextBlock
                    {
                        Text = speed.ToString(),
                        FontSize = 12,
                        FontFamily = new FontFamily("Cascadia Code"),
                        Foreground = labelBrush
                    };

                    Canvas.SetLeft(label, -18);
                    Canvas.SetTop(label, y - 6);
                    FanCurveCanvas.Children.Add(label);
                    _axisLabels.Add(label);
                }
            }
        }

        private void RenderCurveLine()
        {
            _curveVisualization = new Polyline
            {
                Stroke = new SolidColorBrush(Colors.DarkViolet),
                StrokeThickness = 2,
                Points = new PointCollection()
            };

            // Generate smooth curve points
            for (int temp = 0; temp <= 90; temp += 2)
            {
                double fanSpeed = InterpolateFanSpeed(temp);
                double x = TemperatureToX(temp);
                double y = FanSpeedToY(fanSpeed);
                _curveVisualization.Points.Add(new Point(x, y));
            }

            FanCurveCanvas.Children.Add(_curveVisualization);
        }

        private void RenderControlPoints()
        {
            var pointBrush = new SolidColorBrush(Colors.DarkViolet);
            var pointBorder = new SolidColorBrush(Colors.White);

            for (int i = 0; i < _currentCurve.Points.Length; i++)
            {
                var point = _currentCurve.Points[i];

                var ellipse = new Ellipse
                {
                    Width = POINT_RADIUS * 2,
                    Height = POINT_RADIUS * 2,
                    Fill = pointBrush,
                    Stroke = pointBorder,
                    StrokeThickness = 2,
                    Tag = i // Store point index
                };

                // Create a focus border for gamepad navigation
                var focusBorder = new Border
                {
                    Width = POINT_RADIUS * 2 + 8, // +4px on each side for thicker border
                    Height = POINT_RADIUS * 2 + 8,
                    BorderThickness = new Thickness(4), // Double thickness for better visibility
                    CornerRadius = new CornerRadius((POINT_RADIUS + 4)),
                    BorderBrush = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
                    Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
                    IsHitTestVisible = false // Don't interfere with mouse interaction
                };

                double x = TemperatureToX(point.Temperature) - POINT_RADIUS;
                double borderX = x - 4; // Offset for thicker border
                double y = FanSpeedToY(point.FanSpeed) - POINT_RADIUS;
                double borderY = y - 4;

                Canvas.SetLeft(ellipse, x);
                Canvas.SetTop(ellipse, y);
                Canvas.SetLeft(focusBorder, borderX);
                Canvas.SetTop(focusBorder, borderY);

                FanCurveCanvas.Children.Add(ellipse);
                FanCurveCanvas.Children.Add(focusBorder);
                _controlPoints.Add(ellipse);
                _controlPointFocusBorders.Add(focusBorder);
            }
        }



        // Keep your existing curve interpolation method:
        private double InterpolateFanSpeed(double temperature)
        {
            var points = _currentCurve.Points.OrderBy(p => p.Temperature).ToArray();

            // Handle edge cases
            if (temperature <= points[0].Temperature)
                return points[0].FanSpeed;
            if (temperature >= points[^1].Temperature)
                return points[^1].FanSpeed;

            // Find surrounding points and interpolate
            for (int i = 0; i < points.Length - 1; i++)
            {
                if (temperature >= points[i].Temperature && temperature <= points[i + 1].Temperature)
                {
                    double t = (temperature - points[i].Temperature) /
                              (points[i + 1].Temperature - points[i].Temperature);
                    return points[i].FanSpeed + t * (points[i + 1].FanSpeed - points[i].FanSpeed);
                }
            }

            return 50; // Fallback
        }

        private void Canvas_PointerPressed(object sender, PointerRoutedEventArgs e)
        {
            if (!_currentCurve.IsEnabled) return;

            if (_currentCurve.ActivePreset != "Custom")
            {
                return;
            }

            var position = e.GetCurrentPoint(FanCurveCanvas).Position;
            var pointer = e.Pointer;

            // Check if we clicked/touched on a control point
            for (int i = 0; i < _controlPoints.Count; i++)
            {
                var point = _controlPoints[i];
                var pointCenter = new Point(
                    Canvas.GetLeft(point) + POINT_RADIUS,
                    Canvas.GetTop(point) + POINT_RADIUS);

                double distance = Math.Sqrt(
                    Math.Pow(position.X - pointCenter.X, 2) +
                    Math.Pow(position.Y - pointCenter.Y, 2));

                // Larger hit tolerance for touch
                var hitTolerance = pointer.PointerDeviceType == Microsoft.UI.Input.PointerDeviceType.Touch ?
                    POINT_RADIUS + 15 : POINT_RADIUS + 8;

                if (distance <= hitTolerance)
                {
                    // CRITICAL: Prevent scrolling by handling the event FIRST
                    e.Handled = true;

                    _isDragging = true;
                    _dragPointIndex = i;

                    // Track touch-specific state
                    if (pointer.PointerDeviceType == Microsoft.UI.Input.PointerDeviceType.Touch)
                    {
                        _isTouchDragging = true;
                        _touchPointerId = pointer.PointerId;
                    }

                    // Capture the pointer to prevent parent controls from handling it
                    FanCurveCanvas.CapturePointer(pointer);

                    // Show tooltip at the point position
                    var currentPoint = _currentCurve.Points[i];
                    var pointPosition = new Point(
                        TemperatureToX(currentPoint.Temperature),
                        FanSpeedToY(currentPoint.FanSpeed)
                    );

                    UpdateTooltip(currentPoint.Temperature, currentPoint.FanSpeed, pointPosition);
                    ShowTooltip();

                    return;
                }
            }

        }

        private void CustomPresetButton_Click(object sender, RoutedEventArgs e)
        {
            // Load the saved custom curve points
            var customPoints = SettingsService.GetCustomFanCurve();
            _currentCurve.Points = customPoints;
            _currentCurve.ActivePreset = "Custom";

            System.Diagnostics.Debug.WriteLine($"Loaded custom curve with {customPoints.Length} points");

            // Re-render the canvas with the custom curve
            RenderCurveCanvas();

            UpdatePresetButtonStates();
            SettingsService.SetFanCurve(_currentCurve);

            // Apply the custom curve if enabled
            if (_currentCurve.IsEnabled && _fanControlService != null)
            {
                if (_temperatureControlEnabled && _currentTemperature != null)
                {
                    ApplyTemperatureBasedFanControl(_currentTemperature.MaxTemperature);
                }
                else
                {
                    ApplyFanCurve();
                }
            }

        }

        private void Canvas_PointerMoved(object sender, PointerRoutedEventArgs e)
        {
            if (!_isDragging || _dragPointIndex < 0) return;

            // CRITICAL: Always handle the event during dragging to prevent scrolling
            e.Handled = true;

            var pointer = e.Pointer;

            // For touch, make sure we're tracking the same touch point
            if (_isTouchDragging && pointer.PointerId != _touchPointerId)
            {
                return;
            }

            var position = e.GetCurrentPoint(FanCurveCanvas).Position;

            // Convert to temperature and fan speed
            double newTemp = XToTemperature(position.X);
            double newSpeed = YToFanSpeed(position.Y);

            // Constrain temperature to valid range for this point (2-degree minimum gap)
            if (_dragPointIndex > 0)
                newTemp = Math.Max(newTemp, _currentCurve.Points[_dragPointIndex - 1].Temperature + 2);
            if (_dragPointIndex < _currentCurve.Points.Length - 1)
                newTemp = Math.Min(newTemp, _currentCurve.Points[_dragPointIndex + 1].Temperature - 2);

            // Update the curve point
            _currentCurve.Points[_dragPointIndex].Temperature = newTemp;
            _currentCurve.Points[_dragPointIndex].FanSpeed = newSpeed;

            // Calculate the NEW point position (where the point actually is)
            var newPointPosition = new Point(
                TemperatureToX(newTemp),
                FanSpeedToY(newSpeed)
            );

            // Update tooltip to follow the point
            UpdateTooltip(newTemp, newSpeed, newPointPosition);

            // Only update the dragged point position, not full re-render
            UpdateDraggedPointPosition(_dragPointIndex, newPointPosition);
            UpdateCurveLineOnly();
        }

        // 4. ENHANCED Canvas_PointerReleased (no changes needed, but for completeness)
        private void Canvas_PointerReleased(object sender, PointerRoutedEventArgs e)
        {
            var pointer = e.Pointer;

            if (_isTouchDragging && pointer.PointerId != _touchPointerId)
            {
                return;
            }

            if (_isDragging)
            {
                e.Handled = true;

                _isDragging = false;
                _dragPointIndex = -1;
                _isTouchDragging = false;
                _touchPointerId = 0;

                FanCurveCanvas.ReleasePointerCapture(pointer);
                HideTooltip();
                RenderCurveCanvas();

                if (_currentCurve.ActivePreset == "Custom")
                {
                    SettingsService.SetCustomFanCurve(_currentCurve.Points);
                }

                SettingsService.SetFanCurve(_currentCurve);
                FanCurveChanged?.Invoke(this, new FanCurveChangedEventArgs(_currentCurve, "Fan curve updated"));
                if (_currentCurve.IsEnabled && _fanControlService != null)
                {
                    if (_temperatureControlEnabled && _currentTemperature != null)
                    {
                        ApplyTemperatureBasedFanControl(_currentTemperature.MaxTemperature);
                    }
                    else
                    {
                        ApplyFanCurve();
                    }
                }

            }
        }
        private void UpdateDraggedPointPosition(int pointIndex, Point newPosition)
        {
            if (pointIndex >= 0 && pointIndex < _controlPoints.Count)
            {
                var ellipse = _controlPoints[pointIndex];
                Canvas.SetLeft(ellipse, newPosition.X - POINT_RADIUS);
                Canvas.SetTop(ellipse, newPosition.Y - POINT_RADIUS);
            }
        }

        private void UpdateCurveLineOnly()
        {
            if (_curveVisualization == null) return;

            _curveVisualization.Points.Clear();

            // Generate smooth curve points
            for (int temp = 0; temp <= 90; temp += 2)
            {
                double fanSpeed = InterpolateFanSpeed(temp);
                double x = TemperatureToX(temp);
                double y = FanSpeedToY(fanSpeed);
                _curveVisualization.Points.Add(new Point(x, y));
            }
        }

        // UPDATED: Toggle event handler with temperature monitoring integration
        private void FanCurveToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (_isUpdatingControls) return;

            try
            {
                bool isEnabled = FanCurveToggle.IsOn;
                _currentCurve.IsEnabled = isEnabled;

                SettingsService.SetFanCurveEnabled(isEnabled);

                // Show/hide all curve-related panels
                CurvePanel.Visibility = isEnabled ? Visibility.Visible : Visibility.Collapsed;
                TemperatureStatusPanel.Visibility = isEnabled ? Visibility.Visible : Visibility.Collapsed;
                PresetButtonsPanel.Visibility = isEnabled ? Visibility.Visible : Visibility.Collapsed; // NEW

                if (isEnabled)
                {
                    if (_fanControlService != null)
                    {
                        ApplyFanCurve();
                        EnableTemperatureBasedControl();
                    }
                }
                else
                {
                    DisableTemperatureBasedControl();
                    if (_fanControlService != null)
                    {
                        _fanControlService.SetAutoMode();
                    }
                }

                UpdateTemperatureMonitoringState();
                UpdateStatusText();
                FanCurveChanged?.Invoke(this, new FanCurveChangedEventArgs(_currentCurve, $"Fan curve {(isEnabled ? "enabled" : "disabled")}"));
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error toggling fan curve: {ex.Message}");
            }
        }

        // Keep your existing methods unchanged:
        private bool ApplyFanCurveOnStartup()
        {
            // More robust curve application for startup scenarios
            if (_fanControlService == null)
            {
                System.Diagnostics.Debug.WriteLine("Cannot apply fan curve: service not available");
                return false;
            }

            try
            {
                System.Diagnostics.Debug.WriteLine("Applying fan curve on startup...");

                // First, explicitly set to software mode
                var modeResult = _fanControlService.SetFanMode(FanControlMode.Software);
                if (!modeResult.Success)
                {
                    System.Diagnostics.Debug.WriteLine($"Failed to set software mode: {modeResult.Message}");
                    return false;
                }

                // UPDATED: If we have current temperature, use it; otherwise use average
                double fanSpeed;
                if (_currentTemperature != null && _currentTemperature.MaxTemperature > 0)
                {
                    fanSpeed = InterpolateFanSpeed(_currentTemperature.MaxTemperature);
                    System.Diagnostics.Debug.WriteLine($"Using temperature-based fan speed: {fanSpeed:F1}% for {_currentTemperature.MaxTemperature:F1}°C");
                }
                else
                {
                    fanSpeed = _currentCurve.Points.Average(p => p.FanSpeed);
                    System.Diagnostics.Debug.WriteLine($"Using average fan speed: {fanSpeed:F1}%");
                }

                // Apply the fan speed
                var speedResult = _fanControlService.SetFanSpeed(fanSpeed);
                if (speedResult.Success)
                {
                    System.Diagnostics.Debug.WriteLine($"Successfully applied fan curve: {fanSpeed:F0}% speed");
                    return true;
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"Failed to set fan speed: {speedResult.Message}");
                    return false;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Exception applying fan curve on startup: {ex.Message}");
                return false;
            }
        }

        private void ApplyFanCurve()
        {
            // UPDATED: Use temperature if available, otherwise use average
            if (_fanControlService == null) return;

            try
            {
                double fanSpeed;
                if (_temperatureControlEnabled && _currentTemperature != null && _currentTemperature.MaxTemperature > 0)
                {
                    fanSpeed = InterpolateFanSpeed(_currentTemperature.MaxTemperature);
                }
                else
                {
                    // Fallback to average speed
                    fanSpeed = _currentCurve.Points.Average(p => p.FanSpeed);
                }

                _fanControlService.SetFanSpeed(fanSpeed);
            }
            catch (Exception ex)
            {
            }
        }

        private double TemperatureToX(double temperature)
        {
            return (temperature / 90.0) * 190;
        }

        private double FanSpeedToY(double fanSpeed)
        {
            return 140 - (fanSpeed / 100.0) * 140;
        }

        private double XToTemperature(double x)
        {
            return Math.Clamp((x / 190) * 90.0, 0, 90);
        }

        private double YToFanSpeed(double y)
        {
            return Math.Clamp(((140 - y) / 140) * 100.0, 0, 100);
        }

        private void StealthPreset_Click(object sender, RoutedEventArgs e)
        {
            ApplyPreset(FanCurvePreset.Stealth);
        }

        private void CruisePreset_Click(object sender, RoutedEventArgs e)
        {
            ApplyPreset(FanCurvePreset.Cruise);
        }

        private void WarpPreset_Click(object sender, RoutedEventArgs e)
        {
            ApplyPreset(FanCurvePreset.Warp);
        }

        private void ApplyPreset(FanCurvePreset preset)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine($"Applying preset: {preset.Name}");

                // Update curve points
                _currentCurve.Points = preset.Points.Select(p => new FanCurvePoint
                {
                    Temperature = p.Temperature,
                    FanSpeed = p.FanSpeed
                }).ToArray();

                // Track active preset
                _activePresetName = preset.Name;
                _currentCurve.ActivePreset = preset.Name;

                // Update UI
                UpdatePresetButtonStates();
                RenderCurveCanvas();

                // Save to settings
                SettingsService.SetFanCurve(_currentCurve);

                // Apply immediately if enabled
                if (_currentCurve.IsEnabled && _fanControlService != null)
                {
                    if (_temperatureControlEnabled && _currentTemperature != null)
                    {
                        ApplyTemperatureBasedFanControl(_currentTemperature.MaxTemperature);
                    }
                    else
                    {
                        ApplyFanCurve();
                    }
                }

                // Notify of change
                FanCurveChanged?.Invoke(this, new FanCurveChangedEventArgs(
                    _currentCurve, $"Applied {preset.Name} preset"));

                System.Diagnostics.Debug.WriteLine($"Successfully applied {preset.Name} preset");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error applying preset {preset.Name}: {ex.Message}");
            }
        }

        private void UpdateButtonState(Button button, bool isActive)
        {

            if (button == null)
            {
                return;
            }

            if (isActive)
            {
                // Active button style
                button.Background = new SolidColorBrush(Colors.DarkViolet);
                button.Foreground = new SolidColorBrush(Colors.White);
            }
            else
            {
                // Inactive button style  
                button.Background = new SolidColorBrush(ColorHelper.FromArgb(60, 128, 128, 128)); // Semi-transparent gray
                button.Foreground = new SolidColorBrush(ColorHelper.FromArgb(180, 255, 255, 255)); // Muted white
            }
        }

        // NEW: Update button visual states
        private void UpdatePresetButtonStates()
        {
            // Update all 4 buttons using the helper method
            UpdateButtonState(StealthPresetButton, _currentCurve.ActivePreset == "Stealth");
            UpdateButtonState(CruisePresetButton, _currentCurve.ActivePreset == "Cruise");
            UpdateButtonState(WarpPresetButton, _currentCurve.ActivePreset == "Warp");
            UpdateButtonState(CustomPresetButton, _currentCurve.ActivePreset == "Custom");
        }

        private void DetectActivePreset()
        {
            if (string.IsNullOrEmpty(_currentCurve.ActivePreset))
            {
                // Try to detect if current curve matches a preset
                foreach (var preset in FanCurvePreset.AllPresets)
                {
                    if (CurveMatchesPreset(_currentCurve, preset))
                    {
                        _activePresetName = preset.Name;
                        _currentCurve.ActivePreset = preset.Name;
                        break;
                    }
                }
            }
            else
            {
                _activePresetName = _currentCurve.ActivePreset;
            }
        }

        private bool CurveMatchesPreset(FanCurve curve, FanCurvePreset preset)
        {
            if (curve.Points.Length != preset.Points.Length) return false;

            for (int i = 0; i < curve.Points.Length; i++)
            {
                var curvePoint = curve.Points[i];
                var presetPoint = preset.Points[i];

                // Allow small tolerance for floating point comparison
                if (Math.Abs(curvePoint.Temperature - presetPoint.Temperature) > 0.5 ||
                    Math.Abs(curvePoint.FanSpeed - presetPoint.FanSpeed) > 0.5)
                {
                    return false;
                }
            }

            return true;
        }


        private void UpdateTooltip(double temperature, double fanSpeed, Point pointPosition)
        {
            if (DragTooltip == null || TooltipText == null || CanvasContainer == null)
                return;

            // Update tooltip text
            TooltipText.Text = $"{temperature:F0}°C → {fanSpeed:F0}%";

            // Get the canvas container's position within the main grid
            var canvasContainerTransform = CanvasContainer.TransformToVisual(MainChartGrid);
            var canvasContainerPosition = canvasContainerTransform.TransformPoint(new Point(0, 0));

            // Calculate tooltip position relative to the main grid
            var gridPointX = canvasContainerPosition.X + pointPosition.X;
            var gridPointY = canvasContainerPosition.Y + pointPosition.Y;

            // Tooltip dimensions
            var tooltipWidth = 80.0;
            var tooltipHeight = 25.0;

            // FINGER-FRIENDLY: Much higher offset to clear finger area
            // Different offsets for touch vs mouse
            var verticalOffset = _isTouchDragging ? -45 : -15; // 45px above for touch, 15px for mouse
            var horizontalOffset = _isTouchDragging ? 0 : 0;    // Could offset horizontally too if needed

            // Position tooltip well above the point for touch, closer for mouse
            double tooltipX = gridPointX - (tooltipWidth / 2) + horizontalOffset; // Center horizontally
            double tooltipY = gridPointY + verticalOffset - tooltipHeight;        // Way above for touch

            // Get main grid bounds for edge detection
            var gridWidth = MainChartGrid.ActualWidth > 0 ? MainChartGrid.ActualWidth : 280;
            var gridHeight = MainChartGrid.ActualHeight > 0 ? MainChartGrid.ActualHeight : 200;

            // Adjust horizontal position if tooltip would go off-screen
            if (tooltipX < 5)
            {
                tooltipX = 5;
            }
            else if (tooltipX + tooltipWidth > gridWidth - 5)
            {
                tooltipX = gridWidth - tooltipWidth - 5;
            }

            // For touch: if tooltip would go off the top, try positioning to the side instead
            if (tooltipY < 5)
            {
                if (_isTouchDragging)
                {
                    // For touch: try positioning to the right side of the finger
                    tooltipX = gridPointX + 35; // To the right of the finger
                    tooltipY = gridPointY - (tooltipHeight / 2); // Vertically centered on point

                    // If that goes off the right edge, try the left side
                    if (tooltipX + tooltipWidth > gridWidth - 5)
                    {
                        tooltipX = gridPointX - tooltipWidth - 35; // To the left of the finger
                    }

                    // If still problems, fall back to below the finger
                    if (tooltipX < 5)
                    {
                        tooltipX = gridPointX - (tooltipWidth / 2); // Center horizontally
                        tooltipY = gridPointY + 35; // Below the finger
                    }
                }
                else
                {
                    // For mouse: just put it below the point
                    tooltipY = gridPointY + 15;
                }
            }

            // Final bounds check
            tooltipX = Math.Max(5, Math.Min(tooltipX, gridWidth - tooltipWidth - 5));
            tooltipY = Math.Max(5, Math.Min(tooltipY, gridHeight - tooltipHeight - 5));

            // Set tooltip position
            DragTooltip.Margin = new Thickness(tooltipX, tooltipY, 0, 0);
        }

        // Optional: Add visual feedback to show the difference between touch and mouse
        private void ShowTooltip()
        {
            if (DragTooltip == null) return;

            // Different visual styles for touch vs mouse
            if (_isTouchDragging)
            {
                // Slightly larger and more visible for touch
                DragTooltip.Background = new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(240, 0, 0, 0)); // More opaque
                DragTooltip.BorderThickness = new Thickness(2); // Thicker border
            }
            else
            {
                // Standard style for mouse
                DragTooltip.Background = new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(221, 0, 0, 0)); // Original opacity
                DragTooltip.BorderThickness = new Thickness(1); // Original border
            }

            DragTooltip.Visibility = Visibility.Visible;
            DragTooltip.Opacity = 1.0;
        }

        private void HideTooltip()
        {

            if (DragTooltip != null)
            {
                DragTooltip.Visibility = Visibility.Collapsed;
            }
        }

        // IGamepadNavigable event handlers
        public void OnGamepadNavigateUp() 
        {
            // If a control point is activated, move it instead of navigating
            if (_isControlPointActivated)
            {
                AdjustControlPointVertically(1); // Move control point up (increase fan speed)
                return;
            }
            
            if (_currentFocusedElement >= 5 && _currentFocusedElement <= 9) // From control points back to Custom button
            {
                _currentFocusedElement = 4; // Custom button
                UpdateFocusVisuals();
                System.Diagnostics.Debug.WriteLine($"🎮 FanCurve: Moved up from control points to Custom button");
            }
            else if (_currentFocusedElement >= 1 && _currentFocusedElement <= 4) // From preset buttons back to toggle
            {
                _currentFocusedElement = 0;
                UpdateFocusVisuals();
                System.Diagnostics.Debug.WriteLine($"🎮 FanCurve: Moved up to toggle");
            }
        }
        
        public void OnGamepadNavigateDown() 
        {
            // If a control point is activated, move it instead of navigating
            if (_isControlPointActivated)
            {
                AdjustControlPointVertically(-1); // Move control point down (decrease fan speed)
                return;
            }
            
            if (_currentFocusedElement == 0 && PresetButtonsPanel?.Visibility == Visibility.Visible) // From toggle to first preset button
            {
                _currentFocusedElement = 1;
                UpdateFocusVisuals();
                System.Diagnostics.Debug.WriteLine($"🎮 FanCurve: Moved down to preset buttons");
            }
            else if (_currentFocusedElement == 4 && _currentCurve.ActivePreset == "Custom" && CurvePanel?.Visibility == Visibility.Visible) // From Custom button to middle control point
            {
                _currentFocusedElement = 7; // Middle control point (index 2, so element 5+2=7)
                UpdateFocusVisuals();
                System.Diagnostics.Debug.WriteLine($"🎮 FanCurve: Moved down to control points");
            }
        }
        
        public void OnGamepadNavigateLeft()
        {
            // If a control point is activated, move it instead of navigating
            if (_isControlPointActivated)
            {
                AdjustSliderValue(-1); // Move control point left (decrease temperature)
                return;
            }
            
            if (_currentFocusedElement > 5 && _currentFocusedElement <= 9) // Between control points
            {
                _currentFocusedElement--;
                UpdateFocusVisuals();
                System.Diagnostics.Debug.WriteLine($"🎮 FanCurve: Moved left to control point {_currentFocusedElement - 5}");
            }
            else if (_currentFocusedElement == 5) // Wrap around to last control point
            {
                _currentFocusedElement = 9;
                UpdateFocusVisuals();
                System.Diagnostics.Debug.WriteLine($"🎮 FanCurve: Wrapped around to last control point");
            }
            else if (_currentFocusedElement > 1 && _currentFocusedElement <= 4) // In preset buttons area
            {
                _currentFocusedElement--;
                UpdateFocusVisuals();
                System.Diagnostics.Debug.WriteLine($"🎮 FanCurve: Moved left to element {_currentFocusedElement}");
            }
        }

        public void OnGamepadNavigateRight()
        {
            // If a control point is activated, move it instead of navigating
            if (_isControlPointActivated)
            {
                AdjustSliderValue(1); // Move control point right (increase temperature)
                return;
            }
            
            if (_currentFocusedElement >= 5 && _currentFocusedElement < 9) // Between control points
            {
                _currentFocusedElement++;
                UpdateFocusVisuals();
                System.Diagnostics.Debug.WriteLine($"🎮 FanCurve: Moved right to control point {_currentFocusedElement - 5}");
            }
            else if (_currentFocusedElement == 9) // Wrap around to first control point
            {
                _currentFocusedElement = 5;
                UpdateFocusVisuals();
                System.Diagnostics.Debug.WriteLine($"🎮 FanCurve: Wrapped around to first control point");
            }
            else if (_currentFocusedElement == 0 && PresetButtonsPanel?.Visibility == Visibility.Visible) // From toggle to first preset (alternative to down)
            {
                _currentFocusedElement = 1;
                UpdateFocusVisuals();
                System.Diagnostics.Debug.WriteLine($"🎮 FanCurve: Moved right to preset buttons");
            }
            else if (_currentFocusedElement >= 1 && _currentFocusedElement < 4) // Between preset buttons
            {
                _currentFocusedElement++;
                UpdateFocusVisuals();
                System.Diagnostics.Debug.WriteLine($"🎮 FanCurve: Moved right to element {_currentFocusedElement}");
            }
        }

        public void OnGamepadActivate()
        {
            if (_currentFocusedElement == 0) // Toggle
            {
                FanCurveToggle.IsOn = !FanCurveToggle.IsOn;
                System.Diagnostics.Debug.WriteLine($"🎮 FanCurve: Toggled fan curve");
            }
            else if (_currentFocusedElement == 1) // Stealth
            {
                StealthPreset_Click(StealthPresetButton, new RoutedEventArgs());
                System.Diagnostics.Debug.WriteLine($"🎮 FanCurve: Activated Stealth preset");
            }
            else if (_currentFocusedElement == 2) // Cruise
            {
                CruisePreset_Click(CruisePresetButton, new RoutedEventArgs());
                System.Diagnostics.Debug.WriteLine($"🎮 FanCurve: Activated Cruise preset");
            }
            else if (_currentFocusedElement == 3) // Warp
            {
                WarpPreset_Click(WarpPresetButton, new RoutedEventArgs());
                System.Diagnostics.Debug.WriteLine($"🎮 FanCurve: Activated Warp preset");
            }
            else if (_currentFocusedElement == 4) // Custom
            {
                CustomPresetButton_Click(CustomPresetButton, new RoutedEventArgs());
                System.Diagnostics.Debug.WriteLine($"🎮 FanCurve: Activated Custom preset");
            }
            else if (_currentFocusedElement >= 5 && _currentFocusedElement <= 9) // Control points
            {
                // Auto-switch to Custom mode if not already in it (required for editing)
                if (_currentCurve.ActivePreset != "Custom")
                {
                    System.Diagnostics.Debug.WriteLine($"🎮 FanCurve: Auto-switching to Custom mode for control point editing");
                    CustomPresetButton_Click(CustomPresetButton, new RoutedEventArgs());
                }

                int controlPointIndex = _currentFocusedElement - 5;
                if (!_isControlPointActivated)
                {
                    // Activate control point for editing
                    _isControlPointActivated = true;
                    _activeControlPointIndex = controlPointIndex;
                    _originalControlPointPosition = _currentCurve.Points[controlPointIndex];

                    // Set slider activated state for GamepadNavigationService
                    IsSliderActivated = true;

                    UpdateFocusVisuals();
                    System.Diagnostics.Debug.WriteLine($"🎮 FanCurve: Activated control point {controlPointIndex} for editing");
                }
                else
                {
                    // Confirm changes and deactivate
                    _isControlPointActivated = false;
                    _activeControlPointIndex = -1;

                    IsSliderActivated = false;

                    UpdateFocusVisuals();
                    System.Diagnostics.Debug.WriteLine($"🎮 FanCurve: Confirmed control point {controlPointIndex} changes");
                }
            }
        }

        public void OnGamepadFocusReceived()
        {
            // Ensure gamepad service is initialized
            if (_gamepadNavigationService == null)
            {
                InitializeGamepadNavigationService();
            }
            
            _currentFocusedElement = 0; // Start with toggle
            
            // Use the property to trigger change notifications
            IsFocused = true;
            
            // Update visuals with a dispatcher delay to ensure bindings are ready
            DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.High, () =>
            {
                UpdateFocusVisuals();
            });
        }

        public void OnGamepadFocusLost()
        {
            IsFocused = false;
            UpdateFocusVisuals();
        }

        public void FocusLastElement()
        {
            // Not used - FanCurveControl is not in a NavigableExpander
        }

        private void InitializeGamepadNavigation()
        {
            GamepadNavigation.SetIsEnabled(this, true);
            GamepadNavigation.SetNavigationGroup(this, "MainControls");
            GamepadNavigation.SetNavigationOrder(this, 6);
        }

        private void InitializeGamepadNavigationService()
        {
            if (Application.Current is App app && app.MainWindow is MainWindow mainWindow)
            {
                _gamepadNavigationService = mainWindow.GamepadNavigationService;
            }
        }

        private void UpdateFocusVisuals()
        {
            // Use dispatcher to ensure UI updates happen immediately
            DispatcherQueue.TryEnqueue(() =>
            {
                // Update focus brush fields based on current state
                if (IsFocused && _gamepadNavigationService?.IsGamepadActive == true)
                {
                    _focusBorderBrush = new SolidColorBrush(Microsoft.UI.Colors.DarkViolet);
                    _focusBorderThickness = new Thickness(2);
                }
                else
                {
                    _focusBorderBrush = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
                    _focusBorderThickness = new Thickness(0);
                }

                OnPropertyChanged(nameof(FocusBorderBrush));
                OnPropertyChanged(nameof(FocusBorderThickness));
                OnPropertyChanged(nameof(ToggleFocusBrush));
                OnPropertyChanged(nameof(PresetButtonFocusBrush));
                OnPropertyChanged(nameof(ControlPointFocusBrush));
                
                // Update individual button focus states
                UpdatePresetButtonFocusStates();
                
                // Update control point focus states
                UpdateControlPointFocusStates();
            });
        }
        
        private void UpdatePresetButtonFocusStates()
        {
            // Reset all buttons first
            SetPresetButtonFocus(StealthPresetButton, false);
            SetPresetButtonFocus(CruisePresetButton, false);
            SetPresetButtonFocus(WarpPresetButton, false);
            SetPresetButtonFocus(CustomPresetButton, false);
            
            // Set focus on current element
            if (_isFocused && _gamepadNavigationService?.IsGamepadActive == true)
            {
                switch (_currentFocusedElement)
                {
                    case 1: SetPresetButtonFocus(StealthPresetButton, true); break;
                    case 2: SetPresetButtonFocus(CruisePresetButton, true); break;
                    case 3: SetPresetButtonFocus(WarpPresetButton, true); break;
                    case 4: SetPresetButtonFocus(CustomPresetButton, true); break;
                }
            }
        }
        
        private void UpdateControlPointFocusStates()
        {
            // Update all control point focus borders
            for (int i = 0; i < _controlPointFocusBorders.Count; i++)
            {
                var border = _controlPointFocusBorders[i];
                var shouldShowFocus = _isFocused && 
                                      _gamepadNavigationService?.IsGamepadActive == true && 
                                      _currentFocusedElement == (5 + i);
                                      
                if (shouldShowFocus)
                {
                    // Use DodgerBlue when activated for editing, DarkViolet for navigation
                    var focusColor = _isControlPointActivated && _activeControlPointIndex == i 
                        ? Microsoft.UI.Colors.DodgerBlue 
                        : Microsoft.UI.Colors.DarkViolet;
                    border.BorderBrush = new SolidColorBrush(focusColor);
                }
                else
                {
                    border.BorderBrush = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
                }
            }
        }
        
        private void SetPresetButtonFocus(Button? button, bool hasFocus)
        {
            if (button != null)
            {
                var borderBrush = hasFocus ? new SolidColorBrush(Microsoft.UI.Colors.DarkViolet) : new SolidColorBrush(Microsoft.UI.Colors.Transparent);
                var borderThickness = hasFocus ? new Thickness(2) : new Thickness(0);
                
                // Map buttons to their named borders
                Border? border = button.Name switch
                {
                    "StealthPresetButton" => StealthPresetBorder,
                    "CruisePresetButton" => CruisePresetBorder,
                    "WarpPresetButton" => WarpPresetBorder,
                    "CustomPresetButton" => CustomPresetBorder,
                    _ => button.Parent as Border
                };
                
                if (border != null)
                {
                    border.BorderBrush = borderBrush;
                    border.BorderThickness = borderThickness;
                }
            }
        }
        
        private void AdjustControlPoint(int direction)
        {
            if (!_isControlPointActivated || _activeControlPointIndex < 0 || _activeControlPointIndex >= _currentCurve.Points.Length)
                return;

            var currentPoint = _currentCurve.Points[_activeControlPointIndex];

            // Calculate new temperature (constrain to valid range, matching mouse behavior)
            double newTemperature = ConstrainTemperature(currentPoint.Temperature + direction, _activeControlPointIndex);

            // Only update if temperature actually changed
            if (Math.Abs(newTemperature - currentPoint.Temperature) > 0.01)
            {
                // Create new point with updated temperature (matching mouse mode - no extra validation)
                var newPoint = new FanCurvePoint
                {
                    Temperature = newTemperature,
                    FanSpeed = currentPoint.FanSpeed
                };

                // Direct update, matching mouse behavior
                _currentCurve.Points[_activeControlPointIndex] = newPoint;
                UpdateControlPointPosition(_activeControlPointIndex, newPoint);
                UpdateCurveLineOnly();

                // Save changes and apply to fan
                SaveAndApplyGamepadChanges();
            }
        }
        
        private void AdjustControlPointVertically(int direction)
        {
            if (!_isControlPointActivated || _activeControlPointIndex < 0 || _activeControlPointIndex >= _currentCurve.Points.Length)
                return;

            var currentPoint = _currentCurve.Points[_activeControlPointIndex];

            // Calculate new fan speed (clamp to 0-100, matching mouse behavior)
            double newFanSpeed = Math.Clamp(currentPoint.FanSpeed + direction, 0, 100);

            // Only update if fan speed actually changed
            if (Math.Abs(newFanSpeed - currentPoint.FanSpeed) > 0.01)
            {
                // Create new point with updated fan speed (matching mouse mode - no extra validation)
                var newPoint = new FanCurvePoint
                {
                    Temperature = currentPoint.Temperature,
                    FanSpeed = newFanSpeed
                };

                // Direct update, matching mouse behavior
                _currentCurve.Points[_activeControlPointIndex] = newPoint;
                UpdateControlPointPosition(_activeControlPointIndex, newPoint);
                UpdateCurveLineOnly();

                // Save changes and apply to fan
                SaveAndApplyGamepadChanges();
            }
        }
        
        private double ConstrainTemperature(double temperature, int pointIndex)
        {
            double minTemp = 30;
            double maxTemp = 90;

            // Ensure temperature stays between adjacent points with 2-degree minimum gap
            // This provides a balance between precision and preventing points from overlapping
            if (pointIndex > 0)
                minTemp = Math.Max(minTemp, _currentCurve.Points[pointIndex - 1].Temperature + 2);
            if (pointIndex < _currentCurve.Points.Length - 1)
                maxTemp = Math.Min(maxTemp, _currentCurve.Points[pointIndex + 1].Temperature - 2);

            return Math.Clamp(temperature, minTemp, maxTemp);
        }
        
        private double ConstrainFanSpeed(double fanSpeed)
        {
            return Math.Clamp(fanSpeed, 0, 100);
        }
        
        private bool IsValidControlPointPosition(FanCurvePoint point, int pointIndex)
        {
            // Check temperature ordering constraints with 2-degree minimum gap
            if (pointIndex > 0 && point.Temperature < _currentCurve.Points[pointIndex - 1].Temperature + 2)
                return false;
            if (pointIndex < _currentCurve.Points.Length - 1 && point.Temperature > _currentCurve.Points[pointIndex + 1].Temperature - 2)
                return false;

            // Check fan speed bounds
            if (point.FanSpeed < 0 || point.FanSpeed > 100)
                return false;

            return true;
        }
        
        private void UpdateControlPointPosition(int pointIndex, FanCurvePoint newPoint)
        {
            if (pointIndex < _controlPoints.Count)
            {
                var ellipse = _controlPoints[pointIndex];
                var focusBorder = _controlPointFocusBorders[pointIndex];

                double x = TemperatureToX(newPoint.Temperature) - POINT_RADIUS;
                double y = FanSpeedToY(newPoint.FanSpeed) - POINT_RADIUS;
                double borderX = x - 4; // Updated for thicker border
                double borderY = y - 4;

                Canvas.SetLeft(ellipse, x);
                Canvas.SetTop(ellipse, y);
                Canvas.SetLeft(focusBorder, borderX);
                Canvas.SetTop(focusBorder, borderY);
            }
        }

        private void SaveAndApplyGamepadChanges()
        {
            try
            {
                // Save custom curve if in Custom mode
                if (_currentCurve.ActivePreset == "Custom")
                {
                    SettingsService.SetCustomFanCurve(_currentCurve.Points);
                }

                // Save the current curve state
                SettingsService.SetFanCurve(_currentCurve);

                // Apply to fan hardware if enabled
                if (_currentCurve.IsEnabled && _fanControlService != null)
                {
                    if (_temperatureControlEnabled && _currentTemperature != null && _currentTemperature.MaxTemperature > 0)
                    {
                        ApplyTemperatureBasedFanControl(_currentTemperature.MaxTemperature);
                    }
                    else
                    {
                        ApplyFanCurve();
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error saving/applying gamepad changes: {ex.Message}");
            }
        }
        
        private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        public void Dispose()
        {
            // ADD: Disconnect from temperature monitoring
            DisconnectFromGlobalTemperatureMonitoring();

            if (_fanControlService != null)
            {
                _fanControlService.Dispose();
            }
        }
    }
    // Keep your existing event arguments:
    public class FanCurveChangedEventArgs : EventArgs
    {
        public FanCurve Curve { get; }
        public string StatusMessage { get; }

        public FanCurveChangedEventArgs(FanCurve curve, string statusMessage)
        {
            Curve = curve;
            StatusMessage = statusMessage;
        }
    }

}