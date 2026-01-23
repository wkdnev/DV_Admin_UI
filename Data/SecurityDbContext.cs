using DV.Shared.Security;
using DV.Shared.Security;
using Microsoft.EntityFrameworkCore;
using System.Security;

namespace DV.Admin.UI.Data;

public class SecurityDbContext : DbContext
{
    public SecurityDbContext(DbContextOptions<SecurityDbContext> options) : base(options)
    {
    }

    public DbSet<ApplicationUser> Users => Set<ApplicationUser>();
    public DbSet<ApplicationRole> Roles => Set<ApplicationRole>();
    // REMOVED: public DbSet<UserRole> UserRoles => Set<UserRole>(); - Deprecated global roles
    // REMOVED: public DbSet<Permission> Permissions => Set<Permission>(); - Permissions system removed
    // REMOVED: public DbSet<RolePermission> RolePermissions => Set<RolePermission>(); - Deprecated global role permissions
    public DbSet<ProjectRole> ProjectRoles => Set<ProjectRole>();
    public DbSet<UserProjectRole> UserProjectRoles => Set<UserProjectRole>();
    // REMOVED: public DbSet<ProjectRolePermission> ProjectRolePermissions => Set<ProjectRolePermission>(); - Permissions system removed
    // REMOVED: public DbSet<UserProjectAccess> UserProjectAccess => Set<UserProjectAccess>(); - Explicit Access removed
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<UserSession> UserSessions => Set<UserSession>();
    public DbSet<SessionActivity> SessionActivities => Set<SessionActivity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Configure ApplicationUser entity
        modelBuilder.Entity<ApplicationUser>(entity =>
        {
            entity.ToTable("ApplicationUser", "dbo");
            entity.HasKey(e => e.UserId);
            entity.Property(e => e.Username).IsRequired().HasMaxLength(255);
            entity.Property(e => e.DisplayName).HasMaxLength(255);
            entity.Property(e => e.Email).HasMaxLength(255);
            entity.Property(e => e.IsActive).HasDefaultValue(true); // Soft delete support
            entity.Property(e => e.IsGlobalAdmin).HasDefaultValue(false);
            entity.HasIndex(e => e.Username).IsUnique();
        });

        // Configure ApplicationRole entity
        modelBuilder.Entity<ApplicationRole>(entity =>
        {
            entity.ToTable("ApplicationRole", "dbo");
            entity.HasKey(e => e.RoleId);
            entity.Property(e => e.Name).IsRequired().HasMaxLength(50);
            entity.Property(e => e.Description).HasMaxLength(255);
            entity.HasIndex(e => e.Name).IsUnique();
        });

        // REMOVED: UserRole entity configuration - deprecated global role system
        // Global roles have been replaced with project-scoped roles (UserProjectRole)

        // REMOVED: Permission entity configuration - permissions system has been removed
        // Authorization is now based on project roles and page-level authorization only

        // REMOVED: RolePermission entity configuration - deprecated global role permission system  
        // Global role permissions have been replaced with project-scoped authorization

        // Configure ProjectRole entity
        modelBuilder.Entity<ProjectRole>(entity =>
        {
            entity.ToTable("ProjectRole", "dbo");
            entity.HasKey(e => e.ProjectRoleId);
            entity.Property(e => e.DisplayName).IsRequired().HasMaxLength(100);
            entity.Property(e => e.Description).HasMaxLength(500);

            // Foreign key to Project (cross-database reference - do NOT include Project entity in this context)
            entity.Property(e => e.ProjectId).IsRequired();
            // Note: We do NOT configure a navigation property to Project since it's in a different DbContext
            
            // Foreign key to ApplicationRole (role template)
            entity.HasOne(e => e.ApplicationRole)
                .WithMany()
                .HasForeignKey(e => e.ApplicationRoleId)
                .OnDelete(DeleteBehavior.Restrict); // Prevent deletion of base roles if used in projects

            // Unique constraint: one role per project (keeping ApplicationRoleId for reference)
            entity.HasIndex(e => new { e.ProjectId, e.ApplicationRoleId })
                .IsUnique()
                .HasDatabaseName("IX_ProjectRole_Project_Role");
        });

        // Configure UserProjectRole entity
        modelBuilder.Entity<UserProjectRole>(entity =>
        {
            entity.ToTable("UserProjectRole", "dbo");
            entity.HasKey(e => new { e.UserId, e.ProjectRoleId });

            entity.HasOne(e => e.User)
                .WithMany()
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.ProjectRole)
                .WithMany(pr => pr.UserProjectRoles)
                .HasForeignKey(e => e.ProjectRoleId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.Property(e => e.AssignedBy).HasMaxLength(255);
        });

        // REMOVED: ProjectRolePermission entity configuration - permissions system has been removed
        // Authorization is now based on project roles only, without granular permission assignments

