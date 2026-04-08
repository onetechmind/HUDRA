using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using HUDRA.Configuration;
using HUDRA.Models;
using HUDRA.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace HUDRA.Services.Web
{
    /// <summary>
    /// Registers all REST API endpoints for the HUDRA web remote.
    /// All commands proxy to existing services via the ServiceBridge.
    /// </summary>
    public static class HudraApiEndpoints
    {
        public static void MapEndpoints(IEndpointRouteBuilder app, ServiceBridge bridge)
        {
            MapLogoEndpoint(app);
            MapAuthEndpoints(app);
            MapStatusEndpoints(app, bridge);
            MapTdpEndpoints(app, bridge);
            MapAudioEndpoints(app, bridge);
            MapBrightnessEndpoints(app, bridge);
            MapResolutionEndpoints(app, bridge);
            MapFpsEndpoints(app, bridge);
            MapHdrEndpoints(app, bridge);
            MapFanEndpoints(app, bridge);
            MapGameEndpoints(app, bridge);
            MapPowerEndpoints(app, bridge);
            MapBatteryEndpoints(app, bridge);
            MapAmdEndpoints(app, bridge);
            MapLosslessScalingEndpoints(app, bridge);
        }

        // --- Logo ---

        private static void MapLogoEndpoint(IEndpointRouteBuilder app)
        {
            app.MapGet("/api/logo", () =>
            {
                var logoPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "HUDRA-logo-violet.png");
                if (File.Exists(logoPath))
                    return Results.File(File.ReadAllBytes(logoPath), "image/png");
                return Results.NotFound();
            });
        }

        // --- Auth ---

        private static void MapAuthEndpoints(IEndpointRouteBuilder app)
        {
            app.MapPost("/api/auth/login", async (HttpContext ctx) =>
            {
                try
                {
                    using var reader = new StreamReader(ctx.Request.Body);
                    var body = await reader.ReadToEndAsync();
                    var request = JsonSerializer.Deserialize<LoginRequest>(body);

                    if (request == null || string.IsNullOrEmpty(request.pin))
                        return Results.BadRequest(new { error = "PIN is required" });

                    var clientIp = ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown";
                    var (success, token, error) = WebAuthMiddleware.TryLogin(request.pin, clientIp);

                    if (!success)
                        return Results.Json(new { error }, statusCode: 401);

                    WebAuthMiddleware.SetSessionCookie(ctx, token!);
                    return Results.Ok(new { success = true });
                }
                catch (Exception ex)
                {
                    return Results.Problem(ex.Message);
                }
            });

            app.MapGet("/api/auth/check", (HttpContext ctx) =>
            {
                var isAuth = WebAuthMiddleware.IsAuthenticated(ctx);
                return Results.Ok(new { authenticated = isAuth });
            });
        }

        // --- Status (full snapshot) ---

        private static void MapStatusEndpoints(IEndpointRouteBuilder app, ServiceBridge bridge)
        {
            app.MapGet("/api/status", async () =>
            {
                try
                {
                    var temp = bridge.GetCurrentTemperature();
                    var battery = bridge.GetCurrentBattery();
                    var tdpResult = await bridge.GetCurrentTdpAsync();
                    var audio = bridge.CreateAudioService();
                    var brightness = bridge.CreateBrightnessService();
                    var hdr = bridge.CreateHdrService();
                    var resolution = bridge.CreateResolutionService();
                    var amdState = await bridge.GetAmdStateAsync();

                    var currentRes = resolution.GetCurrentResolution();
                    var currentRefresh = resolution.GetCurrentRefreshRate();
                    var fpsLimiter = bridge.GetFpsLimiter();

                    return Results.Ok(new
                    {
                        temperature = temp != null ? new { cpu = Math.Round(temp.CpuTemperature, 1), gpu = Math.Round(temp.GpuTemperature, 1) } : null,
                        battery = battery != null ? new { percent = battery.Percent, isCharging = battery.IsCharging, onAc = battery.OnAc } : null,
                        tdp = new
                        {
                            current = tdpResult.Success ? tdpResult.TdpWatts : 0,
                            min = HudraSettings.MIN_TDP,
                            max = HudraSettings.MAX_TDP,
                            stickyEnabled = SettingsService.GetTdpCorrectionEnabled()
                        },
                        volume = new { level = (int)(audio.GetMasterVolumeScalar() * 100), muted = audio.GetMuteStatus() },
                        brightness = brightness.GetBrightness(),
                        resolution = currentRes.Success ? new { width = currentRes.CurrentResolution.Width, height = currentRes.CurrentResolution.Height } : null,
                        refreshRate = currentRefresh.Success ? currentRefresh.RefreshRate : 0,
                        fpsLimit = fpsLimiter?.GetCurrentFpsLimit() ?? 0,
                        hdr = new { enabled = hdr.IsHdrEnabled(), supported = hdr.IsHdrSupported() },
                        fan = new
                        {
                            speed = bridge.GetCurrentFanSpeed(),
                            preset = SettingsService.GetFanCurve().ActivePreset,
                            enabled = SettingsService.GetFanCurveEnabled()
                        },
                        amd = new
                        {
                            available = amdState.available,
                            rsrEnabled = amdState.rsrEnabled,
                            rsrSharpness = amdState.rsrSharpness,
                            afmfEnabled = amdState.afmfEnabled,
                            antiLagEnabled = amdState.antiLagEnabled
                        }
                    });
                }
                catch (Exception ex)
                {
                    return Results.Problem(ex.Message);
                }
            });
        }

        // --- TDP ---

        private static void MapTdpEndpoints(IEndpointRouteBuilder app, ServiceBridge bridge)
        {
            app.MapGet("/api/tdp", async () =>
            {
                var result = await bridge.GetCurrentTdpAsync();
                return Results.Ok(new
                {
                    current = result.Success ? result.TdpWatts : 0,
                    min = HudraSettings.MIN_TDP,
                    max = HudraSettings.MAX_TDP,
                    stickyEnabled = SettingsService.GetTdpCorrectionEnabled(),
                    lastUsed = SettingsService.GetLastUsedTdp()
                });
            });

            app.MapPost("/api/tdp", async (HttpContext ctx) =>
            {
                var body = await ParseBody<TdpRequest>(ctx);
                if (body == null) return Results.BadRequest(new { error = "Invalid request" });

                var watts = Math.Clamp(body.watts, HudraSettings.MIN_TDP, HudraSettings.MAX_TDP);
                var result = await bridge.SetTdpAsync(watts);

                if (result.Success) bridge.NotifyWebMutation();
                return result.Success
                    ? Results.Ok(new { success = true, tdp = watts })
                    : Results.Problem(result.Message);
            });

            app.MapGet("/api/tdp/sticky", () =>
                Results.Ok(new { enabled = SettingsService.GetTdpCorrectionEnabled() }));

            app.MapPost("/api/tdp/sticky", async (HttpContext ctx) =>
            {
                var body = await ParseBody<EnabledRequest>(ctx);
                if (body == null) return Results.BadRequest(new { error = "Invalid request" });

                SettingsService.SetTdpCorrectionEnabled(body.enabled);
                bridge.NotifyWebMutation();
                return Results.Ok(new { success = true, enabled = body.enabled });
            });
        }

        // --- Audio ---

        private static void MapAudioEndpoints(IEndpointRouteBuilder app, ServiceBridge bridge)
        {
            app.MapGet("/api/volume", () =>
            {
                var audio = bridge.CreateAudioService();
                return Results.Ok(new { level = (int)(audio.GetMasterVolumeScalar() * 100), muted = audio.GetMuteStatus() });
            });

            app.MapPost("/api/volume", async (HttpContext ctx) =>
            {
                var body = await ParseBody<VolumeRequest>(ctx);
                if (body == null) return Results.BadRequest(new { error = "Invalid request" });

                var audio = bridge.CreateAudioService();
                audio.SetMasterVolumeScalar(Math.Clamp(body.level, 0, 100) / 100f);
                bridge.NotifyWebMutation();
                return Results.Ok(new { success = true, level = body.level });
            });

            app.MapPost("/api/volume/mute", () =>
            {
                var audio = bridge.CreateAudioService();
                audio.ToggleMute();
                // Small delay to let the system process the mute toggle
                System.Threading.Thread.Sleep(50);
                bridge.NotifyWebMutation();
                return Results.Ok(new { success = true, muted = audio.GetMuteStatus() });
            });
        }

        // --- Brightness ---

        private static void MapBrightnessEndpoints(IEndpointRouteBuilder app, ServiceBridge bridge)
        {
            app.MapGet("/api/brightness", () =>
            {
                var svc = bridge.CreateBrightnessService();
                return Results.Ok(new { level = svc.GetBrightness() });
            });

            app.MapPost("/api/brightness", async (HttpContext ctx) =>
            {
                var body = await ParseBody<BrightnessRequest>(ctx);
                if (body == null) return Results.BadRequest(new { error = "Invalid request" });

                var svc = bridge.CreateBrightnessService();
                svc.SetBrightness(Math.Clamp(body.level, 0, 100));
                bridge.NotifyWebMutation();
                return Results.Ok(new { success = true, level = body.level });
            });
        }

        // --- Resolution ---

        private static void MapResolutionEndpoints(IEndpointRouteBuilder app, ServiceBridge bridge)
        {
            app.MapGet("/api/resolution", () =>
            {
                var svc = bridge.CreateResolutionService();
                var current = svc.GetCurrentResolution();
                var available = svc.GetAvailableResolutions();
                var currentRefresh = svc.GetCurrentRefreshRate();

                return Results.Ok(new
                {
                    current = current.Success ? new { width = current.CurrentResolution.Width, height = current.CurrentResolution.Height } : null,
                    refreshRate = currentRefresh.Success ? currentRefresh.RefreshRate : 0,
                    available = available.Select(r => new { width = r.Width, height = r.Height, display = r.DisplayText }).Distinct().ToList(),
                    availableRefreshRates = current.Success ? svc.GetAvailableRefreshRates(current.CurrentResolution) : new System.Collections.Generic.List<int>()
                });
            });

            app.MapPost("/api/resolution", async (HttpContext ctx) =>
            {
                var body = await ParseBody<ResolutionRequest>(ctx);
                if (body == null) return Results.BadRequest(new { error = "Invalid request" });

                var svc = bridge.CreateResolutionService();
                var resolution = new ResolutionService.Resolution
                {
                    Width = body.width,
                    Height = body.height,
                    RefreshRate = body.refreshRate
                };

                var result = svc.SetResolution(resolution);
                if (result.Success) bridge.NotifyWebMutation();
                return result.Success
                    ? Results.Ok(new { success = true })
                    : Results.Problem(result.Message);
            });
        }

        // --- FPS Limiter ---

        private static void MapFpsEndpoints(IEndpointRouteBuilder app, ServiceBridge bridge)
        {
            app.MapGet("/api/fps-limit", () =>
            {
                var svc = bridge.GetFpsLimiter();
                return Results.Ok(new
                {
                    current = svc?.GetCurrentFpsLimit() ?? 0,
                    isInstalled = RtssFpsLimiterService.GetCachedInstallationStatus()
                });
            });

            app.MapPost("/api/fps-limit", async (HttpContext ctx) =>
            {
                var body = await ParseBody<FpsRequest>(ctx);
                if (body == null) return Results.BadRequest(new { error = "Invalid request" });

                var svc = bridge.GetFpsLimiter();
                if (svc == null)
                    return Results.Problem("RTSS not available");

                bool success;
                if (body.fps <= 0)
                    success = await svc.DisableGlobalFpsLimitAsync();
                else
                    success = await svc.SetGlobalFpsLimitAsync(body.fps);

                if (success)
                {
                    // Keep settings in sync so the native UI shows the correct value on next navigation
                    SettingsService.SetSelectedFpsLimit(Math.Max(0, body.fps));
                    bridge.NotifyWebMutation();
                }
                return success
                    ? Results.Ok(new { success = true, fps = body.fps })
                    : Results.Problem("Failed to set FPS limit");
            });
        }

        // --- HDR ---

        private static void MapHdrEndpoints(IEndpointRouteBuilder app, ServiceBridge bridge)
        {
            app.MapGet("/api/hdr", () =>
            {
                var svc = bridge.CreateHdrService();
                return Results.Ok(new { enabled = svc.IsHdrEnabled(), supported = svc.IsHdrSupported() });
            });

            app.MapPost("/api/hdr", async (HttpContext ctx) =>
            {
                var body = await ParseBody<EnabledRequest>(ctx);
                if (body == null) return Results.BadRequest(new { error = "Invalid request" });

                var svc = bridge.CreateHdrService();
                var success = svc.SetHdrEnabled(body.enabled);

                if (success) bridge.NotifyWebMutation();
                return success
                    ? Results.Ok(new { success = true, enabled = body.enabled })
                    : Results.Problem("Failed to set HDR state");
            });
        }

        // --- Fan Control ---

        private static void MapFanEndpoints(IEndpointRouteBuilder app, ServiceBridge bridge)
        {
            app.MapGet("/api/fan", () =>
            {
                var fanCurve = SettingsService.GetFanCurve();
                var temp = bridge.GetCurrentTemperature();
                return Results.Ok(new
                {
                    speed = bridge.GetCurrentFanSpeed(),
                    temperature = temp != null ? Math.Round(temp.MaxTemperature, 1) : 0,
                    preset = fanCurve.ActivePreset,
                    enabled = fanCurve.IsEnabled,
                    initialized = bridge.IsFanControlInitialized()
                });
            });

            app.MapPost("/api/fan/preset", async (HttpContext ctx) =>
            {
                var body = await ParseBody<FanPresetRequest>(ctx);
                if (body == null) return Results.BadRequest(new { error = "Invalid request" });

                var validPresets = new[] { "stealth", "cruise", "warp", "Stealth", "Cruise", "Warp" };
                if (!validPresets.Contains(body.preset, StringComparer.OrdinalIgnoreCase))
                    return Results.BadRequest(new { error = "Invalid preset. Use: stealth, cruise, warp" });

                // Save the preset to settings
                var fanCurve = SettingsService.GetFanCurve();
                fanCurve.ActivePreset = body.preset;
                SettingsService.SetFanCurve(fanCurve);

                bridge.NotifyWebMutation();
                return Results.Ok(new { success = true, preset = body.preset });
            });

            app.MapPost("/api/fan/enabled", async (HttpContext ctx) =>
            {
                var body = await ParseBody<EnabledRequest>(ctx);
                if (body == null) return Results.BadRequest(new { error = "Invalid request" });

                SettingsService.SetFanCurveEnabled(body.enabled);

                if (body.enabled)
                {
                    var tempMonitor = bridge.GetTemperatureMonitor();
                    if (tempMonitor != null)
                        bridge.EnableFanControl(tempMonitor);
                }
                else
                {
                    bridge.DisableFanControl();
                }

                bridge.NotifyWebMutation();
                return Results.Ok(new { success = true, enabled = body.enabled });
            });
        }

        // --- Games ---

        private static void MapGameEndpoints(IEndpointRouteBuilder app, ServiceBridge bridge)
        {
            app.MapGet("/api/games", async () =>
            {
                var detection = bridge.GetGameDetection();
                if (detection == null)
                    return Results.Ok(new { games = Array.Empty<object>() });

                var games = await detection.GetAllGamesAsync();
                var result = games.Select(g =>
                {
                    var artPath = CleanArtworkPath(g.ArtworkPath);
                    var hasArt = !string.IsNullOrEmpty(artPath) && File.Exists(artPath);
                    return new
                    {
                        processName = g.ProcessName,
                        displayName = g.DisplayName,
                        source = g.Source.ToString(),
                        hasArtwork = hasArt,
                        artworkUrl = hasArt
                            ? $"/api/games/{Uri.EscapeDataString(g.ProcessName)}/artwork"
                            : null,
                        hasProfile = !string.IsNullOrEmpty(g.ProfileJson)
                    };
                });

                return Results.Ok(new { games = result });
            });

            app.MapGet("/api/games/{processName}/artwork", (string processName) =>
            {
                var detection = bridge.GetGameDetection();
                if (detection == null)
                    return Results.NotFound();

                var games = detection.GetAllGamesAsync().GetAwaiter().GetResult();
                var game = games.FirstOrDefault(g =>
                    string.Equals(g.ProcessName, processName, StringComparison.OrdinalIgnoreCase));

                var artPath = CleanArtworkPath(game?.ArtworkPath);
                if (game == null || string.IsNullOrEmpty(artPath) || !File.Exists(artPath))
                    return Results.NotFound();

                var ext = Path.GetExtension(artPath).ToLower();
                var contentType = ext switch
                {
                    ".png" => "image/png",
                    ".jpg" or ".jpeg" => "image/jpeg",
                    ".webp" => "image/webp",
                    _ => "application/octet-stream"
                };

                return Results.File(File.ReadAllBytes(artPath), contentType);
            });

            app.MapPost("/api/games/{processName}/launch", async (string processName) =>
            {
                var detection = bridge.GetGameDetection();
                if (detection == null)
                    return Results.Problem("Game detection not available");

                var games = await detection.GetAllGamesAsync();
                var game = games.FirstOrDefault(g =>
                    string.Equals(g.ProcessName, processName, StringComparison.OrdinalIgnoreCase));

                if (game == null)
                    return Results.NotFound(new { error = "Game not found" });

                try
                {
                    var launcher = new GameLauncherService();
                    launcher.LaunchGame(game);
                    return Results.Ok(new { success = true, game = game.DisplayName });
                }
                catch (Exception ex)
                {
                    return Results.Problem($"Failed to launch: {ex.Message}");
                }
            });

            app.MapGet("/api/games/{processName}/profile", (string processName) =>
            {
                var profileService = bridge.GetGameProfile();
                if (profileService == null)
                    return Results.Problem("Profile service not available");

                var profile = profileService.GetProfileForGame(processName);
                return Results.Ok(new { profile });
            });
        }

        // --- Power ---

        private static void MapPowerEndpoints(IEndpointRouteBuilder app, ServiceBridge bridge)
        {
            app.MapGet("/api/power/profile", async () =>
            {
                var svc = bridge.GetPowerProfile();
                if (svc == null)
                    return Results.Problem("Power profile service not available");

                var profiles = await svc.GetAvailableProfilesAsync();
                var active = await svc.GetActiveProfileAsync();

                return Results.Ok(new
                {
                    active = active != null ? new { id = active.Id, name = active.Name } : null,
                    profiles = profiles.Select(p => new { id = p.Id, name = p.Name, isActive = p.IsActive })
                });
            });

            app.MapPost("/api/power/profile", async (HttpContext ctx) =>
            {
                var body = await ParseBody<PowerProfileRequest>(ctx);
                if (body == null) return Results.BadRequest(new { error = "Invalid request" });

                var svc = bridge.GetPowerProfile();
                if (svc == null)
                    return Results.Problem("Power profile service not available");

                if (!Guid.TryParse(body.profileId, out var guid))
                    return Results.BadRequest(new { error = "Invalid profile ID" });

                var success = await svc.SetActiveProfileAsync(guid);
                if (success) bridge.NotifyWebMutation();
                return success
                    ? Results.Ok(new { success = true })
                    : Results.Problem("Failed to set power profile");
            });

            app.MapGet("/api/power/boost", async () =>
            {
                var svc = bridge.GetPowerProfile();
                if (svc == null)
                    return Results.Ok(new { enabled = false });

                var enabled = await svc.GetCpuBoostEnabledAsync();
                return Results.Ok(new { enabled });
            });

            app.MapPost("/api/power/boost", async (HttpContext ctx) =>
            {
                var body = await ParseBody<EnabledRequest>(ctx);
                if (body == null) return Results.BadRequest(new { error = "Invalid request" });

                var svc = bridge.GetPowerProfile();
                if (svc == null)
                    return Results.Problem("Power profile service not available");

                var success = await svc.SetCpuBoostEnabledAsync(body.enabled);
                if (success) bridge.NotifyWebMutation();
                return success
                    ? Results.Ok(new { success = true, enabled = body.enabled })
                    : Results.Problem("Failed to set CPU boost");
            });
        }

        // --- Battery ---

        private static void MapBatteryEndpoints(IEndpointRouteBuilder app, ServiceBridge bridge)
        {
            app.MapGet("/api/battery", () =>
            {
                var battery = bridge.GetCurrentBattery();
                if (battery == null)
                    return Results.Ok(new { percent = 0, isCharging = false, onAc = false });

                return Results.Ok(new
                {
                    percent = battery.Percent,
                    isCharging = battery.IsCharging,
                    onAc = battery.OnAc
                });
            });
        }

        // --- AMD Features ---

        private static void MapAmdEndpoints(IEndpointRouteBuilder app, ServiceBridge bridge)
        {
            app.MapGet("/api/amd", async () =>
            {
                var state = await bridge.GetAmdStateAsync();
                return Results.Ok(new
                {
                    available = state.available,
                    rsrEnabled = state.rsrEnabled,
                    rsrSharpness = state.rsrSharpness,
                    afmfEnabled = state.afmfEnabled,
                    antiLagEnabled = state.antiLagEnabled
                });
            });

            app.MapPost("/api/amd/rsr", async (HttpContext ctx) =>
            {
                var body = await ParseBody<AmdRsrRequest>(ctx);
                if (body == null) return Results.BadRequest(new { error = "Invalid request" });

                var sharpness = Math.Clamp(body.sharpness, 0, 100);
                var success = await bridge.SetAmdRsrAsync(body.enabled, sharpness);
                if (success) bridge.NotifyWebMutation();
                return success
                    ? Results.Ok(new { success = true, enabled = body.enabled, sharpness })
                    : Results.Problem("Failed to set RSR state");
            });

            app.MapPost("/api/amd/rsr/sharpness", async (HttpContext ctx) =>
            {
                var body = await ParseBody<SharpnessRequest>(ctx);
                if (body == null) return Results.BadRequest(new { error = "Invalid request" });

                var sharpness = Math.Clamp(body.sharpness, 0, 100);
                var success = await bridge.SetAmdRsrSharpnessAsync(sharpness);
                if (success) bridge.NotifyWebMutation();
                return success
                    ? Results.Ok(new { success = true, sharpness })
                    : Results.Problem("Failed to set RSR sharpness (RSR may not be enabled)");
            });

            app.MapPost("/api/amd/afmf", async (HttpContext ctx) =>
            {
                var body = await ParseBody<EnabledRequest>(ctx);
                if (body == null) return Results.BadRequest(new { error = "Invalid request" });

                var success = await bridge.SetAmdAfmfAsync(body.enabled);
                if (success) bridge.NotifyWebMutation();
                return success
                    ? Results.Ok(new { success = true, enabled = body.enabled })
                    : Results.Problem("Failed to set AFMF state");
            });

            app.MapPost("/api/amd/antilag", async (HttpContext ctx) =>
            {
                var body = await ParseBody<EnabledRequest>(ctx);
                if (body == null) return Results.BadRequest(new { error = "Invalid request" });

                var success = await bridge.SetAmdAntiLagAsync(body.enabled);
                if (success) bridge.NotifyWebMutation();
                return success
                    ? Results.Ok(new { success = true, enabled = body.enabled })
                    : Results.Problem("Failed to set Anti-Lag state");
            });
        }

        // --- Lossless Scaling ---

        private static void MapLosslessScalingEndpoints(IEndpointRouteBuilder app, ServiceBridge bridge)
        {
            app.MapGet("/api/lossless/status", () =>
            {
                var ls = bridge.GetLosslessScaling();
                var detection = bridge.GetGameDetection();
                bool isRunning = ls?.IsLosslessScalingRunning() ?? false;
                bool hasGame = detection?.CurrentGame != null;
                return Results.Ok(new
                {
                    isRunning,
                    hasGame,
                    canTrigger = isRunning && hasGame
                });
            });

            app.MapPost("/api/lossless/trigger", async () =>
            {
                var error = await bridge.TriggerLosslessScalingAsync();
                return error == null
                    ? Results.Ok(new { success = true })
                    : Results.Problem(error);
            });
        }

        // --- Helpers ---

        /// <summary>
        /// Strips cache-busting query strings (e.g. "?t=123") that the native Library page
        /// appends to ArtworkPath on shared DetectedGame objects.
        /// </summary>
        private static string? CleanArtworkPath(string? path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            var qi = path.IndexOf('?');
            return qi > 0 ? path.Substring(0, qi) : path;
        }

        private static async Task<T?> ParseBody<T>(HttpContext ctx) where T : class
        {
            try
            {
                using var reader = new StreamReader(ctx.Request.Body);
                var body = await reader.ReadToEndAsync();
                return JsonSerializer.Deserialize<T>(body);
            }
            catch
            {
                return null;
            }
        }

        // --- Request DTOs ---

        private record LoginRequest(string pin);
        private record TdpRequest(int watts);
        private record VolumeRequest(int level);
        private record BrightnessRequest(int level);
        private record ResolutionRequest(int width, int height, int refreshRate);
        private record FpsRequest(int fps);
        private record EnabledRequest(bool enabled);
        private record FanPresetRequest(string preset);
        private record PowerProfileRequest(string profileId);
        private record AmdRsrRequest(bool enabled, int sharpness);
        private record SharpnessRequest(int sharpness);
    }
}
