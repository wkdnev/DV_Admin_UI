using DV.Admin.UI.Data;
// ============================================================================
// ProjectRoleService.cs - Project-Scoped Role Management Service
// ============================================================================
//
// Purpose: Provides methods for managing project-scoped roles, user assignments,
// and permissions. This service enables project-level access control and 
// separation of duties across different projects.
//
// Created: [Date]
// Last Updated: [Date]
//
// Dependencies:
// - DocViewer_Proto.Security: For project role entities and security context
// - DocViewer_Proto.Models: For Project entity
// - Microsoft.EntityFrameworkCore: For database operations
//
// Notes:
// - This service coordinates project-specific role and permission management
// - Enables users to have different roles in different projects
// - Provides fine-grained access control at the project level
// ============================================================================

using DV.Shared.Security;
using DV.Shared.Models;
using Microsoft.EntityFrameworkCore;

namespace DV.Admin.UI.Services;

/// <summary>
/// Service for managing project-scoped roles, user assignments, and permissions
/// </summary>
public class ProjectRoleService
{
    private readonly SecurityDbContext _securityContext;
    private readonly AppDbContext _appContext;

    public ProjectRoleService(SecurityDbContext securityContext, AppDbContext appContext)
    {
        _securityContext = securityContext;
        _appContext = appContext;
    }

    // ========================================================================
    // Project Role Management
    // ========================================================================

    /// <summary>
    /// Creates a new project role based on an application role template
    /// </summary>
    public async Task<ProjectRole> CreateProjectRoleAsync(int projectId, int applicationRoleId, string? customDescription = null)
    {
        // Get the base application role
        var appRole = await _securityContext.Roles.FindAsync(applicationRoleId);
        if (appRole == null)
            throw new ArgumentException($"Application role with ID {applicationRoleId} not found");

        // Get the project name for display
        var project = await _appContext.Projects.FindAsync(projectId);
        if (project == null)
            throw new ArgumentException($"Project with ID {projectId} not found");

        // Check if this project role already exists
        var existing = await _securityContext.ProjectRoles
            .FirstOrDefaultAsync(pr => pr.ProjectId == projectId && pr.ApplicationRoleId == applicationRoleId);
        
        if (existing != null)
            throw new InvalidOperationException($"Project role '{appRole.Name}' already exists for project '{project.ProjectName}'");

        var projectRole = new ProjectRole
        {
            ProjectId = projectId,
            ApplicationRoleId = applicationRoleId,
            DisplayName = $"{project.ProjectName} {appRole.Name}",
            Description = customDescription ?? $"{appRole.Name} role for {project.ProjectName} project",
            IsActive = true,
            CreatedDate = DateTime.UtcNow
        };

        _securityContext.ProjectRoles.Add(projectRole);
        await _securityContext.SaveChangesAsync();

        return projectRole;
    }

    /// <summary>
    /// Gets all project roles for a specific project
    /// </summary>
    public async Task<List<ProjectRole>> GetProjectRolesAsync(int projectId)
    {
        return await _securityContext.ProjectRoles
            .Include(pr => pr.ApplicationRole)
            .Where(pr => pr.ProjectId == projectId && pr.IsActive)
            .OrderBy(pr => pr.DisplayName)
            .ToListAsync();
    }

    /// <summary>
    /// Gets all projects and their roles for a specific user
    /// </summary>
    public async Task<List<UserProjectAccess>> GetUserProjectAccessAsync(int userId)
    {
        var userProjectRoles = await _securityContext.UserProjectRoles
            .Include(upr => upr.ProjectRole)
            .AsNoTracking() // Don't track entities - read-only query
            // NOTE: .ThenInclude(pr => pr!.ApplicationRole) removed - ApplicationRole table no longer exists
            .Where(upr => upr.UserId == userId && upr.IsActive)
            .ToListAsync();

        var projectIds = userProjectRoles.Select(upr => upr.ProjectRole!.ProjectId).Distinct().ToList();
        
        // Get projects from the AppDbContext
        var projects = await _appContext.Projects
            .AsNoTracking() // Don't track entities - read-only query
            .Where(p => projectIds.Contains(p.ProjectId))
            .ToListAsync();

        var result = new List<UserProjectAccess>();
        foreach (var project in projects)
        {
            var projectRoles = userProjectRoles
                .Where(upr => upr.ProjectRole!.ProjectId == project.ProjectId)
                .Select(upr => upr.ProjectRole!)
                .ToList();

            result.Add(new UserProjectAccess
            {
                Project = project,
                ProjectRoles = projectRoles
            });
        }

        return result;
    }

