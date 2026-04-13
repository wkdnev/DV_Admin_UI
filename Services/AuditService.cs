using DV.Admin.UI.Data;
using DV.Shared.Security;
using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using System.Diagnostics;

namespace DV.Admin.UI.Services;

/// <summary>
/// Service for comprehensive audit logging of user activities and security events
/// </summary>
public class AuditService
{
    private readonly SecurityDbContext _securityContext;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ILogger<AuditService> _logger;

    public AuditService(
        SecurityDbContext securityContext,
        IHttpContextAccessor httpContextAccessor,
        ILogger<AuditService> logger)
    {
        _securityContext = securityContext;
        _httpContextAccessor = httpContextAccessor;
        _logger = logger;
    }

    /// <summary>
    /// Logs a successful document access event
    /// </summary>
    public async Task LogDocumentAccessAsync(
        string username,
        int? userId,
        int documentId,
        int? projectId,
        string action,
        string? documentTitle = null,
        long? durationMs = null)
    {
        var details = $"Document accessed: {documentTitle ?? "Unknown"}";
        
        await LogEventAsync(new AuditLog
        {
            EventType = AuditEventTypes.DocumentAccess,
            Action = action,
            Username = username,
            UserId = userId,
            ProjectId = projectId ?? 0,
            DocumentId = documentId,
            Result = AuditResults.Success,
            Details = details,
            DurationMs = durationMs
        });
    }

    /// <summary>
    /// Logs a failed document access attempt
    /// </summary>
    public async Task LogDocumentAccessDeniedAsync(
        string username,
        int? userId,
        int documentId,
        int? projectId,
        string reason,
        string? documentTitle = null)
    {
        var details = $"Access denied to document: {documentTitle ?? "Unknown"}. Reason: {reason}";
        
        await LogEventAsync(new AuditLog
        {
            EventType = AuditEventTypes.DocumentAccess,
            Action = AuditActions.ViewDocument,
            Username = username,
            UserId = userId,
            ProjectId = projectId ?? 0,
            DocumentId = documentId,
            Result = AuditResults.Denied,
            Details = details
        });
    }

    /// <summary>
    /// Logs project access events
    /// </summary>
    public async Task LogProjectAccessAsync(
        string username,
        int? userId,
        int? projectId,
        string action,
        string result,
        string? details = null,
        string? grantedBy = null)
    {
        var fullDetails = details;
        if (!string.IsNullOrEmpty(grantedBy))
        {
            fullDetails = $"{details} (Granted by: {grantedBy})";
        }

        await LogEventAsync(new AuditLog
        {
            EventType = AuditEventTypes.ProjectAccess,
            Action = action,
            Username = username,
            UserId = userId,
            ProjectId = projectId,
            Result = result,
            Details = fullDetails
        });
    }

    /// <summary>
    /// Logs admin override events
    /// </summary>
    public async Task LogAdminOverrideAsync(
        string adminUsername,
        int? adminUserId,
        string action,
        string? targetResource = null,
        int? targetUserId = null,
        int? projectId = null,
        string? details = null)
    {
        var metadata = new
        {
            TargetUserId = targetUserId,
            TargetResource = targetResource,
            AdminPrivileges = true
        };

        await LogEventAsync(new AuditLog
        {
            EventType = AuditEventTypes.SystemAdmin,
            Action = action,
            Username = adminUsername,
            UserId = adminUserId,
            ProjectId = projectId,
            ResourceId = targetResource,
            Result = AuditResults.Success,
            Details = $"Admin override: {details}",
            Metadata = JsonSerializer.Serialize(metadata)
        });
    }

    /// <summary>
    /// Logs role management events
    /// </summary>
    public async Task LogRoleManagementAsync(
        string username,
        int? userId,
        string action,
        string roleName,
        int? targetUserId = null,
        int? projectId = null,
        string? details = null)
    {
        var metadata = new
        {
            RoleName = roleName,
            TargetUserId = targetUserId,
            ProjectScoped = projectId.HasValue
        };

        await LogEventAsync(new AuditLog
        {
            EventType = AuditEventTypes.RoleManagement,
            Action = action,
            Username = username,
            UserId = userId,
            ProjectId = projectId,
            ResourceId = roleName,
            Result = AuditResults.Success,
            Details = details,
            Metadata = JsonSerializer.Serialize(metadata)
        });
    }

