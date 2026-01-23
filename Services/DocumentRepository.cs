using DV.Admin.UI.Data;
// ============================================================================
// DocumentRepository.cs - Data Access Layer for Document Viewer Application
// ============================================================================
//
// Purpose: Provides methods for interacting with the database to retrieve and 
// manipulate data related to documents, projects, and document pages. This 
// repository uses Entity Framework Core and supports schema-based project separation.
//
// Created: [Date]
// Last Updated: [Date]
//
// Dependencies:
// - Microsoft.EntityFrameworkCore: For Entity Framework Core operations.
// - DocViewer_Proto.Models: Contains the entity models and AppDbContext.
//
// Notes:
// - This repository supports asynchronous operations for better scalability.
// - Includes methods for searching, retrieving, and listing documents and projects.
// - Each project has its own schema with Document and DocumentPage tables.
// - Uses Entity Framework Core instead of Dapper for database operations.
// ============================================================================

using DV.Shared.Models; // Imports models like Document, Project, etc.
using DV.Admin.UI.Infrastructure.Caching;
using Microsoft.EntityFrameworkCore; // Provides EF Core functionality

namespace DV.Admin.UI.Services; 

// ============================================================================
// DocumentRepository Class
// ============================================================================
// Purpose: Acts as the data access layer for the application, providing methods 
// to interact with the database for documents, projects, and pages using 
// Entity Framework Core and schema-based project separation.
// ============================================================================
public class DocumentRepository
{
    private readonly AppDbContext _context; // Entity Framework context
    private readonly ICacheService _cache;

    // ========================================================================
    // Constructor: DocumentRepository
    // ========================================================================
    // Purpose: Initializes the repository with an Entity Framework context.
    // Parameters:
    // - context: An instance of AppDbContext for database operations.
    public DocumentRepository(AppDbContext context, ICacheService cache)
    {
        _context = context;
        _cache = cache;
    }

    // ========================================================================
    // Method: GetProjectsAsync (Overload)
    // ========================================================================
    // Purpose: Retrieves all projects from the database.
    // Returns: An IEnumerable of Project objects.
    public async Task<IEnumerable<Project>> GetProjectsAsync()
    {
        return await GetProjectsAsync("DefaultConnection"); // Call the overloaded method for compatibility
    }

    // ========================================================================
    // Method: GetProjectsAsync
    // ========================================================================
    // Purpose: Retrieves all projects from the database (database parameter kept for compatibility).
    // Parameters:
    // - database: The name of the database (ignored, kept for compatibility).
    // Returns: An IEnumerable of Project objects.
    public async Task<IEnumerable<Project>> GetProjectsAsync(string database)
    {
        return await _cache.GetOrSetAsync("projects:all", async () =>
        {
            return await _context.Projects
                .Where(p => p.IsActive)
                .OrderBy(p => p.ProjectName)
                .ToListAsync();
        }, TimeSpan.FromHours(1));
    }

    // ========================================================================
    // Method: GetProjectAsync
    // ========================================================================
    // Purpose: Retrieves a specific project by its ID.
    // Parameters:
    // - database: The name of the database (ignored, kept for compatibility).
    // - projectId: The ID of the project to retrieve.
    // Returns: A Project object or null if not found.
    public async Task<Project?> GetProjectAsync(string database, int projectId)
    {
        return await _cache.GetOrSetAsync($"project:id:{projectId}", async () =>
        {
            return await _context.Projects
                .FirstOrDefaultAsync(p => p.ProjectId == projectId);
        }, TimeSpan.FromHours(1));
    }

    // ========================================================================
    // Method: SearchAsync
    // ========================================================================
    // Purpose: Searches for documents based on a term, with pagination support.
    // Parameters:
    // - database: The name of the database (ignored, kept for compatibility).
    // - searchTerm: The term to search for in document fields.
    // - page: The page number for pagination (1-based).
    // - pageSize: The number of results per page.
    // - projectId: Optional project ID to filter results.
    // Returns: An IEnumerable of Document objects matching the search criteria.
    public async Task<IEnumerable<Document>> SearchAsync(string database, string? searchTerm, int page, int pageSize, int? projectId = null)
    {
        // First, get the project to determine the schema
        if (projectId.HasValue)
        {
            var project = await GetProjectAsync(database, projectId.Value);
            if (project != null && !string.IsNullOrEmpty(project.SchemaName))
            {
                return await SearchInSchemaAsync(project.SchemaName, searchTerm, page, pageSize, projectId);
            }
        }

        // If no specific project or project not found, search across all schemas
        return await SearchAcrossAllSchemasAsync(searchTerm, page, pageSize, projectId);
    }

    // ========================================================================
    // Method: SearchInSchemaAsync
    // ========================================================================
    // Purpose: Searches for documents within a specific project schema.
    private async Task<IEnumerable<Document>> SearchInSchemaAsync(string schemaName, string? searchTerm, int page, int pageSize, int? projectId)
    {
        var sql = $"SELECT * FROM [{schemaName}].[Document] WHERE 1=1";
        var parameters = new List<object>();

        if (projectId.HasValue)
        {
            sql += " AND ProjectId = {" + parameters.Count + "}";
            parameters.Add(projectId.Value);
        }

        if (!string.IsNullOrWhiteSpace(searchTerm))
        {
            sql += " AND (Title LIKE {" + parameters.Count + "} OR Author LIKE {" + (parameters.Count + 1) + "} OR DocumentNumber LIKE {" + (parameters.Count + 2) + "} OR Keywords LIKE {" + (parameters.Count + 3) + "})";
            var searchPattern = $"%{searchTerm}%";
            parameters.Add(searchPattern);
            parameters.Add(searchPattern);
            parameters.Add(searchPattern);
            parameters.Add(searchPattern);
        }

        sql += " ORDER BY CreatedOn DESC";
        sql += $" OFFSET {(page - 1) * pageSize} ROWS FETCH NEXT {pageSize} ROWS ONLY";

        return await _context.Database.SqlQueryRaw<Document>(sql, parameters.ToArray()).ToListAsync();
    }

