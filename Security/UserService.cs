using DV.Admin.UI.Data;
using DV.Admin.UI.Infrastructure.Caching;
using DV.Shared.DTOs;
using DV.Shared.Security;
using DV.Shared.Security;
using DV.Admin.UI.Infrastructure.Caching;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace DV.Admin.UI.Services;

public class UserService
{
    private readonly SecurityDbContext _context;
    private readonly ICacheService _cache;
    private readonly ILogger<UserService> _logger;
    private readonly NotificationApiService _notificationService;

    public UserService(SecurityDbContext context, ICacheService cache, ILogger<UserService> logger, NotificationApiService notificationService)
    {
        _context = context;
        _cache = cache;
        _logger = logger;
        _notificationService = notificationService;
    }

    public async Task<ApplicationUser?> GetUserByUsernameAsync(string username)
    {
        var cacheKey = $"user:username:{username.ToLower()}";
        
        return await _cache.GetOrSetAsync(cacheKey, async () =>
        {
            // Don't normalize - use the full username as provided
            // This preserves domain prefixes like "AD\neil.rainsforth"
            
            // Search for the exact username first, then try normalized version for backward compatibility
            var users = await _context.Users
                .Where(u => u.Username.ToLower() == username.ToLower())
                .ToListAsync();
            
            if (users.Any())
            {
                return users.First();
            }
            
            // For backward compatibility, also search for normalized version
            // Only if no exact match is found
            var normalized = NormalizeUsername(username);
            if (normalized != username)
            {
                var normalizedUsers = await _context.Users
                    .Where(u => u.Username.ToLower() == normalized.ToLower())
                    .ToListAsync();
                
                return normalizedUsers.FirstOrDefault();
            }
            
            return null;
        }, TimeSpan.FromMinutes(30));
    }

    public async Task<ApplicationUser> EnsureUserExistsAsync(string username, string displayName, string email)
    {
        // Use the FULL username (including domain prefix like "AD\neil.rainsforth")
        // Do NOT normalize it
        var user = await GetUserByUsernameAsync(username);
        if (user == null)
        {
            user = new ApplicationUser
            {
                Username = username, // Keep the full username with domain
                DisplayName = displayName ?? ExtractDisplayNameFromUsername(username),
                Email = email ?? string.Empty,
                IsActive = true,
                CreatedDate = DateTime.UtcNow
            };
            _context.Users.Add(user);
            await _context.SaveChangesAsync();
            
            // Invalidate user cache since we created a new user
            await InvalidateUserCacheAsync(username);
        }
        return user;
    }

    /// <summary>
    /// Gets the current username from the HTTP context (placeholder for now)
    /// </summary>
    public async Task<string?> GetCurrentUsernameAsync()
    {
        // TODO: Implement proper current user detection from HttpContext
        // For now, return a placeholder - this should be updated to use IHttpContextAccessor
        await Task.CompletedTask;
        return "Unknown";
    }

    // REMOVED: UserHasProjectPermissionAsync and UserHasPermissionAsync methods
    // Permissions system has been removed - authorization is now based on project roles only

    /// <summary>
    /// Gets all project roles for a user
    /// </summary>
    public async Task<List<ProjectRole>> GetUserProjectRolesAsync(int userId)
    {
        return await _context.UserProjectRoles
            .Include(upr => upr.ProjectRole)
            .ThenInclude(pr => pr!.ApplicationRole)
            .Where(upr => upr.UserId == userId && upr.IsActive)
            .Select(upr => upr.ProjectRole!)
            .ToListAsync();
    }

    /// <summary>
    /// Gets all projects a user has access to
    /// </summary>
    public async Task<List<int>> GetUserAccessibleProjectsAsync(int userId)
    {
        return await _context.UserProjectRoles
            .Include(upr => upr.ProjectRole)
            .Where(upr => upr.UserId == userId && upr.IsActive && upr.ProjectRole!.IsActive)
            .Select(upr => upr.ProjectRole!.ProjectId)
            .Distinct()
            .ToListAsync();
    }

    public async Task<List<ApplicationUser>> GetAllUsersAsync()
    {
        return await _context.Users
            .OrderBy(u => u.Username)
            .ToListAsync();
    }

    public async Task UpdateUserAsync(ApplicationUser user)
    {
        // DO NOT normalize username - keep it as provided (with domain prefix)
        _context.Users.Update(user);
        await _context.SaveChangesAsync();
    }

    public async Task DeleteUserAsync(int userId)
    {
        var user = await _context.Users.FindAsync(userId);
        if (user == null) return;

        // Remove project role assignments
        var projectRoles = await _context.UserProjectRoles.Where(upr => upr.UserId == userId).ToListAsync();
        if (projectRoles.Any())
        {
            _context.UserProjectRoles.RemoveRange(projectRoles);
        }

        _context.Users.Remove(user);
        await _context.SaveChangesAsync();
    }

    private string NormalizeUsername(string username)
    {
        if (string.IsNullOrWhiteSpace(username)) return string.Empty;
        if (username.Contains('\\')) return username.Split('\\')[1];
        return username;
    }