    /// <summary>
    /// Logs user management events
    /// </summary>
    public async Task LogUserManagementAsync(
        string adminUsername,
        int? adminUserId,
        string action,
        string targetUsername,
        int? targetUserId = null,
        string? details = null)
    {
        var metadata = new
        {
            TargetUsername = targetUsername,
            TargetUserId = targetUserId
        };

        await LogEventAsync(new AuditLog
        {
            EventType = AuditEventTypes.UserManagement,
            Action = action,
            Username = adminUsername,
            UserId = adminUserId,
            ResourceId = targetUsername,
            Result = AuditResults.Success,
            Details = details,
            Metadata = JsonSerializer.Serialize(metadata)
        });
    }

    /// <summary>
    /// Logs authentication events
    /// </summary>
    public async Task LogAuthenticationAsync(
        string username,
        string action,
        string result,
        string? details = null)
    {
        await LogEventAsync(new AuditLog
        {
            EventType = AuditEventTypes.Authentication,
            Action = action,
            Username = username,
            Result = result,
            Details = details
        });
    }

    /// <summary>
    /// Logs security violations or suspicious activity
    /// </summary>
    public async Task LogSecurityEventAsync(
        string username,
        int? userId,
        string action,
        string details,
        int? projectId = null,
        int? documentId = null)
    {
        await LogEventAsync(new AuditLog
        {
            EventType = AuditEventTypes.SecurityEvent,
            Action = action,
            Username = username,
            UserId = userId,
            ProjectId = projectId,
            DocumentId = documentId,
            Result = AuditResults.Warning,
            Details = details
        });
    }

    /// <summary>
    /// Logs search activities
    /// </summary>
    public async Task LogSearchAsync(
        string username,
        int? userId,
        int? projectId,
        string searchTerm,
        int resultCount,
        long durationMs)
    {
        var details = $"Search term: '{searchTerm}', Results: {resultCount}";

        await LogEventAsync(new AuditLog
        {
            EventType = AuditEventTypes.DocumentAccess,
            Action = AuditActions.SearchDocuments,
            Username = username,
            UserId = userId,
            ProjectId = projectId,
            Result = AuditResults.Success,
            Details = details,
            DurationMs = durationMs
        });
    }

    /// <summary>
    /// Core method to log audit events with NIST SP 800-53 AU-9(3) hash chaining
    /// </summary>
    public async Task LogEventAsync(AuditLog auditLog)
    {
        try
        {
            _logger.LogInformation("AUDIT DEBUG: Attempting to log audit event: {EventType}.{Action} for user {Username}", 
                auditLog.EventType, auditLog.Action, auditLog.Username);

            // Enhance with HTTP context information
            EnhanceWithHttpContext(auditLog);

            _logger.LogInformation("AUDIT DEBUG: Enhanced audit log - IP: {IpAddress}, SessionId: {SessionId}", 
                auditLog.IpAddress, auditLog.SessionId);

            // NIST SP 800-53 AU-9(3): Hash chaining for cryptographic integrity
            // Use advisory lock to serialize chain writes across all app instances
            var strategy = _securityContext.Database.CreateExecutionStrategy();
            await strategy.ExecuteAsync(async () =>
            {
                using var transaction = await _securityContext.Database.BeginTransactionAsync();

                // PostgreSQL advisory lock (key derived from NIST 800-53 AU-9(3))
                await _securityContext.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(8053093)");

                // Get the hash of the most recent audit record (the chain tip)
                var previousHash = await _securityContext.AuditLogs
                    .Where(al => al.RecordHash != null)
                    .OrderByDescending(al => al.AuditLogId)
                    .Select(al => al.RecordHash)
                    .FirstOrDefaultAsync()
                    ?? AuditLog.GenesisHash;

                auditLog.PreviousHash = previousHash;
                auditLog.RecordHash = ComputeRecordHash(auditLog);

                _securityContext.AuditLogs.Add(auditLog);
                await _securityContext.SaveChangesAsync();
                await transaction.CommitAsync();
            });

            _logger.LogInformation("AUDIT DEBUG: Audit event saved successfully with hash chain. RecordHash: {Hash}", auditLog.RecordHash);

            // Also log to application logger for immediate visibility
            var logLevel = GetLogLevel(auditLog.Result);
            _logger.Log(logLevel, 
                "AUDIT: {EventType}.{Action} by {Username} - {Result}: {Details}",
                auditLog.EventType, auditLog.Action, auditLog.Username, auditLog.Result, auditLog.Details);
        }
        catch (Exception ex)
        {
            // Audit logging should never break the application
            _logger.LogError(ex, "AUDIT ERROR: Failed to write audit log for {EventType}.{Action} by {Username}. Error: {ErrorMessage}",
                auditLog.EventType, auditLog.Action, auditLog.Username, ex.Message);
            
            _logger.LogError("AUDIT ERROR: Failed audit log details: EventType={EventType}, Action={Action}, Username={Username}, Result={Result}, Details={Details}",
                auditLog.EventType, auditLog.Action, auditLog.Username, auditLog.Result, auditLog.Details);
        }
    }

