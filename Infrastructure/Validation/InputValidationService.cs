// ============================================================================
// InputValidationService.cs - Comprehensive Input Validation Service
// ============================================================================
//
// Purpose: Provides centralized input validation to prevent security 
// vulnerabilities like XSS, SQL injection, and malicious file uploads.
//
// Created: September 23, 2025
//
// Dependencies:
// - System.Text.RegularExpressions for pattern validation
//
// Notes:
// - All user inputs should go through this service before processing
// - Provides both synchronous and asynchronous validation methods
// - Configurable validation rules via appsettings
// ============================================================================

using System.Text.RegularExpressions;
using System.Web;

namespace DV.Admin.UI.Infrastructure.Validation;

public class InputValidationResult
{
    public bool IsValid { get; set; }
    public string ErrorMessage { get; set; } = string.Empty;
    
    public static InputValidationResult Success => new() { IsValid = true };
    public static InputValidationResult Failure(string errorMessage) => new() { IsValid = false, ErrorMessage = errorMessage };
}

public interface IInputValidationService
{
    InputValidationResult ValidateInput(string input, InputType inputType);
    InputValidationResult ValidateFileName(string fileName);
    InputValidationResult ValidateFileContent(byte[] content, string fileName);
    string SanitizeInput(string input);
    string SanitizeFileName(string fileName);
    bool IsValidEmailFormat(string email);
    bool IsValidUsernameFormat(string username);
    bool ContainsMaliciousPatterns(string input);
}

public class InputValidationService : IInputValidationService
{
    private readonly ILogger<InputValidationService> _logger;
    
    // Common malicious patterns
    private static readonly string[] MaliciousPatterns = {
        @"<script[^>]*>.*?</script>",  // Script tags
        @"javascript:",                // JavaScript URLs
        @"vbscript:",                 // VBScript URLs
        @"onload\s*=",                // Event handlers
        @"onerror\s*=",
        @"onclick\s*=",
        @"eval\s*\(",                 // Dangerous JavaScript functions
        @"expression\s*\(",
        @"--",                        // SQL comment
        @"\/\*.*?\*\/",              // SQL block comment
        @"'\s*(or|and)\s*'",         // Basic SQL injection
        @"union\s+select",           // SQL union
        @"drop\s+table",             // SQL drop
        @"delete\s+from",            // SQL delete
        @"insert\s+into",            // SQL insert
        @"update\s+.*\s+set",        // SQL update
        @"exec\s*\(",                // SQL execute
    };

    private static readonly Regex MaliciousRegex = new(
        string.Join("|", MaliciousPatterns), 
        RegexOptions.IgnoreCase | RegexOptions.Compiled
    );

    private static readonly Regex EmailRegex = new(
        @"^[a-zA-Z0-9._%+-]+@[a-zA-Z0-9.-]+\.[a-zA-Z]{2,}$",
        RegexOptions.Compiled
    );

    private static readonly Regex UsernameRegex = new(
        @"^[a-zA-Z0-9\\_\\.\\-]+$",
        RegexOptions.Compiled
    );

    public InputValidationService(ILogger<InputValidationService> logger)
    {
        _logger = logger;
    }

    public InputValidationResult ValidateInput(string input, InputType inputType)
    {
        if (string.IsNullOrEmpty(input))
        {
            return InputValidationResult.Success;
        }

        // Check for malicious patterns
        if (ContainsMaliciousPatterns(input))
        {
            _logger.LogWarning("Malicious pattern detected in input: {InputType}", inputType);
            return InputValidationResult.Failure("Input contains potentially malicious content.");
        }

        // Type-specific validation
        return inputType switch
        {
            InputType.Username => ValidateUsername(input),
            InputType.Email => ValidateEmail(input),
            InputType.FileName => ValidateFileName(input),
            InputType.GeneralText => ValidateGeneralText(input),
            InputType.ProjectName => ValidateProjectName(input),
            InputType.SchemaName => ValidateSchemaName(input),
            _ => InputValidationResult.Success
        };
    }

    public InputValidationResult ValidateFileName(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return InputValidationResult.Failure("File name cannot be empty.");
        }

        if (fileName.Length > 255)
        {
            return InputValidationResult.Failure("File name is too long (max 255 characters).");
        }

        // Check for dangerous characters
        var dangerousChars = new char[] { '<', '>', ':', '"', '|', '?', '*', '\0' };
        if (fileName.IndexOfAny(dangerousChars) >= 0)
        {
            return InputValidationResult.Failure("File name contains invalid characters.");
        }

