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
using Microsoft.Data.SqlClient;
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
        var query = "SELECT COUNT(*) AS TableCount FROM sys.tables WHERE name = 'Project' AND schema_id = SCHEMA_ID('dbo')";
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
            SELECT COUNT(*) AS ColumnCount
            FROM sys.columns 
            WHERE object_id = OBJECT_ID('dbo.Project') 
            AND name IN ('SchemaName', 'Description', 'CreatedDate', 'IsActive')";
        
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
            IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'Project' AND schema_id = SCHEMA_ID('dbo'))
            BEGIN
                CREATE TABLE [dbo].[Project] (
                    [ProjectId] int IDENTITY(1,1) NOT NULL,
                    [ProjectCode] nvarchar(50) NOT NULL,
                    [ProjectName] nvarchar(255) NOT NULL,
                    [FolderPath] nvarchar(500) NULL,
                    [Principal] nvarchar(255) NULL,
                    [SchemaName] nvarchar(128) NOT NULL,
                    [Description] nvarchar(1000) NULL,
                    [CreatedDate] datetime2 NOT NULL DEFAULT GETUTCDATE(),
                    [IsActive] bit NOT NULL DEFAULT 1,
                    CONSTRAINT [PK_Project] PRIMARY KEY CLUSTERED ([ProjectId] ASC)
                );

                -- Create unique indexes
                CREATE UNIQUE INDEX [IX_Project_ProjectCode] ON [dbo].[Project] ([ProjectCode]);
                CREATE UNIQUE INDEX [IX_Project_SchemaName] ON [dbo].[Project] ([SchemaName]);
            END";

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
                IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('dbo.Project') AND name = 'SchemaName')
                BEGIN
                    ALTER TABLE dbo.Project ADD SchemaName nvarchar(128) NULL;
                END

                IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('dbo.Project') AND name = 'Description')
                BEGIN
                    ALTER TABLE dbo.Project ADD Description nvarchar(1000) NULL;
                END

                IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('dbo.Project') AND name = 'CreatedDate')
                BEGIN
                    ALTER TABLE dbo.Project ADD CreatedDate datetime2 NOT NULL DEFAULT GETUTCDATE();
                END

                IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('dbo.Project') AND name = 'IsActive')
                BEGIN
                    ALTER TABLE dbo.Project ADD IsActive bit NOT NULL DEFAULT 1;
                END

                -- Update existing records to have schema names based on project codes
                UPDATE dbo.Project 
                SET SchemaName = LOWER(REPLACE(REPLACE(REPLACE(ProjectCode, ' ', '_'), '-', '_'), '.', '_'))
                WHERE SchemaName IS NULL;

                -- Make SchemaName column NOT NULL after setting values
                IF EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('dbo.Project') AND name = 'SchemaName' AND is_nullable = 1)
                BEGIN
                    ALTER TABLE dbo.Project ALTER COLUMN SchemaName nvarchar(128) NOT NULL;
                END

                -- Add unique constraints if they don't exist
                IF NOT EXISTS (SELECT * FROM sys.indexes WHERE object_id = OBJECT_ID('dbo.Project') AND name = 'IX_Project_ProjectCode')
                BEGIN
                    CREATE UNIQUE INDEX IX_Project_ProjectCode ON dbo.Project (ProjectCode);
                END

                IF NOT EXISTS (SELECT * FROM sys.indexes WHERE object_id = OBJECT_ID('dbo.Project') AND name = 'IX_Project_SchemaName')
                BEGIN
                    CREATE UNIQUE INDEX IX_Project_SchemaName ON dbo.Project (SchemaName);
                END";

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
        
        var query = "SELECT COUNT(*) AS ProjectCount FROM dbo.Project";
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
            IF NOT EXISTS (SELECT * FROM dbo.Project)
            BEGIN
                INSERT INTO dbo.Project (ProjectCode, ProjectName, FolderPath, Principal, SchemaName, Description, CreatedDate, IsActive)
                VALUES 
                    ('INV001', 'Invoices', '/documents/invoices', 'Finance Department', 'invoices', 'Invoice documents and related correspondence', GETUTCDATE(), 1),
                    ('CORR001', 'Correspondence', '/documents/correspondence', 'Admin Department', 'correspondence', 'General correspondence and letters', GETUTCDATE(), 1),
                    ('BILL001', 'Bills and Receipts', '/documents/bills', 'Accounting Department', 'bills', 'Bills, receipts and payment documentation', GETUTCDATE(), 1);
            END";

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
                    SELECT name AS Value
                    FROM sys.columns 
                    WHERE object_id = OBJECT_ID('dbo.Project')";
                
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
