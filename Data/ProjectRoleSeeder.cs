using DV.Admin.UI.Security;
// ============================================================================
// ProjectRoleSeeder.cs - Seed Data for Project-Scoped RBAC
// ============================================================================
//
// Purpose: Provides sample data and initialization for the project-scoped
// role-based access control system. Creates sample project roles and 
// demonstrates the relationship between users, projects, and roles.
//
// Created: [Date]
// Last Updated: [Date]
//
// Dependencies:
// - DV.Admin.UI.Services: For ProjectRoleService and related services
// - DV.Admin.UI.Security: For security entities
// - DV.Admin.UI.Models: For Project entity
//
// Notes:
// - This seeder should be run after the database is created and migrated
// - Provides realistic sample data for testing and demonstration
// - Creates a complete project-scoped access control scenario
// ============================================================================

using DV.Admin.UI.Services;
using DV.Shared.Security;
using DV.Shared.Models;

namespace DV.Admin.UI.Data;

/// <summary>
/// Seeds sample data for the project-scoped RBAC system
/// </summary>
public class ProjectRoleSeeder
{
    private readonly ProjectRoleService _projectRoleService;
    private readonly UserService _userService;
    private readonly RoleService _roleService;
    // REMOVED: PermissionService - permissions system has been removed
    private readonly ProjectService _projectService;

    public ProjectRoleSeeder(
        ProjectRoleService projectRoleService,
        UserService userService,
        RoleService roleService,
        // REMOVED: PermissionService parameter
        ProjectService projectService)
    {
        _projectRoleService = projectRoleService;
        _userService = userService;
        _roleService = roleService;
        // REMOVED: _permissionService assignment
        _projectService = projectService;
    }

    /// <summary>
    /// Seeds the database with sample project roles and assignments
    /// </summary>
    public async Task SeedAsync()
    {
        try
        {
            Console.WriteLine("Starting project-scoped RBAC seeding...");
            
            await SeedAdditionalRolesIfNeeded();
            Console.WriteLine("? Additional roles seeded");
            
            await SeedSampleProjectsIfNeeded();
            Console.WriteLine("? Sample projects seeded");
            
            await SeedProjectRolesIfNeeded();
            Console.WriteLine("? Project roles seeded");
            
            await SeedSampleUserAssignments();
            Console.WriteLine("? Sample user assignments completed");
            
            Console.WriteLine("Project-scoped RBAC seeding completed successfully!");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error during RBAC seeding: {ex.Message}");
            throw;
        }
    }

    private async Task SeedAdditionalRolesIfNeeded()
    {
        var roles = await _roleService.GetAllRolesAsync();
        var roleNames = roles.Select(r => r.Name).ToHashSet();

        if (!roleNames.Contains("Auditor"))
        {
            await _roleService.CreateRoleAsync("Auditor", "Can audit documents and generate compliance reports");
        }

        if (!roleNames.Contains("Editor"))
        {
            await _roleService.CreateRoleAsync("Editor", "Can create and edit documents");
        }

        if (!roleNames.Contains("ReadOnly"))
        {
            await _roleService.CreateRoleAsync("ReadOnly", "Can view documents but not edit");
        }

        if (!roleNames.Contains("DataOwner"))
        {
            await _roleService.CreateRoleAsync("DataOwner", "Owns and governs data access and classification");
        }

        if (!roleNames.Contains("Security"))
        {
            await _roleService.CreateRoleAsync("Security", "Monitors security and access controls");
        }

        // Note: Admin role is intentionally excluded from project-scoped roles
        // Admin is a global system administrator role that operates outside project boundaries
    }

