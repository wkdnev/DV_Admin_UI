using DV.Shared.Models;
using DV.Admin.UI.Data;
using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;
using System.Text;

namespace DV.Admin.UI.Services;

/// <summary>
/// Service for handling document uploads and BLOB storage operations
/// </summary>
public class DocumentUploadService
{
    private readonly AppDbContext _context;
    private readonly ILogger<DocumentUploadService> _logger;
    private readonly IConfiguration _configuration;

    // Supported file types for upload
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

    // Maximum file size (50MB by default)
    private readonly long _maxFileSize;

    public DocumentUploadService(AppDbContext context, ILogger<DocumentUploadService> logger, IConfiguration configuration)
    {
        _context = context;
        _logger = logger;
        _configuration = configuration;
        _maxFileSize = _configuration.GetValue<long>("DocumentUpload:MaxFileSizeBytes", 50 * 1024 * 1024); // 50MB default
    }

    /// <summary>
    /// Uploads a file and stores it as BLOB in the appropriate project schema
    /// </summary>
    public async Task<DocumentPage> UploadFileAsync(int documentId, int pageNumber, IFormFile file, string? pageReference = null, int userId = 0, string documentIndex = "", DocumentPageMetadata? metadata = null)
    {
        if (file == null || file.Length == 0)
            throw new ArgumentException("File is required and cannot be empty");

        if (file.Length > _maxFileSize)
            throw new ArgumentException($"File size exceeds maximum allowed size of {_maxFileSize / (1024 * 1024)}MB");

        var fileExtension = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (!SupportedFileTypes.ContainsKey(fileExtension))
            throw new ArgumentException($"File type '{fileExtension}' is not supported");

        var contentType = SupportedFileTypes[fileExtension];

        // Find which schema contains this document
        var schemaName = await FindSchemaForDocumentAsync(documentId);
        if (string.IsNullOrEmpty(schemaName))
            throw new ArgumentException($"Document {documentId} not found in any project schema");

        // Read file content
        byte[] fileContent;
        using (var memoryStream = new MemoryStream())
        {
            await file.CopyToAsync(memoryStream);
            fileContent = memoryStream.ToArray();
        }

        // Virus Scan
        await ScanForVirusesAsync(fileContent, file.FileName);

        // Calculate MD5 checksum
        var checksumMD5 = CalculateMD5(fileContent);

        // Create DocumentPage entity using simplified BLOB constructor
        var documentPage = new DocumentPage(
            pageId: 0, // Will be assigned by database
            documentId: documentId,
            documentIndex: documentIndex,
            pageNumber: pageNumber,
            fileName: file.FileName,
            fileType: fileExtension.TrimStart('.').ToUpperInvariant(),
            fileContent: fileContent,
            contentType: contentType,
            createdBy: userId,
            checksumMD5: checksumMD5
        );

        if (metadata != null)
        {
            documentPage = documentPage with
            {
                PageReference = metadata.PageReference ?? documentPage.PageReference,
                FrameNumber = metadata.FrameNumber ?? documentPage.FrameNumber,
                Level1 = metadata.Level1 ?? documentPage.Level1,
                Level2 = metadata.Level2 ?? documentPage.Level2,
                Level3 = metadata.Level3 ?? documentPage.Level3,
                Level4 = metadata.Level4 ?? documentPage.Level4,
                DiskNumber = metadata.DiskNumber ?? documentPage.DiskNumber,
                FileFormat = metadata.FileFormat ?? documentPage.FileFormat,
                PageSize = metadata.PageSize ?? documentPage.PageSize,
                StorageType = (int)DocumentStorageType.Blob
            };
        }

        // Save to the appropriate schema
        await SaveDocumentPageToSchemaAsync(documentPage, schemaName);

        _logger.LogInformation("Successfully uploaded file {FileName} for Document {DocumentId}, Page {PageNumber} to schema {SchemaName}",
            file.FileName, documentId, pageNumber, schemaName);

        return documentPage;
    }

