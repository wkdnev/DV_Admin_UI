using DV.Admin.UI.Data;
using DV.Shared.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Http;
using System.Text.Json;

namespace DV.Admin.UI.Services;

/// <summary>
/// Service for comprehensive session management and tracking
/// </summary>
public class SessionManagementService
{
    private readonly SecurityDbContext _securityContext;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ILogger<SessionManagementService> _logger;
    private readonly AuditService _auditService;

    public SessionManagementService(
        SecurityDbContext securityContext,
        IHttpContextAccessor httpContextAccessor,
        ILogger<SessionManagementService> logger,
        AuditService auditService)
    {
        _securityContext = securityContext;
        _httpContextAccessor = httpContextAccessor;
        _logger = logger;
        _auditService = auditService;
    }

    /// <summary>
    /// Initialize or update a user session
    /// </summary>
    public async Task<UserSession> InitializeSessionAsync(string username, int? userId, string? currentRole = null)
    {
        try
        {
            var httpContext = _httpContextAccessor.HttpContext;
            if (httpContext?.Session == null)
            {
                throw new InvalidOperationException("Session is not available");
            }

            var sessionKey = httpContext.Session.Id;
            var ipAddress = GetClientIpAddress(httpContext);
            var userAgent = httpContext.Request.Headers["User-Agent"].FirstOrDefault();

            // Check if session already exists
            var existingSession = await _securityContext.UserSessions
                .FirstOrDefaultAsync(s => s.SessionKey == sessionKey && s.IsActive);

            if (existingSession != null)
            {
                // Update existing session
                existingSession.LastActivity = DateTime.UtcNow;
                existingSession.CurrentRole = currentRole;
                
                await _securityContext.SaveChangesAsync();
                
                // Log activity
                await LogSessionActivityAsync(existingSession.SessionId, SessionActivityTypes.PageView, 
                    "Session Updated", httpContext.Request.Path);

                return existingSession;
            }

            // Create new session
            var session = new UserSession
            {
                SessionKey = sessionKey,
                UserId = userId,
                Username = username,
                IpAddress = ipAddress,
                UserAgent = userAgent,
                CurrentRole = currentRole,
                CreatedAt = DateTime.UtcNow,
                LastActivity = DateTime.UtcNow,
                ExpiresAt = DateTime.UtcNow.AddHours(2), // Match session timeout from Program.cs
                IsActive = true
            };

            _securityContext.UserSessions.Add(session);
            await _securityContext.SaveChangesAsync();

            // Log session creation
            await LogSessionActivityAsync(session.SessionId, SessionActivityTypes.Login, 
                "Session Created", "New user session initialized");

            // Log to audit system
            await _auditService.LogEventAsync(new AuditLog
            {
                EventType = AuditEventTypes.Authentication,
                Action = "SessionCreated",
                Username = username,
                UserId = userId,
                Result = AuditResults.Success,
                Details = $"New session created from {ipAddress}",
                IpAddress = ipAddress,
                SessionId = sessionKey
            });

            _logger.LogInformation("Session initialized for user {Username} with session {SessionKey}", 
                username, sessionKey);

            return session;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error initializing session for user {Username}", username);
            throw;
        }
    }

