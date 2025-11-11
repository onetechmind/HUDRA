# HUDRA Architecture Analysis & Design Pattern Opportunities

## Executive Summary

HUDRA is a WinUI 3 application for managing hardware settings on handheld gaming devices. The codebase demonstrates solid foundational design with services, events, and proper separation of concerns, but has several architectural opportunities for improvement around dependency management, state management, error handling, and async patterns.

**Key Findings:**
- **40 services** managing various concerns (TDP, FanControl, Battery, Gaming, etc.)
- **Direct service instantiation** throughout the codebase rather than centralized DI
- **Event-driven communication** but with potential for formalization
- **Async void methods** creating exception tracking challenges
- **Static state management** (SettingsService) with thread safety via locks
- **Large monolithic components** (MainWindow: 1500 lines, MainWindow.xaml.cs: 61KB)
- **Limited interface abstraction** (only 1 interface file)

---

## 1. CURRENT DEPENDENCY MANAGEMENT

### Current Pattern: Manual Service Instantiation

**App.xaml.cs** creates services at startup:
```csharp
TdpMonitor = new TdpMonitorService(MainWindow.DispatcherQueue);
TemperatureMonitor = new TemperatureMonitorService(MainWindow.DispatcherQueue);
FanControlService = new FanControlService(MainWindow.DispatcherQueue);
TurboService = new TurboService(FanControlService.DetectedDevice);
_trayIcon = new TrayIconService();
_powerEventService = new PowerEventService(MainWindow, MainWindow.DispatcherQueue);
```

**MainWindow.xaml.cs** creates additional services:
```csharp
_dpiService = new DpiScalingService(this);
_windowManager = new WindowManagementService(this, _dpiService);
_audioService = new AudioService();
_brightnessService = new BrightnessService();
_batteryService = new BatteryService(DispatcherQueue);
_powerProfileService = new PowerProfileService();
// ... 8+ more services
```

### Issues Identified

1. **No centralized dependency container** - Services scattered across App.xaml.cs and MainWindow.xaml.cs
2. **Service graph dependencies implicit** - FanControlService creates TDPService internally
3. **Lifetime management unclear** - Some services stored as static in App, others as instance fields
4. **Hard to test** - No interface abstraction or mock injection points
5. **Order dependencies** - Initialization order matters (e.g., TurboService depends on FanControlService)
6. **Disposal challenges** - Manual cleanup in App.CleanupAndExit() is error-prone

### Recommendation 1: Implement Service Locator Pattern (Short-term)

Create a centralized service container:

```csharp
public class ServiceContainer
{
    private static readonly Lazy<ServiceContainer> _instance = 
        new(() => new ServiceContainer());
    
    public static ServiceContainer Instance => _instance.Value;
    
    private readonly Dictionary<Type, Func<object>> _factories = new();
    private readonly Dictionary<Type, object> _singletons = new();
    
    public void Register<T>(Func<T> factory) where T : class
    {
        _factories[typeof(T)] = () => factory();
    }
    
    public void RegisterSingleton<T>(T instance) where T : class
    {
        _singletons[typeof(T)] = instance;
    }
    
    public T? GetService<T>() where T : class
    {
        var type = typeof(T);
        if (_singletons.TryGetValue(type, out var singleton))
            return singleton as T;
        if (_factories.TryGetValue(type, out var factory))
            return factory() as T;
        return null;
    }
}
```

**Usage in App.xaml.cs:**
```csharp
protected override async void OnLaunched(LaunchActivatedEventArgs args)
{
    MainWindow = new MainWindow();
    
    // Register services in order
    ServiceContainer.Instance.RegisterSingleton(MainWindow);
    ServiceContainer.Instance.RegisterSingleton(new TdpMonitorService(MainWindow.DispatcherQueue));
    ServiceContainer.Instance.RegisterSingleton(new TemperatureMonitorService(MainWindow.DispatcherQueue));
    
    // Services can now access others via container
    var tdpMonitor = ServiceContainer.Instance.GetService<TdpMonitorService>();
}
```

### Recommendation 2: Long-term - Migrate to Microsoft.Extensions.DependencyInjection

