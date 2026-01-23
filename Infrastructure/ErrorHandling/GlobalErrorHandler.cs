using Microsoft.AspNetCore.Diagnostics;
using System.Text.Json;
using System.Diagnostics;

namespace DV.Admin.UI.Infrastructure.ErrorHandling;

public class GlobalErrorHandler : IExceptionHandler
{
    private readonly ILogger<GlobalErrorHandler> _logger;

    public GlobalErrorHandler(ILogger<GlobalErrorHandler> logger)
    {
        _logger = logger;
    }

    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        _logger.LogError(exception, "An unhandled exception occurred");

        var response = new
        {
            error = "An error occurred while processing your request",
            traceId = Activity.Current?.Id ?? httpContext.TraceIdentifier,
            timestamp = DateTime.UtcNow
        };

        httpContext.Response.StatusCode = 500;
        httpContext.Response.ContentType = "application/json";

        await httpContext.Response.WriteAsync(
            JsonSerializer.Serialize(response),
            cancellationToken);

        return true;
    }
}

public static class GlobalErrorHandlerExtensions
{
    public static IServiceCollection AddGlobalErrorHandling(this IServiceCollection services)
    {
        services.AddExceptionHandler<GlobalErrorHandler>();
        services.AddProblemDetails();
        return services;
    }
}
