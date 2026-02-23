using DV.Shared.Models;
using DV.Shared.Models;
// ============================================================================
// AppDbContext.cs - Database Context for Document Viewer Application
// ============================================================================
//
// Purpose: Defines the Entity Framework Core database context for the application. 
// This context manages the interaction with the database and provides DbSet 
// properties for the application's entities. Updated to support schema-based
// project architecture where each project has its own schema.
//
// Created: [Date]
// Last Updated: [Date]
//
// Dependencies:
// - Microsoft.EntityFrameworkCore: Provides EF Core functionality.
// - DocViewer_Proto.Models: Contains the entity models (Document, Project, etc.).
// - Microsoft.Data.SqlClient: For SqlParameter objects.
//
// Notes:
// - Uses .NET 8 features, including simplified property patterns.
// - Configures entity relationships and constraints in the OnModelCreating method.
// - Projects are stored in dbo schema, documents in project-specific schemas.
// ============================================================================

using DV.Shared.Models; // Imports the entity models (Document, Project, etc.)
using Microsoft.EntityFrameworkCore; // Provides EF Core functionality
using Microsoft.Data.SqlClient; // Provides SqlParameter objects
using System.Collections.Generic; // Provides collection types
using System.Reflection.Emit; // Provides metadata emission functionality

namespace DV.Admin.UI.Data;