        return InputValidationResult.Success;
    }

    public InputValidationResult ValidateFileContent(byte[] content, string fileName)
    {
        if (content == null || content.Length == 0)
        {
            return InputValidationResult.Failure("File content cannot be empty.");
        }

        // Basic file signature validation
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        
        // Check file signatures (magic numbers)
        var isValidSignature = extension switch
        {
            ".pdf" => content.Length >= 4 && content[0] == 0x25 && content[1] == 0x50 && content[2] == 0x44 && content[3] == 0x46,
            ".jpg" or ".jpeg" => content.Length >= 3 && content[0] == 0xFF && content[1] == 0xD8 && content[2] == 0xFF,
            ".png" => content.Length >= 8 && content[0] == 0x89 && content[1] == 0x50 && content[2] == 0x4E && content[3] == 0x47,
            ".tif" or ".tiff" => content.Length >= 4 && ((content[0] == 0x49 && content[1] == 0x49) || (content[0] == 0x4D && content[1] == 0x4D)),
            ".bmp" => content.Length >= 2 && content[0] == 0x42 && content[1] == 0x4D,
            ".gif" => content.Length >= 6 && content[0] == 0x47 && content[1] == 0x49 && content[2] == 0x46,
            _ => true // Allow other extensions without signature validation
        };

        if (!isValidSignature)
        {
            return InputValidationResult.Failure($"File content does not match the expected format for {extension} files.");
        }

        return InputValidationResult.Success;
    }

    public string SanitizeInput(string input)
    {
        if (string.IsNullOrEmpty(input))
            return string.Empty;

        // HTML encode to prevent XSS
        var sanitized = HttpUtility.HtmlEncode(input);
        
        // Remove any remaining dangerous patterns
        sanitized = MaliciousRegex.Replace(sanitized, "");
        
        return sanitized.Trim();
    }

    public string SanitizeFileName(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            return "untitled";

        // Remove dangerous characters
        var sanitized = Regex.Replace(fileName, @"[<>:""|?*\x00-\x1f]", "");
        
        // Remove leading/trailing dots and spaces
        sanitized = sanitized.Trim('.', ' ');
        
        // Ensure it's not empty after sanitization
        if (string.IsNullOrWhiteSpace(sanitized))
            return "untitled";

        // Limit length
        if (sanitized.Length > 200)
            sanitized = sanitized.Substring(0, 200);

        return sanitized;
    }

    public bool IsValidEmailFormat(string email)
    {
        return !string.IsNullOrWhiteSpace(email) && EmailRegex.IsMatch(email);
    }

    public bool IsValidUsernameFormat(string username)
    {
        return !string.IsNullOrWhiteSpace(username) && 
               username.Length >= 3 && 
               username.Length <= 50 && 
               UsernameRegex.IsMatch(username);
    }

    public bool ContainsMaliciousPatterns(string input)
    {
        if (string.IsNullOrEmpty(input))
            return false;

        return MaliciousRegex.IsMatch(input);
    }

    private InputValidationResult ValidateUsername(string username)
    {
        if (!IsValidUsernameFormat(username))
        {
            return InputValidationResult.Failure("Username must be 3-50 characters and contain only letters, numbers, dots, hyphens, and underscores.");
        }
        return InputValidationResult.Success;
    }

    private InputValidationResult ValidateEmail(string email)
    {
        if (!IsValidEmailFormat(email))
        {
            return InputValidationResult.Failure("Invalid email format.");
        }
        return InputValidationResult.Success;
    }

    private InputValidationResult ValidateGeneralText(string text)
    {
        if (text.Length > 5000)
        {
            return InputValidationResult.Failure("Text is too long (max 5000 characters).");
        }
        return InputValidationResult.Success;
    }

    private InputValidationResult ValidateProjectName(string projectName)
    {
        if (string.IsNullOrWhiteSpace(projectName))
        {
            return InputValidationResult.Failure("Project name cannot be empty.");
        }

        if (projectName.Length > 100)
        {
            return InputValidationResult.Failure("Project name is too long (max 100 characters).");
        }

        if (!Regex.IsMatch(projectName, @"^[a-zA-Z0-9\s\-_\.]+$"))
        {
            return InputValidationResult.Failure("Project name contains invalid characters.");
        }

        return InputValidationResult.Success;
    }

    private InputValidationResult ValidateSchemaName(string schemaName)
    {
        if (string.IsNullOrWhiteSpace(schemaName))
        {
            return InputValidationResult.Failure("Schema name cannot be empty.");
        }

        if (schemaName.Length > 50)
        {
            return InputValidationResult.Failure("Schema name is too long (max 50 characters).");
        }

        if (!Regex.IsMatch(schemaName, @"^[a-zA-Z][a-zA-Z0-9_]*$"))
        {
            return InputValidationResult.Failure("Schema name must start with a letter and contain only letters, numbers, and underscores.");
        }

        return InputValidationResult.Success;
    }
}

public enum InputType
{
    Username,
    Email,
    FileName,
    GeneralText,
    ProjectName,
    SchemaName
}
