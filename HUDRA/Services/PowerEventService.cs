using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using HUDRA.Services.Power;
using Microsoft.UI.Xaml;
using Microsoft.UI.Dispatching;
using WinRT.Interop;

namespace HUDRA.Services
{
    /// <summary>
    /// Carries the power source the machine switched to.
    /// </summary>
    public class PowerSourceChangedEventArgs : EventArgs
    {
        public bool IsOnAc { get; }

        public PowerSourceChangedEventArgs(bool isOnAc)
        {
            IsOnAc = isOnAc;
        }
    }

    public class PowerEventService : IDisposable
    {
        private readonly DispatcherQueue _dispatcher;
        private readonly Window _window;
        private IntPtr _windowHandle;
        private WNDPROC _wndProcDelegate;
        private IntPtr _originalWndProc;
        private IntPtr _powerSourceNotifyHandle;
        private bool _disposed = false;

        // Debouncing fields
        private bool _isResumeEventPending = false;
        private readonly object _debounceLock = new object();

        // Only touched from the window procedure, i.e. the UI thread.
        private readonly PowerSourceTransitionTracker _powerSourceTracker = new();

        // Windows Power Management Messages
        private const int WM_POWERBROADCAST = 0x0218;
        private const int PBT_APMRESUMEAUTOMATIC = 0x0012;
        private const int PBT_APMRESUMESUSPEND = 0x0007;
        private const int PBT_APMSUSPEND = 0x0004;
        private const int PBT_POWERSETTINGCHANGE = 0x8013;

        // Windows API constants
        private const int GWL_WNDPROC = -4;
        private const int DEVICE_NOTIFY_WINDOW_HANDLE = 0;

        // GUID_ACDC_POWER_SOURCE - fires on cable plug/unplug only. Deliberately not
        // PBT_APMPOWERSTATUSCHANGE, which also fires on battery-percentage ticks.
        private static readonly Guid GUID_ACDC_POWER_SOURCE =
            new Guid("5d3e9a59-e9d5-4b00-a6bd-ff34ff516548");

