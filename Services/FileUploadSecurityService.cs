// ============================================================================
// FileUploadSecurityService.cs - Enhanced File Upload Security Service
// ============================================================================
//
// Purpose: Provides comprehensive file upload security validation including
// MIME type validation, file content validation, file name sanitization,
// and role-based file size limits.
//
// Features:
// - MIME type validation against file extension
// - File content signature validation (magic bytes)
// - File name sanitization and validation
// - Role-based file size limits
// - Malicious file pattern detection
// - Content analysis for embedded threats
//
// ============================================================================

using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;

namespace DV.Admin.UI.Services;

/// <summary>
/// Configuration options for file upload security
/// </summary>
public class FileUploadSecurityOptions
{
    public long DefaultMaxFileSizeBytes { get; set; } = 50 * 1024 * 1024; // 50MB
    public long AdminMaxFileSizeBytes { get; set; } = 100 * 1024 * 1024; // 100MB
    public long EditorMaxFileSizeBytes { get; set; } = 75 * 1024 * 1024; // 75MB
    public long ReadOnlyMaxFileSizeBytes { get; set; } = 25 * 1024 * 1024; // 25MB
    
    public bool EnableMimeTypeValidation { get; set; } = true;
    public bool EnableFileSignatureValidation { get; set; } = true;
    public bool EnableContentAnalysis { get; set; } = true;
    public bool EnableFileNameSanitization { get; set; } = true;
    
    public string[] BlockedFileExtensions { get; set; } = new[]
    {
        ".exe", ".bat", ".cmd", ".scr", ".com", ".pif", ".js", ".vbs", ".jar",
        ".ps1", ".sh", ".php", ".asp", ".aspx", ".jsp", ".html", ".htm"
    };

    public string[] SuspiciousPatterns { get; set; } = new[]
    {
        "javascript:", "vbscript:", "data:", "file:", "ftp:", 
        "<script", "</script>", "eval(", "alert(", "confirm(",
        "prompt(", "document.", "window.", "location."
    };
}

/// <summary>
/// File validation result
/// </summary>
public class FileValidationResult
{
    public bool IsValid { get; set; }
    public List<string> Errors { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
    public string? SanitizedFileName { get; set; }
    public string? DetectedMimeType { get; set; }
    public long FileSizeBytes { get; set; }
    public string? FileHash { get; set; }
}

/// <summary>
/// File signature for validation
/// </summary>
public class FileSignature
{
    public string Extension { get; set; } = string.Empty;
    public string MimeType { get; set; } = string.Empty;
    public byte[][] Signatures { get; set; } = Array.Empty<byte[]>();
}

/// <summary>
/// Enhanced file upload security service
/// </summary>
public class FileUploadSecurityService
{
    private readonly FileUploadSecurityOptions _options;
    private readonly ILogger<FileUploadSecurityService> _logger;
    