    /// <summary>
    /// Extracts display name from full username (removes domain prefix for display purposes)
    /// </summary>
    private string ExtractDisplayNameFromUsername(string username)
    {
        if (string.IsNullOrWhiteSpace(username)) return string.Empty;
        if (username.Contains('\\')) return username.Split('\\')[1];
        return username;
    }

    /// <summary>
    /// Deactivates a user account
    /// </summary>
    public async Task DeactivateUserAsync(int userId)
    {
        var user = await _context.Users.FindAsync(userId);
        if (user != null)
        {
            user.IsActive = false;
            await _context.SaveChangesAsync();
        }
    }

    /// <summary>
    /// Reactivates a user account
    /// </summary>
    public async Task ReactivateUserAsync(int userId)
    {
        var user = await _context.Users.FindAsync(userId);
        if (user != null)
        {
            user.IsActive = true;
            await _context.SaveChangesAsync();
        }
    }

    // ============================================================================
    // Global Admin Management Methods
    // ============================================================================

    /// <summary>
    /// Checks if a user is a global administrator
    /// </summary>
    public async Task<bool> IsGlobalAdminAsync(int userId)
    {
        var user = await _context.Users.FindAsync(userId);
        return user?.IsGlobalAdmin == true;
    }
    
    /// <summary>
    /// Checks if a user is a global administrator by username
    /// </summary>
    public async Task<bool> IsGlobalAdminAsync(string username)
    {
        var user = await GetUserByUsernameAsync(username);
        return user?.IsGlobalAdmin == true;
    }
    
    /// <summary>
    /// Sets a user as a global administrator
    /// </summary>
    public async Task SetGlobalAdminAsync(int userId, bool isAdmin = true)
    {
        var user = await _context.Users.FindAsync(userId);
        if (user != null)
        {
            user.IsGlobalAdmin = isAdmin;
            await _context.SaveChangesAsync();
        }
    }
    
    /// <summary>
    /// Gets all global administrators
    /// </summary>
    public async Task<List<ApplicationUser>> GetGlobalAdminsAsync()
    {
        return await _context.Users
            .Where(u => u.IsGlobalAdmin == true)
            .OrderBy(u => u.Username)
            .ToListAsync();
    }
    
    /// <summary>
    /// Gets all non-admin users
    /// </summary>
    public async Task<List<ApplicationUser>> GetNonAdminUsersAsync()
    {
        return await _context.Users
            .Where(u => u.IsGlobalAdmin != true)
            .OrderBy(u => u.Username)
            .ToListAsync();
    }
    
    /// <summary>
    /// Promotes a user to global administrator
    /// </summary>
    public async Task PromoteToGlobalAdminAsync(int userId)
    {
        await SetGlobalAdminAsync(userId, true);

        // SI-5: RoleChange notification — promoted to global admin
        try
        {
            await _notificationService.CreateNotificationAsync(new CreateNotificationDto
            {
                UserId = userId,
                Title = "Promoted to Global Admin",
                Message = "You have been granted Global Administrator privileges.",
                Category = NotificationCategories.RoleChange,
                IsImportant = true,
                SourceSystem = NotificationSources.Admin,
                CorrelationId = $"promote-admin-{userId}"
            });
        }
        catch (Exception notifEx)
        {
            _logger.LogWarning(notifEx, "Failed to create promotion notification for user {UserId}", userId);
        }
    }
    
    /// <summary>
    /// Removes global administrator privileges from a user
    /// </summary>
    public async Task RemoveGlobalAdminAsync(int userId)
    {
        await SetGlobalAdminAsync(userId, false);

        // SI-5: RoleChange notification — removed from global admin
        try
        {
            await _notificationService.CreateNotificationAsync(new CreateNotificationDto
            {
                UserId = userId,
                Title = "Global Admin Removed",
                Message = "Your Global Administrator privileges have been removed.",
                Category = NotificationCategories.RoleChange,
                IsImportant = true,
                SourceSystem = NotificationSources.Admin,
                CorrelationId = $"demote-admin-{userId}"
            });
        }
        catch (Exception notifEx)
        {
            _logger.LogWarning(notifEx, "Failed to create demotion notification for user {UserId}", userId);
        }
    }

    /// <summary>
    /// Promotes a user to global administrator by username
    /// </summary>
    public async Task<bool> PromoteToGlobalAdminAsync(string username)
    {
        var user = await GetUserByUsernameAsync(username);
        if (user == null)
            return false;
        
        await PromoteToGlobalAdminAsync(user.UserId);
        return true;
    }
    
    /// <summary>
    /// Removes global administrator privileges from a user by username
    /// </summary>
    public async Task<bool> RemoveGlobalAdminAsync(string username)
    {
        var user = await GetUserByUsernameAsync(username);
        if (user == null)
            return false;
        
        await RemoveGlobalAdminAsync(user.UserId);
        return true;
    }

    // ============================================================================
    // Legacy Compatibility Methods
    // ============================================================================
    // These methods provide backward compatibility for the old role system while
    // mapping to the new simplified architecture.

