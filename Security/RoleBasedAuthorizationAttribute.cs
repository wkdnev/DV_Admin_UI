// ============================================================================
// RoleBasedAuthorizationAttribute.cs - Role-Based Authorization Attribute
// ============================================================================
//
// Purpose: Provides role-based authorization for Blazor components and pages.
// This attribute ensures that users can only access pages/components that 
// their current active role permits.
//
// Created: [Date]
// Last Updated: [Date]
//
// Dependencies:
// - Microsoft.AspNetCore.Authorization: For authorization infrastructure
// - DV.Admin.UI.Services: For role context management
//
// Notes:
// - Works with the RoleContextService to enforce active role permissions
// - Supports both single role and multiple role requirements
// - Integrates with existing ASP.NET Core authorization pipeline
// ============================================================================

using DV.Shared.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using DV.Shared.Security;

namespace DV.Admin.UI.Security;

// ============================================================================
// RoleBasedAuthorizationAttribute Class
// ============================================================================
// Purpose: Attribute for marking components/pages with role-based access control.
// ============================================================================
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
public class RoleBasedAuthorizationAttribute : AuthorizeAttribute
{
    public string[] RequiredRoles { get; }
    public bool RequireAllRoles { get; set; } = false;

    public RoleBasedAuthorizationAttribute(params string[] requiredRoles)
    {
        RequiredRoles = requiredRoles ?? throw new ArgumentNullException(nameof(requiredRoles));
        
        // Set the policy to a unique name that includes the roles (for the custom policy provider)
        // The policy provider will parse this to create the requirement with the correct roles
        Policy = $"RoleBasedAccess:{string.Join(",", requiredRoles)}";
    }
}

// ============================================================================
// RoleBasedAuthorizationRequirement Class
// ============================================================================
// Purpose: Authorization requirement for role-based access control.
// ============================================================================
public class RoleBasedAuthorizationRequirement : IAuthorizationRequirement
{
    public string[] RequiredRoles { get; }
    public bool RequireAllRoles { get; }

    public RoleBasedAuthorizationRequirement(string[] requiredRoles, bool requireAllRoles = false)
    {
        RequiredRoles = requiredRoles ?? throw new ArgumentNullException(nameof(requiredRoles));
        RequireAllRoles = requireAllRoles;
    }
}

// ============================================================================
// RoleBasedAuthorizationHandler Class
// ============================================================================
// Purpose: Handles role-based authorization requirements.
// ============================================================================
public class RoleBasedAuthorizationHandler : AuthorizationHandler<RoleBasedAuthorizationRequirement>
{
    private readonly Services.RoleContextService _roleContext;
    private readonly Services.UserService _userService;
    private readonly ILogger<RoleBasedAuthorizationHandler> _logger;
    private readonly IServiceProvider _serviceProvider;

