using HUDRA.Services;
using HUDRA.Services.FanControl;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Windows.Foundation;

namespace HUDRA.Controls
{
    public sealed partial class FanCurveControl : UserControl
    {
        public event EventHandler<FanCurveChangedEventArgs>? FanCurveChanged;

        private FanControlService? _fanControlService;
        private bool _isUpdatingControls = false;
        private bool _isInitialized = false;
        private bool _isDragging = false;
        private int _dragPointIndex = -1;

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
        private Polyline? _curveVisualization;
        private readonly List<Line> _gridLines = new();
        private readonly List<TextBlock> _axisLabels = new();

        //touch handling
        private bool _isTouchDragging = false;
        private uint _touchPointerId = 0;

        public FanCurveControl()
        {
            this.InitializeComponent();
            // Don't load curve here - wait for Initialize() when everything is ready
            InitializeDefaultCurve();
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

                _fanControlService = new FanControlService(DispatcherQueue);

                // ADD: Connect to global temperature monitoring
                ConnectToGlobalTemperatureMonitoring();

                // Initialize the service
                var result = await _fanControlService.InitializeAsync();

                if (result.Success)
                {

                    // Set up canvas rendering
                    SetupCanvas();
                    RenderCurveCanvas();

                    // Load saved curve state
                    System.Diagnostics.Debug.WriteLine($"=== Setting UI State ===");
                    System.Diagnostics.Debug.WriteLine($"Current curve enabled: {_currentCurve.IsEnabled}");

                    _isUpdatingControls = true;
                    FanCurveToggle.IsOn = _currentCurve.IsEnabled; // CHANGED: Use FanCurveToggle instead of CurveEnabledToggle
                    System.Diagnostics.Debug.WriteLine($"Toggle set to: {FanCurveToggle.IsOn}");

                    // ADD: Update temperature monitoring state
                    UpdateTemperatureMonitoringState();

                    CurvePanel.Visibility = _currentCurve.IsEnabled ? Visibility.Visible : Visibility.Collapsed;
                    TemperatureStatusPanel.Visibility = _currentCurve.IsEnabled ? Visibility.Visible : Visibility.Collapsed;
                    System.Diagnostics.Debug.WriteLine($"Panel visibility: {CurvePanel.Visibility}");
                    _isUpdatingControls = false;

                    // Apply saved curve if it was enabled (with delay to ensure service is ready)
                    if (_currentCurve.IsEnabled)
                    {

                        // Use dispatcher to ensure fan service is fully ready
                        DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Normal, async () =>
                        {
                            // Small delay to ensure service is completely initialized
                            await Task.Delay(500);

                            System.Diagnostics.Debug.WriteLine("Applying saved fan curve on startup");
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
                    FanCurveToggle.IsEnabled = false; // CHANGED: Use FanCurveToggle
                }

                _isInitialized = true;
                FanCurveChanged?.Invoke(this, new FanCurveChangedEventArgs(
                    _currentCurve, result.Message));
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

            System.Diagnostics.Debug.WriteLine($"UpdateTemperatureDisplay called: {temperatureData.MaxTemperature:F1}°C, Curve enabled: {_currentCurve.IsEnabled}");

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
        private void SetupCanvas()
        {
            // Essential for touch hit testing
            FanCurveCanvas.Background = new SolidColorBrush(Colors.Transparent);
            FanCurveCanvas.IsHitTestVisible = true;

            // CRITICAL: Prevent parent scroll viewers from stealing touch events
            FanCurveCanvas.ManipulationMode = Microsoft.UI.Xaml.Input.ManipulationModes.None;

        }

        private void RenderCurveCanvas()
        {
            FanCurveCanvas.Children.Clear();
            _controlPoints.Clear();
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

                double x = TemperatureToX(point.Temperature) - POINT_RADIUS;
                double y = FanSpeedToY(point.FanSpeed) - POINT_RADIUS;

                Canvas.SetLeft(ellipse, x);
                Canvas.SetTop(ellipse, y);

                FanCurveCanvas.Children.Add(ellipse);
                _controlPoints.Add(ellipse);
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

            var position = e.GetCurrentPoint(FanCurveCanvas).Position;
            var pointer = e.Pointer;

            System.Diagnostics.Debug.WriteLine($"Pointer pressed: Type={pointer.PointerDeviceType}, Position={position.X:F1},{position.Y:F1}");

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
                    System.Diagnostics.Debug.WriteLine($"Point {i} hit! Starting drag with {pointer.PointerDeviceType}");

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

            System.Diagnostics.Debug.WriteLine("No point hit");

            // If no point was hit, allow normal scrolling behavior
            // Don't set e.Handled = true here
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
                System.Diagnostics.Debug.WriteLine($"Touch ID mismatch: expected {_touchPointerId}, got {pointer.PointerId}");
                return;
            }

            var position = e.GetCurrentPoint(FanCurveCanvas).Position;

            // Convert to temperature and fan speed
            double newTemp = XToTemperature(position.X);
            double newSpeed = YToFanSpeed(position.Y);

            // Constrain temperature to valid range for this point
            if (_dragPointIndex > 0)
                newTemp = Math.Max(newTemp, _currentCurve.Points[_dragPointIndex - 1].Temperature + 5);
            if (_dragPointIndex < _currentCurve.Points.Length - 1)
                newTemp = Math.Min(newTemp, _currentCurve.Points[_dragPointIndex + 1].Temperature - 5);

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

            // For touch, make sure we're releasing the same touch point
            if (_isTouchDragging && pointer.PointerId != _touchPointerId)
            {
                System.Diagnostics.Debug.WriteLine($"Touch release ID mismatch: expected {_touchPointerId}, got {pointer.PointerId}");
                return;
            }

            if (_isDragging)
            {
                System.Diagnostics.Debug.WriteLine($"Pointer released: {pointer.PointerDeviceType}");

                // Handle the event to prevent any residual scroll behavior
                e.Handled = true;

                _isDragging = false;
                _dragPointIndex = -1;
                _isTouchDragging = false;
                _touchPointerId = 0;

                FanCurveCanvas.ReleasePointerCapture(pointer);

                // Hide tooltip
                HideTooltip();

                // Full re-render only when dragging is complete
                RenderCurveCanvas();

                // Save the updated curve to settings
                SettingsService.SetFanCurve(_currentCurve);

                // Notify of curve change
                FanCurveChanged?.Invoke(this, new FanCurveChangedEventArgs(
                    _currentCurve, "Fan curve updated"));

                // Apply new curve if enabled
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

                // Save enabled state to settings
                SettingsService.SetFanCurveEnabled(isEnabled);
                System.Diagnostics.Debug.WriteLine($"Fan curve enabled state saved: {isEnabled}");

                // Show/hide curve panel
                CurvePanel.Visibility = isEnabled ? Visibility.Visible : Visibility.Collapsed;
                TemperatureStatusPanel.Visibility = isEnabled ? Visibility.Visible : Visibility.Collapsed;

                if (isEnabled)
                {
                    if (_fanControlService != null)
                    {
                        ApplyFanCurve();
                        EnableTemperatureBasedControl(); // NEW: Enable temperature control
                    }
                }
                else
                {
                    DisableTemperatureBasedControl(); // NEW: Disable temperature control

                    if (_fanControlService != null)
                    {
                        // Return to hardware mode
                        _fanControlService.SetAutoMode();
                    }
                }

                // Update temperature monitoring state
                UpdateTemperatureMonitoringState();

                FanCurveChanged?.Invoke(this, new FanCurveChangedEventArgs(
                    _currentCurve, $"Fan curve {(isEnabled ? "enabled" : "disabled")}"));
            }
            catch (Exception ex)
            {
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
            System.Diagnostics.Debug.WriteLine("=== HideTooltip ===");

            if (DragTooltip != null)
            {
                DragTooltip.Visibility = Visibility.Collapsed;
                System.Diagnostics.Debug.WriteLine("Tooltip hidden");
            }
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