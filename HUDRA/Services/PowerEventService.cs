using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Dispatching;
using WinRT.Interop;

namespace HUDRA.Services
{
    public class PowerEventService : IDisposable
    {
        private readonly DispatcherQueue _dispatcher;
        private readonly Window _window;
        private IntPtr _windowHandle;
        private WNDPROC _wndProcDelegate;
        private IntPtr _originalWndProc;
        private bool _disposed = false;

        // Debouncing fields
        private bool _isResumeEventPending = false;
        private readonly object _debounceLock = new object();

        // Windows Power Management Messages
        private const int WM_POWERBROADCAST = 0x0218;
        private const int PBT_APMRESUMEAUTOMATIC = 0x0012;
        private const int PBT_APMRESUMESUSPEND = 0x0007;
        private const int PBT_APMSUSPEND = 0x0004;

        // Windows API constants
        private const int GWL_WNDPROC = -4;

        public event EventHandler? HibernationResumeDetected;
        public event EventHandler? SuspendDetected;

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr CallWindowProc(IntPtr lpPrevWndFunc, IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

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
                    HandlePowerEvent(powerEvent);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"⚠️ Error in power event handling: {ex.Message}");
            }

            // Call the original window procedure
            return CallWindowProc(_originalWndProc, hWnd, msg, wParam, lParam);
        }

        private void HandlePowerEvent(int powerEvent)
        {
            switch (powerEvent)
            {
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