    public RoleBasedAuthorizationHandler(
        Services.RoleContextService roleContext, 
        Services.UserService userService, 
        ILogger<RoleBasedAuthorizationHandler> logger,
        IServiceProvider serviceProvider)
    {
        _roleContext = roleContext;
        _userService = userService;
        _logger = logger;
        _serviceProvider = serviceProvider;
    }

    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context, 
        RoleBasedAuthorizationRequirement requirement)
    {
        // Check if user is authenticated
        if (!context.User.Identity?.IsAuthenticated == true)
        {
            _logger.LogInformation("User is not authenticated");
            context.Fail();
            return;
        }

        var username = context.User.Identity!.Name;
        if (string.IsNullOrEmpty(username))
        {
            _logger.LogWarning("Username is null or empty");
            context.Fail();
            return;
        }

        _logger.LogInformation("HandleRequirementAsync: Requirement has {Count} required roles: [{Roles}]", 
            requirement.RequiredRoles?.Length ?? 0, 
            requirement.RequiredRoles != null ? string.Join(", ", requirement.RequiredRoles) : "(null)");
        
        _logger.LogInformation("Checking role-based access for user '{Username}' with required roles: {RequiredRoles}",
            username, string.Join(", ", requirement.RequiredRoles ?? new string[0]));

        try
        {
            // Check if role context is already initialized (from Razor component scope)
            string? currentRoleName = _roleContext.CurrentRole?.Name;
            
            // If role context not initialized, check session directly
            if (_roleContext.CurrentRole == null && _roleContext.UserRoles.Count == 0)
            {
                _logger.LogInformation("Role context not initialized, checking session API for user '{Username}'", username);

                using var scope = _serviceProvider.CreateScope();
                var sessionService = scope.ServiceProvider.GetRequiredService<ISessionManagementService>();

                var activeSessions = await sessionService.GetUserSessionsByUsernameAsync(username, activeOnly: true);

                _logger.LogInformation("Found {Count} active sessions for user '{Username}'", activeSessions.Count, username);

                var activeSession = activeSessions.FirstOrDefault();

                _logger.LogInformation("Active session found: {Found}, Role: {Role}",
                    activeSession != null, activeSession?.CurrentRole ?? "(null)");

                if (activeSession != null && !string.IsNullOrEmpty(activeSession.CurrentRole))
                {
                    currentRoleName = activeSession.CurrentRole;
                    _logger.LogInformation("Found role '{Role}' in session for user '{Username}'", currentRoleName, username);
                }
                
                // If no role found in session either, user needs to select a role
                if (string.IsNullOrEmpty(currentRoleName))
                {
                    _logger.LogWarning("No role found in context or database session for user '{Username}' - user must navigate through role selector first", username);
                    context.Fail();
                    return;
                }
            }
            
            // If we have roles in context but no current role, handle that
            if (string.IsNullOrEmpty(currentRoleName) && _roleContext.UserRoles.Count > 0)
            {
                // For multi-role users, this is expected - they need to select a role first
                if (_roleContext.UserRoles.Count > 1)
                {
                    _logger.LogInformation("User '{Username}' has {RoleCount} roles but no current role selected - needs to choose via role selector", username, _roleContext.UserRoles.Count);
                    context.Fail(); // This will redirect them to role selection
                    return;
                }
                // For single-role users, try to set their only role
                else if (_roleContext.UserRoles.Count == 1)
                {
                    var onlyRole = _roleContext.UserRoles.First();
                    await _roleContext.SetActiveRoleAsync(onlyRole.Name);
                    currentRoleName = onlyRole.Name;
                    _logger.LogInformation("Auto-set current role to '{RoleName}' for single-role user '{Username}'", currentRoleName, username);
                }
            }
            
            // Final check - we should have a role name by now
            if (string.IsNullOrEmpty(currentRoleName))
            {
                _logger.LogWarning("User '{Username}' has no current role set", username);
                context.Fail();
                return;
            }

            _logger.LogInformation("User '{Username}' current role: '{CurrentRole}'", username, currentRoleName);

        // Check role requirements
        if (requirement.RequireAllRoles)
        {
            // User's current role must be one of the required roles (simplified check)
            var currentRoleMatches = requirement.RequiredRoles!.Any(role =>
                currentRoleName.Equals(role, StringComparison.OrdinalIgnoreCase));
            
            if (currentRoleMatches)
            {
                _logger.LogInformation("User '{Username}' current role '{CurrentRole}' matches required role(s)", username, currentRoleName);
                context.Succeed(requirement);
            }
            else
            {
                _logger.LogWarning("User '{Username}' current role '{CurrentRole}' does not match any required roles: {RequiredRoles}",
                    username, currentRoleName, string.Join(", ", requirement.RequiredRoles!));
                context.Fail();
            }
        }
        else
        {
            // User's current active role must be one of the required roles
            var currentRoleMatches = requirement.RequiredRoles!.Any(role => 
                currentRoleName.Equals(role, StringComparison.OrdinalIgnoreCase));                if (currentRoleMatches)
                {
                    _logger.LogInformation("User '{Username}' current role '{CurrentRole}' matches required role(s)", username, currentRoleName);
                    context.Succeed(requirement);
                }
                else
                {
                    _logger.LogWarning("User '{Username}' current role '{CurrentRole}' does not match any required roles: {RequiredRoles}",
                        username, currentRoleName, string.Join(", ", requirement.RequiredRoles!));
                    context.Fail();
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking role-based authorization for user '{Username}'", username);
            context.Fail();
        }
    }
}