```csharp
public class ServiceCollectionExtensions
{
    public static IServiceCollection AddHudraServices(this IServiceCollection services, Window mainWindow)
    {
        // Core infrastructure
        services.AddSingleton(mainWindow);
        services.AddSingleton(mainWindow.DispatcherQueue);
        
        // Monitor services
        services.AddSingleton<TdpMonitorService>();
        services.AddSingleton<TemperatureMonitorService>();
        
        // Control services - depend on monitors
        services.AddSingleton<FanControlService>(provider =>
        {
            var dispatcher = provider.GetRequiredService<DispatcherQueue>();
            return new FanControlService(dispatcher);
        });
        
        services.AddSingleton<TurboService>(provider =>
        {
            var fanControl = provider.GetRequiredService<FanControlService>();
            return new TurboService(fanControl.DetectedDevice);
        });
        
        // UI services
        services.AddSingleton<NavigationService>();
        services.AddSingleton<WindowManagementService>();
        
        return services;
    }
}
```

**App.xaml.cs initialization:**
```csharp
public static IServiceProvider Services { get; private set; }

protected override async void OnLaunched(LaunchActivatedEventArgs args)
{
    MainWindow = new MainWindow();
    
    var services = new ServiceCollection();
    services.AddHudraServices(MainWindow);
    Services = services.BuildServiceProvider();
    
    // Access services via injection
    var tdpMonitor = Services.GetRequiredService<TdpMonitorService>();
}
```

---

## 2. STATE MANAGEMENT

### Current Patterns

**Settings Persistence** (SettingsService.cs - 766 lines):
```csharp
private static Dictionary<string, object>? _settings;
private static readonly object _lock = new object();

public static int GetStartupTdp()
{
    lock (_lock)
    {
        if (_settings != null && _settings.TryGetValue(StartupTdpKey, out var value))
        {
            // ... JSON element handling ...
            return Convert.ToInt32(value);
        }
        return HudraSettings.DEFAULT_STARTUP_TDP;
    }
}
```

**In-Memory State** (App.xaml.cs):
```csharp
public TdpMonitorService? TdpMonitor { get; private set; }
public TemperatureMonitorService? TemperatureMonitor { get; private set; }
public FanControlService? FanControlService { get; private set; }
public bool StartupTdpAlreadyApplied { get; private set; } = false;
```

**State Synchronization:**
- MainWindow properties with INotifyPropertyChanged
- Direct service method calls from UI
- Settings service acts as global mutable state

### Issues Identified

1. **SettingsService is a global singleton** - All state mutations require lock acquisition
2. **No reactive state management** - Changes don't flow through property changed events
3. **Scattered state** - Settings in SettingsService, monitors in App, UI state in MainWindow
4. **Transient inconsistency** - During app startup, multiple services read state simultaneously
5. **No observable properties** - Can't easily track state changes across the app
6. **Manual synchronization** - MainWindow polls TdpMonitor instead of being notified

### Recommendation 3: Introduce Observable State Pattern

Create a centralized state container:

```csharp
public class AppState : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    
    private int _currentTdp = -1;
    public int CurrentTdp
    {
        get => _currentTdp;
        set
        {
            if (_currentTdp != value)
            {
                _currentTdp = value;
                OnPropertyChanged();
            }
        }
    }
    
    private double _currentTemperature = 0;
    public double CurrentTemperature
    {
        get => _currentTemperature;
        set
        {
            if (_currentTemperature != value)
            {
                _currentTemperature = value;
                OnPropertyChanged();
            }
        }
    }
    
    private FanControlMode _fanMode = FanControlMode.Hardware;
    public FanControlMode FanMode
    {
        get => _fanMode;
        set
        {
            if (_fanMode != value)
            {
                _fanMode = value;
                OnPropertyChanged();
            }
        }
    }
    
    private bool _isGameRunning = false;
    public bool IsGameRunning
    {
        get => _isGameRunning;
        set
        {
            if (_isGameRunning != value)
            {
                _isGameRunning = value;
                OnPropertyChanged();
            }
        }
    }
    
    protected void OnPropertyChanged([CallerMemberName] string name = "")
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
```

