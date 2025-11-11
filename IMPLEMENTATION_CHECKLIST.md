# HUDRA Architecture Improvements - Implementation Checklist

## Phase 1: Foundation (Weeks 1-2) - LOW RISK

### 1.1 Service Locator Container [ ]
- [ ] Create `Services/ServiceContainer.cs` with registration/resolution methods
- [ ] Add unit tests for container functionality
- [ ] Document container usage patterns
- [ ] Create example registrations for key services

**Files to Create:**
- `Services/ServiceContainer.cs`
- `Services/ServiceContainerExtensions.cs`

**Estimated Time:** 8 hours

### 1.2 Fix Async Void Methods (21 instances) [ ]

**Pages affected:**
- [ ] `Pages/ScalingPage.xaml.cs` - ApplyButton_Click, RestoreButton_Click
- [ ] `Pages/SettingsPage.xaml.cs` - StartupToggle_Toggled, RefreshDatabaseButton_Click, ResetDatabaseButton_Click
- [ ] `App.xaml.cs` - OnLaunched, OnHibernationResumeDetected

**Controls affected:**
- [ ] `Controls/TdpPickerControl.xaml.cs` - LoadCurrentTdp
- [ ] `Controls/FanControlControl.xaml.cs` - Initialize
- [ ] `Controls/LosslessScalingControl.xaml.cs` - ApplyButton_Click, RestoreButton_Click
- [ ] `Controls/FanCurveControl.xaml.cs` - Initialize
- [ ] `Controls/PowerProfileControl.xaml.cs` - OnDefaultProfileSelectionChanged, OnGamingProfileSelectionChanged, OnCpuBoostToggled
- [ ] `Controls/FpsLimiterControl.xaml.cs` - Initialize, OnFpsLimitChanged

**Helpers affected:**
- [ ] `Helpers/AutoSetManager.cs` - OnTimerTick

**Services affected:**
- [ ] `Services/Power/PowerProfileService.cs` - OnGameDetected, OnGameStopped

**For each method:**
- [ ] Extract async Task method
- [ ] Keep event handler as async void wrapper with try-catch
- [ ] Add error logging
- [ ] Test exception handling

**Example Migration:**
```csharp
// BEFORE
private async void ApplyButton_Click(object sender, RoutedEventArgs e) { }

// AFTER
private async void ApplyButton_Click(object sender, RoutedEventArgs e)
{
    try { await ApplyChangesAsync(); }
    catch (Exception ex) { _logger.LogError("Apply failed", ex); }
}

private async Task ApplyChangesAsync() { /* implementation */ }
```

**Estimated Time:** 6 hours

### 1.3 Structured Logging Interface [ ]
- [ ] Create `Services/Logging/ILogger.cs` interface
- [ ] Create `Services/Logging/Logger.cs` implementation
- [ ] Create `Services/Logging/LogLevel.cs` enum
- [ ] Replace all `Debug.WriteLine()` calls in critical services:
  - [ ] `TdpMonitorService`
  - [ ] `FanControlService`
  - [ ] `TemperatureMonitorService`
  - [ ] `PowerEventService`
  - [ ] `TurboService`
- [ ] Create log directory in LocalApplicationData
- [ ] Implement JSON structured logging
- [ ] Add timestamps and categories

**Files to Create:**
- `Services/Logging/ILogger.cs`
- `Services/Logging/Logger.cs`
- `Services/Logging/LogLevel.cs`
- `Services/Logging/LogEntry.cs`

**Estimated Time:** 8 hours

### 1.4 Observable Application State [ ]
- [ ] Create `Services/AppState.cs` with INotifyPropertyChanged
- [ ] Identify all state properties to track:
  - [ ] CurrentTdp
  - [ ] CurrentTemperature
  - [ ] FanMode
  - [ ] IsGameRunning
  - [ ] ActivePowerProfile
  - [ ] BatteryPercentage
  - [ ] IsLosslessScalingEnabled
  - [ ] CurrentResolution
- [ ] Implement property change notifications
- [ ] Create property change event tracking
- [ ] Register AppState as singleton in ServiceContainer
- [ ] Update services to write state (e.g., TdpMonitorService)

**Files to Create:**
- `Services/AppState.cs`
- `Models/StateChangedEventArgs.cs`

**Estimated Time:** 10 hours

### ✅ Phase 1 Total: ~32 hours (1 week)

---

## Phase 2: Integration (Weeks 3-4) - MEDIUM RISK

### 2.1 Service Interfaces [ ]

