using DV.Admin.UI.Data;
// ============================================================================
// ProjectService.cs - Project Management Service for Document Viewer Application
// ============================================================================
//
// Purpose: Provides methods for managing projects and their associated schemas.
// This service handles the creation, updating, and deletion of projects along
// with their corresponding database schemas.
//
// Created: [Date]
// Last Updated: [Date]
//
// Dependencies:
// - DocViewer_Proto.Models: Contains the Project entity and AppDbContext.
// - DocViewer_Proto.Services: Contains SchemaService for schema management.
// - Microsoft.EntityFrameworkCore: For Entity Framework Core operations.
//
// Notes:
// - This service coordinates between project metadata and schema creation.
// - Ensures data consistency between projects and their schemas.
// - Uses Entity Framework Core with proper change tracking for record types.
// ============================================================================

using DV.Shared.Models;
using Microsoft.EntityFrameworkCore;

namespace DV.Admin.UI.Services;

// ============================================================================
// ProjectService Class
// ============================================================================
// Purpose: Manages projects and their associated database schemas using EF Core.
// ============================================================================
public class ProjectService
{
    private readonly AppDbContext _context;
    private readonly SchemaService _schemaService;

    public ProjectService(AppDbContext context, SchemaService schemaService)
    {
        _context = context;
        _schemaService = schemaService;
    }

    // ========================================================================
    // Method: GetAllProjectsAsync
    // ========================================================================
    // Purpose: Retrieves all projects from the database.
    public async Task<IEnumerable<Project>> GetAllProjectsAsync()
    {
        return await _context.Projects
            .AsNoTracking() // No tracking needed for read operations
            .OrderBy(p => p.ProjectName)
            .ToListAsync();
    }

    // ========================================================================
    // Method: GetActiveProjectsAsync
    // ========================================================================
    // Purpose: Retrieves all active projects from the database.
    public async Task<IEnumerable<Project>> GetActiveProjectsAsync()
    {
        return await _context.Projects
            .AsNoTracking() // No tracking needed for read operations
            .Where(p => p.IsActive)
            .OrderBy(p => p.ProjectName)
            .ToListAsync();
    }

    // ========================================================================
    // Method: GetProjectByIdAsync
    // ========================================================================
    // Purpose: Retrieves a specific project by its ID.
    public async Task<Project?> GetProjectByIdAsync(int projectId)
    {
        return await _context.Projects
            .AsNoTracking() // No tracking needed for read operations
            .FirstOrDefaultAsync(p => p.ProjectId == projectId);
    }

    // ========================================================================
    // Method: GetProjectBySchemaNameAsync
    // ========================================================================
    // Purpose: Retrieves a project by its schema name.
    public async Task<Project?> GetProjectBySchemaNameAsync(string schemaName)
    {
        return await _context.Projects
            .AsNoTracking() // No tracking needed for read operations
            .FirstOrDefaultAsync(p => p.SchemaName == schemaName);
    }

    // ========================================================================
    // Method: CreateProjectAsync
    // ========================================================================
    // Purpose: Creates a new project with its associated database schema.
    public async Task<int> CreateProjectAsync(Project project)
    {
        // Validate schema name
        if (string.IsNullOrWhiteSpace(project.SchemaName))
        {
            throw new ArgumentException("Schema name is required for project creation.");
        }

        // Check if schema name is already in use
        var existingProject = await GetProjectBySchemaNameAsync(project.SchemaName);
        if (existingProject != null)
        {
            throw new InvalidOperationException($"A project with schema name '{project.SchemaName}' already exists.");
        }

        // Use EF's execution strategy to handle retries and transactions properly
        var strategy = _context.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                // Add the project record
                _context.Projects.Add(project);
                await _context.SaveChangesAsync();

                // Create the database schema within the existing transaction
                // Pass _context so SchemaService uses the same connection/transaction
                await _schemaService.CreateProjectSchemaAsync(project.SchemaName, useExistingTransaction: true, sharedContext: _context);

                await transaction.CommitAsync();
                return project.ProjectId;
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }
        });
    }

    // ========================================================================
    // Method: UpdateProjectAsync
    // ========================================================================
    // Purpose: Updates an existing project (schema name cannot be changed).
    public async Task UpdateProjectAsync(Project project)
    {
        // Use ExecuteUpdate for better performance with record types
        var rowsAffected = await _context.Projects
            .Where(p => p.ProjectId == project.ProjectId)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(p => p.ProjectCode, project.ProjectCode)
                .SetProperty(p => p.ProjectName, project.ProjectName)
                .SetProperty(p => p.FolderPath, project.FolderPath)
                .SetProperty(p => p.ReadPrincipal, project.ReadPrincipal)
                .SetProperty(p => p.EditPrincipal, project.EditPrincipal)
                .SetProperty(p => p.Description, project.Description)
                .SetProperty(p => p.IsActive, project.IsActive)
            );

        if (rowsAffected == 0)
        {
            throw new InvalidOperationException($"Project with ID {project.ProjectId} not found.");
        }
    }

    // ========================================================================
    // Method: DeleteProjectAsync
    // ========================================================================
    // Purpose: Deletes a project and its associated schema.
    public async Task DeleteProjectAsync(int projectId)
    {
        var project = await GetProjectByIdAsync(projectId);
        if (project == null)
        {
            throw new InvalidOperationException($"Project with ID {projectId} not found.");
        }

        // Use EF's execution strategy to handle retries and transactions properly
        var strategy = _context.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                // Delete the project record using ExecuteDelete for efficiency
                var rowsDeleted = await _context.Projects
                    .Where(p => p.ProjectId == projectId)
                    .ExecuteDeleteAsync();

                if (rowsDeleted == 0)
                {
                    throw new InvalidOperationException($"Project with ID {projectId} not found.");
                }

                // Drop the database schema within the existing transaction
                if (!string.IsNullOrEmpty(project.SchemaName))
                {
                    await _schemaService.DropProjectSchemaAsync(project.SchemaName, useExistingTransaction: true, sharedContext: _context);
                }

                await transaction.CommitAsync();
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }
        });
    }

    // ========================================================================
    // Method: DeactivateProjectAsync
    // ========================================================================
    // Purpose: Deactivates a project without deleting it or its schema.
    public async Task DeactivateProjectAsync(int projectId)
    {
        var rowsAffected = await _context.Projects
            .Where(p => p.ProjectId == projectId)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(p => p.IsActive, false)
            );

        if (rowsAffected == 0)
        {
            throw new InvalidOperationException($"Project with ID {projectId} not found.");
        }
    }

    // ========================================================================
    // Method: ActivateProjectAsync
    // ========================================================================
    // Purpose: Activates a previously deactivated project.
    public async Task ActivateProjectAsync(int projectId)
    {
        var rowsAffected = await _context.Projects
            .Where(p => p.ProjectId == projectId)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(p => p.IsActive, true)
            );

        if (rowsAffected == 0)
        {
            throw new InvalidOperationException($"Project with ID {projectId} not found.");
        }
    }

    // ========================================================================
    // Method: ValidateSchemaNameAsync
    // ========================================================================
    // Purpose: Validates if a schema name is available for use.
    public async Task<bool> ValidateSchemaNameAsync(string schemaName)
    {
        // Check if the schema name is already used by a project
        var existingProject = await GetProjectBySchemaNameAsync(schemaName);
        if (existingProject != null)
        {
            return false;
        }

        // Check if the schema exists in the database
        var schemaExists = await _schemaService.SchemaExistsAsync(schemaName);
        return !schemaExists;
    }
}
