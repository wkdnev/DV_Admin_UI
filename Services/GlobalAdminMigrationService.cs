using DV.Admin.UI.Data;
using DV.Shared.Security;
using DV.Admin.UI.Services;
using Microsoft.EntityFrameworkCore;

namespace DV.Admin.UI.Services;

/// <summary>
/// Utility service for migrating from UserRole-based admin to IsGlobalAdmin field
/// </summary>
public class GlobalAdminMigrationService
{
    private readonly SecurityDbContext _context;
    private readonly UserService _userService;
    private readonly RoleService _roleService;
    private readonly ILogger<GlobalAdminMigrationService> _logger;

    public GlobalAdminMigrationService(
        SecurityDbContext context,
        UserService userService,
        RoleService roleService,
        ILogger<GlobalAdminMigrationService> logger)
    {
        _context = context;
        _userService = userService;
        _roleService = roleService;
        _logger = logger;
    }

    /// <summary>
    /// Migrates users with Admin role from UserRole table to IsGlobalAdmin field
    /// </summary>
    public async Task<MigrationResult> MigrateAdminUsersAsync()
    {
        var result = new MigrationResult();

        try
        {
            _logger.LogInformation("Starting global admin migration from UserRole table to IsGlobalAdmin field");

            // Get the Admin role from the database
            var roles = await _roleService.GetAllRolesAsync();
            var adminRole = roles.FirstOrDefault(r => r.Name.Equals("Admin", StringComparison.OrdinalIgnoreCase));

            if (adminRole == null)
            {
                result.ErrorMessage = "Admin role not found in database";
                _logger.LogWarning("Admin role not found - cannot migrate");
                return result;
            }

            // Get users currently assigned to the Admin role via UserRole table
            List<ApplicationUser> usersWithAdminRole;
            try
            {
                usersWithAdminRole = await _userService.GetUsersInRoleAsync(adminRole.RoleId);
                _logger.LogInformation($"Found {usersWithAdminRole.Count} users with Admin role in UserRole table");
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"Could not query UserRole table (may have been dropped): {ex.Message}");
                usersWithAdminRole = new List<ApplicationUser>();
            }

            // Migrate each user
            foreach (var user in usersWithAdminRole)
            {
                try
                {
                    if (user.IsGlobalAdmin != true)
                    {
                        await _userService.SetGlobalAdminAsync(user.UserId, true);
                        result.MigratedUsers.Add(user);
                        _logger.LogInformation($"Migrated user {user.Username} to global admin via IsGlobalAdmin field");
                    }
                    else
                    {
                        result.AlreadyMigratedUsers.Add(user);
                        _logger.LogInformation($"User {user.Username} already has IsGlobalAdmin = true");
                    }
                }
                catch (Exception ex)
                {
                    result.FailedUsers.Add(new FailedMigration
                    {
                        User = user,
                        ErrorMessage = ex.Message
                    });
                    _logger.LogError(ex, $"Failed to migrate user {user.Username}");
                }
            }

            result.Success = true;
            _logger.LogInformation($"Migration completed. Migrated: {result.MigratedUsers.Count}, Already migrated: {result.AlreadyMigratedUsers.Count}, Failed: {result.FailedUsers.Count}");
        }
        catch (Exception ex)
        {
            result.ErrorMessage = ex.Message;
            _logger.LogError(ex, "Global admin migration failed");
        }

        return result;
    }

    /// <summary>
    /// Validates that all admin users have been properly migrated
    /// </summary>
    public async Task<ValidationResult> ValidateMigrationAsync()
    {
        var result = new ValidationResult();

        try
        {
            // Get users with IsGlobalAdmin = true
            var globalAdmins = await _userService.GetGlobalAdminsAsync();
            result.GlobalAdminUsers = globalAdmins;

            // Try to get users with Admin role (if UserRole table still exists)
            try
            {
                var roles = await _roleService.GetAllRolesAsync();
                var adminRole = roles.FirstOrDefault(r => r.Name.Equals("Admin", StringComparison.OrdinalIgnoreCase));
                
                if (adminRole != null)
                {
                    var roleBasedAdmins = await _userService.GetUsersInRoleAsync(adminRole.RoleId);
                    result.RoleBasedAdminUsers = roleBasedAdmins;

                    // Check for mismatches
                    var globalAdminIds = globalAdmins.Select(u => u.UserId).ToHashSet();
                    var roleAdminIds = roleBasedAdmins.Select(u => u.UserId).ToHashSet();

                    result.OnlyInGlobalAdmin = globalAdmins.Where(u => !roleAdminIds.Contains(u.UserId)).ToList();
                    result.OnlyInRoleTable = roleBasedAdmins.Where(u => !globalAdminIds.Contains(u.UserId)).ToList();
                    result.InBothSystems = globalAdmins.Where(u => roleAdminIds.Contains(u.UserId)).ToList();
                }
            }
            catch (Exception ex)
            {
                result.UserRoleTableExists = false;
                result.Notes = $"UserRole table not accessible: {ex.Message}";
            }

            result.Success = true;
        }
        catch (Exception ex)
        {
            result.ErrorMessage = ex.Message;
        }

        return result;
    }
}

/// <summary>
/// Result of the migration operation
/// </summary>
public class MigrationResult
{
    public bool Success { get; set; } = false;
    public string? ErrorMessage { get; set; }
    public List<ApplicationUser> MigratedUsers { get; set; } = new();
    public List<ApplicationUser> AlreadyMigratedUsers { get; set; } = new();
    public List<FailedMigration> FailedUsers { get; set; } = new();
}

/// <summary>
/// Information about a failed user migration
/// </summary>
public class FailedMigration
{
    public ApplicationUser User { get; set; } = null!;
    public string ErrorMessage { get; set; } = string.Empty;
}

/// <summary>
/// Result of migration validation
/// </summary>
public class ValidationResult
{
    public bool Success { get; set; } = false;
    public string? ErrorMessage { get; set; }
    public string? Notes { get; set; }
    public bool UserRoleTableExists { get; set; } = true;
    public List<ApplicationUser> GlobalAdminUsers { get; set; } = new();
    public List<ApplicationUser> RoleBasedAdminUsers { get; set; } = new();
    public List<ApplicationUser> OnlyInGlobalAdmin { get; set; } = new();
    public List<ApplicationUser> OnlyInRoleTable { get; set; } = new();
    public List<ApplicationUser> InBothSystems { get; set; } = new();
}