**Create interfaces for these services:**
- [ ] `IServiceContainer.cs` (existing ServiceContainer)
- [ ] `ITdpMonitorService.cs` (TdpMonitorService)
- [ ] `ITemperatureMonitorService.cs` (TemperatureMonitorService)
- [ ] `IFanControlService.cs` (FanControlService)
- [ ] `ITDPService.cs` (TDPService)
- [ ] `IPowerProfileService.cs` (PowerProfileService)
- [ ] `IBatteryService.cs` (BatteryService)
- [ ] `IAudioService.cs` (AudioService)
- [ ] `IBrightnessService.cs` (BrightnessService)
- [ ] `IResolutionService.cs` (ResolutionService)
- [ ] `INavigationService.cs` (NavigationService)
- [ ] `IWindowManagementService.cs` (WindowManagementService)
- [ ] `ILosslessScalingService.cs` (LosslessScalingService)
- [ ] `IRtssFpsLimiterService.cs` (RtssFpsLimiterService)
- [ ] `IEnhancedGameDetectionService.cs` (EnhancedGameDetectionService)

**For each interface:**
- [ ] Extract public members to interface
- [ ] Ensure implementations implement interface
- [ ] Update ServiceContainer to support interface registration
- [ ] Create mock implementations for testing

**Estimated Time:** 10 hours

### 2.2 Event Aggregator [ ]
- [ ] Create `Services/Events/IEvent.cs` marker interface
- [ ] Create `Services/Events/IEventAggregator.cs` interface
- [ ] Create `Services/Events/EventAggregator.cs` implementation
- [ ] Create event classes:
  - [ ] `Events/TdpChangedEvent.cs`
  - [ ] `Events/GameDetectedEvent.cs`
  - [ ] `Events/GameStoppedEvent.cs`
  - [ ] `Events/HibernationResumedEvent.cs`
  - [ ] `Events/FanModeChangedEvent.cs`
  - [ ] `Events/PowerProfileChangedEvent.cs`
  - [ ] `Events/BatteryInfoChangedEvent.cs`
  - [ ] `Events/TemperatureChangedEvent.cs`
- [ ] Replace direct event subscriptions with aggregator
- [ ] Update services to publish to aggregator instead of raising events
- [ ] Register aggregator as singleton

**Files to Create:**
- `Services/Events/IEvent.cs`
- `Services/Events/IEventAggregator.cs`
- `Services/Events/EventAggregator.cs`
- `Services/Events/TdpChangedEvent.cs`
- `Services/Events/GameDetectedEvent.cs`
- (... additional event classes ...)

**Migration Example:**
```csharp
// BEFORE
public event EventHandler<TdpDriftEventArgs>? TdpDriftDetected;
TdpDriftDetected?.Invoke(this, new TdpDriftEventArgs(...));

// AFTER
_eventAggregator.Publish(new TdpChangedEvent { ... });
```

**Estimated Time:** 12 hours

### 2.3 Configuration Objects [ ]
- [ ] Create `Configuration/HudraConfiguration.cs` main config class
- [ ] Create config sub-classes:
  - [ ] `Configuration/TdpConfiguration.cs`
  - [ ] `Configuration/FanConfiguration.cs`
  - [ ] `Configuration/MonitorConfiguration.cs`
  - [ ] `Configuration/UiConfiguration.cs`
  - [ ] `Configuration/GameConfiguration.cs`
  - [ ] `Configuration/PowerConfiguration.cs`
- [ ] Move all magic numbers from code to configuration:
  - [ ] TDP drift threshold (2 → DriftThreshold)
  - [ ] Timer delays (1000ms → TimeSpan)
  - [ ] Check intervals (60s → TimeSpan)
  - [ ] Window dimensions
  - [ ] Hysteresis values
- [ ] Create `Configuration/ConfigurationLoader.cs` to load JSON
- [ ] Create `appsettings.json` with all defaults
- [ ] Add config validation
- [ ] Register configuration as singleton

**Files to Create:**
- `Configuration/HudraConfiguration.cs`
- `Configuration/TdpConfiguration.cs`
- `Configuration/FanConfiguration.cs`
- `Configuration/MonitorConfiguration.cs`
- `Configuration/UiConfiguration.cs`
- `Configuration/GameConfiguration.cs`
- `Configuration/PowerConfiguration.cs`
- `Configuration/ConfigurationLoader.cs`
- `appsettings.json`

**Estimated Time:** 8 hours

### 2.4 Settings Repository Pattern [ ]
- [ ] Create `Repositories/ISettingsRepository.cs` interface
- [ ] Create `Repositories/JsonSettingsRepository.cs` implementation
- [ ] Move persistence logic from SettingsService to repository
- [ ] Keep SettingsService as thin wrapper for compatibility
- [ ] Add encryption for sensitive settings
- [ ] Add settings validation
- [ ] Implement async Save/Load methods

