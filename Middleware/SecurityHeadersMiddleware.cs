// ============================================================================
// SecurityHeadersMiddleware.cs - Security Headers Middleware
// ============================================================================
//
// Purpose: Adds comprehensive security headers to HTTP responses to protect
// against common web vulnerabilities and enhance security posture.
//
// Features:
// - HSTS (HTTP Strict Transport Security)
// - CSP (Content Security Policy) 
// - X-Frame-Options (Clickjacking protection)
// - X-Content-Type-Options (MIME sniffing protection)
// - X-XSS-Protection (XSS protection)
// - Referrer-Policy (Information leakage protection)
// - Permissions-Policy (Feature policy)
// - Cache-Control for sensitive pages
//
// ============================================================================

using Microsoft.Extensions.Options;

namespace DV.Admin.UI.Middleware;

/// <summary>
/// Configuration options for security headers
/// </summary>
public class SecurityHeadersOptions
{
    public bool EnableHSTS { get; set; } = true;
    public int HSTSMaxAge { get; set; } = 31536000; // 1 year
    public bool HSTSIncludeSubdomains { get; set; } = true;
    public bool HSTSPreload { get; set; } = false;

    public bool EnableCSP { get; set; } = true;
    public string ContentSecurityPolicy { get; set; } = 
        "default-src 'self'; " +
        "script-src 'self' 'unsafe-inline' 'unsafe-eval' https://cdn.jsdelivr.net; " +
        "style-src 'self' 'unsafe-inline' https://cdn.jsdelivr.net https://fonts.googleapis.com; " +
        "font-src 'self' https://fonts.gstatic.com; " +
        "img-src 'self' data: https:; " +
        "connect-src 'self'; " +
        "frame-ancestors 'none'; " +
        "base-uri 'self'";

    public bool EnableFrameOptions { get; set; } = true;
    public string FrameOptions { get; set; } = "DENY";

    public bool EnableContentTypeOptions { get; set; } = true;
    public bool EnableXSSProtection { get; set; } = true;
    public bool EnableReferrerPolicy { get; set; } = true;
    public string ReferrerPolicy { get; set; } = "strict-origin-when-cross-origin";

    public bool EnablePermissionsPolicy { get; set; } = true;
    public string PermissionsPolicy { get; set; } = 
        "camera=(), microphone=(), geolocation=(), payment=(), usb=()";

    public string[] SensitivePaths { get; set; } = new[]
    {
        "/admin", "/security", "/api", "/management"
    };
}

/// <summary>
/// Middleware that adds security headers to HTTP responses
/// </summary>
public class SecurityHeadersMiddleware
{
    private readonly RequestDelegate _next;
    private readonly SecurityHeadersOptions _options;
    private readonly ILogger<SecurityHeadersMiddleware> _logger;

    public SecurityHeadersMiddleware(
        RequestDelegate next,
        IOptions<SecurityHeadersOptions> options,
        ILogger<SecurityHeadersMiddleware> logger)
    {
        _next = next;
        _options = options.Value;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // Execute next middleware first
        await _next(context);

        // Add security headers to response
        AddSecurityHeaders(context);
    }

    /// <summary>
    /// Adds appropriate security headers to the HTTP response
    /// </summary>
    private void AddSecurityHeaders(HttpContext context)
    {
        var response = context.Response;
        var request = context.Request;

        try
        {
            // HSTS - HTTP Strict Transport Security
            if (_options.EnableHSTS && request.IsHttps)
            {
                var hstsValue = $"max-age={_options.HSTSMaxAge}";
                if (_options.HSTSIncludeSubdomains)
                    hstsValue += "; includeSubDomains";
                if (_options.HSTSPreload)
                    hstsValue += "; preload";

                AddHeaderSafely(response, "Strict-Transport-Security", hstsValue);
            }

            // CSP - Content Security Policy
            if (_options.EnableCSP)
            {
                AddHeaderSafely(response, "Content-Security-Policy", _options.ContentSecurityPolicy);
            }

            // X-Frame-Options - Clickjacking protection
            if (_options.EnableFrameOptions)
            {
                AddHeaderSafely(response, "X-Frame-Options", _options.FrameOptions);
            }

            // X-Content-Type-Options - MIME sniffing protection
            if (_options.EnableContentTypeOptions)
            {
                AddHeaderSafely(response, "X-Content-Type-Options", "nosniff");
            }

            // X-XSS-Protection - XSS protection (legacy but still useful)
            if (_options.EnableXSSProtection)
            {
                AddHeaderSafely(response, "X-XSS-Protection", "1; mode=block");
            }

            // Referrer-Policy - Information leakage protection
            if (_options.EnableReferrerPolicy)
            {
                AddHeaderSafely(response, "Referrer-Policy", _options.ReferrerPolicy);
            }

            // Permissions-Policy - Feature policy
            if (_options.EnablePermissionsPolicy)
            {
                AddHeaderSafely(response, "Permissions-Policy", _options.PermissionsPolicy);
            }

            // Cache-Control for sensitive pages
            if (IsSensitivePath(request.Path))
            {
                AddHeaderSafely(response, "Cache-Control", "no-store, no-cache, must-revalidate, private");
                AddHeaderSafely(response, "Pragma", "no-cache");
                AddHeaderSafely(response, "Expires", "0");
            }

            // Remove server information
            response.Headers.Remove("Server");
            response.Headers.Remove("X-Powered-By");
            response.Headers.Remove("X-AspNet-Version");
            response.Headers.Remove("X-AspNetMvc-Version");

        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error adding security headers");
        }
    }

    /// <summary>
    /// Safely adds a header, avoiding duplicates
    /// </summary>
    private static void AddHeaderSafely(HttpResponse response, string name, string value)
    {
        if (!response.Headers.ContainsKey(name))
        {
            response.Headers[name] = value;
        }
    }

    /// <summary>
    /// Determines if the current path is sensitive and requires additional security
    /// </summary>
    private bool IsSensitivePath(PathString path)
    {
        var pathValue = path.Value?.ToLowerInvariant() ?? "";
        return _options.SensitivePaths.Any(sensitivePath => 
            pathValue.StartsWith(sensitivePath.ToLowerInvariant()));
    }
}

/// <summary>
/// Extension methods for adding security headers middleware
/// </summary>
public static class SecurityHeadersMiddlewareExtensions
{
    public static IApplicationBuilder UseSecurityHeaders(this IApplicationBuilder builder)
    {
        return builder.UseMiddleware<SecurityHeadersMiddleware>();
    }

    public static IApplicationBuilder UseSecurityHeaders(this IApplicationBuilder builder, 
        Action<SecurityHeadersOptions> configureOptions)
    {
        var options = new SecurityHeadersOptions();
        configureOptions(options);
        
        return builder.UseMiddleware<SecurityHeadersMiddleware>(Options.Create(options));
    }
}
