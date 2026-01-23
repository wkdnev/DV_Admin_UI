// ============================================================================
// UserRoleEventHandlers.cs - Event Handlers for User Role Change Events
// ============================================================================
//
// Purpose: Implements event handlers for user role and permission change
// events, providing security monitoring, audit logging, and compliance
// tracking for administrative actions.
//
// Features:
// - Security audit logging for role changes
// - Administrative action tracking
// - Global admin promotion/demotion monitoring
// - User account lifecycle tracking
//
// Usage:
// - Automatically triggered by DomainEventDispatcher
// - Registered in DI container for automatic discovery
// - Handles all user role and admin status change events
//
// ============================================================================

using DV.Shared.Domain.Events;
using DV.Admin.UI.Services;
using Microsoft.Extensions.Logging;

namespace DV.Admin.UI.Infrastructure.Events.Handlers;

/// <summary>
/// Handles user project role change events for audit logging
/// </summary>
public class UserRoleAuditHandler : IDomainEventHandler<UserProjectRoleChangedEvent>
{
    private readonly AuditService _auditService;
    private readonly ILogger<UserRoleAuditHandler> _logger;

    public UserRoleAuditHandler(
        AuditService auditService,
        ILogger<UserRoleAuditHandler> logger)
    {
        _auditService = auditService;
        _logger = logger;
    }

    public async Task HandleAsync(UserProjectRoleChangedEvent domainEvent, CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("User role change: {RoleChange}", domainEvent.ToString());

            await _auditService.LogRoleManagementAsync(
                domainEvent.ChangedByUsername,
                null, // adminUserId - not available in domain event
                "ChangeUserRole",
                $"{domainEvent.PreviousRole} -> {domainEvent.NewRole}",
                null, // targetUserId - not available
                null, // projectId - not available  
                $"User {domainEvent.Username} role changed in project {domainEvent.ProjectSchema}: {domainEvent.ChangeReason}");

            // Log security-relevant information
            _logger.LogInformation("Role change audit logged: User={Username}, " +
                "Project={ProjectSchema}, Change={ChangeType}, " +
                "From={PreviousRole}, To={NewRole}, By={ChangedBy}",
                domainEvent.Username,
                domainEvent.ProjectSchema,
                domainEvent.ChangeType,
                domainEvent.PreviousRole ?? "None",
                domainEvent.NewRole ?? "None",
                domainEvent.ChangedByUsername);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to log audit for user role change: {Username} in {ProjectSchema}", 
                domainEvent.Username, domainEvent.ProjectSchema);
        }
    }
}

/// <summary>
/// Handles global admin status change events for security monitoring
/// </summary>
public class GlobalAdminSecurityHandler : IDomainEventHandler<UserGlobalAdminStatusChangedEvent>
{
    private readonly AuditService _auditService;
    private readonly ILogger<GlobalAdminSecurityHandler> _logger;

    public GlobalAdminSecurityHandler(
        AuditService auditService,
        ILogger<GlobalAdminSecurityHandler> logger)
    {
        _auditService = auditService;
        _logger = logger;
    }

    public async Task HandleAsync(UserGlobalAdminStatusChangedEvent domainEvent, CancellationToken cancellationToken = default)
    {
        try
        {
            // Global admin changes are high-security events
            _logger.LogWarning("Global admin status change: {AdminChange}", domainEvent.ToString());

            await _auditService.LogUserManagementAsync(
                domainEvent.ChangedByUsername,
                null, // adminUserId - not available in domain event
                domainEvent.ChangeType == AdminChangeType.Promoted ? "PromoteToGlobalAdmin" : "DemoteFromGlobalAdmin",
                domainEvent.Username,
                null, // targetUserId - not available
                $"Global admin status change: {domainEvent.ChangeReason}");

            // Additional security logging for admin promotions/demotions
            if (domainEvent.ChangeType == AdminChangeType.Promoted)
            {
                _logger.LogWarning("SECURITY: User {Username} promoted to Global Admin by {AdminUser} - Reason: {Reason}",
                    domainEvent.Username,
                    domainEvent.ChangedByUsername,
                    domainEvent.ChangeReason);
            }
            else if (domainEvent.ChangeType == AdminChangeType.Demoted)
            {
                _logger.LogWarning("SECURITY: User {Username} removed from Global Admin by {AdminUser} - Reason: {Reason}",
                    domainEvent.Username,
                    domainEvent.ChangedByUsername,
                    domainEvent.ChangeReason);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to log audit for global admin status change: {Username}", 
                domainEvent.Username);
        }
    }
}

