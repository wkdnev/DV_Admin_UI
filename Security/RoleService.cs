using DV.Admin.UI.Data;
using DV.Admin.UI.Infrastructure.Caching;
using DV.Shared.Security;
using DV.Shared.Security;
using DV.Admin.UI.Infrastructure.Caching;
using Microsoft.EntityFrameworkCore;

namespace DV.Admin.UI.Services;

public class RoleService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ICacheService _cache;

    public RoleService(IServiceProvider serviceProvider, ICacheService cache)
    {
        _serviceProvider = serviceProvider;
        _cache = cache;
    }

    public async Task<List<ApplicationRole>> GetAllRolesAsync()
    {
        return await _cache.GetOrSetAsync("roles:all", async () =>
        {
            using var scope = _serviceProvider.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<SecurityDbContext>();
            return await context.Roles.ToListAsync();
        }, TimeSpan.FromHours(1));
    }

    public async Task<ApplicationRole?> GetRoleByIdAsync(int roleId)
    {
        return await _cache.GetOrSetAsync($"role:id:{roleId}", async () =>
        {
            using var scope = _serviceProvider.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<SecurityDbContext>();
            return await context.Roles.FindAsync(roleId);
        }, TimeSpan.FromHours(1));
    }

    public async Task<ApplicationRole> CreateRoleAsync(string name, string description)
    {
        using var scope = _serviceProvider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<SecurityDbContext>();
        
        var role = new ApplicationRole
        {
            Name = name,
            Description = description
        };

        context.Roles.Add(role);
        await context.SaveChangesAsync();
        
        // Invalidate roles cache
        await _cache.RemoveByPatternAsync("roles:");
        
        return role;
    }

    public async Task UpdateRoleAsync(ApplicationRole role)
    {
        using var scope = _serviceProvider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<SecurityDbContext>();
        
        context.Roles.Update(role);
        await context.SaveChangesAsync();
        
        // Invalidate roles cache
        await _cache.RemoveByPatternAsync("roles:");
        await _cache.RemoveAsync($"role:id:{role.RoleId}");
    }

    public async Task DeleteRoleAsync(int roleId)
    {
        using var scope = _serviceProvider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<SecurityDbContext>();
        
        var role = await context.Roles.FindAsync(roleId);
        if (role != null)
        {
            context.Roles.Remove(role);
            await context.SaveChangesAsync();
            
            // Invalidate roles cache
            await _cache.RemoveByPatternAsync("roles:");
            await _cache.RemoveAsync($"role:id:{roleId}");
        }
    }

    // DEPRECATED: Global role permissions have been replaced with project-scoped permissions
    // Use ProjectRolePermission and ProjectRoleService instead
    /*
    public async Task<List<Permission>> GetRolePermissionsAsync(int roleId)
    {
        using var scope = _serviceProvider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<SecurityDbContext>();
        
        return await (
            from rp in context.RolePermissions
            join p in context.Permissions on rp.PermissionId equals p.PermissionId
            where rp.RoleId == roleId
            select p
        ).ToListAsync();
    }

    public async Task AssignPermissionToRoleAsync(int roleId, int permissionId)
    {
        using var scope = _serviceProvider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<SecurityDbContext>();
        
        var rolePermission = new RolePermission { RoleId = roleId, PermissionId = permissionId };
        context.RolePermissions.Add(rolePermission);
        await context.SaveChangesAsync();
    }

    public async Task RemovePermissionFromRoleAsync(int roleId, int permissionId)
    {
        using var scope = _serviceProvider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<SecurityDbContext>();
        
        var rolePermission = await context.RolePermissions
            .FirstOrDefaultAsync(rp => rp.RoleId == roleId && rp.PermissionId == permissionId);

        if (rolePermission != null)
        {
            context.RolePermissions.Remove(rolePermission);
            await context.SaveChangesAsync();
        }
    }
    */
}
