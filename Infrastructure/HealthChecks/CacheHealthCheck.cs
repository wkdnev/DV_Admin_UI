// ============================================================================
// CacheHealthCheck.cs - Caching System Health Check
// ============================================================================
//
// Purpose: Monitors caching system health including memory cache operations,
// cache hit rates, and performance metrics for the application's caching
// infrastructure.
//
// Features:
// - Cache connectivity and operation testing
// - Cache performance monitoring
// - Memory usage tracking
// - Cache hit/miss ratio analysis
//
// ============================================================================

using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Caching.Memory;
using System.Reflection;

namespace DV.Admin.UI.Infrastructure.HealthChecks;

/// <summary>
/// Health check for caching system operations and performance
/// </summary>
public class CacheHealthCheck : IHealthCheck
{
    private readonly IMemoryCache _memoryCache;
    private readonly ILogger<CacheHealthCheck> _logger;
    private static readonly string TestCacheKey = "HealthCheck_TestKey";

    public CacheHealthCheck(
        IMemoryCache memoryCache,
        ILogger<CacheHealthCheck> logger)
    {
        _memoryCache = memoryCache;
        _logger = logger;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, 
        CancellationToken cancellationToken = default)
    {
        try
        {
            var data = new Dictionary<string, object>();
            var healthyComponents = new List<string>();
            var issues = new List<string>();

            // Test basic cache operations
            var cacheResult = await CheckCacheOperationsAsync(cancellationToken);
            data.Add("CacheOperations", cacheResult);
            
            if (cacheResult.IsHealthy)
                healthyComponents.Add("CacheOperations");
            else
                issues.Add($"CacheOperations: {cacheResult.ErrorMessage}");

            // Get cache statistics
            var statsResult = GetCacheStatistics();
            data.Add("CacheStatistics", statsResult);

            // Test cache performance
            var performanceResult = await CheckCachePerformanceAsync(cancellationToken);
            data.Add("CachePerformance", performanceResult);
            
            if (performanceResult.IsHealthy)
                healthyComponents.Add("CachePerformance");
            else
                issues.Add($"CachePerformance: {performanceResult.ErrorMessage}");

            // Overall health determination
            var isHealthy = cacheResult.IsHealthy && performanceResult.IsHealthy;
            var status = isHealthy ? HealthStatus.Healthy : HealthStatus.Unhealthy;

            var description = isHealthy 
                ? $"Cache system healthy: {string.Join(", ", healthyComponents)}"
                : $"Cache system issues: {string.Join("; ", issues)}";

            _logger.LogInformation("Cache health check completed: {Status} - {Description}", 
                status, description);

            return new HealthCheckResult(status, description, null, data);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Cache health check failed with exception");
            return new HealthCheckResult(
                HealthStatus.Unhealthy, 
                "Cache health check failed", 
                ex,
                new Dictionary<string, object> { ["Error"] = ex.Message });
        }
    }

    private async Task<CacheOperationsResult> CheckCacheOperationsAsync(CancellationToken cancellationToken)
    {
        try
        {
            var startTime = DateTime.UtcNow;
            var testValue = $"HealthCheck_{DateTime.UtcNow:yyyyMMddHHmmss}";

            // Test cache set operation
            var cacheOptions = new MemoryCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(1),
                Priority = CacheItemPriority.Low
            };

            _memoryCache.Set(TestCacheKey, testValue, cacheOptions);

            // Test cache get operation
            var retrievedValue = _memoryCache.Get<string>(TestCacheKey);
            var cacheHit = retrievedValue == testValue;

            // Test cache remove operation
            _memoryCache.Remove(TestCacheKey);
            var afterRemoval = _memoryCache.Get<string>(TestCacheKey);
            var successfulRemoval = afterRemoval == null;

            var responseTime = DateTime.UtcNow - startTime;

            // Simulate async operation
            await Task.Delay(1, cancellationToken);

            return new CacheOperationsResult
            {
                IsHealthy = cacheHit && successfulRemoval,
                ResponseTimeMs = (int)responseTime.TotalMilliseconds,
                SetOperationSuccessful = true,
                GetOperationSuccessful = cacheHit,
                RemoveOperationSuccessful = successfulRemoval,
                TestValue = testValue,
                RetrievedValue = retrievedValue
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Cache operations check failed");
            return new CacheOperationsResult 
            { 
                IsHealthy = false, 
                ErrorMessage = ex.Message 
            };
        }
    }