    /// <summary>
    /// Update session activity
    /// </summary>
    public async Task UpdateSessionActivityAsync(string? activityType = null, string? action = null, string? resource = null)
    {
        try
        {
            var httpContext = _httpContextAccessor.HttpContext;
            if (httpContext?.Session == null) return;

            var sessionKey = httpContext.Session.Id;
            var session = await _securityContext.UserSessions
                .FirstOrDefaultAsync(s => s.SessionKey == sessionKey && s.IsActive);

            if (session != null)
            {
                session.LastActivity = DateTime.UtcNow;
                await _securityContext.SaveChangesAsync();

                if (!string.IsNullOrEmpty(activityType))
                {
                    await LogSessionActivityAsync(session.SessionId, activityType, action, resource);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating session activity");
            // Don't throw - session activity logging should not break the application
        }
    }

    /// <summary>
    /// Update current role for session
    /// </summary>
    public async Task UpdateSessionRoleAsync(string newRole)
    {
        try
        {
            var httpContext = _httpContextAccessor.HttpContext;
            if (httpContext?.Session == null) return;

            var sessionKey = httpContext.Session.Id;
            var session = await _securityContext.UserSessions
                .FirstOrDefaultAsync(s => s.SessionKey == sessionKey && s.IsActive);

            if (session != null)
            {
                var oldRole = session.CurrentRole;
                session.CurrentRole = newRole;
                session.LastActivity = DateTime.UtcNow;
                
                await _securityContext.SaveChangesAsync();

                // Log role switch
                await LogSessionActivityAsync(session.SessionId, SessionActivityTypes.RoleSwitch, 
                    "Role Changed", $"From '{oldRole}' to '{newRole}'");

                // Log to audit system
                await _auditService.LogEventAsync(new AuditLog
                {
                    EventType = AuditEventTypes.Authentication,
                    Action = "RoleSwitch",
                    Username = session.Username,
                    UserId = session.UserId,
                    Result = AuditResults.Success,
                    Details = $"Role changed from '{oldRole}' to '{newRole}'",
                    SessionId = sessionKey
                });
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating session role");
        }
    }

    /// <summary>
    /// Update current role for user's most recent session (for Blazor Server where HttpContext.Session may not be available)
    /// </summary>
    public async Task UpdateSessionRoleByUsernameAsync(string username, string newRole)
    {
        _logger.LogInformation("UpdateSessionRoleByUsernameAsync called for user {Username} with role {Role}", username, newRole);
        
        try
        {
            // Get the most recent active session for this user, or create one if none exists
            var allSessions = await _securityContext.UserSessions
                .Where(s => s.Username == username)
                .OrderByDescending(s => s.LastActivity)
                .ToListAsync();
            
            _logger.LogInformation("Found {Count} total sessions for user {Username}", allSessions.Count, username);
            
            var session = allSessions.FirstOrDefault();

            if (session != null)
            {
                _logger.LogInformation("Updating session {SessionKey}: OldRole={OldRole}, IsActive={IsActive}", 
                    session.SessionKey, session.CurrentRole ?? "(null)", session.IsActive);
                
                var oldRole = session.CurrentRole;
                session.CurrentRole = newRole;
                session.LastActivity = DateTime.UtcNow;
                session.IsActive = true; // Ensure session is active
                
                _logger.LogInformation("Updated session role for user {Username}: {OldRole} -> {NewRole}, SessionId: {SessionId}, Setting IsActive=true", 
                    username, oldRole ?? "(null)", newRole, session.SessionId);
                
                var changes = await _securityContext.SaveChangesAsync();
                _logger.LogInformation("SaveChangesAsync completed: {Changes} entities updated", changes);

                // Log to audit system
                await _auditService.LogEventAsync(new AuditLog
                {
                    EventType = AuditEventTypes.Authentication,
                    Action = "RoleSwitch",
                    Username = session.Username,
                    UserId = session.UserId,
                    Result = AuditResults.Success,
                    Details = $"Role changed from '{oldRole}' to '{newRole}'",
                    SessionId = session.SessionKey
                });
            }
            else
            {
                _logger.LogWarning("No session found for user {Username} to update role", username);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating session role by username for {Username}", username);
        }
    }

    /// <summary>
    /// Terminate a session (logout or admin termination)
    /// </summary>
    public async Task<bool> TerminateSessionAsync(string sessionKey, string? terminatedBy = null, bool isAdminTermination = false)
    {
        try
        {
            var session = await _securityContext.UserSessions
                .FirstOrDefaultAsync(s => s.SessionKey == sessionKey && s.IsActive);

            if (session == null) return false;

            session.IsActive = false;
            session.TerminatedAt = DateTime.UtcNow;
            session.AdminTerminated = isAdminTermination;
            session.TerminatedBy = terminatedBy;

            await _securityContext.SaveChangesAsync();

            // Log termination
            var activityType = isAdminTermination ? SessionActivityTypes.AdminTermination : SessionActivityTypes.Logout;
            await LogSessionActivityAsync(session.SessionId, activityType, 
                "Session Terminated", isAdminTermination ? $"Terminated by admin: {terminatedBy}" : "User logout");

            // Log to audit system
            await _auditService.LogEventAsync(new AuditLog
            {
                EventType = isAdminTermination ? AuditEventTypes.SystemAdmin : AuditEventTypes.Authentication,
                Action = isAdminTermination ? "AdminSessionTermination" : "Logout",
                Username = session.Username,
                UserId = session.UserId,
                Result = AuditResults.Success,
                Details = isAdminTermination ? $"Session terminated by admin: {terminatedBy}" : "User logout",
                SessionId = sessionKey
            });

            _logger.LogInformation("Session {SessionKey} terminated for user {Username}. Admin termination: {IsAdmin}", 
                sessionKey, session.Username, isAdminTermination);

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error terminating session {SessionKey}", sessionKey);
            return false;
        }
    }

    /// <summary>
    /// Get all active sessions
    /// </summary>
    public async Task<List<UserSession>> GetActiveSessionsAsync()
    {
        return await _securityContext.UserSessions
            .Include(s => s.User)
            .Where(s => s.IsActive && s.ExpiresAt > DateTime.UtcNow)
            .OrderByDescending(s => s.LastActivity)
            .ToListAsync();
    }

    /// <summary>
    /// Get sessions for a specific user
    /// </summary>
    public async Task<List<UserSession>> GetUserSessionsAsync(int userId, bool activeOnly = true)
    {
        var query = _securityContext.UserSessions.Where(s => s.UserId == userId);
        
        if (activeOnly)
        {
            query = query.Where(s => s.IsActive && s.ExpiresAt > DateTime.UtcNow);
        }

        return await query
            .OrderByDescending(s => s.LastActivity)
            .ToListAsync();
    }

    /// <summary>
    /// Get session statistics
    /// </summary>
    public async Task<SessionStatistics> GetSessionStatisticsAsync()
    {
        var now = DateTime.UtcNow;
        var today = now.Date;
        var yesterday = today.AddDays(-1);

        return new SessionStatistics
        {
            TotalActiveSessions = await _securityContext.UserSessions
                .CountAsync(s => s.IsActive && s.ExpiresAt > now),
            
            TotalSessionsToday = await _securityContext.UserSessions
                .CountAsync(s => s.CreatedAt >= today),
            
            TotalSessionsYesterday = await _securityContext.UserSessions
                .CountAsync(s => s.CreatedAt >= yesterday && s.CreatedAt < today),
            
            UniqueUsersToday = await _securityContext.UserSessions
                .Where(s => s.CreatedAt >= today)
                .Select(s => s.UserId)
                .Distinct()
                .CountAsync(),
            
            AdminTerminatedSessions = await _securityContext.UserSessions
                .CountAsync(s => s.AdminTerminated),
            
            AverageSessionDuration = await GetAverageSessionDurationAsync(),
            
            TopActiveUsers = await GetTopActiveUsersAsync(5)
        };
    }

    /// <summary>
    /// Clean up expired sessions
    /// </summary>
    public async Task<int> CleanupExpiredSessionsAsync()
    {
        try
        {
            var expiredSessions = await _securityContext.UserSessions
                .Where(s => s.IsActive && s.ExpiresAt <= DateTime.UtcNow)
                .ToListAsync();

            foreach (var session in expiredSessions)
            {
                session.IsActive = false;
                session.TerminatedAt = DateTime.UtcNow;

                // Log expiration
                await LogSessionActivityAsync(session.SessionId, SessionActivityTypes.IdleTimeout, 
                    "Session Expired", "Automatic cleanup - session timeout");
            }

            await _securityContext.SaveChangesAsync();

            _logger.LogInformation("Cleaned up {Count} expired sessions", expiredSessions.Count);
            return expiredSessions.Count;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error cleaning up expired sessions");
            return 0;
        }
    }

    /// <summary>
    /// Log session activity
    /// </summary>
    private async Task LogSessionActivityAsync(int sessionId, string activityType, string? action = null, string? resource = null)
    {
        try
        {
            var activity = new SessionActivity
            {
                SessionId = sessionId,
                ActivityType = activityType,
                Action = action,
                Resource = resource,
                Timestamp = DateTime.UtcNow
            };

            _securityContext.SessionActivities.Add(activity);
            await _securityContext.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error logging session activity");
            // Don't throw - activity logging should not break the application
        }
    }

    /// <summary>
    /// Get client IP address
    /// </summary>
    private string? GetClientIpAddress(HttpContext context)
    {
        var xForwardedFor = context.Request.Headers["X-Forwarded-For"].FirstOrDefault();
        if (!string.IsNullOrEmpty(xForwardedFor))
            return xForwardedFor.Split(',')[0].Trim();

        var xRealIp = context.Request.Headers["X-Real-IP"].FirstOrDefault();
        if (!string.IsNullOrEmpty(xRealIp))
            return xRealIp;

        return context.Connection.RemoteIpAddress?.ToString();
    }

    /// <summary>
    /// Get average session duration
    /// </summary>
    private async Task<TimeSpan> GetAverageSessionDurationAsync()
    {
        var sessions = await _securityContext.UserSessions
            .Where(s => !s.IsActive && s.TerminatedAt.HasValue)
            .Select(s => new { s.CreatedAt, s.TerminatedAt })
            .Take(100) // Sample recent sessions
            .ToListAsync();

        if (!sessions.Any()) return TimeSpan.Zero;

        var totalMinutes = sessions
            .Where(s => s.TerminatedAt.HasValue)
            .Average(s => (s.TerminatedAt!.Value - s.CreatedAt).TotalMinutes);

        return TimeSpan.FromMinutes(totalMinutes);
    }

    /// <summary>
    /// Get top active users
    /// </summary>
    private async Task<List<UserSessionSummary>> GetTopActiveUsersAsync(int count)
    {
        var today = DateTime.UtcNow.Date;
        
        return await _securityContext.UserSessions
            .Where(s => s.CreatedAt >= today)
            .GroupBy(s => new { s.UserId, s.Username })
            .Select(g => new UserSessionSummary
            {
                UserId = g.Key.UserId,
                Username = g.Key.Username ?? "Unknown",
                SessionCount = g.Count(),
                LastActivity = g.Max(s => s.LastActivity)
            })
            .OrderByDescending(s => s.SessionCount)
            .Take(count)
            .ToListAsync();
    }
}

/// <summary>
/// Session statistics for dashboard
/// </summary>
public class SessionStatistics
{
    public int TotalActiveSessions { get; set; }
    public int TotalSessionsToday { get; set; }
    public int TotalSessionsYesterday { get; set; }
    public int UniqueUsersToday { get; set; }
    public int AdminTerminatedSessions { get; set; }
    public TimeSpan AverageSessionDuration { get; set; }
    public List<UserSessionSummary> TopActiveUsers { get; set; } = new();
}

/// <summary>
/// User session summary
/// </summary>
public class UserSessionSummary
{
    public int? UserId { get; set; }
    public string Username { get; set; } = string.Empty;
    public int SessionCount { get; set; }
    public DateTime LastActivity { get; set; }
}