    /// <summary>
    /// Retrieves audit logs with filtering options
    /// </summary>
    public async Task<List<AuditLog>> GetAuditLogsAsync(
        DateTime? fromDate = null,
        DateTime? toDate = null,
        string? username = null,
        string? eventType = null,
        string? action = null,
        string? result = null,
        int? projectId = null,
        int? documentId = null,
        int skip = 0,
        int take = 100)
    {
        var query = _securityContext.AuditLogs
            .Include(al => al.User)
            .AsQueryable();

        if (fromDate.HasValue)
            query = query.Where(al => al.Timestamp >= fromDate.Value);

        if (toDate.HasValue)
            query = query.Where(al => al.Timestamp <= toDate.Value);

        if (!string.IsNullOrEmpty(username))
            query = query.Where(al => al.Username.Contains(username));

        if (!string.IsNullOrEmpty(eventType))
            query = query.Where(al => al.EventType == eventType);

        if (!string.IsNullOrEmpty(action))
            query = query.Where(al => al.Action == action);

        if (!string.IsNullOrEmpty(result))
            query = query.Where(al => al.Result == result);

        if (projectId.HasValue)
            query = query.Where(al => al.ProjectId == projectId.Value);

        if (documentId.HasValue)
            query = query.Where(al => al.DocumentId == documentId.Value);

        return await query
            .OrderByDescending(al => al.Timestamp)
            .Skip(skip)
            .Take(take)
            .ToListAsync();
    }

    /// <summary>
    /// Gets audit statistics for dashboard
    /// </summary>
    public async Task<AuditStatistics> GetAuditStatisticsAsync(DateTime? fromDate = null, DateTime? toDate = null)
    {
        var query = _securityContext.AuditLogs.AsQueryable();

        if (fromDate.HasValue)
            query = query.Where(al => al.Timestamp >= fromDate.Value);

        if (toDate.HasValue)
            query = query.Where(al => al.Timestamp <= toDate.Value);

        var stats = await query
            .GroupBy(al => new { al.EventType, al.Result })
            .Select(g => new { g.Key.EventType, g.Key.Result, Count = g.Count() })
            .ToListAsync();

        return new AuditStatistics
        {
            TotalEvents = stats.Sum(s => s.Count),
            SuccessfulEvents = stats.Where(s => s.Result == AuditResults.Success).Sum(s => s.Count),
            FailedEvents = stats.Where(s => s.Result == AuditResults.Failed).Sum(s => s.Count),
            DeniedEvents = stats.Where(s => s.Result == AuditResults.Denied).Sum(s => s.Count),
            DocumentAccessEvents = stats.Where(s => s.EventType == AuditEventTypes.DocumentAccess).Sum(s => s.Count),
            ProjectAccessEvents = stats.Where(s => s.EventType == AuditEventTypes.ProjectAccess).Sum(s => s.Count),
            AdminEvents = stats.Where(s => s.EventType == AuditEventTypes.SystemAdmin).Sum(s => s.Count),
            SecurityEvents = stats.Where(s => s.EventType == AuditEventTypes.SecurityEvent).Sum(s => s.Count),
            FromDate = fromDate,
            ToDate = toDate
        };
    }

