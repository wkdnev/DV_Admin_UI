using System.Security.Cryptography;
using DV.Admin.UI.Data;
using DV.Shared.DTOs;
using DV.Shared.Interfaces;
using DV.Shared.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace DV.Admin.UI.Services;

/// <summary>
/// NIST SP 800-53 Rev 5 compliant credential management.
/// IA-5: PBKDF2-SHA512 with 600k iterations, 128-bit salt.
/// AC-7: Account lockout after 5 consecutive failures, 15-minute window.
/// NIST SP 800-63B: Password complexity — minimum 12 characters, no composition rules
/// (per NIST recommendation against arbitrary complexity rules).
/// </summary>
public class CredentialService : ICredentialService
{
    private readonly SecurityDbContext _context;
    private readonly ILogger<CredentialService> _logger;
    private readonly NotificationApiService _notificationService;
    private readonly AuditService _auditService;

    // NIST AC-7 parameters
    private const int MaxFailedAttempts = 5;
    private const int LockoutMinutes = 15;

    // NIST IA-5 / SP 800-63B parameters
    private const int MinPasswordLength = 12;
    private const int Iterations = 600_000;
    private const int SaltBytes = 16;
    private const int HashBytes = 64;
    private const string Algorithm = "PBKDF2-SHA512";

    public CredentialService(SecurityDbContext context, ILogger<CredentialService> logger, NotificationApiService notificationService, AuditService auditService)
    {
        _context = context;
        _logger = logger;
        _notificationService = notificationService;
        _auditService = auditService;
    }

    public async Task<string?> CreateCredentialAsync(int userId, string password, string createdBy)
    {
        var validationError = ValidatePasswordComplexity(password);
        if (validationError != null) return validationError;

        // Check user exists
        var user = await _context.Users.FindAsync(userId);
        if (user == null) return "User not found.";

        // Remove existing credential if any
        var existing = await _context.UserCredentials.FirstOrDefaultAsync(c => c.UserId == userId);
        if (existing != null)
            _context.UserCredentials.Remove(existing);

        var (hash, salt) = HashPassword(password);

        var credential = new UserCredential
        {
            UserId = userId,
            PasswordHash = hash,
            PasswordSalt = salt,
            Iterations = Iterations,
            HashAlgorithm = Algorithm,
            CreatedAt = DateTime.UtcNow,
            PasswordChangedAt = DateTime.UtcNow,
            CreatedBy = createdBy,
            MustChangePassword = true,
            FailedLoginAttempts = 0,
            IsLockedOut = false
        };

        _context.UserCredentials.Add(credential);
        await _context.SaveChangesAsync();

        _logger.LogInformation("Local credential created for user {UserId} by {Admin}", userId, createdBy);
        return null; // success
    }

    public async Task<ApplicationUser?> ValidateCredentialAsync(string username, string password)
    {
        var user = await _context.Users
            .FirstOrDefaultAsync(u => u.Username.ToLower() == username.ToLower() && u.IsActive);

        if (user == null)
        {
            // Constant-time delay to prevent user enumeration
            await SimulateHashDelay();
            return null;
        }

        var credential = await _context.UserCredentials
            .FirstOrDefaultAsync(c => c.UserId == user.UserId);

        if (credential == null)
        {
            await SimulateHashDelay();
            return null;
        }

        // Check lockout (NIST AC-7)
        if (credential.IsLockedOut)
        {
            if (credential.LockoutEndUtc.HasValue && credential.LockoutEndUtc.Value <= DateTime.UtcNow)
            {
                // Lockout expired — reset
                credential.IsLockedOut = false;
                credential.FailedLoginAttempts = 0;
                credential.LockoutEndUtc = null;
            }
            else
            {
                _logger.LogWarning("Login attempt for locked account: {Username}", username);
                return null;
            }
        }

        // Verify password
        var isValid = VerifyPassword(password, credential.PasswordHash, credential.PasswordSalt, credential.Iterations);

        if (!isValid)
        {
            credential.FailedLoginAttempts++;
            credential.LastFailedLoginAt = DateTime.UtcNow;

            if (credential.FailedLoginAttempts >= MaxFailedAttempts)
            {
                credential.IsLockedOut = true;
                credential.LockoutEndUtc = DateTime.UtcNow.AddMinutes(LockoutMinutes);
                _logger.LogWarning("Account locked after {Attempts} failed attempts: {Username}",
                    credential.FailedLoginAttempts, username);

                // SI-5: Security notification — account lockout
                try
                {
                    await _notificationService.CreateNotificationAsync(new CreateNotificationDto
                    {
                        UserId = user.UserId,
                        Title = "Account Locked",
                        Message = $"Your account was locked after {MaxFailedAttempts} failed login attempts. It will unlock in {LockoutMinutes} minutes.",
                        Category = NotificationCategories.Security,
                        IsImportant = true,
                        SourceSystem = NotificationSources.Admin,
                        CorrelationId = $"lockout-{user.UserId}"
                    });
                }
                catch (Exception notifEx)
                {
                    _logger.LogWarning(notifEx, "Failed to create lockout notification for user {UserId}", user.UserId);
                }
            }

            await _context.SaveChangesAsync();
            return null;
        }

        // Success — reset failure counters
        credential.FailedLoginAttempts = 0;
        credential.LastFailedLoginAt = null;
        credential.IsLockedOut = false;
        credential.LockoutEndUtc = null;
        credential.LastSuccessfulLoginAt = DateTime.UtcNow;

        // Update user's LastLogin
        user.LastLogin = DateTime.UtcNow;

        await _context.SaveChangesAsync();

        _logger.LogInformation("Successful local login: {Username}", username);
        return user;
    }

