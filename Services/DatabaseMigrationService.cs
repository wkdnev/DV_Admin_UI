using DV.Admin.UI.Data;
// ============================================================================
// DatabaseMigrationService.cs - Simple Database Migration Service
// ============================================================================
//
// Purpose: Provides a simple service for performing database migrations
// without dependencies on other services that might reference new columns.
//
// Created: [Date]
// Last Updated: [Date]
//
// Dependencies:
// - Microsoft.EntityFrameworkCore: For Entity Framework Core operations.
// - DocViewer_Proto.Models: Contains the AppDbContext.
// - Microsoft.Data.SqlClient: For SqlParameter objects.
//
// Notes:
// - This service is independent of ProjectService and other services.
// - Used specifically for database schema migrations.
// - Uses Entity Framework Core instead of Dapper for database operations.
// ============================================================================

using Microsoft.EntityFrameworkCore;
using Npgsql;
using DV.Shared.Models;

namespace DV.Admin.UI.Services;

// ============================================================================
// DatabaseMigrationService Class
// ============================================================================
// Purpose: Handles database schema migrations independently using EF Core.
// ============================================================================
public class DatabaseMigrationService
{
    private readonly AppDbContext _context;

    public DatabaseMigrationService(AppDbContext context)
    {
        _context = context;
    }

    // ========================================================================
    // Method: CheckIfProjectTableExistsAsync
    // ========================================================================
    // Purpose: Checks if the Project table exists at all.
    public async Task<bool> CheckIfProjectTableExistsAsync()
    {
        var query = "SELECT COUNT(*) AS \"Value\" FROM information_schema.tables WHERE table_name = 'Project' AND table_schema = 'dbo'";
        var count = await _context.Database.SqlQueryRaw<int>(query).FirstOrDefaultAsync();
        return count > 0;
    }

    // ========================================================================
    // Method: CheckIfMigrationNeededAsync
    // ========================================================================
    // Purpose: Checks if the project schema migration is needed.
    public async Task<bool> CheckIfMigrationNeededAsync()
    {
        // First check if table exists at all
        var tableExists = await CheckIfProjectTableExistsAsync();
        if (!tableExists)
        {
            return true; // Need to create the entire table
        }

        // If table exists, check if it has the new schema columns
        var query = @"
            SELECT COUNT(*) AS ""Value""
            FROM information_schema.columns 
            WHERE table_schema = 'dbo' AND table_name = 'Project'
            AND column_name IN ('SchemaName', 'Description', 'CreatedDate', 'IsActive')";
        
        var count = await _context.Database.SqlQueryRaw<int>(query).FirstOrDefaultAsync();
        return count < 4; // Migration needed if not all 4 columns exist
    }

    // ========================================================================
    // Method: CreateProjectTableAsync
    // ========================================================================
    // Purpose: Creates the Project table with the new schema architecture.
    public async Task CreateProjectTableAsync()
    {
        var createTableScript = @"
            -- Create the Project table with all required columns
            CREATE TABLE IF NOT EXISTS ""dbo"".""Project"" (
                ""ProjectId"" SERIAL NOT NULL,
                ""ProjectCode"" varchar(50) NOT NULL,
                ""ProjectName"" varchar(255) NOT NULL,
                ""FolderPath"" varchar(500) NULL,
                ""Principal"" varchar(255) NULL,
                ""SchemaName"" varchar(128) NOT NULL,
                ""Description"" varchar(1000) NULL,
                ""CreatedDate"" timestamp NOT NULL DEFAULT (NOW() AT TIME ZONE 'UTC'),
                ""IsActive"" boolean NOT NULL DEFAULT true,
                CONSTRAINT ""PK_Project"" PRIMARY KEY (""ProjectId"")
            );

            -- Create unique indexes
            CREATE UNIQUE INDEX IF NOT EXISTS ""IX_Project_ProjectCode"" ON ""dbo"".""Project"" (""ProjectCode"");
            CREATE UNIQUE INDEX IF NOT EXISTS ""IX_Project_SchemaName"" ON ""dbo"".""Project"" (""SchemaName"");
        ";

        await _context.Database.ExecuteSqlRawAsync(createTableScript);
    }

