# Type Safety & Error Handling - Code Examples and Fixes

## 1. ASYNC VOID REFACTORING EXAMPLES

### Current Problem Code
```csharp
// ❌ BAD: Async void - exceptions will crash app
private async void OnFpsLimitChanged(object sender, SelectionChangedEventArgs e)
{
    var selectedFps = _fpsSettings.AvailableFpsOptions[FpsLimitComboBox.SelectedIndex];
    FpsLimitChanged?.Invoke(this, new FpsLimitChangedEventArgs(selectedFps));
}

// ❌ BAD: Initialize returns void instead of Task
public async void Initialize(RtssFpsLimiterService fpsLimiterService)
{
    _fpsLimiterService = fpsLimiterService;
    var detection = await _fpsLimiterService.DetectRtssInstallationAsync();
}
```

### Recommended Fixes

#### Fix 1: For Event Handlers - Use Exception Handling Wrapper
```csharp
// ✅ GOOD: Event handler with proper exception handling
private void OnFpsLimitChanged(object sender, SelectionChangedEventArgs e)
{
    // Fire-and-forget with error handling
    _ = HandleFpsLimitChangedAsync();
}

private async Task HandleFpsLimitChangedAsync()
{
    try
    {
        if (FpsLimitComboBox?.SelectedIndex >= 0 && 
            FpsLimitComboBox.SelectedIndex < _fpsSettings.AvailableFpsOptions.Count)
        {
            var selectedFps = _fpsSettings.AvailableFpsOptions[FpsLimitComboBox.SelectedIndex];
            if (selectedFps != _fpsSettings.SelectedFpsLimit)
            {
                _fpsSettings.SelectedFpsLimit = selectedFps;
                OnPropertyChanged(nameof(FpsSettings));
                
                FpsLimitChanged?.Invoke(this, new FpsLimitChangedEventArgs(selectedFps));
                System.Diagnostics.Debug.WriteLine($"FPS limit changed to: {selectedFps}");
            }
        }
    }
    catch (Exception ex)
    {
        System.Diagnostics.Debug.WriteLine($"Error handling FPS limit change: {ex.Message}");
        // Optionally notify user
        _updateStatusAction?.Invoke($"Error: {ex.Message}");
    }
}
```

#### Fix 2: For Initialization - Return Task Instead
```csharp
// ❌ OLD: Returns void
public async void Initialize(RtssFpsLimiterService fpsLimiterService)
{
    _fpsLimiterService = fpsLimiterService;
}

// ✅ NEW: Returns Task for proper waiting
public async Task InitializeAsync(RtssFpsLimiterService fpsLimiterService)
{
    try
    {
        _fpsLimiterService = fpsLimiterService;
        
        if (_fpsLimiterService != null)
        {
            var detection = await _fpsLimiterService.DetectRtssInstallationAsync();
            IsRtssSupported = detection.IsInstalled && detection.IsRunning;
        }
    }
    catch (Exception ex)
    {
        System.Diagnostics.Debug.WriteLine($"Failed to initialize FPS limiter: {ex.Message}");
        IsRtssSupported = false;
    }
}

// Usage:
public async void ControlLoaded(object sender, RoutedEventArgs e)
{
    await InitializeAsync(_fpsLimiterService);
}
```

#### Fix 3: App.OnLaunched - Use Synchronous Setup or Task Management
```csharp
// ❌ OLD: Async void
protected override async void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
{
    MainWindow = new MainWindow();
    var initResult = await FanControlService.InitializeAsync();
}

// ✅ NEW: Synchronous with proper async handling
protected override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
{
    MainWindow = new MainWindow();
    
    // Fire initialization in background with error handling
    _ = InitializeServicesAsync();
}

private async Task InitializeServicesAsync()
{
    try
    {
        // Preload RTSS installation status
        await RtssFpsLimiterService.PreloadInstallationStatusAsync();

        // Initialize FanControlService
        var initResult = await FanControlService.InitializeAsync();
        System.Diagnostics.Debug.WriteLine($"Fan control initialization: {initResult.Message}");

        if (initResult.Success)
        {
            var fanCurve = SettingsService.GetFanCurve();
            if (fanCurve.IsEnabled)
            {
                FanControlService.EnableTemperatureControl(TemperatureMonitor);
            }
        }
    }
    catch (Exception ex)
    {
        System.Diagnostics.Debug.WriteLine($"Service initialization failed: {ex.Message}");
        // Log to file or telemetry
    }
}
```

