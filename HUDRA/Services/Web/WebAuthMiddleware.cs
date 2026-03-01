using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using HUDRA.Services;
using Microsoft.AspNetCore.Http;

namespace HUDRA.Services.Web
{
    /// <summary>
    /// Simple PIN-based authentication middleware for the HUDRA web remote.
    /// Issues session cookies on successful PIN entry. Rate-limits failed attempts.
    /// </summary>
    public class WebAuthMiddleware
    {
        private readonly RequestDelegate _next;

        // In-memory session store (cleared on restart — intentional)
        private static readonly ConcurrentDictionary<string, SessionInfo> _sessions = new();

        // Rate limiting per IP
        private static readonly ConcurrentDictionary<string, RateLimitInfo> _rateLimits = new();

        private const int MAX_FAILED_ATTEMPTS = 5;
        private static readonly TimeSpan LOCKOUT_DURATION = TimeSpan.FromMinutes(5);
        private static readonly TimeSpan SESSION_EXPIRY = TimeSpan.FromHours(24);
        private const int MAX_SESSIONS = 10;
        private const string SESSION_COOKIE_NAME = "hudra_session";

        public WebAuthMiddleware(RequestDelegate next)
        {
            _next = next;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            var path = context.Request.Path.Value ?? "";

            // Allow access without auth: static files, auth endpoints, SignalR negotiate
            if (IsExemptPath(path))
            {
                await _next(context);
                return;
            }

            // Check session cookie
            if (context.Request.Cookies.TryGetValue(SESSION_COOKIE_NAME, out var sessionToken) &&
                _sessions.TryGetValue(sessionToken, out var session) &&
                session.ExpiresAt > DateTime.UtcNow)
            {
                // Valid session — extend expiry on activity
                session.ExpiresAt = DateTime.UtcNow.Add(SESSION_EXPIRY);
                await _next(context);
                return;
            }

            // No valid session
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync(JsonSerializer.Serialize(new { error = "Authentication required" }));
        }

        private static bool IsExemptPath(string path)
        {
            // Static files
            if (path == "/" || path.EndsWith(".html") || path.EndsWith(".css") ||
                path.EndsWith(".js") || path.EndsWith(".ico") || path.EndsWith(".png"))
                return true;

            // Auth endpoints
            if (path.StartsWith("/api/auth/"))
                return true;

            // SignalR negotiation
            if (path.StartsWith("/hudrahub"))
                return true;

            return false;
        }

        // --- Static methods for auth endpoints ---

        public static (bool Success, string? SessionToken, string Error) TryLogin(string pin, string clientIp)
        {
            // Check rate limit
            if (_rateLimits.TryGetValue(clientIp, out var rateLimit))
            {
                if (rateLimit.FailedAttempts >= MAX_FAILED_ATTEMPTS &&
                    rateLimit.LockedUntil > DateTime.UtcNow)
                {
                    var remaining = (rateLimit.LockedUntil - DateTime.UtcNow).TotalSeconds;
                    return (false, null, $"Too many failed attempts. Try again in {(int)remaining} seconds.");
                }

                // Reset if lockout has expired
                if (rateLimit.LockedUntil <= DateTime.UtcNow)
                {
                    rateLimit.FailedAttempts = 0;
                }
            }

            // Verify PIN
            var storedHash = SettingsService.GetWebRemotePinHash();
            var storedSalt = SettingsService.GetWebRemotePinSalt();

            if (string.IsNullOrEmpty(storedHash))
            {
                return (false, null, "No PIN configured. Set a PIN in HUDRA Settings.");
            }

            var inputHash = HashPin(pin, storedSalt);

            if (!CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(inputHash),
                Encoding.UTF8.GetBytes(storedHash)))
            {
                // Failed attempt
                var limit = _rateLimits.GetOrAdd(clientIp, _ => new RateLimitInfo());
                limit.FailedAttempts++;
                if (limit.FailedAttempts >= MAX_FAILED_ATTEMPTS)
                {
                    limit.LockedUntil = DateTime.UtcNow.Add(LOCKOUT_DURATION);
                }
                return (false, null, "Invalid PIN.");
            }

            // Success — clear rate limit
            _rateLimits.TryRemove(clientIp, out _);

            // Create session
            CleanupExpiredSessions();
            var token = Guid.NewGuid().ToString("N");
            _sessions[token] = new SessionInfo
            {
                CreatedAt = DateTime.UtcNow,
                ExpiresAt = DateTime.UtcNow.Add(SESSION_EXPIRY),
                ClientIp = clientIp
            };

            return (true, token, "");
        }

        public static bool IsAuthenticated(HttpContext context)
        {
            if (context.Request.Cookies.TryGetValue(SESSION_COOKIE_NAME, out var token) &&
                _sessions.TryGetValue(token, out var session))
            {
                return session.ExpiresAt > DateTime.UtcNow;
            }
            return false;
        }

        public static void SetSessionCookie(HttpContext context, string token)
        {
            context.Response.Cookies.Append(SESSION_COOKIE_NAME, token, new CookieOptions
            {
                HttpOnly = true,
                SameSite = SameSiteMode.Lax,
                MaxAge = SESSION_EXPIRY,
                Path = "/"
            });
        }

        public static string HashPin(string pin, string salt)
        {
            using var sha256 = SHA256.Create();
            var bytes = Encoding.UTF8.GetBytes(salt + pin);
            var hash = sha256.ComputeHash(bytes);
            return Convert.ToBase64String(hash);
        }

        public static string GenerateSalt()
        {
            var salt = new byte[16];
            RandomNumberGenerator.Fill(salt);
            return Convert.ToBase64String(salt);
        }

        private static void CleanupExpiredSessions()
        {
            var expired = _sessions.Where(s => s.Value.ExpiresAt <= DateTime.UtcNow).ToList();
            foreach (var s in expired)
                _sessions.TryRemove(s.Key, out _);

            // Enforce max sessions — remove oldest
            while (_sessions.Count >= MAX_SESSIONS)
            {
                var oldest = _sessions.OrderBy(s => s.Value.CreatedAt).FirstOrDefault();
                if (oldest.Key != null)
                    _sessions.TryRemove(oldest.Key, out _);
            }
        }

        private class SessionInfo
        {
            public DateTime CreatedAt { get; set; }
            public DateTime ExpiresAt { get; set; }
            public string ClientIp { get; set; } = "";
        }

        private class RateLimitInfo
        {
            public int FailedAttempts { get; set; }
            public DateTime LockedUntil { get; set; }
        }
    }
}
