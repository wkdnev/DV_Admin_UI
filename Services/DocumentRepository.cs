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

    // Explicit column lists to avoid SELECT * (prevents loading unnecessary data)
    private const string DocumentColumns = "\"DocumentId\", \"ProjectId\", \"DocumentIndex\", \"Issue\", \"DocumentStatus\", \"DocumentNumber\", \"Title\", \"Author\", \"DocumentDate\", \"Keywords\", \"Memo\", \"DocumentType\", \"OldDM\", \"CM\", \"GM\", \"EM\", \"Text01\", \"Text02\", \"Text03\", \"Text04\", \"Text05\", \"Text06\", \"Text07\", \"Text08\", \"Text09\", \"Text10\", \"Text11\", \"Text12\", \"Date01\", \"Date02\", \"Date03\", \"Date04\", \"Boolean01\", \"Boolean02\", \"Boolean03\", \"Number01\", \"Number02\", \"Number03\", \"Version\", \"Status\", \"Classification\", \"FilePath\", \"CreatedOn\", \"CreatedBy\", \"ModifiedOn\", \"ModifiedBy\", \"PublicToken\"";
    private const string DocumentListColumns = "\"DocumentId\", \"ProjectId\", \"DocumentNumber\", \"Title\", \"Author\", \"DocumentDate\", \"DocumentType\", \"Status\", \"Classification\", \"Keywords\", \"CreatedOn\", \"CreatedBy\", \"ModifiedOn\", \"ModifiedBy\", \"PublicToken\"";
    private const string PageColumnsNoBlob = "\"PageId\", \"DocumentId\", \"DocumentIndex\", \"PageNumber\", \"PageReference\", \"FrameNumber\", \"Level1\", \"Level2\", \"Level3\", \"Level4\", \"DiskNumber\", \"FileName\", \"FilePath\", \"FileType\", CAST(NULL AS bytea) AS \"FileContent\", \"FileSize\", \"FileFormat\", \"PageSize\", \"ContentType\", \"UploadedDate\", \"ChecksumMD5\", \"StorageType\", \"CreatedOn\", \"CreatedBy\", \"ModifiedOn\", \"ModifiedBy\"";

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
        var sql = $"SELECT {DocumentColumns} FROM \"{schemaName}\".\"Document\" WHERE 1=1";
        var parameters = new List<object>();

        if (projectId.HasValue)
        {
            sql += " AND \"ProjectId\" = {" + parameters.Count + "}";
            parameters.Add(projectId.Value);
        }

        if (!string.IsNullOrWhiteSpace(searchTerm))
        {
            sql += " AND (\"Title\" LIKE {" + parameters.Count + "} OR \"Author\" LIKE {" + (parameters.Count + 1) + "} OR \"DocumentNumber\" LIKE {" + (parameters.Count + 2) + "} OR \"Keywords\" LIKE {" + (parameters.Count + 3) + "})";
            var searchPattern = $"%{searchTerm}%";
            parameters.Add(searchPattern);
            parameters.Add(searchPattern);
            parameters.Add(searchPattern);
            parameters.Add(searchPattern);
        }

        sql += " ORDER BY \"CreatedOn\" DESC";
        sql += $" LIMIT {pageSize} OFFSET {(page - 1) * pageSize}";

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

        // Cap per-schema results to avoid loading entire tables into memory
        var perSchemaLimit = pageSize * 3;

        // Query each schema in parallel with capped results
        var tasks = projects
            .Where(p => !projectId.HasValue || p.ProjectId == projectId.Value)
            .Select(project => SafeSearchInSchemaAsync(project.SchemaName, searchTerm, 1, perSchemaLimit, project.ProjectId));

        var results = await Task.WhenAll(tasks);

        // Merge and paginate combined results
        return results
            .SelectMany(r => r)
            .OrderByDescending(d => d.CreatedOn)
            .Skip((page - 1) * pageSize)
            .Take(pageSize);
    }

    private async Task<IEnumerable<Document>> SafeSearchInSchemaAsync(string schemaName, string? searchTerm, int page, int pageSize, int? projectId)
    {
        try
        {
            return await SearchInSchemaAsync(schemaName, searchTerm, page, pageSize, projectId);
        }
        catch
        {
            return Enumerable.Empty<Document>();
        }
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
        // Build a single UNION ALL query across all schemas instead of N+1 queries
        var projects = await _cache.GetOrSetAsync("projects:active-schemas", async () =>
        {
            return await _context.Projects
                .Where(p => p.IsActive && !string.IsNullOrEmpty(p.SchemaName))
                .Select(p => p.SchemaName)
                .ToListAsync();
        }, TimeSpan.FromMinutes(10));

        if (!projects.Any())
            return null;

        var unionParts = projects.Select(schema =>
            $"SELECT {DocumentColumns} FROM \"{schema}\".\"Document\" WHERE \"DocumentId\" = {{0}}");
        var sql = string.Join(" UNION ALL ", unionParts);

        return await _context.Database.SqlQueryRaw<Document>(sql, documentId).FirstOrDefaultAsync();
    }

    // ========================================================================
    // Method: GetDocumentAsync (with schema)
    // ========================================================================
    // Purpose: Retrieves a specific document by its ID from a specific schema.
    public async Task<Document?> GetDocumentAsync(string database, string schemaName, int documentId)
    {
        var sql = $"SELECT {DocumentColumns} FROM \"{schemaName}\".\"Document\" WHERE \"DocumentId\" = {{0}}";
        return await _context.Database.SqlQueryRaw<Document>(sql, documentId).FirstOrDefaultAsync();
    }

    // ========================================================================
    // Method: GetDocumentByTokenAsync
    // ========================================================================
    // Purpose: Retrieves a document by its opaque public token (UNION ALL across all schemas).
    // Note: Document.SchemaName is [NotMapped] so SqlQueryRaw won't populate it.
    //       We look up the schema from the Project table after the query.
    public async Task<Document?> GetDocumentByTokenAsync(string token)
    {
        var schemas = await _cache.GetOrSetAsync("projects:active-schemas", async () =>
        {
            return await _context.Projects
                .Where(p => p.IsActive && !string.IsNullOrEmpty(p.SchemaName))
                .Select(p => p.SchemaName)
                .ToListAsync();
        }, TimeSpan.FromMinutes(10));

        if (!schemas.Any())
            return null;

        var unionParts = schemas.Select(schema =>
            $"SELECT {DocumentColumns} FROM \"{schema}\".\"Document\" WHERE \"PublicToken\" = {{0}}");
        var sql = string.Join(" UNION ALL ", unionParts);

        var doc = await _context.Database.SqlQueryRaw<Document>(sql, token).FirstOrDefaultAsync();

        if (doc?.ProjectId != null)
        {
            var project = await _context.Projects.FirstOrDefaultAsync(p => p.ProjectId == doc.ProjectId.Value);
            doc.SchemaName = project?.SchemaName;
        }

        return doc;
    }

    // ========================================================================
    // Method: BackfillDocumentTokensAsync
    // ========================================================================
    // Purpose: Generates PublicToken for any documents that don't have one yet.
    public async Task<int> BackfillDocumentTokensAsync()
    {
        var schemas = await _context.Projects
            .Where(p => p.IsActive && !string.IsNullOrEmpty(p.SchemaName))
            .Select(p => p.SchemaName)
            .ToListAsync();

        int totalUpdated = 0;
        foreach (var schema in schemas)
        {
            try
            {
                var docs = await _context.Database
                    .SqlQueryRaw<Document>($"SELECT {DocumentColumns} FROM \"{schema}\".\"Document\" WHERE \"PublicToken\" IS NULL")
                    .ToListAsync();

                foreach (var doc in docs)
                {
                    var token = DV.Shared.Constants.DocumentTokenGenerator.GenerateToken();
                    await _context.Database.ExecuteSqlRawAsync(
                        $"UPDATE \"{schema}\".\"Document\" SET \"PublicToken\" = {{0}} WHERE \"DocumentId\" = {{1}}",
                        token, doc.DocumentId);
                    totalUpdated++;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"BackfillDocumentTokensAsync: Error in schema '{schema}': {ex.Message}");
            }
        }

        return totalUpdated;
    }

    // ========================================================================
    // Method: GetPagesAsync
    // ========================================================================
    // Purpose: Retrieves all pages for a specific document.
    public async Task<IEnumerable<DocumentPage>> GetPagesAsync(string database, int documentId)
    {
        // Build a single UNION ALL query across all schemas instead of N+1 queries
        var schemas = await _cache.GetOrSetAsync("projects:active-schemas", async () =>
        {
            return await _context.Projects
                .Where(p => p.IsActive && !string.IsNullOrEmpty(p.SchemaName))
                .Select(p => p.SchemaName)
                .ToListAsync();
        }, TimeSpan.FromMinutes(10));

        if (!schemas.Any())
            return new List<DocumentPage>();

        var unionParts = schemas.Select(schema =>
            $"SELECT {PageColumnsNoBlob} FROM \"{schema}\".\"DocumentPage\" WHERE \"DocumentId\" = {{0}}");
        var sql = string.Join(" UNION ALL ", unionParts) + " ORDER BY \"PageNumber\"";

        return await _context.Database.SqlQueryRaw<DocumentPage>(sql, documentId).ToListAsync();
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

        // Build a single UNION ALL query to find the schema in one round-trip
        var unionParts = projects.Select(project =>
            $"SELECT '{project.SchemaName}' AS \"Value\" FROM \"{project.SchemaName}\".\"Document\" WHERE \"DocumentId\" = {{0}}");
        var sql = string.Join(" UNION ALL ", unionParts);

        if (!projects.Any())
            return null;

        return await _context.Database.SqlQueryRaw<string>(sql, documentId).FirstOrDefaultAsync();
    }

    // ========================================================================
    // Method: CountInSchemaAsync
    // ========================================================================
    // Purpose: Returns total document count in a schema, with optional filters.
    public async Task<int> CountInSchemaAsync(string schemaName, string? searchTerm = null, string? status = null, string? documentType = null, string? classification = null, DateTime? dateFrom = null, DateTime? dateTo = null)
    {
        var sql = $"SELECT COUNT(*) AS \"Value\" FROM \"{schemaName}\".\"Document\" WHERE 1=1";
        var parameters = new List<object>();

        AppendFilterClauses(ref sql, parameters, searchTerm, status, documentType, classification, dateFrom, dateTo);

        return await _context.Database.SqlQueryRaw<int>(sql, parameters.ToArray()).FirstOrDefaultAsync();
    }

    // ========================================================================
    // Method: SearchInSchemaAdvancedAsync
    // ========================================================================
    // Purpose: Advanced search with sorting, filtering, and server-side pagination.
    public async Task<IEnumerable<Document>> SearchInSchemaAdvancedAsync(string schemaName, string? searchTerm, int page, int pageSize, string sortColumn = "CreatedOn", bool sortAscending = false, string? status = null, string? documentType = null, string? classification = null, DateTime? dateFrom = null, DateTime? dateTo = null)
    {
        // Whitelist sort columns to prevent SQL injection
        var allowedSortColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "DocumentId", "DocumentNumber", "Title", "Author", "DocumentDate", "DocumentType",
            "Status", "Classification", "CreatedOn", "ModifiedOn", "DocumentIndex", "Issue", "Version"
        };

        if (!allowedSortColumns.Contains(sortColumn))
            sortColumn = "CreatedOn";

        var sortDirection = sortAscending ? "ASC" : "DESC";

        var sql = $"SELECT {DocumentColumns} FROM \"{schemaName}\".\"Document\" WHERE 1=1";
        var parameters = new List<object>();

        AppendFilterClauses(ref sql, parameters, searchTerm, status, documentType, classification, dateFrom, dateTo);

        sql += $" ORDER BY \"{sortColumn}\" {sortDirection}";
        sql += $" LIMIT {pageSize} OFFSET {(page - 1) * pageSize}";

        return await _context.Database.SqlQueryRaw<Document>(sql, parameters.ToArray()).ToListAsync();
    }

    // ========================================================================
    // Method: GetDistinctValuesAsync
    // ========================================================================
    // Purpose: Gets distinct values for a column (for filter dropdowns).
    public async Task<List<string>> GetDistinctValuesAsync(string schemaName, string columnName)
    {
        var allowedColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Status", "DocumentType", "Classification", "Author"
        };

        if (!allowedColumns.Contains(columnName))
            return new List<string>();

        var sql = $"SELECT DISTINCT \"{columnName}\" AS \"Value\" FROM \"{schemaName}\".\"Document\" WHERE \"{columnName}\" IS NOT NULL AND \"{columnName}\" != '' ORDER BY \"{columnName}\"";
        return await _context.Database.SqlQueryRaw<string>(sql).ToListAsync();
    }

    // ========================================================================
    // Method: GetSchemaStatsAsync
    // ========================================================================
    // Purpose: Gets document statistics for a schema.
    public async Task<DocumentSchemaStats> GetSchemaStatsAsync(string schemaName)
    {
        var stats = new DocumentSchemaStats();

        try
        {
            stats.TotalDocuments = await _context.Database
                .SqlQueryRaw<int>($"SELECT COUNT(*) AS \"Value\" FROM \"{schemaName}\".\"Document\"")
                .FirstOrDefaultAsync();

            stats.TotalPages = await _context.Database
                .SqlQueryRaw<int>($"SELECT COUNT(*) AS \"Value\" FROM \"{schemaName}\".\"DocumentPage\"")
                .FirstOrDefaultAsync();

            stats.BlobPages = await _context.Database
                .SqlQueryRaw<int>($"SELECT COUNT(*) AS \"Value\" FROM \"{schemaName}\".\"DocumentPage\" WHERE \"StorageType\" = 1")
                .FirstOrDefaultAsync();

            stats.TotalStorageBytes = await _context.Database
                .SqlQueryRaw<long>($"SELECT COALESCE(SUM(COALESCE(\"FileSize\", 0)), 0) AS \"Value\" FROM \"{schemaName}\".\"DocumentPage\"")
                .FirstOrDefaultAsync();

            // Status breakdown
            var statusCounts = await _context.Database
                .SqlQueryRaw<StatusCount>($"SELECT COALESCE(\"Status\", 'Unknown') AS \"Name\", COUNT(*) AS \"Count\" FROM \"{schemaName}\".\"Document\" GROUP BY \"Status\"")
                .ToListAsync();
            stats.StatusBreakdown = statusCounts.ToDictionary(s => s.Name, s => s.Count);
        }
        catch
        {
            // Schema might not have all tables
        }

        return stats;
    }

    // ========================================================================
    // Method: CreateDocumentAsync
    // ========================================================================
    // Purpose: Creates a new document in a schema (metadata only, no file upload).
    public async Task<int> CreateDocumentAsync(string schemaName, Document document, int userId)
    {
        var sql = $@"
            INSERT INTO ""{schemaName}"".""Document"" 
            (""ProjectId"", ""DocumentNumber"", ""Title"", ""Author"", ""DocumentDate"", ""DocumentType"", ""Status"", ""Classification"",
             ""Keywords"", ""Memo"", ""DocumentIndex"", ""Issue"", ""Version"", ""FilePath"",
             ""Text01"", ""Text02"", ""Text03"", ""Text04"", ""Text05"", ""Text06"", ""Text07"", ""Text08"", ""Text09"", ""Text10"", ""Text11"", ""Text12"",
             ""Date01"", ""Date02"", ""Date03"", ""Date04"",
             ""Boolean01"", ""Boolean02"", ""Boolean03"",
             ""Number01"", ""Number02"", ""Number03"",
             ""OldDM"", ""CM"", ""GM"", ""EM"",
             ""PublicToken"", ""CreatedOn"", ""CreatedBy"", ""ModifiedOn"", ""ModifiedBy"")
            VALUES 
            ({{0}}, {{1}}, {{2}}, {{3}}, {{4}}, {{5}}, {{6}}, {{7}},
             {{8}}, {{9}}, {{10}}, {{11}}, {{12}}, {{13}},
             {{14}}, {{15}}, {{16}}, {{17}}, {{18}}, {{19}}, {{20}}, {{21}}, {{22}}, {{23}}, {{24}}, {{25}},
             {{26}}, {{27}}, {{28}}, {{29}},
             {{30}}, {{31}}, {{32}},
             {{33}}, {{34}}, {{35}},
             {{36}}, {{37}}, {{38}}, {{39}},
             {{40}}, {{41}}, {{42}}, {{43}}, {{44}})
            RETURNING ""DocumentId""";

        var now = DateTime.UtcNow;
        var parameters = new object?[]
        {
            document.ProjectId ?? (object)DBNull.Value,
            string.IsNullOrWhiteSpace(document.DocumentNumber) ? $"DOC_{now:yyyyMMdd}_{Guid.NewGuid():N}" : document.DocumentNumber,
            document.Title ?? (object)DBNull.Value,
            document.Author ?? (object)DBNull.Value,
            document.DocumentDate ?? (object)DBNull.Value,
            document.DocumentType ?? (object)DBNull.Value,
            document.Status ?? (object)DBNull.Value,
            document.Classification ?? (object)DBNull.Value,
            document.Keywords ?? (object)DBNull.Value,
            document.Memo ?? (object)DBNull.Value,
            document.DocumentIndex ?? (object)DBNull.Value,
            document.Issue ?? (object)DBNull.Value,
            document.Version ?? (object)DBNull.Value,
            document.FilePath ?? (object)DBNull.Value,
            document.Text01 ?? (object)DBNull.Value, document.Text02 ?? (object)DBNull.Value,
            document.Text03 ?? (object)DBNull.Value, document.Text04 ?? (object)DBNull.Value,
            document.Text05 ?? (object)DBNull.Value, document.Text06 ?? (object)DBNull.Value,
            document.Text07 ?? (object)DBNull.Value, document.Text08 ?? (object)DBNull.Value,
            document.Text09 ?? (object)DBNull.Value, document.Text10 ?? (object)DBNull.Value,
            document.Text11 ?? (object)DBNull.Value, document.Text12 ?? (object)DBNull.Value,
            document.Date01 ?? (object)DBNull.Value, document.Date02 ?? (object)DBNull.Value,
            document.Date03 ?? (object)DBNull.Value, document.Date04 ?? (object)DBNull.Value,
            document.Boolean01 ?? (object)DBNull.Value, document.Boolean02 ?? (object)DBNull.Value,
            document.Boolean03 ?? (object)DBNull.Value,
            document.Number01 ?? (object)DBNull.Value, document.Number02 ?? (object)DBNull.Value,
            document.Number03 ?? (object)DBNull.Value,
            document.OldDM ?? (object)DBNull.Value, document.CM ?? (object)DBNull.Value,
            document.GM ?? (object)DBNull.Value, document.EM ?? (object)DBNull.Value,
            DV.Shared.Constants.DocumentTokenGenerator.GenerateToken(),
            now, userId, now, userId
        };

        return await _context.Database.SqlQueryRaw<int>(sql, parameters!).FirstOrDefaultAsync();
    }

    // ========================================================================
    // Method: UpdateDocumentAsync
    // ========================================================================
    // Purpose: Updates all metadata fields for a document in a schema.
    public async Task<int> UpdateDocumentAsync(string schemaName, Document document, int userId)
    {
        var sql = $@"
            UPDATE ""{schemaName}"".""Document"" SET
                ""DocumentNumber"" = {{0}}, ""Title"" = {{1}}, ""Author"" = {{2}}, ""DocumentDate"" = {{3}},
                ""DocumentType"" = {{4}}, ""Status"" = {{5}}, ""Classification"" = {{6}}, ""Keywords"" = {{7}},
                ""Memo"" = {{8}}, ""DocumentIndex"" = {{9}}, ""Issue"" = {{10}}, ""Version"" = {{11}}, ""FilePath"" = {{12}},
                ""Text01"" = {{13}}, ""Text02"" = {{14}}, ""Text03"" = {{15}}, ""Text04"" = {{16}},
                ""Text05"" = {{17}}, ""Text06"" = {{18}}, ""Text07"" = {{19}}, ""Text08"" = {{20}},
                ""Text09"" = {{21}}, ""Text10"" = {{22}}, ""Text11"" = {{23}}, ""Text12"" = {{24}},
                ""Date01"" = {{25}}, ""Date02"" = {{26}}, ""Date03"" = {{27}}, ""Date04"" = {{28}},
                ""Boolean01"" = {{29}}, ""Boolean02"" = {{30}}, ""Boolean03"" = {{31}},
                ""Number01"" = {{32}}, ""Number02"" = {{33}}, ""Number03"" = {{34}},
                ""OldDM"" = {{35}}, ""CM"" = {{36}}, ""GM"" = {{37}}, ""EM"" = {{38}},
                ""ModifiedOn"" = {{39}}, ""ModifiedBy"" = {{40}}
            WHERE ""DocumentId"" = {{41}}";

        var now = DateTime.UtcNow;
        var parameters = new object?[]
        {
            document.DocumentNumber ?? (object)DBNull.Value,
            document.Title ?? (object)DBNull.Value,
            document.Author ?? (object)DBNull.Value,
            document.DocumentDate ?? (object)DBNull.Value,
            document.DocumentType ?? (object)DBNull.Value,
            document.Status ?? (object)DBNull.Value,
            document.Classification ?? (object)DBNull.Value,
            document.Keywords ?? (object)DBNull.Value,
            document.Memo ?? (object)DBNull.Value,
            document.DocumentIndex ?? (object)DBNull.Value,
            document.Issue ?? (object)DBNull.Value,
            document.Version ?? (object)DBNull.Value,
            document.FilePath ?? (object)DBNull.Value,
            document.Text01 ?? (object)DBNull.Value, document.Text02 ?? (object)DBNull.Value,
            document.Text03 ?? (object)DBNull.Value, document.Text04 ?? (object)DBNull.Value,
            document.Text05 ?? (object)DBNull.Value, document.Text06 ?? (object)DBNull.Value,
            document.Text07 ?? (object)DBNull.Value, document.Text08 ?? (object)DBNull.Value,
            document.Text09 ?? (object)DBNull.Value, document.Text10 ?? (object)DBNull.Value,
            document.Text11 ?? (object)DBNull.Value, document.Text12 ?? (object)DBNull.Value,
            document.Date01 ?? (object)DBNull.Value, document.Date02 ?? (object)DBNull.Value,
            document.Date03 ?? (object)DBNull.Value, document.Date04 ?? (object)DBNull.Value,
            document.Boolean01 ?? (object)DBNull.Value, document.Boolean02 ?? (object)DBNull.Value,
            document.Boolean03 ?? (object)DBNull.Value,
            document.Number01 ?? (object)DBNull.Value, document.Number02 ?? (object)DBNull.Value,
            document.Number03 ?? (object)DBNull.Value,
            document.OldDM ?? (object)DBNull.Value, document.CM ?? (object)DBNull.Value,
            document.GM ?? (object)DBNull.Value, document.EM ?? (object)DBNull.Value,
            now, userId,
            document.DocumentId
        };

        return await _context.Database.ExecuteSqlRawAsync(sql, parameters!);
    }

    // ========================================================================
    // Method: DeleteDocumentAsync
    // ========================================================================
    // Purpose: Deletes a document and all its pages from a schema.
    public async Task<int> DeleteDocumentAsync(string schemaName, int documentId)
    {
        // Delete pages first (cascade)
        await _context.Database.ExecuteSqlRawAsync(
            $"DELETE FROM \"{schemaName}\".\"DocumentPage\" WHERE \"DocumentId\" = {{0}}", documentId);

        // Delete the document
        return await _context.Database.ExecuteSqlRawAsync(
            $"DELETE FROM \"{schemaName}\".\"Document\" WHERE \"DocumentId\" = {{0}}", documentId);
    }

    // ========================================================================
    // Method: DeleteDocumentsBulkAsync
    // ========================================================================
    // Purpose: Deletes multiple documents and their pages from a schema.
    public async Task<int> DeleteDocumentsBulkAsync(string schemaName, List<int> documentIds)
    {
        if (documentIds == null || documentIds.Count == 0)
            return 0;

        var idList = string.Join(",", documentIds);

        // Delete pages first
        await _context.Database.ExecuteSqlRawAsync(
            $"DELETE FROM \"{schemaName}\".\"DocumentPage\" WHERE \"DocumentId\" IN ({idList})");

        // Delete documents
        return await _context.Database.ExecuteSqlRawAsync(
            $"DELETE FROM \"{schemaName}\".\"Document\" WHERE \"DocumentId\" IN ({idList})");
    }

    // ========================================================================
    // Method: UpdateDocumentStatusBulkAsync
    // ========================================================================
    // Purpose: Updates the status of multiple documents at once.
    public async Task<int> UpdateDocumentStatusBulkAsync(string schemaName, List<int> documentIds, string newStatus, int userId)
    {
        if (documentIds == null || documentIds.Count == 0)
            return 0;

        var idList = string.Join(",", documentIds);
        var now = DateTime.UtcNow;

        return await _context.Database.ExecuteSqlRawAsync(
            $"UPDATE \"{schemaName}\".\"Document\" SET \"Status\" = {{0}}, \"ModifiedOn\" = {{1}}, \"ModifiedBy\" = {{2}} WHERE \"DocumentId\" IN ({idList})",
            newStatus, now, userId);
    }

    // ========================================================================
    // Method: GetPagesByDocumentIdAsync
    // ========================================================================
    // Purpose: Gets all pages for a document in a specific schema.
    public async Task<List<DocumentPage>> GetPagesByDocumentIdAsync(string schemaName, int documentId)
    {
        var sql = $"SELECT \"PageId\", \"DocumentId\", \"DocumentIndex\", \"PageNumber\", \"PageReference\", \"FrameNumber\", \"Level1\", \"Level2\", \"Level3\", \"Level4\", \"DiskNumber\", \"FileName\", \"FilePath\", \"FileType\", \"FileSize\", \"FileFormat\", \"PageSize\", \"ContentType\", \"UploadedDate\", \"ChecksumMD5\", \"StorageType\", \"CreatedOn\", \"CreatedBy\", \"ModifiedOn\", \"ModifiedBy\" FROM \"{schemaName}\".\"DocumentPage\" WHERE \"DocumentId\" = {{0}} ORDER BY \"PageNumber\"";
        return await _context.Database.SqlQueryRaw<DocumentPage>(sql, documentId).ToListAsync();
    }

    // ========================================================================
    // Method: DeletePageAsync
    // ========================================================================
    // Purpose: Deletes a single page from a schema.
    public async Task<int> DeletePageAsync(string schemaName, int pageId)
    {
        return await _context.Database.ExecuteSqlRawAsync(
            $"DELETE FROM \"{schemaName}\".\"DocumentPage\" WHERE \"PageId\" = {{0}}", pageId);
    }

    // ========================================================================
    // Helper: AppendFilterClauses
    // ========================================================================
    private void AppendFilterClauses(ref string sql, List<object> parameters, string? searchTerm, string? status, string? documentType, string? classification, DateTime? dateFrom, DateTime? dateTo)
    {
        if (!string.IsNullOrWhiteSpace(searchTerm))
        {
            var idx = parameters.Count;
            sql += $" AND (\"Title\" LIKE {{{idx}}} OR \"Author\" LIKE {{{idx + 1}}} OR \"DocumentNumber\" LIKE {{{idx + 2}}} OR \"Keywords\" LIKE {{{idx + 3}}} OR \"Memo\" LIKE {{{idx + 4}}} OR \"DocumentIndex\" LIKE {{{idx + 5}}})";
            var pattern = $"%{searchTerm}%";
            for (int i = 0; i < 6; i++) parameters.Add(pattern);
        }

        if (!string.IsNullOrWhiteSpace(status))
        {
            sql += $" AND \"Status\" = {{{parameters.Count}}}";
            parameters.Add(status);
        }

        if (!string.IsNullOrWhiteSpace(documentType))
        {
            sql += $" AND \"DocumentType\" = {{{parameters.Count}}}";
            parameters.Add(documentType);
        }

        if (!string.IsNullOrWhiteSpace(classification))
        {
            sql += $" AND \"Classification\" = {{{parameters.Count}}}";
            parameters.Add(classification);
        }

        if (dateFrom.HasValue)
        {
            sql += $" AND \"CreatedOn\" >= {{{parameters.Count}}}";
            parameters.Add(dateFrom.Value);
        }

        if (dateTo.HasValue)
        {
            sql += $" AND \"CreatedOn\" <= {{{parameters.Count}}}";
            parameters.Add(dateTo.Value);
        }
    }
}

// ========================================================================
// Supporting DTOs for DocumentRepository
// ========================================================================
public class DocumentSchemaStats
{
    public int TotalDocuments { get; set; }
    public int TotalPages { get; set; }
    public int BlobPages { get; set; }
    public long TotalStorageBytes { get; set; }
    public Dictionary<string, int> StatusBreakdown { get; set; } = new();

    public string FormattedStorage
    {
        get
        {
            if (TotalStorageBytes < 1024) return $"{TotalStorageBytes} B";
            if (TotalStorageBytes < 1024 * 1024) return $"{TotalStorageBytes / 1024.0:F1} KB";
            if (TotalStorageBytes < 1024 * 1024 * 1024) return $"{TotalStorageBytes / (1024.0 * 1024):F1} MB";
            return $"{TotalStorageBytes / (1024.0 * 1024 * 1024):F2} GB";
        }
    }
}

public class StatusCount
{
    public string Name { get; set; } = string.Empty;
    public int Count { get; set; }
}