// ============================================================================
// AppDbContext Class
// ============================================================================
// Purpose: Represents the database context for the application. Manages the 
// interaction with the database and provides DbSet properties for entities.
// Updated to support schema-based project architecture.
// Inherits: DbContext (EF Core base class for database contexts).
// ============================================================================
public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    // ========================================================================
    // DbSet Properties
    // ========================================================================
    // Purpose: Represents the tables in the database. Each DbSet corresponds to 
    // a table and provides CRUD operations for the associated entity.

    public DbSet<Project> Projects => Set<Project>(); // Represents the "Projects" table in dbo schema
    public DbSet<Programme> Programmes => Set<Programme>(); // Represents the "Programme" table in dbo schema
    public DbSet<DatasetDefinition> DatasetDefinitions => Set<DatasetDefinition>(); // Represents the "DatasetDefinition" table in dbo schema
    public DbSet<DriveMapping> DriveMappings => Set<DriveMapping>(); // Represents the "DriveMapping" table in dbo schema
    public DbSet<SheetType> SheetTypes => Set<SheetType>(); // Represents the "SheetType" table in dbo schema
    public DbSet<OldDM> OldDMs => Set<OldDM>(); // Represents the "OldDM" table in dbo schema
    public DbSet<CM> CMs => Set<CM>(); // Represents the "CM" table in dbo schema
    public DbSet<EM> EMs => Set<EM>(); // Represents the "EM" table in dbo schema
    public DbSet<GM> GMs => Set<GM>(); // Represents the "GM" table in dbo schema
    
    // Note: DocumentPage tables exist in project-specific schemas with BLOB support
    // Schema-based tables are managed through SchemaService and accessed via raw SQL
    // Each schema contains: Document and DocumentPage (with BLOB columns)

    // ========================================================================
    // OnModelCreating Method
    // ========================================================================
    // Purpose: Configures the entity relationships, constraints, and table 
    // mappings using the ModelBuilder. This method is called when the model 
    // for the context is being created.
    //
    // Parameters:
    // - modelBuilder: An instance of ModelBuilder used to configure the model.
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Configure the "Project" entity in dbo schema
        modelBuilder.Entity<Project>(entity =>
        {
            entity.ToTable("Project", "dbo"); // Maps the entity to the "Project" table in dbo schema
            entity.HasKey(e => e.ProjectId); // Sets the primary key
            entity.Property(e => e.ProjectName).IsRequired().HasMaxLength(255); // Makes the "ProjectName" property required
            entity.Property(e => e.ProjectCode).IsRequired().HasMaxLength(50); // Makes the "ProjectCode" property required
            entity.Property(e => e.SchemaName).IsRequired().HasMaxLength(128); // Schema name is required
            entity.Property(e => e.FolderPath).HasMaxLength(500); // Optional folder path
            entity.Property(e => e.ReadPrincipal).HasMaxLength(255); // Read access principal
            entity.Property(e => e.EditPrincipal).HasMaxLength(255); // Edit access principal
            entity.Property(e => e.Description).HasMaxLength(1000); // Optional description
            entity.Property(e => e.CreatedDate).HasDefaultValueSql("GETUTCDATE()"); // Default creation date
            entity.Property(e => e.IsActive).HasDefaultValue(true); // Default to active

            // Add unique constraints
            entity.HasIndex(e => e.ProjectCode).IsUnique();
            entity.HasIndex(e => e.SchemaName).IsUnique();
        });

        // Configure the "Programme" entity in dbo schema
        modelBuilder.Entity<Programme>(entity =>
        {
            entity.ToTable("Programme", "dbo"); // Maps the entity to the "Programme" table in dbo schema
            entity.HasKey(e => e.ProgrammeId); // Sets the primary key
            entity.Property(e => e.ProgrammeName).IsRequired().HasMaxLength(255); // Programme name is required
            entity.Property(e => e.CreatedOn).HasDefaultValueSql("GETUTCDATE()"); // Default creation date
            entity.Property(e => e.CreatedBy).IsRequired(); // Created by user ID is required
            entity.Property(e => e.ModifiedOn); // Optional modification date
            entity.Property(e => e.ModifiedBy); // Optional modified by user ID
        });

        // Configure the "DatasetDefinition" entity in dbo schema
        modelBuilder.Entity<DatasetDefinition>(entity =>
        {
            entity.ToTable("DatasetDefinition", "dbo"); // Maps the entity to the "DatasetDefinition" table in dbo schema
            entity.HasKey(e => e.DatasetDefinitionId); // Sets the primary key
            entity.Property(e => e.DatasetName).IsRequired().HasMaxLength(255); // Dataset/schema name is required
            entity.Property(e => e.BriefDescription).IsRequired().HasMaxLength(50); // Brief description is required
            entity.Property(e => e.FullDescription).HasMaxLength(1000); // Optional full description
            entity.Property(e => e.Principal).IsRequired().HasMaxLength(50); // AD group name is required
            entity.Property(e => e.Available).HasDefaultValue(true); // Default to available
            entity.Property(e => e.DriveMappingID).IsRequired(); // Drive mapping ID is required
            entity.Property(e => e.ProjectID).IsRequired(); // Project ID is required (FK to Project)
            entity.Property(e => e.CreatedOn).HasDefaultValueSql("GETUTCDATE()"); // Default creation date
            entity.Property(e => e.CreatedBy).IsRequired(); // Created by user ID is required
            entity.Property(e => e.ModifiedOn); // Optional modification date
            entity.Property(e => e.ModifiedBy); // Optional modified by user ID

            // Add unique constraint on DatasetName (schema name must be unique)
            entity.HasIndex(e => e.DatasetName).IsUnique();
            
            // Add foreign key relationship to Project table
            entity.HasOne<Project>()
                .WithMany()
                .HasForeignKey(e => e.ProjectID)
                .OnDelete(DeleteBehavior.Restrict); // Prevent cascade delete
        });

        // Configure the "DriveMapping" entity in dbo schema
        modelBuilder.Entity<DriveMapping>(entity =>
        {
            entity.ToTable("DriveMapping", "dbo"); // Maps the entity to the "DriveMapping" table in dbo schema
            entity.HasKey(e => e.DriveMappingId); // Sets the primary key
            entity.Property(e => e.DiskId).HasMaxLength(8); // Optional disk identifier (8 chars)
            entity.Property(e => e.ShareName).IsRequired().HasMaxLength(50); // Share name is required
            entity.Property(e => e.DirectoryPath).IsRequired().HasMaxLength(255); // Directory path is required
            entity.Property(e => e.Description).IsRequired().HasMaxLength(255); // Description is required
            entity.Property(e => e.CreatedOn).HasDefaultValueSql("GETUTCDATE()"); // Default creation date
            entity.Property(e => e.CreatedBy).IsRequired(); // Created by user ID is required
            entity.Property(e => e.ModifiedOn); // Optional modification date
            entity.Property(e => e.ModifiedBy); // Optional modified by user ID
        });

        modelBuilder.Entity<SheetType>(entity =>
        {
            entity.ToTable("SheetType", "dbo"); // Maps the entity to the "SheetType" table in dbo schema
            entity.HasKey(e => e.SheetTypeId); // Sets the primary key
            entity.Property(e => e.SheetTypeCode).IsRequired().HasMaxLength(3); // 3-character code (e.g., "INV", "RCP")
            entity.Property(e => e.SheetTypeName).IsRequired().HasMaxLength(50); // Full name (e.g., "Invoice", "Receipt")
            entity.Property(e => e.CreatedOn).HasDefaultValueSql("GETUTCDATE()"); // Default creation date
            entity.Property(e => e.CreatedBy).IsRequired(); // Created by user ID is required
            entity.Property(e => e.ModifiedOn); // Optional modification date
            entity.Property(e => e.ModifiedBy); // Optional modified by user ID
            
            // Create unique index on SheetTypeCode to ensure no duplicate codes
            entity.HasIndex(e => e.SheetTypeCode).IsUnique();
        });

        modelBuilder.Entity<OldDM>(entity =>
        {
            entity.ToTable("OldDM", "dbo"); // Maps the entity to the "OldDM" table in dbo schema
            entity.HasKey(e => e.OldDMId); // Sets the primary key
            entity.Property(e => e.OldDMName).HasMaxLength(50); // Optional name (nullable, max 50 chars)
            entity.Property(e => e.CreatedOn).HasDefaultValueSql("GETUTCDATE()"); // Default creation date
            entity.Property(e => e.CreatedBy).IsRequired(); // Created by user ID is required
            entity.Property(e => e.ModifiedOn); // Optional modification date
            entity.Property(e => e.ModifiedBy); // Optional modified by user ID
        });

        modelBuilder.Entity<CM>(entity =>
        {
            entity.ToTable("CM", "dbo"); // Maps the entity to the "CM" table in dbo schema
            entity.HasKey(e => e.CMId); // Sets the primary key
            entity.Property(e => e.CMName).HasMaxLength(50); // Optional name (nullable, max 50 chars)
            entity.Property(e => e.CreatedOn).HasDefaultValueSql("GETUTCDATE()"); // Default creation date
            entity.Property(e => e.CreatedBy).IsRequired(); // Created by user ID is required
            entity.Property(e => e.ModifiedOn); // Optional modification date
            entity.Property(e => e.ModifiedBy); // Optional modified by user ID
        });

        modelBuilder.Entity<EM>(entity =>
        {
            entity.ToTable("EM", "dbo"); // Maps the entity to the "EM" table in dbo schema
            entity.HasKey(e => e.EMId); // Sets the primary key
            entity.Property(e => e.EMName).HasMaxLength(50); // Optional name (nullable, max 50 chars)
            entity.Property(e => e.CreatedOn).HasDefaultValueSql("GETUTCDATE()"); // Default creation date
            entity.Property(e => e.CreatedBy).IsRequired(); // Created by user ID is required
            entity.Property(e => e.ModifiedOn); // Optional modification date
            entity.Property(e => e.ModifiedBy); // Optional modified by user ID
        });

        modelBuilder.Entity<GM>(entity =>
        {
            entity.ToTable("GM", "dbo"); // Maps the entity to the "GM" table in dbo schema
            entity.HasKey(e => e.GMId); // Sets the primary key
            entity.Property(e => e.GMName).HasMaxLength(50); // Optional name (nullable, max 50 chars)
            entity.Property(e => e.CreatedOn).HasDefaultValueSql("GETUTCDATE()"); // Default creation date
            entity.Property(e => e.CreatedBy).IsRequired(); // Created by user ID is required
            entity.Property(e => e.ModifiedOn); // Optional modification date
            entity.Property(e => e.ModifiedBy); // Optional modified by user ID
        });

        // Note: Document entities are no longer configured here as they exist 
        // in project-specific schemas and are accessed through raw SQL queries 
        // in the DocumentRepository.
    }

    // ========================================================================
    // Dynamic Schema Methods
    // ========================================================================
    // Purpose: Provide methods to work with entities in dynamic schemas.

    /// <summary>
    /// Executes raw SQL query for documents in a specific schema
    /// </summary>
    public async Task<List<Document>> GetDocumentsFromSchemaAsync(string schemaName, string? whereClause = null, object? parameters = null)
    {
        var sql = $"SELECT * FROM [{schemaName}].[Document]";
        if (!string.IsNullOrEmpty(whereClause))
        {
            sql += $" WHERE {whereClause}";
        }

        // Use EF Core raw SQL instead of Dapper
        return await Database.SqlQueryRaw<Document>(sql).ToListAsync();
    }

    /// <summary>
    /// Checks if a schema exists and has the required tables
    /// </summary>
    public async Task<bool> SchemaExistsAsync(string schemaName)
    {
        var sql = @"
            SELECT COUNT(*) AS SchemaTableCount
            FROM sys.schemas s
            INNER JOIN sys.tables t1 ON s.schema_id = t1.schema_id AND t1.name = 'Document'
            INNER JOIN sys.tables t2 ON s.schema_id = t2.schema_id AND t2.name = 'DocumentPage'
            WHERE s.name = @schemaName";

        var parameters = new[] { new Microsoft.Data.SqlClient.SqlParameter("@schemaName", schemaName) };
        var count = await Database.SqlQueryRaw<int>(sql, parameters).FirstOrDefaultAsync();
        return count > 0;
    }
}