/// <summary>
/// Handles user account creation events
/// </summary>
public class UserAccountCreationHandler : IDomainEventHandler<UserAccountCreatedEvent>
{
    private readonly AuditService _auditService;
    private readonly ILogger<UserAccountCreationHandler> _logger;

    public UserAccountCreationHandler(
        AuditService auditService,
        ILogger<UserAccountCreationHandler> logger)
    {
        _auditService = auditService;
        _logger = logger;
    }

    public async Task HandleAsync(UserAccountCreatedEvent domainEvent, CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("User account created: {AccountCreation}", domainEvent.ToString());

            await _auditService.LogUserManagementAsync(
                domainEvent.CreatedByUsername,
                null, // adminUserId - not available in domain event
                "CreateUser",
                domainEvent.Username,
                null, // targetUserId - not available
                $"Account created - Display Name: {domainEvent.DisplayName}, Global Admin: {domainEvent.IsGlobalAdmin}");

            // Special logging for admin account creation
            if (domainEvent.IsGlobalAdmin)
            {
                _logger.LogWarning("SECURITY: Global Admin account created - Username: {Username}, " +
                    "Created by: {CreatedBy}",
                    domainEvent.Username,
                    domainEvent.CreatedByUsername);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to log audit for user account creation: {Username}", 
                domainEvent.Username);
        }
    }
}

/// <summary>
/// Handles role change events for security compliance monitoring
/// </summary>
public class RoleChangeComplianceHandler : IDomainEventHandler<UserProjectRoleChangedEvent>
{
    private readonly ILogger<RoleChangeComplianceHandler> _logger;

    public RoleChangeComplianceHandler(ILogger<RoleChangeComplianceHandler> logger)
    {
        _logger = logger;
    }

    public async Task HandleAsync(UserProjectRoleChangedEvent domainEvent, CancellationToken cancellationToken = default)
    {
        try
        {
            // Monitor for compliance-relevant role changes
            var sensitiveRoles = new[] { "DataOwner", "Security", "Auditor" };
            
            bool isPreviousRoleSensitive = !string.IsNullOrEmpty(domainEvent.PreviousRole) && 
                sensitiveRoles.Contains(domainEvent.PreviousRole);
            bool isNewRoleSensitive = !string.IsNullOrEmpty(domainEvent.NewRole) && 
                sensitiveRoles.Contains(domainEvent.NewRole);

            if (isPreviousRoleSensitive || isNewRoleSensitive)
            {
                _logger.LogWarning("COMPLIANCE: Sensitive role change detected - " +
                    "User: {Username}, Project: {ProjectSchema}, " +
                    "From: {PreviousRole}, To: {NewRole}, " +
                    "Changed by: {ChangedBy}, Reason: {Reason}",
                    domainEvent.Username,
                    domainEvent.ProjectSchema,
                    domainEvent.PreviousRole ?? "None",
                    domainEvent.NewRole ?? "None",
                    domainEvent.ChangedByUsername,
                    domainEvent.ChangeReason);
            }

            // Future compliance features could include:
            // - Integration with compliance management systems
            // - Automated notifications to compliance officers
            // - Segregation of duties validation
            // - Role assignment approval workflows

            await Task.CompletedTask;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to process compliance monitoring for role change: {Username}", 
                domainEvent.Username);
        }
    }
}
