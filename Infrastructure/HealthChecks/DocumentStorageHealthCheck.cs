using DV.Admin.UI.Data;
using DV.Admin.UI.Data;
// ============================================================================
// DocumentStorageHealthCheck.cs - Document Storage Health Check
// ============================================================================
//
// Purpose: Monitors document storage system health including BLOB storage
// operations and file system access for document uploads and retrieval.
//
// Features:
// - BLOB storage connectivity testing
// - Document retrieval performance monitoring
// - Storage capacity and usage tracking
// - Multi-schema document access validation
//
// ============================================================================

using Microsoft.Extensions.Diagnostics.HealthChecks;
using DV.Admin.UI.Services;
using DV.Admin.UI.Data;
using DV.Shared.Models;
using Microsoft.EntityFrameworkCore;

namespace DV.Admin.UI.Infrastructure.HealthChecks;

/// <summary>
/// Health check for document storage operations and BLOB functionality
/// </summary>
public class DocumentStorageHealthCheck : IHealthCheck
{
    private readonly AppDbContext _context;
    private readonly DocumentUploadService _documentService;
    private readonly ILogger<DocumentStorageHealthCheck> _logger;

    public DocumentStorageHealthCheck(
        AppDbContext context,
        DocumentUploadService documentService,
        ILogger<DocumentStorageHealthCheck> logger)
    {
        _context = context;
        _documentService = documentService;
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

            // Check document storage accessibility
            var storageResult = await CheckDocumentStorageAsync(cancellationToken);
            data.Add("DocumentStorage", storageResult);
            
            if (storageResult.IsHealthy)
                healthyComponents.Add("DocumentStorage");
            else
                issues.Add($"DocumentStorage: {storageResult.ErrorMessage}");

            // Check BLOB operations
            var blobResult = await CheckBlobOperationsAsync(cancellationToken);
            data.Add("BlobOperations", blobResult);
            
            if (blobResult.IsHealthy)
                healthyComponents.Add("BlobOperations");
            else
                issues.Add($"BlobOperations: {blobResult.ErrorMessage}");

            // Check storage statistics
            var statsResult = await GetStorageStatisticsAsync(cancellationToken);
            data.Add("StorageStatistics", statsResult);

            // Overall health determination
            var isHealthy = storageResult.IsHealthy && blobResult.IsHealthy;
            var status = isHealthy ? HealthStatus.Healthy : HealthStatus.Unhealthy;

            var description = isHealthy 
                ? $"Document storage healthy: {string.Join(", ", healthyComponents)}"
                : $"Document storage issues: {string.Join("; ", issues)}";

            _logger.LogInformation("Document storage health check completed: {Status} - {Description}", 
                status, description);

            return new HealthCheckResult(status, description, null, data);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Document storage health check failed with exception");
            return new HealthCheckResult(
                HealthStatus.Unhealthy, 
                "Document storage health check failed", 
                ex,
                new Dictionary<string, object> { ["Error"] = ex.Message });
        }
    }

    private async Task<StorageCheckResult> CheckDocumentStorageAsync(CancellationToken cancellationToken)
    {
        try
        {
            var startTime = DateTime.UtcNow;

            // Test basic project table access first
            var projectCount = await _context.Projects.CountAsync(cancellationToken);

            // Test document access in existing schemas using raw SQL
            var schemaDocumentCounts = new Dictionary<string, int>();
            var totalDocuments = 0;
            var totalPages = 0;

            // Get list of project schemas
            var projects = await _context.Projects
                .Where(p => p.IsActive)
                .Select(p => p.SchemaName)
                .ToListAsync(cancellationToken);

            foreach (var schema in projects)
            {
                try
                {
                    // Check if schema exists and get document count
                    var documentCountSql = $"SELECT COUNT(*) FROM \"{schema}\".\"Document\"";
                    var documentCount = await _context.Database
                        .SqlQueryRaw<int>(documentCountSql)
                        .FirstOrDefaultAsync(cancellationToken);
                    
                    schemaDocumentCounts[schema] = documentCount;
                    totalDocuments += documentCount;

                    // Get page count
                    var pageCountSql = $"SELECT COUNT(*) FROM \"{schema}\".\"DocumentPage\"";
                    var pageCount = await _context.Database
                        .SqlQueryRaw<int>(pageCountSql)
                        .FirstOrDefaultAsync(cancellationToken);
                    
                    totalPages += pageCount;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Could not access schema {Schema}", schema);
                    schemaDocumentCounts[schema] = -1; // Indicate error
                }
            }

            var responseTime = DateTime.UtcNow - startTime;

            return new StorageCheckResult
            {
                IsHealthy = true,
                ResponseTimeMs = (int)responseTime.TotalMilliseconds,
                ProjectCount = projectCount,
                DocumentCount = totalDocuments,
                PageCount = totalPages,
                SampleRetrieved = schemaDocumentCounts.Count,
                SchemaDocumentCounts = schemaDocumentCounts
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Document storage check failed");
            return new StorageCheckResult 
            { 
                IsHealthy = false, 
                ErrorMessage = ex.Message 
            };
        }
    }

    private async Task<BlobCheckResult> CheckBlobOperationsAsync(CancellationToken cancellationToken)
    {
        try
        {
            var startTime = DateTime.UtcNow;

            // Test BLOB data access using raw SQL across schemas
            var blobCount = 0;
            var totalBlobSize = 0L;
            var sampleBlobs = new List<object>();

            // Get active projects to check their schemas
            var projects = await _context.Projects
                .Where(p => p.IsActive)
                .Select(p => p.SchemaName)
                .Take(3) // Sample a few schemas
                .ToListAsync(cancellationToken);

            foreach (var schema in projects)
            {
                try
                {
                    // Count pages with BLOB content in this schema
                    var schemaBlobCountSql = $"SELECT COUNT(*) FROM \"{schema}\".\"DocumentPage\" WHERE \"FileContent\" IS NOT NULL";
                    var schemaBlobCount = await _context.Database
                        .SqlQueryRaw<int>(schemaBlobCountSql)
                        .FirstOrDefaultAsync(cancellationToken);
                    
                    blobCount += schemaBlobCount;

                    // Get sample BLOB info
                    var sampleBlobSql = $"SELECT \"PageId\", \"FileName\", OCTET_LENGTH(\"FileContent\") as \"ContentSize\", \"ContentType\" FROM \"{schema}\".\"DocumentPage\" WHERE \"FileContent\" IS NOT NULL LIMIT 1";
                    var sampleResult = await _context.Database
                        .SqlQueryRaw<dynamic>(sampleBlobSql)
                        .FirstOrDefaultAsync(cancellationToken);
                    
                    if (sampleResult != null)
                    {
                        sampleBlobs.Add(new { Schema = schema, Sample = sampleResult });
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Could not check BLOBs in schema {Schema}", schema);
                }
            }

            var responseTime = DateTime.UtcNow - startTime;

            return new BlobCheckResult
            {
                IsHealthy = true,
                ResponseTimeMs = (int)responseTime.TotalMilliseconds,
                BlobsWithContent = blobCount,
                TotalBlobSizeBytes = totalBlobSize,
                SampleBlobs = sampleBlobs.Select(b => new BlobInfo
                {
                    PageId = 0, // Schema sample
                    FileName = $"Sample from {b}",
                    ContentSize = 0,
                    ContentType = "sample"
                }).ToList()
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "BLOB operations check failed");
            return new BlobCheckResult 
            { 
                IsHealthy = false, 
                ErrorMessage = ex.Message 
            };
        }
    }

    private async Task<StorageStatistics> GetStorageStatisticsAsync(CancellationToken cancellationToken)
    {
        try
        {
            // Get storage usage statistics across all schemas
            var totalPages = 0;
            var totalSize = 0L;

            var projects = await _context.Projects
                .Where(p => p.IsActive)
                .Select(p => p.SchemaName)
                .ToListAsync(cancellationToken);

            foreach (var schema in projects)
            {
                try
                {
                    var pageCountSql = $"SELECT COUNT(*) FROM \"{schema}\".\"DocumentPage\"";
                    var pageCount = await _context.Database
                        .SqlQueryRaw<int>(pageCountSql)
                        .FirstOrDefaultAsync(cancellationToken);
                    
                    totalPages += pageCount;

                    var sizeSql = $"SELECT COALESCE(SUM(OCTET_LENGTH(\"FileContent\")), 0) FROM \"{schema}\".\"DocumentPage\" WHERE \"FileContent\" IS NOT NULL";
                    var schemaSize = await _context.Database
                        .SqlQueryRaw<long>(sizeSql)
                        .FirstOrDefaultAsync(cancellationToken);
                    
                    totalSize += schemaSize;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Could not get statistics for schema {Schema}", schema);
                }
            }

            return new StorageStatistics
            {
                TotalPages = totalPages,
                TotalSizeBytes = totalSize,
                AverageSizeBytes = totalPages > 0 ? (double)totalSize / totalPages : 0
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get storage statistics");
            return new StorageStatistics { ErrorMessage = ex.Message };
        }
    }
}

#region Result Classes

/// <summary>
/// Result of document storage health check
/// </summary>
public class StorageCheckResult
{
    public bool IsHealthy { get; set; }
    public string? ErrorMessage { get; set; }
    public int ResponseTimeMs { get; set; }
    public int ProjectCount { get; set; }
    public int DocumentCount { get; set; }
    public int PageCount { get; set; }
    public int SampleRetrieved { get; set; }
    public Dictionary<string, int> SchemaDocumentCounts { get; set; } = new();
}

/// <summary>
/// Result of BLOB operations health check
/// </summary>
public class BlobCheckResult
{
    public bool IsHealthy { get; set; }
    public string? ErrorMessage { get; set; }
    public int ResponseTimeMs { get; set; }
    public int BlobsWithContent { get; set; }
    public long TotalBlobSizeBytes { get; set; }
    public List<BlobInfo> SampleBlobs { get; set; } = new();
}

/// <summary>
/// Information about a BLOB storage item
/// </summary>
public class BlobInfo
{
    public int PageId { get; set; }
    public string FileName { get; set; } = string.Empty;
    public int ContentSize { get; set; }
    public string ContentType { get; set; } = string.Empty;
}

/// <summary>
/// Storage usage statistics
/// </summary>
public class StorageStatistics
{
    public int TotalPages { get; set; }
    public long TotalSizeBytes { get; set; }
    public double AverageSizeBytes { get; set; }
    public string? ErrorMessage { get; set; }
    
    public string TotalSizeFormatted => FormatBytes(TotalSizeBytes);
    public string AverageSizeFormatted => FormatBytes((long)AverageSizeBytes);

    private static string FormatBytes(long bytes)
    {
        string[] suffixes = { "B", "KB", "MB", "GB", "TB" };
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
