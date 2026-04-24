using DV.Admin.UI.Data;
// ============================================================================
// RoleContextService.cs - Simplified Role Context Management Service
// ============================================================================
//
// Purpose: Manages the current active role for a user session based on the 
// new simplified architecture where only global admins exist at global level
// and all other roles are project-scoped.
//
// Architecture:
// - Global Level: Only IsGlobalAdmin flag for system administrators
// - Project Level: All other roles are project-specific via ProjectRole system
//
// Created: [Date]
// Last Updated: [Date]
//
// Dependencies:
// - DocViewer_Proto.Security: For ApplicationRole and user management.
// - Microsoft.AspNetCore.Components.Authorization: For authentication state.
//
// Notes:
// - This service maintains the current active role state
// - Provides role switching functionality for multi-project users
// - Integrates with the permission system for role-based access control
// ============================================================================

using DV.Shared.Security;
using DV.Shared.Interfaces;
using DV.Admin.UI.Services;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.Logging;

namespace DV.Admin.UI.Services;

// ============================================================================
// RoleContextService Class
// ============================================================================
// Purpose: Manages the current active role context for authenticated users
// in the simplified architecture.
// ============================================================================
public class RoleContextService
{
    private readonly AuthenticationStateProvider _authStateProvider;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<RoleContextService> _logger;
    private readonly ISessionManagementService _sessionService;
    private readonly SemaphoreSlim _initLock = new(1, 1);
    
    private ApplicationRole? _currentRole;
    private List<ApplicationRole> _userRoles = new();
    private string? _currentUsername;
    private bool _isInitialized = false;
    private Task<bool>? _initializationTask;

    public RoleContextService(
        AuthenticationStateProvider authStateProvider, 
        IServiceProvider serviceProvider, 
        ILogger<RoleContextService> logger,
        ISessionManagementService sessionService)
    {
        _authStateProvider = authStateProvider;
        _serviceProvider = serviceProvider;
        _logger = logger;
        _sessionService = sessionService;
    }

    // ========================================================================
    // Events
    // ========================================================================
    public event EventHandler<RoleChangedEventArgs>? RoleChanged;

    // ========================================================================
    // Properties
    // ========================================================================
    public ApplicationRole? CurrentRole => _currentRole;
    public List<ApplicationRole> UserRoles => _userRoles;
    public bool HasMultipleRoles => _userRoles.Count > 1;
    public string? CurrentUsername => _currentUsername;

    // ========================================================================
    // Method: InitializeAsync
    // ========================================================================
    // Purpose: Initializes the role context for the current user using the
    // simplified architecture. Thread-safe and caches the result.
    public async Task<bool> InitializeAsync()
    {
        // If already initialized, return immediately
        if (_isInitialized)
        {
            return true;
        }

        // If initialization is in progress, wait for it
        if (_initializationTask != null)
        {
            return await _initializationTask;
        }

        // Acquire lock and perform initialization
        await _initLock.WaitAsync();
        try
        {
            // Double-check after acquiring lock
            if (_isInitialized)
            {
                return true;
            }

            // Create the initialization task
            _initializationTask = InitializeInternalAsync();
            var result = await _initializationTask;
            
            if (result)
            {
                _isInitialized = true;
            }
            
            return result;
        }
        finally
        {
            _initLock.Release();
        }
    }

