// ============================================================================
// DocumentUploadResultService.cs - Result Pattern Demonstration
// ============================================================================
//
// Purpose: Demonstrates the Result pattern implementation for better error
// handling in document upload operations. This service shows how to replace
// exception-based error handling with a more functional approach.
//
// Features:
// - Result pattern for error handling without exceptions
// - Fluent API for chaining operations
// - Comprehensive error collection and reporting
// - Async operation support
// - Clean separation of success/failure paths
//
// ============================================================================

using DV.Shared.Models;
using DV.Admin.UI.Infrastructure.Common;
using Microsoft.AspNetCore.Http;

namespace DV.Admin.UI.Services;

/// <summary>
/// Demonstrates the Result pattern for document upload operations
/// Provides examples of how to use Result&lt;T&gt; for better error handling
/// </summary>
public class DocumentUploadResultService
{
    private readonly ILogger<DocumentUploadResultService> _logger;
    private readonly DocumentUploadService _uploadService;

    // Supported file types for demonstration
    private static readonly Dictionary<string, string> SupportedFileTypes = new()
    {
        { ".pdf", "application/pdf" },
        { ".tiff", "image/tiff" },
        { ".tif", "image/tiff" },
        { ".jpg", "image/jpeg" },
        { ".jpeg", "image/jpeg" },
        { ".png", "image/png" },
        { ".bmp", "image/bmp" },
        { ".gif", "image/gif" }
    };

    public DocumentUploadResultService(
        ILogger<DocumentUploadResultService> logger,
        DocumentUploadService uploadService)
    {
        _logger = logger;
        _uploadService = uploadService;
    }

    #region Result Pattern Examples

    /// <summary>
    /// Validates a file using the Result pattern instead of exceptions
    /// Demonstrates: Basic Result pattern usage with success/failure paths
    /// </summary>
    public Task<Result<FileValidationInfo>> ValidateFileAsync(IFormFile file)
    {
        try
        {
            // Input validation using Result pattern
            if (file == null)
                return Task.FromResult(Result<FileValidationInfo>.Failure("File cannot be null"));

            if (file.Length == 0)
                return Task.FromResult(Result<FileValidationInfo>.Failure("File cannot be empty"));

            if (string.IsNullOrWhiteSpace(file.FileName))
                return Task.FromResult(Result<FileValidationInfo>.Failure("File name is required"));

            // File size validation (50MB limit)
            const long maxFileSize = 50 * 1024 * 1024;
            if (file.Length > maxFileSize)
                return Task.FromResult(Result<FileValidationInfo>.Failure(
                    $"File size ({file.Length:N0} bytes) exceeds maximum allowed size ({maxFileSize:N0} bytes)"));

            // File extension validation
            var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
            if (string.IsNullOrEmpty(extension))
                return Task.FromResult(Result<FileValidationInfo>.Failure("File must have an extension"));

            if (!SupportedFileTypes.ContainsKey(extension))
                return Task.FromResult(Result<FileValidationInfo>.Failure(
                    $"File type '{extension}' is not supported. Supported types: {string.Join(", ", SupportedFileTypes.Keys)}"));

            // Success case - return validation info
            var validationInfo = new FileValidationInfo
            {
                FileName = file.FileName,
                FileSize = file.Length,
                FileExtension = extension,
                ContentType = SupportedFileTypes[extension],
                IsValid = true
            };

            _logger.LogInformation("File validation successful for {FileName}", file.FileName);
            return Task.FromResult(Result<FileValidationInfo>.Success(validationInfo));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error validating file {FileName}", file?.FileName ?? "unknown");
            return Task.FromResult(Result<FileValidationInfo>.Failure($"Validation error: {ex.Message}"));
        }
    }

    /// <summary>
    /// Validates multiple files and collects all errors
    /// Demonstrates: Error collection and batch processing with Result pattern
    /// </summary>
    public async Task<Result<List<FileValidationInfo>>> ValidateMultipleFilesAsync(IEnumerable<IFormFile> files)
    {
        try
        {
            var validFiles = new List<FileValidationInfo>();
            var errors = new List<string>();

            foreach (var file in files)
            {
                var validationResult = await ValidateFileAsync(file);
                
                if (validationResult.IsSuccess)
                {
                    validFiles.Add(validationResult.Value);
                }
                else
                {
                    // Collect errors with file context
                    var fileErrors = validationResult.Errors
                        .Select(error => $"{file?.FileName ?? "unknown"}: {error}")
                        .ToList();
                    errors.AddRange(fileErrors);
                }
            }

            // Return based on validation results
            if (errors.Any())
            {
                _logger.LogWarning("Batch validation failed for {ErrorCount} files", errors.Count);
                return Result<List<FileValidationInfo>>.Failure(errors);
            }

            _logger.LogInformation("Batch validation successful for {FileCount} files", validFiles.Count);
            return Result<List<FileValidationInfo>>.Success(validFiles);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during batch file validation");
            return Result<List<FileValidationInfo>>.Failure($"Batch validation error: {ex.Message}");
        }
    }