---

## 2. NULLABLE REFERENCE SAFETY FIXES

### Current Problem Code
```csharp
// ❌ BAD: No null check before direct property access
MainWindow.SetTdpMonitor(TdpMonitor);  // MainWindow is nullable
MainWindow.WindowManager.SetInitialVisibilityState(false);

// ❌ BAD: Potential null dereference in lambda
var matchingGame = _cachedGames.Values.FirstOrDefault(dbGame => 
    string.Equals(dbGame.ExecutablePath, processExePath, StringComparison.OrdinalIgnoreCase));
```

### Recommended Fixes

#### Fix 1: Safe MainWindow Access Pattern
```csharp
// ✅ GOOD: Create null-safe wrapper
private void SafeSetTdpMonitor(TdpMonitorService monitor)
{
    if (MainWindow == null)
    {
        System.Diagnostics.Debug.WriteLine("Cannot set TDP monitor - MainWindow is not initialized");
        return;
    }
    
    MainWindow.SetTdpMonitor(monitor);
}

private void SafeSetInitialVisibilityState(bool isVisible)
{
    MainWindow?.WindowManager?.SetInitialVisibilityState(isVisible);
    
    if (MainWindow == null)
    {
        System.Diagnostics.Debug.WriteLine("Warning: MainWindow is null during visibility setup");
    }
}

// Usage:
SafeSetTdpMonitor(TdpMonitor);
SafeSetInitialVisibilityState(false);
```

#### Fix 2: Safe Database Lookups
```csharp
// ❌ OLD: Unsafe
var matchingGame = _cachedGames.Values.FirstOrDefault(dbGame => 
    string.Equals(dbGame.ExecutablePath, processExePath, StringComparison.OrdinalIgnoreCase));

// ✅ NEW: Safe with validation
private DetectedGame? FindGameByExecutablePath(string? executablePath)
{
    if (string.IsNullOrEmpty(executablePath))
        return null;
    
    return _cachedGames.Values.FirstOrDefault(dbGame => 
        dbGame != null && 
        !string.IsNullOrEmpty(dbGame.ExecutablePath) &&
        string.Equals(dbGame.ExecutablePath, executablePath, StringComparison.OrdinalIgnoreCase));
}
```

---

## 3. EXCEPTION HANDLING IMPROVEMENTS

### Problem Code
```csharp
// ❌ BAD: Silent failure
catch (Exception)
{
    continue;  // No logging or indication of failure
}

// ❌ BAD: Only logs to debug
catch (Exception ex)
{
    System.Diagnostics.Debug.WriteLine($"Error: {ex.Message}");
    _settings = new Dictionary<string, object>();  // Silently loses data
}
```

### Recommended Fixes

#### Fix 1: Add Proper Logging and Telemetry
```csharp
// ✅ GOOD: Proper error logging
private static void LoadSettings()
{
    try
    {
        if (File.Exists(SettingsPath))
        {
            var json = File.ReadAllText(SettingsPath);
            _settings = JsonSerializer.Deserialize<Dictionary<string, object>>(json);
        }
        else
        {
            _settings = new Dictionary<string, object>();
        }
    }
    catch (JsonException jsonEx)
    {
        System.Diagnostics.Debug.WriteLine($"Failed to parse settings JSON: {jsonEx.Message}");
        LogErrorToFile("SettingsService.LoadSettings", jsonEx);
        _settings = new Dictionary<string, object>();
    }
    catch (IOException ioEx)
    {
        System.Diagnostics.Debug.WriteLine($"Failed to read settings file: {ioEx.Message}");
        LogErrorToFile("SettingsService.LoadSettings", ioEx);
        _settings = new Dictionary<string, object>();
    }
    catch (Exception ex)
    {
        System.Diagnostics.Debug.WriteLine($"Unexpected error loading settings: {ex.Message}");
        LogErrorToFile("SettingsService.LoadSettings", ex);
        _settings = new Dictionary<string, object>();
    }
}

private static void LogErrorToFile(string context, Exception ex)
{
    try
    {
        var logPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "HUDRA", "errors.log");
        
        var message = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {context}: {ex.GetType().Name}: {ex.Message}\n";
        File.AppendAllText(logPath, message);
    }
    catch
    {
        // Ignore logging failures
    }
}
```