**Usage pattern:**
```csharp
public class TdpMonitorService : IDisposable
{
    private readonly AppState _state;
    
    public TdpMonitorService(AppState state, DispatcherQueue dispatcher)
    {
        _state = state;
        _dispatcher = dispatcher;
    }
    
    private void CheckTdpCallback(object? state)
    {
        try
        {
            var current = _tdpService.GetCurrentTdp();
            _dispatcher.TryEnqueue(() =>
            {
                _state.CurrentTdp = current.TdpWatts;  // Triggers PropertyChanged
            });
        }
        catch (Exception ex) { /* ... */ }
    }
}
```

**UI Binding:**
```xml
<TextBlock Text="{Binding CurrentTdp, Mode=OneWay}" />
```

### Recommendation 4: Separate Persistence from State

Create distinct layers:

```csharp
// Settings: Persistent configuration (disk)
public interface ISettingsRepository
{
    T GetSetting<T>(string key, T defaultValue);
    void SetSetting<T>(string key, T value);
}

// State: Runtime application state (memory)
public interface IAppState
{
    T GetState<T>(string key);
    void SetState<T>(string key, T value);
    event EventHandler<StateChangedEventArgs> StateChanged;
}

// Usage in services
public class FanControlService
{
    private readonly IAppState _appState;
    private readonly ISettingsRepository _settings;
    
    public async Task EnableFanCurveAsync()
    {
        _appState.SetState("fan_mode", FanControlMode.Software);
        _settings.SetSetting("FanCurveEnabled", true); // Persisted
    }
}
```

---

## 3. CONFIGURATION MANAGEMENT

### Current Patterns

**Scattered configuration:**

1. **HudraSettings.cs** - Constants
```csharp
public static class HudraSettings
{
    public const int MIN_TDP = 5;
    public const int MAX_TDP = 30;
    public const double BASE_WINDOW_WIDTH = 375.0;
    public static readonly TimeSpan TDP_AUTO_SET_DELAY = TimeSpan.FromMilliseconds(1000);
}
```

2. **Settings.xml** - Game profiles and UI layout
```xml
<WindowWidth>800</WindowWidth>
<Hotkey>S</Hotkey>
```

3. **Hardcoded values throughout code:**
```csharp
// In App.xaml.cs
targetTdp = HudraSettings.DEFAULT_STARTUP_TDP;

// In TdpMonitorService.cs
if (Math.Abs(current - target) > 2) // Magic number!

// In FanControlService.cs - line 195
"Error setting fan mode"
```

### Issues Identified

1. **Magic numbers scattered** - "2" TDP drift threshold, "1000" delay, "60" seconds monitor interval
2. **Enum string keys** - Settings stored by string keys in JSON, error-prone
3. **No schema validation** - Settings.xml not validated against schema
4. **Inconsistent defaults** - Some in constants, some in GetSetting fallbacks
5. **No environment-specific config** - Same values for Debug/Release/Device types

### Recommendation 5: Create Configuration Objects

```csharp
public class HudraConfiguration
{
    public TdpConfiguration TdpConfig { get; set; } = new();
    public FanConfiguration FanConfig { get; set; } = new();
    public MonitorConfiguration MonitorConfig { get; set; } = new();
    public UiConfiguration UiConfig { get; set; } = new();
}

public class TdpConfiguration
{
    public int MinTdp { get; set; } = 5;
    public int MaxTdp { get; set; } = 30;
    public int DefaultStartupTdp { get; set; } = 10;
    public int DriftThreshold { get; set; } = 2;  // Named constant!
    public TimeSpan AutoSetDelay { get; set; } = TimeSpan.FromMilliseconds(1000);
    public bool CorrectionEnabled { get; set; } = true;
}

public class MonitorConfiguration
{
    public TimeSpan CheckInterval { get; set; } = TimeSpan.FromSeconds(60);
    public TimeSpan BatteryUpdateInterval { get; set; } = TimeSpan.FromSeconds(30);
    public TimeSpan TopMostCheckInterval { get; set; } = TimeSpan.FromMilliseconds(200);
}
```

