using System.ComponentModel.DataAnnotations;

namespace DV.Admin.UI.Infrastructure.Configuration;

public class AppSettings
{
    public DatabaseSettings Database { get; set; } = new();
    public CacheSettings Cache { get; set; } = new();
    public SecuritySettings Security { get; set; } = new();
    public LoggingSettings Logging { get; set; } = new();
    public PerformanceSettings Performance { get; set; } = new();
    public DocumentUploadSettings DocumentUpload { get; set; } = new();
}

public class DatabaseSettings
{
    [Range(1, 300)]
    public int CommandTimeout { get; set; } = 30;
    
    [Range(1, 10)]
    public int MaxRetryCount { get; set; } = 5;
    
    public bool EnableSensitiveDataLogging { get; set; } = false;
}

public class CacheSettings
{
    public int DefaultExpirationMinutes { get; set; } = 30;
    public int UserCacheExpirationMinutes { get; set; } = 15;
    public int PermissionCacheExpirationMinutes { get; set; } = 10;
    public long MaxMemorySizeMB { get; set; } = 100;
}

public class SecuritySettings
{
    public int SessionTimeoutHours { get; set; } = 2;
    public bool RequireHttps { get; set; } = true;
    public bool EnableAuditLogging { get; set; } = true;
    public int MaxLoginAttempts { get; set; } = 5;
    public int LockoutDurationMinutes { get; set; } = 15;
}

public class LoggingSettings
{
    public bool EnablePerformanceLogging { get; set; } = true;
    public bool EnableSqlLogging { get; set; } = false;
    public string LogLevel { get; set; } = "Information";
}

public class PerformanceSettings
{
    public int DefaultPageSize { get; set; } = 50;
    public int MaxPageSize { get; set; } = 1000;
    public int SlowQueryThresholdMs { get; set; } = 1000;
}

public class DocumentUploadSettings
{
    [Range(1024, long.MaxValue, ErrorMessage = "Max file size must be at least 1KB")]
    public long MaxFileSizeBytes { get; set; } = 52428800; // 50MB
    
    [Required]
    [MinLength(1, ErrorMessage = "At least one file type must be allowed")]
    public List<string> AllowedFileTypes { get; set; } = new()
    {
        ".pdf", ".tiff", ".tif", ".jpg", ".jpeg", ".png", ".bmp", ".gif"
    };
    
    public bool EnableVirusScanning { get; set; } = false;
    public bool ValidateFileContent { get; set; } = true;
    
    [Required]
    public string UploadPath { get; set; } = "uploads";
    
    [Range(1, 20)]
    public int MaxConcurrentUploads { get; set; } = 5;
}
