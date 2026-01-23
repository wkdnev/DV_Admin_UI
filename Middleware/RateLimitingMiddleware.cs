// ============================================================================
// RateLimitingMiddleware.cs - API Rate Limiting Middleware
// ============================================================================
//
// Purpose: Provides rate limiting functionality to prevent API abuse and 
// ensure fair usage of system resources. Implements sliding window rate 
// limiting with configurable limits per IP address.
//
// Features:
// - Configurable requests per minute per IP
// - Sliding window algorithm for smooth rate limiting
// - Proper HTTP 429 responses with retry-after headers
// - Memory-efficient storage with automatic cleanup
// - Logging for monitoring and debugging
//
// ============================================================================

using System.Collections.Concurrent;
using System.Net;
using Microsoft.Extensions.Options;

namespace DV.Admin.UI.Middleware;

/// <summary>
/// Configuration options for rate limiting
/// </summary>
public class RateLimitingOptions
{
    public int RequestsPerMinute { get; set; } = 100;
    public int RequestsPerHour { get; set; } = 1000;
    public bool EnableRateLimiting { get; set; } = true;
    public string[] ExemptPaths { get; set; } = Array.Empty<string>();
    public string[] ExemptIpAddresses { get; set; } = Array.Empty<string>();
}

/// <summary>
/// Tracks request history for an IP address
/// </summary>
public class RequestTracker
{
    private readonly Queue<DateTime> _requestTimes = new();
    private readonly object _lock = new();

    public bool IsAllowed(int requestsPerMinute, int requestsPerHour)
    {
        lock (_lock)
        {
            var now = DateTime.UtcNow;
            var oneMinuteAgo = now.AddMinutes(-1);
            var oneHourAgo = now.AddHours(-1);

            // Remove old requests
            while (_requestTimes.Count > 0 && _requestTimes.Peek() < oneHourAgo)
            {
                _requestTimes.Dequeue();
            }

            // Count requests in the last minute and hour
            var requestsInLastMinute = _requestTimes.Count(t => t > oneMinuteAgo);
            var requestsInLastHour = _requestTimes.Count;

            // Check limits
            if (requestsInLastMinute >= requestsPerMinute || requestsInLastHour >= requestsPerHour)
            {
                return false;
            }

            // Add current request
            _requestTimes.Enqueue(now);
            return true;
        }
    }

    public DateTime GetOldestRequest()
    {
        lock (_lock)
        {
            return _requestTimes.Count > 0 ? _requestTimes.Peek() : DateTime.MinValue;
        }
    }
}

/// <summary>
/// Middleware that provides rate limiting functionality for API endpoints
/// </summary>
public class RateLimitingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly RateLimitingOptions _options;
    private readonly ILogger<RateLimitingMiddleware> _logger;
    private readonly ConcurrentDictionary<string, RequestTracker> _trackers = new();
    private readonly Timer _cleanupTimer;

    public RateLimitingMiddleware(
        RequestDelegate next,
        IOptions<RateLimitingOptions> options,
        ILogger<RateLimitingMiddleware> logger)
    {
        _next = next;
        _options = options.Value;
        _logger = logger;

        // Cleanup old trackers every 5 minutes
        _cleanupTimer = new Timer(CleanupOldTrackers, null, TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(5));
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // Skip rate limiting if disabled
        if (!_options.EnableRateLimiting)
        {
            await _next(context);
            return;
        }

        // Skip rate limiting for exempt paths
        var path = context.Request.Path.Value?.ToLowerInvariant() ?? "";
        if (_options.ExemptPaths.Any(exemptPath => path.StartsWith(exemptPath.ToLowerInvariant())))
        {
            await _next(context);
            return;
        }

        // Get client IP address
        var clientIp = GetClientIpAddress(context);

        // Skip rate limiting for exempt IP addresses
        if (_options.ExemptIpAddresses.Contains(clientIp))
        {
            await _next(context);
            return;
        }

        // Get or create tracker for this IP
        var tracker = _trackers.GetOrAdd(clientIp, _ => new RequestTracker());

        // Check if request is allowed
        if (!tracker.IsAllowed(_options.RequestsPerMinute, _options.RequestsPerHour))
        {
            _logger.LogWarning("Rate limit exceeded for IP: {ClientIp}", clientIp);

            // Calculate retry-after time (next minute)
            var oldestRequest = tracker.GetOldestRequest();
            var retryAfter = Math.Max(1, (int)(60 - (DateTime.UtcNow - oldestRequest).TotalSeconds));

            context.Response.StatusCode = (int)HttpStatusCode.TooManyRequests;
            context.Response.Headers["Retry-After"] = retryAfter.ToString();
            context.Response.Headers["X-RateLimit-Limit"] = _options.RequestsPerMinute.ToString();
            context.Response.Headers["X-RateLimit-Remaining"] = "0";
            context.Response.Headers["X-RateLimit-Reset"] = DateTimeOffset.UtcNow.AddSeconds(retryAfter).ToUnixTimeSeconds().ToString();

            await context.Response.WriteAsync($"Rate limit exceeded. Try again in {retryAfter} seconds.");
            return;
        }

        // Add rate limit headers to response
        context.Response.Headers["X-RateLimit-Limit"] = _options.RequestsPerMinute.ToString();

        await _next(context);
    }

    /// <summary>
    /// Gets the client IP address from the request
    /// </summary>
    private string GetClientIpAddress(HttpContext context)
    {
        // Check for forwarded IP first (for load balancers/proxies)
        var forwardedFor = context.Request.Headers["X-Forwarded-For"].FirstOrDefault();
        if (!string.IsNullOrEmpty(forwardedFor))
        {
            return forwardedFor.Split(',')[0].Trim();
        }

        // Check for real IP header
        var realIp = context.Request.Headers["X-Real-IP"].FirstOrDefault();
        if (!string.IsNullOrEmpty(realIp))
        {
            return realIp;
        }

        // Fall back to connection remote IP
        return context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    }

    /// <summary>
    /// Removes old trackers to prevent memory leaks
    /// </summary>
    private void CleanupOldTrackers(object? state)
    {
        var cutoff = DateTime.UtcNow.AddHours(-2);
        var keysToRemove = new List<string>();

        foreach (var kvp in _trackers)
        {
            if (kvp.Value.GetOldestRequest() < cutoff)
            {
                keysToRemove.Add(kvp.Key);
            }
        }

        foreach (var key in keysToRemove)
        {
            _trackers.TryRemove(key, out _);
        }

        if (keysToRemove.Count > 0)
        {
            _logger.LogDebug("Cleaned up {Count} old rate limit trackers", keysToRemove.Count);
        }
    }

    /// <summary>
    /// Cleanup resources
    /// </summary>
    public void Dispose()
    {
        _cleanupTimer?.Dispose();
    }
}