**Load from JSON configuration file:**
```csharp
public class ConfigurationLoader
{
    public static HudraConfiguration Load(string path)
    {
        var json = File.ReadAllText(path);
        var options = new JsonSerializerOptions 
        { 
            PropertyNameCaseInsensitive = true,
            WriteIndented = true 
        };
        return JsonSerializer.Deserialize<HudraConfiguration>(json, options)
            ?? new HudraConfiguration(); // Fallback to defaults
    }
}
```

**appsettings.json:**
```json
{
  "tdpConfig": {
    "minTdp": 5,
    "maxTdp": 30,
    "defaultStartupTdp": 10,
    "driftThreshold": 2,
    "autoSetDelayMs": 1000,
    "correctionEnabled": true
  },
  "fanConfig": {
    "temperatureCheckInterval": 5000,
    "autoModeHysteresis": 3
  },
  "monitorConfig": {
    "checkIntervalSeconds": 60,
    "batteryUpdateIntervalSeconds": 30,
    "topMostCheckIntervalMs": 200
  }
}
```

---

## 4. EVENT COMMUNICATION

### Current Event System

Services use direct EventHandler pattern:

```csharp
// TdpMonitorService
public event EventHandler<TdpDriftEventArgs>? TdpDriftDetected;

// EnhancedGameDetectionService
public event EventHandler<GameInfo?>? GameDetected;
public event EventHandler? GameStopped;

// TrayIconService
public event EventHandler? DoubleClicked;
public event EventHandler? ExitRequested;
```

App and pages subscribe directly:
```csharp
_trayIcon.DoubleClicked += (s, e) => MainWindow.ToggleWindowVisibility();
_trayIcon.ExitRequested += (s, e) => CleanupAndExit();
_powerEventService.HibernationResumeDetected += OnHibernationResumeDetected;
```

### Issues Identified

1. **Direct service references required** - Components need to know about services to subscribe
2. **Tight coupling** - Services hardcoded in event subscriptions
3. **No event ordering** - Multiple handlers execute in uncertain order
4. **Memory leaks possible** - Forgotten unsubscriptions keep services alive
5. **Difficult to trace flows** - Event cause/effect relationships implicit
6. **No event aggregation** - Each service fires its own events independently

### Recommendation 6: Implement Event Aggregator Pattern

```csharp
public interface IEvent
{
    // Marker interface
}

public class TdpChangedEvent : IEvent
{
    public int PreviousTdp { get; set; }
    public int NewTdp { get; set; }
    public string Reason { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}

public class GameDetectedEvent : IEvent
{
    public GameInfo Game { get; set; }
    public DateTime DetectedAt { get; set; } = DateTime.UtcNow;
}

public class HibernationResumedEvent : IEvent
{
    public DateTime ResumedAt { get; set; } = DateTime.UtcNow;
}

// Event Aggregator
public interface IEventAggregator
{
    void Publish<TEvent>(TEvent eventToPublish) where TEvent : IEvent;
    
    void Subscribe<TEvent>(Action<TEvent> handler) where TEvent : IEvent;
    void Subscribe<TEvent>(object subscriber, Action<TEvent> handler) where TEvent : IEvent;
    
    void Unsubscribe<TEvent>(Action<TEvent> handler) where TEvent : IEvent;
    void Unsubscribe<TEvent>(object subscriber) where TEvent : IEvent;
}

public class EventAggregator : IEventAggregator
{
    private readonly Dictionary<Type, List<(WeakReference, Delegate)>> _subscribers = new();
    private readonly object _lock = new object();
    
    public void Publish<TEvent>(TEvent eventToPublish) where TEvent : IEvent
    {
        lock (_lock)
        {
            var eventType = typeof(TEvent);
            if (!_subscribers.TryGetValue(eventType, out var handlers))
                return;
            
            foreach (var (weakRef, handler) in handlers.ToList())
            {
                if (weakRef.IsAlive)
                {
                    (handler as Action<TEvent>)?.Invoke(eventToPublish);
                }
                else
                {
                    handlers.Remove((weakRef, handler)); // Clean up dead references
                }
            }
        }
    }
    
    public void Subscribe<TEvent>(Action<TEvent> handler) where TEvent : IEvent
    {
        lock (_lock)
        {
            var eventType = typeof(TEvent);
            if (!_subscribers.ContainsKey(eventType))
                _subscribers[eventType] = new();
            _subscribers[eventType].Add((new WeakReference(handler.Target), handler));
        }
    }
    
    public void Subscribe<TEvent>(object subscriber, Action<TEvent> handler) where TEvent : IEvent
    {
        lock (_lock)
        {
            var eventType = typeof(TEvent);
            if (!_subscribers.ContainsKey(eventType))
                _subscribers[eventType] = new();
            _subscribers[eventType].Add((new WeakReference(subscriber), handler));
        }
    }
    
    public void Unsubscribe<TEvent>(object subscriber) where TEvent : IEvent
    {
        lock (_lock)
        {
            var eventType = typeof(TEvent);
            if (_subscribers.TryGetValue(eventType, out var handlers))
            {
                handlers.RemoveAll(h => !h.Item1.IsAlive || h.Item1.Target == subscriber);
            }
        }
    }
}
```