    // File signatures for validation (magic bytes)
    private static readonly Dictionary<string, FileSignature> FileSignatures = new()
    {
        {
            ".pdf", new FileSignature
            {
                Extension = ".pdf",
                MimeType = "application/pdf",
                Signatures = new[] { new byte[] { 0x25, 0x50, 0x44, 0x46 } } // %PDF
            }
        },
        {
            ".jpg", new FileSignature
            {
                Extension = ".jpg",
                MimeType = "image/jpeg",
                Signatures = new[] { new byte[] { 0xFF, 0xD8, 0xFF } } // JPEG
            }
        },
        {
            ".jpeg", new FileSignature
            {
                Extension = ".jpeg",
                MimeType = "image/jpeg",
                Signatures = new[] { new byte[] { 0xFF, 0xD8, 0xFF } } // JPEG
            }
        },
        {
            ".png", new FileSignature
            {
                Extension = ".png",
                MimeType = "image/png",
                Signatures = new[] { new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A } } // PNG
            }
        },
        {
            ".tiff", new FileSignature
            {
                Extension = ".tiff",
                MimeType = "image/tiff",
                Signatures = new[] 
                { 
                    new byte[] { 0x49, 0x49, 0x2A, 0x00 }, // TIFF little-endian
                    new byte[] { 0x4D, 0x4D, 0x00, 0x2A }  // TIFF big-endian
                }
            }
        },
        {
            ".tif", new FileSignature
            {
                Extension = ".tif",
                MimeType = "image/tiff",
                Signatures = new[] 
                { 
                    new byte[] { 0x49, 0x49, 0x2A, 0x00 }, // TIFF little-endian
                    new byte[] { 0x4D, 0x4D, 0x00, 0x2A }  // TIFF big-endian
                }
            }
        },
        {
            ".bmp", new FileSignature
            {
                Extension = ".bmp",
                MimeType = "image/bmp",
                Signatures = new[] { new byte[] { 0x42, 0x4D } } // BM
            }
        },
        {
            ".gif", new FileSignature
            {
                Extension = ".gif",
                MimeType = "image/gif",
                Signatures = new[] 
                { 
                    new byte[] { 0x47, 0x49, 0x46, 0x38, 0x37, 0x61 }, // GIF87a
                    new byte[] { 0x47, 0x49, 0x46, 0x38, 0x39, 0x61 }  // GIF89a
                }
            }
        }
    };

