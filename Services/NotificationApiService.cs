// ============================================================================
// NotificationApiService.cs - Notification API Client for Admin UI
// ============================================================================
//
// All notification data access goes through the API (DV_API).
// No direct database access from UI applications.
// ============================================================================

using DV.Shared.DTOs;
using Microsoft.AspNetCore.Components.Authorization;
using System.Net.Http.Json;

namespace DV.Admin.UI.Services;

public class NotificationApiService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<NotificationApiService> _logger;
    private readonly AuthenticationStateProvider _authStateProvider;

    public NotificationApiService(
        IHttpClientFactory httpClientFactory,
        ILogger<NotificationApiService> logger,
        AuthenticationStateProvider authStateProvider)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
        _authStateProvider = authStateProvider;
    }

    /// <summary>
    /// Creates an HttpRequestMessage with X-Forwarded-User header set from the current auth state.
    /// This ensures the API resolves the correct user even when HttpContext is null (Blazor circuits).
    /// </summary>
    private async Task<HttpRequestMessage> CreateRequestAsync(HttpMethod method, string url)
    {
        var request = new HttpRequestMessage(method, url);
        try
        {
            var authState = await _authStateProvider.GetAuthenticationStateAsync();
            var username = authState.User?.Identity?.Name;
            if (!string.IsNullOrEmpty(username))
            {
                _logger.LogDebug("NotificationApiService: Setting X-Forwarded-User to {Username}", username);
                request.Headers.Add("X-Forwarded-User", username);
            }
            else
            {
                _logger.LogWarning("NotificationApiService: AuthState resolved but Identity.Name is null/empty. IsAuthenticated={IsAuth}",
                    authState.User?.Identity?.IsAuthenticated);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not resolve auth state for X-Forwarded-User header");
        }
        return request;
    }

    public async Task<UnreadCountDto> GetUnreadCountAsync(string? username = null)
    {
        try
        {
            var client = _httpClientFactory.CreateClient("Api");
            var request = await CreateRequestAsync(HttpMethod.Get, "api/notifications/unread-count");
            if (!string.IsNullOrEmpty(username))
            {
                request.Headers.Remove("X-Forwarded-User");
                request.Headers.Add("X-Forwarded-User", username);
            }
            var response = await client.SendAsync(request);

            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadFromJsonAsync<UnreadCountDto>();
                return result ?? new UnreadCountDto();
            }

            _logger.LogWarning("Failed to get unread count: {StatusCode}", response.StatusCode);
            return new UnreadCountDto();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching unread notification count");
            return new UnreadCountDto();
        }
    }

    public async Task<NotificationListResponseDto> GetNotificationsAsync(
        int page = 1,
        int pageSize = 20,
        string? category = null,
        bool? isRead = null,
        bool? isImportant = null,
        string sortBy = "CreatedAtUtc",
        bool sortDescending = true)
    {
        try
        {
            var client = _httpClientFactory.CreateClient("Api");
            var queryParams = new List<string>
            {
                $"page={page}",
                $"pageSize={pageSize}",
                $"sortBy={sortBy}",
                $"sortDescending={sortDescending}"
            };

            if (!string.IsNullOrEmpty(category)) queryParams.Add($"category={Uri.EscapeDataString(category)}");
            if (isRead.HasValue) queryParams.Add($"isRead={isRead.Value}");
            if (isImportant.HasValue) queryParams.Add($"isImportant={isImportant.Value}");

            var url = $"api/notifications?{string.Join("&", queryParams)}";
            var request = await CreateRequestAsync(HttpMethod.Get, url);
            var response = await client.SendAsync(request);

            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadFromJsonAsync<NotificationListResponseDto>();
                return result ?? new NotificationListResponseDto();
            }

            _logger.LogWarning("Failed to get notifications: {StatusCode}", response.StatusCode);
            return new NotificationListResponseDto();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching notifications");
            return new NotificationListResponseDto();
        }
    }

    public async Task<bool> MarkAsReadAsync(int notificationId)
    {
        try
        {
            var client = _httpClientFactory.CreateClient("Api");
            var request = await CreateRequestAsync(HttpMethod.Put, $"api/notifications/{notificationId}/read");
            var response = await client.SendAsync(request);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error marking notification {Id} as read", notificationId);
            return false;
        }
    }

    public async Task<int> MarkAllAsReadAsync()
    {
        try
        {
            var client = _httpClientFactory.CreateClient("Api");
            var request = await CreateRequestAsync(HttpMethod.Put, "api/notifications/read-all");
            var response = await client.SendAsync(request);

            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadFromJsonAsync<MarkAllReadResponseDto>();
                return result?.MarkedCount ?? 0;
            }

            return 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error marking all notifications as read");
            return 0;
        }
    }

    public async Task<bool> DeleteNotificationAsync(int notificationId)
    {
        try
        {
            var client = _httpClientFactory.CreateClient("Api");
            var request = await CreateRequestAsync(HttpMethod.Delete, $"api/notifications/{notificationId}");
            var response = await client.SendAsync(request);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting notification {Id}", notificationId);
            return false;
        }
    }

    public async Task<int> BulkDeleteAsync(List<int> notificationIds)
    {
        try
        {
            var client = _httpClientFactory.CreateClient("Api");
            var request = await CreateRequestAsync(HttpMethod.Delete, "api/notifications/bulk");
            request.Content = JsonContent.Create(new BulkNotificationActionDto { NotificationIds = notificationIds });
            var response = await client.SendAsync(request);

            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadFromJsonAsync<BulkDeleteResponseDto>();
                return result?.DeletedCount ?? 0;
            }

            return 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error bulk deleting notifications");
            return 0;
        }
    }

    public async Task<bool> CreateNotificationAsync(CreateNotificationDto dto)
    {
        try
        {
            var client = _httpClientFactory.CreateClient("Api");
            var request = await CreateRequestAsync(HttpMethod.Post, "api/notifications");
            request.Content = JsonContent.Create(dto);
            var response = await client.SendAsync(request);
            if (!response.IsSuccessStatusCode)
                _logger.LogWarning("Failed to create notification: {StatusCode}", response.StatusCode);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating notification for user {UserId}", dto.UserId);
            return false;
        }
    }
}