**Usage in services:**

```csharp
public class TdpMonitorService : IDisposable
{
    private readonly IEventAggregator _events;
    
    public TdpMonitorService(IEventAggregator events, DispatcherQueue dispatcher)
    {
        _events = events;
        _dispatcher = dispatcher;
    }
    
    private void CheckTdpCallback(object? state)
    {
        try
        {
            var current = _tdpService.GetCurrentTdp();
            if (current != _targetTdp)
            {
                _dispatcher.TryEnqueue(() =>
                {
                    // Publish event instead of raising custom event
                    _events.Publish(new TdpChangedEvent 
                    { 
                        PreviousTdp = _targetTdp,
                        NewTdp = current.TdpWatts,
                        Reason = "Drift detected and corrected"
                    });
                });
            }
        }
        catch (Exception ex) { /* ... */ }
    }
}
```

**Usage in App.xaml.cs:**

```csharp
protected override async void OnLaunched(LaunchActivatedEventArgs args)
{
    // Register aggregator
    var eventAggregator = new EventAggregator();
    ServiceContainer.Instance.RegisterSingleton(eventAggregator);
    
    MainWindow = new MainWindow();
    
    // Subscribe to events
    eventAggregator.Subscribe<HibernationResumedEvent>(OnHibernationResumed);
    eventAggregator.Subscribe<GameDetectedEvent>(OnGameDetected);
}

private void OnHibernationResumed(HibernationResumedEvent evt)
{
    Debug.WriteLine($"System resumed at {evt.ResumedAt:O}");
    // Reinitialize services
}
```

---

## 5. ASYNC PATTERNS

### Current Issues

**1. Async void methods** (21 instances):
```csharp
// Pages/ScalingPage.xaml.cs
private async void ApplyButton_Click(object sender, RoutedEventArgs e)
{
    // Exceptions won't be caught by try-catch at caller!
}

// Services/Power/PowerProfileService.cs
private async void OnGameDetected(object? sender, GameInfo? gameInfo)
{
    // No way to track completion or failures
}
```

**2. ContinueWith without ConfigureAwait:**
```csharp
Task.Delay(1000).ContinueWith(_ =>
{
    MainWindow?.DispatcherQueue.TryEnqueue(() => { /* ... */ });
});
// This can cause deadlocks on UI thread in some scenarios
```

**3. Task.Run on dispatcher methods:**
```csharp
reinitTasks.Add(Task.Run(async () =>
{
    var result = await FanControlService.ReinitializeAfterResumeAsync();
    // Async context switching unclear
}));
```

### Issues Identified

1. **Async void exceptions unobservable** - Exceptions in async void methods crash the app or go silent
2. **No ConfigureAwait** - All async calls might capture UI context unnecessarily
3. **Mixed Task.Run and async/await** - Unclear execution context
4. **No cancellation tokens** - Long-running operations can't be cancelled
5. **Fire-and-forget tasks** - Task results ignored, errors lost