    /// <summary>
    /// Gets all projects for statistics purposes
    /// </summary>
    public async Task<List<Project>> GetAllProjectsAsync()
    {
        return (await _appContext.Projects.ToListAsync());
    }

    // ========================================================================
    // User Assignment Management
    // ========================================================================

    /// <summary>
    /// Assigns a user to a project role
    /// </summary>
    public async Task AssignUserToProjectRoleAsync(int userId, int projectRoleId, string? assignedBy = null)
    {
        // Get the project role to validate project access
        var projectRole = await _securityContext.ProjectRoles
            .FirstOrDefaultAsync(pr => pr.ProjectRoleId == projectRoleId);

        if (projectRole == null)
        {
            throw new InvalidOperationException("Project role not found");
        }

        // Access validation removed
        // await _userProjectAccessService.ValidateUserProjectAccessAsync(userId, projectRole.ProjectId);

        // Check if assignment already exists
        var existing = await _securityContext.UserProjectRoles
            .FirstOrDefaultAsync(upr => upr.UserId == userId && upr.ProjectRoleId == projectRoleId);

        if (existing != null)
        {
            if (existing.IsActive)
                throw new InvalidOperationException("User is already assigned to this project role");
            
            // Reactivate the assignment
            existing.IsActive = true;
            existing.AssignedDate = DateTime.UtcNow;
            existing.AssignedBy = assignedBy;
        }
        else
        {
            var assignment = new UserProjectRole
            {
                UserId = userId,
                ProjectRoleId = projectRoleId,
                AssignedDate = DateTime.UtcNow,
                AssignedBy = assignedBy,
                IsActive = true
            };

            _securityContext.UserProjectRoles.Add(assignment);
        }

        await _securityContext.SaveChangesAsync();
    }

    /// <summary>
    /// Removes a user from a project role
    /// </summary>
    public async Task RemoveUserFromProjectRoleAsync(int userId, int projectRoleId)
    {
        var assignment = await _securityContext.UserProjectRoles
            .FirstOrDefaultAsync(upr => upr.UserId == userId && upr.ProjectRoleId == projectRoleId);

        if (assignment != null)
        {
            assignment.IsActive = false;
            await _securityContext.SaveChangesAsync();
        }
    }

    /// <summary>
    /// Gets all users assigned to a specific project role
    /// </summary>
    public async Task<List<ApplicationUser>> GetUsersInProjectRoleAsync(int projectRoleId)
    {
        return await _securityContext.UserProjectRoles
            .Include(upr => upr.User)
            .Where(upr => upr.ProjectRoleId == projectRoleId && upr.IsActive)
            .Select(upr => upr.User!)
            .ToListAsync();
    }

    // ========================================================================
    // ========================================================================
    // REMOVED: Permission Management section
    // The permissions system has been removed - authorization is now based on project roles only
    // ========================================================================


    /// <summary>
    /// Gets all projects a user has access to
    /// </summary>
    public async Task<List<int>> GetUserAccessibleProjectsAsync(int userId)
    {
        return await _securityContext.UserProjectRoles
            .Include(upr => upr.ProjectRole)
            .Where(upr => upr.UserId == userId && upr.IsActive && upr.ProjectRole!.IsActive)
            .Select(upr => upr.ProjectRole!.ProjectId)
            .Distinct()
            .ToListAsync();
    }

    // ========================================================================
    // Project Role Deletion
    // ========================================================================

    /// <summary>
    /// Deletes a project role and all its assignments
    /// </summary>
    public async Task DeleteProjectRoleAsync(int projectRoleId)
    {
        using var transaction = await _securityContext.Database.BeginTransactionAsync();
        try
        {
            // First, deactivate all user assignments for this project role
            var userAssignments = await _securityContext.UserProjectRoles
                .Where(upr => upr.ProjectRoleId == projectRoleId)
                .ToListAsync();

            foreach (var assignment in userAssignments)
            {
                assignment.IsActive = false;
            }

            // Finally, delete the project role itself
            var projectRole = await _securityContext.ProjectRoles.FindAsync(projectRoleId);
            if (projectRole != null)
            {
                _securityContext.ProjectRoles.Remove(projectRole);
            }

            await _securityContext.SaveChangesAsync();
            await transaction.CommitAsync();
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }
}

/// <summary>
/// Helper class to represent a user's access to a project
/// </summary>
public class UserProjectAccess
{
    public Project Project { get; set; } = null!;
    public List<ProjectRole> ProjectRoles { get; set; } = new();
}
