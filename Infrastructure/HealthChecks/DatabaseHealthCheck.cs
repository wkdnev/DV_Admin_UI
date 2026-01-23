using DV.Admin.UI.Data;
using DV.Admin.UI.Data;
// ============================================================================
// DatabaseHealthCheck.cs - Database Health Check Implementation
// ============================================================================
//
// Purpose: Provides health check monitoring for database connectivity and
// basic operations across both SecurityDbContext and AppDbContext.
//
// Features:
// - Connection testing for both database contexts
// - Basic query execution validation
// - Performance monitoring with timeout handling
// - Detailed health status reporting
//
// ============================================================================

using Microsoft.Extensions.Diagnostics.HealthChecks;
using DV.Shared.Security;
using DV.Admin.UI.Data;
using DV.Shared.Models;
using Microsoft.EntityFrameworkCore;

namespace DV.Admin.UI.Infrastructure.HealthChecks;

/// <summary>
/// Health check for database connectivity and basic operations
/// </summary>
public class DatabaseHealthCheck : IHealthCheck
{
    private readonly SecurityDbContext _securityContext;
    private readonly AppDbContext _appContext;
    private readonly ILogger<DatabaseHealthCheck> _logger;

    public DatabaseHealthCheck(
        SecurityDbContext securityContext, 
        AppDbContext appContext,
        ILogger<DatabaseHealthCheck> logger)
    {
        _securityContext = securityContext;
        _appContext = appContext;
        _logger = logger;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, 
        CancellationToken cancellationToken = default)
    {
        try
        {
            var data = new Dictionary<string, object>();
            var healthyComponents = new List<string>();
            var issues = new List<string>();

            // Check Security Database Context
            var securityCheckResult = await CheckSecurityDatabaseAsync(cancellationToken);
            data.Add("SecurityDatabase", securityCheckResult);
            
            if (securityCheckResult.IsHealthy)
                healthyComponents.Add("SecurityDatabase");
            else
                issues.Add($"SecurityDatabase: {securityCheckResult.ErrorMessage}");

            // Check Application Database Context
            var appCheckResult = await CheckApplicationDatabaseAsync(cancellationToken);
            data.Add("ApplicationDatabase", appCheckResult);
            
            if (appCheckResult.IsHealthy)
                healthyComponents.Add("ApplicationDatabase");
            else
                issues.Add($"ApplicationDatabase: {appCheckResult.ErrorMessage}");

            // Overall health determination
            var isHealthy = securityCheckResult.IsHealthy && appCheckResult.IsHealthy;
            var status = isHealthy ? HealthStatus.Healthy : HealthStatus.Unhealthy;

            var description = isHealthy 
                ? $"All database connections healthy: {string.Join(", ", healthyComponents)}"
                : $"Database issues detected: {string.Join("; ", issues)}";

            _logger.LogInformation("Database health check completed: {Status} - {Description}", 
                status, description);

            return new HealthCheckResult(status, description, null, data);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Database health check failed with exception");
            return new HealthCheckResult(
                HealthStatus.Unhealthy, 
                "Database health check failed with exception", 
                ex,
                new Dictionary<string, object> { ["Error"] = ex.Message });
        }
    }

    private async Task<DatabaseCheckResult> CheckSecurityDatabaseAsync(CancellationToken cancellationToken)
    {
        try
        {
            var startTime = DateTime.UtcNow;
            
            // Test basic connectivity
            var canConnect = await _securityContext.Database.CanConnectAsync(cancellationToken);
            if (!canConnect)
            {
                return new DatabaseCheckResult 
                { 
                    IsHealthy = false, 
                    ErrorMessage = "Cannot connect to security database" 
                };
            }

            // Test basic query operation
            var userCount = await _securityContext.Users.CountAsync(cancellationToken);
            var projectRoleCount = await _securityContext.ProjectRoles.CountAsync(cancellationToken);
            
            var responseTime = DateTime.UtcNow - startTime;

            return new DatabaseCheckResult
            {
                IsHealthy = true,
                ResponseTimeMs = (int)responseTime.TotalMilliseconds,
                UserCount = userCount,
                ProjectRoleCount = projectRoleCount
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Security database health check failed");
            return new DatabaseCheckResult 
            { 
                IsHealthy = false, 
                ErrorMessage = ex.Message 
            };
        }
    }

    private async Task<DatabaseCheckResult> CheckApplicationDatabaseAsync(CancellationToken cancellationToken)
    {
        try
        {
            var startTime = DateTime.UtcNow;
            
            // Test basic connectivity
            var canConnect = await _appContext.Database.CanConnectAsync(cancellationToken);
            if (!canConnect)
            {
                return new DatabaseCheckResult 
                { 
                    IsHealthy = false, 
                    ErrorMessage = "Cannot connect to application database" 
                };
            }

            // Test basic query operation
            var projectCount = await _appContext.Projects.CountAsync(cancellationToken);
            
            var responseTime = DateTime.UtcNow - startTime;

            return new DatabaseCheckResult
            {
                IsHealthy = true,
                ResponseTimeMs = (int)responseTime.TotalMilliseconds,
                ProjectCount = projectCount
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Application database health check failed");
            return new DatabaseCheckResult 
            { 
                IsHealthy = false, 
                ErrorMessage = ex.Message 
            };
        }
    }
}

/// <summary>
/// Result of database health check operations
/// </summary>
public class DatabaseCheckResult
{
    public bool IsHealthy { get; set; }
    public string? ErrorMessage { get; set; }
    public int ResponseTimeMs { get; set; }
    public int UserCount { get; set; }
    public int ProjectRoleCount { get; set; }
    public int ProjectCount { get; set; }
}