    public FileUploadSecurityService(
        IOptions<FileUploadSecurityOptions> options, 
        ILogger<FileUploadSecurityService> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    /// <summary>
    /// Validates file upload security
    /// </summary>
    public async Task<FileValidationResult> ValidateFileAsync(IFormFile file, string[] userRoles)
    {
        var result = new FileValidationResult
        {
            FileSizeBytes = file.Length
        };

        try
        {
            // Basic validation
            if (file == null || file.Length == 0)
            {
                result.Errors.Add("File is required and cannot be empty");
                return result;
            }

            // File name validation and sanitization
            if (_options.EnableFileNameSanitization)
            {
                var fileNameResult = ValidateAndSanitizeFileName(file.FileName);
                if (!fileNameResult.IsValid)
                {
                    result.Errors.AddRange(fileNameResult.Errors);
                    return result;
                }
                result.SanitizedFileName = fileNameResult.SanitizedFileName;
            }

            // File extension validation
            var fileExtension = Path.GetExtension(file.FileName).ToLowerInvariant();
            if (string.IsNullOrEmpty(fileExtension))
            {
                result.Errors.Add("File must have a valid extension");
                return result;
            }

            if (_options.BlockedFileExtensions.Contains(fileExtension))
            {
                result.Errors.Add($"File type '{fileExtension}' is not allowed for security reasons");
                return result;
            }

            if (!FileSignatures.ContainsKey(fileExtension))
            {
                result.Errors.Add($"File type '{fileExtension}' is not supported");
                return result;
            }

            // Role-based file size validation
            var maxFileSize = GetMaxFileSizeForRoles(userRoles);
            if (file.Length > maxFileSize)
            {
                result.Errors.Add($"File size ({file.Length / (1024 * 1024):F2}MB) exceeds maximum allowed size ({maxFileSize / (1024 * 1024):F2}MB) for your role");
                return result;
            }

            // Read file content for validation
            using var stream = file.OpenReadStream();
            var buffer = new byte[Math.Min(8192, file.Length)]; // Read first 8KB for analysis
            await stream.ReadAsync(buffer, 0, buffer.Length);

            // MIME type validation
            if (_options.EnableMimeTypeValidation)
            {
                var mimeValidationResult = ValidateMimeType(file.ContentType, fileExtension);
                if (!mimeValidationResult.IsValid)
                {
                    result.Errors.AddRange(mimeValidationResult.Errors);
                }
                result.DetectedMimeType = mimeValidationResult.DetectedMimeType;
            }

            // File signature validation (magic bytes)
            if (_options.EnableFileSignatureValidation)
            {
                var signatureValidationResult = ValidateFileSignature(buffer, fileExtension);
                if (!signatureValidationResult.IsValid)
                {
                    result.Errors.AddRange(signatureValidationResult.Errors);
                }
            }

            // Content analysis
            if (_options.EnableContentAnalysis)
            {
                var contentAnalysisResult = AnalyzeFileContent(buffer, fileExtension);
                result.Warnings.AddRange(contentAnalysisResult.Warnings);
                if (contentAnalysisResult.HasSuspiciousContent)
                {
                    result.Errors.Add("File contains suspicious content that may pose a security risk");
                }
            }

            // Calculate file hash
            stream.Position = 0;
            result.FileHash = await CalculateFileHashAsync(stream);

            result.IsValid = result.Errors.Count == 0;

            if (result.IsValid)
            {
                _logger.LogInformation("File validation successful: {FileName}, Size: {Size}MB, Type: {Extension}",
                    result.SanitizedFileName, (file.Length / (1024.0 * 1024.0)).ToString("F2"), fileExtension);
            }
            else
            {
                _logger.LogWarning("File validation failed: {FileName}, Errors: {Errors}",
                    file.FileName, string.Join(", ", result.Errors));
            }

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during file validation: {FileName}", file.FileName);
            result.Errors.Add("An error occurred during file validation");
            return result;
        }
    }

    /// <summary>
    /// Validates and sanitizes file name
    /// </summary>
    private FileValidationResult ValidateAndSanitizeFileName(string fileName)
    {
        var result = new FileValidationResult();

        if (string.IsNullOrWhiteSpace(fileName))
        {
            result.Errors.Add("File name cannot be empty");
            return result;
        }

        // Check for path traversal attempts
        if (fileName.Contains("..") || fileName.Contains("/") || fileName.Contains("\\"))
        {
            result.Errors.Add("File name contains invalid path characters");
            return result;
        }

        // Check for suspicious patterns
        foreach (var pattern in _options.SuspiciousPatterns)
        {
            if (fileName.Contains(pattern, StringComparison.OrdinalIgnoreCase))
            {
                result.Errors.Add($"File name contains suspicious pattern: {pattern}");
                return result;
            }
        }

        // Sanitize file name
        var sanitized = fileName;
        
        // Remove control characters
        sanitized = Regex.Replace(sanitized, @"[\x00-\x1F\x7F]", "");
        
        // Remove reserved characters
        var reservedChars = new char[] { '<', '>', ':', '"', '|', '?', '*' };
        foreach (var ch in reservedChars)
        {
            sanitized = sanitized.Replace(ch, '_');
        }

        // Limit length
        if (sanitized.Length > 255)
        {
            var extension = Path.GetExtension(sanitized);
            var nameWithoutExt = Path.GetFileNameWithoutExtension(sanitized);
            sanitized = nameWithoutExt.Substring(0, Math.Min(nameWithoutExt.Length, 250 - extension.Length)) + extension;
        }

        result.SanitizedFileName = sanitized;
        result.IsValid = true;
        return result;
    }

    /// <summary>
    /// Validates MIME type against file extension
    /// </summary>
    private FileValidationResult ValidateMimeType(string? providedMimeType, string fileExtension)
    {
        var result = new FileValidationResult();

        if (!FileSignatures.TryGetValue(fileExtension, out var expectedSignature))
        {
            result.Errors.Add($"Unknown file type: {fileExtension}");
            return result;
        }

        result.DetectedMimeType = expectedSignature.MimeType;

        if (!string.IsNullOrEmpty(providedMimeType))
        {
            if (!providedMimeType.Equals(expectedSignature.MimeType, StringComparison.OrdinalIgnoreCase))
            {
                result.Errors.Add($"MIME type mismatch: expected {expectedSignature.MimeType}, got {providedMimeType}");
                return result;
            }
        }

        result.IsValid = true;
        return result;
    }

    /// <summary>
    /// Validates file signature (magic bytes)
    /// </summary>
    private FileValidationResult ValidateFileSignature(byte[] fileContent, string fileExtension)
    {
        var result = new FileValidationResult();

        if (!FileSignatures.TryGetValue(fileExtension, out var expectedSignature))
        {
            result.Errors.Add($"No signature validation available for: {fileExtension}");
            return result;
        }

        bool signatureMatches = false;
        foreach (var signature in expectedSignature.Signatures)
        {
            if (fileContent.Length >= signature.Length)
            {
                bool matches = true;
                for (int i = 0; i < signature.Length; i++)
                {
                    if (fileContent[i] != signature[i])
                    {
                        matches = false;
                        break;
                    }
                }
                if (matches)
                {
                    signatureMatches = true;
                    break;
                }
            }
        }

        if (!signatureMatches)
        {
            result.Errors.Add($"File signature does not match expected type: {fileExtension}");
            return result;
        }

        result.IsValid = true;
        return result;
    }

    /// <summary>
    /// Analyzes file content for suspicious patterns
    /// </summary>
    private (bool HasSuspiciousContent, List<string> Warnings) AnalyzeFileContent(byte[] content, string fileExtension)
    {
        var warnings = new List<string>();
        bool hasSuspiciousContent = false;

        try
        {
            // Convert to string for pattern analysis (only for first part of file)
            var textContent = Encoding.UTF8.GetString(content, 0, Math.Min(content.Length, 4096));

            foreach (var pattern in _options.SuspiciousPatterns)
            {
                if (textContent.Contains(pattern, StringComparison.OrdinalIgnoreCase))
                {
                    warnings.Add($"Suspicious pattern detected: {pattern}");
                    hasSuspiciousContent = true;
                }
            }

            // Check for embedded executable content
            if (ContainsExecutableSignatures(content))
            {
                warnings.Add("File may contain embedded executable content");
                hasSuspiciousContent = true;
            }
        }
        catch
        {
            // If we can't analyze the content, it's not necessarily suspicious
        }

        return (hasSuspiciousContent, warnings);
    }

    /// <summary>
    /// Checks for embedded executable signatures
    /// </summary>
    private bool ContainsExecutableSignatures(byte[] content)
    {
        var executableSignatures = new byte[][]
        {
            new byte[] { 0x4D, 0x5A }, // MZ (PE/EXE)
            new byte[] { 0x50, 0x4B }, // PK (ZIP/JAR)
            new byte[] { 0x7F, 0x45, 0x4C, 0x46 }, // ELF
        };

        foreach (var signature in executableSignatures)
        {
            for (int i = 0; i <= content.Length - signature.Length; i++)
            {
                bool matches = true;
                for (int j = 0; j < signature.Length; j++)
                {
                    if (content[i + j] != signature[j])
                    {
                        matches = false;
                        break;
                    }
                }
                if (matches) return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Gets maximum file size based on user roles
    /// </summary>
    private long GetMaxFileSizeForRoles(string[] userRoles)
    {
        if (userRoles.Contains("Admin", StringComparer.OrdinalIgnoreCase))
        {
            return _options.AdminMaxFileSizeBytes;
        }
        if (userRoles.Contains("Editor", StringComparer.OrdinalIgnoreCase) || 
            userRoles.Contains("DataOwner", StringComparer.OrdinalIgnoreCase))
        {
            return _options.EditorMaxFileSizeBytes;
        }
        if (userRoles.Contains("ReadOnly", StringComparer.OrdinalIgnoreCase))
        {
            return _options.ReadOnlyMaxFileSizeBytes;
        }

        return _options.DefaultMaxFileSizeBytes;
    }

    /// <summary>
    /// Calculates SHA-256 hash of file content
    /// </summary>
    private async Task<string> CalculateFileHashAsync(Stream stream)
    {
        using var sha256 = System.Security.Cryptography.SHA256.Create();
        var hashBytes = await sha256.ComputeHashAsync(stream);
        return Convert.ToHexString(hashBytes);
    }
}
