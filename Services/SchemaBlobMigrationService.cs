using DV.Admin.UI.Data;
using Microsoft.EntityFrameworkCore;
using DV.Shared.Models;

namespace DV.Admin.UI.Services;

/// <summary>
/// Service for migrating existing project schemas to support BLOB storage
/// </summary>
public class SchemaBlobMigrationService
{
    private readonly AppDbContext _context;
    private readonly ILogger<SchemaBlobMigrationService> _logger;

    public SchemaBlobMigrationService(AppDbContext context, ILogger<SchemaBlobMigrationService> logger)
    {
        _context = context;
        _logger = logger;
    }

    /// <summary>
    /// Checks if a schema's DocumentPage table has BLOB storage columns
    /// </summary>
    public async Task<bool> SchemaHasBlobColumnsAsync(string schemaName)
    {
        try
        {
            // Use ExecuteScalarAsync for reliable column counting
            var sql = $@"
                SELECT COUNT(*) 
                FROM information_schema.columns
                WHERE table_schema = '{schemaName}' 
                  AND table_name = 'DocumentPage' 
                  AND column_name IN ('FileContent', 'FileSize', 'ContentType', 'UploadedDate', 'ChecksumMD5', 'StorageType')";
            
            using var command = _context.Database.GetDbConnection().CreateCommand();
            command.CommandText = sql;
            
            if (_context.Database.GetDbConnection().State != System.Data.ConnectionState.Open)
                await _context.Database.OpenConnectionAsync();
            
            var result = await command.ExecuteScalarAsync();
            var count = Convert.ToInt32(result);
            
            _logger.LogInformation("Schema {SchemaName} BLOB columns check: {Count}/6 columns found", schemaName, count);
            return count >= 6; // All 6 BLOB columns should exist
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking BLOB columns for schema {SchemaName}", schemaName);
            return false;
        }
    }

    /// <summary>
    /// Checks if a schema exists and has DocumentPage table
    /// </summary>
    public async Task<bool> SchemaHasDocumentPageTableAsync(string schemaName)
    {
        try
        {
            // Use ExecuteSqlRaw to execute a simple existence check
            var sql = $@"
                SELECT CASE WHEN COUNT(*) > 0 THEN 1 ELSE 0 END AS ""Result""
                FROM information_schema.tables
                WHERE table_schema = '{schemaName}' AND table_name = 'DocumentPage'";
            
            using var command = _context.Database.GetDbConnection().CreateCommand();
            command.CommandText = sql;
            
            if (_context.Database.GetDbConnection().State != System.Data.ConnectionState.Open)
                await _context.Database.OpenConnectionAsync();
            
            var result = await command.ExecuteScalarAsync();
            var hasTable = Convert.ToInt32(result) > 0;
            
            _logger.LogInformation("Schema {SchemaName} DocumentPage table check: {HasTable}", schemaName, hasTable);
            return hasTable;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking DocumentPage table for schema {SchemaName}", schemaName);
            return false;
        }
    }