#### Fix 2: User-Facing Error Messages
```csharp
// ❌ BAD: Silent failure
catch (Exception ex)
{
    System.Diagnostics.Debug.WriteLine($"Failed to check RTSS: {ex.Message}");
    IsRtssSupported = false;  // User doesn't know why
}

// ✅ GOOD: Inform user
private async Task RefreshRtssStatusAsync()
{
    if (_fpsLimiterService == null)
        return;

    try
    {
        var detection = await _fpsLimiterService.DetectRtssInstallationAsync(forceRefresh: true);
        IsRtssInstalled = detection.IsInstalled;
        IsRtssSupported = detection.IsInstalled && detection.IsRunning;
        
        if (!detection.IsInstalled)
        {
            System.Diagnostics.Debug.WriteLine("RTSS not detected");
            // Optionally show notification to user
        }
    }
    catch (OperationCanceledException)
    {
        System.Diagnostics.Debug.WriteLine("RTSS detection was cancelled");
        IsRtssSupported = false;
    }
    catch (Exception ex)
    {
        System.Diagnostics.Debug.WriteLine($"Failed to refresh RTSS status: {ex.Message}");
        IsRtssInstalled = false;
        IsRtssSupported = false;
        
        // Show user-facing error if critical
        await ShowErrorNotificationAsync("Failed to detect RTSS. Please check your installation.");
    }
}

private async Task ShowErrorNotificationAsync(string message)
{
    // Show in UI
    DispatcherQueue.TryEnqueue(() =>
    {
        // Display message to user (e.g., InfoBar, MessageDialog, etc.)
    });
}
```

---

## 4. JSON SERIALIZATION SAFETY IMPROVEMENTS

### Problem Code
```csharp
// ❌ BAD: GetInt32 can throw
if (value is JsonElement jsonElement && jsonElement.ValueKind == JsonValueKind.Number)
{
    return jsonElement.GetInt32();  // Can throw InvalidOperationException
}

// ❌ BAD: Unsafe deserialization
points = JsonSerializer.Deserialize<FanCurvePoint[]>(pointsJson) 
    ?? GetDefaultFanCurvePoints();  // No error handling
```

### Recommended Fixes

#### Fix 1: Use TryGetXxx Methods
```csharp
// ✅ GOOD: Use TryGetInt32
private static int GetIntegerSetting(string key, int defaultValue)
{
    if (_settings == null)
        return defaultValue;

    if (!_settings.TryGetValue(key, out var value))
        return defaultValue;

    if (value is JsonElement jsonElement)
    {
        if (jsonElement.TryGetInt32(out int result))
        {
            return result;
        }
        else
        {
            System.Diagnostics.Debug.WriteLine(
                $"Setting '{key}' is not a valid integer: {jsonElement}");
            return defaultValue;
        }
    }
    else if (value is int intValue)
    {
        return intValue;
    }

    System.Diagnostics.Debug.WriteLine(
        $"Setting '{key}' has unexpected type: {value?.GetType().Name}");
    return defaultValue;
}
```

#### Fix 2: Safe JSON Deserialization with Validation
```csharp
// ✅ GOOD: Safe deserialization with proper error handling
public static FanCurve GetFanCurve()
{
    lock (_lock)
    {
        try
        {
            var isEnabled = GetBooleanSetting(FanCurveEnabledKey, false);
            var pointsJson = GetStringSetting(FanCurvePointsKey, "");
            var activePreset = GetStringSetting(FanCurveActivePresetKey, "");

            FanCurvePoint[] points;

            if (!string.IsNullOrEmpty(pointsJson))
            {
                try
                {
                    points = JsonSerializer.Deserialize<FanCurvePoint[]>(pointsJson);
                    
                    if (points == null || points.Length == 0)
                    {
                        System.Diagnostics.Debug.WriteLine(
                            "Deserialized fan curve is empty, using defaults");
                        points = GetDefaultFanCurvePoints();
                    }
                    
                    // Validate each point
                    if (!ValidateFanCurvePoints(points))
                    {
                        System.Diagnostics.Debug.WriteLine(
                            "Fan curve points validation failed, using defaults");
                        points = GetDefaultFanCurvePoints();
                    }
                }
                catch (JsonException jsonEx)
                {
                    System.Diagnostics.Debug.WriteLine(
                        $"Failed to parse fan curve JSON: {jsonEx.Message}");
                    points = GetDefaultFanCurvePoints();
                }
            }
            else
            {
                points = GetDefaultFanCurvePoints();
            }

            return new FanCurve
            {
                IsEnabled = isEnabled,
                Points = points,
                ActivePreset = activePreset
            };
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error loading fan curve: {ex.Message}");
            return new FanCurve
            {
                IsEnabled = false,
                Points = GetDefaultFanCurvePoints(),
                ActivePreset = ""
            };
        }
    }
}

private static bool ValidateFanCurvePoints(FanCurvePoint[] points)
{
    if (points == null || points.Length == 0)
        return false;

    // Check points are in ascending temperature order
    for (int i = 1; i < points.Length; i++)
    {
        if (points[i].Temperature <= points[i - 1].Temperature)
        {
            System.Diagnostics.Debug.WriteLine(
                $"Invalid fan curve: temperatures not in ascending order");
            return false;
        }

        // Check fan speeds are valid (0-100)
        if (points[i].FanSpeed < 0 || points[i].FanSpeed > 100)
        {
            System.Diagnostics.Debug.WriteLine(
                $"Invalid fan curve: speed out of range: {points[i].FanSpeed}");
            return false;
        }
    }

    return true;
}
```