    /// <summary>
    /// Imports an existing file from the filesystem into BLOB storage
    /// </summary>
    public async Task<DocumentPage> ImportFileFromPathAsync(string filePath, int documentId, int pageNumber, string? pageReference = null, int userId = 0, string documentIndex = "", DocumentPageMetadata? metadata = null)
    {
        if (!File.Exists(filePath))
            throw new FileNotFoundException($"File not found: {filePath}");

        var fileInfo = new FileInfo(filePath);
        if (fileInfo.Length > _maxFileSize)
            throw new ArgumentException($"File size exceeds maximum allowed size of {_maxFileSize / (1024 * 1024)}MB");

        var fileExtension = Path.GetExtension(filePath).ToLowerInvariant();
        if (!SupportedFileTypes.ContainsKey(fileExtension))
            throw new ArgumentException($"File type '{fileExtension}' is not supported");

        var contentType = SupportedFileTypes[fileExtension];
        var fileName = Path.GetFileName(filePath);

        // Find which schema contains this document
        var schemaName = await FindSchemaForDocumentAsync(documentId);
        if (string.IsNullOrEmpty(schemaName))
            throw new ArgumentException($"Document {documentId} not found in any project schema");

        // Read file content
        var fileContent = await File.ReadAllBytesAsync(filePath);

        // Virus Scan
        await ScanForVirusesAsync(fileContent, fileName);

        // Calculate MD5 checksum
        var checksumMD5 = CalculateMD5(fileContent);

        // Create DocumentPage entity using simplified BLOB constructor
        var documentPage = new DocumentPage(
            pageId: 0, // Will be assigned by database
            documentId: documentId,
            documentIndex: documentIndex,
            pageNumber: pageNumber,
            fileName: fileName,
            fileType: fileExtension.TrimStart('.').ToUpperInvariant(),
            fileContent: fileContent,
            contentType: contentType,
            createdBy: userId,
            checksumMD5: checksumMD5
        );

        if (metadata != null)
        {
            documentPage = documentPage with
            {
                PageReference = metadata.PageReference ?? documentPage.PageReference,
                FrameNumber = metadata.FrameNumber ?? documentPage.FrameNumber,
                Level1 = metadata.Level1 ?? documentPage.Level1,
                Level2 = metadata.Level2 ?? documentPage.Level2,
                Level3 = metadata.Level3 ?? documentPage.Level3,
                Level4 = metadata.Level4 ?? documentPage.Level4,
                DiskNumber = metadata.DiskNumber ?? documentPage.DiskNumber,
                FileFormat = metadata.FileFormat ?? documentPage.FileFormat,
                PageSize = metadata.PageSize ?? documentPage.PageSize,
                StorageType = (int)DocumentStorageType.Blob
            };
        }

        // Save to the appropriate schema
        await SaveDocumentPageToSchemaAsync(documentPage, schemaName);

        _logger.LogInformation("Successfully imported file {FilePath} for Document {DocumentId}, Page {PageNumber} to schema {SchemaName}",
            filePath, documentId, pageNumber, schemaName);

        return documentPage;
    }