    /// <summary>
    /// Gets user roles in the legacy format for backward compatibility.
    /// Maps the new architecture to the old ApplicationRole format.
    /// </summary>
    public async Task<List<ApplicationRole>> GetUserRolesAsync(int userId)
    {
        var roles = new List<ApplicationRole>();

        // Check if user is global admin
        var isGlobalAdmin = await IsGlobalAdminAsync(userId);
        if (isGlobalAdmin)
        {
            roles.Add(new ApplicationRole
            {
                RoleId = 1,
                Name = "Admin",
                Description = "Global System Administrator"
            });
        }

        // Get project-specific roles and create virtual ApplicationRole objects
        var projectRoles = await GetUserProjectRolesAsync(userId);
        var uniqueRoleNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        
        // Add Admin role name to avoid duplicates
        if (isGlobalAdmin)
        {
            uniqueRoleNames.Add("Admin");
        }

        foreach (var projectRole in projectRoles)
        {
            var roleName = projectRole.ApplicationRole?.Name ?? "Unknown";
            
            // Skip if we already have this role name
            if (uniqueRoleNames.Contains(roleName))
                continue;

            roles.Add(new ApplicationRole
            {
                RoleId = projectRole.ProjectRoleId + 10000, // Offset to avoid conflicts
                Name = roleName,
                Description = projectRole.ApplicationRole?.Description ?? $"{roleName} role"
            });

            uniqueRoleNames.Add(roleName);
        }

        return roles;
    }

    /// <summary>
    /// Legacy method for role assignment - now maps to project role assignment.
    /// This is a placeholder implementation for backward compatibility.
    /// </summary>
    public Task AssignRoleToUserAsync(int userId, int roleId)
    {
        // This method is deprecated in the new architecture
        // Project-specific role assignment should be done through ProjectRoleService
        throw new NotSupportedException("Role assignment is now handled through project-specific roles. Use ProjectRoleService.AssignUserToProjectRoleAsync instead.");
    }

    /// <summary>
    /// Legacy method for getting users in a role - now maps to project role queries.
    /// This is a placeholder implementation for backward compatibility.
    /// </summary>
    public async Task<List<ApplicationUser>> GetUsersInRoleAsync(int roleId)
    {
        // This method is deprecated in the new architecture
        // For global admin users, we can still provide this functionality
        if (roleId == 1) // Admin role
        {
            return await GetGlobalAdminsAsync();
        }

        // For other roles, they are now project-specific
        throw new NotSupportedException("User role queries are now handled through project-specific roles. Use ProjectRoleService for project-specific role queries.");
    }

    /// <summary>
    /// Legacy method for consolidating duplicate users.
    /// This is a placeholder implementation for backward compatibility.
    /// </summary>
    public async Task ConsolidateDuplicateUsersAsync(string username)
    {
        // Find potential duplicate users based on username normalization
        var allUsers = await GetAllUsersAsync();
        var normalizedUsername = NormalizeUsername(username);
        
        var duplicates = allUsers
            .Where(u => NormalizeUsername(u.Username).Equals(normalizedUsername, StringComparison.OrdinalIgnoreCase))
            .OrderBy(u => u.CreatedDate)
            .ToList();

        if (duplicates.Count <= 1)
        {
            return; // No duplicates to consolidate
        }

        var primaryUser = duplicates.First();
        var duplicateUsers = duplicates.Skip(1).ToList();

        // For each duplicate user, we would need to:
        // 1. Merge their project role assignments to the primary user
        // 2. Update any document access records
        // 3. Delete the duplicate user records
        
        // This is a complex operation that should be done carefully
        // For now, we'll just delete the duplicate users without the full merge
        foreach (var duplicate in duplicateUsers)
        {
            await DeleteUserAsync(duplicate.UserId);
        }
    }
    /// <summary>
    /// Synchronizes the IsGlobalAdmin flag from external claims
    /// </summary>
    public async Task SyncGlobalAdminStatusAsync(string username, bool isGlobalAdmin)
    {
        var user = await GetUserByUsernameAsync(username);
        // Compare with explicitly nullable check
        if (user != null && (user.IsGlobalAdmin ?? false) != isGlobalAdmin)
        {
             // Force re-fetch tracking
             var dbUser = await _context.Users.FindAsync(user.UserId);
             if (dbUser != null)
             {
                 dbUser.IsGlobalAdmin = isGlobalAdmin;
                 await _context.SaveChangesAsync();
                 await InvalidateUserCacheAsync(username);
             }
        }
    }

    /// <summary>
    /// Invalidates all cache entries for a specific user
    /// </summary>
    private async Task InvalidateUserCacheAsync(string username)
    {
        await _cache.RemoveByPatternAsync($"user:username:{username.ToLower()}");
        // REMOVED: Permission cache clearing - permissions system has been removed
    }

    /// <summary>
    /// Invalidates all user-related cache entries
    /// </summary>
    public async Task InvalidateAllUserCacheAsync()
    {
        await _cache.RemoveByPatternAsync("user:");
    }
}