    public async Task<string?> ChangePasswordAsync(int userId, string currentPassword, string newPassword)
    {
        var credential = await _context.UserCredentials.FirstOrDefaultAsync(c => c.UserId == userId);
        if (credential == null) return "No credential found for this user.";

        // Verify current password
        if (!VerifyPassword(currentPassword, credential.PasswordHash, credential.PasswordSalt, credential.Iterations))
            return "Current password is incorrect.";

        var validationError = ValidatePasswordComplexity(newPassword);
        if (validationError != null) return validationError;

        var (hash, salt) = HashPassword(newPassword);
        credential.PasswordHash = hash;
        credential.PasswordSalt = salt;
        credential.Iterations = Iterations;
        credential.HashAlgorithm = Algorithm;
        credential.PasswordChangedAt = DateTime.UtcNow;
        credential.MustChangePassword = false;

        await _context.SaveChangesAsync();

        _logger.LogInformation("Password changed by user {UserId}", userId);

        // NIST AU-2: Audit password change
        await _auditService.LogCredentialChangeAsync(
            $"UserId:{userId}", userId, AuditActions.ChangePassword, AuditResults.Success,
            details: "User changed their own password");

        // SI-5: Security notification — password changed
        try
        {
            await _notificationService.CreateNotificationAsync(new CreateNotificationDto
            {
                UserId = userId,
                Title = "Password Changed",
                Message = "Your password was changed successfully.",
                Category = NotificationCategories.Security,
                SourceSystem = NotificationSources.Admin,
                CorrelationId = $"pwd-change-{userId}"
            });
        }
        catch (Exception notifEx)
        {
            _logger.LogWarning(notifEx, "Failed to create password change notification for user {UserId}", userId);
        }

        return null;
    }

    public async Task<string?> AdminResetPasswordAsync(int userId, string newPassword, string adminUsername)
    {
        var validationError = ValidatePasswordComplexity(newPassword);
        if (validationError != null) return validationError;

        var credential = await _context.UserCredentials.FirstOrDefaultAsync(c => c.UserId == userId);
        if (credential == null) return "No credential found for this user.";

        var (hash, salt) = HashPassword(newPassword);
        credential.PasswordHash = hash;
        credential.PasswordSalt = salt;
        credential.Iterations = Iterations;
        credential.HashAlgorithm = Algorithm;
        credential.PasswordChangedAt = DateTime.UtcNow;
        credential.MustChangePassword = true;
        credential.CreatedBy = adminUsername;

        // Clear lockout on admin reset
        credential.FailedLoginAttempts = 0;
        credential.IsLockedOut = false;
        credential.LockoutEndUtc = null;

        await _context.SaveChangesAsync();

        _logger.LogInformation("Password reset by admin {Admin} for user {UserId}", adminUsername, userId);

        // NIST AU-2: Audit admin password reset
        await _auditService.LogCredentialChangeAsync(
            adminUsername, null, AuditActions.AdminResetPassword, AuditResults.Success,
            targetUsername: $"UserId:{userId}", targetUserId: userId,
            details: $"Admin {adminUsername} reset password for user {userId}");

        // SI-5: Security notification — admin password reset
        try
        {
            await _notificationService.CreateNotificationAsync(new CreateNotificationDto
            {
                UserId = userId,
                Title = "Password Reset by Admin",
                Message = $"Your password was reset by an administrator. You will be required to change it on next login.",
                Category = NotificationCategories.Security,
                IsImportant = true,
                SourceSystem = NotificationSources.Admin,
                CorrelationId = $"admin-reset-{userId}"
            });
        }
        catch (Exception notifEx)
        {
            _logger.LogWarning(notifEx, "Failed to create admin reset notification for user {UserId}", userId);
        }

        return null;
    }