    private async Task SeedSampleProjectsIfNeeded()
    {
        var projects = await _projectService.GetAllProjectsAsync();
        var projectCodes = projects.Select(p => p.ProjectCode).ToHashSet();
        var schemaNames = projects.Select(p => p.SchemaName).ToHashSet();

        Console.WriteLine($"Found {projects.Count()} existing projects");
        Console.WriteLine($"Existing project codes: {string.Join(", ", projectCodes)}");
        Console.WriteLine($"Existing schema names: {string.Join(", ", schemaNames)}");

        // Check for Invoices project
        if (!projectCodes.Contains("INV001") && !schemaNames.Contains("invoices"))
        {
            Console.WriteLine("Creating Invoices project...");
            var invoicesProject = new Project(
                0,
                "INV001",
                "Invoices",
                "/data/invoices",
                "Finance Team",
                "invoices",
                "Financial invoices and billing documents",
                DateTime.UtcNow,
                true);

            await _projectService.CreateProjectAsync(invoicesProject);
            Console.WriteLine("? Invoices project created");
        }
        else
        {
            Console.WriteLine("Invoices project already exists (skipping)");
        }

        // Check for Correspondence project
        if (!projectCodes.Contains("COR001") && !schemaNames.Contains("correspondence"))
        {
            Console.WriteLine("Creating Correspondence project...");
            var correspondenceProject = new Project(
                0,
                "COR001",
                "Correspondence",
                "/data/correspondence",
                "Communications Team",
                "correspondence",
                "General business correspondence and communications",
                DateTime.UtcNow,
                true);

            await _projectService.CreateProjectAsync(correspondenceProject);
            Console.WriteLine("? Correspondence project created");
        }
        else
        {
            Console.WriteLine("Correspondence project already exists (skipping)");
        }

        // Check for HR project
        if (!projectCodes.Contains("HR001") && !schemaNames.Contains("hr"))
        {
            Console.WriteLine("Creating HR project...");
            var hrProject = new Project(
                0,
                "HR001",
                "Human Resources",
                "/data/hr",
                "HR Department",
                "hr",
                "Human resources documents and employee records",
                DateTime.UtcNow,
                true);

            await _projectService.CreateProjectAsync(hrProject);
            Console.WriteLine("? HR project created");
        }
        else
        {
            Console.WriteLine("HR project already exists (skipping)");
        }
    }

    private async Task SeedProjectRolesIfNeeded()
    {
        var projects = await _projectService.GetAllProjectsAsync();
        var roles = await _roleService.GetAllRolesAsync();

        // Filter out Admin role - it's a global system administrator role, not project-scoped
        var projectScopedRoles = roles.Where(r => r.Name != "Admin").ToList();

        Console.WriteLine($"Creating project roles for {projectScopedRoles.Count} roles (excluding Admin)");

        // Create project roles for each project
        foreach (var project in projects)
        {
            var existingProjectRoles = await _projectRoleService.GetProjectRolesAsync(project.ProjectId);
            var existingRoleNames = existingProjectRoles.Select(pr => pr.ApplicationRole?.Name).ToHashSet();

            foreach (var role in projectScopedRoles)
            {
                if (!existingRoleNames.Contains(role.Name))
                {
                    try
                    {
                        var projectRole = await _projectRoleService.CreateProjectRoleAsync(
                            project.ProjectId,
                            role.RoleId,
                            GetProjectSpecificRoleDescription(project.ProjectName, role.Name));

                        // REMOVED: Permission assignment - permissions system has been removed
                        // Authorization is now based on project roles only
                        
                        Console.WriteLine($"Created project role: {role.Name} for project: {project.ProjectName}");
                    }
                    catch (InvalidOperationException ex)
                    {
                        Console.WriteLine($"Role {role.Name} already exists for project {project.ProjectName}: {ex.Message}");
                    }
                }
                else
                {
                    Console.WriteLine($"Project role {role.Name} already exists for project {project.ProjectName}");
                }
            }
        }
    }

    private async Task SeedSampleUserAssignments()
    {
        // This would typically be done through the admin interface
        // For demo purposes, we can assign the current user to some roles
        
        var projects = await _projectService.GetAllProjectsAsync();
        var invoicesProject = projects.FirstOrDefault(p => p.ProjectCode == "INV001");
        var correspondenceProject = projects.FirstOrDefault(p => p.ProjectCode == "COR001");

        if (invoicesProject != null && correspondenceProject != null)
        {
            Console.WriteLine("Sample project roles created successfully!");
            Console.WriteLine($"Invoices Project ID: {invoicesProject.ProjectId}");
            Console.WriteLine($"Correspondence Project ID: {correspondenceProject.ProjectId}");
            Console.WriteLine("Use the Admin > Project Roles interface to assign users to project roles.");
        }
    }

    private string GetProjectSpecificRoleDescription(string projectName, string roleName)
    {
        return $"{roleName} role for the {projectName} project with project-specific permissions and access controls.";
    }

    // REMOVED: AssignProjectRolePermissions method - permissions system has been removed
}
