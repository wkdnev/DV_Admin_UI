using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;

using DV.Shared.Security;

namespace DV.Admin.UI.Security;

/// <summary>
/// Custom policy provider that creates role-based authorization policies dynamically
/// based on the roles specified in the RoleBasedAuthorization attribute
/// </summary>
public class RoleBasedAuthorizationPolicyProvider : IAuthorizationPolicyProvider
{
    private const string POLICY_PREFIX = "RoleBasedAccess:";
    private readonly DefaultAuthorizationPolicyProvider _fallbackPolicyProvider;
    private readonly ILogger<RoleBasedAuthorizationPolicyProvider> _logger;

    public RoleBasedAuthorizationPolicyProvider(
        IOptions<AuthorizationOptions> options,
        ILogger<RoleBasedAuthorizationPolicyProvider> logger)
    {
        _fallbackPolicyProvider = new DefaultAuthorizationPolicyProvider(options);
        _logger = logger;
    }

    public Task<AuthorizationPolicy> GetDefaultPolicyAsync()
    {
        return _fallbackPolicyProvider.GetDefaultPolicyAsync();
    }

    public Task<AuthorizationPolicy?> GetFallbackPolicyAsync()
    {
        return _fallbackPolicyProvider.GetFallbackPolicyAsync();
    }

    public Task<AuthorizationPolicy?> GetPolicyAsync(string policyName)
    {
        _logger.LogInformation("PolicyProvider: GetPolicyAsync called with policy name: '{PolicyName}'", policyName);
        
        // Check if this is a role-based policy
        if (policyName.StartsWith(POLICY_PREFIX))
        {
            // Extract the roles from the policy name
            var rolesString = policyName.Substring(POLICY_PREFIX.Length);
            var roles = rolesString.Split(',', StringSplitOptions.RemoveEmptyEntries);

            _logger.LogInformation("PolicyProvider: Creating role-based policy for roles: {Roles}", string.Join(", ", roles));

            // Create a policy with the role-based authorization requirement
            var policy = new AuthorizationPolicyBuilder();
            policy.RequireAuthenticatedUser();
            policy.AddRequirements(new RoleBasedAuthorizationRequirement(roles));

            return Task.FromResult<AuthorizationPolicy?>(policy.Build());
        }

        _logger.LogInformation("PolicyProvider: Not a role-based policy, using fallback for: '{PolicyName}'", policyName);
        
        // Fall back to the default policy provider for other policies
        return _fallbackPolicyProvider.GetPolicyAsync(policyName);
    }
}