### Recommendation 7: Establish Async Patterns

**Rule 1: Never use async void except for event handlers:**

```csharp
// ❌ WRONG - Exceptions lost
private async void SaveSettings()
{
    await _settingsService.SaveAsync();
}

// ✅ CORRECT - Exceptions propagate
private async Task SaveSettingsAsync()
{
    await _settingsService.SaveAsync();
}

// ✅ CORRECT - Event handler (special case)
private async void SaveButton_Click(object sender, RoutedEventArgs e)
{
    try
    {
        await SaveSettingsAsync();
    }
    catch (Exception ex)
    {
        Debug.WriteLine($"Save failed: {ex.Message}");
    }
}
```

**Rule 2: Always use ConfigureAwait in libraries:**

```csharp
public class TdpMonitorService
{
    private async Task MonitorLoopAsync()
    {
        while (!_disposed)
        {
            try
            {
                var result = await _tdpService.GetCurrentTdpAsync()
                    .ConfigureAwait(false);  // Don't need UI context
                    
                await Task.Delay(1000)
                    .ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Monitor error: {ex.Message}");
            }
        }
    }
}
```

**Rule 3: Use async all the way down:**

```csharp
// ❌ WRONG - Mixes async/sync
private async void InitializeAsync()
{
    var device = _deviceService.DetectDevice();  // Sync call blocks!
    var config = await _configService.LoadAsync();
}

// ✅ CORRECT - Consistent async
private async Task InitializeAsync()
{
    var device = await _deviceService.DetectDeviceAsync();
    var config = await _configService.LoadAsync();
}
```

**Rule 4: Implement proper cancellation:**

```csharp
public class TdpMonitorService : IDisposable
{
    private CancellationTokenSource _cts = new();
    
    public async Task StartMonitoringAsync()
    {
        while (!_cts.Token.IsCancellationRequested)
        {
            try
            {
                await CheckTdpAsync(_cts.Token)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }
    
    public void Dispose()
    {
        _cts.Cancel();
        _cts.Dispose();
    }
}
```

**Rule 5: Track background tasks:**

```csharp
public class ServiceBase
{
    protected List<Task> _backgroundTasks = new();
    
    protected async Task TrackTaskAsync(Task task)
    {
        _backgroundTasks.Add(task);
        try
        {
            await task.ConfigureAwait(false);
        }
        finally
        {
            _backgroundTasks.Remove(task);
        }
    }
    
    public async Task ShutdownAsync()
    {
        await Task.WhenAll(_backgroundTasks);
    }
}
```

---

## 6. ERROR HANDLING STRATEGY

### Current Approach

**DebugLogger service** (basic):
```csharp
public static class DebugLogger
{
    public static void Log(string message, string category = "DEBUG")
    {
        lock (LogLock)
        {
            System.Diagnostics.Debug.WriteLine(logEntry);
            File.AppendAllText(LogPath, logEntry + Environment.NewLine);
        }
    }
}
```

**Scattered try-catch blocks:**
```csharp
try
{
    // Attempt operation
}
catch (Exception ex)
{
    System.Diagnostics.Debug.WriteLine($"Error: {ex.Message}");
    return FanControlResult.FailureResult($"Error: {ex.Message}", ex);
}
```

**Result pattern** (emerging):
```csharp
public class FanControlResult
{
    public bool Success { get; set; }
    public string Message { get; set; }
    public Exception? Exception { get; set; }
}
```

### Issues Identified

1. **No structured logging** - All logs are strings, can't query or filter
2. **Exception information lost** - Only message logged, stack traces discarded
3. **Inconsistent error reporting** - Some methods return Result, others throw
4. **No error aggregation** - Can't see error patterns
5. **Limited context** - No request tracing across service calls
6. **Silent failures** - Some errors swallowed without notification

### Recommendation 8: Structured Logging & Error Handling

**Create structured logger interface:**

