// ============================================================================
// RepositoryBase.cs - Generic Repository Base Implementation
// ============================================================================
//
// Purpose: Provides a base implementation of the generic repository pattern
// using Entity Framework Core for data access operations.
//
// Features:
// - Generic CRUD operations
// - Async query capabilities
// - Transaction management
// - Bulk operations
// - Specification pattern support
//
// ============================================================================

using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using DV.Admin.UI.Infrastructure.Repositories;

namespace DV.Admin.UI.Infrastructure.Repositories;

/// <summary>
/// Base repository implementation using Entity Framework Core
/// </summary>
/// <typeparam name="TEntity">The entity type</typeparam>
/// <typeparam name="TKey">The primary key type</typeparam>
/// <typeparam name="TContext">The DbContext type</typeparam>
public abstract class RepositoryBase<TEntity, TKey, TContext> : IRepository<TEntity, TKey>
    where TEntity : class
    where TContext : DbContext
{
    protected readonly TContext Context;
    protected readonly DbSet<TEntity> DbSet;
    protected readonly ILogger<RepositoryBase<TEntity, TKey, TContext>> Logger;
    private IDbContextTransaction? _currentTransaction;

    protected RepositoryBase(TContext context, ILogger<RepositoryBase<TEntity, TKey, TContext>> logger)
    {
        Context = context ?? throw new ArgumentNullException(nameof(context));
        DbSet = context.Set<TEntity>();
        Logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    // ========================================================================
    // Query Operations
    // ========================================================================

    public virtual async Task<TEntity?> GetByIdAsync(TKey id, CancellationToken cancellationToken = default)
    {
        try
        {
            var entity = await DbSet.FindAsync(new object[] { id! }, cancellationToken);
            Logger.LogDebug("Retrieved entity {EntityType} with ID {Id}: {Found}",
                typeof(TEntity).Name, id, entity != null ? "Found" : "Not Found");
            return entity;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error retrieving entity {EntityType} with ID {Id}", typeof(TEntity).Name, id);
            throw;
        }
    }

    public virtual async Task<IEnumerable<TEntity>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var entities = await DbSet.ToListAsync(cancellationToken);
            Logger.LogDebug("Retrieved {Count} entities of type {EntityType}", entities.Count, typeof(TEntity).Name);
            return entities;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error retrieving all entities of type {EntityType}", typeof(TEntity).Name);
            throw;
        }
    }

    public virtual async Task<IEnumerable<TEntity>> FindAsync(
        Expression<Func<TEntity, bool>> predicate,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var entities = await DbSet.Where(predicate).ToListAsync(cancellationToken);
            Logger.LogDebug("Found {Count} entities of type {EntityType} matching predicate",
                entities.Count, typeof(TEntity).Name);
            return entities;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error finding entities of type {EntityType}", typeof(TEntity).Name);
            throw;
        }
    }

    public virtual async Task<TEntity?> FirstOrDefaultAsync(
        Expression<Func<TEntity, bool>> predicate,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var entity = await DbSet.FirstOrDefaultAsync(predicate, cancellationToken);
            Logger.LogDebug("FirstOrDefault query for {EntityType}: {Found}",
                typeof(TEntity).Name, entity != null ? "Found" : "Not Found");
            return entity;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error in FirstOrDefault query for type {EntityType}", typeof(TEntity).Name);
            throw;
        }
    }

    public virtual async Task<bool> AnyAsync(
        Expression<Func<TEntity, bool>> predicate,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var exists = await DbSet.AnyAsync(predicate, cancellationToken);
            Logger.LogDebug("Any query for {EntityType}: {Exists}", typeof(TEntity).Name, exists);
            return exists;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error in Any query for type {EntityType}", typeof(TEntity).Name);
            throw;
        }
    }

    public virtual async Task<int> CountAsync(
        Expression<Func<TEntity, bool>>? predicate = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var count = predicate == null
                ? await DbSet.CountAsync(cancellationToken)
                : await DbSet.CountAsync(predicate, cancellationToken);
            Logger.LogDebug("Count query for {EntityType}: {Count}", typeof(TEntity).Name, count);
            return count;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error in Count query for type {EntityType}", typeof(TEntity).Name);
            throw;
        }
    }

    public virtual async Task<IEnumerable<TEntity>> GetPagedAsync(
        int pageNumber,
        int pageSize,
        Expression<Func<TEntity, bool>>? predicate = null,
        Expression<Func<TEntity, object>>? orderBy = null,
        bool ascending = true,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (pageNumber < 1) pageNumber = 1;
            if (pageSize < 1) pageSize = 10;

            var query = DbSet.AsQueryable();

            if (predicate != null)
                query = query.Where(predicate);

            if (orderBy != null)
                query = ascending ? query.OrderBy(orderBy) : query.OrderByDescending(orderBy);

            var entities = await query
                .Skip((pageNumber - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync(cancellationToken);

            Logger.LogDebug("Paged query for {EntityType}: Page {Page}, Size {Size}, Results {Count}",
                typeof(TEntity).Name, pageNumber, pageSize, entities.Count);

            return entities;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error in paged query for type {EntityType}", typeof(TEntity).Name);
            throw;
        }
    }

    // ========================================================================
    // Command Operations
    // ========================================================================

    public virtual async Task<TEntity> AddAsync(TEntity entity, CancellationToken cancellationToken = default)
    {
        try
        {
            var entry = await DbSet.AddAsync(entity, cancellationToken);
            Logger.LogDebug("Added entity {EntityType} to context", typeof(TEntity).Name);
            return entry.Entity;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error adding entity of type {EntityType}", typeof(TEntity).Name);
            throw;
        }
    }

    public virtual async Task AddRangeAsync(IEnumerable<TEntity> entities, CancellationToken cancellationToken = default)
    {
        try
        {
            await DbSet.AddRangeAsync(entities, cancellationToken);
            var count = entities.Count();
            Logger.LogDebug("Added {Count} entities of type {EntityType} to context", count, typeof(TEntity).Name);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error adding range of entities of type {EntityType}", typeof(TEntity).Name);
            throw;
        }
    }

    public virtual Task<TEntity> UpdateAsync(TEntity entity, CancellationToken cancellationToken = default)
    {
        try
        {
            var entry = DbSet.Update(entity);
            Logger.LogDebug("Updated entity {EntityType} in context", typeof(TEntity).Name);
            return Task.FromResult(entry.Entity);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error updating entity of type {EntityType}", typeof(TEntity).Name);
            throw;
        }
    }

    public virtual Task UpdateRangeAsync(IEnumerable<TEntity> entities, CancellationToken cancellationToken = default)
    {
        try
        {
            DbSet.UpdateRange(entities);
            var count = entities.Count();
            Logger.LogDebug("Updated {Count} entities of type {EntityType} in context", count, typeof(TEntity).Name);
            return Task.CompletedTask;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error updating range of entities of type {EntityType}", typeof(TEntity).Name);
            throw;
        }
    }

    public virtual async Task<bool> DeleteAsync(TKey id, CancellationToken cancellationToken = default)
    {
        try
        {
            var entity = await GetByIdAsync(id, cancellationToken);
            if (entity == null)
            {
                Logger.LogWarning("Entity {EntityType} with ID {Id} not found for deletion", typeof(TEntity).Name, id);
                return false;
            }

            DbSet.Remove(entity);
            Logger.LogDebug("Removed entity {EntityType} with ID {Id} from context", typeof(TEntity).Name, id);
            return true;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error deleting entity {EntityType} with ID {Id}", typeof(TEntity).Name, id);
            throw;
        }
    }

    public virtual Task<bool> DeleteAsync(TEntity entity, CancellationToken cancellationToken = default)
    {
        try
        {
            DbSet.Remove(entity);
            Logger.LogDebug("Removed entity {EntityType} from context", typeof(TEntity).Name);
            return Task.FromResult(true);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error deleting entity of type {EntityType}", typeof(TEntity).Name);
            throw;
        }
    }

    public virtual Task<int> DeleteRangeAsync(IEnumerable<TEntity> entities, CancellationToken cancellationToken = default)
    {
        try
        {
            var entitiesList = entities.ToList();
            DbSet.RemoveRange(entitiesList);
            var count = entitiesList.Count;
            Logger.LogDebug("Removed {Count} entities of type {EntityType} from context", count, typeof(TEntity).Name);
            return Task.FromResult(count);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error deleting range of entities of type {EntityType}", typeof(TEntity).Name);
            throw;
        }
    }

    public virtual async Task<int> DeleteAsync(
        Expression<Func<TEntity, bool>> predicate,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var entities = await DbSet.Where(predicate).ToListAsync(cancellationToken);
            DbSet.RemoveRange(entities);
            Logger.LogDebug("Removed {Count} entities of type {EntityType} matching predicate", entities.Count, typeof(TEntity).Name);
            return entities.Count;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error deleting entities of type {EntityType} by predicate", typeof(TEntity).Name);
            throw;
        }
    }

    // ========================================================================
    // Transaction Operations
    // ========================================================================

    public virtual async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var changes = await Context.SaveChangesAsync(cancellationToken);
            Logger.LogDebug("Saved {Changes} changes to database", changes);
            return changes;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error saving changes to database");
            throw;
        }
    }

    public virtual async Task BeginTransactionAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            if (_currentTransaction != null)
            {
                Logger.LogWarning("Transaction already in progress");
                return;
            }

            _currentTransaction = await Context.Database.BeginTransactionAsync(cancellationToken);
            Logger.LogDebug("Transaction started");
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error starting transaction");
            throw;
        }
    }

    public virtual async Task CommitTransactionAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            if (_currentTransaction == null)
            {
                Logger.LogWarning("No transaction to commit");
                return;
            }

            await _currentTransaction.CommitAsync(cancellationToken);
            Logger.LogDebug("Transaction committed");
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error committing transaction");
            await RollbackTransactionAsync(cancellationToken);
            throw;
        }
        finally
        {
            if (_currentTransaction != null)
            {
                await _currentTransaction.DisposeAsync();
                _currentTransaction = null;
            }
        }
    }

    public virtual async Task RollbackTransactionAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            if (_currentTransaction == null)
            {
                Logger.LogWarning("No transaction to rollback");
                return;
            }

            await _currentTransaction.RollbackAsync(cancellationToken);
            Logger.LogDebug("Transaction rolled back");
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error rolling back transaction");
            throw;
        }
        finally
        {
            if (_currentTransaction != null)
            {
                await _currentTransaction.DisposeAsync();
                _currentTransaction = null;
            }
        }
    }

    // ========================================================================
    // Dispose Pattern
    // ========================================================================

    protected virtual void Dispose(bool disposing)
    {
        if (disposing)
        {
            _currentTransaction?.Dispose();
        }
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }
}