    /// <summary>
    /// Processes file upload with workflow chaining using Result pattern
    /// Demonstrates: Chaining operations and early returns with Result pattern
    /// </summary>
    public async Task<Result<UploadResult>> ProcessFileUploadWorkflowAsync(
        int documentId, 
        IFormFile file, 
        string? pageReference = null,
        int userId = 0)
    {
        try
        {
            // Step 1: Validate file using Result pattern
            var validationResult = await ValidateFileAsync(file);
            if (validationResult.IsFailure)
            {
                return Result<UploadResult>.Failure(validationResult.Errors);
            }

            // Step 2: Determine next page number (simulated)
            var pageNumberResult = await GetNextPageNumberAsync(documentId);
            if (pageNumberResult.IsFailure)
            {
                return Result<UploadResult>.Failure(pageNumberResult.Errors);
            }

            // Step 3: Upload file using traditional service (wrapped in Result)
            var uploadResult = await UploadFileWithResultWrapperAsync(
                documentId, 
                pageNumberResult.Value, 
                file, 
                pageReference,
                userId);

            if (uploadResult.IsFailure)
            {
                return Result<UploadResult>.Failure(uploadResult.Errors);
            }

            // Success - create upload result
            var result = new UploadResult
            {
                DocumentId = documentId,
                PageNumber = pageNumberResult.Value,
                FileName = validationResult.Value.FileName,
                FileSize = validationResult.Value.FileSize,
                ContentType = validationResult.Value.ContentType,
                UploadedAt = DateTime.UtcNow,
                PageId = uploadResult.Value.PageId,
                Success = true
            };

            _logger.LogInformation(
                "File upload workflow completed successfully for {FileName} -> Document {DocumentId}, Page {PageNumber}", 
                file.FileName, documentId, pageNumberResult.Value);

            return Result<UploadResult>.Success(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, 
                "Error in file upload workflow for document {DocumentId}", documentId);
            return Result<UploadResult>.Failure($"Upload workflow error: {ex.Message}");
        }
    }

    /// <summary>
    /// Demonstrates Result pattern with fluent API and method chaining
    /// Shows: Advanced Result pattern usage with transformation chains
    /// </summary>
    public async Task<Result<string>> ProcessAndReportFileUploadAsync(int documentId, IFormFile file, int userId = 0)
    {
        // Step 1: Validate the file
        var validationResult = await ValidateFileAsync(file);
        if (validationResult.IsFailure)
        {
            _logger.LogWarning("File upload process failed during validation: {Errors}", 
                string.Join("; ", validationResult.Errors));
            return Result<string>.Failure(validationResult.Errors);
        }

        // Step 2: Process the upload workflow
    var uploadResult = await ProcessFileUploadWorkflowAsync(documentId, file, null, userId);
        if (uploadResult.IsFailure)
        {
            _logger.LogWarning("File upload process failed during upload: {Errors}", 
                string.Join("; ", uploadResult.Errors));
            return Result<string>.Failure(uploadResult.Errors);
        }

        // Step 3: Create summary report
        var summary = $"Upload completed: {uploadResult.Value.FileName} " +
                     $"({uploadResult.Value.FileSize:N0} bytes) -> " +
                     $"Document {uploadResult.Value.DocumentId}, Page {uploadResult.Value.PageNumber}";

        _logger.LogInformation("File upload process completed: {Summary}", summary);
        return Result<string>.Success(summary);
    }

    #endregion

    #region Helper Methods

    /// <summary>
    /// Gets the next available page number for a document
    /// Demonstrates: Converting traditional async methods to Result pattern
    /// </summary>
    private async Task<Result<int>> GetNextPageNumberAsync(int documentId)
    {
        try
        {
            // Simulate getting next page number from database or service
            // For demo purposes, we'll simulate this logic
            await Task.Delay(10); // Simulate async operation
            
            if (documentId <= 0)
                return Result<int>.Failure("Invalid document ID");

            // Simulate getting the next page number (normally from database)
            var nextPageNumber = 1; // This would normally be calculated from existing pages
            return Result<int>.Success(nextPageNumber);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting next page number for document {DocumentId}", documentId);
            return Result<int>.Failure("Failed to determine next page number");
        }
    }

    /// <summary>
    /// Wraps traditional upload service method in Result pattern
    /// Demonstrates: Adapting existing exception-based APIs to Result pattern
    /// </summary>
    private async Task<Result<DocumentPage>> UploadFileWithResultWrapperAsync(
        int documentId, 
        int pageNumber, 
        IFormFile file, 
        string? pageReference = null,
        int userId = 0)
    {
        try
        {
            var documentPage = await _uploadService.UploadFileAsync(
                documentId, pageNumber, file, pageReference, userId);
            return Result<DocumentPage>.Success(documentPage);
        }
        catch (ArgumentException ex)
        {
            return Result<DocumentPage>.Failure($"Upload validation failed: {ex.Message}");
        }
        catch (FileNotFoundException ex)
        {
            return Result<DocumentPage>.Failure($"Document not found: {ex.Message}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error uploading file for document {DocumentId}, page {PageNumber}", 
                documentId, pageNumber);
            return Result<DocumentPage>.Failure("Upload operation failed");
        }
    }

    #endregion
}

#region Result Data Models

/// <summary>
/// Contains file validation information
/// </summary>
public class FileValidationInfo
{
    public string FileName { get; set; } = string.Empty;
    public long FileSize { get; set; }
    public string FileExtension { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public bool IsValid { get; set; }
    public DateTime ValidatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Contains upload operation result information
/// </summary>
public class UploadResult
{
    public int DocumentId { get; set; }
    public int PageNumber { get; set; }
    public int PageId { get; set; }
    public string FileName { get; set; } = string.Empty;
    public long FileSize { get; set; }
    public string ContentType { get; set; } = string.Empty;
    public DateTime UploadedAt { get; set; }
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
}

#endregion
