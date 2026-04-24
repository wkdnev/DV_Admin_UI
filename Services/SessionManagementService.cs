using DV.Shared.Security;
using DV.Shared.DTOs;
using DV.Shared.Interfaces;
using System.Net.Http.Json;

namespace DV.Admin.UI.Services;

/// <summary>
/// HTTP-delegated session management service.
/// All session state is managed by DV_API; this service forwards calls via the named "Api" HttpClient.
/// </summary>
public class SessionManagementService : ISessionManagementService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ILogger<SessionManagementService> _logger;

    public SessionManagementService(
        IHttpClientFactory httpClientFactory,
        IHttpContextAccessor httpContextAccessor,
        ILogger<SessionManagementService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _httpContextAccessor = httpContextAccessor;
        _logger = logger;
    }

    private HttpClient CreateClient() => _httpClientFactory.CreateClient("Api");
    private string? GetSessionKey() => _httpContextAccessor.HttpContext?.Session?.Id;

    private string? GetClientIpAddress()
    {
        var ctx = _httpContextAccessor.HttpContext;
        if (ctx == null) return null;
        return ctx.Request.Headers["X-Forwarded-For"].FirstOrDefault()?.Split(',')[0].Trim()
            ?? ctx.Connection.RemoteIpAddress?.ToString();
    }

    public async Task<UserSession> InitializeSessionAsync(string username, int? userId, string? currentRole = null)
    {
        var ctx = _httpContextAccessor.HttpContext;
        var sessionKey = ctx?.Session?.Id;
        if (string.IsNullOrEmpty(sessionKey))
            throw new InvalidOperationException("Session is not available");

        var request = new InitializeSessionRequestDto
        {
            Username = username,
            UserId = userId,
            SessionKey = sessionKey,
            IpAddress = GetClientIpAddress(),
            UserAgent = ctx?.Request.Headers["User-Agent"].FirstOrDefault(),
            CurrentRole = currentRole
        };

        try
        {
            var response = await CreateClient().PostAsJsonAsync("api/session/initialize", request);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadFromJsonAsync<UserSession>()
                ?? throw new InvalidOperationException("No session returned from API");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error initializing session for {Username}", username);
            throw;
        }
    }

    public async Task UpdateSessionActivityAsync(string? activityType = null, string? action = null, string? resource = null)
    {
        var sessionKey = GetSessionKey();
        if (string.IsNullOrEmpty(sessionKey)) return;
        try
        {
            var request = new SessionActivityRequestDto
            {
                SessionKey = sessionKey,
                ActivityType = activityType,
                Action = action,
                Resource = resource
            };
            await CreateClient().PostAsJsonAsync("api/session/activity", request);
        }
        catch (Exception ex) { _logger.LogWarning(ex, "Error updating session activity"); }
    }

    public async Task<bool> IsSessionValidAsync(string? sessionKey)
    {
        if (string.IsNullOrEmpty(sessionKey)) return false;
        try
        {
            var response = await CreateClient().GetAsync($"api/session/validate/{Uri.EscapeDataString(sessionKey)}");
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error validating session â€” failing open");
            return true;
        }
    }

    public async Task UpdateSessionRoleAsync(string newRole)
    {
        var sessionKey = GetSessionKey();
        if (string.IsNullOrEmpty(sessionKey)) return;
        try
        {
            var request = new UpdateSessionRoleRequestDto { SessionKey = sessionKey, NewRole = newRole };
            await CreateClient().PutAsJsonAsync("api/session/role", request);
        }
        catch (Exception ex) { _logger.LogWarning(ex, "Error updating session role"); }
    }

    public async Task UpdateSessionRoleByUsernameAsync(string username, string newRole)
    {
        try
        {
            var request = new UpdateSessionRoleByUsernameRequestDto { Username = username, NewRole = newRole };
            await CreateClient().PutAsJsonAsync("api/session/role/byusername", request);
        }
        catch (Exception ex) { _logger.LogWarning(ex, "Error updating session role for {Username}", username); }
    }

    public async Task<bool> TerminateSessionAsync(string sessionKey, string? terminatedBy = null, bool isAdminTermination = false)
    {
        try
        {
            var response = await CreateClient().DeleteAsync($"api/session/{Uri.EscapeDataString(sessionKey)}");
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex) { _logger.LogError(ex, "Error terminating session {SessionKey}", sessionKey); return false; }
    }

    public async Task TerminateAllUserSessionsAsync(string username, string? terminatedBy = null)
    {
        try { await CreateClient().DeleteAsync($"api/session/user/{Uri.EscapeDataString(username)}"); }
        catch (Exception ex) { _logger.LogError(ex, "Error terminating all sessions for {Username}", username); }
    }

    public async Task<List<UserSession>> GetActiveSessionsAsync()
    {
        try { return await CreateClient().GetFromJsonAsync<List<UserSession>>("api/session") ?? []; }
        catch (Exception ex) { _logger.LogError(ex, "Error getting active sessions"); return []; }
    }

    public async Task<UserSession?> GetSessionByIdAsync(int sessionId)
    {
        try { return await CreateClient().GetFromJsonAsync<UserSession>($"api/session/{sessionId}"); }
        catch (Exception ex) { _logger.LogError(ex, "Error getting session {SessionId}", sessionId); return null; }
    }

    public async Task<List<UserSession>> GetUserSessionsAsync(int userId, bool activeOnly = true)
    {
        try
        {
            return await CreateClient().GetFromJsonAsync<List<UserSession>>(
                $"api/session/user/{userId}?activeOnly={activeOnly}") ?? [];
        }
        catch (Exception ex) { _logger.LogError(ex, "Error getting sessions for user {UserId}", userId); return []; }
    }

    public async Task<List<UserSession>> GetUserSessionsByUsernameAsync(string username, bool activeOnly = true)
    {
        try
        {
            return await CreateClient().GetFromJsonAsync<List<UserSession>>(
                $"api/session/user/byusername/{Uri.EscapeDataString(username)}?activeOnly={activeOnly}") ?? [];
        }
        catch (Exception ex) { _logger.LogError(ex, "Error getting sessions for user {Username}", username); return []; }
    }

    public async Task<SessionStatistics> GetSessionStatisticsAsync()
    {
        try
        {
            return await CreateClient().GetFromJsonAsync<SessionStatistics>("api/session/statistics")
                ?? new SessionStatistics();
        }
        catch (Exception ex) { _logger.LogError(ex, "Error getting session statistics"); return new SessionStatistics(); }
    }

    public async Task<int> CleanupExpiredSessionsAsync()
    {
        try
        {
            var response = await CreateClient().PostAsync("api/session/cleanup", null);
            return response.IsSuccessStatusCode
                ? await response.Content.ReadFromJsonAsync<int>()
                : 0;
        }
        catch (Exception ex) { _logger.LogError(ex, "Error triggering session cleanup"); return 0; }
    }
}