---

## 5. REFLECTION SAFETY IMPROVEMENTS

### Problem Code
```csharp
// ❌ BAD: Unsafe reflection
result.Provider.GetType().GetProperty("IsAvailable")
    ?.SetValue(result.Provider, false);  // Silent failure
```

### Recommended Fix
```csharp
// ✅ GOOD: Type-safe approach
public interface IGameLibraryProvider
{
    string ProviderName { get; }
    GameSource GameSource { get; }
    bool IsAvailable { get; }  // Add setter
    Task<Dictionary<string, DetectedGame>> GetGamesAsync();
    event EventHandler<string>? ScanProgressChanged;
}

public class GameLibNetProvider : IGameLibraryProvider
{
    // ... existing code ...
    public bool IsAvailable { get; set; } = true;  // Now settable
}

// Usage - no reflection needed:
if (result.Success)
{
    // Provider.IsAvailable can now be set directly
    foreach (var result in providerResults)
    {
        if (!result.Success && result.Provider is IGameLibraryProvider provider)
        {
            provider.IsAvailable = false;
        }
    }
}
```

---

## 6. GLOBAL ERROR HANDLING SETUP

### Add to App.xaml.cs
```csharp
public partial class App : Application
{
    public App()
    {
        InitializeComponent();
        
        // Setup global exception handlers
        SetupGlobalExceptionHandlers();
    }

    private void SetupGlobalExceptionHandlers()
    {
        // Handle unhandled exceptions on the UI thread
        this.UnhandledException += OnUnhandledException;

        // Handle unhandled exceptions on background threads
        AppDomain.CurrentDomain.UnhandledException += OnAppDomainUnhandledException;

        // Handle unobserved task exceptions
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
    }

    private void OnUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        System.Diagnostics.Debug.WriteLine($"[CRITICAL] Unhandled UI exception: {e.Exception}");
        LogCriticalError("UnhandledException", e.Exception);
        
        // Prevent app crash for debugging
        e.Handled = true;
    }

    private void OnAppDomainUnhandledException(object sender, System.UnhandledExceptionEventArgs e)
    {
        var ex = e.ExceptionObject as Exception;
        System.Diagnostics.Debug.WriteLine($"[CRITICAL] Unhandled app domain exception: {ex?.Message}");
        LogCriticalError("AppDomainUnhandledException", ex);
    }

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        System.Diagnostics.Debug.WriteLine($"[CRITICAL] Unobserved task exception: {e.Exception}");
        LogCriticalError("UnobservedTaskException", e.Exception);
        e.SetObserved();  // Prevent app crash
    }

    private static void LogCriticalError(string context, Exception? ex)
    {
        try
        {
            var logPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "HUDRA", "critical_errors.log");
            
            var dir = Path.GetDirectoryName(logPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);
            
            var message = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {context}\n" +
                         $"Message: {ex?.Message}\n" +
                         $"Type: {ex?.GetType().Name}\n" +
                         $"StackTrace: {ex?.StackTrace}\n\n";
            
            File.AppendAllText(logPath, message);
        }
        catch
        {
            // Ignore logging failures
        }
    }
}
```

---

## Implementation Priority

1. **Immediate (This week):**
   - Fix async void methods in event handlers
   - Add global exception handlers
   - Add null checks for MainWindow

2. **This month:**
   - Replace bare catch blocks with logging
   - Implement TryParse for JSON parsing
   - Add input validation

3. **Ongoing:**
   - Refactor Dictionary<string, object> to typed classes
   - Add Result<T> pattern
   - Add integration tests

