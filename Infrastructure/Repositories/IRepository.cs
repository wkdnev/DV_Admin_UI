// ============================================================================
// IRepository.cs - Generic Repository Interface
// ============================================================================
//
// Purpose: Defines the contract for generic repository operations to provide
// a consistent data access layer abstraction across all entities.
//
// Features:
// - Generic CRUD operations
// - Async query capabilities
// - Specification pattern support
// - Bulk operations
// - Transaction support
//
// ============================================================================

using System.Linq.Expressions;

namespace DV.Admin.UI.Infrastructure.Repositories;

/// <summary>
/// Generic repository interface for data access operations
/// </summary>
/// <typeparam name="TEntity">The entity type</typeparam>
/// <typeparam name="TKey">The primary key type</typeparam>
public interface IRepository<TEntity, TKey> where TEntity : class
{
    // ========================================================================
    // Query Operations
    // ========================================================================
    
    /// <summary>
    /// Gets an entity by its primary key
    /// </summary>
    Task<TEntity?> GetByIdAsync(TKey id, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Gets all entities
    /// </summary>
    Task<IEnumerable<TEntity>> GetAllAsync(CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Finds entities matching the specified predicate
    /// </summary>
    Task<IEnumerable<TEntity>> FindAsync(
        Expression<Func<TEntity, bool>> predicate, 
        CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Gets the first entity matching the predicate or null
    /// </summary>
    Task<TEntity?> FirstOrDefaultAsync(
        Expression<Func<TEntity, bool>> predicate, 
        CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Checks if any entity matches the predicate
    /// </summary>
    Task<bool> AnyAsync(
        Expression<Func<TEntity, bool>> predicate, 
        CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Counts entities matching the predicate
    /// </summary>
    Task<int> CountAsync(
        Expression<Func<TEntity, bool>>? predicate = null, 
        CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Gets paged results
    /// </summary>
    Task<IEnumerable<TEntity>> GetPagedAsync(
        int pageNumber, 
        int pageSize, 
        Expression<Func<TEntity, bool>>? predicate = null,
        Expression<Func<TEntity, object>>? orderBy = null,
        bool ascending = true,
        CancellationToken cancellationToken = default);

    // ========================================================================
    // Command Operations
    // ========================================================================
    
    /// <summary>
    /// Adds a new entity
    /// </summary>
    Task<TEntity> AddAsync(TEntity entity, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Adds multiple entities
    /// </summary>
    Task AddRangeAsync(IEnumerable<TEntity> entities, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Updates an existing entity
    /// </summary>
    Task<TEntity> UpdateAsync(TEntity entity, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Updates multiple entities
    /// </summary>
    Task UpdateRangeAsync(IEnumerable<TEntity> entities, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Removes an entity by its primary key
    /// </summary>
    Task<bool> DeleteAsync(TKey id, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Removes an entity
    /// </summary>
    Task<bool> DeleteAsync(TEntity entity, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Removes multiple entities
    /// </summary>
    Task<int> DeleteRangeAsync(IEnumerable<TEntity> entities, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Removes entities matching the predicate
    /// </summary>
    Task<int> DeleteAsync(
        Expression<Func<TEntity, bool>> predicate, 
        CancellationToken cancellationToken = default);

    // ========================================================================
    // Transaction Operations
    // ========================================================================
    
    /// <summary>
    /// Saves all pending changes
    /// </summary>
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Begins a new transaction
    /// </summary>
    Task BeginTransactionAsync(CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Commits the current transaction
    /// </summary>
    Task CommitTransactionAsync(CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Rolls back the current transaction
    /// </summary>
    Task RollbackTransactionAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Simplified repository interface for entities with integer keys
/// </summary>
/// <typeparam name="TEntity">The entity type</typeparam>
public interface IRepository<TEntity> : IRepository<TEntity, int> where TEntity : class
{
}

/// <summary>
/// Interface for repository specification pattern
/// </summary>
/// <typeparam name="TEntity">The entity type</typeparam>
public interface ISpecification<TEntity> where TEntity : class
{
    /// <summary>
    /// Gets the criteria expression
    /// </summary>
    Expression<Func<TEntity, bool>>? Criteria { get; }
    
    /// <summary>
    /// Gets the include expressions
    /// </summary>
    List<Expression<Func<TEntity, object>>> Includes { get; }
    
    /// <summary>
    /// Gets the order by expression
    /// </summary>
    Expression<Func<TEntity, object>>? OrderBy { get; }
    
    /// <summary>
    /// Gets the order by descending expression
    /// </summary>
    Expression<Func<TEntity, object>>? OrderByDescending { get; }
    
    /// <summary>
    /// Gets the page number for paging (1-based)
    /// </summary>
    int? PageNumber { get; }
    
    /// <summary>
    /// Gets the page size for paging
    /// </summary>
    int? PageSize { get; }
    
    /// <summary>
    /// Whether paging is enabled
    /// </summary>
    bool IsPagingEnabled { get; }
}