    // ========================================================================
    // Method: ExecuteProjectSchemaMigrationAsync
    // ========================================================================
    // Purpose: Executes the project schema migration.
    public async Task ExecuteProjectSchemaMigrationAsync()
    {
        var tableExists = await CheckIfProjectTableExistsAsync();
        
        if (!tableExists)
        {
            // Create the entire table from scratch
            await CreateProjectTableAsync();
        }
        else
        {
            // Add missing columns to existing table
            var migrationScript = @"
                -- Add new columns to the existing Project table
                ALTER TABLE ""dbo"".""Project"" ADD COLUMN IF NOT EXISTS ""SchemaName"" varchar(128) NULL;
                ALTER TABLE ""dbo"".""Project"" ADD COLUMN IF NOT EXISTS ""Description"" varchar(1000) NULL;
                ALTER TABLE ""dbo"".""Project"" ADD COLUMN IF NOT EXISTS ""CreatedDate"" timestamp NOT NULL DEFAULT (NOW() AT TIME ZONE 'UTC');
                ALTER TABLE ""dbo"".""Project"" ADD COLUMN IF NOT EXISTS ""IsActive"" boolean NOT NULL DEFAULT true;

                -- Update existing records to have schema names based on project codes
                UPDATE ""dbo"".""Project"" 
                SET ""SchemaName"" = LOWER(REPLACE(REPLACE(REPLACE(""ProjectCode"", ' ', '_'), '-', '_'), '.', '_'))
                WHERE ""SchemaName"" IS NULL;

                -- Make SchemaName column NOT NULL after setting values
                DO $$ BEGIN
                    IF EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema = 'dbo' AND table_name = 'Project' AND column_name = 'SchemaName' AND is_nullable = 'YES') THEN
                        ALTER TABLE ""dbo"".""Project"" ALTER COLUMN ""SchemaName"" SET NOT NULL;
                    END IF;
                END $$;

                -- Add unique constraints if they don't exist
                CREATE UNIQUE INDEX IF NOT EXISTS ""IX_Project_ProjectCode"" ON ""dbo"".""Project"" (""ProjectCode"");
                CREATE UNIQUE INDEX IF NOT EXISTS ""IX_Project_SchemaName"" ON ""dbo"".""Project"" (""SchemaName"");
            ";

            await _context.Database.ExecuteSqlRawAsync(migrationScript);
        }
    }

    // ========================================================================
    // Method: GetProjectCountAsync
    // ========================================================================
    // Purpose: Gets the count of projects (for verification).
    public async Task<int> GetProjectCountAsync()
    {
        var tableExists = await CheckIfProjectTableExistsAsync();
        if (!tableExists)
        {
            return 0;
        }
        
        var query = "SELECT COUNT(*) AS \"Value\" FROM \"dbo\".\"Project\"";
        return await _context.Database.SqlQueryRaw<int>(query).FirstOrDefaultAsync();
    }

    // ========================================================================
    // Method: CreateSampleProjectsAsync
    // ========================================================================
    // Purpose: Creates sample projects for testing the new schema architecture.
    public async Task CreateSampleProjectsAsync()
    {
        var sampleProjects = @"
            -- Insert sample projects if none exist
            INSERT INTO ""dbo"".""Project"" (""ProjectCode"", ""ProjectName"", ""FolderPath"", ""Principal"", ""SchemaName"", ""Description"", ""CreatedDate"", ""IsActive"")
            SELECT val.* FROM (VALUES 
                ('INV001', 'Invoices', '/documents/invoices', 'Finance Department', 'invoices', 'Invoice documents and related correspondence', NOW() AT TIME ZONE 'UTC', true),
                ('CORR001', 'Correspondence', '/documents/correspondence', 'Admin Department', 'correspondence', 'General correspondence and letters', NOW() AT TIME ZONE 'UTC', true),
                ('BILL001', 'Bills and Receipts', '/documents/bills', 'Accounting Department', 'bills', 'Bills, receipts and payment documentation', NOW() AT TIME ZONE 'UTC', true)
            ) AS val(""ProjectCode"", ""ProjectName"", ""FolderPath"", ""Principal"", ""SchemaName"", ""Description"", ""CreatedDate"", ""IsActive"")
            WHERE NOT EXISTS (SELECT 1 FROM ""dbo"".""Project"")";

        await _context.Database.ExecuteSqlRawAsync(sampleProjects);
    }

    // ========================================================================
    // Method: GetDatabaseStatusAsync
    // ========================================================================
    // Purpose: Gets a comprehensive status of the database for troubleshooting.
    public async Task<DatabaseStatus> GetDatabaseStatusAsync()
    {
        var status = new DatabaseStatus();
        
        try
        {
            // Check if table exists
            status.TableExists = await CheckIfProjectTableExistsAsync();
            
            if (status.TableExists)
            {
                // Check individual columns using a proper DTO
                var columnQuery = @"
                    SELECT column_name AS ""Value""
                    FROM information_schema.columns 
                    WHERE table_schema = 'dbo' AND table_name = 'Project'";
                
                var columnResults = await _context.Database.SqlQueryRaw<ColumnNameResult>(columnQuery).ToListAsync();
                status.ExistingColumns = columnResults.Select(c => c.Value).ToList();
                
                // Check project count
                status.ProjectCount = await GetProjectCountAsync();
            }
            
            status.MigrationNeeded = await CheckIfMigrationNeededAsync();
        }
        catch (Exception ex)
        {
            status.ErrorMessage = ex.Message;
        }
        
        return status;
    }
}

// ============================================================================
// ColumnNameResult Class
// ============================================================================
// Purpose: DTO for column name queries to work with SqlQueryRaw.
// ============================================================================
public class ColumnNameResult
{
    public string Value { get; set; } = string.Empty;
}

// ============================================================================
// DatabaseStatus Class
// ============================================================================
// Purpose: Represents the status of the database for debugging.
// ============================================================================
public class DatabaseStatus
{
    public bool TableExists { get; set; }
    public List<string> ExistingColumns { get; set; } = new();
    public int ProjectCount { get; set; }
    public bool MigrationNeeded { get; set; }
    public string? ErrorMessage { get; set; }
}
