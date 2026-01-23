using DV.Admin.UI.Services;

namespace DV.Admin.UI.Middleware;

/// <summary>
/// Middleware to automatically track user sessions and activity
/// </summary>
public class SessionTrackingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<SessionTrackingMiddleware> _logger;

    public SessionTrackingMiddleware(RequestDelegate next, ILogger<SessionTrackingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context, SessionManagementService sessionService)
    {
        try
        {
            // Only track authenticated users
            if (context.User.Identity?.IsAuthenticated == true)
            {
                var username = context.User.Identity.Name;
                if (!string.IsNullOrEmpty(username))
                {
                    // Update session activity for page requests (not API calls or static files)
                    if (context.Request.Path.HasValue && 
                        !context.Request.Path.Value.StartsWith("/api/") &&
                        !context.Request.Path.Value.StartsWith("/_blazor") &&
                        !context.Request.Path.Value.StartsWith("/_content") &&
                        !context.Request.Path.Value.Contains("."))
                    {
                        await sessionService.UpdateSessionActivityAsync(
                            "PageView", 
                            context.Request.Method,
                            context.Request.Path.Value);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            // Session tracking should not break the application
            _logger.LogWarning(ex, "Error in session tracking middleware");
        }

        await _next(context);
    }
}
