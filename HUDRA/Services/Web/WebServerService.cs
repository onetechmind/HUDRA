using System;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using HUDRA.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;

namespace HUDRA.Services.Web
{
    /// <summary>
    /// Manages the embedded ASP.NET Core Kestrel web server lifecycle.
    /// Runs in-process alongside the WinUI 3 application, sharing existing service instances.
    /// </summary>
    public class WebServerService : IDisposable
    {
        private WebApplication? _app;
        private readonly ServiceBridge _bridge;
        private CancellationTokenSource? _cts;
        private bool _disposed;
        private int _port;

        // Event subscriptions (stored for cleanup)
        private EventHandler<TemperatureChangedEventArgs>? _tempHandler;
        private EventHandler<BatteryInfo>? _batteryHandler;

        // Periodic status broadcast for native→web sync
        private Timer? _statusTimer;
        private readonly SemaphoreSlim _broadcastLock = new(1, 1);

        public bool IsRunning => _app != null;
        public int Port => _port;

        public WebServerService(ServiceBridge bridge)
        {
            _bridge = bridge;
        }

        public async Task StartAsync(int port)
        {
            if (_app != null)
            {
                Debug.WriteLine("[WebRemote] Server already running, stopping first...");
                await StopAsync();
            }

            _port = port;
            _cts = new CancellationTokenSource();

            try
            {
                var builder = WebApplication.CreateSlimBuilder();

                // Configure Kestrel to listen on all interfaces
                builder.WebHost.ConfigureKestrel(options =>
                {
                    options.Listen(IPAddress.Any, port);
                });

                // Add SignalR
                builder.Services.AddSignalR();

                // Register ServiceBridge as singleton
                builder.Services.AddSingleton(_bridge);

                _app = builder.Build();

                // Auth middleware
                _app.UseMiddleware<WebAuthMiddleware>();

                // Serve static files from wwwroot
                var wwwrootPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Services", "Web", "wwwroot");
                if (System.IO.Directory.Exists(wwwrootPath))
                {
                    var fileProvider = new PhysicalFileProvider(wwwrootPath);

                    // UseDefaultFiles must come BEFORE UseStaticFiles for index.html rewriting
                    _app.UseDefaultFiles(new DefaultFilesOptions
                    {
                        FileProvider = fileProvider,
                        DefaultFileNames = new[] { "index.html" }
                    });

                    _app.UseStaticFiles(new StaticFileOptions
                    {
                        FileProvider = fileProvider,
                        RequestPath = ""
                    });
                }

                // Map SignalR hub
                _app.MapHub<HudraHub>("/hudrahub");

                // Map API endpoints
                HudraApiEndpoints.MapEndpoints(_app, _bridge);

                // Subscribe to service events for SignalR broadcast
                SubscribeToServiceEvents();

                // Start the server (StartAsync begins listening; RunAsync blocks which we don't want)
                await _app.StartAsync(_cts.Token);

                // Periodic status broadcast (every 3s) for native→web sync
                _statusTimer = new Timer(_ => _ = BroadcastControlStateAsync(), null,
                    TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(3));

                // Immediate broadcast when web API mutates state
                _bridge.WebMutationOccurred += OnWebMutation;

                // Add firewall rule (HUDRA runs as admin)
                EnsureFirewallRule(port);

                Debug.WriteLine($"[WebRemote] Server started on port {port}");
                Debug.WriteLine($"[WebRemote] Access at http://{GetLocalIpAddress()}:{port}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[WebRemote] Failed to start server: {ex.Message}");
                _app = null;
                throw;
            }
        }

        public async Task StopAsync()
        {
            if (_app == null) return;

            try
            {
                _statusTimer?.Dispose();
                _statusTimer = null;
                _bridge.WebMutationOccurred -= OnWebMutation;
                UnsubscribeFromServiceEvents();
                _cts?.Cancel();

                await _app.StopAsync();
                await _app.DisposeAsync();

                _app = null;
                _cts?.Dispose();
                _cts = null;

                Debug.WriteLine("[WebRemote] Server stopped");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[WebRemote] Error stopping server: {ex.Message}");
                _app = null;
            }
        }

