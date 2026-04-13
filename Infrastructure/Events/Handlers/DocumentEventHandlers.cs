// ============================================================================
// DocumentEventHandlers.cs - Event Handlers for Document-Related Events
// ============================================================================
//
// Purpose: Implements event handlers for document upload events,
// providing post-upload processing such as audit logging, notifications,
// and document indexing workflows.
//
// Features:
// - Audit logging for document uploads
// - Performance monitoring for large uploads
// - Error notification for failed uploads
// - Extensible handler pattern for future features
//
// Usage:
// - Automatically triggered by DomainEventDispatcher
// - Registered in DI container for automatic discovery
// - Handles DocumentUploadedEvent and DocumentUploadFailedEvent
//
// ============================================================================

using DV.Shared.Domain.Events;
using DV.Admin.UI.Services;
using DV.Shared.Security;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace DV.Admin.UI.Infrastructure.Events.Handlers;

/// <summary>
/// Handles document uploaded events for audit logging
/// </summary>
public class DocumentUploadAuditHandler : IDomainEventHandler<DocumentUploadedEvent>
{
    private readonly AuditService _auditService;
    private readonly ILogger<DocumentUploadAuditHandler> _logger;

    public DocumentUploadAuditHandler(
        AuditService auditService,
        ILogger<DocumentUploadAuditHandler> logger)
    {
        _auditService = auditService;
        _logger = logger;
    }

    public async Task HandleAsync(DocumentUploadedEvent domainEvent, CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogDebug("Processing document upload audit for document {DocumentId}", 
                domainEvent.DocumentId);

            await _auditService.LogEventAsync(new AuditLog
            {
                EventType = AuditEventTypes.DataModification,
                Action = AuditActions.UploadDocument,
                Username = domainEvent.UploadedByUsername,
                ProjectId = null, // Would need project mapping
                Result = AuditResults.Success,
                Details = $"Document uploaded: {domainEvent.FileName} ({domainEvent.FileSizeBytes} bytes, {domainEvent.PageCount} pages)",
                Metadata = JsonSerializer.Serialize(new 
                {
                    DocumentId = domainEvent.DocumentId,
                    FileName = domainEvent.FileName,
                    ProjectSchema = domainEvent.ProjectSchema,
                    FileSizeBytes = domainEvent.FileSizeBytes,
                    PageCount = domainEvent.PageCount
                })
            });

            _logger.LogInformation("Document upload audit logged: {DocumentInfo}", 
                domainEvent.ToString());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to log audit for document upload {DocumentId}", 
                domainEvent.DocumentId);
            // Don't rethrow - audit failure shouldn't break the upload
        }
    }
}

/// <summary>
/// Handles document uploaded events for performance monitoring
/// </summary>
public class DocumentUploadPerformanceHandler : IDomainEventHandler<DocumentUploadedEvent>
{
    private readonly ILogger<DocumentUploadPerformanceHandler> _logger;

    public DocumentUploadPerformanceHandler(ILogger<DocumentUploadPerformanceHandler> logger)
    {
        _logger = logger;
    }

    public async Task HandleAsync(DocumentUploadedEvent domainEvent, CancellationToken cancellationToken = default)
    {
        try
        {
            // Monitor large file uploads
            const long largFileThreshold = 10 * 1024 * 1024; // 10MB
            if (domainEvent.FileSizeBytes > largFileThreshold)
            {
                _logger.LogInformation("Large file upload detected: {FileName} ({FileSize}) " +
                    "uploaded to {ProjectSchema} by {Username}",
                    domainEvent.FileName,
                    domainEvent.FormattedFileSize,
                    domainEvent.ProjectSchema,
                    domainEvent.UploadedByUsername);
            }

            // Monitor multi-page documents
            if (domainEvent.PageCount > 50)
            {
                _logger.LogInformation("Multi-page document uploaded: {FileName} " +
                    "({PageCount} pages) to {ProjectSchema}",
                    domainEvent.FileName,
                    domainEvent.PageCount,
                    domainEvent.ProjectSchema);
            }

            // Track upload patterns by project
            _logger.LogDebug("Document upload metrics: Project={ProjectSchema}, " +
                "Size={FileSizeBytes}, Pages={PageCount}, ContentType={ContentType}",
                domainEvent.ProjectSchema,
                domainEvent.FileSizeBytes,
                domainEvent.PageCount,
                domainEvent.ContentType);

            await Task.CompletedTask;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to process performance metrics for document upload {DocumentId}", 
                domainEvent.DocumentId);
        }
    }
}

/// <summary>
/// Handles failed document upload events
/// </summary>
public class DocumentUploadFailureHandler : IDomainEventHandler<DocumentUploadFailedEvent>
{
    private readonly AuditService _auditService;
    private readonly ILogger<DocumentUploadFailureHandler> _logger;

    public DocumentUploadFailureHandler(
        AuditService auditService,
        ILogger<DocumentUploadFailureHandler> logger)
    {
        _auditService = auditService;
        _logger = logger;
    }

    public async Task HandleAsync(DocumentUploadFailedEvent domainEvent, CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogWarning("Document upload failed: {ErrorMessage} for file {FileName} " +
                "by user {Username} in project {ProjectSchema}",
                domainEvent.ErrorMessage,
                domainEvent.FileName,
                domainEvent.UploadedByUsername,
                domainEvent.ProjectSchema);

            // Log the failure for security and troubleshooting
            await _auditService.LogEventAsync(new AuditLog
            {
                EventType = AuditEventTypes.DataModification,
                Action = AuditActions.UploadDocument,
                Username = domainEvent.UploadedByUsername,
                ProjectId = null, // Would need project mapping
                Result = AuditResults.Error,
                Details = $"Document upload failed: {domainEvent.FileName} - {domainEvent.ErrorMessage}",
                Metadata = JsonSerializer.Serialize(new 
                {
                    FileName = domainEvent.FileName,
                    ProjectSchema = domainEvent.ProjectSchema,
                    ErrorMessage = domainEvent.ErrorMessage,
                    ErrorCode = domainEvent.ErrorCode
                })
            });

            // In a production system, you might:
            // - Send notifications to administrators for certain error types
            // - Track repeated failures for rate limiting
            // - Generate alerts for security-related failures
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to process document upload failure event for {FileName}", 
                domainEvent.FileName);
        }
    }
}

/// <summary>
/// Placeholder handler for future document indexing functionality
/// </summary>
public class DocumentIndexingHandler : IDomainEventHandler<DocumentUploadedEvent>
{
    private readonly ILogger<DocumentIndexingHandler> _logger;

    public DocumentIndexingHandler(ILogger<DocumentIndexingHandler> logger)
    {
        _logger = logger;
    }

    public async Task HandleAsync(DocumentUploadedEvent domainEvent, CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogDebug("Document indexing placeholder for document {DocumentId}: {FileName}",
                domainEvent.DocumentId, domainEvent.FileName);

            // Future implementation could include:
            // - Full-text indexing for search
            // - Metadata extraction
            // - Classification and tagging
            // - OCR processing for scanned documents
            // - Integration with search engines like Elasticsearch

            await Task.CompletedTask;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Document indexing failed for document {DocumentId}", 
                domainEvent.DocumentId);
        }
    }
}
