// ============================================================================
// RoleEnforcementMiddleware.cs - Role-Based Route Protection Middleware
// ============================================================================
//
// Purpose: Middleware that enforces role-based access control at the route level.
// Automatically redirects users to appropriate pages based on their current
// active role and the route they're trying to access.
//
// Created: [Date]
// Last Updated: [Date]
//
// Dependencies:
// - DV.Admin.UI.Services.RoleContextService: For role context management
// - Microsoft.AspNetCore.Http: For HTTP context operations
//
// Notes:
// - Runs early in the request pipeline to intercept unauthorized access
// - Handles automatic redirections for role-based navigation
// - Integrates with the RoleContextService for active role enforcement
// ============================================================================

using DV.Admin.UI.Services;

namespace DV.Admin.UI.Middleware;

// ============================================================================
// RoleEnforcementMiddleware Class
// ============================================================================
// Purpose: Enforces role-based access control for HTTP requests.
// ============================================================================
public class RoleEnforcementMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<RoleEnforcementMiddleware> _logger;

    public RoleEnforcementMiddleware(RequestDelegate next, ILogger<RoleEnforcementMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context, RoleContextService roleContext)
    {
        var path = context.Request.Path.Value?.ToLower() ?? "";

        // Skip middleware for static files, API endpoints, and auth endpoints
        if (ShouldSkipMiddleware(path))
        {
            await _next(context);
            return;
        }

        // Check if user is authenticated
        if (!context.User.Identity?.IsAuthenticated == true)
        {
            await _next(context);
            return;
        }

        try
        {
            // Initialize role context if needed
            if (roleContext.CurrentRole == null)
            {
                await roleContext.InitializeAsync();
            }

            // Handle role-based routing
            await HandleRoleBasedRouting(context, roleContext, path);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in role enforcement middleware for path: {Path}", path);
            await _next(context);
        }
    }

    private async Task HandleRoleBasedRouting(HttpContext context, RoleContextService roleContext, string path)
    {
        // If user has multiple roles but no active role selected, redirect to role selection
        if (roleContext.HasMultipleRoles && roleContext.CurrentRole == null && path != "/select-role")
        {
            context.Response.Redirect("/select-role");
            return;
        }

        // If user has no roles assigned and not on an error/auth page
        if (!roleContext.UserRoles.Any() && !IsAuthOrErrorPage(path))
        {
            context.Response.Redirect("/login");
            return;
        }

        // Check if current role can access the requested route
        if (roleContext.CurrentRole != null && !roleContext.CanAccessRoute(path))
        {
            _logger.LogWarning("User {Username} with role {Role} attempted to access unauthorized path: {Path}", 
                roleContext.CurrentUsername, roleContext.CurrentRole.Name, path);
            
            // Redirect to appropriate dashboard for current role
            var dashboardRoute = roleContext.GetDashboardRouteForRole();
            context.Response.Redirect(dashboardRoute);
            return;
        }

        await _next(context);
    }

    private static bool ShouldSkipMiddleware(string path)
    {
        var skipPaths = new[]
        {
            "/_framework",
            "/_content",
            "/css",
            "/js",
            "/api",
            "/login",
            "/logout",
            "/access-denied",
            "/select-role",
            "/.well-known"
        };

        return skipPaths.Any(skipPath => path.StartsWith(skipPath, StringComparison.OrdinalIgnoreCase)) ||
               path.Contains(".") && !path.EndsWith(".razor", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsAuthOrErrorPage(string path)
    {
        var authPages = new[]
        {
            "/login",
            "/logout", 
            "/access-denied",
            "/select-role",
            "/error"
        };

        return authPages.Any(authPage => path.StartsWith(authPage, StringComparison.OrdinalIgnoreCase));
    }
}
