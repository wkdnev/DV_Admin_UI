using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using DV.Admin.UI.Services;

using DV.Shared.Security;

namespace DV.Admin.UI.Security;

/// <summary>
/// Authorization handler that verifies if the current user is a global administrator
/// </summary>
public class GlobalAdminAuthorizationHandler : AuthorizationHandler<GlobalAdminRequirement>
{
    private readonly UserService _userService;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public GlobalAdminAuthorizationHandler(
        UserService userService,
        IHttpContextAccessor httpContextAccessor)
    {
        _userService = userService;
        _httpContextAccessor = httpContextAccessor;
    }

    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context, 
        GlobalAdminRequirement requirement)
    {
        // Get the username from the HttpContext
        var username = _httpContextAccessor.HttpContext?.User?.Identity?.Name;
        
        if (string.IsNullOrEmpty(username))
        {
            return; // Not authenticated
        }

        // Check if the user is a global admin
        var isGlobalAdmin = await _userService.IsGlobalAdminAsync(username);
        
        if (isGlobalAdmin)
        {
            context.Succeed(requirement);
        }
    }
}