    public async Task UnlockAccountAsync(int userId)
    {
        var credential = await _context.UserCredentials.FirstOrDefaultAsync(c => c.UserId == userId);
        if (credential == null) return;

        credential.IsLockedOut = false;
        credential.FailedLoginAttempts = 0;
        credential.LockoutEndUtc = null;
        await _context.SaveChangesAsync();

        _logger.LogInformation("Account unlocked for user {UserId}", userId);

        // SI-5: Security notification — account unlocked
        try
        {
            await _notificationService.CreateNotificationAsync(new CreateNotificationDto
            {
                UserId = userId,
                Title = "Account Unlocked",
                Message = "Your account has been unlocked by an administrator.",
                Category = NotificationCategories.Security,
                SourceSystem = NotificationSources.Admin,
                CorrelationId = $"unlock-{userId}"
            });
        }
        catch (Exception notifEx)
        {
            _logger.LogWarning(notifEx, "Failed to create unlock notification for user {UserId}", userId);
        }
    }

    public async Task<bool> HasCredentialAsync(int userId)
    {
        return await _context.UserCredentials.AnyAsync(c => c.UserId == userId);
    }

    public async Task RemoveCredentialAsync(int userId)
    {
        var credential = await _context.UserCredentials.FirstOrDefaultAsync(c => c.UserId == userId);
        if (credential != null)
        {
            _context.UserCredentials.Remove(credential);
            await _context.SaveChangesAsync();
            _logger.LogInformation("Local credential removed for user {UserId}", userId);
        }
    }

    public async Task<UserCredential?> GetCredentialInfoAsync(int userId)
    {
        return await _context.UserCredentials
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.UserId == userId);
    }

    /// <summary>
    /// NIST SP 800-63B password validation.
    /// Requires minimum 12 characters. No arbitrary composition rules.
    /// Checks against common weak passwords.
    /// </summary>
    public string? ValidatePasswordComplexity(string password)
    {
        if (string.IsNullOrWhiteSpace(password))
            return "Password is required.";

        if (password.Length < MinPasswordLength)
            return $"Password must be at least {MinPasswordLength} characters.";

        if (password.Length > 128)
            return "Password must not exceed 128 characters.";

        // NIST SP 800-63B: Check against common weak passwords
        var lower = password.ToLowerInvariant();
        var commonPasswords = new HashSet<string>
        {
            "password1234", "123456789012", "qwertyuiopas", "letmeinplease",
            "adminadminadmin", "changemechangeme", "welcome12345", "password1234!",
            "iloveyou1234", "trustno1trust", "dragon123456", "master123456",
            "abc123456789", "passw0rd1234", "shadow123456"
        };

        if (commonPasswords.Contains(lower))
            return "This password is too common. Please choose a stronger password.";

        return null;
    }

    // ── Private helpers ──

    private static (string Hash, string Salt) HashPassword(string password)
    {
        var saltBytes = RandomNumberGenerator.GetBytes(SaltBytes);
        var hashBytes = Rfc2898DeriveBytes.Pbkdf2(
            password,
            saltBytes,
            Iterations,
            HashAlgorithmName.SHA512,
            HashBytes);

        return (Convert.ToBase64String(hashBytes), Convert.ToBase64String(saltBytes));
    }

    private static bool VerifyPassword(string password, string storedHash, string storedSalt, int iterations)
    {
        var saltBytes = Convert.FromBase64String(storedSalt);
        var expectedHash = Rfc2898DeriveBytes.Pbkdf2(
            password,
            saltBytes,
            iterations,
            HashAlgorithmName.SHA512,
            HashBytes);

        return CryptographicOperations.FixedTimeEquals(
            expectedHash,
            Convert.FromBase64String(storedHash));
    }

    /// <summary>
    /// Simulates hash computation time to prevent user-enumeration timing attacks.
    /// </summary>
    private static async Task SimulateHashDelay()
    {
        // Perform a throwaway hash to match timing of a real verification
        var dummySalt = RandomNumberGenerator.GetBytes(SaltBytes);
        Rfc2898DeriveBytes.Pbkdf2("dummy", dummySalt, Iterations, HashAlgorithmName.SHA512, HashBytes);
        await Task.CompletedTask;
    }
}