    /// <summary>
    /// Creates a timed audit operation for measuring duration
    /// </summary>
    public TimedAuditOperation StartTimedOperation(
        string username,
        int? userId,
        string eventType,
        string action)
    {
        return new TimedAuditOperation(this, username, userId, eventType, action);
    }

    private void EnhanceWithHttpContext(AuditLog auditLog)
    {
        try
        {
            var httpContext = _httpContextAccessor.HttpContext;
            if (httpContext != null)
            {
                auditLog.IpAddress = GetClientIpAddress(httpContext);
                auditLog.UserAgent = httpContext.Request.Headers["User-Agent"].FirstOrDefault();
                
                // Safely get session ID - don't fail if sessions aren't working
                try
                {
                    auditLog.SessionId = httpContext.Session?.Id;
                }
                catch (Exception sessionEx)
                {
                    // Log session error but don't fail the audit
                    _logger.LogWarning(sessionEx, "Unable to access session for audit log");
                    auditLog.SessionId = null;
                }
            }
        }
        catch (Exception ex)
        {
            // Don't fail audit logging if HTTP context enhancement fails
            _logger.LogWarning(ex, "Unable to enhance audit log with HTTP context information");
        }
    }

    private string? GetClientIpAddress(HttpContext context)
    {
        // Try to get the real IP address from various headers
        var xForwardedFor = context.Request.Headers["X-Forwarded-For"].FirstOrDefault();
        if (!string.IsNullOrEmpty(xForwardedFor))
            return xForwardedFor.Split(',')[0].Trim();

        var xRealIp = context.Request.Headers["X-Real-IP"].FirstOrDefault();
        if (!string.IsNullOrEmpty(xRealIp))
            return xRealIp;

        return context.Connection.RemoteIpAddress?.ToString();
    }

    private LogLevel GetLogLevel(string result)
    {
        return result switch
        {
            AuditResults.Success => LogLevel.Information,
            AuditResults.Warning => LogLevel.Warning,
            AuditResults.Failed => LogLevel.Error,
            AuditResults.Denied => LogLevel.Warning,
            AuditResults.Error => LogLevel.Error,
            _ => LogLevel.Information
        };
    }

