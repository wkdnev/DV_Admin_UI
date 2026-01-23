using System.Diagnostics;

namespace DV.Admin.UI.Infrastructure.Logging;

public static class LoggerExtensions
{
    public static void LogUserAction(this ILogger logger, string username, string action, string? details = null)
    {
        logger.LogInformation("User action: {Username} performed {Action}. Details: {Details}", 
            username, action, details ?? "None");
    }

    public static void LogSecurityEvent(this ILogger logger, string eventType, string username, string details)
    {
        logger.LogWarning("Security event: {EventType} for user {Username}. Details: {Details}", 
            eventType, username, details);
    }

    public static void LogPerformanceMetric(this ILogger logger, string operation, TimeSpan duration, int? recordCount = null)
    {
        logger.LogInformation("Performance: {Operation} took {Duration}ms. Records: {RecordCount}", 
            operation, duration.TotalMilliseconds, recordCount ?? 0);
    }

    public static void LogDatabaseOperation(this ILogger logger, string operation, string table, TimeSpan duration)
    {
        logger.LogDebug("Database: {Operation} on {Table} took {Duration}ms", 
            operation, table, duration.TotalMilliseconds);
    }
}

public class PerformanceTracker : IDisposable
{
    private readonly ILogger _logger;
    private readonly string _operationName;
    private readonly Stopwatch _stopwatch;
    private readonly int? _recordCount;

    public PerformanceTracker(ILogger logger, string operationName, int? recordCount = null)
    {
        _logger = logger;
        _operationName = operationName;
        _recordCount = recordCount;
        _stopwatch = Stopwatch.StartNew();
    }

    public void Dispose()
    {
        _stopwatch.Stop();
        _logger.LogPerformanceMetric(_operationName, _stopwatch.Elapsed, _recordCount);
    }
}

public static class PerformanceLogger
{
    public static PerformanceTracker Track(ILogger logger, string operationName, int? recordCount = null)
    {
        return new PerformanceTracker(logger, operationName, recordCount);
    }
}
