using DV.Admin.UI.Services;
using Microsoft.Extensions.Caching.Memory;

namespace DV.Admin.UI.Middleware;

/// <summary>
/// Middleware to track user sessions and enforce NIST SP 800-53 session controls.
/// Calls InitializeSessionAsync which handles sliding expiration, absolute timeout,
/// concurrent session limits, and anomaly detection internally.
/// Uses in-memory caching to avoid database hits on every request.
/// </summary>
public class SessionTrackingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<SessionTrackingMiddleware> _logger;
    private static readonly TimeSpan SessionCacheDuration = TimeSpan.FromSeconds(30);

    public SessionTrackingMiddleware(RequestDelegate next, ILogger<SessionTrackingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context, SessionManagementService sessionService, IMemoryCache memoryCache)
    {
        try
        {
            if (context.User.Identity?.IsAuthenticated == true)
            {
                var username = context.User.Identity.Name;
                if (!string.IsNullOrEmpty(username))
                {
                    if (context.Request.Path.HasValue && 
                        !context.Request.Path.Value.StartsWith("/api/") &&
                        !context.Request.Path.Value.StartsWith("/_blazor") &&
                        !context.Request.Path.Value.StartsWith("/_content") &&
                        !context.Request.Path.Value.Contains("."))
                    {
                        var cacheKey = $"session:valid:{username}:{context.Session.Id}";

                        if (!memoryCache.TryGetValue(cacheKey, out bool isActive))
                        {
                            // Cache miss — hit the database
                            var session = await sessionService.InitializeSessionAsync(username, null);
                            isActive = session.IsActive;

                            memoryCache.Set(cacheKey, isActive, new MemoryCacheEntryOptions
                            {
                                SlidingExpiration = SessionCacheDuration
                            });
                        }

                        if (!isActive)
                        {
                            _logger.LogWarning("Session terminated for {Username}, redirecting to logout", username);
                            context.Response.Redirect("/Auth/Logout");
                            return;
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error in session tracking middleware");
        }

        await _next(context);
    }
}