**Files to Create:**
- `Repositories/ISettingsRepository.cs`
- `Repositories/JsonSettingsRepository.cs`
- `Repositories/SettingsValidator.cs`

**Estimated Time:** 8 hours

### ✅ Phase 2 Total: ~38 hours (2-3 weeks)

---

## Phase 3: Optimization (Weeks 5-8) - MEDIUM-HIGH RISK

### 3.1 Component Refactoring - MainWindow [ ]

**Target:** Reduce MainWindow.xaml.cs from 1500 to <500 lines

**Extract Behaviors:**
- [ ] Create `Behaviors/WindowDragBehavior.cs` - Window drag handling
- [ ] Create `Behaviors/GamepadNavigationBehavior.cs` - Gamepad input handling
- [ ] Create `Behaviors/WindowVisibilityBehavior.cs` - Window show/hide logic
- [ ] Create `Behaviors/BatteryMonitoringBehavior.cs` - Battery updates
- [ ] Create `Behaviors/PowerEventBehavior.cs` - Power event handling
- [ ] Create `Behaviors/FpsLimiterBehavior.cs` - FPS limiter setup
- [ ] Create `Behaviors/GameDetectionBehavior.cs` - Game detection subscriptions

**Extract ViewModels:**
- [ ] Create `ViewModels/MainWindowViewModel.cs` - Main window state
- [ ] Create `ViewModels/BatteryViewModel.cs` - Battery display state
- [ ] Create `ViewModels/PowerProfileViewModel.cs` - Power profile state
- [ ] Create `ViewModels/FpsLimiterViewModel.cs` - FPS limiter state

**Extract Helpers:**
- [ ] Move DPI scaling logic to behavior
- [ ] Move window management subscriptions
- [ ] Move battery monitor subscriptions

**Estimated Time:** 16 hours

### 3.2 Add Unit Tests [ ]

**Critical paths to test:**
- [ ] ServiceContainer registration/resolution
- [ ] AppState property changes
- [ ] EventAggregator publish/subscribe
- [ ] Logger message formatting
- [ ] Configuration loading/validation
- [ ] TdpMonitorService drift detection
- [ ] FanControlService mode switching
- [ ] TemperatureMonitorService readings
- [ ] SettingsRepository persistence

**Create test project:**
- [ ] `HUDRA.Tests/HUDRA.Tests.csproj`
- [ ] `HUDRA.Tests/Services/ServiceContainerTests.cs`
- [ ] `HUDRA.Tests/Services/AppStateTests.cs`
- [ ] `HUDRA.Tests/Services/EventAggregatorTests.cs`
- [ ] `HUDRA.Tests/Services/LoggerTests.cs`
- [ ] `HUDRA.Tests/Configuration/ConfigurationLoaderTests.cs`

**Target coverage:** >70% of critical services

**Estimated Time:** 20 hours

### 3.3 Migrate to MS.Extensions.DependencyInjection [ ]

- [ ] Add NuGet: `Microsoft.Extensions.DependencyInjection`
- [ ] Create `ServiceCollectionExtensions.cs`
- [ ] Migrate ServiceContainer to use built-in provider
- [ ] Update all registrations
- [ ] Test existing functionality
- [ ] Remove custom ServiceContainer (keep as adapter if needed)
- [ ] Update documentation

**Files to Modify:**
- [ ] `App.xaml.cs` - Service registration
- [ ] `MainWindow.xaml.cs` - Service injection
- [ ] All page constructors - Constructor injection
- [ ] All control constructors - Constructor injection

**Estimated Time:** 12 hours

### 3.4 Architecture Documentation [ ]

- [ ] Create `Architecture/DECISIONS.md` - Architecture Decision Records
  - [ ] Why Service Locator pattern?
  - [ ] Why Event Aggregator?
  - [ ] Why Observable State?
  - [ ] Configuration strategy decisions
- [ ] Create `Architecture/PATTERNS.md` - Common patterns guide
  - [ ] How to create a new service
  - [ ] How to handle async operations
  - [ ] How to log errors
  - [ ] How to publish events
  - [ ] How to access state
- [ ] Create `Architecture/MIGRATION.md` - Step-by-step migration guide
- [ ] Update `README.md` with architecture overview
- [ ] Create diagrams (ASCII or Mermaid)

**Estimated Time:** 8 hours

### ✅ Phase 3 Total: ~56 hours (3-4 weeks)

---

## Overall Implementation Timeline