    // ========================================================================
    // Method: SearchAcrossAllSchemasAsync
    // ========================================================================
    // Purpose: Searches for documents across all project schemas.
    private async Task<IEnumerable<Document>> SearchAcrossAllSchemasAsync(string? searchTerm, int page, int pageSize, int? projectId)
    {
        // Get all active projects with schemas
        var projects = await _context.Projects
            .Where(p => p.IsActive && !string.IsNullOrEmpty(p.SchemaName))
            .ToListAsync();

        if (!projects.Any())
        {
            return new List<Document>();
        }

        var allDocuments = new List<Document>();

        // Search in each schema
        foreach (var project in projects)
        {
            if (projectId.HasValue && project.ProjectId != projectId.Value)
                continue;

            try
            {
                var schemaDocuments = await SearchInSchemaAsync(project.SchemaName, searchTerm, 1, int.MaxValue, project.ProjectId);
                allDocuments.AddRange(schemaDocuments);
            }
            catch
            {
                // Skip schemas that don't exist or have issues
                continue;
            }
        }

        // Apply pagination to combined results
        return allDocuments
            .OrderByDescending(d => d.CreatedOn)
            .Skip((page - 1) * pageSize)
            .Take(pageSize);
    }

    // ========================================================================
    // Method: GetDocumentAsync
    // ========================================================================
    // Purpose: Retrieves a specific document by its ID.
    // Parameters:
    // - database: The name of the database (ignored, kept for compatibility).
    // - documentId: The ID of the document to retrieve.
    // Returns: A Document object or null if not found.
    public async Task<Document?> GetDocumentAsync(string database, int documentId)
    {
        // Try to find the document across all schemas
        var projects = await _context.Projects
            .Where(p => p.IsActive && !string.IsNullOrEmpty(p.SchemaName))
            .ToListAsync();

        foreach (var project in projects)
        {
            try
            {
                var sql = $"SELECT * FROM [{project.SchemaName}].[Document] WHERE DocumentId = {{0}}";
                var document = await _context.Database.SqlQueryRaw<Document>(sql, documentId).FirstOrDefaultAsync();
                if (document != null)
                {
                    return document;
                }
            }
            catch
            {
                // Continue to next schema if this one fails
                continue;
            }
        }

        return null;
    }

    // ========================================================================
    // Method: GetDocumentAsync (with schema)
    // ========================================================================
    // Purpose: Retrieves a specific document by its ID from a specific schema.
    public async Task<Document?> GetDocumentAsync(string database, string schemaName, int documentId)
    {
        var sql = $"SELECT * FROM [{schemaName}].[Document] WHERE DocumentId = {{0}}";
        return await _context.Database.SqlQueryRaw<Document>(sql, documentId).FirstOrDefaultAsync();
    }

    // ========================================================================
    // Method: GetPagesAsync
    // ========================================================================
    // Purpose: Retrieves all pages for a specific document.
    public async Task<IEnumerable<DocumentPage>> GetPagesAsync(string database, int documentId)
    {
        // Try to find the document pages across all schemas
        var projects = await _context.Projects
            .Where(p => p.IsActive && !string.IsNullOrEmpty(p.SchemaName))
            .ToListAsync();

        foreach (var project in projects)
        {
            try
            {
                var sql = $"SELECT * FROM [{project.SchemaName}].[DocumentPage] WHERE DocumentId = {{0}} ORDER BY PageNumber";
                var pages = await _context.Database.SqlQueryRaw<DocumentPage>(sql, documentId).ToListAsync();
                if (pages.Any())
                {
                    return pages;
                }
            }
            catch
            {
                // Continue to next schema if this one fails
                continue;
            }
        }

        return new List<DocumentPage>();
    }

    // ========================================================================
    // Method: GetDocumentPagesAsync (Alias for GetPagesAsync)
    // ========================================================================
    // Purpose: Alias for GetPagesAsync to support BLOB viewer component.
    public async Task<List<DocumentPage>> GetDocumentPagesAsync(string database, int documentId)
    {
        var pages = await GetPagesAsync(database, documentId);
        return pages.ToList();
    }

    // ========================================================================
    // Method: GetSchemaForDocumentAsync
    // ========================================================================
    // Purpose: Finds which schema contains a specific document.
    public async Task<string?> GetSchemaForDocumentAsync(string database, int documentId)
    {
        var projects = await _context.Projects
            .Where(p => p.IsActive && !string.IsNullOrEmpty(p.SchemaName))
            .ToListAsync();

        foreach (var project in projects)
        {
            try
            {
                var sql = $"SELECT COUNT(*) FROM [{project.SchemaName}].[Document] WHERE DocumentId = {{0}}";
                var count = await _context.Database.SqlQueryRaw<int>(sql, documentId).FirstOrDefaultAsync();
                if (count > 0)
                {
                    return project.SchemaName;
                }
            }
            catch
            {
                // Continue to next schema if this one fails
                continue;
            }
        }

        return null;
    }
}
