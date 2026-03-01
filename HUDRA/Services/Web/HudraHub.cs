using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;

namespace HUDRA.Services.Web
{
    /// <summary>
    /// SignalR hub for real-time HUDRA state updates pushed to connected web clients.
    ///
    /// Server-to-client events:
    /// - TemperatureUpdated(cpuTemp, gpuTemp)
    /// - BatteryUpdated(percent, isCharging, onAc)
    /// - TdpUpdated(currentTdp, targetTdp)
    /// - FanStatusUpdated(fanSpeed, temperature)
    /// - GameDetected(gameName, gameId)
    /// - GameExited()
    /// - StatusUpdated(fullStatusSnapshot)
    /// </summary>
    public class HudraHub : Hub
    {
        public override async Task OnConnectedAsync()
        {
            System.Diagnostics.Debug.WriteLine($"[WebRemote] Client connected: {Context.ConnectionId}");
            await base.OnConnectedAsync();
        }

        public override async Task OnDisconnectedAsync(System.Exception? exception)
        {
            System.Diagnostics.Debug.WriteLine($"[WebRemote] Client disconnected: {Context.ConnectionId}");
            await base.OnDisconnectedAsync(exception);
        }

        /// <summary>
        /// Client can request a full status snapshot on connect.
        /// </summary>
        public async Task RequestStatus()
        {
            // The WebServerService will handle providing the full snapshot
            // via the StatusUpdated event when a client connects
            await Clients.Caller.SendAsync("StatusRequested");
        }
    }
}
