using DV.Admin.UI.Data;
// ============================================================================
// SchemaService.cs - Schema Management Service for Document Viewer Application
// ============================================================================
//
// Purpose: Provides methods for managing database schemas for projects.
// Each project has its own schema containing Document, DocumentPage, and 
// BadFileReport tables.
//
// Created: [Date]
// Last Updated: October 16, 2025
//
// Dependencies:
// - Microsoft.EntityFrameworkCore: For Entity Framework Core operations.
// - DocViewer_Proto.Models: Contains the AppDbContext.
// - Microsoft.Data.SqlClient: For SqlParameter objects.
//
// Notes:
// - This service handles dynamic schema creation and management.
// - Supports creating new project schemas with required tables.
// - Uses Entity Framework Core instead of Dapper for database operations.
// ============================================================================

using Microsoft.EntityFrameworkCore;
using Microsoft.Data.SqlClient;
using DV.Shared.Models;

namespace DV.Admin.UI.Services;

// ============================================================================
// SchemaService Class
// ============================================================================
// Purpose: Manages database schemas for project-based document storage using EF Core.
// ============================================================================
public class SchemaService
{
    private readonly AppDbContext _context;

    public SchemaService(AppDbContext context)
    {
        _context = context;
    }

    // ========================================================================
    // Method: CreateProjectSchemaAsync
    // ========================================================================
    // Purpose: Creates a new database schema for a project with required tables.
    public async Task CreateProjectSchemaAsync(string schemaName)
    {
        Console.WriteLine($"Starting schema creation for: {schemaName}");
        
        var strategy = _context.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                await CreateProjectSchemaInternalAsync(schemaName);
                await transaction.CommitAsync();
                Console.WriteLine($"Schema creation completed successfully for: {schemaName}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error creating schema {schemaName}: {ex.Message}");
                Console.WriteLine($"Stack trace: {ex.StackTrace}");
                await transaction.RollbackAsync();
                throw;
            }
        });
    }

    // ========================================================================
    // Method: CreateProjectSchemaAsync (Overload for existing transaction)
    // ========================================================================
    // Purpose: Creates a new database schema within an existing transaction.
    // When useExistingTransaction is true, uses the provided sharedContext
    // to ensure all operations run on the same connection/transaction.
    public async Task CreateProjectSchemaAsync(string schemaName, bool useExistingTransaction, AppDbContext? sharedContext = null)
    {
        if (!useExistingTransaction)
        {
            await CreateProjectSchemaAsync(schemaName);
            return;
        }

        Console.WriteLine($"Creating schema within existing transaction for: {schemaName}");
        await CreateProjectSchemaInternalAsync(schemaName, sharedContext ?? _context);
        Console.WriteLine($"Schema creation completed for: {schemaName}");
    }

    // ========================================================================
    // Method: CreateProjectSchemaInternalAsync
    // ========================================================================
    // Purpose: Internal method that creates schema without managing transactions.
    // Accepts an optional context parameter to allow sharing a connection/transaction
    // with the calling service (e.g. ProjectService).
    private async Task CreateProjectSchemaInternalAsync(string schemaName, AppDbContext? ctx = null)
    {
        var db = ctx ?? _context;
        Console.WriteLine($"Creating schema: {schemaName}");
        
        // Create the schema
        var createSchemaQuery = $"IF NOT EXISTS (SELECT * FROM sys.schemas WHERE name = '{schemaName}') EXEC('CREATE SCHEMA [{schemaName}]')";
        Console.WriteLine($"Creating schema: {schemaName}");
        await db.Database.ExecuteSqlRawAsync(createSchemaQuery);

        // Create Document table in the schema
        var createDocumentTableQuery = $@"
            IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[{schemaName}].[Document]') AND type in (N'U'))
            BEGIN
                CREATE TABLE [{schemaName}].[Document] (
                    [DocumentId] int IDENTITY(1,1) NOT NULL,
                    [ProjectId] int NOT NULL,
                    [DocumentIndex] nvarchar(12) NULL,
                    [DocumentNumber] nvarchar(50) NOT NULL,
                    [Version] nvarchar(20) NULL,
                    [Issue] nvarchar(10) NULL,
                    [Status] nvarchar(50) NULL,
                    [DocumentStatus] nvarchar(50) NULL,
                    [Title] nvarchar(255) NULL,
                    [Author] nvarchar(100) NULL,
                    [Keywords] nvarchar(500) NULL,
                    [Memo] nvarchar(max) NULL,
                    [DocumentType] nvarchar(20) NOT NULL,
                    [Classification] nvarchar(50) NULL,
                    [FilePath] nvarchar(500) NULL,
                    [DocumentDate] datetime2 NULL,
                    
                    -- Custom Text Fields
                    [Text01] nvarchar(255) NULL,
                    [Text02] nvarchar(255) NULL,
                    [Text03] nvarchar(255) NULL,
                    [Text04] nvarchar(255) NULL,
                    [Text05] nvarchar(255) NULL,
                    [Text06] nvarchar(255) NULL,
                    [Text07] nvarchar(255) NULL,
                    [Text08] nvarchar(255) NULL,
                    [Text09] nvarchar(255) NULL,
                    [Text10] nvarchar(255) NULL,
                    [Text11] nvarchar(255) NULL,
                    [Text12] nvarchar(255) NULL,
                    
                    -- Custom Date Fields
                    [Date01] datetime2 NULL,
                    [Date02] datetime2 NULL,
                    [Date03] datetime2 NULL,
                    [Date04] datetime2 NULL,
                    
                    -- Custom Boolean Fields
                    [Boolean01] bit NULL,
                    [Boolean02] bit NULL,
                    [Boolean03] bit NULL,
                    
                    -- Custom Number Fields
                    [Number01] float NULL,
                    [Number02] float NULL,
                    [Number03] float NULL,
                    
                    -- Reference Fields
                    [OldDM] nvarchar(50) NULL,
                    [CM] nvarchar(50) NULL,
                    [GM] nvarchar(50) NULL,
                    [EM] nvarchar(50) NULL,
                    
                    -- Audit Fields
                    [CreatedOn] datetime2(7) NOT NULL DEFAULT GETUTCDATE(),
                    [CreatedBy] int NOT NULL,
                    [ModifiedOn] datetime2 NULL,
                    [ModifiedBy] int NULL,
                    
                    CONSTRAINT [PK_{schemaName}_Document] PRIMARY KEY CLUSTERED ([DocumentId] ASC),
                    CONSTRAINT [FK_{schemaName}_Document_Project] FOREIGN KEY ([ProjectId]) REFERENCES [dbo].[Project] ([ProjectId]),
                    CONSTRAINT [FK_{schemaName}_Document_CreatedBy] FOREIGN KEY ([CreatedBy]) REFERENCES [dbo].[ApplicationUser] ([UserId]) ON DELETE NO ACTION,
                    CONSTRAINT [FK_{schemaName}_Document_ModifiedBy] FOREIGN KEY ([ModifiedBy]) REFERENCES [dbo].[ApplicationUser] ([UserId]) ON DELETE NO ACTION
                )
            END";
        Console.WriteLine($"Creating Document table for schema: {schemaName}");
        await db.Database.ExecuteSqlRawAsync(createDocumentTableQuery);

        // Create DocumentPage table in the schema with BLOB storage support
        var createDocumentPageTableQuery = $@"
            IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[{schemaName}].[DocumentPage]') AND type in (N'U'))
            BEGIN
                CREATE TABLE [{schemaName}].[DocumentPage] (
                    [PageId] int IDENTITY(1,1) NOT NULL,
                    [DocumentId] int NOT NULL,
                    [DocumentIndex] nvarchar(12) NOT NULL,
                    [PageNumber] int NOT NULL,
                    [PageReference] nvarchar(255) NULL,
                    [FrameNumber] int NULL,
                    
                    -- Hierarchy Levels
                    [Level1] nvarchar(50) NULL,
                    [Level2] nvarchar(50) NULL,
                    [Level3] nvarchar(50) NULL,
                    [Level4] nvarchar(50) NULL,
                    
                    -- Disk Information
                    [DiskNumber] nvarchar(10) NULL,
                    
                    -- File Information
                    [FileName] nvarchar(255) NOT NULL,
                    [FilePath] nvarchar(1000) NULL,
                    [FileType] nvarchar(20) NOT NULL,
                    
                    -- BLOB Storage Columns
                    [FileContent] varbinary(max) NULL,
                    [FileSize] bigint NULL,
                    [FileFormat] int NULL,
                    [PageSize] nvarchar(5) NULL,
                    [ContentType] nvarchar(100) NULL,
                    [UploadedDate] datetime2 NULL,
                    [ChecksumMD5] nvarchar(32) NULL,
                    [StorageType] int NOT NULL DEFAULT 0,
                    
                    -- Audit Fields
                    [CreatedOn] datetime2 NOT NULL DEFAULT GETUTCDATE(),
                    [CreatedBy] int NOT NULL,
                    [ModifiedOn] datetime2 NULL,
                    [ModifiedBy] int NULL,
                    
                    CONSTRAINT [PK_{schemaName}_DocumentPage] PRIMARY KEY CLUSTERED ([PageId] ASC),
                    CONSTRAINT [FK_{schemaName}_DocumentPage_Document] FOREIGN KEY ([DocumentId]) REFERENCES [{schemaName}].[Document] ([DocumentId]) ON DELETE CASCADE
                );
                
                -- Create indexes for performance
                CREATE INDEX [IX_{schemaName}_DocumentPage_DocumentId] ON [{schemaName}].[DocumentPage] ([DocumentId]);
                CREATE INDEX [IX_{schemaName}_DocumentPage_DocumentIndex] ON [{schemaName}].[DocumentPage] ([DocumentIndex]);
                CREATE INDEX [IX_{schemaName}_DocumentPage_StorageType] ON [{schemaName}].[DocumentPage] ([StorageType]);
                CREATE UNIQUE INDEX [IX_{schemaName}_DocumentPage_DocumentId_PageNumber] ON [{schemaName}].[DocumentPage] ([DocumentId], [PageNumber]);
            END";
        Console.WriteLine($"Creating DocumentPage table for schema: {schemaName}");
        await db.Database.ExecuteSqlRawAsync(createDocumentPageTableQuery);

        // Create BadFileReport table in the schema
        var createBadFileReportTableQuery = $@"
            IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[{schemaName}].[BadFileReport]') AND type in (N'U'))
            BEGIN
                CREATE TABLE [{schemaName}].[BadFileReport] (
                    [BadFileReportId] int IDENTITY(1,1) NOT NULL,
                    [ReportedBy] int NOT NULL,
                    [ReportedOn] datetime2(7) NOT NULL DEFAULT GETUTCDATE(),
                    [DocumentPageId] int NOT NULL,
                    [ReportType] nvarchar(50) NOT NULL,
                    [ImageStatus] bit NULL,
                    [ImageUrl] nvarchar(50) NULL,
                    [UpdatedBy] int NOT NULL,
                    [CorrectiveAction] nvarchar(50) NULL,
                    [CreatedOn] datetime2(7) NOT NULL DEFAULT GETUTCDATE(),
                    [CreatedBy] int NOT NULL,
                    [ModifiedOn] datetime2(7) NULL,
                    [ModifiedBy] int NULL,
                    
                    CONSTRAINT [PK_{schemaName}_BadFileReport] PRIMARY KEY CLUSTERED ([BadFileReportId] ASC),
                    CONSTRAINT [FK_{schemaName}_BadFileReport_DocumentPage] FOREIGN KEY ([DocumentPageId]) REFERENCES [{schemaName}].[DocumentPage] ([PageId]) ON DELETE CASCADE,
                    CONSTRAINT [FK_{schemaName}_BadFileReport_ReportedBy] FOREIGN KEY ([ReportedBy]) REFERENCES [dbo].[ApplicationUser] ([UserId]) ON DELETE NO ACTION,
                    CONSTRAINT [FK_{schemaName}_BadFileReport_UpdatedBy] FOREIGN KEY ([UpdatedBy]) REFERENCES [dbo].[ApplicationUser] ([UserId]) ON DELETE NO ACTION,
                    CONSTRAINT [FK_{schemaName}_BadFileReport_CreatedBy] FOREIGN KEY ([CreatedBy]) REFERENCES [dbo].[ApplicationUser] ([UserId]) ON DELETE NO ACTION,
                    CONSTRAINT [FK_{schemaName}_BadFileReport_ModifiedBy] FOREIGN KEY ([ModifiedBy]) REFERENCES [dbo].[ApplicationUser] ([UserId]) ON DELETE NO ACTION
                );
                
                -- Create indexes for performance
                CREATE INDEX [IX_{schemaName}_BadFileReport_DocumentPageId] ON [{schemaName}].[BadFileReport] ([DocumentPageId]);
                CREATE INDEX [IX_{schemaName}_BadFileReport_ReportedBy] ON [{schemaName}].[BadFileReport] ([ReportedBy]);
                CREATE INDEX [IX_{schemaName}_BadFileReport_ReportType] ON [{schemaName}].[BadFileReport] ([ReportType]);
                CREATE INDEX [IX_{schemaName}_BadFileReport_ImageStatus] ON [{schemaName}].[BadFileReport] ([ImageStatus]);
            END";
        Console.WriteLine($"Creating BadFileReport table for schema: {schemaName}");
        await db.Database.ExecuteSqlRawAsync(createBadFileReportTableQuery);
    }

    // ========================================================================
    // Method: DropProjectSchemaAsync
    // ========================================================================
    // Purpose: Drops a project schema and all its tables.
    public async Task DropProjectSchemaAsync(string schemaName)
    {
        var strategy = _context.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                await DropProjectSchemaInternalAsync(schemaName);
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
    // Method: DropProjectSchemaAsync (Overload for existing transaction)
    // ========================================================================
    // Purpose: Drops a project schema within an existing transaction.
    public async Task DropProjectSchemaAsync(string schemaName, bool useExistingTransaction, AppDbContext? sharedContext = null)
    {
        if (!useExistingTransaction)
        {
            await DropProjectSchemaAsync(schemaName);
            return;
        }

        await DropProjectSchemaInternalAsync(schemaName, sharedContext ?? _context);
    }

    // ========================================================================
    // Method: DropProjectSchemaInternalAsync
    // ========================================================================
    // Purpose: Internal method that drops schema without managing transactions.
    private async Task DropProjectSchemaInternalAsync(string schemaName, AppDbContext? ctx = null)
    {
        var db = ctx ?? _context;
        // Drop tables in reverse dependency order
        var dropTablesQuery = $@"
            IF EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[{schemaName}].[BadFileReport]') AND type in (N'U'))
                DROP TABLE [{schemaName}].[BadFileReport];
            
            IF EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[{schemaName}].[DocumentPage]') AND type in (N'U'))
                DROP TABLE [{schemaName}].[DocumentPage];
            
            IF EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[{schemaName}].[Document]') AND type in (N'U'))
                DROP TABLE [{schemaName}].[Document];
            
            IF EXISTS (SELECT * FROM sys.schemas WHERE name = '{schemaName}')
                DROP SCHEMA [{schemaName}];";

        await db.Database.ExecuteSqlRawAsync(dropTablesQuery);
    }

    // ========================================================================
    // Method: SchemaExistsAsync
    // ========================================================================
    // Purpose: Checks if a schema exists in the database.
    public async Task<bool> SchemaExistsAsync(string schemaName)
    {
        try
        {
            // Use ExecuteSqlRaw to run a simple test query that will succeed if the schema exists
            // and fail if it doesn't - this is more reliable than SqlQueryRaw for schema checks
            var testSql = $"SELECT 1 FROM sys.schemas WHERE name = '{schemaName}'";
            
            // Try to execute a simple query to check if schema exists
            // This approach works better with EF Core's connection state management
            try
            {
                await _context.Database.ExecuteSqlRawAsync(
                    "IF EXISTS(SELECT 1 FROM sys.schemas WHERE name = {0}) SELECT 1 ELSE THROW 50000, 'Schema not found', 1", 
                    schemaName);
                return true; // If we get here, schema exists
            }
            catch (Exception)
            {
                return false; // If query throws, schema doesn't exist
            }
        }
        catch (Exception ex)
        {
            // Log detailed error information
            Console.WriteLine($"Error checking schema {schemaName}: {ex.Message}");
            if (ex.InnerException != null)
            {
                Console.WriteLine($"Inner exception: {ex.InnerException.Message}");
            }
            
            // Return false for any error - assume schema doesn't exist
            return false;
        }
    }

    // ========================================================================
    // Method: MigrateExistingSchemaAsync
    // ========================================================================
    // Purpose: Migrates an existing schema to add new columns to Document and DocumentPage tables.
    public async Task MigrateExistingSchemaAsync(string schemaName)
    {
        Console.WriteLine($"Migrating schema: {schemaName}");

        // Migrate Document table
        var migrateDocumentQuery = $@"
            -- Add new columns to Document table if they don't exist
            IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'[{schemaName}].[Document]') AND name = 'DocumentIndex')
                ALTER TABLE [{schemaName}].[Document] ADD [DocumentIndex] nvarchar(12) NULL;

            IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'[{schemaName}].[Document]') AND name = 'Issue')
                ALTER TABLE [{schemaName}].[Document] ADD [Issue] nvarchar(10) NULL;

            IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'[{schemaName}].[Document]') AND name = 'DocumentStatus')
                ALTER TABLE [{schemaName}].[Document] ADD [DocumentStatus] nvarchar(50) NULL;

            IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'[{schemaName}].[Document]') AND name = 'DocumentDate')
                ALTER TABLE [{schemaName}].[Document] ADD [DocumentDate] datetime2 NULL;

            -- Custom Text Fields
            IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'[{schemaName}].[Document]') AND name = 'Text01')
                ALTER TABLE [{schemaName}].[Document] ADD [Text01] nvarchar(255) NULL;
            IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'[{schemaName}].[Document]') AND name = 'Text02')
                ALTER TABLE [{schemaName}].[Document] ADD [Text02] nvarchar(255) NULL;
            IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'[{schemaName}].[Document]') AND name = 'Text03')
                ALTER TABLE [{schemaName}].[Document] ADD [Text03] nvarchar(255) NULL;
            IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'[{schemaName}].[Document]') AND name = 'Text04')
                ALTER TABLE [{schemaName}].[Document] ADD [Text04] nvarchar(255) NULL;
            IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'[{schemaName}].[Document]') AND name = 'Text05')
                ALTER TABLE [{schemaName}].[Document] ADD [Text05] nvarchar(255) NULL;
            IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'[{schemaName}].[Document]') AND name = 'Text06')
                ALTER TABLE [{schemaName}].[Document] ADD [Text06] nvarchar(255) NULL;
            IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'[{schemaName}].[Document]') AND name = 'Text07')
                ALTER TABLE [{schemaName}].[Document] ADD [Text07] nvarchar(255) NULL;
            IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'[{schemaName}].[Document]') AND name = 'Text08')
                ALTER TABLE [{schemaName}].[Document] ADD [Text08] nvarchar(255) NULL;
            IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'[{schemaName}].[Document]') AND name = 'Text09')
                ALTER TABLE [{schemaName}].[Document] ADD [Text09] nvarchar(255) NULL;
            IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'[{schemaName}].[Document]') AND name = 'Text10')
                ALTER TABLE [{schemaName}].[Document] ADD [Text10] nvarchar(255) NULL;
            IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'[{schemaName}].[Document]') AND name = 'Text11')
                ALTER TABLE [{schemaName}].[Document] ADD [Text11] nvarchar(255) NULL;
            IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'[{schemaName}].[Document]') AND name = 'Text12')
                ALTER TABLE [{schemaName}].[Document] ADD [Text12] nvarchar(255) NULL;

            -- Custom Date Fields
            IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'[{schemaName}].[Document]') AND name = 'Date01')
                ALTER TABLE [{schemaName}].[Document] ADD [Date01] datetime2 NULL;
            IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'[{schemaName}].[Document]') AND name = 'Date02')
                ALTER TABLE [{schemaName}].[Document] ADD [Date02] datetime2 NULL;
            IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'[{schemaName}].[Document]') AND name = 'Date03')
                ALTER TABLE [{schemaName}].[Document] ADD [Date03] datetime2 NULL;
            IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'[{schemaName}].[Document]') AND name = 'Date04')
                ALTER TABLE [{schemaName}].[Document] ADD [Date04] datetime2 NULL;

            -- Custom Boolean Fields
            IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'[{schemaName}].[Document]') AND name = 'Boolean01')
                ALTER TABLE [{schemaName}].[Document] ADD [Boolean01] bit NULL;
            IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'[{schemaName}].[Document]') AND name = 'Boolean02')
                ALTER TABLE [{schemaName}].[Document] ADD [Boolean02] bit NULL;
            IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'[{schemaName}].[Document]') AND name = 'Boolean03')
                ALTER TABLE [{schemaName}].[Document] ADD [Boolean03] bit NULL;

            -- Custom Number Fields
            IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'[{schemaName}].[Document]') AND name = 'Number01')
                ALTER TABLE [{schemaName}].[Document] ADD [Number01] float NULL;
            IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'[{schemaName}].[Document]') AND name = 'Number02')
                ALTER TABLE [{schemaName}].[Document] ADD [Number02] float NULL;
            IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'[{schemaName}].[Document]') AND name = 'Number03')
                ALTER TABLE [{schemaName}].[Document] ADD [Number03] float NULL;

            -- Reference Fields
            IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'[{schemaName}].[Document]') AND name = 'OldDM')
                ALTER TABLE [{schemaName}].[Document] ADD [OldDM] nvarchar(50) NULL;
            IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'[{schemaName}].[Document]') AND name = 'CM')
                ALTER TABLE [{schemaName}].[Document] ADD [CM] nvarchar(50) NULL;
            IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'[{schemaName}].[Document]') AND name = 'GM')
                ALTER TABLE [{schemaName}].[Document] ADD [GM] nvarchar(50) NULL;
            IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'[{schemaName}].[Document]') AND name = 'EM')
                ALTER TABLE [{schemaName}].[Document] ADD [EM] nvarchar(50) NULL;

            -- Audit Fields (add if not exist, with special handling for NOT NULL fields)
            IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'[{schemaName}].[Document]') AND name = 'CreatedBy')
                ALTER TABLE [{schemaName}].[Document] ADD [CreatedBy] int NOT NULL DEFAULT 0;
            IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'[{schemaName}].[Document]') AND name = 'ModifiedOn')
                ALTER TABLE [{schemaName}].[Document] ADD [ModifiedOn] datetime2 NULL;
            IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'[{schemaName}].[Document]') AND name = 'ModifiedBy')
                ALTER TABLE [{schemaName}].[Document] ADD [ModifiedBy] int NULL;
            
            -- Drop old ModifiedDate column if it exists
            IF EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'[{schemaName}].[Document]') AND name = 'ModifiedDate')
                ALTER TABLE [{schemaName}].[Document] DROP COLUMN [ModifiedDate];
            
            -- Add foreign keys if they don't exist
            IF NOT EXISTS (SELECT * FROM sys.foreign_keys WHERE name = 'FK_{schemaName}_Document_CreatedBy' AND parent_object_id = OBJECT_ID(N'[{schemaName}].[Document]'))
                ALTER TABLE [{schemaName}].[Document] ADD CONSTRAINT [FK_{schemaName}_Document_CreatedBy] FOREIGN KEY ([CreatedBy]) REFERENCES [dbo].[ApplicationUser] ([UserId]) ON DELETE NO ACTION;
            IF NOT EXISTS (SELECT * FROM sys.foreign_keys WHERE name = 'FK_{schemaName}_Document_ModifiedBy' AND parent_object_id = OBJECT_ID(N'[{schemaName}].[Document]'))
                ALTER TABLE [{schemaName}].[Document] ADD CONSTRAINT [FK_{schemaName}_Document_ModifiedBy] FOREIGN KEY ([ModifiedBy]) REFERENCES [dbo].[ApplicationUser] ([UserId]) ON DELETE NO ACTION;";

        Console.WriteLine($"Migrating Document table for schema: {schemaName}");
        await _context.Database.ExecuteSqlRawAsync(migrateDocumentQuery);

        // Migrate DocumentPage table
        var migrateDocumentPageQuery = $@"
            -- Add new columns to DocumentPage table if they don't exist
            IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'[{schemaName}].[DocumentPage]') AND name = 'DocumentIndex')
                ALTER TABLE [{schemaName}].[DocumentPage] ADD [DocumentIndex] nvarchar(12) NOT NULL DEFAULT '';

            IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'[{schemaName}].[DocumentPage]') AND name = 'FrameNumber')
                ALTER TABLE [{schemaName}].[DocumentPage] ADD [FrameNumber] int NULL;

            -- Hierarchy Levels
            IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'[{schemaName}].[DocumentPage]') AND name = 'Level1')
                ALTER TABLE [{schemaName}].[DocumentPage] ADD [Level1] nvarchar(50) NULL;
            IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'[{schemaName}].[DocumentPage]') AND name = 'Level2')
                ALTER TABLE [{schemaName}].[DocumentPage] ADD [Level2] nvarchar(50) NULL;
            IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'[{schemaName}].[DocumentPage]') AND name = 'Level3')
                ALTER TABLE [{schemaName}].[DocumentPage] ADD [Level3] nvarchar(50) NULL;
            IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'[{schemaName}].[DocumentPage]') AND name = 'Level4')
                ALTER TABLE [{schemaName}].[DocumentPage] ADD [Level4] nvarchar(50) NULL;

            -- Disk Information
            IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'[{schemaName}].[DocumentPage]') AND name = 'DiskNumber')
                ALTER TABLE [{schemaName}].[DocumentPage] ADD [DiskNumber] nvarchar(10) NULL;

            -- File Format and Page Size
            IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'[{schemaName}].[DocumentPage]') AND name = 'FileFormat')
                ALTER TABLE [{schemaName}].[DocumentPage] ADD [FileFormat] int NULL;
            IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'[{schemaName}].[DocumentPage]') AND name = 'PageSize')
                ALTER TABLE [{schemaName}].[DocumentPage] ADD [PageSize] nvarchar(5) NULL;

            -- Audit Fields
            IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'[{schemaName}].[DocumentPage]') AND name = 'CreatedOn')
                ALTER TABLE [{schemaName}].[DocumentPage] ADD [CreatedOn] datetime2 NOT NULL DEFAULT GETUTCDATE();
            IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'[{schemaName}].[DocumentPage]') AND name = 'CreatedBy')
                ALTER TABLE [{schemaName}].[DocumentPage] ADD [CreatedBy] int NOT NULL DEFAULT 0;
            IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'[{schemaName}].[DocumentPage]') AND name = 'ModifiedOn')
                ALTER TABLE [{schemaName}].[DocumentPage] ADD [ModifiedOn] datetime2 NULL;
            IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'[{schemaName}].[DocumentPage]') AND name = 'ModifiedBy')
                ALTER TABLE [{schemaName}].[DocumentPage] ADD [ModifiedBy] int NULL;

            -- Add index on DocumentIndex if it doesn't exist
            IF NOT EXISTS (SELECT * FROM sys.indexes WHERE object_id = OBJECT_ID(N'[{schemaName}].[DocumentPage]') AND name = 'IX_{schemaName}_DocumentPage_DocumentIndex')
                CREATE INDEX [IX_{schemaName}_DocumentPage_DocumentIndex] ON [{schemaName}].[DocumentPage] ([DocumentIndex]);";

        Console.WriteLine($"Migrating DocumentPage table for schema: {schemaName}");
        await _context.Database.ExecuteSqlRawAsync(migrateDocumentPageQuery);
        
        Console.WriteLine($"Schema migration completed for: {schemaName}");
    }

    // ========================================================================
    // Method: MigrateAllExistingSchemasAsync
    // ========================================================================
    // Purpose: Migrates all existing project schemas to add new columns.
    public async Task MigrateAllExistingSchemasAsync()
    {
        Console.WriteLine("Starting migration of all existing schemas...");
        
        // Get all schemas that have Document tables
        var getSchemasQuery = @"
            SELECT DISTINCT s.name AS Value
            FROM sys.schemas s
            INNER JOIN sys.tables t ON t.schema_id = s.schema_id
            WHERE t.name = 'Document'
            AND s.name NOT IN ('dbo', 'sys', 'guest', 'INFORMATION_SCHEMA')
            ORDER BY s.name";
        
        var schemaResults = await _context.Database.SqlQueryRaw<ColumnNameResult>(getSchemasQuery).ToListAsync();
        var schemas = schemaResults.Select(s => s.Value).ToList();
        
        Console.WriteLine($"Found {schemas.Count} schemas to migrate: {string.Join(", ", schemas)}");
        
        foreach (var schema in schemas)
        {
            try
            {
                await MigrateExistingSchemaAsync(schema);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error migrating schema {schema}: {ex.Message}");
                throw;
            }
        }
        
        Console.WriteLine($"Successfully migrated {schemas.Count} schemas.");
    }
}
