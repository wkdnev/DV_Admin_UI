using DV.Admin.UI.Data;
using DV.Shared.DTOs;
using DV.Shared.Interfaces;
using DV.Shared.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace DV.Admin.UI.Services;

public class AccessGroupService : IAccessGroupService
{
    private readonly SecurityDbContext _context;
    private readonly ILogger<AccessGroupService> _logger;
    private readonly NotificationApiService _notificationService;

    public AccessGroupService(SecurityDbContext context, ILogger<AccessGroupService> logger, NotificationApiService notificationService)
    {
        _context = context;
        _logger = logger;
        _notificationService = notificationService;
    }

    // ── Group CRUD ──

    public async Task<List<AccessGroup>> GetAllGroupsAsync()
    {
        return await _context.AccessGroups
            .Include(g => g.Members)
            .OrderBy(g => g.GroupName)
            .ToListAsync();
    }

    public async Task<AccessGroup?> GetGroupByIdAsync(int groupId)
    {
        return await _context.AccessGroups
            .Include(g => g.Members)
                .ThenInclude(m => m.User)
            .FirstOrDefaultAsync(g => g.AccessGroupId == groupId);
    }

    public async Task<AccessGroup?> GetGroupByNameAsync(string groupName)
    {
        return await _context.AccessGroups
            .FirstOrDefaultAsync(g => g.GroupName == groupName);
    }

    public async Task<AccessGroup> CreateGroupAsync(string groupName, string? description, string createdBy)
    {
        var existing = await _context.AccessGroups
            .AnyAsync(g => g.GroupName == groupName);
        if (existing)
            throw new InvalidOperationException($"A group named '{groupName}' already exists.");

        var group = new AccessGroup
        {
            GroupName = groupName,
            Description = description,
            CreatedBy = createdBy,
            CreatedDate = DateTime.UtcNow,
            IsActive = true
        };

        _context.AccessGroups.Add(group);
        await _context.SaveChangesAsync();

        _logger.LogInformation("Access group '{GroupName}' created by {CreatedBy}", groupName, createdBy);
        return group;
    }

    public async Task UpdateGroupAsync(int groupId, string groupName, string? description)
    {
        var duplicate = await _context.AccessGroups
            .AnyAsync(g => g.GroupName == groupName && g.AccessGroupId != groupId);
        if (duplicate)
            throw new InvalidOperationException($"A group named '{groupName}' already exists.");

        await _context.AccessGroups
            .Where(g => g.AccessGroupId == groupId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(g => g.GroupName, groupName)
                .SetProperty(g => g.Description, description));

        _logger.LogInformation("Access group {GroupId} updated to '{GroupName}'", groupId, groupName);
    }

    public async Task DeleteGroupAsync(int groupId)
    {
        var group = await _context.AccessGroups.FindAsync(groupId);
        if (group == null) return;

        _context.AccessGroups.Remove(group);
        await _context.SaveChangesAsync();

        _logger.LogInformation("Access group '{GroupName}' (ID {GroupId}) deleted", group.GroupName, groupId);
    }

    public async Task SetGroupActiveAsync(int groupId, bool isActive)
    {
        await _context.AccessGroups
            .Where(g => g.AccessGroupId == groupId)
            .ExecuteUpdateAsync(s => s.SetProperty(g => g.IsActive, isActive));
    }

    // ── Membership ──

    public async Task<List<AccessGroupMember>> GetGroupMembersAsync(int groupId)
    {
        return await _context.AccessGroupMembers
            .Include(m => m.User)
            .Where(m => m.AccessGroupId == groupId)
            .OrderBy(m => m.User!.DisplayName)
            .ToListAsync();
    }

    public async Task AddMemberAsync(int groupId, int userId, string addedBy)
    {
        var already = await _context.AccessGroupMembers
            .AnyAsync(m => m.AccessGroupId == groupId && m.UserId == userId);
        if (already) return;

        _context.AccessGroupMembers.Add(new AccessGroupMember
        {
            AccessGroupId = groupId,
            UserId = userId,
            AddedDate = DateTime.UtcNow,
            AddedBy = addedBy
        });
        await _context.SaveChangesAsync();

        _logger.LogInformation("User {UserId} added to group {GroupId} by {AddedBy}", userId, groupId, addedBy);

        // SI-5: ProjectAccess notification — user added to group
        try
        {
            var group = await _context.AccessGroups.FindAsync(groupId);
            await _notificationService.CreateNotificationAsync(new CreateNotificationDto
            {
                UserId = userId,
                Title = "Added to Access Group",
                Message = $"You have been added to the access group '{group?.GroupName ?? "Unknown"}'.",
                Category = NotificationCategories.ProjectAccess,
                SourceSystem = NotificationSources.Admin,
                CorrelationId = $"group-add-{groupId}-{userId}"
            });
        }
        catch (Exception notifEx)
        {
            _logger.LogWarning(notifEx, "Failed to create group membership notification for user {UserId}", userId);
        }
    }

    public async Task RemoveMemberAsync(int groupId, int userId)
    {
        await _context.AccessGroupMembers
            .Where(m => m.AccessGroupId == groupId && m.UserId == userId)
            .ExecuteDeleteAsync();

        _logger.LogInformation("User {UserId} removed from group {GroupId}", userId, groupId);

        // SI-5: ProjectAccess notification — user removed from group
        try
        {
            var group = await _context.AccessGroups.FindAsync(groupId);
            await _notificationService.CreateNotificationAsync(new CreateNotificationDto
            {
                UserId = userId,
                Title = "Removed from Access Group",
                Message = $"You have been removed from the access group '{group?.GroupName ?? "Unknown"}'.",
                Category = NotificationCategories.ProjectAccess,
                SourceSystem = NotificationSources.Admin,
                CorrelationId = $"group-remove-{groupId}-{userId}"
            });
        }
        catch (Exception notifEx)
        {
            _logger.LogWarning(notifEx, "Failed to create group removal notification for user {UserId}", userId);
        }
    }

    public async Task<bool> IsMemberAsync(int groupId, int userId)
    {
        return await _context.AccessGroupMembers
            .AnyAsync(m => m.AccessGroupId == groupId && m.UserId == userId);
    }

    // ── Access resolution ──

    public async Task<HashSet<string>> GetUserGroupNamesAsync(int userId)
    {
        var names = await _context.AccessGroupMembers
            .Where(m => m.UserId == userId && m.AccessGroup!.IsActive)
            .Select(m => m.AccessGroup!.GroupName)
            .ToListAsync();

        return new HashSet<string>(names, StringComparer.OrdinalIgnoreCase);
    }

    public async Task<bool> IsUserInGroupAsync(int userId, string groupName)
    {
        return await _context.AccessGroupMembers
            .AnyAsync(m => m.UserId == userId
                        && m.AccessGroup!.GroupName == groupName
                        && m.AccessGroup.IsActive);
    }
}