    private async Task<CachePerformanceResult> CheckCachePerformanceAsync(CancellationToken cancellationToken)
    {
        try
        {
            var performanceTests = new List<TimeSpan>();
            var testData = new Dictionary<string, string>();

            // Generate test data
            for (int i = 0; i < 10; i++)
            {
                testData[$"perftest_{i}"] = $"Performance test data {i} - {DateTime.UtcNow}";
            }

            // Test cache set performance
            var setStartTime = DateTime.UtcNow;
            foreach (var kvp in testData)
            {
                _memoryCache.Set(kvp.Key, kvp.Value, TimeSpan.FromMinutes(1));
            }
            var setDuration = DateTime.UtcNow - setStartTime;

            // Test cache get performance
            var getStartTime = DateTime.UtcNow;
            var retrievedCount = 0;
            foreach (var key in testData.Keys)
            {
                if (_memoryCache.Get<string>(key) != null)
                    retrievedCount++;
            }
            var getDuration = DateTime.UtcNow - getStartTime;

            // Cleanup test data
            foreach (var key in testData.Keys)
            {
                _memoryCache.Remove(key);
            }

            // Simulate async operation
            await Task.Delay(1, cancellationToken);

            var avgSetTimeMs = setDuration.TotalMilliseconds / testData.Count;
            var avgGetTimeMs = getDuration.TotalMilliseconds / testData.Count;

            return new CachePerformanceResult
            {
                IsHealthy = avgSetTimeMs < 10 && avgGetTimeMs < 5, // Performance thresholds
                SetOperationsCount = testData.Count,
                GetOperationsCount = testData.Count,
                SuccessfulRetrievals = retrievedCount,
                AverageSetTimeMs = avgSetTimeMs,
                AverageGetTimeMs = avgGetTimeMs,
                TotalSetTimeMs = setDuration.TotalMilliseconds,
                TotalGetTimeMs = getDuration.TotalMilliseconds
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Cache performance check failed");
            return new CachePerformanceResult 
            { 
                IsHealthy = false, 
                ErrorMessage = ex.Message 
            };
        }
    }

    private CacheStatistics GetCacheStatistics()
    {
        try
        {
            // Get memory cache statistics using reflection (since IMemoryCache doesn't expose internal stats)
            var field = typeof(MemoryCache).GetField("_coherentState", BindingFlags.NonPublic | BindingFlags.Instance);
            var coherentState = field?.GetValue(_memoryCache);
            var entriesCollection = coherentState?.GetType()
                .GetProperty("EntriesCollection", BindingFlags.NonPublic | BindingFlags.Instance);
            
            var entries = entriesCollection?.GetValue(coherentState) as System.Collections.IDictionary;
            var entryCount = entries?.Count ?? 0;

            // Get memory usage (approximate)
            var gcMemoryBefore = GC.GetTotalMemory(false);
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            var gcMemoryAfter = GC.GetTotalMemory(true);

            return new CacheStatistics
            {
                EntryCount = entryCount,
                ApproximateMemoryUsageBytes = gcMemoryBefore,
                MemoryAfterGCBytes = gcMemoryAfter,
                LastUpdated = DateTime.UtcNow
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get cache statistics");
            return new CacheStatistics 
            { 
                ErrorMessage = ex.Message,
                LastUpdated = DateTime.UtcNow
            };
        }
    }
}

#region Result Classes

/// <summary>
/// Result of cache operations health check
/// </summary>
public class CacheOperationsResult
{
    public bool IsHealthy { get; set; }
    public string? ErrorMessage { get; set; }
    public int ResponseTimeMs { get; set; }
    public bool SetOperationSuccessful { get; set; }
    public bool GetOperationSuccessful { get; set; }
    public bool RemoveOperationSuccessful { get; set; }
    public string TestValue { get; set; } = string.Empty;
    public string? RetrievedValue { get; set; }
}

/// <summary>
/// Result of cache performance testing
/// </summary>
public class CachePerformanceResult
{
    public bool IsHealthy { get; set; }
    public string? ErrorMessage { get; set; }
    public int SetOperationsCount { get; set; }
    public int GetOperationsCount { get; set; }
    public int SuccessfulRetrievals { get; set; }
    public double AverageSetTimeMs { get; set; }
    public double AverageGetTimeMs { get; set; }
    public double TotalSetTimeMs { get; set; }
    public double TotalGetTimeMs { get; set; }
    
    public double CacheHitRate => GetOperationsCount > 0 ? (double)SuccessfulRetrievals / GetOperationsCount * 100 : 0;
}

/// <summary>
/// Cache system statistics
/// </summary>
public class CacheStatistics
{
    public int EntryCount { get; set; }
    public long ApproximateMemoryUsageBytes { get; set; }
    public long MemoryAfterGCBytes { get; set; }
    public DateTime LastUpdated { get; set; }
    public string? ErrorMessage { get; set; }
    
    public string MemoryUsageFormatted => FormatBytes(ApproximateMemoryUsageBytes);
    public string MemoryAfterGCFormatted => FormatBytes(MemoryAfterGCBytes);

    private static string FormatBytes(long bytes)
    {
        string[] suffixes = { "B", "KB", "MB", "GB" };
        int counter = 0;
        decimal number = bytes;
        while (Math.Round(number / 1024) >= 1)
        {
            number /= 1024;
            counter++;
        }
        return $"{number:n1} {suffixes[counter]}";
    }
}

#endregion