    private async Task<bool> InitializeInternalAsync()
    {
        try
        {
            var authState = await _authStateProvider.GetAuthenticationStateAsync();
            if (!authState.User.Identity?.IsAuthenticated == true)
            {
                _logger.LogWarning("RoleContextService: User not authenticated");
                return false;
            }

            _currentUsername = authState.User.Identity!.Name;
            if (string.IsNullOrEmpty(_currentUsername))
            {
                _logger.LogWarning("RoleContextService: Username is null or empty");
                return false;
            }

            _logger.LogInformation($"RoleContextService: Initializing for user: {_currentUsername}");

            // Create a child scope for DB operations to avoid disposed context issues
            // during Blazor prerendering (SSR scope gets disposed before async operations complete)
            using var scope = _serviceProvider.CreateScope();
            var userService = scope.ServiceProvider.GetRequiredService<UserService>();
            var firstUserAdminService = scope.ServiceProvider.GetRequiredService<FirstUserAdminService>();
            var projectRoleService = scope.ServiceProvider.GetRequiredService<ProjectRoleService>();
            
            // Ensure the user exists in the database
            var displayName = _currentUsername.Contains('\\') ? _currentUsername.Split('\\')[1] : _currentUsername;
            var email = $"{displayName}@example.com";
            
            // Check if this is the first user and promote to admin if needed
            var wasFirstUser = await firstUserAdminService.EnsureFirstUserIsAdminAsync(_currentUsername, displayName, email);
            if (wasFirstUser)
            {
                _logger.LogWarning($"RoleContextService: First user detected - {_currentUsername} promoted to Global Administrator");
            }
            
            var user = await userService.EnsureUserExistsAsync(_currentUsername, displayName, email);
            var userId = user.UserId;
            _logger.LogInformation($"RoleContextService: User found/created with ID: {userId}");

            // Step 1: Check if user is a global admin
            var isGlobalAdmin = await userService.IsGlobalAdminAsync(userId);
            
            // Step 2: Get project-specific roles - data is already materialized with AsNoTracking
            var userProjectAccess = await projectRoleService.GetUserProjectAccessAsync(userId);
            
            // Extract only the data we need into simple value tuples
            var projectRolesData = userProjectAccess
                .SelectMany(upa => upa.ProjectRoles.Select(pr => (
                    ProjectRoleId: pr.ProjectRoleId,
                    DisplayName: pr.DisplayName ?? "Unknown",
                    ProjectId: pr.ProjectId
                )))
                .ToList();
            
            _logger.LogInformation($"RoleContextService: Found {userProjectAccess.Count} projects with roles");
            
            // Try to restore current role from session
            string? sessionRoleName = null;
            try
            {
                // Query session by username via the session management service
                var sessions = await _sessionService.GetUserSessionsByUsernameAsync(_currentUsername!, activeOnly: true);
                var activeSession = sessions.FirstOrDefault();

                if (activeSession != null)
                {
                    sessionRoleName = activeSession.CurrentRole;
                    if (!string.IsNullOrEmpty(sessionRoleName))
                    {
                        _logger.LogInformation($"RoleContextService: Found role '{sessionRoleName}' in session for user {_currentUsername}");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "RoleContextService: Error retrieving role from session");
            }
            
            // Now build the roles list from the materialized data (no DB access)
            _userRoles.Clear();
            
            // Add Admin role if user is global admin
            if (isGlobalAdmin)
            {
                var adminRole = new ApplicationRole
                {
                    RoleId = 1,
                    Name = "Admin",
                    Description = "Global System Administrator"
                };
                _userRoles.Add(adminRole);
                _logger.LogInformation($"RoleContextService: User is global admin, added Admin role");
            }
            
            // Add project-specific roles from materialized data
            var existingRoleNames = new HashSet<string>(_userRoles.Select(r => r.Name), StringComparer.OrdinalIgnoreCase);
            foreach (var (projectRoleId, roleDisplayName, projectId) in projectRolesData)
            {
                var roleName = ExtractRoleTypeFromDisplayName(roleDisplayName);
                
                // Skip Admin role since global admin already added it
                if (roleName.Equals("Admin", StringComparison.OrdinalIgnoreCase))
                    continue;

                // Only add if we haven't already added this role name
                if (!existingRoleNames.Contains(roleName))
                {
                    var virtualRole = new ApplicationRole
                    {
                        RoleId = projectRoleId + 10000,
                        Name = roleName,
                        Description = $"{roleName} role across assigned projects"
                    };

                    _userRoles.Add(virtualRole);
                    existingRoleNames.Add(roleName);
                    _logger.LogInformation($"RoleContextService: Added consolidated role: {virtualRole.Name}");
                }
            }
            
            _logger.LogInformation($"RoleContextService: Total consolidated roles: {_userRoles.Count}");
            _logger.LogInformation($"RoleContextService: Final role list: {string.Join(", ", _userRoles.Select(r => r.Name))}");
            
            // Smart role assignment logic:
            // - Priority 1: Restore from session if valid
            // - Priority 2: If user has exactly one role: auto-assign it
            // - Priority 3: If user has multiple roles: let them choose (unless session has a role)
            // - Priority 4: If user has no roles: leave current role as null
            
            if (!string.IsNullOrEmpty(sessionRoleName))
            {
                // Restore role from session if it's still valid
                var sessionRole = _userRoles.FirstOrDefault(r => r.Name.Equals(sessionRoleName, StringComparison.OrdinalIgnoreCase));
                if (sessionRole != null)
                {
                    _currentRole = sessionRole;
                    _logger.LogInformation($"RoleContextService: Restored role from session: {_currentRole.Name}");
                }
                else
                {
                    _logger.LogWarning($"RoleContextService: Session role '{sessionRoleName}' is no longer valid for user");
                }
            }
            
            // If no valid session role, apply default logic
            if (_currentRole == null)
            {
                if (_userRoles.Count == 1)
                {
                    _currentRole = _userRoles.First();
                    _logger.LogInformation($"RoleContextService: User has single role, auto-assigned: {_currentRole.Name}");
                }
                else if (_userRoles.Count > 1)
                {
                    _logger.LogInformation($"RoleContextService: User has {_userRoles.Count} roles - no current role set, user must choose via SelectRole");
                    _currentRole = null; // Force role selection for multi-role users without a current role
                }
                else
                {
                    _logger.LogInformation("RoleContextService: No roles found for user");
                    _currentRole = null;
                }
            }

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "RoleContextService: Error during initialization");
            return false;
        }
    }