        private void SubscribeToServiceEvents()
        {
            // Temperature updates → SignalR broadcast
            var tempMonitor = _bridge.GetTemperatureMonitor();
            if (tempMonitor != null)
            {
                _tempHandler = (_, args) =>
                {
                    BroadcastAsync("TemperatureUpdated", new
                    {
                        cpu = Math.Round(args.TemperatureData.CpuTemperature, 1),
                        gpu = Math.Round(args.TemperatureData.GpuTemperature, 1)
                    });
                };
                tempMonitor.TemperatureChanged += _tempHandler;
            }

            // Battery updates → SignalR broadcast
            var battery = _bridge.GetBatteryService();
            if (battery != null)
            {
                _batteryHandler = (_, info) =>
                {
                    BroadcastAsync("BatteryUpdated", new
                    {
                        percent = info.Percent,
                        isCharging = info.IsCharging,
                        onAc = info.OnAc
                    });
                };
                battery.BatteryInfoUpdated += _batteryHandler;
            }
        }

        private void UnsubscribeFromServiceEvents()
        {
            var tempMonitor = _bridge.GetTemperatureMonitor();
            if (tempMonitor != null && _tempHandler != null)
                tempMonitor.TemperatureChanged -= _tempHandler;

            var battery = _bridge.GetBatteryService();
            if (battery != null && _batteryHandler != null)
                battery.BatteryInfoUpdated -= _batteryHandler;
        }

        private void BroadcastAsync(string method, object data)
        {
            if (_app == null) return;

            try
            {
                var hubContext = _app.Services.GetService<IHubContext<HudraHub>>();
                hubContext?.Clients.All.SendAsync(method, data);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[WebRemote] Broadcast error: {ex.Message}");
            }
        }

        private void OnWebMutation() => _ = BroadcastControlStateAsync();

        private async Task BroadcastControlStateAsync()
        {
            if (_app == null) return;
            if (!_broadcastLock.Wait(0)) return; // Skip if already broadcasting
            try
            {
                var snapshot = await _bridge.BuildControlStateSnapshotAsync();
                BroadcastAsync("StatusUpdated", snapshot);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[WebRemote] Status broadcast error: {ex.Message}");
            }
            finally
            {
                _broadcastLock.Release();
            }
        }

        public static string GetLocalIpAddress()
        {
            try
            {
                // Find the first non-loopback IPv4 address on an active interface
                foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (ni.OperationalStatus != OperationalStatus.Up) continue;
                    if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;

                    var props = ni.GetIPProperties();
                    foreach (var addr in props.UnicastAddresses)
                    {
                        if (addr.Address.AddressFamily == AddressFamily.InterNetwork)
                        {
                            return addr.Address.ToString();
                        }
                    }
                }
            }
            catch { }
            return "127.0.0.1";
        }

        private static void EnsureFirewallRule(int port)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "netsh",
                    Arguments = $"advfirewall firewall show rule name=\"HUDRA Web Remote\"",
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var check = Process.Start(psi);
                var output = check?.StandardOutput.ReadToEnd() ?? "";
                check?.WaitForExit();

                if (!output.Contains("HUDRA Web Remote"))
                {
                    var addPsi = new ProcessStartInfo
                    {
                        FileName = "netsh",
                        Arguments = $"advfirewall firewall add rule name=\"HUDRA Web Remote\" dir=in action=allow protocol=TCP localport={port}",
                        UseShellExecute = false,
                        CreateNoWindow = true
                    };

                    using var add = Process.Start(addPsi);
                    add?.WaitForExit();
                    Debug.WriteLine($"[WebRemote] Firewall rule added for port {port}");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[WebRemote] Failed to configure firewall: {ex.Message}");
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            _statusTimer?.Dispose();
            _bridge.WebMutationOccurred -= OnWebMutation;
            UnsubscribeFromServiceEvents();

            if (_app != null)
            {
                try
                {
                    _cts?.Cancel();
                    _app.StopAsync().GetAwaiter().GetResult();
                    _app.DisposeAsync().AsTask().GetAwaiter().GetResult();
                }
                catch { }
            }

            _cts?.Dispose();
        }
    }
}