| Phase | Duration | Risk | Hours | Start Date | End Date |
|-------|----------|------|-------|-----------|----------|
| 1: Foundation | 1 week | Low | 32 | Week 1 | Week 2 |
| 2: Integration | 2 weeks | Medium | 38 | Week 3 | Week 4 |
| 3: Optimization | 4 weeks | Medium-High | 56 | Week 5 | Week 8 |
| **TOTAL** | **8 weeks** | **Medium** | **126** | | |

**Development Team:** 1-2 developers
**Recommended Velocity:** 20-30 hours/week (0.5-0.75 FTE)

---

## Testing Checklist

### Phase 1 Testing
- [ ] All async void conversions compile and run
- [ ] ServiceContainer can register/retrieve services
- [ ] AppState property changes fire PropertyChanged
- [ ] Logging writes to file without exceptions
- [ ] No breaking changes to existing functionality

### Phase 2 Testing
- [ ] All service interfaces are implemented correctly
- [ ] EventAggregator publishes/subscribes without issues
- [ ] Configuration loads from appsettings.json
- [ ] Settings repository persists values
- [ ] Old event code still works for compatibility

### Phase 3 Testing
- [ ] Unit tests pass (70%+ coverage)
- [ ] MainWindow <500 lines
- [ ] Behaviors extracted and testable
- [ ] ViewModels work with services
- [ ] MS.Extensions.DependencyInjection fully integrated
- [ ] No memory leaks from event subscriptions

---

## Risk Mitigation

### Low Risk Items
- Service Locator (additive, doesn't break existing)
- Structured Logging (additive, doesn't break existing)
- Configuration objects (can coexist with existing)

### Medium Risk Items
- Async void conversions (requires careful testing)
- Event Aggregator migration (requires updating many subscriptions)
- AppState (changes state access pattern)

### High Risk Items
- Component refactoring (structural changes)
- Test infrastructure (new tooling/patterns)
- Full DI migration (architectural change)

**Mitigation Strategies:**
1. Implement in feature branches
2. Keep old code working in parallel (adapter pattern)
3. Extensive manual testing before merging
4. Gradual rollout (not all services at once)
5. Frequent integration with main branch

---

## Success Metrics

### Code Quality
- [ ] Cyclomatic complexity of MainWindow <10
- [ ] SettingsService <200 lines
- [ ] Average service <300 lines
- [ ] All service interfaces defined
- [ ] 0 async void methods (except event handlers)

### Testing
- [ ] 70%+ code coverage on services
- [ ] All critical paths tested
- [ ] Unit tests for configuration loading
- [ ] Unit tests for event aggregator
- [ ] Integration tests for service startup

### Architecture
- [ ] ServiceContainer used throughout
- [ ] EventAggregator handles all events
- [ ] AppState for all UI-relevant state
- [ ] Configuration in JSON
- [ ] Clear separation of concerns

### Documentation
- [ ] Architecture Decision Records created
- [ ] Pattern guide written
- [ ] Migration guide written
- [ ] README updated
- [ ] Code comments updated

---

## Notes & Considerations

### Dependencies to Add
```xml
<ItemGroup>
  <PackageReference Include="Microsoft.Extensions.DependencyInjection" Version="8.0.0" />
  <PackageReference Include="Microsoft.Extensions.Configuration.Json" Version="8.0.0" />
  <PackageReference Include="xunit" Version="2.6.2" />
  <PackageReference Include="xunit.runner.visualstudio" Version="2.5.0" />
  <PackageReference Include="Moq" Version="4.20.0" />
</ItemGroup>
```

### Git Strategy
- Create feature branch: `feature/architecture-refactor`
- Small, reviewable commits:
  - `feat(services): Add ServiceContainer`
  - `refactor(async): Convert async void to Task`
  - `feat(logging): Add structured logger`
  - etc.
- Frequent PRs instead of one giant PR
- Maintain main branch stability

### Backward Compatibility
- Old event patterns still work (via EventAggregator bridge)
- SettingsService static methods remain available
- All public APIs unchanged
- Gradual migration possible

---

## Questions to Resolve

- [ ] Should logging be synchronous or async?
- [ ] How to handle old code during migration?
- [ ] Create new test project or add to existing?
- [ ] How much telemetry/detailed logging needed?
- [ ] Should configuration be per-device?
- [ ] Performance implications of event aggregator?

---

## Approval Sign-off

- [ ] Architecture Lead review & approval
- [ ] Team consensus on timeline
- [ ] Resource allocation confirmed
- [ ] Risk mitigation strategies accepted
- [ ] Success criteria agreed upon

