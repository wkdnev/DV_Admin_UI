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
using Npgsql;
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
        var createSchemaQuery = $"CREATE SCHEMA IF NOT EXISTS \"{schemaName}\"";
        Console.WriteLine($"Creating schema: {schemaName}");
        await db.Database.ExecuteSqlRawAsync(createSchemaQuery);

        // Create Document table in the schema
        var createDocumentTableQuery = $@"
            CREATE TABLE IF NOT EXISTS ""{schemaName}"".""Document"" (
                ""DocumentId"" SERIAL NOT NULL,
                ""ProjectId"" integer NOT NULL,
                ""DocumentIndex"" varchar(12) NULL,
                ""DocumentNumber"" varchar(50) NOT NULL,
                ""Version"" varchar(20) NULL,
                ""Issue"" varchar(10) NULL,
                ""Status"" varchar(50) NULL,
                ""DocumentStatus"" varchar(50) NULL,
                ""Title"" varchar(255) NULL,
                ""Author"" varchar(100) NULL,
                ""Keywords"" varchar(500) NULL,
                ""Memo"" text NULL,
                ""DocumentType"" varchar(20) NOT NULL,
                ""Classification"" varchar(50) NULL,
                ""FilePath"" varchar(500) NULL,
                ""DocumentDate"" timestamp NULL,
                
                -- Custom Text Fields
                ""Text01"" varchar(255) NULL,
                ""Text02"" varchar(255) NULL,
                ""Text03"" varchar(255) NULL,
                ""Text04"" varchar(255) NULL,
                ""Text05"" varchar(255) NULL,
                ""Text06"" varchar(255) NULL,
                ""Text07"" varchar(255) NULL,
                ""Text08"" varchar(255) NULL,
                ""Text09"" varchar(255) NULL,
                ""Text10"" varchar(255) NULL,
                ""Text11"" varchar(255) NULL,
                ""Text12"" varchar(255) NULL,
                
                -- Custom Date Fields
                ""Date01"" timestamp NULL,
                ""Date02"" timestamp NULL,
                ""Date03"" timestamp NULL,
                ""Date04"" timestamp NULL,
                
                -- Custom Boolean Fields
                ""Boolean01"" boolean NULL,
                ""Boolean02"" boolean NULL,
                ""Boolean03"" boolean NULL,
                
                -- Custom Number Fields
                ""Number01"" double precision NULL,
                ""Number02"" double precision NULL,
                ""Number03"" double precision NULL,
                
                -- Reference Fields
                ""OldDM"" varchar(50) NULL,
                ""CM"" varchar(50) NULL,
                ""GM"" varchar(50) NULL,
                ""EM"" varchar(50) NULL,
                
                -- Opaque Token
                ""PublicToken"" varchar(44) NULL,

                -- Audit Fields
                ""CreatedOn"" timestamp NOT NULL DEFAULT (NOW() AT TIME ZONE 'UTC'),
                ""CreatedBy"" integer NOT NULL,
                ""ModifiedOn"" timestamp NULL,
                ""ModifiedBy"" integer NULL,
                
                CONSTRAINT ""PK_{schemaName}_Document"" PRIMARY KEY (""DocumentId"")
            );
            DO $$ BEGIN
                ALTER TABLE ""{schemaName}"".""Document"" ADD CONSTRAINT ""FK_{schemaName}_Document_Project"" FOREIGN KEY (""ProjectId"") REFERENCES ""dbo"".""Project"" (""ProjectId"");
            EXCEPTION WHEN duplicate_object THEN NULL; END $$;
            DO $$ BEGIN
                ALTER TABLE ""{schemaName}"".""Document"" ADD CONSTRAINT ""FK_{schemaName}_Document_CreatedBy"" FOREIGN KEY (""CreatedBy"") REFERENCES ""dbo"".""ApplicationUser"" (""UserId"") ON DELETE NO ACTION;
            EXCEPTION WHEN duplicate_object THEN NULL; END $$;
            DO $$ BEGIN
                ALTER TABLE ""{schemaName}"".""Document"" ADD CONSTRAINT ""FK_{schemaName}_Document_ModifiedBy"" FOREIGN KEY (""ModifiedBy"") REFERENCES ""dbo"".""ApplicationUser"" (""UserId"") ON DELETE NO ACTION;
            EXCEPTION WHEN duplicate_object THEN NULL; END $$";
        Console.WriteLine($"Creating Document table for schema: {schemaName}");
        await db.Database.ExecuteSqlRawAsync(createDocumentTableQuery);

        // Create DocumentPage table in the schema with BLOB storage support
        var createDocumentPageTableQuery = $@"
            CREATE TABLE IF NOT EXISTS ""{schemaName}"".""DocumentPage"" (
                ""PageId"" SERIAL NOT NULL,
                ""DocumentId"" integer NOT NULL,
                ""DocumentIndex"" varchar(12) NOT NULL,
                ""PageNumber"" integer NOT NULL,
                ""PageReference"" varchar(255) NULL,
                ""FrameNumber"" integer NULL,
                
                -- Hierarchy Levels
                ""Level1"" varchar(50) NULL,
                ""Level2"" varchar(50) NULL,
                ""Level3"" varchar(50) NULL,
                ""Level4"" varchar(50) NULL,
                
                -- Disk Information
                ""DiskNumber"" varchar(10) NULL,
                
                -- File Information
                ""FileName"" varchar(255) NOT NULL,
                ""FilePath"" varchar(1000) NULL,
                ""FileType"" varchar(20) NOT NULL,
                
                -- BLOB Storage Columns
                ""FileContent"" bytea NULL,
                ""FileSize"" bigint NULL,
                ""FileFormat"" integer NULL,
                ""PageSize"" varchar(5) NULL,
                ""ContentType"" varchar(100) NULL,
                ""UploadedDate"" timestamp NULL,
                ""ChecksumMD5"" varchar(32) NULL,
                ""StorageType"" integer NOT NULL DEFAULT 0,
                
                -- Audit Fields
                ""CreatedOn"" timestamp NOT NULL DEFAULT (NOW() AT TIME ZONE 'UTC'),
                ""CreatedBy"" integer NOT NULL,
                ""ModifiedOn"" timestamp NULL,
                ""ModifiedBy"" integer NULL,
                
                CONSTRAINT ""PK_{schemaName}_DocumentPage"" PRIMARY KEY (""PageId""),
                CONSTRAINT ""FK_{schemaName}_DocumentPage_Document"" FOREIGN KEY (""DocumentId"") REFERENCES ""{schemaName}"".""Document"" (""DocumentId"") ON DELETE CASCADE
            );
            
            -- Create indexes for performance
            CREATE INDEX IF NOT EXISTS ""IX_{schemaName}_DocumentPage_DocumentId"" ON ""{schemaName}"".""DocumentPage"" (""DocumentId"");
            CREATE INDEX IF NOT EXISTS ""IX_{schemaName}_DocumentPage_DocumentIndex"" ON ""{schemaName}"".""DocumentPage"" (""DocumentIndex"");
            CREATE INDEX IF NOT EXISTS ""IX_{schemaName}_DocumentPage_StorageType"" ON ""{schemaName}"".""DocumentPage"" (""StorageType"");
            CREATE UNIQUE INDEX IF NOT EXISTS ""IX_{schemaName}_DocumentPage_DocumentId_PageNumber"" ON ""{schemaName}"".""DocumentPage"" (""DocumentId"", ""PageNumber"")";
        Console.WriteLine($"Creating DocumentPage table for schema: {schemaName}");
        await db.Database.ExecuteSqlRawAsync(createDocumentPageTableQuery);

        // Create BadFileReport table in the schema (with upgraded columns)
        var createBadFileReportTableQuery = $@"
            CREATE TABLE IF NOT EXISTS ""{schemaName}"".""BadFileReport"" (
                ""BadFileReportId"" SERIAL NOT NULL,
                ""ReportedBy"" integer NOT NULL,
                ""ReportedOn"" timestamp NOT NULL DEFAULT (NOW() AT TIME ZONE 'UTC'),
                ""DocumentPageId"" integer NOT NULL,
                ""DocumentId"" integer NOT NULL DEFAULT 0,
                ""SchemaName"" varchar(128) NOT NULL DEFAULT '',
                ""FileName"" varchar(500) NULL,
                ""PageNumber"" integer NOT NULL DEFAULT 1,
                ""ReportType"" varchar(50) NOT NULL,
                ""Description"" text NULL,
                ""Priority"" varchar(20) NOT NULL DEFAULT 'Normal',
                ""Status"" varchar(30) NOT NULL DEFAULT 'Open',
                ""ImageStatus"" boolean NULL,
                ""ImageUrl"" varchar(500) NULL,
                ""UpdatedBy"" integer NOT NULL,
                ""CorrectiveAction"" text NULL,
                ""ResolutionNotes"" text NULL,
                ""ResolvedBy"" integer NULL,
                ""ResolvedOn"" timestamp NULL,
                ""CreatedOn"" timestamp NOT NULL DEFAULT (NOW() AT TIME ZONE 'UTC'),
                ""CreatedBy"" integer NOT NULL,
                ""ModifiedOn"" timestamp NULL,
                ""ModifiedBy"" integer NULL,
                
                CONSTRAINT ""PK_{schemaName}_BadFileReport"" PRIMARY KEY (""BadFileReportId""),
                CONSTRAINT ""FK_{schemaName}_BadFileReport_DocumentPage"" FOREIGN KEY (""DocumentPageId"") REFERENCES ""{schemaName}"".""DocumentPage"" (""PageId"") ON DELETE CASCADE
            );
            DO $$ BEGIN
                ALTER TABLE ""{schemaName}"".""BadFileReport"" ADD CONSTRAINT ""FK_{schemaName}_BadFileReport_ReportedBy"" FOREIGN KEY (""ReportedBy"") REFERENCES ""dbo"".""ApplicationUser"" (""UserId"") ON DELETE NO ACTION;
            EXCEPTION WHEN duplicate_object THEN NULL; END $$;
            DO $$ BEGIN
                ALTER TABLE ""{schemaName}"".""BadFileReport"" ADD CONSTRAINT ""FK_{schemaName}_BadFileReport_UpdatedBy"" FOREIGN KEY (""UpdatedBy"") REFERENCES ""dbo"".""ApplicationUser"" (""UserId"") ON DELETE NO ACTION;
            EXCEPTION WHEN duplicate_object THEN NULL; END $$;
            DO $$ BEGIN
                ALTER TABLE ""{schemaName}"".""BadFileReport"" ADD CONSTRAINT ""FK_{schemaName}_BadFileReport_CreatedBy"" FOREIGN KEY (""CreatedBy"") REFERENCES ""dbo"".""ApplicationUser"" (""UserId"") ON DELETE NO ACTION;
            EXCEPTION WHEN duplicate_object THEN NULL; END $$;
            DO $$ BEGIN
                ALTER TABLE ""{schemaName}"".""BadFileReport"" ADD CONSTRAINT ""FK_{schemaName}_BadFileReport_ModifiedBy"" FOREIGN KEY (""ModifiedBy"") REFERENCES ""dbo"".""ApplicationUser"" (""UserId"") ON DELETE NO ACTION;
            EXCEPTION WHEN duplicate_object THEN NULL; END $$;
            DO $$ BEGIN
                ALTER TABLE ""{schemaName}"".""BadFileReport"" ADD CONSTRAINT ""FK_{schemaName}_BadFileReport_ResolvedBy"" FOREIGN KEY (""ResolvedBy"") REFERENCES ""dbo"".""ApplicationUser"" (""UserId"") ON DELETE NO ACTION;
            EXCEPTION WHEN duplicate_object THEN NULL; END $$;
            
            -- Create indexes for performance
            CREATE INDEX IF NOT EXISTS ""IX_{schemaName}_BadFileReport_DocumentPageId"" ON ""{schemaName}"".""BadFileReport"" (""DocumentPageId"");
            CREATE INDEX IF NOT EXISTS ""IX_{schemaName}_BadFileReport_ReportedBy"" ON ""{schemaName}"".""BadFileReport"" (""ReportedBy"");
            CREATE INDEX IF NOT EXISTS ""IX_{schemaName}_BadFileReport_ReportType"" ON ""{schemaName}"".""BadFileReport"" (""ReportType"");
            CREATE INDEX IF NOT EXISTS ""IX_{schemaName}_BadFileReport_ImageStatus"" ON ""{schemaName}"".""BadFileReport"" (""ImageStatus"");
            CREATE INDEX IF NOT EXISTS ""IX_{schemaName}_BadFileReport_Status"" ON ""{schemaName}"".""BadFileReport"" (""Status"");
            CREATE INDEX IF NOT EXISTS ""IX_{schemaName}_BadFileReport_DocumentId"" ON ""{schemaName}"".""BadFileReport"" (""DocumentId"")";
        Console.WriteLine($"Creating BadFileReport table for schema: {schemaName}");
        await db.Database.ExecuteSqlRawAsync(createBadFileReportTableQuery);

        // Migrate existing BadFileReport tables to add new columns
        var migrateBadFileReportQuery = $@"
            ALTER TABLE ""{schemaName}"".""BadFileReport"" ADD COLUMN IF NOT EXISTS ""DocumentId"" integer NOT NULL DEFAULT 0;
            ALTER TABLE ""{schemaName}"".""BadFileReport"" ADD COLUMN IF NOT EXISTS ""SchemaName"" varchar(128) NOT NULL DEFAULT '';
            ALTER TABLE ""{schemaName}"".""BadFileReport"" ADD COLUMN IF NOT EXISTS ""FileName"" varchar(500) NULL;
            ALTER TABLE ""{schemaName}"".""BadFileReport"" ADD COLUMN IF NOT EXISTS ""PageNumber"" integer NOT NULL DEFAULT 1;
            ALTER TABLE ""{schemaName}"".""BadFileReport"" ADD COLUMN IF NOT EXISTS ""Description"" text NULL;
            ALTER TABLE ""{schemaName}"".""BadFileReport"" ADD COLUMN IF NOT EXISTS ""Priority"" varchar(20) NOT NULL DEFAULT 'Normal';
            ALTER TABLE ""{schemaName}"".""BadFileReport"" ADD COLUMN IF NOT EXISTS ""Status"" varchar(30) NOT NULL DEFAULT 'Open';
            ALTER TABLE ""{schemaName}"".""BadFileReport"" ADD COLUMN IF NOT EXISTS ""ResolutionNotes"" text NULL;
            ALTER TABLE ""{schemaName}"".""BadFileReport"" ADD COLUMN IF NOT EXISTS ""ResolvedBy"" integer NULL;
            ALTER TABLE ""{schemaName}"".""BadFileReport"" ADD COLUMN IF NOT EXISTS ""ResolvedOn"" timestamp NULL;

            -- Widen existing narrow columns
            ALTER TABLE ""{schemaName}"".""BadFileReport"" ALTER COLUMN ""ImageUrl"" TYPE varchar(500);
            ALTER TABLE ""{schemaName}"".""BadFileReport"" ALTER COLUMN ""CorrectiveAction"" TYPE text;

            -- Add new indexes if missing
            CREATE INDEX IF NOT EXISTS ""IX_{schemaName}_BadFileReport_Status"" ON ""{schemaName}"".""BadFileReport"" (""Status"");
            CREATE INDEX IF NOT EXISTS ""IX_{schemaName}_BadFileReport_DocumentId"" ON ""{schemaName}"".""BadFileReport"" (""DocumentId"");

            -- Add FK for ResolvedBy if missing
            DO $$ BEGIN
                ALTER TABLE ""{schemaName}"".""BadFileReport"" ADD CONSTRAINT ""FK_{schemaName}_BadFileReport_ResolvedBy"" FOREIGN KEY (""ResolvedBy"") REFERENCES ""dbo"".""ApplicationUser"" (""UserId"") ON DELETE NO ACTION;
            EXCEPTION WHEN duplicate_object THEN NULL; END $$";
        Console.WriteLine($"Migrating BadFileReport table for schema: {schemaName}");
        await db.Database.ExecuteSqlRawAsync(migrateBadFileReportQuery);
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
            DROP TABLE IF EXISTS ""{schemaName}"".""BadFileReport"";
            DROP TABLE IF EXISTS ""{schemaName}"".""DocumentPage"";
            DROP TABLE IF EXISTS ""{schemaName}"".""Document"";
            DROP SCHEMA IF EXISTS ""{schemaName}"";";

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
            var result = await _context.Database.SqlQueryRaw<int>(
                "SELECT COUNT(*)::int AS \"Value\" FROM information_schema.schemata WHERE schema_name = {0}",
                schemaName).FirstOrDefaultAsync();
            return result > 0;
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
            ALTER TABLE ""{schemaName}"".""Document"" ADD COLUMN IF NOT EXISTS ""DocumentIndex"" varchar(12) NULL;
            ALTER TABLE ""{schemaName}"".""Document"" ADD COLUMN IF NOT EXISTS ""Issue"" varchar(10) NULL;
            ALTER TABLE ""{schemaName}"".""Document"" ADD COLUMN IF NOT EXISTS ""DocumentStatus"" varchar(50) NULL;
            ALTER TABLE ""{schemaName}"".""Document"" ADD COLUMN IF NOT EXISTS ""DocumentDate"" timestamp NULL;

            -- Custom Text Fields
            ALTER TABLE ""{schemaName}"".""Document"" ADD COLUMN IF NOT EXISTS ""Text01"" varchar(255) NULL;
            ALTER TABLE ""{schemaName}"".""Document"" ADD COLUMN IF NOT EXISTS ""Text02"" varchar(255) NULL;
            ALTER TABLE ""{schemaName}"".""Document"" ADD COLUMN IF NOT EXISTS ""Text03"" varchar(255) NULL;
            ALTER TABLE ""{schemaName}"".""Document"" ADD COLUMN IF NOT EXISTS ""Text04"" varchar(255) NULL;
            ALTER TABLE ""{schemaName}"".""Document"" ADD COLUMN IF NOT EXISTS ""Text05"" varchar(255) NULL;
            ALTER TABLE ""{schemaName}"".""Document"" ADD COLUMN IF NOT EXISTS ""Text06"" varchar(255) NULL;
            ALTER TABLE ""{schemaName}"".""Document"" ADD COLUMN IF NOT EXISTS ""Text07"" varchar(255) NULL;
            ALTER TABLE ""{schemaName}"".""Document"" ADD COLUMN IF NOT EXISTS ""Text08"" varchar(255) NULL;
            ALTER TABLE ""{schemaName}"".""Document"" ADD COLUMN IF NOT EXISTS ""Text09"" varchar(255) NULL;
            ALTER TABLE ""{schemaName}"".""Document"" ADD COLUMN IF NOT EXISTS ""Text10"" varchar(255) NULL;
            ALTER TABLE ""{schemaName}"".""Document"" ADD COLUMN IF NOT EXISTS ""Text11"" varchar(255) NULL;
            ALTER TABLE ""{schemaName}"".""Document"" ADD COLUMN IF NOT EXISTS ""Text12"" varchar(255) NULL;

            -- Custom Date Fields
            ALTER TABLE ""{schemaName}"".""Document"" ADD COLUMN IF NOT EXISTS ""Date01"" timestamp NULL;
            ALTER TABLE ""{schemaName}"".""Document"" ADD COLUMN IF NOT EXISTS ""Date02"" timestamp NULL;
            ALTER TABLE ""{schemaName}"".""Document"" ADD COLUMN IF NOT EXISTS ""Date03"" timestamp NULL;
            ALTER TABLE ""{schemaName}"".""Document"" ADD COLUMN IF NOT EXISTS ""Date04"" timestamp NULL;

            -- Custom Boolean Fields
            ALTER TABLE ""{schemaName}"".""Document"" ADD COLUMN IF NOT EXISTS ""Boolean01"" boolean NULL;
            ALTER TABLE ""{schemaName}"".""Document"" ADD COLUMN IF NOT EXISTS ""Boolean02"" boolean NULL;
            ALTER TABLE ""{schemaName}"".""Document"" ADD COLUMN IF NOT EXISTS ""Boolean03"" boolean NULL;

            -- Custom Number Fields
            ALTER TABLE ""{schemaName}"".""Document"" ADD COLUMN IF NOT EXISTS ""Number01"" double precision NULL;
            ALTER TABLE ""{schemaName}"".""Document"" ADD COLUMN IF NOT EXISTS ""Number02"" double precision NULL;
            ALTER TABLE ""{schemaName}"".""Document"" ADD COLUMN IF NOT EXISTS ""Number03"" double precision NULL;

            -- Reference Fields
            ALTER TABLE ""{schemaName}"".""Document"" ADD COLUMN IF NOT EXISTS ""OldDM"" varchar(50) NULL;
            ALTER TABLE ""{schemaName}"".""Document"" ADD COLUMN IF NOT EXISTS ""CM"" varchar(50) NULL;
            ALTER TABLE ""{schemaName}"".""Document"" ADD COLUMN IF NOT EXISTS ""GM"" varchar(50) NULL;
            ALTER TABLE ""{schemaName}"".""Document"" ADD COLUMN IF NOT EXISTS ""EM"" varchar(50) NULL;

            -- Audit Fields
            ALTER TABLE ""{schemaName}"".""Document"" ADD COLUMN IF NOT EXISTS ""CreatedBy"" integer NOT NULL DEFAULT 0;
            ALTER TABLE ""{schemaName}"".""Document"" ADD COLUMN IF NOT EXISTS ""ModifiedOn"" timestamp NULL;
            ALTER TABLE ""{schemaName}"".""Document"" ADD COLUMN IF NOT EXISTS ""ModifiedBy"" integer NULL;
            
            -- Drop old ModifiedDate column if it exists
            ALTER TABLE ""{schemaName}"".""Document"" DROP COLUMN IF EXISTS ""ModifiedDate"";
            
            -- Add foreign keys if they don't exist
            DO $$ BEGIN
                ALTER TABLE ""{schemaName}"".""Document"" ADD CONSTRAINT ""FK_{schemaName}_Document_CreatedBy"" FOREIGN KEY (""CreatedBy"") REFERENCES ""dbo"".""ApplicationUser"" (""UserId"") ON DELETE NO ACTION;
            EXCEPTION WHEN duplicate_object THEN NULL; END $$;
            DO $$ BEGIN
                ALTER TABLE ""{schemaName}"".""Document"" ADD CONSTRAINT ""FK_{schemaName}_Document_ModifiedBy"" FOREIGN KEY (""ModifiedBy"") REFERENCES ""dbo"".""ApplicationUser"" (""UserId"") ON DELETE NO ACTION;
            EXCEPTION WHEN duplicate_object THEN NULL; END $$;

            -- Opaque Token
            ALTER TABLE ""{schemaName}"".""Document"" ADD COLUMN IF NOT EXISTS ""PublicToken"" varchar(44) NULL;

            CREATE UNIQUE INDEX IF NOT EXISTS ""UX_{schemaName}_Document_PublicToken"" ON ""{schemaName}"".""Document"" (""PublicToken"") WHERE ""PublicToken"" IS NOT NULL";

        Console.WriteLine($"Migrating Document table for schema: {schemaName}");
        await _context.Database.ExecuteSqlRawAsync(migrateDocumentQuery);

        // Migrate DocumentPage table
        var migrateDocumentPageQuery = $@"
            -- Add new columns to DocumentPage table if they don't exist
            ALTER TABLE ""{schemaName}"".""DocumentPage"" ADD COLUMN IF NOT EXISTS ""DocumentIndex"" varchar(12) NOT NULL DEFAULT '';
            ALTER TABLE ""{schemaName}"".""DocumentPage"" ADD COLUMN IF NOT EXISTS ""FrameNumber"" integer NULL;

            -- Hierarchy Levels
            ALTER TABLE ""{schemaName}"".""DocumentPage"" ADD COLUMN IF NOT EXISTS ""Level1"" varchar(50) NULL;
            ALTER TABLE ""{schemaName}"".""DocumentPage"" ADD COLUMN IF NOT EXISTS ""Level2"" varchar(50) NULL;
            ALTER TABLE ""{schemaName}"".""DocumentPage"" ADD COLUMN IF NOT EXISTS ""Level3"" varchar(50) NULL;
            ALTER TABLE ""{schemaName}"".""DocumentPage"" ADD COLUMN IF NOT EXISTS ""Level4"" varchar(50) NULL;

            -- Disk Information
            ALTER TABLE ""{schemaName}"".""DocumentPage"" ADD COLUMN IF NOT EXISTS ""DiskNumber"" varchar(10) NULL;

            -- File Format and Page Size
            ALTER TABLE ""{schemaName}"".""DocumentPage"" ADD COLUMN IF NOT EXISTS ""FileFormat"" integer NULL;
            ALTER TABLE ""{schemaName}"".""DocumentPage"" ADD COLUMN IF NOT EXISTS ""PageSize"" varchar(5) NULL;

            -- Audit Fields
            ALTER TABLE ""{schemaName}"".""DocumentPage"" ADD COLUMN IF NOT EXISTS ""CreatedOn"" timestamp NOT NULL DEFAULT (NOW() AT TIME ZONE 'UTC');
            ALTER TABLE ""{schemaName}"".""DocumentPage"" ADD COLUMN IF NOT EXISTS ""CreatedBy"" integer NOT NULL DEFAULT 0;
            ALTER TABLE ""{schemaName}"".""DocumentPage"" ADD COLUMN IF NOT EXISTS ""ModifiedOn"" timestamp NULL;
            ALTER TABLE ""{schemaName}"".""DocumentPage"" ADD COLUMN IF NOT EXISTS ""ModifiedBy"" integer NULL;

            -- Add index on DocumentIndex if it doesn't exist
            CREATE INDEX IF NOT EXISTS ""IX_{schemaName}_DocumentPage_DocumentIndex"" ON ""{schemaName}"".""DocumentPage"" (""DocumentIndex"")";

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
            SELECT DISTINCT t.table_schema AS ""Value""
            FROM information_schema.tables t
            WHERE t.table_name = 'Document'
            AND t.table_schema NOT IN ('dbo', 'pg_catalog', 'information_schema', 'public')
            ORDER BY t.table_schema";
        
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