```csharp
public enum LogLevel
{
    Debug,
    Information,
    Warning,
    Error,
    Critical
}

public interface ILogger
{
    void Log(LogLevel level, string message, Exception? exception = null, 
        Dictionary<string, object>? properties = null);
    
    void LogDebug(string message, Dictionary<string, object>? properties = null)
        => Log(LogLevel.Debug, message, null, properties);
    
    void LogInformation(string message, Dictionary<string, object>? properties = null)
        => Log(LogLevel.Information, message, null, properties);
    
    void LogWarning(string message, Exception? exception = null, 
        Dictionary<string, object>? properties = null)
        => Log(LogLevel.Warning, message, exception, properties);
    
    void LogError(string message, Exception exception, 
        Dictionary<string, object>? properties = null)
        => Log(LogLevel.Error, message, exception, properties);
}

public class Logger : ILogger
{
    private readonly string _category;
    private readonly string _logPath;
    private readonly object _lock = new();
    
    public Logger(string category)
    {
        _category = category;
        _logPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "HUDRA", "logs", $"{DateTime.Now:yyyy-MM-dd}.log");
    }
    
    public void Log(LogLevel level, string message, Exception? exception = null,
        Dictionary<string, object>? properties = null)
    {
        lock (_lock)
        {
            var entry = new
            {
                timestamp = DateTime.UtcNow,
                level = level.ToString(),
                category = _category,
                message = message,
                exception = exception?.ToString(),
                properties = properties ?? new()
            };
            
            var json = JsonSerializer.Serialize(entry);
            
            // Log to file
            Directory.CreateDirectory(Path.GetDirectoryName(_logPath)!);
            File.AppendAllText(_logPath, json + Environment.NewLine);
            
            // Log to debug output
            Debug.WriteLine(json);
        }
    }
}
```

**Unified result pattern:**

```csharp
public abstract class Result
{
    public bool Success { get; protected set; }
    public string Message { get; protected set; }
    public Exception? Exception { get; protected set; }
    public Dictionary<string, object> Context { get; protected set; } = new();
    
    public static Success Ok(string message = "") => new(message);
    public static Failure Fail(string message, Exception? ex = null) => new(message, ex);
}

public class Success : Result
{
    public Success(string message = "")
    {
        Success = true;
        Message = message;
    }
}

public class Failure : Result
{
    public Failure(string message, Exception? ex = null)
    {
        Success = false;
        Message = message;
        Exception = ex;
    }
}

public class Result<T> : Result
{
    public T? Data { get; set; }
    
    public static Result<T> Ok(T data, string message = "")
        => new() { Success = true, Data = data, Message = message };
    
    public static Result<T> Fail(string message, Exception? ex = null)
        => new() { Success = false, Message = message, Exception = ex };
}
```

**Usage in services:**

```csharp
public class FanControlService
{
    private readonly ILogger _logger;
    
    public async Task<Result> SetFanModeAsync(FanControlMode mode)
    {
        var context = new Dictionary<string, object>
        {
            { "device", DeviceInfo },
            { "target_mode", mode.ToString() }
        };
        
        try
        {
            _logger.LogInformation($"Attempting to set fan mode to {mode}",
                new() { { "operation", "fan_mode_change" } });
            
            if (!IsDeviceAvailable)
            {
                _logger.LogWarning("Fan control device not available");
                return Result.Fail("No fan control device available");
            }
            
            bool success = _device!.SetFanControl(mode);
            if (!success)
            {
                _logger.LogWarning($"Failed to set fan mode to {mode}", context);
                return Result.Fail($"Device rejected fan mode {mode}");
            }
            
            CurrentMode = mode;
            _logger.LogInformation($"Fan mode changed to {mode}", context);
            return Result.Ok($"Fan mode set to {mode}");
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error setting fan mode: {ex.Message}", ex, context);
            return Result.Fail($"Error setting fan mode: {ex.Message}", ex);
        }
    }
}
```

---

## 7. ARCHITECTURAL IMPROVEMENTS SUMMARY

### High Priority (1-2 weeks)

| Issue | Current | Solution | Benefit |
|-------|---------|----------|---------|
| Service Instantiation | Manual in App/MainWindow | Service Locator | Centralized, testable, flexible |
| Async void methods | 21 instances, exceptions lost | Rule: async Task only | Observable exceptions, proper error handling |
| State Synchronization | Scattered, implicit | Observable AppState | Reactive UI, consistent state |
| Error Handling | Inconsistent try-catch | Structured logging + Result pattern | Traceable issues, consistent handling |

