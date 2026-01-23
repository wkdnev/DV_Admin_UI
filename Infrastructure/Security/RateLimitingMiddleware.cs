using Microsoft.AspNetCore.Http;
using System.Collections.Concurrent;

namespace DV.Admin.UI.Infrastructure.Security;

public class RateLimitingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<RateLimitingMiddleware> _logger;
    private static readonly ConcurrentDictionary<string, UserRateLimit> _requests = new();

    public RateLimitingMiddleware(RequestDelegate next, ILogger<RateLimitingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var clientId = GetClientIdentifier(context);
        var endpoint = context.Request.Path.Value ?? "";

        // Different limits for different endpoints
        var limit = GetRateLimit(endpoint);
        
        if (!IsRequestAllowed(clientId, limit))
        {
            _logger.LogWarning("Rate limit exceeded for client {ClientId} on endpoint {Endpoint}", clientId, endpoint);
            context.Response.StatusCode = 429; // Too Many Requests
            await context.Response.WriteAsync("Rate limit exceeded. Please try again later.");
            return;
        }

        await _next(context);
    }

    private string GetClientIdentifier(HttpContext context)
    {
        // Use username if authenticated, otherwise IP address
        if (context.User.Identity?.IsAuthenticated == true)
        {
            return context.User.Identity.Name ?? context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        }

        return context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    }

    private RateLimitConfig GetRateLimit(string endpoint)
    {
        return endpoint.ToLower() switch
        {
            var path when path.StartsWith("/api/") => new RateLimitConfig { MaxRequests = 100, TimeWindow = TimeSpan.FromMinutes(1) },
            var path when path.StartsWith("/admin/") => new RateLimitConfig { MaxRequests = 50, TimeWindow = TimeSpan.FromMinutes(1) },
            var path when path.Contains("/login") => new RateLimitConfig { MaxRequests = 5, TimeWindow = TimeSpan.FromMinutes(1) },
            _ => new RateLimitConfig { MaxRequests = 200, TimeWindow = TimeSpan.FromMinutes(1) }
        };
    }

    private bool IsRequestAllowed(string clientId, RateLimitConfig config)
    {
        var now = DateTime.UtcNow;
        var userLimit = _requests.GetOrAdd(clientId, _ => new UserRateLimit());

        lock (userLimit)
        {
            // Clean old requests
            userLimit.Requests.RemoveAll(r => now - r > config.TimeWindow);

            // Check if under limit
            if (userLimit.Requests.Count >= config.MaxRequests)
            {
                return false;
            }

            // Add current request
            userLimit.Requests.Add(now);
            return true;
        }
    }

    private class UserRateLimit
    {
        public List<DateTime> Requests { get; } = new();
    }

    private class RateLimitConfig
    {
        public int MaxRequests { get; set; }
        public TimeSpan TimeWindow { get; set; }
    }
}

public static class RateLimitingExtensions
{
    public static IApplicationBuilder UseRateLimiting(this IApplicationBuilder builder)
    {
        return builder.UseMiddleware<RateLimitingMiddleware>();
    }
}
