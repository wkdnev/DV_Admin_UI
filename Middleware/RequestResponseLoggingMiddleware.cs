// ============================================================================
// RequestResponseLoggingMiddleware.cs - HTTP Request/Response Logging
// ============================================================================
//
// Purpose: Provides comprehensive logging of HTTP requests and responses
// for security monitoring, debugging, and audit trails.
//
// Features:
// - Request logging (method, path, headers, body size)
// - Response logging (status, headers, body size, timing)
// - Performance metrics (request duration)
// - Security monitoring (suspicious requests)
// - Configurable log levels and filtering
// - PII-sensitive data protection
//
// ============================================================================

using System.Diagnostics;
using System.Text;
using Microsoft.Extensions.Options;

namespace DV.Admin.UI.Middleware;

/// <summary>
/// Configuration options for request/response logging
/// </summary>
public class RequestResponseLoggingOptions
{
    public bool EnableRequestLogging { get; set; } = true;
    public bool EnableResponseLogging { get; set; } = true;
    public bool LogHeaders { get; set; } = true;
    public bool LogRequestBody { get; set; } = false; // Disabled by default for security
    public bool LogResponseBody { get; set; } = false; // Disabled by default for performance
    public int MaxBodyLength { get; set; } = 1024; // Maximum body length to log
    
    public LogLevel RequestLogLevel { get; set; } = LogLevel.Information;
    public LogLevel ResponseLogLevel { get; set; } = LogLevel.Information;
    public LogLevel PerformanceLogLevel { get; set; } = LogLevel.Information;
    public LogLevel SecurityLogLevel { get; set; } = LogLevel.Warning;

    public string[] ExcludedPaths { get; set; } = new[]
    {
        "/health", "/heartbeat", "/_blazor", "/css", "/js", "/images", "/favicon.ico"
    };

    public string[] SensitiveHeaders { get; set; } = new[]
    {
        "authorization", "cookie", "x-api-key", "x-auth-token", "set-cookie"
    };

    public string[] SuspiciousPatterns { get; set; } = new[]
    {
        "script", "javascript:", "vbscript:", "onload", "onerror", "eval(", "alert(", 
        "../", "..\\", "/etc/passwd", "cmd.exe", "powershell"
    };

    public int SlowRequestThresholdMs { get; set; } = 5000; // 5 seconds
}