### Medium Priority (2-4 weeks)

| Issue | Current | Solution | Benefit |
|-------|---------|----------|---------|
| Event Communication | Direct service events | Event Aggregator | Loose coupling, centralized flow |
| Configuration | Scattered constants | Configuration objects + JSON | Named constants, environment-specific config |
| Component Size | MainWindow: 1500 lines | Extract services/behaviors | Maintainability, testability |
| Interface Abstraction | Only 1 interface | Service interfaces | Dependency injection, testing |

### Long-term (1-3 months)

| Issue | Current | Solution | Benefit |
|-------|---------|----------|---------|
| DI Container | Manual Service Locator | MS.Extensions.DependencyInjection | Industry standard, better tooling |
| Test Coverage | Minimal testability | Testable architecture | Confidence, regression prevention |
| Documentation | Scattered docs | Architecture Decision Records | Knowledge sharing, consistency |

---

## 8. IMPLEMENTATION ROADMAP

### Phase 1: Foundation (Week 1-2)
1. Create ServiceContainer and migrate service instantiation
2. Fix all async void methods → async Task
3. Create AppState with INotifyPropertyChanged
4. Implement structured Logger interface

### Phase 2: Integration (Week 3-4)
1. Create service interfaces from implementations
2. Implement EventAggregator
3. Migrate old event patterns to EventAggregator
4. Create configuration objects and JSON loader

### Phase 3: Refactoring (Week 5-8)
1. Break down MainWindow (1500 lines) into behaviors
2. Extract page composition logic
3. Improve test coverage
4. Migrate to MS.Extensions.DependencyInjection

---

## 9. CODE EXAMPLES & PATTERNS

### Before: Manual DI with Tight Coupling

```csharp
// App.xaml.cs
public partial class App : Application
{
    public FanControlService? FanControlService { get; private set; }
    public TdpMonitorService? TdpMonitor { get; private set; }
    
    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        MainWindow = new MainWindow();
        TdpMonitor = new TdpMonitorService(MainWindow.DispatcherQueue);
        FanControlService = new FanControlService(MainWindow.DispatcherQueue);
        // ...
    }
}

// MainWindow.xaml.cs - accessing App services
var app = (App)Application.Current;
var tdpMonitor = app.TdpMonitor; // Tight coupling!
```

### After: Service Locator Pattern

```csharp
// ServiceContainer.cs
public class ServiceContainer
{
    private static readonly Lazy<ServiceContainer> _instance = 
        new(() => new ServiceContainer());
    public static ServiceContainer Instance => _instance.Value;
    
    private readonly Dictionary<Type, Func<object>> _factories = new();
    
    public T? GetService<T>() where T : class
    {
        if (_factories.TryGetValue(typeof(T), out var factory))
            return factory() as T;
        return null;
    }
}

// App.xaml.cs
protected override async void OnLaunched(LaunchActivatedEventArgs args)
{
    MainWindow = new MainWindow();
    
    // Register services
    ServiceContainer.Instance.Register(() =>
        new TdpMonitorService(MainWindow.DispatcherQueue));
    ServiceContainer.Instance.Register(() =>
        new FanControlService(MainWindow.DispatcherQueue));
}

// MainWindow.xaml.cs
var tdpMonitor = ServiceContainer.Instance.GetService<TdpMonitorService>();
```

---

## Conclusion

HUDRA has a solid foundation with good separation of concerns through services. The main opportunities for improvement are:

1. **Centralize dependency management** - Move from scattered instantiation to container pattern
2. **Establish async conventions** - Eliminate async void, use ConfigureAwait consistently  
3. **Formalize state management** - Observable state instead of scattered static/instance state
4. **Improve error visibility** - Structured logging and consistent Result patterns
5. **Reduce coupling** - Event aggregator and service interfaces

These improvements will make the codebase more maintainable, testable, and easier to extend as the application grows.
