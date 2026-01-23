using DV.Admin.UI.Data;
// ============================================================================
// FirstUserAdminService.cs - Automatic First User Global Admin Setup
// ============================================================================
//
// Purpose: Automatically promotes the first user who logs in to Global Admin
// status. This simplifies initial application setup on new deployments.
//
// Usage: Called automatically during application startup and user authentication.
//
// Security Note: This service only promotes the FIRST user. Subsequent users
// will have normal access and must be granted admin rights by an existing admin.
//
// ============================================================================

using DV.Shared.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace DV.Admin.UI.Services;

public class FirstUserAdminService
{
    private readonly SecurityDbContext _securityContext;
    private readonly ILogger<FirstUserAdminService> _logger;
    private static readonly object _lock = new object();
    private static bool _hasCheckedFirstUser = false;

    public FirstUserAdminService(
        SecurityDbContext securityContext,
        ILogger<FirstUserAdminService> logger)
    {
        _securityContext = securityContext;
        _logger = logger;
    }

    /// <summary>
    /// Checks if this is the first user in the system and promotes them to Global Admin.
    /// This method is thread-safe and only executes once per application lifetime.
    /// </summary>
    public async Task<bool> EnsureFirstUserIsAdminAsync(string username, string displayName, string email)
    {
        // Double-check locking pattern to avoid race conditions
        if (_hasCheckedFirstUser)
        {
            return false; // Already processed first user
        }

        lock (_lock)
        {
            if (_hasCheckedFirstUser)
            {
                return false;
            }

            // Mark as checked immediately to prevent other threads from entering
            _hasCheckedFirstUser = true;
        }

        try
        {
            _logger.LogInformation("Checking if this is the first user login...");

            // Check if ANY users exist in the system
            var userCount = await _securityContext.Users.CountAsync();
            
            if (userCount == 0)
            {
                _logger.LogWarning("No users found in system. Creating first user as Global Administrator.");
                
                // Create the first user with Global Admin privileges
                var firstUser = new ApplicationUser
                {
                    Username = username,
                    DisplayName = displayName ?? ExtractDisplayNameFromUsername(username),
                    Email = email ?? GenerateEmailFromUsername(username),
                    IsActive = true,
                    IsGlobalAdmin = true, // IMPORTANT: First user gets admin rights
                    CreatedDate = DateTime.UtcNow,
                    LastLogin = DateTime.UtcNow
                };

                _securityContext.Users.Add(firstUser);
                await _securityContext.SaveChangesAsync();

                _logger.LogWarning(
                    "FIRST USER CREATED: {Username} has been granted Global Administrator privileges.",
                    username);

                return true;
            }
            else
            {
                _logger.LogInformation("First user already exists. Current user count: {UserCount}", userCount);
                
                // Check if the current user exists
                var existingUser = await _securityContext.Users
                    .FirstOrDefaultAsync(u => u.Username.ToLower() == username.ToLower());

                if (existingUser == null)
                {
                    // Create new user without admin privileges
                    _logger.LogInformation("Creating new user: {Username} (not first user, no auto-admin)", username);
                    
                    var newUser = new ApplicationUser
                    {
                        Username = username,
                        DisplayName = displayName ?? ExtractDisplayNameFromUsername(username),
                        Email = email ?? GenerateEmailFromUsername(username),
                        IsActive = true,
                        IsGlobalAdmin = false, // NOT first user, no auto-admin
                        CreatedDate = DateTime.UtcNow,
                        LastLogin = DateTime.UtcNow
                    };

                    _securityContext.Users.Add(newUser);
                    await _securityContext.SaveChangesAsync();
                }
                else
                {
                    // Update last login for existing user
                    existingUser.LastLogin = DateTime.UtcNow;
                    await _securityContext.SaveChangesAsync();
                    
                    _logger.LogInformation("Updated last login for user: {Username}", username);
                }

                return false;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during first user admin check for {Username}", username);
            throw;
        }
    }

    /// <summary>
    /// Checks if there are any Global Admins in the system.
    /// Useful for displaying warnings if no admins exist.
    /// </summary>
    public async Task<bool> HasGlobalAdminAsync()
    {
        try
        {
            return await _securityContext.Users
                .AnyAsync(u => u.IsGlobalAdmin == true && u.IsActive);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking for global admin existence");
            return false;
        }
    }

    /// <summary>
    /// Gets the count of Global Admins in the system.
    /// </summary>
    public async Task<int> GetGlobalAdminCountAsync()
    {
        try
        {
            return await _securityContext.Users
                .CountAsync(u => u.IsGlobalAdmin == true && u.IsActive);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error counting global admins");
            return 0;
        }
    }

    /// <summary>
    /// Gets information about the first user (oldest created user).
    /// </summary>
    public async Task<ApplicationUser?> GetFirstUserAsync()
    {
        try
        {
            return await _securityContext.Users
                .OrderBy(u => u.CreatedDate)
                .FirstOrDefaultAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving first user");
            return null;
        }
    }

    /// <summary>
    /// Resets the first user check flag. USE WITH CAUTION - only for testing!
    /// </summary>
    public static void ResetFirstUserCheck()
    {
        lock (_lock)
        {
            _hasCheckedFirstUser = false;
        }
    }

    // Helper method to extract display name from username
    private string ExtractDisplayNameFromUsername(string username)
    {
        // Handle domain\username format (e.g., "AD\john.doe")
        if (username.Contains('\\'))
        {
            var parts = username.Split('\\');
            username = parts[parts.Length - 1];
        }

        // Remove @ domain suffix if present (e.g., "john.doe@company.com")
        if (username.Contains('@'))
        {
            username = username.Split('@')[0];
        }

        // Convert to display name format: "john.doe" -> "John Doe"
        var nameParts = username.Split(new[] { '.', '_', '-' }, StringSplitOptions.RemoveEmptyEntries);
        var displayName = string.Join(" ", nameParts.Select(part => 
            char.ToUpper(part[0]) + part.Substring(1).ToLower()));

        return displayName;
    }

    // Helper method to generate a placeholder email from username
    private string GenerateEmailFromUsername(string username)
    {
        // Handle domain\username format
        if (username.Contains('\\'))
        {
            var parts = username.Split('\\');
            username = parts[parts.Length - 1];
        }

        // If already looks like an email, use it
        if (username.Contains('@'))
        {
            return username;
        }

        // Generate a placeholder email
        return $"{username.ToLower()}@company.local";
    }
}