    // ========================================================================
    // Method: LoadUserRoles
    // ========================================================================
    // Purpose: Loads user roles using the simplified architecture:
    // - Global Admin status via IsGlobalAdmin flag
    // - Project-specific roles via ProjectRole system
    private async Task LoadUserRoles(IServiceScope scope, int userId)
    {
        var userService = scope.ServiceProvider.GetRequiredService<UserService>();
        var projectRoleService = scope.ServiceProvider.GetRequiredService<ProjectRoleService>();

        // Clear existing roles
        _userRoles.Clear();

        // Step 1: Check if user is a global admin
        var isGlobalAdmin = await userService.IsGlobalAdminAsync(userId);
        if (isGlobalAdmin)
        {
            // Add Admin role for global administrators
            var adminRole = new ApplicationRole
            {
                RoleId = 1, // Standard Admin role ID
                Name = "Admin",
                Description = "Global System Administrator"
            };
            _userRoles.Add(adminRole);
            _logger.LogInformation($"RoleContextService: User is global admin, added Admin role");
        }

        // Step 2: Get project-specific roles and convert them to virtual ApplicationRole format
        var userProjectAccess = await projectRoleService.GetUserProjectAccessAsync(userId);
        _logger.LogInformation($"RoleContextService: Found {userProjectAccess.Count} projects with roles");

        // Create a set to track unique role names to avoid duplicates
        var existingRoleNames = new HashSet<string>(_userRoles.Select(r => r.Name), StringComparer.OrdinalIgnoreCase);

        foreach (var projectAccess in userProjectAccess)
        {
            foreach (var projectRole in projectAccess.ProjectRoles)
            {
                // Use DisplayName instead of ApplicationRole.Name since ApplicationRole table is removed
                // Extract the role type from DisplayName (e.g., "Bills and Receipts Editor" -> "Editor")
                var displayName = projectRole.DisplayName ?? "Unknown";
                var roleName = ExtractRoleTypeFromDisplayName(displayName);
                
                // Skip Admin role since global admin already added it
                if (roleName.Equals("Admin", StringComparison.OrdinalIgnoreCase))
                    continue;

                // Only add if we haven't already added this role name
                if (!existingRoleNames.Contains(roleName))
                {
                    // Create a virtual ApplicationRole that represents the project-specific role
                    var virtualRole = new ApplicationRole
                    {
                        RoleId = projectRole.ProjectRoleId + 10000, // Offset to avoid ID conflicts
                        Name = roleName,
                        Description = $"{roleName} role across assigned projects"
                    };

                    _userRoles.Add(virtualRole);
                    existingRoleNames.Add(roleName);
                    _logger.LogInformation($"RoleContextService: Added consolidated role: {virtualRole.Name}");
                }
            }
        }

        _logger.LogInformation($"RoleContextService: Total consolidated roles: {_userRoles.Count}");
        _logger.LogInformation($"RoleContextService: Final role list: {string.Join(", ", _userRoles.Select(r => r.Name))}");
    }

    // ========================================================================
    // Method: SetActiveRoleAsync
    // ========================================================================
    // Purpose: Sets the active role for the current user session.
    public async Task<bool> SetActiveRoleAsync(string roleName)
    {
        var role = _userRoles.FirstOrDefault(r => r.Name.Equals(roleName, StringComparison.OrdinalIgnoreCase));
        if (role == null)
        {
            return false;
        }

        var previousRole = _currentRole;
        _currentRole = role;

        // Update session with new role
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var sessionService = scope.ServiceProvider.GetService<ISessionManagementService>();
            if (sessionService != null)
            {
                _logger.LogInformation($"RoleContextService: Updating session role to '{roleName}' for user '{_currentUsername}'");
                // Use username-based update since HttpContext.Session may not be available in Blazor Server
                await sessionService.UpdateSessionRoleByUsernameAsync(_currentUsername!, roleName);
                _logger.LogInformation($"RoleContextService: Session role updated successfully");
            }
            else
            {
                _logger.LogWarning($"RoleContextService: ISessionManagementService not available");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update session role for user {Username}", _currentUsername);
        }

        // Notify subscribers of role change
        RoleChanged?.Invoke(this, new RoleChangedEventArgs(previousRole, _currentRole));

        return true;
    }

