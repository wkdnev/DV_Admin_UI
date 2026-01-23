using System.ComponentModel.DataAnnotations;
using System.Text.RegularExpressions;
using DV.Shared.Models;
using DV.Shared.Security;

namespace DV.Admin.UI.Infrastructure.Validation;

public interface IValidationService
{
    ValidationResult Validate<T>(T model) where T : class;
    bool IsValidUsername(string username);
    bool IsValidEmail(string email);
    string SanitizeInput(string input);
    bool IsValidProjectCode(string projectCode);
}

public class ValidationService : IValidationService
{
    private readonly ILogger<ValidationService> _logger;
    
    // Username pattern: allows domain\username or just username
    private static readonly Regex UsernameRegex = new(@"^([A-Za-z0-9_.-]+\\)?[A-Za-z0-9_.-]+$", RegexOptions.Compiled);
    
    // Email pattern
    private static readonly Regex EmailRegex = new(@"^[^@\s]+@[^@\s]+\.[^@\s]+$", RegexOptions.Compiled);
    
    // Project code pattern: alphanumeric with underscores/hyphens
    private static readonly Regex ProjectCodeRegex = new(@"^[A-Za-z0-9_-]+$", RegexOptions.Compiled);

    public ValidationService(ILogger<ValidationService> logger)
    {
        _logger = logger;
    }

    public ValidationResult Validate<T>(T model) where T : class
    {
        var validationResults = new List<System.ComponentModel.DataAnnotations.ValidationResult>();
        var validationContext = new ValidationContext(model);

        var isValid = Validator.TryValidateObject(model, validationContext, validationResults, true);

        // Additional custom validations based on type
        if (model is ApplicationUser user)
        {
            ValidateUserAsync(user, validationResults);
        }
        else if (model is Project project)
        {
            ValidateProjectAsync(project, validationResults);
        }

        return new ValidationResult
        {
            IsValid = isValid && validationResults.Count == 0,
            Errors = validationResults.Select(vr => vr.ErrorMessage ?? "Unknown error").ToList()
        };
    }

    public bool IsValidUsername(string username)
    {
        if (string.IsNullOrWhiteSpace(username)) return false;
        if (username.Length > 255) return false;
        return UsernameRegex.IsMatch(username);
    }

    public bool IsValidEmail(string email)
    {
        if (string.IsNullOrWhiteSpace(email)) return false;
        if (email.Length > 320) return false;
        return EmailRegex.IsMatch(email);
    }

    public string SanitizeInput(string input)
    {
        if (string.IsNullOrEmpty(input)) return string.Empty;

        // Remove potentially dangerous characters
        var sanitized = input
            .Replace("<", "&lt;")
            .Replace(">", "&gt;")
            .Replace("\"", "&quot;")
            .Replace("'", "&#x27;")
            .Replace("/", "&#x2F;");

        return sanitized.Trim();
    }

    public bool IsValidProjectCode(string projectCode)
    {
        if (string.IsNullOrWhiteSpace(projectCode)) return false;
        if (projectCode.Length > 50) return false;
        return ProjectCodeRegex.IsMatch(projectCode);
    }

    private void ValidateUserAsync(ApplicationUser user, List<System.ComponentModel.DataAnnotations.ValidationResult> results)
    {
        if (!IsValidUsername(user.Username))
        {
            results.Add(new System.ComponentModel.DataAnnotations.ValidationResult(
                "Username contains invalid characters or format",
                new[] { nameof(user.Username) }));
        }

        if (!string.IsNullOrEmpty(user.Email) && !IsValidEmail(user.Email))
        {
            results.Add(new System.ComponentModel.DataAnnotations.ValidationResult(
                "Email format is invalid",
                new[] { nameof(user.Email) }));
        }
    }

    private void ValidateProjectAsync(Project project, List<System.ComponentModel.DataAnnotations.ValidationResult> results)
    {
        if (!IsValidProjectCode(project.ProjectCode))
        {
            results.Add(new System.ComponentModel.DataAnnotations.ValidationResult(
                "Project code contains invalid characters",
                new[] { nameof(project.ProjectCode) }));
        }

        // Validate schema name follows SQL naming conventions
        if (!IsValidProjectCode(project.SchemaName))
        {
            results.Add(new System.ComponentModel.DataAnnotations.ValidationResult(
                "Schema name contains invalid characters",
                new[] { nameof(project.SchemaName) }));
        }
    }
}

public class ValidationResult
{
    public bool IsValid { get; set; }
    public List<string> Errors { get; set; } = new();
}