        // Configure UserProjectAccess entity
        modelBuilder.Entity<UserProjectAccess>(entity =>
        {
            entity.ToTable("UserProjectAccess", "dbo");
            entity.HasKey(e => new { e.UserId, e.ProjectId });

            entity.HasOne(e => e.User)
                .WithMany()
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            // Note: ProjectId is just an int reference, no direct FK to avoid cross-context dependencies
            entity.Property(e => e.GrantedBy).HasMaxLength(255);
            entity.Property(e => e.AccessReason).HasMaxLength(500);

            entity.HasIndex(e => new { e.UserId, e.IsActive })
                .HasDatabaseName("IX_UserProjectAccess_User_Active");

            entity.HasIndex(e => new { e.ProjectId, e.IsActive })
                .HasDatabaseName("IX_UserProjectAccess_Project_Active");
        });

        // Configure AuditLog entity
        modelBuilder.Entity<AuditLog>(entity =>
        {
            entity.ToTable("AuditLog", "dbo");
            entity.HasKey(e => e.AuditLogId);

            entity.HasOne(e => e.User)
                .WithMany()
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.Property(e => e.EventType).HasMaxLength(50).IsRequired();
            entity.Property(e => e.Action).HasMaxLength(100).IsRequired();
            entity.Property(e => e.Username).HasMaxLength(255).IsRequired();
            entity.Property(e => e.ResourceId).HasMaxLength(255);
            entity.Property(e => e.Result).HasMaxLength(50).IsRequired();
            entity.Property(e => e.Details).HasMaxLength(1000);
            entity.Property(e => e.IpAddress).HasMaxLength(45);
            entity.Property(e => e.UserAgent).HasMaxLength(500);
            entity.Property(e => e.SessionId).HasMaxLength(100);
            entity.Property(e => e.Metadata).HasMaxLength(2000);

            // Indexes for performance
            entity.HasIndex(e => e.Timestamp)
                .HasDatabaseName("IX_AuditLog_Timestamp");

            entity.HasIndex(e => new { e.EventType, e.Timestamp })
                .HasDatabaseName("IX_AuditLog_EventType_Timestamp");

            entity.HasIndex(e => new { e.Username, e.Timestamp })
                .HasDatabaseName("IX_AuditLog_Username_Timestamp");

            entity.HasIndex(e => new { e.ProjectId, e.Timestamp })
                .HasDatabaseName("IX_AuditLog_Project_Timestamp");

            entity.HasIndex(e => new { e.DocumentId, e.Timestamp })
                .HasDatabaseName("IX_AuditLog_Document_Timestamp");

            entity.HasIndex(e => new { e.Result, e.Timestamp })
                .HasDatabaseName("IX_AuditLog_Result_Timestamp");
        });

        // Configure UserSession entity
        modelBuilder.Entity<UserSession>(entity =>
        {
            entity.ToTable("UserSession", "dbo");
            entity.HasKey(e => e.SessionId);

            entity.HasOne(e => e.User)
                .WithMany()
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.Property(e => e.SessionKey).IsRequired().HasMaxLength(100);
            entity.Property(e => e.Username).IsRequired().HasMaxLength(255);
            entity.Property(e => e.IpAddress).HasMaxLength(45);
            entity.Property(e => e.UserAgent).HasMaxLength(500);
            entity.Property(e => e.CurrentRole).HasMaxLength(50);
            entity.Property(e => e.TerminatedBy).HasMaxLength(255);
            entity.Property(e => e.Metadata).HasMaxLength(1000);

            // Indexes for performance
            entity.HasIndex(e => e.SessionKey)
                .HasDatabaseName("IX_UserSession_SessionKey");

            entity.HasIndex(e => new { e.UserId, e.IsActive })
                .HasDatabaseName("IX_UserSession_User_Active");

            entity.HasIndex(e => new { e.IsActive, e.ExpiresAt })
                .HasDatabaseName("IX_UserSession_Active_Expires");

            entity.HasIndex(e => e.LastActivity)
                .HasDatabaseName("IX_UserSession_LastActivity");
        });

        // Configure SessionActivity entity
        modelBuilder.Entity<SessionActivity>(entity =>
        {
            entity.ToTable("SessionActivity", "dbo");
            entity.HasKey(e => e.ActivityId);

            entity.HasOne(e => e.Session)
                .WithMany()
                .HasForeignKey(e => e.SessionId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.Property(e => e.ActivityType).IsRequired().HasMaxLength(50);
            entity.Property(e => e.Action).HasMaxLength(100);
            entity.Property(e => e.Resource).HasMaxLength(500);
            entity.Property(e => e.Details).HasMaxLength(1000);

            // Indexes for performance
            entity.HasIndex(e => new { e.SessionId, e.Timestamp })
                .HasDatabaseName("IX_SessionActivity_Session_Timestamp");

            entity.HasIndex(e => new { e.ActivityType, e.Timestamp })
                .HasDatabaseName("IX_SessionActivity_Type_Timestamp");
        });

        // Seed initial admin role and permissions
        SeedData(modelBuilder);
    }

    private void SeedData(ModelBuilder modelBuilder)
    {
        // REMOVED: Seed initial permissions - permissions system has been removed
        // Authorization is now based on project roles only

        // REMOVED: Seed admin role - ApplicationRole table no longer exists
        // Admin access is now controlled via IsGlobalAdmin flag in ApplicationUser

        // NOTE: RolePermission seeding removed - granular permissions are no longer used
        // Authorization is now based on project roles and page-level authorization
    }
}
