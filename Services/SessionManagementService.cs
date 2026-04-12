using DV.Shared.Security;
using DV.Shared.Constants;
using DV.Shared.DTOs;
using DV.Shared.Interfaces;
using Microsoft.EntityFrameworkCore;
using DV.Admin.UI.Data;

namespace DV.Admin.UI.Services;

/// <summary>
/// NIST SP 800-53 Rev 5 compliant session management service.
/// Implements AC-12 (Session Termination), AC-10 (Concurrent Session Control),
/// SC-23 (Session Authenticity) controls.
/// </summary>
public class SessionManagementService : ISessionManagementService
{
    private readonly SecurityDbContext _securityContext;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ILogger<SessionManagementService> _logger;
    private readonly AuditService _auditService;
    private readonly NotificationApiService _notificationService;

    public SessionManagementService(
        SecurityDbContext securityContext,
        IHttpContextAccessor httpContextAccessor,
        ILogger<SessionManagementService> logger,
        AuditService auditService,
        NotificationApiService notificationService)
    {
        _securityContext = securityContext;
        _httpContextAccessor = httpContextAccessor;
        _logger = logger;
        _auditService = auditService;
        _notificationService = notificationService;
    }

    /// <summary>
    /// Initialize or update a user session with sliding + absolute timeout enforcement.
    /// AC-10: Enforces concurrent session limit per user.
    /// </summary>
    public async Task<UserSession> InitializeSessionAsync(string username, int? userId, string? currentRole = null)
    {
        try
        {
            var httpContext = _httpContextAccessor.HttpContext;
            if (httpContext?.Session == null)
                throw new InvalidOperationException("Session is not available");

            var sessionKey = httpContext.Session.Id;
            var ipAddress = GetClientIpAddress(httpContext);
            var userAgent = httpContext.Request.Headers["User-Agent"].FirstOrDefault();
            var now = DateTime.UtcNow;

            // Check for existing active session with this key
            var existingSession = await _securityContext.UserSessions
                .FirstOrDefaultAsync(s => s.SessionKey == sessionKey && s.IsActive);

            if (existingSession != null)
            {
                // AC-12: Check absolute timeout (session cannot exceed max lifetime)
                if ((now - existingSession.CreatedAt).TotalHours >= SessionConfig.AbsoluteTimeoutHours)
                {
                    existingSession.IsActive = false;
                    existingSession.TerminatedAt = now;
                    await _securityContext.SaveChangesAsync();
                    await LogSessionActivityAsync(existingSession.SessionId, SessionActivityTypes.AbsoluteTimeout,
                        "Absolute Timeout", $"Session exceeded {SessionConfig.AbsoluteTimeoutHours}h maximum lifetime");
                    _logger.LogInformation("Session {SessionKey} terminated: absolute timeout for {Username}", sessionKey, username);
                    // Fall through to create a new session
                }
                else
                {
                    // SC-23: Detect session anomalies (IP or User-Agent change)
                    if (existingSession.IpAddress != ipAddress || existingSession.UserAgent != userAgent)
                    {
                        _logger.LogWarning("Session anomaly for {Username}: IP {OldIp}->{NewIp}, UA changed: {UaChanged}",
                            username, existingSession.IpAddress, ipAddress, existingSession.UserAgent != userAgent);
                        await LogSessionActivityAsync(existingSession.SessionId, SessionActivityTypes.AnomalyDetected,
                            "Session Anomaly", $"IP: {existingSession.IpAddress}->{ipAddress}");

                        // SI-5: SessionAlert notification — anomaly detected
                        if (userId.HasValue)
                        {
                            try
                            {
                                await _notificationService.CreateNotificationAsync(new CreateNotificationDto
                                {
                                    UserId = userId.Value,
                                    Title = "Session Anomaly Detected",
                                    Message = $"Unusual activity detected on your session: IP address or browser changed.",
                                    Category = NotificationCategories.SessionAlert,
                                    IsImportant = true,
                                    SourceSystem = NotificationSources.System,
                                    CorrelationId = $"anomaly-{existingSession.SessionId}"
                                });
                            }
                            catch (Exception notifEx)
                            {
                                _logger.LogWarning(notifEx, "Failed to create anomaly notification for user {UserId}", userId);
                            }
                        }
                    }

                    // Sliding expiration: extend ExpiresAt on every activity
                    existingSession.LastActivity = now;
                    existingSession.ExpiresAt = now.AddMinutes(SessionConfig.IdleTimeoutMinutes);
                    if (currentRole != null)
                        existingSession.CurrentRole = currentRole;

                    await _securityContext.SaveChangesAsync();
                    return existingSession;
                }
            }

            // AC-10: Enforce concurrent session limit before creating new session
            await EnforceConcurrentSessionLimitAsync(username, now);

            // Create new session
            var session = new UserSession
            {
                SessionKey = sessionKey,
                UserId = userId,
                Username = username,
                IpAddress = ipAddress,
                UserAgent = userAgent,
                CurrentRole = currentRole,
                CreatedAt = now,
                LastActivity = now,
                ExpiresAt = now.AddMinutes(SessionConfig.IdleTimeoutMinutes),
                IsActive = true
            };

            _securityContext.UserSessions.Add(session);
            await _securityContext.SaveChangesAsync();

            await LogSessionActivityAsync(session.SessionId, SessionActivityTypes.Login,
                "Session Created", $"New session from {ipAddress}");

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

            _logger.LogInformation("Session initialized for {Username} with key {SessionKey}", username, sessionKey);
            return session;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error initializing session for {Username}", username);
            throw;
        }
    }