/// <summary>
/// Middleware that logs HTTP requests and responses for monitoring and debugging
/// </summary>
public class RequestResponseLoggingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly RequestResponseLoggingOptions _options;
    private readonly ILogger<RequestResponseLoggingMiddleware> _logger;

    public RequestResponseLoggingMiddleware(
        RequestDelegate next,
        IOptions<RequestResponseLoggingOptions> options,
        ILogger<RequestResponseLoggingMiddleware> logger)
    {
        _next = next;
        _options = options.Value;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // Skip logging for excluded paths
        if (ShouldSkipLogging(context.Request.Path))
        {
            await _next(context);
            return;
        }

        var stopwatch = Stopwatch.StartNew();
        var requestId = Guid.NewGuid().ToString("N")[..8];

        try
        {
            // Log request
            if (_options.EnableRequestLogging)
            {
                await LogRequestAsync(context, requestId);
            }

            // Check for suspicious patterns
            CheckForSuspiciousActivity(context, requestId);

            // Capture original response body stream
            var originalBodyStream = context.Response.Body;
            using var responseBodyStream = new MemoryStream();
            context.Response.Body = responseBodyStream;

            // Execute next middleware
            await _next(context);

            // Log response
            if (_options.EnableResponseLogging)
            {
                await LogResponseAsync(context, requestId, stopwatch.ElapsedMilliseconds);
            }

            // Log performance if slow
            if (stopwatch.ElapsedMilliseconds > _options.SlowRequestThresholdMs)
            {
                _logger.Log(_options.PerformanceLogLevel,
                    "Slow request detected - RequestId: {RequestId}, Duration: {Duration}ms, Path: {Path}",
                    requestId, stopwatch.ElapsedMilliseconds, context.Request.Path);
            }

            // Copy response body back to original stream
            responseBodyStream.Seek(0, SeekOrigin.Begin);
            await responseBodyStream.CopyToAsync(originalBodyStream);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in request/response logging middleware - RequestId: {RequestId}", requestId);
            throw;
        }
        finally
        {
            stopwatch.Stop();
        }
    }

    /// <summary>
    /// Logs incoming HTTP request details
    /// </summary>
    private async Task LogRequestAsync(HttpContext context, string requestId)
    {
        var request = context.Request;
        var logBuilder = new StringBuilder();

        logBuilder.AppendLine($"REQUEST - RequestId: {requestId}");
        logBuilder.AppendLine($"Method: {request.Method}");
        logBuilder.AppendLine($"Path: {request.Path}");
        logBuilder.AppendLine($"QueryString: {request.QueryString}");
        logBuilder.AppendLine($"ContentType: {request.ContentType}");
        logBuilder.AppendLine($"ContentLength: {request.ContentLength}");
        logBuilder.AppendLine($"RemoteIP: {GetClientIpAddress(context)}");
        logBuilder.AppendLine($"UserAgent: {request.Headers.UserAgent}");

        // Log headers (excluding sensitive ones)
        if (_options.LogHeaders)
        {
            logBuilder.AppendLine("Headers:");
            foreach (var header in request.Headers)
            {
                if (!_options.SensitiveHeaders.Contains(header.Key.ToLowerInvariant()))
                {
                    logBuilder.AppendLine($"  {header.Key}: {header.Value}");
                }
                else
                {
                    logBuilder.AppendLine($"  {header.Key}: [REDACTED]");
                }
            }
        }

        // Log request body if enabled and safe
        if (_options.LogRequestBody && request.ContentLength > 0 && IsTextContent(request.ContentType))
        {
            request.EnableBuffering();
            var body = await ReadBodyAsync(request.Body, _options.MaxBodyLength);
            if (!string.IsNullOrEmpty(body))
            {
                logBuilder.AppendLine($"Body: {body}");
            }
            request.Body.Position = 0;
        }

        _logger.Log(_options.RequestLogLevel, logBuilder.ToString());
    }

    /// <summary>
    /// Logs outgoing HTTP response details
    /// </summary>
    private async Task LogResponseAsync(HttpContext context, string requestId, long durationMs)
    {
        var response = context.Response;
        var logBuilder = new StringBuilder();

        logBuilder.AppendLine($"RESPONSE - RequestId: {requestId}");
        logBuilder.AppendLine($"StatusCode: {response.StatusCode}");
        logBuilder.AppendLine($"ContentType: {response.ContentType}");
        logBuilder.AppendLine($"ContentLength: {response.ContentLength}");
        logBuilder.AppendLine($"Duration: {durationMs}ms");

        // Log response headers (excluding sensitive ones)
        if (_options.LogHeaders)
        {
            logBuilder.AppendLine("Headers:");
            foreach (var header in response.Headers)
            {
                if (!_options.SensitiveHeaders.Contains(header.Key.ToLowerInvariant()))
                {
                    logBuilder.AppendLine($"  {header.Key}: {header.Value}");
                }
                else
                {
                    logBuilder.AppendLine($"  {header.Key}: [REDACTED]");
                }
            }
        }

        // Log response body if enabled and safe
        if (_options.LogResponseBody && IsTextContent(response.ContentType))
        {
            var body = await ReadBodyAsync(context.Response.Body, _options.MaxBodyLength);
            if (!string.IsNullOrEmpty(body))
            {
                logBuilder.AppendLine($"Body: {body}");
            }
        }

        _logger.Log(_options.ResponseLogLevel, logBuilder.ToString());
    }

    /// <summary>
    /// Checks for suspicious activity in the request
    /// </summary>
    private void CheckForSuspiciousActivity(HttpContext context, string requestId)
    {
        var request = context.Request;
        var suspiciousIndicators = new List<string>();

        // Check URL for suspicious patterns
        var fullUrl = $"{request.Path}{request.QueryString}".ToLowerInvariant();
        foreach (var pattern in _options.SuspiciousPatterns)
        {
            if (fullUrl.Contains(pattern.ToLowerInvariant()))
            {
                suspiciousIndicators.Add($"URL contains: {pattern}");
            }
        }

        // Check headers for suspicious content
        foreach (var header in request.Headers)
        {
            var headerValue = header.Value.ToString().ToLowerInvariant();
            foreach (var pattern in _options.SuspiciousPatterns)
            {
                if (headerValue.Contains(pattern.ToLowerInvariant()))
                {
                    suspiciousIndicators.Add($"Header {header.Key} contains: {pattern}");
                }
            }
        }

        // Log suspicious activity
        if (suspiciousIndicators.Count > 0)
        {
            _logger.Log(_options.SecurityLogLevel,
                "Suspicious activity detected - RequestId: {RequestId}, IP: {IP}, Path: {Path}, Indicators: {Indicators}",
                requestId, GetClientIpAddress(context), request.Path, string.Join(", ", suspiciousIndicators));
        }
    }

    /// <summary>
    /// Determines if logging should be skipped for this path
    /// </summary>
    private bool ShouldSkipLogging(PathString path)
    {
        var pathValue = path.Value?.ToLowerInvariant() ?? "";
        return _options.ExcludedPaths.Any(excludedPath => 
            pathValue.StartsWith(excludedPath.ToLowerInvariant()));
    }

    /// <summary>
    /// Gets the client IP address
    /// </summary>
    private string GetClientIpAddress(HttpContext context)
    {
        return context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    }

    /// <summary>
    /// Determines if content type is text-based and safe to log
    /// </summary>
    private bool IsTextContent(string? contentType)
    {
        if (string.IsNullOrEmpty(contentType))
            return false;

        var textTypes = new[] { "text/", "application/json", "application/xml", "application/x-www-form-urlencoded" };
        return textTypes.Any(type => contentType.StartsWith(type, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Reads body content up to the specified maximum length
    /// </summary>
    private async Task<string> ReadBodyAsync(Stream body, int maxLength)
    {
        try
        {
            body.Seek(0, SeekOrigin.Begin);
            var buffer = new byte[Math.Min(maxLength, (int)body.Length)];
            var bytesRead = await body.ReadAsync(buffer, 0, buffer.Length);
            return Encoding.UTF8.GetString(buffer, 0, bytesRead);
        }
        catch
        {
            return "[Error reading body]";
        }
    }
}

/// <summary>
/// Extension methods for adding request/response logging middleware
/// </summary>
public static class RequestResponseLoggingMiddlewareExtensions
{
    public static IApplicationBuilder UseRequestResponseLogging(this IApplicationBuilder builder)
    {
        return builder.UseMiddleware<RequestResponseLoggingMiddleware>();
    }

    public static IApplicationBuilder UseRequestResponseLogging(this IApplicationBuilder builder,
        Action<RequestResponseLoggingOptions> configureOptions)
    {
        var options = new RequestResponseLoggingOptions();
        configureOptions(options);

        return builder.UseMiddleware<RequestResponseLoggingMiddleware>(Options.Create(options));
    }
}