    /// <summary>
    /// Adds BLOB storage columns to an existing schema's DocumentPage table
    /// </summary>
    public async Task AddBlobColumnsToSchemaAsync(string schemaName)
    {
        try
        {
            var sql = $@"
                -- Add BLOB storage columns if they don't exist
                ALTER TABLE ""{schemaName}"".""DocumentPage"" ADD COLUMN IF NOT EXISTS ""FileContent"" bytea NULL;
                ALTER TABLE ""{schemaName}"".""DocumentPage"" ADD COLUMN IF NOT EXISTS ""FileSize"" bigint NULL;
                ALTER TABLE ""{schemaName}"".""DocumentPage"" ADD COLUMN IF NOT EXISTS ""ContentType"" varchar(100) NULL;
                ALTER TABLE ""{schemaName}"".""DocumentPage"" ADD COLUMN IF NOT EXISTS ""UploadedDate"" timestamp NULL;
                ALTER TABLE ""{schemaName}"".""DocumentPage"" ADD COLUMN IF NOT EXISTS ""ChecksumMD5"" varchar(32) NULL;
                ALTER TABLE ""{schemaName}"".""DocumentPage"" ADD COLUMN IF NOT EXISTS ""StorageType"" integer NOT NULL DEFAULT 0;

                -- Update FilePath to allow NULL (for BLOB-only storage)
                ALTER TABLE ""{schemaName}"".""DocumentPage"" ALTER COLUMN ""FilePath"" DROP NOT NULL;
                ALTER TABLE ""{schemaName}"".""DocumentPage"" ALTER COLUMN ""FilePath"" TYPE varchar(1000);

                -- Add indexes if they don't exist
                CREATE INDEX IF NOT EXISTS ""IX_{schemaName}_DocumentPage_StorageType"" ON ""{schemaName}"".""DocumentPage"" (""StorageType"");
                CREATE UNIQUE INDEX IF NOT EXISTS ""IX_{schemaName}_DocumentPage_DocumentId_PageNumber"" ON ""{schemaName}"".""DocumentPage"" (""DocumentId"", ""PageNumber"");
            ";

            await _context.Database.ExecuteSqlRawAsync(sql);
            _logger.LogInformation("Successfully added BLOB columns to schema {SchemaName}", schemaName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error adding BLOB columns to schema {SchemaName}", schemaName);
            throw;
        }
    }

    /// <summary>
    /// Gets all active project schemas
    /// </summary>
    public async Task<List<string>> GetActiveProjectSchemasAsync()
    {
        try
        {
            var projects = await _context.Projects
                .Where(p => p.IsActive && !string.IsNullOrEmpty(p.SchemaName))
                .Select(p => p.SchemaName)
                .ToListAsync();

            return projects;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting active project schemas");
            return new List<string>();
        }
    }

    /// <summary>
    /// Migrates all existing schemas to support BLOB storage
    /// </summary>
    public async Task<SchemaMigrationResult> MigrateAllSchemasAsync()
    {
        var result = new SchemaMigrationResult();

        try
        {
            var schemas = await GetActiveProjectSchemasAsync();
            _logger.LogInformation("Found {SchemaCount} active project schemas to check", schemas.Count);

            foreach (var schema in schemas)
            {
                try
                {
                    // Check if schema has DocumentPage table
                    if (!await SchemaHasDocumentPageTableAsync(schema))
                    {
                        result.SkippedSchemas.Add(new SchemaSkipInfo
                        {
                            SchemaName = schema,
                            Reason = "DocumentPage table not found"
                        });
                        continue;
                    }

                    // Check if schema already has BLOB columns
                    if (await SchemaHasBlobColumnsAsync(schema))
                    {
                        result.AlreadyMigratedSchemas.Add(schema);
                        continue;
                    }

                    // Add BLOB columns
                    await AddBlobColumnsToSchemaAsync(schema);
                    result.MigratedSchemas.Add(schema);
                }
                catch (Exception ex)
                {
                    result.FailedSchemas.Add(new SchemaFailureInfo
                    {
                        SchemaName = schema,
                        ErrorMessage = ex.Message
                    });
                    _logger.LogError(ex, "Failed to migrate schema {SchemaName}", schema);
                }
            }

            result.Success = true;
            _logger.LogInformation("Schema migration completed. Migrated: {MigratedCount}, Already migrated: {AlreadyMigratedCount}, Failed: {FailedCount}, Skipped: {SkippedCount}",
                result.MigratedSchemas.Count, result.AlreadyMigratedSchemas.Count, result.FailedSchemas.Count, result.SkippedSchemas.Count);
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.ErrorMessage = ex.Message;
            _logger.LogError(ex, "Schema migration failed");
        }

        return result;
    }

    /// <summary>
    /// Gets the migration status for all schemas
    /// </summary>
    public async Task<List<SchemaStatus>> GetSchemaMigrationStatusAsync()
    {
        var statuses = new List<SchemaStatus>();

        try
        {
            var schemas = await GetActiveProjectSchemasAsync();

            foreach (var schema in schemas)
            {
                var status = new SchemaStatus
                {
                    SchemaName = schema,
                    HasDocumentPageTable = await SchemaHasDocumentPageTableAsync(schema),
                    HasBlobColumns = await SchemaHasBlobColumnsAsync(schema)
                };

                status.MigrationNeeded = status.HasDocumentPageTable && !status.HasBlobColumns;
                statuses.Add(status);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting schema migration status");
        }

        return statuses;
    }

    /// <summary>
    /// Gets detailed information about a schema for debugging
    /// </summary>
    public async Task<SchemaDebugInfo> GetSchemaDebugInfoAsync(string schemaName)
    {
        var debugInfo = new SchemaDebugInfo { SchemaName = schemaName };

        try
        {
            // Check if schema exists using ExecuteScalarAsync
            var schemaExistsSql = $"SELECT COUNT(*) FROM information_schema.schemata WHERE schema_name = '{schemaName}'";
            
            using var connection = _context.Database.GetDbConnection();
            if (connection.State != System.Data.ConnectionState.Open)
                await connection.OpenAsync();

            using var schemaCommand = connection.CreateCommand();
            schemaCommand.CommandText = schemaExistsSql;
            var schemaResult = await schemaCommand.ExecuteScalarAsync();
            debugInfo.SchemaExists = Convert.ToInt32(schemaResult) > 0;

            if (debugInfo.SchemaExists)
            {
                // Get all tables in the schema
                var tablesSql = $@"
                    SELECT table_name 
                    FROM information_schema.tables 
                    WHERE table_schema = '{schemaName}'";
                
                using var tablesCommand = connection.CreateCommand();
                tablesCommand.CommandText = tablesSql;
                using var reader = await tablesCommand.ExecuteReaderAsync();
                
                while (await reader.ReadAsync())
                {
                    debugInfo.TablesInSchema.Add(reader.GetString(0));
                }
                reader.Close();

                // Get DocumentPage columns if table exists
                if (debugInfo.TablesInSchema.Contains("DocumentPage"))
                {
                    var columnsSql = $@"
                        SELECT column_name 
                        FROM information_schema.columns
                        WHERE table_schema = '{schemaName}' AND table_name = 'DocumentPage'
                        ORDER BY ordinal_position";
                    
                    using var columnsCommand = connection.CreateCommand();
                    columnsCommand.CommandText = columnsSql;
                    using var columnsReader = await columnsCommand.ExecuteReaderAsync();
                    
                    while (await columnsReader.ReadAsync())
                    {
                        debugInfo.DocumentPageColumns.Add(columnsReader.GetString(0));
                    }
                    columnsReader.Close();
                }
            }
        }
        catch (Exception ex)
        {
            debugInfo.ErrorMessage = ex.Message;
            _logger.LogError(ex, "Error getting debug info for schema {SchemaName}", schemaName);
        }

        return debugInfo;
    }
}

#region Result Classes

/// <summary>
/// Result of schema migration operation
/// </summary>
public class SchemaMigrationResult
{
    public bool Success { get; set; } = false;
    public string? ErrorMessage { get; set; }
    public List<string> MigratedSchemas { get; set; } = new();
    public List<string> AlreadyMigratedSchemas { get; set; } = new();
    public List<SchemaFailureInfo> FailedSchemas { get; set; } = new();
    public List<SchemaSkipInfo> SkippedSchemas { get; set; } = new();
}

/// <summary>
/// Information about a failed schema migration
/// </summary>
public class SchemaFailureInfo
{
    public string SchemaName { get; set; } = string.Empty;
    public string ErrorMessage { get; set; } = string.Empty;
}

/// <summary>
/// Information about a skipped schema
/// </summary>
public class SchemaSkipInfo
{
    public string SchemaName { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
}

/// <summary>
/// Status of a schema's BLOB migration
/// </summary>
public class SchemaStatus
{
    public string SchemaName { get; set; } = string.Empty;
    public bool HasDocumentPageTable { get; set; }
    public bool HasBlobColumns { get; set; }
    public bool MigrationNeeded { get; set; }
}

/// <summary>
/// Debug information about a schema
/// </summary>
public class SchemaDebugInfo
{
    public string SchemaName { get; set; } = string.Empty;
    public bool SchemaExists { get; set; }
    public List<string> TablesInSchema { get; set; } = new();
    public List<string> DocumentPageColumns { get; set; } = new();
    public string? ErrorMessage { get; set; }
}

#endregion