    // ========================================================================
    // Method: GetDashboardRouteForRole
    // ========================================================================
    // Purpose: Returns the appropriate dashboard route based on the role.
    public string GetDashboardRouteForRole(string? roleName = null)
    {
        var targetRole = roleName ?? _currentRole?.Name;

        return targetRole?.ToLower() switch
        {
            "admin" => "/admin",
            "auditor" => "/auditor/dashboard",
            "editor" => "/editor/dashboard",
            "dataowner" => "/dataowner/dashboard", 
            "security" => "/security/dashboard",
            "readonly" => "/",
            _ => "/"
        };
    }

    // ========================================================================
    // Method: HasRoleAsync
    // ========================================================================
    // Purpose: Checks if the current user has a specific role.
    public Task<bool> HasRoleAsync(string roleName)
    {
        return Task.FromResult(_userRoles.Any(r => r.Name.Equals(roleName, StringComparison.OrdinalIgnoreCase)));
    }

    // ========================================================================
    // Method: IsCurrentRole
    // ========================================================================
    // Purpose: Checks if the specified role is the currently active role.
    public bool IsCurrentRole(string roleName)
    {
        return _currentRole?.Name.Equals(roleName, StringComparison.OrdinalIgnoreCase) == true;
    }

    // ========================================================================
    // Method: CanAccessRoute
    // ========================================================================
    // Purpose: Determines if the current role can access a specific route.
    public bool CanAccessRoute(string route)
    {
        if (_currentRole == null) return false;

        // Define route access rules based on current role
        return _currentRole.Name.ToLower() switch
        {
            "admin" => true, // Admin can access everything
            "auditor" => route.StartsWith("/auditor") || route == "/" || route.StartsWith("/documents"),
            "editor" => route.StartsWith("/editor") || route == "/" || route.StartsWith("/documents"),
            "dataowner" => route.StartsWith("/dataowner") || route == "/" || route.StartsWith("/documents"),
            "security" => route.StartsWith("/security") || route == "/" || route.StartsWith("/documents"),
            "readonly" => route == "/" || route.StartsWith("/documents"),
            _ => false
        };
    }

    // ========================================================================
    // Method: GetRoleIcon
    // ========================================================================
    // Purpose: Returns the appropriate Bootstrap icon for a role.
    public string GetRoleIcon(string roleName)
    {
        return roleName.ToLower() switch
        {
            "admin" => "bi-gear-fill",
            "auditor" => "bi-clipboard-data",
            "editor" => "bi-pencil-square",
            "dataowner" => "bi-person-badge",
            "security" => "bi-shield-lock",
            "readonly" => "bi-eye-fill",
            _ => "bi-person-circle"
        };
    }

    // ================================================================================
    // Method: GetRoleColor
    // ================================================================================
    // Purpose: Returns the appropriate color class for a role.
    public string GetRoleColor(string roleName)
    {
        return roleName.ToLower() switch
        {
            "admin" => "text-danger",
            "auditor" => "text-primary",
            "editor" => "text-success",
            "dataowner" => "text-info",
            "security" => "text-warning",
            "readonly" => "text-secondary",
            _ => "text-muted"
        };
    }

    // ========================================================================
    // Helper Methods
    // ========================================================================
    
    /// <summary>
    /// Extracts role type from project role display name
    /// Examples: "Bills and Receipts Editor" -> "Editor", "Invoices ReadOnly" -> "ReadOnly"
    /// </summary>
    private string ExtractRoleTypeFromDisplayName(string displayName)
    {
        if (string.IsNullOrEmpty(displayName))
            return "Unknown";

        // Split by space and take the last word as the role type
        var parts = displayName.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length > 0)
        {
            var lastPart = parts[^1]; // Get the last element
            
            // Handle common role types
            return lastPart switch
            {
                "ReadOnly" => "ReadOnly",
                "Editor" => "Editor", 
                "Auditor" => "Auditor",
                "Security" => "Security",
                "DataOwner" => "DataOwner",
                "Admin" => "Admin",
                _ => lastPart // Default to the last word
            };
        }
        
        return "Unknown";
    }
}

// ============================================================================
// RoleChangedEventArgs Class
// ============================================================================
// Purpose: Event arguments for role change notifications.
// ============================================================================
public class RoleChangedEventArgs : EventArgs
{
    public ApplicationRole? PreviousRole { get; }
    public ApplicationRole CurrentRole { get; }

    public RoleChangedEventArgs(ApplicationRole? previousRole, ApplicationRole currentRole)
    {
        PreviousRole = previousRole;
        CurrentRole = currentRole;
    }
}