        public event EventHandler? HibernationResumeDetected;
        public event EventHandler? SuspendDetected;
        public event EventHandler<PowerSourceChangedEventArgs>? PowerSourceChanged;

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr CallWindowProc(IntPtr lpPrevWndFunc, IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr RegisterPowerSettingNotification(IntPtr hRecipient, ref Guid PowerSettingGuid, int Flags);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool UnregisterPowerSettingNotification(IntPtr Handle);

        // The native struct ends with a variable-length Data payload. Only the first
        // byte is declared, which is all GUID_ACDC_POWER_SOURCE needs: its payload is a
        // little-endian DWORD whose value never exceeds 2. Declaring more would read
        // past the payload of any other (shorter) power setting.
        [StructLayout(LayoutKind.Sequential)]
        private struct POWERBROADCAST_SETTING
        {
            public Guid PowerSetting;
            public uint DataLength;
            public byte Data;
        }

        private delegate IntPtr WNDPROC(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        public PowerEventService(Window window, DispatcherQueue dispatcher)
        {
            _window = window ?? throw new ArgumentNullException(nameof(window));
            _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
            
            InitializePowerEventHandling();
        }

        private void InitializePowerEventHandling()
        {
            try
            {
                _windowHandle = WindowNative.GetWindowHandle(_window);
                if (_windowHandle == IntPtr.Zero)
                {
                    System.Diagnostics.Debug.WriteLine("⚠️ Failed to get window handle for power event handling");
                    return;
                }

                // Create delegate and keep reference to prevent garbage collection
                _wndProcDelegate = new WNDPROC(WndProc);
                
                // Subclass the window to intercept messages
                _originalWndProc = GetWindowLongPtr(_windowHandle, GWL_WNDPROC);
                SetWindowLongPtr(_windowHandle, GWL_WNDPROC, Marshal.GetFunctionPointerForDelegate(_wndProcDelegate));

                // Must come after subclassing: Windows delivers an initial notification
                // as soon as this returns, and it has to reach our window procedure.
                var acdcGuid = GUID_ACDC_POWER_SOURCE;
                _powerSourceNotifyHandle = RegisterPowerSettingNotification(
                    _windowHandle, ref acdcGuid, DEVICE_NOTIFY_WINDOW_HANDLE);
                if (_powerSourceNotifyHandle == IntPtr.Zero)
                {
                    DebugLogger.Log(
                        $"Failed to register AC/DC power source notification (error {Marshal.GetLastWin32Error()})",
                        "PWR");
                }

                System.Diagnostics.Debug.WriteLine("⚡ PowerEventService initialized successfully");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"⚠️ Failed to initialize power event handling: {ex.Message}");
            }
        }

        private IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
        {
            try
            {
                if (msg == WM_POWERBROADCAST)
                {
                    int powerEvent = wParam.ToInt32();
                    HandlePowerEvent(powerEvent, lParam);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"⚠️ Error in power event handling: {ex.Message}");
            }

            // Call the original window procedure
            return CallWindowProc(_originalWndProc, hWnd, msg, wParam, lParam);
        }

        private void HandlePowerEvent(int powerEvent, IntPtr lParam)
        {
            switch (powerEvent)
            {
                case PBT_POWERSETTINGCHANGE:
                    HandlePowerSettingChange(lParam);
                    break;

                case PBT_APMRESUMEAUTOMATIC:
                    System.Diagnostics.Debug.WriteLine("⚡ Hibernation/Sleep resume detected (automatic)");
                    OnHibernationResumeDetectedDebounced();
                    break;

                case PBT_APMRESUMESUSPEND:
                    System.Diagnostics.Debug.WriteLine("⚡ Hibernation/Sleep resume detected (user-initiated)");
                    OnHibernationResumeDetectedDebounced();
                    break;

                case PBT_APMSUSPEND:
                    System.Diagnostics.Debug.WriteLine("⚡ System suspend detected");
                    OnSuspendDetected();
                    break;

                default:
                    System.Diagnostics.Debug.WriteLine($"⚡ Other power event: {powerEvent:X}");
                    break;
            }
        }

        private void HandlePowerSettingChange(IntPtr lParam)
        {
            if (lParam == IntPtr.Zero)
                return;

            var setting = Marshal.PtrToStructure<POWERBROADCAST_SETTING>(lParam);

            // Other power settings could arrive here if more registrations are ever
            // added; only the AC/DC source drives the tracker.
            if (setting.PowerSetting != GUID_ACDC_POWER_SOURCE || setting.DataLength < 1)
                return;

            if (!_powerSourceTracker.TryObserve(setting.Data, out var state))
                return;

            bool isOnAc = state == PowerSourceState.Ac;
            System.Diagnostics.Debug.WriteLine($"⚡ Power source changed to {(isOnAc ? "AC" : "DC")}");

            try
            {
                // Fire event on UI thread
                _dispatcher.TryEnqueue(() =>
                {
                    PowerSourceChanged?.Invoke(this, new PowerSourceChangedEventArgs(isOnAc));
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"⚠️ Error firing power source changed event: {ex.Message}");
            }
        }

        private void OnHibernationResumeDetectedDebounced()
        {
            lock (_debounceLock)
            {
                // If we already have a resume event pending, ignore this one completely
                if (_isResumeEventPending)
                {
                    System.Diagnostics.Debug.WriteLine("⚡ Hibernation resume event already pending - ignoring duplicate");
                    return;
                }

                // This is the first event - let it proceed
                _isResumeEventPending = true;
                System.Diagnostics.Debug.WriteLine("⚡ Starting hibernation resume debounce timer (3 seconds)");
                
                // Start debounced event (no cancellation token - let it complete)
                _ = Task.Run(async () =>
                {
                    try
                    {
                        // Wait 3 seconds to debounce multiple rapid events
                        await Task.Delay(3000);
                        
                        // Fire the actual reinitialization event
                        System.Diagnostics.Debug.WriteLine("⚡ Debounced hibernation resume event firing - starting reinitialization");
                        OnHibernationResumeDetected();
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"⚠️ Error in debounced hibernation resume: {ex.Message}");
                    }
                    finally
                    {
                        // Reset state to allow future hibernation events
                        lock (_debounceLock)
                        {
                            _isResumeEventPending = false;
                            System.Diagnostics.Debug.WriteLine("⚡ Hibernation resume debounce state reset");
                        }
                    }
                });
            }
        }

        private void OnHibernationResumeDetected()
        {
            try
            {
                // Fire event on UI thread
                _dispatcher.TryEnqueue(() =>
                {
                    HibernationResumeDetected?.Invoke(this, EventArgs.Empty);
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"⚠️ Error firing hibernation resume event: {ex.Message}");
            }
        }

        private void OnSuspendDetected()
        {
            try
            {
                // Fire event on UI thread
                _dispatcher.TryEnqueue(() =>
                {
                    SuspendDetected?.Invoke(this, EventArgs.Empty);
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"⚠️ Error firing suspend event: {ex.Message}");
            }
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                try
                {
                    // Unregister before the window procedure is restored, so no
                    // notification can be delivered to a proc we no longer own.
                    if (_powerSourceNotifyHandle != IntPtr.Zero)
                    {
                        UnregisterPowerSettingNotification(_powerSourceNotifyHandle);
                        _powerSourceNotifyHandle = IntPtr.Zero;
                    }

                    // Restore original window procedure
                    if (_windowHandle != IntPtr.Zero && _originalWndProc != IntPtr.Zero)
                    {
                        SetWindowLongPtr(_windowHandle, GWL_WNDPROC, _originalWndProc);
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"⚠️ Error disposing PowerEventService: {ex.Message}");
                }

                _disposed = true;
                System.Diagnostics.Debug.WriteLine("⚡ PowerEventService disposed");
            }
        }
    }
}