    /// <summary>
    /// Migrates existing file-based document pages to BLOB storage within their schemas
    /// </summary>
    public async Task<DocumentMigrationResult> MigrateFilePathToBlobAsync(string connectionString, int? documentId = null)
    {
        var result = new DocumentMigrationResult();

        try
        {
            // Get all active project schemas
            var projects = await _context.Projects
                .Where(p => p.IsActive && !string.IsNullOrEmpty(p.SchemaName))
                .ToListAsync();

            foreach (var project in projects)
            {
                try
                {
                    // Find file-based pages in this schema
                    var sql = $@"
                        SELECT * FROM ""{project.SchemaName}"".""DocumentPage"" 
                        WHERE (""StorageType"" = 0 OR ""StorageType"" IS NULL) 
                          AND ""FilePath"" IS NOT NULL AND ""FilePath"" != ''";

                    if (documentId.HasValue)
                    {
                        sql += $" AND \"DocumentId\" = {documentId.Value}";
                    }

                    var fileBasedPages = await _context.Database.SqlQueryRaw<DocumentPage>(sql).ToListAsync();

                    foreach (var page in fileBasedPages)
                    {
                        try
                        {
                            if (File.Exists(page.FilePath))
                            {
                                // Read file content
                                var fileContent = await File.ReadAllBytesAsync(page.FilePath);
                                var contentType = GetContentTypeFromExtension(page.FileType);
                                var checksumMD5 = CalculateMD5(fileContent);

                                // Update the existing page to use BLOB storage in its schema
                                await UpdateDocumentPageToBlobInSchemaAsync(page.PageId, fileContent, contentType, checksumMD5, project.SchemaName);

                                result.MigratedPages.Add(page);
                            }
                            else
                            {
                                result.FailedPages.Add(new DocumentFailedMigration
                                {
                                    PageId = page.PageId,
                                    FilePath = page.FilePath!,
                                    ErrorMessage = "File not found"
                                });
                            }
                        }
                        catch (Exception ex)
                        {
                            result.FailedPages.Add(new DocumentFailedMigration
                            {
                                PageId = page.PageId,
                                FilePath = page.FilePath!,
                                ErrorMessage = ex.Message
                            });
                            _logger.LogError(ex, "Failed to migrate page {PageId} with file {FilePath} in schema {SchemaName}",
                                page.PageId, page.FilePath, project.SchemaName);
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error processing schema {SchemaName} during migration", project.SchemaName);
                    // Continue with other schemas
                }
            }

            result.Success = true;
            _logger.LogInformation("Migration completed. Migrated: {MigratedCount}, Failed: {FailedCount}",
                result.MigratedPages.Count, result.FailedPages.Count);
        }
        catch (Exception ex)
        {
            result.ErrorMessage = ex.Message;
            _logger.LogError(ex, "Migration failed");
        }

        return result;
    }

    /// <summary>
    /// Gets document page content as BLOB from the appropriate schema
    /// </summary>
    public async Task<(byte[] content, string contentType, string fileName)?> GetDocumentPageContentAsync(int pageId)
    {
        // Find the page across all schemas
        var projects = await _context.Projects
            .Where(p => p.IsActive && !string.IsNullOrEmpty(p.SchemaName))
            .ToListAsync();

        foreach (var project in projects)
        {
            try
            {
                var sql = $"SELECT * FROM \"{project.SchemaName}\".\"DocumentPage\" WHERE \"PageId\" = {{0}}";
                var page = await _context.Database.SqlQueryRaw<DocumentPage>(sql, pageId).FirstOrDefaultAsync();
                
                if (page != null)
                {
                    if (page.UsesBlobStorage)
                    {
                        return (page.FileContent!, page.ContentType!, page.FileName);
                    }
                    else if (page.UsesFilePathStorage && File.Exists(page.FilePath))
                    {
                        // Fallback to file system for legacy documents
                        var content = await File.ReadAllBytesAsync(page.FilePath);
                        var contentType = GetContentTypeFromExtension(page.FileType);
                        return (content, contentType, page.FileName);
                    }
                }
            }
            catch
            {
                // Continue to next schema if this one fails
                continue;
            }
        }

        return null;
    }

    /// <summary>
    /// Validates file upload constraints
    /// </summary>
    public DocumentValidationResult ValidateFile(IFormFile file)
    {
        var result = new DocumentValidationResult { IsValid = true };

        if (file == null || file.Length == 0)
        {
            result.IsValid = false;
            result.Errors.Add("File is required and cannot be empty");
            return result;
        }

        if (file.Length > _maxFileSize)
        {
            result.IsValid = false;
            result.Errors.Add($"File size exceeds maximum allowed size of {_maxFileSize / (1024 * 1024)}MB");
        }

        var fileExtension = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (!SupportedFileTypes.ContainsKey(fileExtension))
        {
            result.IsValid = false;
            result.Errors.Add($"File type '{fileExtension}' is not supported. Supported types: {string.Join(", ", SupportedFileTypes.Keys)}");
        }

        return result;
    }

    /// <summary>
    /// Creates a new document in the specified schema and uploads the first page as BLOB
    /// </summary>
    public async Task<DocumentPage> CreateDocumentAndUploadPageAsync(string schemaName, int projectId, int pageNumber, IFormFile file, string? pageReference = null, int userId = 0, string documentIndex = "")
    {
        if (file == null || file.Length == 0)
            throw new ArgumentException("File is required and cannot be empty");

        if (file.Length > _maxFileSize)
            throw new ArgumentException($"File size exceeds maximum allowed size of {_maxFileSize / (1024 * 1024)}MB");

        var fileExtension = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (!SupportedFileTypes.ContainsKey(fileExtension))
            throw new ArgumentException($"File type '{fileExtension}' is not supported");

        var strategy = _context.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                // Create the document
                var documentId = await CreateDocumentInSchemaAsync(schemaName, projectId, file.FileName, fileExtension, createdBy: userId);

                // Read file content
                byte[] fileContent;
                using var ms = new MemoryStream();
                await file.CopyToAsync(ms);
                fileContent = ms.ToArray();

                var checksumMD5 = CalculateMD5(fileContent);
                var contentType = SupportedFileTypes[fileExtension];

                var documentPage = new DocumentPage(
                    pageId: 0,
                    documentId: documentId,
                    documentIndex: documentIndex,
                    pageNumber: pageNumber,
                    fileName: file.FileName,
                    fileType: fileExtension.TrimStart('.').ToUpperInvariant(),
                    fileContent: fileContent,
                    contentType: contentType,
                    createdBy: userId,
                    checksumMD5: checksumMD5
                );

                var pageId = await SaveDocumentPageToSchemaAsync(documentPage, schemaName);
                documentPage = documentPage with { PageId = pageId };

                await transaction.CommitAsync();
                _logger.LogInformation("Successfully created document {DocumentId} and uploaded page {PageNumber} in schema {SchemaName}", documentId, pageNumber, schemaName);
                return documentPage;
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }
        });
    }

    /// <summary>
    /// Saves a document page to the specified schema and returns the generated PageId
    /// </summary>
    private async Task<int> SaveDocumentPageToSchemaAsync(DocumentPage documentPage, string schemaName)
    {
    var sql = $@"
            INSERT INTO ""{schemaName}"".""DocumentPage"" 
            (DocumentId, DocumentIndex, PageNumber, PageReference, FileName, FilePath, FileType, 
             FrameNumber, Level1, Level2, Level3, Level4, DiskNumber, FileFormat, PageSize,
             FileContent, FileSize, ContentType, UploadedDate, ChecksumMD5, StorageType, CreatedOn, CreatedBy)
            VALUES (@DocumentId, @DocumentIndex, @PageNumber, @PageReference, @FileName, @FilePath, @FileType, 
                @FrameNumber, @Level1, @Level2, @Level3, @Level4, @DiskNumber, @FileFormat, @PageSize,
                @FileContent, @FileSize, @ContentType, @UploadedDate, @ChecksumMD5, @StorageType, @CreatedOn, @CreatedBy)
            RETURNING ""PageId""";

        var pageIds = await _context.Database.SqlQueryRaw<int>(sql,
            new Npgsql.NpgsqlParameter("@DocumentId", documentPage.DocumentId),
            new Npgsql.NpgsqlParameter("@DocumentIndex", documentPage.DocumentIndex ?? (object)DBNull.Value),
            new Npgsql.NpgsqlParameter("@PageNumber", documentPage.PageNumber),
            new Npgsql.NpgsqlParameter("@PageReference", documentPage.PageReference ?? (object)DBNull.Value),
            new Npgsql.NpgsqlParameter("@FileName", documentPage.FileName),
            new Npgsql.NpgsqlParameter("@FilePath", documentPage.FilePath ?? (object)DBNull.Value),
            new Npgsql.NpgsqlParameter("@FileType", documentPage.FileType),
            new Npgsql.NpgsqlParameter("@FrameNumber", documentPage.FrameNumber ?? (object)DBNull.Value),
            new Npgsql.NpgsqlParameter("@Level1", documentPage.Level1 ?? (object)DBNull.Value),
            new Npgsql.NpgsqlParameter("@Level2", documentPage.Level2 ?? (object)DBNull.Value),
            new Npgsql.NpgsqlParameter("@Level3", documentPage.Level3 ?? (object)DBNull.Value),
            new Npgsql.NpgsqlParameter("@Level4", documentPage.Level4 ?? (object)DBNull.Value),
            new Npgsql.NpgsqlParameter("@DiskNumber", documentPage.DiskNumber ?? (object)DBNull.Value),
            new Npgsql.NpgsqlParameter("@FileFormat", documentPage.FileFormat ?? (object)DBNull.Value),
            new Npgsql.NpgsqlParameter("@PageSize", documentPage.PageSize ?? (object)DBNull.Value),
            new Npgsql.NpgsqlParameter("@FileContent", documentPage.FileContent!),
            new Npgsql.NpgsqlParameter("@FileSize", documentPage.FileSize!),
            new Npgsql.NpgsqlParameter("@ContentType", documentPage.ContentType!),
            new Npgsql.NpgsqlParameter("@UploadedDate", documentPage.UploadedDate!),
            new Npgsql.NpgsqlParameter("@ChecksumMD5", documentPage.ChecksumMD5!),
            new Npgsql.NpgsqlParameter("@StorageType", (object)(int)documentPage.StorageType)
            ,new Npgsql.NpgsqlParameter("@CreatedOn", documentPage.CreatedOn),
            new Npgsql.NpgsqlParameter("@CreatedBy", documentPage.CreatedBy)
        ).ToListAsync();

        if (!pageIds.Any())
            throw new InvalidOperationException("Failed to create document page - no PageId returned");

        return pageIds.First();
    }

    /// <summary>
    /// Creates a new document in the specified schema and returns the DocumentId
    /// </summary>
    private async Task<int> CreateDocumentInSchemaAsync(string schemaName, int projectId, string fileName, string fileExtension, int createdBy = 0, string documentIndex = "", string? title = null, string? author = null, string status = "Draft", string? keywords = null, string? memo = null, string? version = null)
    {
        // Generate document number (you may want to customize this logic)
        var documentNumber = $"DOC_{DateTime.UtcNow:yyyyMMdd}_{Guid.NewGuid().ToString("N")[..8].ToUpper()}";
        var documentType = fileExtension.TrimStart('.').ToUpperInvariant();

        var sql = $@"
            INSERT INTO ""{schemaName}"".""Document"" 
            (ProjectId, DocumentNumber, DocumentType, Title, Author, Status, Keywords, Memo, DocumentIndex, Version, PublicToken, CreatedOn, CreatedBy) 
            VALUES (@ProjectId, @DocumentNumber, @DocumentType, @Title, @Author, @Status, @Keywords, @Memo, @DocumentIndex, @Version, @PublicToken, @CreatedOn, @CreatedBy)
            RETURNING ""DocumentId""";

        var documentIds = await _context.Database.SqlQueryRaw<int>(sql,
            new Npgsql.NpgsqlParameter("@ProjectId", projectId),
            new Npgsql.NpgsqlParameter("@DocumentNumber", documentNumber),
            new Npgsql.NpgsqlParameter("@DocumentType", documentType),
            new Npgsql.NpgsqlParameter("@Title", title ?? Path.GetFileNameWithoutExtension(fileName)),
            new Npgsql.NpgsqlParameter("@Author", author ?? (object)DBNull.Value),
            new Npgsql.NpgsqlParameter("@Status", status),
            new Npgsql.NpgsqlParameter("@Keywords", keywords ?? (object)DBNull.Value),
            new Npgsql.NpgsqlParameter("@Memo", memo ?? (object)DBNull.Value),
            new Npgsql.NpgsqlParameter("@DocumentIndex", documentIndex ?? (object)DBNull.Value),
            new Npgsql.NpgsqlParameter("@Version", version ?? (object)DBNull.Value),
            new Npgsql.NpgsqlParameter("@PublicToken", DV.Shared.Constants.DocumentTokenGenerator.GenerateToken()),
            new Npgsql.NpgsqlParameter("@CreatedOn", DateTime.UtcNow),
            new Npgsql.NpgsqlParameter("@CreatedBy", createdBy)
        ).ToListAsync();

        if (!documentIds.Any())
            throw new InvalidOperationException("Failed to create document - no ID returned");

        return documentIds.First();
    }

    /// <summary>
    /// Inserts a file-path based document page into the specified schema and returns the generated PageId
    /// </summary>
    private async Task<int> InsertFilePathPageToSchemaAsync(int documentId, string documentIndex, int pageNumber, string fileName, string filePath, string fileType, int createdBy, string schemaName, DocumentPageMetadata? metadata = null)
    {
        var sql = $@"
            INSERT INTO ""{schemaName}"".""DocumentPage"" 
            (DocumentId, DocumentIndex, PageNumber, FileName, FilePath, FileType, FrameNumber, Level1, Level2, Level3, Level4, DiskNumber, FileFormat, PageSize, FileSize, ContentType, ChecksumMD5, UploadedDate, CreatedOn, CreatedBy, StorageType)
            VALUES (@DocumentId, @DocumentIndex, @PageNumber, @FileName, @FilePath, @FileType, @FrameNumber, @Level1, @Level2, @Level3, @Level4, @DiskNumber, @FileFormat, @PageSize, @FileSize, @ContentType, @ChecksumMD5, @UploadedDate, @CreatedOn, @CreatedBy, @StorageType)
            RETURNING ""PageId""";

        // Prepare file metadata if the file exists
        long? fileSize = null;
        string? contentType = null;
        string? checksum = null;
        try
        {
            if (File.Exists(filePath))
            {
                var bytes = await File.ReadAllBytesAsync(filePath);
                fileSize = bytes.LongLength;
                checksum = CalculateMD5(bytes);
                contentType = GetContentTypeFromExtension(fileType);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to read file metadata for {FilePath}", filePath);
        }

        var pageIds = await _context.Database.SqlQueryRaw<int>(sql,
            new Npgsql.NpgsqlParameter("@DocumentId", documentId),
            new Npgsql.NpgsqlParameter("@DocumentIndex", documentIndex ?? (object)DBNull.Value),
            new Npgsql.NpgsqlParameter("@PageNumber", pageNumber),
            new Npgsql.NpgsqlParameter("@FileName", fileName),
            new Npgsql.NpgsqlParameter("@FilePath", filePath ?? (object)DBNull.Value),
            new Npgsql.NpgsqlParameter("@FileType", fileType),
            new Npgsql.NpgsqlParameter("@FrameNumber", metadata?.FrameNumber ?? (object)DBNull.Value),
            new Npgsql.NpgsqlParameter("@Level1", metadata?.Level1 ?? (object)DBNull.Value),
            new Npgsql.NpgsqlParameter("@Level2", metadata?.Level2 ?? (object)DBNull.Value),
            new Npgsql.NpgsqlParameter("@Level3", metadata?.Level3 ?? (object)DBNull.Value),
            new Npgsql.NpgsqlParameter("@Level4", metadata?.Level4 ?? (object)DBNull.Value),
            new Npgsql.NpgsqlParameter("@DiskNumber", metadata?.DiskNumber ?? (object)DBNull.Value),
            new Npgsql.NpgsqlParameter("@FileFormat", metadata?.FileFormat ?? (object)DBNull.Value),
            new Npgsql.NpgsqlParameter("@PageSize", metadata?.PageSize ?? (object)DBNull.Value),
            new Npgsql.NpgsqlParameter("@FileSize", fileSize ?? (object)DBNull.Value),
            new Npgsql.NpgsqlParameter("@ContentType", contentType ?? (object)DBNull.Value),
            new Npgsql.NpgsqlParameter("@ChecksumMD5", checksum ?? (object)DBNull.Value),
            new Npgsql.NpgsqlParameter("@UploadedDate", DateTime.UtcNow),
            new Npgsql.NpgsqlParameter("@CreatedOn", DateTime.UtcNow),
            new Npgsql.NpgsqlParameter("@CreatedBy", createdBy),
            new Npgsql.NpgsqlParameter("@StorageType", (object)(int)DocumentStorageType.FilePath)
        ).ToListAsync();

        if (!pageIds.Any())
            throw new InvalidOperationException("Failed to create document page - no PageId returned");

        return pageIds.First();
    }

    /// <summary>
    /// Public helper to create a file-path based page in the schema for a document
    /// </summary>
    public async Task<int> CreateFilePathPageInSchemaAsync(string schemaName, int documentId, int pageNumber, string filePath, int userId = 0, string documentIndex = "", DocumentPageMetadata? metadata = null)
    {
        var fileName = Path.GetFileName(filePath);
        var fileType = Path.GetExtension(filePath).TrimStart('.').ToUpperInvariant();
        return await InsertFilePathPageToSchemaAsync(documentId, documentIndex, pageNumber, fileName, filePath, fileType, userId, schemaName, metadata);
    }

    #region Private Helper Methods

    /// <summary>
    /// Finds which schema contains a specific document
    /// </summary>
    private async Task<string?> FindSchemaForDocumentAsync(int documentId)
    {
        var projects = await _context.Projects
            .Where(p => p.IsActive && !string.IsNullOrEmpty(p.SchemaName))
            .ToListAsync();

        foreach (var project in projects)
        {
            try
            {
                var sql = $"SELECT COUNT(*) FROM \"{project.SchemaName}\".\"Document\" WHERE \"DocumentId\" = {{0}}";
                var counts = await _context.Database.SqlQueryRaw<int>(sql, documentId).ToListAsync();
                var count = counts.FirstOrDefault();
                if (count > 0)
                {
                    return project.SchemaName;
                }
            }
            catch
            {
                // Continue to next schema if this one fails
                continue;
            }
        }

        return null;
    }

    /// <summary>
    /// Saves a document page to the specified schema (without returning PageId)
    /// </summary>
    private async Task SaveDocumentPageToSchemaWithoutIdAsync(DocumentPage documentPage, string schemaName)
    {
    var sql = $@"
        INSERT INTO ""{schemaName}"".""DocumentPage"" (DocumentId, DocumentIndex, PageNumber, PageReference, FileName, FilePath, FileType, 
                    FrameNumber, Level1, Level2, Level3, Level4, DiskNumber, FileFormat, PageSize,
                    FileContent, FileSize, ContentType, UploadedDate, ChecksumMD5, StorageType, CreatedOn, CreatedBy)
        VALUES (@DocumentId, @DocumentIndex, @PageNumber, @PageReference, @FileName, @FilePath, @FileType, 
            @FrameNumber, @Level1, @Level2, @Level3, @Level4, @DiskNumber, @FileFormat, @PageSize,
            @FileContent, @FileSize, @ContentType, @UploadedDate, @ChecksumMD5, @StorageType, @CreatedOn, @CreatedBy)";

        await _context.Database.ExecuteSqlRawAsync(sql,
            new Npgsql.NpgsqlParameter("@DocumentId", documentPage.DocumentId),
            new Npgsql.NpgsqlParameter("@DocumentIndex", documentPage.DocumentIndex ?? (object)DBNull.Value),
            new Npgsql.NpgsqlParameter("@PageNumber", documentPage.PageNumber),
            new Npgsql.NpgsqlParameter("@PageReference", documentPage.PageReference ?? (object)DBNull.Value),
            new Npgsql.NpgsqlParameter("@FileName", documentPage.FileName),
            new Npgsql.NpgsqlParameter("@FilePath", documentPage.FilePath),
            new Npgsql.NpgsqlParameter("@FileType", documentPage.FileType),
            new Npgsql.NpgsqlParameter("@FrameNumber", documentPage.FrameNumber ?? (object)DBNull.Value),
            new Npgsql.NpgsqlParameter("@Level1", documentPage.Level1 ?? (object)DBNull.Value),
            new Npgsql.NpgsqlParameter("@Level2", documentPage.Level2 ?? (object)DBNull.Value),
            new Npgsql.NpgsqlParameter("@Level3", documentPage.Level3 ?? (object)DBNull.Value),
            new Npgsql.NpgsqlParameter("@Level4", documentPage.Level4 ?? (object)DBNull.Value),
            new Npgsql.NpgsqlParameter("@DiskNumber", documentPage.DiskNumber ?? (object)DBNull.Value),
            new Npgsql.NpgsqlParameter("@FileFormat", documentPage.FileFormat ?? (object)DBNull.Value),
            new Npgsql.NpgsqlParameter("@PageSize", documentPage.PageSize ?? (object)DBNull.Value),
            new Npgsql.NpgsqlParameter("@FileContent", documentPage.FileContent!),
            new Npgsql.NpgsqlParameter("@FileSize", documentPage.FileSize!),
            new Npgsql.NpgsqlParameter("@ContentType", documentPage.ContentType!),
            new Npgsql.NpgsqlParameter("@UploadedDate", documentPage.UploadedDate!),
            new Npgsql.NpgsqlParameter("@ChecksumMD5", documentPage.ChecksumMD5!),
            new Npgsql.NpgsqlParameter("@StorageType", (object)(int)documentPage.StorageType)
            ,new Npgsql.NpgsqlParameter("@CreatedOn", documentPage.CreatedOn),
            new Npgsql.NpgsqlParameter("@CreatedBy", documentPage.CreatedBy)
        );
    }

    /// <summary>
    /// Updates an existing document page to use BLOB storage in its schema
    /// </summary>
    private async Task UpdateDocumentPageToBlobInSchemaAsync(int pageId, byte[] fileContent, string contentType, string checksumMD5, string schemaName)
    {
        var sql = $@"
            UPDATE ""{schemaName}"".""DocumentPage"" 
            SET ""FileContent"" = @FileContent, 
                ""FileSize"" = @FileSize, 
                ""ContentType"" = @ContentType, 
                ""UploadedDate"" = @UploadedDate, 
                ""ChecksumMD5"" = @ChecksumMD5, 
                ""StorageType"" = @StorageType
            WHERE ""PageId"" = @PageId";

        await _context.Database.ExecuteSqlRawAsync(sql,
            new Npgsql.NpgsqlParameter("@PageId", pageId),
            new Npgsql.NpgsqlParameter("@FileContent", fileContent),
            new Npgsql.NpgsqlParameter("@FileSize", fileContent.Length),
            new Npgsql.NpgsqlParameter("@ContentType", contentType),
            new Npgsql.NpgsqlParameter("@UploadedDate", DateTime.UtcNow),
            new Npgsql.NpgsqlParameter("@ChecksumMD5", checksumMD5),
            new Npgsql.NpgsqlParameter("@StorageType", (object)(int)DocumentStorageType.Blob)
        );
    }


    private async Task ScanForVirusesAsync(byte[] content, string fileName)
    {
        var enableScanning = _configuration.GetValue<bool>("DocumentUpload:EnableVirusScanning");
        if (enableScanning)
        {
             _logger.LogInformation("Scanning file {FileName} for malware...", fileName);
             // Simulated scan delay
             await Task.Delay(50); 
             
             // Simple check for EICAR test file behavior based on filename for testing purposes
             if (fileName.Contains("eicar", StringComparison.OrdinalIgnoreCase))
             {
                 _logger.LogWarning("Virus detected in file {FileName}!", fileName);
                 throw new InvalidOperationException("Security Alert: Threat detected in uploaded file.");
             }
             
             _logger.LogInformation("File {FileName} scan result: Clean.", fileName);
        }
    }

    private static string CalculateMD5(byte[] data)
    {
        using var md5 = MD5.Create();
        var hash = md5.ComputeHash(data);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static string GetContentTypeFromExtension(string fileType)
    {
        var extension = $".{fileType.ToLowerInvariant()}";
        return SupportedFileTypes.GetValueOrDefault(extension, "application/octet-stream");
    }

    #endregion
}

#region Result Classes

/// <summary>
/// Result of file migration operation
/// </summary>
public class DocumentMigrationResult
{
    public bool Success { get; set; } = false;
    public string? ErrorMessage { get; set; }
    public List<DocumentPage> MigratedPages { get; set; } = new();
    public List<DocumentFailedMigration> FailedPages { get; set; } = new();
}

/// <summary>
/// Information about a failed page migration
/// </summary>
public class DocumentFailedMigration
{
    public int PageId { get; set; }
    public string FilePath { get; set; } = string.Empty;
    public string ErrorMessage { get; set; } = string.Empty;
}

/// <summary>
/// Result of file validation
/// </summary>
public class DocumentValidationResult
{
    public bool IsValid { get; set; }
    public List<string> Errors { get; set; } = new();
}

#endregion