    /// <summary>
    /// Computes SHA-256 hash of an audit record's canonical fields + PreviousHash.
    /// NIST SP 800-53 AU-9(3) cryptographic integrity.
    /// </summary>
    internal static string ComputeRecordHash(AuditLog log)
    {
        var payload = string.Join("|",
            log.EventType,
            log.Action,
            log.Username,
            log.UserId?.ToString() ?? "",
            log.ProjectId?.ToString() ?? "",
            log.DocumentId?.ToString() ?? "",
            log.ResourceId ?? "",
            log.Result,
            log.Details ?? "",
            log.IpAddress ?? "",
            log.UserAgent ?? "",
            log.SessionId ?? "",
            log.Timestamp.ToString("O"),
            log.DurationMs?.ToString() ?? "",
            log.Metadata ?? "",
            log.PreviousHash ?? "");

        var hashBytes = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(payload));
        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }

    /// <summary>
    /// Verifies the integrity of the audit log hash chain.
    /// Returns (IsValid, FirstBrokenRecordId, TotalChecked).
    /// Skips pre-hash-chaining records (null RecordHash).
    /// NIST SP 800-53 AU-9(3) verification.
    /// </summary>
    public async Task<(bool IsValid, int? FirstBrokenId, int TotalChecked)> VerifyAuditChainIntegrityAsync(int? maxRecords = null)
    {
        var query = _securityContext.AuditLogs
            .Where(al => al.RecordHash != null)
            .OrderBy(al => al.AuditLogId)
            .AsQueryable();

        if (maxRecords.HasValue)
        {
            // Verify the most recent N records
            query = _securityContext.AuditLogs
                .Where(al => al.RecordHash != null)
                .OrderByDescending(al => al.AuditLogId)
                .Take(maxRecords.Value)
                .OrderBy(al => al.AuditLogId);
        }

        var logs = await query.ToListAsync();
        if (logs.Count == 0)
            return (true, null, 0);

        // For the first hashed record, determine its expected PreviousHash
        string expectedPreviousHash;
        if (maxRecords.HasValue)
        {
            // Look up the record immediately before our window
            var firstId = logs[0].AuditLogId;
            expectedPreviousHash = await _securityContext.AuditLogs
                .Where(al => al.AuditLogId < firstId)
                .OrderByDescending(al => al.AuditLogId)
                .Select(al => al.RecordHash)
                .FirstOrDefaultAsync()
                ?? AuditLog.GenesisHash;
        }
        else
        {
            expectedPreviousHash = AuditLog.GenesisHash;
        }

        foreach (var log in logs)
        {
            // Verify chain linkage
            if (log.PreviousHash != expectedPreviousHash)
            {
                _logger.LogWarning("AUDIT INTEGRITY: Chain break at AuditLogId {Id}. Expected PreviousHash {Expected}, found {Actual}",
                    log.AuditLogId, expectedPreviousHash, log.PreviousHash);
                return (false, log.AuditLogId, logs.Count);
            }

            // Verify record hash
            var computedHash = ComputeRecordHash(log);
            if (computedHash != log.RecordHash)
            {
                _logger.LogWarning("AUDIT INTEGRITY: Hash mismatch at AuditLogId {Id}. Expected {Expected}, found {Actual}",
                    log.AuditLogId, computedHash, log.RecordHash);
                return (false, log.AuditLogId, logs.Count);
            }

            expectedPreviousHash = log.RecordHash;
        }

        _logger.LogInformation("AUDIT INTEGRITY: Chain verification passed for {Count} records", logs.Count);
        return (true, null, logs.Count);
    }
}

/// <summary>
/// Audit statistics for reporting and monitoring
/// </summary>
public class AuditStatistics
{
    public int TotalEvents { get; set; }
    public int SuccessfulEvents { get; set; }
    public int FailedEvents { get; set; }
    public int DeniedEvents { get; set; }
    public int DocumentAccessEvents { get; set; }
    public int ProjectAccessEvents { get; set; }
    public int AdminEvents { get; set; }
    public int SecurityEvents { get; set; }
    public DateTime? FromDate { get; set; }
    public DateTime? ToDate { get; set; }
}

/// <summary>
/// Helper class for measuring operation duration
/// </summary>
public class TimedAuditOperation : IDisposable
{
    private readonly AuditService _auditService;
    private readonly string _username;
    private readonly int? _userId;
    private readonly string _eventType;
    private readonly string _action;
    private readonly Stopwatch _stopwatch;
    private bool _disposed = false;

    public TimedAuditOperation(
        AuditService auditService,
        string username,
        int? userId,
        string eventType,
        string action)
    {
        _auditService = auditService;
        _username = username;
        _userId = userId;
        _eventType = eventType;
        _action = action;
        _stopwatch = Stopwatch.StartNew();
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _ = Task.Run(async () =>
            {
                await _auditService.LogEventAsync(new AuditLog
                {
                    EventType = _eventType,
                    Action = _action,
                    Username = _username,
                    UserId = _userId,
                    Result = AuditResults.Error,
                    Details = "Operation incomplete - disposed without completion",
                    DurationMs = _stopwatch.ElapsedMilliseconds
                });
            });
        }
    }

    public async Task CompleteAsync(string result, string? details = null, int? projectId = null, int? documentId = null)
    {
        _stopwatch.Stop();
        
        await _auditService.LogEventAsync(new AuditLog
        {
            EventType = _eventType,
            Action = _action,
            Username = _username,
            UserId = _userId,
            ProjectId = projectId,
            DocumentId = documentId,
            Result = result,
            Details = details,
            DurationMs = _stopwatch.ElapsedMilliseconds
        });

        _disposed = true;
    }
}