    /// <summary>
    /// Update session activity with throttling to reduce DB writes.
    /// </summary>
    public async Task UpdateSessionActivityAsync(string? activityType = null, string? action = null, string? resource = null)
    {
        try
        {
            var httpContext = _httpContextAccessor.HttpContext;
            if (httpContext?.Session == null) return;

            var sessionKey = httpContext.Session.Id;
            var now = DateTime.UtcNow;

            var session = await _securityContext.UserSessions
                .FirstOrDefaultAsync(s => s.SessionKey == sessionKey && s.IsActive);

            if (session == null) return;

            // Throttle: only update if enough time has passed since last activity
            var secondsSinceLastActivity = (now - session.LastActivity).TotalSeconds;
            if (secondsSinceLastActivity < SessionConfig.ActivityThrottleSeconds && string.IsNullOrEmpty(activityType))
                return;

            // Sliding expiration: extend on activity
            session.LastActivity = now;
            session.ExpiresAt = now.AddMinutes(SessionConfig.IdleTimeoutMinutes);
            await _securityContext.SaveChangesAsync();

            // Only log explicit activity types (not routine heartbeats)
            if (!string.IsNullOrEmpty(activityType) && activityType != "Activity")
            {
                await LogSessionActivityAsync(session.SessionId, activityType, action, resource);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating session activity");
        }
    }

    /// <summary>
    /// Check if a session is valid (active, not expired, within absolute timeout).
    /// </summary>
    public async Task<bool> IsSessionValidAsync(string? sessionKey)
    {
        if (string.IsNullOrEmpty(sessionKey)) return false;

        var now = DateTime.UtcNow;
        var session = await _securityContext.UserSessions
            .FirstOrDefaultAsync(s => s.SessionKey == sessionKey);

        if (session == null) return true; // No DB record yet = new login, allow through

        if (!session.IsActive) return false;
        if (session.ExpiresAt <= now) return false;
        if ((now - session.CreatedAt).TotalHours >= SessionConfig.AbsoluteTimeoutHours) return false;

        return true;
    }

    /// <summary>
    /// Update current role for the active session.
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

            if (session == null) return;

            var oldRole = session.CurrentRole;
            session.CurrentRole = newRole;
            session.LastActivity = DateTime.UtcNow;
            session.ExpiresAt = DateTime.UtcNow.AddMinutes(SessionConfig.IdleTimeoutMinutes);
            await _securityContext.SaveChangesAsync();

            await LogSessionActivityAsync(session.SessionId, SessionActivityTypes.RoleSwitch,
                "Role Changed", $"'{oldRole}' -> '{newRole}'");

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
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating session role");
        }
    }

    /// <summary>
    /// Update role by username (for Blazor Server where HttpContext.Session may not be available).
    /// </summary>
    public async Task UpdateSessionRoleByUsernameAsync(string username, string newRole)
    {
        try
        {
            var session = await _securityContext.UserSessions
                .Where(s => s.Username == username && s.IsActive)
                .OrderByDescending(s => s.LastActivity)
                .FirstOrDefaultAsync();

            if (session == null)
            {
                _logger.LogWarning("No active session found for {Username} to update role", username);
                return;
            }

            var oldRole = session.CurrentRole;
            session.CurrentRole = newRole;
            session.LastActivity = DateTime.UtcNow;
            session.ExpiresAt = DateTime.UtcNow.AddMinutes(SessionConfig.IdleTimeoutMinutes);
            await _securityContext.SaveChangesAsync();

            _logger.LogInformation("Session role updated for {Username}: '{OldRole}' -> '{NewRole}'",
                username, oldRole, newRole);

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
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating session role for {Username}", username);
        }
    }

    /// <summary>
    /// Terminate a specific session.
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

            var activityType = isAdminTermination ? SessionActivityTypes.AdminTermination : SessionActivityTypes.Logout;
            await LogSessionActivityAsync(session.SessionId, activityType,
                "Session Terminated", isAdminTermination ? $"By admin: {terminatedBy}" : "User logout");

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

            _logger.LogInformation("Session {SessionKey} terminated for {Username}. AdminTermination: {IsAdmin}",
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
    /// Terminate all active sessions for a user.
    /// </summary>
    public async Task TerminateAllUserSessionsAsync(string username, string? terminatedBy = null)
    {
        try
        {
            var activeSessions = await _securityContext.UserSessions
                .Where(s => s.Username == username && s.IsActive)
                .ToListAsync();

            var now = DateTime.UtcNow;
            foreach (var session in activeSessions)
            {
                session.IsActive = false;
                session.TerminatedAt = now;
                session.AdminTerminated = terminatedBy != null;
                session.TerminatedBy = terminatedBy;
            }

            await _securityContext.SaveChangesAsync();
            _logger.LogInformation("Terminated {Count} sessions for {Username}", activeSessions.Count, username);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error terminating all sessions for {Username}", username);
        }
    }

    public async Task<List<UserSession>> GetActiveSessionsAsync()
    {
        var now = DateTime.UtcNow;
        var absoluteCutoff = now.AddHours(-SessionConfig.AbsoluteTimeoutHours);

        return await _securityContext.UserSessions
            .Include(s => s.User)
            .Where(s => s.IsActive && s.ExpiresAt > now && s.CreatedAt > absoluteCutoff)
            .OrderByDescending(s => s.LastActivity)
            .ToListAsync();
    }

    public async Task<UserSession?> GetSessionByIdAsync(int sessionId)
    {
        return await _securityContext.UserSessions
            .Include(s => s.User)
            .FirstOrDefaultAsync(s => s.SessionId == sessionId);
    }

    public async Task<List<UserSession>> GetUserSessionsAsync(int userId, bool activeOnly = true)
    {
        var query = _securityContext.UserSessions.Where(s => s.UserId == userId);

        if (activeOnly)
        {
            var now = DateTime.UtcNow;
            var absoluteCutoff = now.AddHours(-SessionConfig.AbsoluteTimeoutHours);
            query = query.Where(s => s.IsActive && s.ExpiresAt > now && s.CreatedAt > absoluteCutoff);
        }

        return await query.OrderByDescending(s => s.LastActivity).ToListAsync();
    }

    public async Task<SessionStatistics> GetSessionStatisticsAsync()
    {
        var now = DateTime.UtcNow;
        var today = now.Date;
        var yesterday = today.AddDays(-1);
        var absoluteCutoff = now.AddHours(-SessionConfig.AbsoluteTimeoutHours);

        // Sequential queries to avoid DbContext concurrency issues
        var totalActive = await _securityContext.UserSessions
            .CountAsync(s => s.IsActive && s.ExpiresAt > now && s.CreatedAt > absoluteCutoff);

        var totalToday = await _securityContext.UserSessions
            .CountAsync(s => s.CreatedAt >= today);

        var totalYesterday = await _securityContext.UserSessions
            .CountAsync(s => s.CreatedAt >= yesterday && s.CreatedAt < today);

        var uniqueToday = await _securityContext.UserSessions
            .Where(s => s.CreatedAt >= today)
            .Select(s => s.UserId)
            .Distinct()
            .CountAsync();

        var adminTerminated = await _securityContext.UserSessions
            .CountAsync(s => s.AdminTerminated);

        var avgDuration = await GetAverageSessionDurationAsync();
        var topUsers = await GetTopActiveUsersAsync(5);

        return new SessionStatistics
        {
            TotalActiveSessions = totalActive,
            TotalSessionsToday = totalToday,
            TotalSessionsYesterday = totalYesterday,
            UniqueUsersToday = uniqueToday,
            AdminTerminatedSessions = adminTerminated,
            AverageSessionDuration = avgDuration,
            TopActiveUsers = topUsers
        };
    }

    /// <summary>
    /// Clean up expired and stale sessions.
    /// </summary>
    public async Task<int> CleanupExpiredSessionsAsync()
    {
        try
        {
            var now = DateTime.UtcNow;
            var absoluteCutoff = now.AddHours(-SessionConfig.AbsoluteTimeoutHours);

            var expiredSessions = await _securityContext.UserSessions
                .Where(s => s.IsActive && (s.ExpiresAt <= now || s.CreatedAt <= absoluteCutoff))
                .ToListAsync();

            foreach (var session in expiredSessions)
            {
                session.IsActive = false;
                session.TerminatedAt = now;

                var reason = session.CreatedAt <= absoluteCutoff
                    ? SessionActivityTypes.AbsoluteTimeout
                    : SessionActivityTypes.IdleTimeout;

                await LogSessionActivityAsync(session.SessionId, reason,
                    "Session Expired", $"Cleanup: {reason}");
            }

            if (expiredSessions.Count > 0)
            {
                await _securityContext.SaveChangesAsync();
                _logger.LogInformation("Cleaned up {Count} expired sessions", expiredSessions.Count);
            }

            return expiredSessions.Count;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error cleaning up expired sessions");
            return 0;
        }
    }

    // ========================================================================
    // Private helpers
    // ========================================================================

    private async Task EnforceConcurrentSessionLimitAsync(string username, DateTime now)
    {
        var activeSessions = await _securityContext.UserSessions
            .Where(s => s.Username == username && s.IsActive && s.ExpiresAt > now)
            .OrderByDescending(s => s.LastActivity)
            .ToListAsync();

        if (activeSessions.Count >= SessionConfig.MaxConcurrentSessionsPerUser)
        {
            var sessionsToTerminate = activeSessions
                .Skip(SessionConfig.MaxConcurrentSessionsPerUser - 1)
                .ToList();

            foreach (var oldSession in sessionsToTerminate)
            {
                oldSession.IsActive = false;
                oldSession.TerminatedAt = now;
                oldSession.TerminatedBy = "System";

                await LogSessionActivityAsync(oldSession.SessionId, SessionActivityTypes.ConcurrentSessionEviction,
                    "Evicted", $"Concurrent session limit ({SessionConfig.MaxConcurrentSessionsPerUser}) exceeded");
            }

            await _securityContext.SaveChangesAsync();
            _logger.LogInformation("Evicted {Count} old sessions for {Username} (concurrent limit: {Limit})",
                sessionsToTerminate.Count, username, SessionConfig.MaxConcurrentSessionsPerUser);

            // SI-5: SessionAlert notification — concurrent session eviction
            var evictedUserId = sessionsToTerminate.FirstOrDefault()?.UserId;
            if (evictedUserId.HasValue)
            {
                try
                {
                    await _notificationService.CreateNotificationAsync(new CreateNotificationDto
                    {
                        UserId = evictedUserId.Value,
                        Title = "Session Evicted",
                        Message = $"An older session was terminated because the concurrent session limit ({SessionConfig.MaxConcurrentSessionsPerUser}) was reached.",
                        Category = NotificationCategories.SessionAlert,
                        IsImportant = true,
                        SourceSystem = NotificationSources.System,
                        CorrelationId = $"eviction-{username}"
                    });
                }
                catch (Exception notifEx)
                {
                    _logger.LogWarning(notifEx, "Failed to create session eviction notification for {Username}", username);
                }
            }
        }
    }

    private async Task LogSessionActivityAsync(int sessionId, string activityType, string? action = null, string? resource = null)
    {
        try
        {
            _securityContext.SessionActivities.Add(new SessionActivity
            {
                SessionId = sessionId,
                ActivityType = activityType,
                Action = action,
                Resource = resource,
                Timestamp = DateTime.UtcNow
            });
            await _securityContext.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error logging session activity");
        }
    }

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

    private async Task<TimeSpan> GetAverageSessionDurationAsync()
    {
        var sessions = await _securityContext.UserSessions
            .Where(s => !s.IsActive && s.TerminatedAt.HasValue)
            .OrderByDescending(s => s.TerminatedAt)
            .Select(s => new { s.CreatedAt, s.TerminatedAt })
            .Take(100)
            .ToListAsync();

        if (!sessions.Any()) return TimeSpan.Zero;

        var totalMinutes = sessions
            .Where(s => s.TerminatedAt.HasValue)
            .Average(s => (s.TerminatedAt!.Value - s.CreatedAt).TotalMinutes);

        return TimeSpan.FromMinutes(totalMinutes);
    }

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
