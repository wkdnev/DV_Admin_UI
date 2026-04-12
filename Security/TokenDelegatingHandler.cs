using System.Net.Http.Headers;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;

using DV.Shared.Security;

namespace DV.Admin.UI.Security;

public class TokenDelegatingHandler : DelegatingHandler
{
    private readonly TokenProvider _tokenProvider;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public TokenDelegatingHandler(TokenProvider tokenProvider, IHttpContextAccessor httpContextAccessor)
    {
        _tokenProvider = tokenProvider;
        _httpContextAccessor = httpContextAccessor;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var accessToken = _tokenProvider.AccessToken;
        if (!string.IsNullOrEmpty(accessToken))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        }

        // Forward the actual authenticated user's identity so the API knows
        // which user is making the request (essential for dev mode and local auth).
        // Skip if the caller (e.g. NotificationApiService) already set the header.
        if (!request.Headers.Contains("X-Forwarded-User"))
        {
            var username = _httpContextAccessor.HttpContext?.User?.Identity?.Name;
            if (!string.IsNullOrEmpty(username))
            {
                request.Headers.Add("X-Forwarded-User", username);
            }
        }

        return await base.SendAsync(request, cancellationToken);
    }
}
