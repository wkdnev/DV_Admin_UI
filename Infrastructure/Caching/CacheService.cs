using Microsoft.Extensions.Caching.Memory;
using System.Collections.Concurrent;

namespace DV.Admin.UI.Infrastructure.Caching;

/// <summary>
/// Interface for caching service with async operations and pattern-based invalidation
/// </summary>
public interface ICacheService
{
    Task<T?> GetAsync<T>(string key);
    Task SetAsync<T>(string key, T value, TimeSpan? expiration = null);
    Task RemoveAsync(string key);
    Task RemoveByPatternAsync(string pattern);
    Task<T> GetOrSetAsync<T>(string key, Func<Task<T>> factory, TimeSpan? expiration = null);
    Task ClearAllAsync();
    Task<bool> ExistsAsync(string key);
    Task<CacheStatistics> GetStatisticsAsync();
}

/// <summary>
/// Cache statistics for monitoring and performance analysis
/// </summary>
public class CacheStatistics
{
    public int TotalKeys { get; set; }
    public long HitCount { get; set; }
    public long MissCount { get; set; }
    public double HitRatio => HitCount + MissCount > 0 ? (double)HitCount / (HitCount + MissCount) * 100 : 0;
    public DateTime LastUpdated { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Enhanced memory cache service with statistics and pattern-based operations
/// </summary>
public class MemoryCacheService : ICacheService
{
    private readonly IMemoryCache _cache;
    private readonly ConcurrentDictionary<string, bool> _cacheKeys = new();
    private readonly ILogger<MemoryCacheService> _logger;
    private long _hitCount = 0;
    private long _missCount = 0;

    public MemoryCacheService(IMemoryCache cache, ILogger<MemoryCacheService> logger)
    {
        _cache = cache;
        _logger = logger;
    }

    public Task<T?> GetAsync<T>(string key)
    {
        try
        {
            if (_cache.TryGetValue(key, out var value) && value is T typedValue)
            {
                Interlocked.Increment(ref _hitCount);
                _logger.LogDebug("Cache hit for key: {Key}", key);
                return Task.FromResult<T?>(typedValue);
            }

            Interlocked.Increment(ref _missCount);
            _logger.LogDebug("Cache miss for key: {Key}", key);
            return Task.FromResult<T?>(default);
        }
        catch (Exception ex)
        {
            Interlocked.Increment(ref _missCount);
            _logger.LogError(ex, "Error retrieving from cache for key: {Key}", key);
            return Task.FromResult<T?>(default);
        }
    }

    public Task SetAsync<T>(string key, T value, TimeSpan? expiration = null)
    {
        try
        {
            var options = new MemoryCacheEntryOptions();
            
            if (expiration.HasValue)
            {
                options.AbsoluteExpirationRelativeToNow = expiration;
            }
            else
            {
                options.SlidingExpiration = TimeSpan.FromMinutes(30);
            }

            options.RegisterPostEvictionCallback((evictedKey, evictedValue, reason, state) =>
            {
                _cacheKeys.TryRemove(evictedKey.ToString() ?? "", out _);
            });

            _cache.Set(key, value, options);
            _cacheKeys.TryAdd(key, true);
            
            _logger.LogDebug("Cached value for key: {Key}", key);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error setting cache for key: {Key}", key);
        }

        return Task.CompletedTask;
    }

    public Task RemoveAsync(string key)
    {
        try
        {
            _cache.Remove(key);
            _cacheKeys.TryRemove(key, out _);
            _logger.LogDebug("Removed cache entry for key: {Key}", key);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error removing cache for key: {Key}", key);
        }

        return Task.CompletedTask;
    }

    public Task RemoveByPatternAsync(string pattern)
    {
        try
        {
            var keysToRemove = _cacheKeys.Keys
                .Where(key => key.Contains(pattern, StringComparison.OrdinalIgnoreCase))
                .ToList();

            _logger.LogWarning("Cache RemoveByPatternAsync: Found {Count} keys matching pattern '{Pattern}'", keysToRemove.Count, pattern);

            foreach (var key in keysToRemove)
            {
                _cache.Remove(key);
                _cacheKeys.TryRemove(key, out _);
                _logger.LogWarning("Cache: Removed key '{Key}'", key);
            }

            _logger.LogWarning("Cache: Removed {Count} cache entries matching pattern: {Pattern}", keysToRemove.Count, pattern);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error removing cache entries by pattern: {Pattern}", pattern);
        }

        return Task.CompletedTask;
    }

    public async Task<T> GetOrSetAsync<T>(string key, Func<Task<T>> factory, TimeSpan? expiration = null)
    {
        // Check if key exists in cache first (important for value types like bool where default != null)
        if (_cache.TryGetValue<T>(key, out var cached))
        {
            Interlocked.Increment(ref _hitCount);
            _logger.LogDebug("Cache hit for key: {Key}", key);
            return cached!;
        }

        Interlocked.Increment(ref _missCount);
        _logger.LogDebug("Cache miss for key: {Key}, executing factory", key);
        
        var value = await factory();
        await SetAsync(key, value, expiration);
        return value;
    }

    public Task ClearAllAsync()
    {
        try
        {
            var keysToRemove = _cacheKeys.Keys.ToList();
            foreach (var key in keysToRemove)
            {
                _cache.Remove(key);
                _cacheKeys.TryRemove(key, out _);
            }

            _logger.LogInformation("Cleared all cache entries. Removed {Count} entries", keysToRemove.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error clearing all cache entries");
        }

        return Task.CompletedTask;
    }

    public Task<bool> ExistsAsync(string key)
    {
        try
        {
            return Task.FromResult(_cache.TryGetValue(key, out _));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking cache existence for key: {Key}", key);
            return Task.FromResult(false);
        }
    }

    public Task<CacheStatistics> GetStatisticsAsync()
    {
        return Task.FromResult(new CacheStatistics
        {
            TotalKeys = _cacheKeys.Count,
            HitCount = _hitCount,
            MissCount = _missCount
        });
    }
}

public static class CacheKeys
{
    public static string UserRoles(int userId) => $"user_roles_{userId}";
    public static string UserPermissions(int userId) => $"user_permissions_{userId}";
    public static string ProjectRoles(int projectId) => $"project_roles_{projectId}";
    public static string UserProjects(int userId) => $"user_projects_{userId}";
    public static string AllProjects() => "all_projects";
    public static string AllPermissions() => "all_permissions";
}
